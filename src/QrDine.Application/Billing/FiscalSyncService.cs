using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Restaurants;

namespace QrDine.Application.Billing;

/// <summary>
/// Orchestrates FBR filing for settled invoices: decides whether a restaurant files
/// at all (SaaS gate + owner switch), resolves the POS context (branch + operator),
/// enforces idempotency (never files the same sale twice), records errors for the
/// pending view, and drives manual retries. It never throws on a filing failure at
/// settle time — the sale always completes; failures are captured for later retry.
/// </summary>
public class FiscalSyncService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IFiscalService _fiscal;

    public FiscalSyncService(IAppDbContext db, ICurrentTenant tenant, IFiscalService fiscal)
    {
        _db = db; _tenant = tenant; _fiscal = fiscal;
    }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");

    /// <summary>
    /// Called during settle (after the invoice is persisted). Mutates the invoice's
    /// fiscal fields; the caller saves. Non-blocking: a failure only marks the invoice
    /// Pending/Failed so it shows in the owner's pending list for a manual retry.
    /// </summary>
    public async Task SyncOnSettle(Invoice invoice, Restaurant r, string? operatorUsername, CancellationToken ct = default)
    {
        if (!r.FeatureFbr || !r.FbrEnabled) { invoice.MarkFiscalNotRequired(); return; }
        try
        {
            await Attempt(invoice, r, operatorUsername, ct);
        }
        catch (Exception ex)
        {
            // The sale must ALWAYS complete and the receipt must ALWAYS print. Any
            // unexpected fiscal error just leaves the invoice Pending for a retry.
            invoice.ApplyFiscalFailure("FBR", FiscalStatus.Pending, $"Filing error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Manually re-file a Pending/Failed invoice. Guards: FBR must be enabled, the
    /// invoice must exist and not already be registered (the no-duplicate trigger).
    /// </summary>
    public async Task<FiscalInvoiceDto> Retry(int invoiceId, string? operatorUsername, bool isOwner = false, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        if (!r.FeatureFbr) throw new DomainException("FBR integration isn't enabled for your account.");
        if (!r.FbrEnabled) throw new DomainException("FBR sync is turned off in Settings.");

        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new DomainException("Invoice not found.");
        if (inv.IsFiscalised)
            throw new DomainException($"Invoice already filed with FBR ({inv.FiscalNumber}). It will not be sent again.");

        // Local mode uses a shared IMS component, so re-filing is restricted to the
        // cashier who created the invoice (an Owner can always override).
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == inv.BranchId, ct);
        if (branch?.FbrMode == FbrMode.Local && !isOwner
            && !string.IsNullOrEmpty(inv.CreatedByUsername)
            && !string.Equals(inv.CreatedByUsername, operatorUsername, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"In local mode only the cashier who created this invoice ({inv.CreatedByUsername}) can re-sync it.");

        await Attempt(inv, r, operatorUsername, ct);
        await _db.SaveChangesAsync(ct);
        return Map(inv, r.Currency);
    }

    // The single filing path. The idempotency guard here is the ultimate backstop:
    // even if two callers race, a registered invoice is never posted again.
    private async Task Attempt(Invoice inv, Restaurant r, string? operatorUsername, CancellationToken ct)
    {
        if (inv.IsFiscalised) return;

        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == inv.BranchId, ct);
        StaffMember? op = string.IsNullOrWhiteSpace(operatorUsername) ? null
            : await _db.Staff.FirstOrDefaultAsync(s => s.Username == operatorUsername, ct);

        var ctx = BuildContext(r, branch, op);
        var res = await _fiscal.Register(inv, ctx, ct);
        if (res.Status == FiscalStatus.Registered)
            inv.ApplyFiscalSuccess(res.Provider, res.Number, res.QrPayload, ctx.PosId);
        else
            inv.ApplyFiscalFailure(res.Provider, res.Status, res.Error ?? "Unknown fiscal error.", ctx.PosId);
    }

    // POSID/token: the operator's own creds win, else the branch defaults.
    private static FiscalContext BuildContext(Restaurant r, Branch? b, StaffMember? op) => new(
        string.IsNullOrWhiteSpace(r.BusinessName) ? r.Name : r.BusinessName, r.Ntn, r.Strn,
        b?.FbrMode ?? FbrMode.Local, b?.FbrSandbox ?? true, b?.FbrEndpointOverride,
        op?.FbrPosId ?? b?.FbrPosId, op?.FbrToken ?? b?.FbrToken);

    /// <summary>List invoices for the owner's fiscal view — synced, or pending/failed.</summary>
    public async Task<IReadOnlyList<FiscalInvoiceDto>> List(bool synced, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        var q = _db.Invoices.AsNoTracking();
        q = synced
            ? q.Where(i => i.FiscalStatus == FiscalStatus.Registered)
            : q.Where(i => i.FiscalStatus == FiscalStatus.Pending || i.FiscalStatus == FiscalStatus.Failed);
        var list = await q.OrderByDescending(i => i.IssuedAt).Take(200).ToListAsync(ct);
        return list.Select(i => Map(i, r.Currency)).ToList();
    }

    private static FiscalInvoiceDto Map(Invoice i, string currency) => new(
        i.Id, i.FormattedNumber, i.Usin, i.TableName, i.IssuedAt, currency, i.Total, i.PaymentMethod,
        i.FiscalStatus.ToString(), i.FiscalNumber, i.FiscalError, i.FiscalAttempts, i.FiscalLastAttemptAt, i.SyncedByPosId);
}
