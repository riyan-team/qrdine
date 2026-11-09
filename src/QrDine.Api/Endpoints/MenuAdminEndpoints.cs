using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Common;
using QrDine.Application.Menu;
using QrDine.Application.Menu.Dtos;
using QrDine.Application.Reports;

namespace QrDine.Api.Endpoints;

// Owner/manager menu management. The read (GET) reuses the same menu the guest
// sees (it already carries ids, variants and modifiers). Writes are gated.
public static class MenuAdminEndpoints
{
    public static void MapMenuAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/r/{slug}/admin")
            .RequireAuthorization(Policies.CanManageMenu)
            .AddEndpointFilter<ValidationFilter>();

        g.MapGet("/menu", async (string slug, MenuService svc) =>
        {
            var menu = await svc.GetMenu(slug, forAdmin: true);
            return menu is null ? Results.NotFound() : Results.Ok(menu);
        });

        // categories
        g.MapPost("/categories", async (CategoryInput body, MenuAdminService svc) => Results.Ok(new { id = await svc.AddCategory(body) }));
        g.MapPut("/categories/{id:int}", async (int id, CategoryInput body, MenuAdminService svc) => { await svc.UpdateCategory(id, body); return Results.NoContent(); });
        g.MapDelete("/categories/{id:int}", async (int id, MenuAdminService svc) => { await svc.DeleteCategory(id); return Results.NoContent(); });

        // items
        g.MapPost("/items", async (ItemInput body, MenuAdminService svc) => Results.Ok(new { id = await svc.CreateItem(body) }));
        g.MapPut("/items/{id:int}", async (int id, ItemInput body, MenuAdminService svc) => { await svc.UpdateItem(id, body); return Results.NoContent(); });
        g.MapPost("/items/{id:int}/availability", async (int id, AvailabilityInput body, MenuAdminService svc) => { await svc.SetAvailability(id, body.Available); return Results.NoContent(); });
        g.MapDelete("/items/{id:int}", async (int id, MenuAdminService svc) => { await svc.DeleteItem(id); return Results.NoContent(); });

        // Photo upload (multipart). Max 5 MB; validated by content, not filename.
        g.MapPost("/items/{id:int}/photo", async (int id, HttpRequest req, IFileStorage storage, MenuAdminService svc, CancellationToken ct) =>
        {
            var img = await ImageUpload.ReadAsync(req, 5 * 1024 * 1024, ct);
            using var stream = new MemoryStream(img.Data);
            var url = await storage.Save(stream, img.Extension, "menu", ct);
            await svc.SetItemPhoto(id, url);
            return Results.Ok(new { imageUrl = url });
        }).DisableAntiforgery();

        // ---- Bulk menu import (review before saving) ----
        // Read a menu from a photo (Gemini). Returns a preview; nothing is saved yet.
        g.MapPost("/menu/import/photo", async (HttpRequest req, IMenuVisionExtractor vision, CancellationToken ct) =>
        {
            var img = await ImageUpload.ReadAsync(req, 8 * 1024 * 1024, ct);
            return Results.Ok(await vision.Extract(img.Data, img.ContentType, ct));
        }).DisableAntiforgery();

        // Parse a pasted/uploaded CSV (Category, Item, Price). Returns a preview.
        g.MapPost("/menu/import/csv", async (HttpRequest req, MenuImportService svc) =>
        {
            // Cap the body so a huge paste can't exhaust server memory.
            const long maxBytes = 1 * 1024 * 1024;   // 1 MB of CSV is thousands of rows
            if (req.ContentLength is long len && len > maxBytes)
                return Results.BadRequest(new { detail = "That CSV is too large (max 1 MB)." });
            using var reader = new StreamReader(req.Body);
            var buffer = new char[maxBytes + 1];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (read > maxBytes) return Results.BadRequest(new { detail = "That CSV is too large (max 1 MB)." });
            var csv = new string(buffer, 0, read);
            return Results.Ok(svc.ParseCsv(csv));
        }).DisableAntiforgery();

        // Apply the reviewed preview → create categories + items.
        g.MapPost("/menu/import/apply", async (ApplyImportRequest body, MenuImportService svc)
            => Results.Ok(new { created = await svc.Apply(body) }));

        // Owner sales dashboard (Owner/Manager).
        app.MapGet("/api/r/{slug}/admin/dashboard", async (int? days, int? branchId, ReportsService svc)
                => Results.Ok(await svc.GetDashboard(days ?? 7, branchId)))
            .RequireAuthorization(Policies.CanViewReports)
            .RequiresPlan(r => r.FeatureReports);

        // Paginated sales report (Owner/Manager) — filters, search, server-side paging.
        app.MapGet("/api/r/{slug}/admin/reports/sales", async (DateTime? from, DateTime? to, int? branchId,
                string? q, string? method, int? page, int? pageSize, ReportsService svc) =>
            {
                var (fromUtc, toUtc) = Window(from, to);
                return Results.Ok(await svc.GetSales(fromUtc, toUtc, branchId, q, method, page ?? 1, pageSize ?? 20));
            })
            .RequireAuthorization(Policies.CanViewReports)
            .RequiresPlan(r => r.FeatureReports);

        // Downloadable invoice report / sales-tax return (CSV) — same filters as the report.
        app.MapGet("/api/r/{slug}/admin/reports/invoices.csv", async (DateTime? from, DateTime? to, int? branchId,
                string? q, string? method, ReportsService svc) =>
            {
                var (fromUtc, toUtc) = Window(from, to);
                var rows = await svc.ExportInvoices(fromUtc, toUtc, branchId, q, method);
                var csv = ReportsCsv.Build(rows);
                var name = $"invoices_{fromUtc:yyyyMMdd}_{toUtc:yyyyMMdd}.csv";
                return Results.File(new System.Text.UTF8Encoding(true).GetBytes(csv), "text/csv", name);
            })
            .RequireAuthorization(Policies.CanViewReports)
            .RequiresPlan(r => r.FeatureReports);
    }

    // Normalise a date window: default to the last 31 days; extend an end-date with no
    // time component to the end of that day so the whole day is included.
    private static (DateTime from, DateTime to) Window(DateTime? from, DateTime? to)
    {
        var fromUtc = from ?? DateTime.UtcNow.AddDays(-31);
        var toUtc = to ?? DateTime.UtcNow;
        if (toUtc.TimeOfDay == TimeSpan.Zero) toUtc = toUtc.AddDays(1).AddSeconds(-1);
        return (fromUtc, toUtc);
    }
}

/// <summary>Builds the invoice-report CSV (with a totals row for the tax return).</summary>
internal static class ReportsCsv
{
    public static string Build(IReadOnlyList<Application.Reports.InvoiceReportRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Invoice,Date,Branch,Table,Payment,Cashier,NetSales,ServiceCharge,SalesTax,FurtherTax,Discount,Total,Tendered,Change,FiscalStatus,FiscalNumber");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                $"INV-{r.Number:D5}", Q(BusinessDay.ToPkt(r.IssuedAt).ToString("yyyy-MM-dd HH:mm")), Q(r.Branch), Q(r.Table), Q(r.PaymentMethod), Q(r.Cashier),
                r.NetSales, r.ServiceCharge, r.SalesTax, r.FurtherTax, r.Discount, r.Total, r.AmountTendered, r.ChangeGiven, r.FiscalStatus, Q(r.FiscalNumber)));
        // Totals row — the numbers a sales-tax return needs at a glance.
        sb.AppendLine(string.Join(",", "TOTAL", "", "", "", "", "",
            rows.Sum(r => r.NetSales), rows.Sum(r => r.ServiceCharge), rows.Sum(r => r.SalesTax),
            rows.Sum(r => r.FurtherTax), rows.Sum(r => r.Discount), rows.Sum(r => r.Total),
            rows.Sum(r => r.AmountTendered), rows.Sum(r => r.ChangeGiven), "", ""));
        return sb.ToString();
    }

    // Quote a field that may contain commas/quotes.
    private static string Q(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
}
