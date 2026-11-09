using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using QrDine.Api.Auth;
using QrDine.Api.Validation;
using QrDine.Application.Billing;
using QrDine.Application.Common;
using QrDine.Application.Customers;
using QrDine.Application.Menu;
using QrDine.Application.Menu.Dtos;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Staffing;
using QrDine.Domain.Common;

namespace QrDine.Api.Endpoints;

// What runs the floor and the till beyond the basic order flow: parcel and delivery tickets,
// moving a table, taking a dish off an order, calls from guests, customer khata, salaries and
// the end-of-day count. Each is gated by a permission the owner can grant per role.
public static class FloorEndpoints
{
    private static string Who(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    public static void MapFloor(this IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/api/r/{slug}").AddEndpointFilter<ValidationFilter>();

        // ---- parcel and delivery ----
        root.MapPost("/tickets", async (TicketRequest body, TicketService svc) => Results.Ok(await svc.Place(body)))
            .RequireAuthorization(Policies.CanCreateOrders).RequireRateLimiting("orders");
        root.MapGet("/tickets", async (TicketService svc) => Results.Ok(await svc.Open()))
            .RequireAuthorization(Policies.StaffAny);
        root.MapPut("/tickets/{code}", async (string code, TicketUpdate body, TicketService svc) => Results.Ok(await svc.Update(code, body)))
            .RequireAuthorization(Policies.CanCreateOrders);

        // ---- shift guests to another table ----
        root.MapPost("/tables/{code}/move", async (string code, MoveTableRequest body, OrderService svc) =>
            Results.Ok(await svc.MoveTable(code, body.ToCode))).RequireAuthorization(Policies.CanCreateOrders);

        // ---- take a dish off a running order ----
        root.MapPost("/orders/{id:int}/items/{itemId:int}/void", async (int id, int itemId, VoidItemRequest body, ClaimsPrincipal user, OrderService svc) =>
            Results.Ok(await svc.VoidItem(id, itemId, body, Who(user)))).RequireAuthorization(Policies.CanCancel);

        // ---- guests calling for a waiter or the bill: the token must be for this table ----
        root.MapPost("/tables/{code}/call", async (string code, CallStaffRequest body, ClaimsPrincipal user, ServiceRequestService svc) =>
        {
            if (user.FindFirst(AppClaims.TokenType)?.Value == AppClaims.TypeGuest
                && !string.Equals(user.FindFirst(AppClaims.TableCode)?.Value, code, StringComparison.OrdinalIgnoreCase))
                throw new DomainException("This session is for a different table.");
            return Results.Ok(await svc.Call(code, body.Kind));
        }).RequireAuthorization(Policies.GuestOrStaff).RequireRateLimiting("public");
        root.MapGet("/staff/requests", async (ServiceRequestService svc) => Results.Ok(await svc.Open()))
            .RequireAuthorization(Policies.StaffAny);
        root.MapPost("/staff/requests/{id:int}/done", async (int id, ClaimsPrincipal user, ServiceRequestService svc) =>
            { await svc.Done(id, Who(user)); return Results.NoContent(); }).RequireAuthorization(Policies.StaffAny);

        // ---- what this restaurant's plan includes today, so an open screen notices a plan change ----
        root.MapGet("/staff/plan", (HttpContext http) =>
            Results.Ok(QrDine.Application.Auth.AuthService.LoadFeatures(http.Items[PlanGate.RestaurantKey] as QrDine.Domain.Restaurants.Restaurant)))
            .RequireAuthorization(Policies.StaffAny);

        // ---- find a customer while taking an order or settling on khata ----
        root.MapGet("/staff/customers/find", async (string? q, CustomerService svc) => Results.Ok(await svc.Find(q)))
            .RequireAuthorization(Policies.StaffAny);

        // ---- customer khata ----
        var customers = app.MapGroup("/api/r/{slug}/admin/customers")
            .RequireAuthorization(Policies.CanManageCustomers).RequiresPlan(r => r.FeatureBilling).AddEndpointFilter<ValidationFilter>();
        customers.MapGet("", async (CustomerService svc) => Results.Ok(await svc.List()));
        customers.MapPost("", async (CustomerInput body, CustomerService svc) => Results.Ok(new { id = await svc.Create(body) }));
        customers.MapPut("/{id:int}", async (int id, CustomerInput body, CustomerService svc) => { await svc.Update(id, body); return Results.NoContent(); });
        customers.MapDelete("/{id:int}", async (int id, CustomerService svc) => { await svc.Archive(id); return Results.NoContent(); });
        customers.MapGet("/{id:int}/ledger", async (int id, CustomerService svc) => Results.Ok(await svc.Ledger(id)));
        customers.MapPost("/{id:int}/payments", async (int id, CustomerPaymentInput body, ClaimsPrincipal user, CustomerService svc) =>
            Results.Ok(new { id = await svc.AddPayment(id, body, Who(user)) }));
        customers.MapDelete("/entries/{entryId:int}", async (int entryId, CustomerService svc) => { await svc.DeleteEntry(entryId); return Results.NoContent(); });

        // ---- salaries and attendance ----
        var payroll = app.MapGroup("/api/r/{slug}/admin/payroll")
            .RequireAuthorization(Policies.CanManagePayroll).RequiresPlan(r => r.FeatureReports).AddEndpointFilter<ValidationFilter>();
        payroll.MapGet("", async (string? month, PayrollService svc) => Results.Ok(await svc.Month(month)));
        payroll.MapPost("", async (EmployeeInput body, PayrollService svc) => Results.Ok(new { id = await svc.Create(body) }));
        payroll.MapPut("/{id:int}", async (int id, EmployeeInput body, PayrollService svc) => { await svc.Update(id, body); return Results.NoContent(); });
        payroll.MapDelete("/{id:int}", async (int id, PayrollService svc) => { await svc.Archive(id); return Results.NoContent(); });
        payroll.MapGet("/{id:int}/entries", async (int id, string? month, PayrollService svc) => Results.Ok(await svc.Entries(id, month)));
        payroll.MapPost("/{id:int}/entries", async (int id, PayrollEntryInput body, ClaimsPrincipal user, PayrollService svc) =>
            Results.Ok(new { id = await svc.AddEntry(id, body, Who(user)) }));
        payroll.MapDelete("/entries/{entryId:int}", async (int entryId, PayrollService svc) => { await svc.DeleteEntry(entryId); return Results.NoContent(); });
        payroll.MapGet("/attendance", async (string? month, PayrollService svc) => Results.Ok(await svc.Attendance(month)));
        payroll.MapPut("/attendance", async (AttendanceInput body, PayrollService svc) => { await svc.SetAttendance(body); return Results.NoContent(); });

        // ---- end-of-day cash count ----
        root.MapGet("/admin/closing", async (string? date, int? branchId, DayCloseService svc) => Results.Ok(await svc.View(date, branchId)))
            .RequireAuthorization(Policies.CanPay).RequiresPlan(r => r.FeatureBilling);
        root.MapPost("/admin/closing", async (DayCloseInput body, ClaimsPrincipal user, DayCloseService svc) =>
            Results.Ok(await svc.Close(body, Who(user)))).RequireAuthorization(Policies.CanPay).RequiresPlan(r => r.FeatureBilling);

        // ---- change every price by a percentage ----
        root.MapPost("/admin/menu/bulk-price", async (BulkPriceRequest body, MenuAdminService svc) =>
            Results.Ok(new { changed = await svc.BulkPrice(body) })).RequireAuthorization(Policies.CanManageMenu);
    }
}
