using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QrDine.Application.Common;
using QrDine.Domain.Identity;

namespace QrDine.Api.Auth;

/// <summary>Authorization policy names used to gate endpoints by role.</summary>
public static class Policies
{
    // Each maps to a configurable permission (see Domain.Permissions).
    public const string StaffAny = "StaffAny";
    public const string CanApprove = "CanApprove";
    public const string CanCook = "CanCook";
    public const string CanServe = "CanServe";
    public const string CanPay = "CanPay";
    public const string CanCancel = "CanCancel";
    public const string CanManageMenu = "CanManageMenu";
    public const string CanManageInventory = "CanManageInventory";
    public const string CanManageSettings = "CanManageSettings";
    public const string CanViewReports = "CanViewReports";
    public const string CanSyncInvoice = "CanSyncInvoice";   // file/retry with FBR
    public const string CanManageExpenses = "CanManageExpenses";
    public const string CanManageSuppliers = "CanManageSuppliers";
    public const string CanCreateOrders = "CanCreateOrders";
    public const string CanManageCustomers = "CanManageCustomers";
    public const string CanManagePayroll = "CanManagePayroll";
    public const string OwnerOnly = "OwnerOnly";       // edit permissions
    public const string GuestOrStaff = "GuestOrStaff";
    public const string Platform = "Platform";
}

public static class AuthSetup
{
    public static IServiceCollection AddQrDineAuth(this IServiceCollection services, IConfiguration config)
    {
        var secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        var issuer = config["Jwt:Issuer"] ?? "qrdine";
        var audience = config["Jwt:Audience"] ?? "qrdine";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    // Must match JwtTokenService: derive a 32-byte key so any secret length works.
                    IssuerSigningKey = new SymmetricSecurityKey(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                // Let SignalR pass the token in the query string on the WS handshake.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var access = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(access) && ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                            ctx.Token = access;
                        return Task.CompletedTask;
                    }
                };
            });

        bool IsStaff(System.Security.Claims.ClaimsPrincipal u) =>
            u.FindFirst(AppClaims.TokenType)?.Value == AppClaims.TypeStaff;

        // A policy that requires a specific permission claim (granted per role by the owner).
        void Perm(Microsoft.AspNetCore.Authorization.AuthorizationOptions o, string policy, string permission)
            => o.AddPolicy(policy, p => p.RequireClaim(AppClaims.Permission, permission));

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Policies.StaffAny, p => p.RequireAssertion(c => IsStaff(c.User)));
            Perm(o, Policies.CanApprove, Permissions.OrdersApprove);
            Perm(o, Policies.CanCook, Permissions.OrdersCook);
            Perm(o, Policies.CanServe, Permissions.OrdersServe);
            Perm(o, Policies.CanPay, Permissions.OrdersPay);
            Perm(o, Policies.CanCancel, Permissions.OrdersCancel);
            Perm(o, Policies.CanManageMenu, Permissions.MenuManage);
            Perm(o, Policies.CanManageInventory, Permissions.InventoryManage);
            Perm(o, Policies.CanManageSettings, Permissions.SettingsManage);
            Perm(o, Policies.CanViewReports, Permissions.ReportsView);
            Perm(o, Policies.CanSyncInvoice, Permissions.InvoiceSync);
            Perm(o, Policies.CanManageExpenses, Permissions.ExpensesManage);
            Perm(o, Policies.CanManageSuppliers, Permissions.SuppliersManage);
            Perm(o, Policies.CanCreateOrders, Permissions.OrdersCreate);
            Perm(o, Policies.CanManageCustomers, Permissions.CustomersManage);
            Perm(o, Policies.CanManagePayroll, Permissions.PayrollManage);
            // Editing permissions is owner-only, so a manager can't grant themselves more.
            o.AddPolicy(Policies.OwnerOnly, p => p.RequireAssertion(c => IsStaff(c.User)).RequireRole("Owner"));
            // Placing an order: a valid guest token OR any staff member (waiter on behalf).
            o.AddPolicy(Policies.GuestOrStaff, p => p.RequireAuthenticatedUser());
            // The SaaS operator control plane.
            o.AddPolicy(Policies.Platform, p => p.RequireAssertion(c =>
                c.User.FindFirst(AppClaims.TokenType)?.Value == AppClaims.TypePlatform));
        });

        return services;
    }
}
