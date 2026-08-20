using Microsoft.EntityFrameworkCore;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Restaurants;

namespace QrDine.Application.Platform;

public record PlatformLoginRequest(string Username, string Password);
public record SetPlanRequest(int PlanId);
public record SetStatusRequest(string Status);

public record PlanDto(int Id, string Name, string Description, decimal PricePkr, decimal SetupFeePkr, int SortOrder, bool IsActive,
    bool Ordering, bool Kitchen, bool Waiter, bool Billing, bool Inventory, bool Reports);
public record PlanInput(string Name, string Description, decimal PricePkr, decimal SetupFeePkr, int SortOrder, bool IsActive,
    bool Ordering, bool Kitchen, bool Waiter, bool Billing, bool Inventory, bool Reports);

public record SetFbrRequest(bool Enabled);

// Operator records a payment collected offline. Kind = "Setup" | "Monthly".
public record RecordPaymentRequest(string Kind, int Months, decimal AmountPkr, string? Note);
public record SubscriptionPaymentDto(int Id, string Kind, int Months, decimal AmountPkr,
    DateTime PaidAt, DateTime? NewPaidThrough, string Note, string RecordedBy);

public record PlatformStatsDto(int Restaurants, int Active, int Suspended, decimal TotalSales, int TotalBills);
public record PlatformRestaurantDto(int Id, string Slug, string Name, string Plan, string Status,
    DateTime CreatedAt, int Staff, int Tables, decimal Sales, int Bills, bool FeatureFbr,
    decimal MonthlyPkr, decimal SetupFeePkr, bool SetupFeePaid, DateTime? PaidThrough, DateTime? LastPaymentAt);

/// <summary>
/// The SaaS operator's control plane. It works ACROSS all tenants, so every query
/// ignores the per-tenant filter. Guarded by a platform token, never a staff one.
/// </summary>
public class PlatformService
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;

    public PlatformService(IAppDbContext db, ITokenService tokens, IPasswordHasher hasher)
    {
        _db = db; _tokens = tokens; _hasher = hasher;
    }

    public async Task<AuthResponse> Login(PlatformLoginRequest req, CancellationToken ct = default)
    {
        var admin = await _db.PlatformAdmins.FirstOrDefaultAsync(a => a.Username == req.Username.Trim().ToLowerInvariant(), ct);
        if (admin is null || !_hasher.Verify(req.Password, admin.PasswordHash))
            throw new DomainException("Invalid username or password.");
        return new AuthResponse(_tokens.CreatePlatformToken(admin), admin.DisplayName, "Platform", Array.Empty<string>());
    }

    public async Task<PlatformStatsDto> Stats(CancellationToken ct = default)
    {
        var restaurants = await _db.Restaurants.AsNoTracking().ToListAsync(ct);
        var invoices = await _db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Select(i => new { i.Total }).ToListAsync(ct);
        return new PlatformStatsDto(
            restaurants.Count,
            restaurants.Count(r => r.Status == RestaurantStatus.Active),
            restaurants.Count(r => r.Status == RestaurantStatus.Suspended),
            invoices.Sum(i => i.Total), invoices.Count);
    }

    public async Task<IReadOnlyList<PlatformRestaurantDto>> ListRestaurants(CancellationToken ct = default)
    {
        var restaurants = await _db.Restaurants.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var plans = (await _db.Plans.AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.Id);

        var invAgg = (await _db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .Select(i => new { i.RestaurantId, i.Total }).ToListAsync(ct))
            .GroupBy(i => i.RestaurantId)
            .ToDictionary(g => g.Key, g => (Sales: g.Sum(x => x.Total), Bills: g.Count()));

        var staffCounts = (await _db.Staff.IgnoreQueryFilters().AsNoTracking()
            .Select(s => s.RestaurantId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var tableCounts = (await _db.Tables.IgnoreQueryFilters().AsNoTracking()
            .Select(t => t.RestaurantId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return restaurants.Select(r =>
        {
            var agg = invAgg.GetValueOrDefault(r.Id);
            var plan = r.PlanId is int pid ? plans.GetValueOrDefault(pid) : null;
            return new PlatformRestaurantDto(r.Id, r.Slug, r.Name, r.PlanName, r.Status.ToString(),
                r.CreatedAt, staffCounts.GetValueOrDefault(r.Id), tableCounts.GetValueOrDefault(r.Id),
                agg.Sales, agg.Bills, r.FeatureFbr,
                plan?.PricePkr ?? 0, plan?.SetupFeePkr ?? 0, r.SetupFeePaid, r.PlanExpiresAt, r.LastPaymentAt);
        }).ToList();
    }

    /// <summary>Record an offline subscription payment. Setup marks the one-time fee
    /// paid; Monthly extends the paid-through date by N months and reactivates.</summary>
    public async Task<SubscriptionPaymentDto> RecordPayment(int restaurantId, RecordPaymentRequest req, string operatorName, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == restaurantId, ct) ?? throw new DomainException("Restaurant not found.");
        if (!Enum.TryParse<Domain.Subscriptions.SubscriptionPaymentKind>(req.Kind, true, out var kind))
            throw new DomainException($"Unknown payment kind '{req.Kind}'.");

        var now = DateTime.UtcNow;
        int months = 0;
        if (kind == Domain.Subscriptions.SubscriptionPaymentKind.Setup)
        {
            r.SetupFeePaid = true;
        }
        else
        {
            months = req.Months <= 0 ? 1 : req.Months;
            var from = r.PlanExpiresAt is DateTime cur && cur > now ? cur : now;
            r.PlanExpiresAt = from.AddMonths(months);
        }
        r.LastPaymentAt = now;
        r.Status = RestaurantStatus.Active;   // recording a payment reactivates access

        var payment = new Domain.Subscriptions.SubscriptionPayment
        {
            RestaurantId = r.Id, Kind = kind, Months = months, AmountPkr = req.AmountPkr,
            PaidAt = now, NewPaidThrough = r.PlanExpiresAt, Note = (req.Note ?? "").Trim(), RecordedBy = operatorName,
        };
        _db.SubscriptionPayments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return new SubscriptionPaymentDto(payment.Id, payment.Kind.ToString(), payment.Months,
            payment.AmountPkr, payment.PaidAt, payment.NewPaidThrough, payment.Note, payment.RecordedBy);
    }

    public async Task<IReadOnlyList<SubscriptionPaymentDto>> ListPayments(int restaurantId, CancellationToken ct = default)
    {
        return await _db.SubscriptionPayments.AsNoTracking()
            .Where(p => p.RestaurantId == restaurantId)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new SubscriptionPaymentDto(p.Id, p.Kind.ToString(), p.Months, p.AmountPkr,
                p.PaidAt, p.NewPaidThrough, p.Note, p.RecordedBy))
            .ToListAsync(ct);
    }

    /// <summary>SaaS gate: grant or revoke FBR integration for a restaurant.</summary>
    public async Task SetFbr(int restaurantId, bool enabled, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == restaurantId, ct) ?? throw new DomainException("Restaurant not found.");
        r.FeatureFbr = enabled;
        if (!enabled) r.FbrEnabled = false;   // revoking the SaaS gate also turns off the owner's sync
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetPlan(int restaurantId, int planId, CancellationToken ct = default)
    {
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new DomainException("Plan not found.");
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == restaurantId, ct) ?? throw new DomainException("Restaurant not found.");
        r.AssignPlan(plan);
        await _db.SaveChangesAsync(ct);
    }

    // ---- plan catalogue ----
    public async Task<IReadOnlyList<PlanDto>> ListPlans(bool activeOnly = false, CancellationToken ct = default)
    {
        var q = _db.Plans.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(p => p.IsActive);
        return await q.OrderBy(p => p.SortOrder).Select(p => new PlanDto(
            p.Id, p.Name, p.Description, p.PricePkr, p.SetupFeePkr, p.SortOrder, p.IsActive,
            p.Ordering, p.Kitchen, p.Waiter, p.Billing, p.Inventory, p.Reports)).ToListAsync(ct);
    }

    public async Task<int> CreatePlan(PlanInput i, CancellationToken ct = default)
    {
        var p = new Domain.Subscriptions.Plan();
        MapPlan(p, i);
        _db.Plans.Add(p);
        await _db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task UpdatePlan(int id, PlanInput i, CancellationToken ct = default)
    {
        var p = await _db.Plans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Plan not found.");
        MapPlan(p, i);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeletePlan(int id, CancellationToken ct = default)
    {
        var p = await _db.Plans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Plan not found.");
        if (await _db.Restaurants.AnyAsync(r => r.PlanId == id, ct))
            throw new DomainException("Some restaurants are on this plan — move them first.");
        _db.Plans.Remove(p);
        await _db.SaveChangesAsync(ct);
    }

    private static void MapPlan(Domain.Subscriptions.Plan p, PlanInput i)
    {
        p.Name = i.Name.Trim(); p.Description = i.Description?.Trim() ?? ""; p.PricePkr = i.PricePkr;
        p.SetupFeePkr = i.SetupFeePkr;
        p.SortOrder = i.SortOrder; p.IsActive = i.IsActive;
        p.Ordering = i.Ordering; p.Kitchen = i.Kitchen; p.Waiter = i.Waiter;
        p.Billing = i.Billing; p.Inventory = i.Inventory; p.Reports = i.Reports;
    }

    public async Task SetStatus(int restaurantId, string status, CancellationToken ct = default)
    {
        if (!Enum.TryParse<RestaurantStatus>(status, true, out var s)) throw new DomainException($"Unknown status '{status}'.");
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == restaurantId, ct) ?? throw new DomainException("Restaurant not found.");
        r.Status = s;
        await _db.SaveChangesAsync(ct);
    }
}
