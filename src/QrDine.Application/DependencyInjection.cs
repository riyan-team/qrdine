using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using QrDine.Application.Auth;
using QrDine.Application.Billing;
using QrDine.Application.Menu;
using QrDine.Application.Ordering;

namespace QrDine.Application;

public static class DependencyInjection
{
    /// <summary>Registers the Application-layer use-case services.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<MenuService>();
        services.AddScoped<MenuAdminService>();
        services.AddScoped<Menu.MenuImportService>();
        services.AddScoped<Admin.RestaurantAdminService>();
        services.AddScoped<Admin.OnboardingService>();
        services.AddScoped<Inventory.InventoryService>();
        services.AddScoped<Platform.PlatformService>();
        services.AddScoped<OrderService>();
        services.AddScoped<AuthService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<BillingService>();
        services.AddScoped<FiscalSyncService>();
        services.AddScoped<Reports.ReportsService>();
        services.AddScoped<Feedback.FeedbackService>();
        services.AddScoped<Loyalty.LoyaltyService>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
