using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Restaurants;

namespace QrDine.Application.Billing;

/// <summary>
/// Sends settled bills to the tax office (FBR or PRA, per branch) and keeps track of the ones still to go.
/// - Whether a restaurant files at all: the operator's switch (FeatureFbr) and the owner's (FbrEnabled).
/// - Where a branch's bills go: Practice (nothing leaves the app), Test (the office's sandbox) or Live.
/// - When: at settle ("send each bill when it is settled"), by hand from the Pending list (one, picked, or all),
///   and every few minutes by the automatic resend.
/// - Never twice: a bill already filed is never sent again, and a claim in the database means two senders at the
///   same moment can't both send it.
/// A filing problem never stops a sale: the bill is saved and printed, and waits in Pending with the reason.
/// </summary>
public class FiscalSyncService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IFiscalService _fiscal;
    private readonly IFiscalCapabilities _caps;

    /// <summary>How long one send may hold a bill before another sender may try it.</summary>
    public static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(2);
    /// <summary>The most bills one "send" press (or one automatic round) sends, so a screen never waits for minutes.</summary>
    public const int MaxPerBatch = 25;
    /// <summary>A refused bill (wrong token, bad data) is resent automatically this many times at most.</summary>
    public const int MaxAutoTriesForRefused = 3;

    public FiscalSyncService(IAppDbContext db, ICurrentTenant tenant, IFiscalService fiscal, IFiscalCapabilities? caps = null)
    {
        _db = db; _tenant = tenant; _fiscal = fiscal; _caps = caps ?? new DefaultFiscalCapabilities();
    }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");

    private static string Office(Branch? b) => (b?.FiscalAuthority ?? FiscalAuthority.FBR).ToString();
    private FiscalEnvironment EnvironmentOf(Branch? b) =>
        _caps.ForcedPractice ? FiscalEnvironment.Practice : b?.FiscalEnvironment ?? FiscalEnvironment.Practice;

    // ───────────────────────── at settle ─────────────────────────

    /// <summary>
    /// Called right after a bill is saved. Sends it now when the owner wants each bill sent at settle, or leaves it
    /// waiting in Pending. Never throws: the sale always completes and the receipt always prints.
    /// The caller saves the invoice afterwards.
    /// </summary>
    public async Task SyncOnSettle(Invoice invoice, Restaurant r, string? operatorUsername, CancellationToken ct = default)
    {
        if (!r.FeatureFbr || !r.FbrEnabled) { invoice.MarkFiscalNotRequired(); return; }
        try
        {
            var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == invoice.BranchId, ct);
            if (!r.FiscalSendAtSettle)
            {
                invoice.MarkFiscalWaiting(Office(branch), EnvironmentOf(branch).ToString());
                return;
            }
            await Send(invoice, r, branch, operatorUsername, FiscalTrigger.Settle, operatorUsername, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            invoice.ApplyFiscalFailure(invoice.FiscalProvider is "None" or "" ? "FBR" : invoice.FiscalProvider,
                FiscalStatus.Pending, $"Filing error: {ex.Message}", null);
        }
    }

    // ───────────────────────── by hand ─────────────────────────

    /// <summary>Sends one waiting or refused bill now.</summary>
    public async Task<FiscalInvoiceDto> Retry(int invoiceId, string? operatorUsername, bool isOwner = false, CancellationToken ct = default)
    {
        var r = await FilingRestaurant(ct);
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new DomainException("Invoice not found.");
        if (inv.IsFiscalised)
            throw new DomainException($"This bill is already filed ({inv.FiscalNumber}). It will not be sent again.");
        if (inv.FiscalStatus == FiscalStatus.NotRequired)
            throw new DomainException("This bill was settled while filing was off, so it isn't sent.");

        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == inv.BranchId, ct);
        RefuseOthersLocalBill(inv, branch, operatorUsername, isOwner);

        var outcome = await Send(inv, r, branch, operatorUsername, FiscalTrigger.Manual, operatorUsername, ct);
        if (outcome == SendOutcome.Busy)
            throw new DomainException("This bill is being sent right now. Refresh in a moment.");
        return Map(inv, r.Currency);
    }

    /// <summary>
    /// Sends several waiting bills: the ones picked, or (no ids) every waiting one, oldest first, up to
    /// <see cref="MaxPerBatch"/> a press. Stops early when the tax office can't be reached, since the rest would fail
    /// the same way.
    /// </summary>
    public async Task<FiscalBatchResult> RetryMany(IReadOnlyCollection<int>? ids, string? operatorUsername, bool isOwner = false,
        CancellationToken ct = default)
    {
        var r = await FilingRestaurant(ct);
        var q = _db.Invoices.AsNoTracking()
            .Where(i => i.FiscalStatus == FiscalStatus.Pending || i.FiscalStatus == FiscalStatus.Failed);
        if (ids is { Count: > 0 }) q = q.Where(i => ids.Contains(i.Id));
        var todo = await q.OrderBy(i => i.IssuedAt).Select(i => i.Id).Take(MaxPerBatch).ToListAsync(ct);

        int sent = 0, refused = 0, waiting = 0, skipped = 0, unreachableInRow = 0;
        string? stopped = null;
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, ct);
        foreach (var id in todo)
        {
            var inv = await _db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == id, ct);
            var branch = branches.GetValueOrDefault(inv.BranchId);
            if (!MayResend(inv, branch, operatorUsername, isOwner)) { skipped++; continue; }
            switch (await Send(inv, r, branch, operatorUsername ?? inv.CreatedByUsername,
                ids is { Count: > 0 } ? FiscalTrigger.Picked : FiscalTrigger.All, operatorUsername, ct))
            {
                case SendOutcome.Filed: sent++; unreachableInRow = 0; break;
                case SendOutcome.Refused: refused++; unreachableInRow = 0; break;
                case SendOutcome.Unreachable: waiting++; unreachableInRow++; break;
                default: skipped++; break;
            }
            if (unreachableInRow >= 3)
            {
                stopped = "The tax office couldn't be reached, so the rest were left waiting. Check the internet and try again.";
                break;
            }
        }
        var left = await _db.Invoices.CountAsync(i => i.FiscalStatus == FiscalStatus.Pending || i.FiscalStatus == FiscalStatus.Failed, ct);
        return new FiscalBatchResult(sent, refused, waiting, skipped, left, stopped);
    }

    // ───────────────────────── automatic ─────────────────────────

    /// <summary>
    /// One round of the automatic resend for this restaurant: bills that couldn't reach the tax office are tried
    /// again (less often the more they've failed), and refused ones a few times, in case the owner fixed the
    /// settings. Returns how many were filed.
    /// </summary>
    public async Task<int> AutoRetry(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        if (!r.FeatureFbr || !r.FbrEnabled || !r.FiscalAutoRetry) return 0;
        var now = DateTime.UtcNow;
        var candidates = await _db.Invoices.AsNoTracking()
            .Where(i => i.FiscalStatus == FiscalStatus.Pending
                || (i.FiscalStatus == FiscalStatus.Failed && i.FiscalAttempts < MaxAutoTriesForRefused))
            .OrderBy(i => i.IssuedAt).Take(200)
            .Select(i => new { i.Id, i.FiscalStatus, i.FiscalAttempts, i.FiscalLastAttemptAt })
            .ToListAsync(ct);
        // Waiting bills: the first try straight away, then every 2, 4 … up to 30 minutes. Refused: every 30 minutes.
        var due = candidates.Where(c => c.FiscalLastAttemptAt is not DateTime last
                || now - last >= (c.FiscalStatus == FiscalStatus.Failed
                    ? TimeSpan.FromMinutes(30)
                    : TimeSpan.FromMinutes(Math.Min(30, 2 * Math.Max(1, c.FiscalAttempts)))))
            .Select(c => c.Id).Take(MaxPerBatch).ToList();
        if (due.Count == 0) return 0;

        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, ct);
        var filed = 0;
        var unreachableInRow = 0;
        foreach (var id in due)
        {
            var inv = await _db.Invoices.Include(i => i.Lines).FirstAsync(i => i.Id == id, ct);
            var outcome = await Send(inv, r, branches.GetValueOrDefault(inv.BranchId), inv.CreatedByUsername, FiscalTrigger.Auto, null, ct);
            if (outcome == SendOutcome.Filed) filed++;
            unreachableInRow = outcome == SendOutcome.Unreachable ? unreachableInRow + 1 : 0;
            if (unreachableInRow >= 2) break;   // offline: the next round tries again
        }
        return filed;
    }

    // ───────────────────────── checks ─────────────────────────

    /// <summary>What stops each branch filing, and what is worth fixing, in plain words.</summary>
    public async Task<IReadOnlyList<FiscalBranchCheck>> Check(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        var cashierPos = await _db.Staff.AsNoTracking().CountAsync(s => s.FbrPosId != null, ct);
        var noPct = r.TaxEnabled
            ? await _db.MenuItems.AsNoTracking().CountAsync(m => m.Available && (m.PctCode == null || m.PctCode == ""), ct)
            : 0;
        return branches.Select(b =>
        {
            var env = EnvironmentOf(b);
            var (problems, warnings) = Problems(r, b, env, cashierPos > 0, noPct, _caps.LocalAvailable);
            return new FiscalBranchCheck(b.Id, b.Name, b.FiscalAuthority.ToString(), env.ToString(),
                b.FiscalAuthority == FiscalAuthority.PRA ? "Cloud" : b.FbrMode.ToString(),
                problems.Count == 0, problems, warnings, _caps.ForcedPractice);
        }).ToList();
    }

    /// <summary>The setup a branch needs before its bills can go to the tax office.</summary>
    public static (List<string> Problems, List<string> Warnings) Problems(Restaurant r, Branch b, FiscalEnvironment env,
        bool someCashierHasPosId, int itemsWithoutPct, bool localAvailable)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var office = b.FiscalAuthority.ToString();
        if (!r.FeatureFbr) problems.Add("Tax office filing isn't in this restaurant's plan. Ask Dine In to turn it on.");
        else if (!r.FbrEnabled) problems.Add("Filing is switched off for this restaurant (Settings → Tax office).");
        if (env == FiscalEnvironment.Practice)
        {
            warnings.Add("Practice: bills get a made-up number and nothing is sent. Switch to Test with your test POSID and token, then Live.");
            return (problems, warnings);
        }
        if (b.FbrPosId is null && !someCashierHasPosId)
            problems.Add($"Add the POSID {office} gave this branch (or give each cashier their own POSID).");
        var online = b.FiscalAuthority == FiscalAuthority.PRA || b.FbrMode == FbrMode.Cloud;
        if (online && string.IsNullOrWhiteSpace(b.FbrToken))
            problems.Add($"Add the {office} token (the long code from {office}'s portal) for this branch.");
        if (!online && !localAvailable)
            problems.Add("“On this computer” needs the FBR software installed on the counter PC, which only the PC edition has. On the online system choose “Online”.");
        if (online && string.IsNullOrWhiteSpace(b.FbrStaticIp))
            warnings.Add($"{office} only accepts bills from the internet address it has on record. Write it in “Registered IP” so you can check it matches.");
        if (string.IsNullOrWhiteSpace(r.Ntn))
            warnings.Add("Add your NTN in Settings → Tax office, so it is printed on receipts.");
        if (itemsWithoutPct > 0)
            warnings.Add($"{itemsWithoutPct} menu item{(itemsWithoutPct == 1 ? " has" : "s have")} no PCT code. {office} may refuse bills without one (Menu → item → PCT code).");
        if (!r.TaxEnabled)
            warnings.Add("Tax is off on bills (Settings → Bill), so bills are filed with no tax.");
        return (problems, warnings);
    }

    /// <summary>
    /// Sends a made-up Rs 100 bill to this branch's Test (or Practice) setup, to prove the POSID, token and address
    /// work. Never to Live: a test bill there would be a real tax invoice. The bill isn't saved; the try is logged.
    /// </summary>
    public async Task<FiscalTestResult> SendTestInvoice(int branchId, string? operatorUsername, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var b = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == branchId, ct)
            ?? throw new DomainException("Branch not found.");
        var env = EnvironmentOf(b);
        if (env == FiscalEnvironment.Live)
            throw new DomainException("Test bills are never sent to the live tax office. Switch this branch to Test, send the test bill, then switch back to Live.");

        var rate = r.TaxEnabled ? r.TaxPercent : 0;
        var lines = new List<InvoiceLine> { new() { ItemCode = "TEST", Name = "Test item", PctCode = "0000.0000", UnitPrice = 100, Qty = 1, TaxRate = rate } };
        var stamp = DateTime.UtcNow.ToString("yyMMddHHmmss");
        var test = Invoice.Create(r.Id, 0, "TEST", "Test bill", lines, 0, 0, "Cash", 0, $"TEST{branchId}-{stamp}");
        test.BranchId = b.Id;
        StaffMember? op = string.IsNullOrWhiteSpace(operatorUsername) ? null
            : await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Username == operatorUsername, ct);
        var ctx = BuildContext(r, b, op, env);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var res = await _fiscal.Register(test, ctx, ct);
        _db.FiscalLog.Add(LogEntry(test, b.Id, null, FiscalTrigger.Test, operatorUsername, ctx, res, 1, clock));
        await _db.SaveChangesAsync(ct);
        return new FiscalTestResult(res.Status == FiscalStatus.Registered, b.FiscalAuthority.ToString(), res.Environment ?? env.ToString(),
            res.Number, res.Error, test.Total, test.TaxAmount);
    }

    // ───────────────────────── the one way a bill is sent ─────────────────────────

    private enum SendOutcome { Filed, Refused, Unreachable, Busy }

    /// <param name="operatorUsername">Whose POSID and token to use (the cashier's own win over the branch's).</param>
    /// <param name="trigger">How this try started, for the log (<see cref="FiscalTrigger"/>).</param>
    /// <param name="by">Who pressed, for the log; null for the automatic resend.</param>
    private async Task<SendOutcome> Send(Invoice inv, Restaurant r, Branch? branch, string? operatorUsername, string trigger, string? by,
        CancellationToken ct)
    {
        if (inv.IsFiscalised) return SendOutcome.Filed;
        // A saved bill is claimed in the database first: whoever gets the claim sends, anyone else steps back.
        var claimed = await _db.ClaimInvoiceForFiling(inv.Id, DateTime.UtcNow + ClaimFor, ct);
        if (!claimed) return SendOutcome.Busy;
        try
        {
            StaffMember? op = string.IsNullOrWhiteSpace(operatorUsername) ? null
                : await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Username == operatorUsername, ct);
            var env = EnvironmentOf(branch);
            var ctx = BuildContext(r, branch, op, env);
            await FillMissingPctCodes(inv, ct);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            FiscalResult res;
            try { res = await _fiscal.Register(inv, ctx, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Something broke before an answer came: log it (the caller decides what happens to the bill).
                var broke = new FiscalResult(Office(branch), FiscalStatus.Pending, null, null, $"Filing error: {ex.Message}", env.ToString());
                _db.FiscalLog.Add(LogEntry(inv, inv.BranchId, inv.Id, trigger, by, ctx, broke, inv.FiscalAttempts + 1, clock, FiscalOutcome.Error));
                try { await _db.SaveChangesAsync(CancellationToken.None); } catch (Exception) { /* the log must never hide the real error */ }
                throw;
            }
            inv.FiscalEnvironment = res.Environment ?? env.ToString();
            if (res.Status == FiscalStatus.Registered)
                inv.ApplyFiscalSuccess(res.Provider, res.Number, res.QrPayload, ctx.PosId);
            else
                inv.ApplyFiscalFailure(res.Provider, res.Status, res.Error ?? "Unknown filing error.", ctx.PosId);
            _db.FiscalLog.Add(LogEntry(inv, inv.BranchId, inv.Id, trigger, by, ctx, res, inv.FiscalAttempts, clock));
            await _db.SaveChangesAsync(ct);
            return res.Status == FiscalStatus.Registered ? SendOutcome.Filed
                : res.Unreachable || res.Status == FiscalStatus.Pending ? SendOutcome.Unreachable
                : SendOutcome.Refused;
        }
        finally
        {
            await _db.ReleaseInvoiceClaim(inv.Id, CancellationToken.None);
        }
    }

    // A bill refused for a missing PCT code is fixed in the menu, then sent again: lines that had no PCT code take the
    // dish's code from the menu now. Nothing else on the bill changes (prices, tax and totals stay as settled).
    private async Task FillMissingPctCodes(Invoice inv, CancellationToken ct)
    {
        var blank = inv.Lines.Where(l => string.IsNullOrWhiteSpace(l.PctCode) && int.TryParse(l.ItemCode, out _)).ToList();
        if (blank.Count == 0) return;
        var ids = blank.Select(l => int.Parse(l.ItemCode)).Distinct().ToList();
        var codes = await _db.MenuItems.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.PctCode != null && m.PctCode != "")
            .Select(m => new { m.Id, m.PctCode }).ToDictionaryAsync(m => m.Id, m => m.PctCode!, ct);
        foreach (var l in blank)
            if (codes.TryGetValue(int.Parse(l.ItemCode), out var pct)) l.PctCode = pct.Trim();
    }

    private async Task<Restaurant> FilingRestaurant(CancellationToken ct)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        if (!r.FeatureFbr) throw new DomainException("Tax office filing isn't in your plan. Ask Dine In to turn it on.");
        if (!r.FbrEnabled) throw new DomainException("Filing is switched off in Settings → Tax office.");
        return r;
    }

    // With the FBR software on the counter PC, a bill is re-sent from the cashier login that made it (an owner may
    // override), since that PC's software holds that cashier's POS registration.
    private static void RefuseOthersLocalBill(Invoice inv, Branch? branch, string? operatorUsername, bool isOwner)
    {
        if (!MayResend(inv, branch, operatorUsername, isOwner))
            throw new DomainException($"With the FBR software on the counter PC, only the cashier who made this bill ({inv.CreatedByUsername}) can send it again.");
    }

    private static bool MayResend(Invoice inv, Branch? branch, string? operatorUsername, bool isOwner) =>
        isOwner || branch is null || branch.FiscalAuthority != FiscalAuthority.FBR || branch.FbrMode != FbrMode.Local
        || string.IsNullOrEmpty(inv.CreatedByUsername)
        || string.Equals(inv.CreatedByUsername, operatorUsername, StringComparison.OrdinalIgnoreCase);

    // POSID/token: the cashier's own win, else the branch's.
    private static FiscalContext BuildContext(Restaurant r, Branch? b, StaffMember? op, FiscalEnvironment env) => new(
        string.IsNullOrWhiteSpace(r.BusinessName) ? r.Name : r.BusinessName, r.Ntn, r.Strn,
        b?.FiscalAuthority == FiscalAuthority.PRA ? FbrMode.Cloud : b?.FbrMode ?? FbrMode.Cloud,
        env != FiscalEnvironment.Live, b?.FbrEndpointOverride,
        op?.FbrPosId ?? b?.FbrPosId, string.IsNullOrWhiteSpace(op?.FbrToken) ? b?.FbrToken : op!.FbrToken,
        b?.FiscalAuthority ?? FiscalAuthority.FBR, env);

    private FiscalLogEntry LogEntry(Invoice inv, int branchId, int? invoiceId, string trigger, string? by, FiscalContext ctx,
        FiscalResult res, int attempt, System.Diagnostics.Stopwatch clock, string? outcome = null)
    {
        var x = res.Exchange;
        return new FiscalLogEntry
        {
            RestaurantId = TenantId,
            InvoiceId = invoiceId,
            BillNumber = invoiceId is null ? (string.IsNullOrEmpty(inv.Usin) ? "TEST" : inv.Usin) : inv.FormattedNumber,
            BranchId = branchId,
            At = DateTime.UtcNow,
            Office = ctx.Authority.ToString(),
            Environment = res.Environment ?? ctx.Environment.ToString(),
            Trigger = trigger,
            By = string.IsNullOrWhiteSpace(by) ? null : by,
            PosId = ctx.PosId,
            Outcome = outcome ?? (res.Status == FiscalStatus.Registered ? FiscalOutcome.Filed
                : res.Unreachable || res.Status == FiscalStatus.Pending ? FiscalOutcome.Waiting
                : FiscalOutcome.Refused),
            FiscalNumber = res.Number,
            Error = FiscalLogEntry.Cut(res.Error, 1990),
            Endpoint = FiscalLogEntry.Cut(x?.Endpoint, 290),
            HttpStatus = x?.HttpStatus,
            DurationMs = x is { DurationMs: > 0 } ? x.DurationMs : (int)clock.ElapsedMilliseconds,
            RequestBody = FiscalLogEntry.Cut(x?.Request, FiscalLogEntry.MaxRequestChars),
            ResponseBody = FiscalLogEntry.Cut(x?.Response, FiscalLogEntry.MaxResponseChars),
            Attempt = Math.Max(1, attempt),
        };
    }

    // ───────────────────────── the log ─────────────────────────

    /// <summary>
    /// The tax office log, newest first: every try at sending a bill, with how it started and how it ended.
    /// Filters: one bill, problems only (refused, waiting, errors), or a bill number search. Without the JSON
    /// (<see cref="LogLine"/> gives one line in full).
    /// </summary>
    public async Task<FiscalLogPage> Log(int? invoiceId, bool problemsOnly, string? search, int? beforeId, int take = 100,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var q = _db.FiscalLog.AsNoTracking();
        if (invoiceId is int id) q = q.Where(x => x.InvoiceId == id);
        if (problemsOnly) q = q.Where(x => x.Outcome != FiscalOutcome.Filed);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.BillNumber.Contains(term) || (x.FiscalNumber != null && x.FiscalNumber.Contains(term)));
        }
        if (beforeId is int before) q = q.Where(x => x.Id < before);
        var rows = await q.OrderByDescending(x => x.Id).Take(take + 1)
            .Select(x => new FiscalLogRow(x.Id, x.At, x.InvoiceId, x.BillNumber, x.BranchId, x.Office, x.Environment, x.Trigger, x.By,
                x.PosId, x.Outcome, x.FiscalNumber, x.Error, x.HttpStatus, x.DurationMs, x.Attempt, x.RequestBody != null))
            .ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(rows.Count - 1);
        // Counts for the last 24 hours, so the owner sees at a glance how filing is going.
        var since = DateTime.UtcNow.AddHours(-24);
        var day = await _db.FiscalLog.AsNoTracking().Where(x => x.At >= since)
            .GroupBy(x => x.Outcome).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        int N(string k) => day.FirstOrDefault(d => d.Key == k)?.N ?? 0;
        return new FiscalLogPage(rows, more,
            new FiscalLogDay(N(FiscalOutcome.Filed), N(FiscalOutcome.Refused), N(FiscalOutcome.Waiting), N(FiscalOutcome.Error)));
    }

    /// <summary>One log line in full: the address, the JSON sent and the answer.</summary>
    public async Task<FiscalLogDetail> LogLine(int id, CancellationToken ct = default)
    {
        var x = await _db.FiscalLog.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new DomainException($"That log line isn't there any more (lines older than {FiscalLogEntry.KeepDays} days are cleared).");
        return new FiscalLogDetail(
            new FiscalLogRow(x.Id, x.At, x.InvoiceId, x.BillNumber, x.BranchId, x.Office, x.Environment, x.Trigger, x.By,
                x.PosId, x.Outcome, x.FiscalNumber, x.Error, x.HttpStatus, x.DurationMs, x.Attempt, x.RequestBody != null),
            x.Endpoint, x.RequestBody, x.ResponseBody);
    }

    // ───────────────────────── lists ─────────────────────────

    /// <summary>The tax office view: filed bills, or the ones still to go (waiting, or refused with the reason).</summary>
    public async Task<IReadOnlyList<FiscalInvoiceDto>> List(bool synced, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        var q = _db.Invoices.AsNoTracking();
        q = synced
            ? q.Where(i => i.FiscalStatus == FiscalStatus.Registered)
            : q.Where(i => i.FiscalStatus == FiscalStatus.Pending || i.FiscalStatus == FiscalStatus.Failed);
        var list = await (synced ? q.OrderByDescending(i => i.IssuedAt) : q.OrderBy(i => i.IssuedAt)).Take(500).ToListAsync(ct);
        return list.Select(i => Map(i, r.Currency)).ToList();
    }

    private static FiscalInvoiceDto Map(Invoice i, string currency) => new(
        i.Id, i.FormattedNumber, i.Usin, i.TableName, i.IssuedAt, currency, i.Total, i.PaymentMethod,
        i.FiscalStatus.ToString(), i.FiscalNumber, i.FiscalError, i.FiscalAttempts, i.FiscalLastAttemptAt, i.SyncedByPosId,
        // Bills from older builds: the provider said "Simulated" for practice and had no environment.
        i.FiscalProvider is "PRA" ? "PRA" : "FBR",
        i.FiscalEnvironment is "" && i.FiscalProvider is "Simulated" ? "Practice" : i.FiscalEnvironment,
        i.FiscalClaimUntil is DateTime c && c > DateTime.UtcNow,
        i.TaxAmount + i.FurtherTaxAmount + i.ServiceTaxAmount);
}
