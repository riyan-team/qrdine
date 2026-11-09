using QrDine.Domain.Common;

namespace QrDine.Domain.Identity;

/// <summary>
/// The catalogue of things a staff member can be allowed to do. The owner grants
/// these per role, so "what each role can do" is configurable, not hard-coded.
/// </summary>
public static class Permissions
{
    public const string OrdersCreate = "orders.create";  // take an order on a table's behalf (waiter / reception)
    public const string OrdersApprove = "orders.approve";
    public const string OrdersCook = "orders.cook";
    public const string OrdersServe = "orders.serve";
    public const string OrdersPay = "orders.pay";
    public const string OrdersCancel = "orders.cancel";
    public const string MenuManage = "menu.manage";
    public const string InventoryManage = "inventory.manage";
    public const string SettingsManage = "settings.manage";
    public const string ReportsView = "reports.view";
    public const string InvoiceSync = "invoice.sync";   // file/retry an invoice with FBR
    public const string ExpensesManage = "expenses.manage";
    public const string SuppliersManage = "suppliers.manage";
    public const string OrdersDiscount = "orders.discount";    // change the discount on a bill
    public const string CustomersManage = "customers.manage";  // customer khata
    public const string PayrollManage = "payroll.manage";      // salaries, advances, attendance

    public static readonly string[] All =
    {
        OrdersCreate, OrdersApprove, OrdersCook, OrdersServe, OrdersPay, OrdersCancel,
        MenuManage, InventoryManage, SettingsManage, ReportsView, InvoiceSync, ExpensesManage, SuppliersManage,
        OrdersDiscount, CustomersManage, PayrollManage,
    };

    /// <summary>Sensible starting grants for a role (matches the original fixed rules).</summary>
    public static string[] DefaultsFor(StaffRole role) => role switch
    {
        StaffRole.Owner or StaffRole.Manager => All,
        StaffRole.Cashier => new[] { OrdersCreate, OrdersApprove, OrdersPay, InvoiceSync, OrdersDiscount, CustomersManage },
        StaffRole.Kitchen => new[] { OrdersCook },
        StaffRole.Waiter => new[] { OrdersCreate, OrdersServe },
        _ => Array.Empty<string>(),
    };
}

/// <summary>One granted permission for a role within a restaurant.</summary>
public class RolePermission : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public StaffRole Role { get; set; }
    public string Permission { get; set; } = "";
}
