using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Domain.Billing;
using QrDine.Infrastructure.Persistence;

namespace QrDine.Infrastructure.Fiscal;

/// <summary>
/// The automatic resend: every few minutes (Fiscal:AutoRetryMinutes), for each restaurant that files and has
/// "keep sending waiting bills" on, sends the bills that couldn't reach the tax office. Each restaurant runs in its
/// own scope with its own tenant, one after another; a bill being sent by someone else is left alone (the claim in
/// <see cref="FiscalSyncService"/>), so this can never file a bill twice.
/// </summary>
public class FiscalAutoRetryService(IServiceScopeFactory scopes, IOptions<FiscalOptions> opt, ILogger<FiscalAutoRetryService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var minutes = opt.Value.AutoRetryMinutes;
        if (minutes <= 0) return;
        var every = TimeSpan.FromMinutes(minutes);
        try { await Task.Delay(TimeSpan.FromSeconds(45), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { await RoundOnce(stop); }
            catch (Exception ex) when (!stop.IsCancellationRequested) { log.LogWarning(ex, "Automatic tax office resend round failed."); }
            try { await Task.Delay(every, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One round over every restaurant with bills waiting. Public for tests.</summary>
    public async Task<int> RoundOnce(CancellationToken ct)
    {
        List<(int Id, string Slug)> due;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var filing = await db.Restaurants.AsNoTracking()
                .Where(r => r.FeatureFbr && r.FbrEnabled && r.FiscalAutoRetry)
                .Select(r => new { r.Id, r.Slug }).ToListAsync(ct);
            if (filing.Count == 0) return 0;
            var ids = filing.Select(r => r.Id).ToList();
            // A restaurant whose counter runs on its own PC files its bills from that PC; the copies here are only copies.
            var onPc = await db.SyncDevices.IgnoreQueryFilters().AsNoTracking()
                .Where(d => ids.Contains(d.RestaurantId) && d.RevokedAt == null && d.CounterSince != null)
                .Select(d => d.RestaurantId).ToListAsync(ct);
            ids = ids.Except(onPc).ToList();
            var waiting = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
                .Where(i => ids.Contains(i.RestaurantId) && (i.FiscalStatus == FiscalStatus.Pending || i.FiscalStatus == FiscalStatus.Failed))
                .Select(i => i.RestaurantId).Distinct().ToListAsync(ct);
            due = filing.Where(r => waiting.Contains(r.Id)).Select(r => (r.Id, r.Slug)).ToList();
        }

        var filed = 0;
        foreach (var (id, slug) in due)
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Set(id, slug);
            try { filed += await scope.ServiceProvider.GetRequiredService<FiscalSyncService>().AutoRetry(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Automatic resend failed for restaurant {Slug}.", slug);
            }
        }
        if (filed > 0) log.LogInformation("Automatic resend filed {Count} bill(s).", filed);
        return filed;
    }
}
