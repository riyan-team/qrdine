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

        // Photo upload (multipart). Max 5 MB, images only.
        g.MapPost("/items/{id:int}/photo", async (int id, IFormFile file, IFileStorage storage, MenuAdminService svc) =>
        {
            if (file is null || file.Length == 0) return Results.BadRequest(new { detail = "No file." });
            if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { detail = "Image must be under 5 MB." });
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return Results.BadRequest(new { detail = "Use a JPG, PNG or WebP image." });
            await using var stream = file.OpenReadStream();
            var url = await storage.Save(stream, ext, $"menu");
            await svc.SetItemPhoto(id, url);
            return Results.Ok(new { imageUrl = url });
        }).DisableAntiforgery();

        // ---- Bulk menu import (review before saving) ----
        // Read a menu from a photo (Gemini). Returns a preview; nothing is saved yet.
        g.MapPost("/menu/import/photo", async (IFormFile file, IMenuVisionExtractor vision) =>
        {
            if (file is null || file.Length == 0) return Results.BadRequest(new { detail = "No file." });
            if (file.Length > 8 * 1024 * 1024) return Results.BadRequest(new { detail = "Image must be under 8 MB." });
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var mime = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            return Results.Ok(await vision.Extract(ms.ToArray(), mime));
        }).DisableAntiforgery();

        // Parse a pasted/uploaded CSV (Category, Item, Price). Returns a preview.
        g.MapPost("/menu/import/csv", async (HttpRequest req, MenuImportService svc) =>
        {
            using var reader = new StreamReader(req.Body);
            var csv = await reader.ReadToEndAsync();
            return Results.Ok(svc.ParseCsv(csv));
        }).DisableAntiforgery();

        // Apply the reviewed preview → create categories + items.
        g.MapPost("/menu/import/apply", async (ApplyImportRequest body, MenuImportService svc)
            => Results.Ok(new { created = await svc.Apply(body) }));

        // Owner sales dashboard (Owner/Manager).
        app.MapGet("/api/r/{slug}/admin/dashboard", async (int? days, int? branchId, ReportsService svc)
                => Results.Ok(await svc.GetDashboard(days ?? 7, branchId)))
            .RequireAuthorization(Policies.CanViewReports);

        // Downloadable invoice report / sales-tax return (CSV) for a date window.
        app.MapGet("/api/r/{slug}/admin/reports/invoices.csv", async (DateTime? from, DateTime? to, int? branchId, ReportsService svc) =>
            {
                var fromUtc = from ?? DateTime.UtcNow.AddDays(-31);
                var toUtc = to ?? DateTime.UtcNow;
                if (toUtc.TimeOfDay == TimeSpan.Zero) toUtc = toUtc.AddDays(1).AddSeconds(-1);  // include the whole end day
                var rows = await svc.ExportInvoices(fromUtc, toUtc, branchId);
                var csv = ReportsCsv.Build(rows);
                var name = $"invoices_{fromUtc:yyyyMMdd}_{toUtc:yyyyMMdd}.csv";
                return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", name);
            })
            .RequireAuthorization(Policies.CanViewReports);
    }
}

/// <summary>Builds the invoice-report CSV (with a totals row for the tax return).</summary>
internal static class ReportsCsv
{
    public static string Build(IReadOnlyList<Application.Reports.InvoiceReportRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Invoice,Date,Branch,Table,Payment,NetSales,ServiceCharge,SalesTax,FurtherTax,Discount,Total,FiscalStatus,FiscalNumber");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                $"INV-{r.Number:D5}", Q(r.IssuedAt.ToString("yyyy-MM-dd HH:mm")), Q(r.Branch), Q(r.Table), Q(r.PaymentMethod),
                r.NetSales, r.ServiceCharge, r.SalesTax, r.FurtherTax, r.Discount, r.Total, r.FiscalStatus, Q(r.FiscalNumber)));
        // Totals row — the numbers a sales-tax return needs at a glance.
        sb.AppendLine(string.Join(",", "TOTAL", "", "", "", "",
            rows.Sum(r => r.NetSales), rows.Sum(r => r.ServiceCharge), rows.Sum(r => r.SalesTax),
            rows.Sum(r => r.FurtherTax), rows.Sum(r => r.Discount), rows.Sum(r => r.Total), "", ""));
        return sb.ToString();
    }

    // Quote a field that may contain commas/quotes.
    private static string Q(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
}
