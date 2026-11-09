using QrDine.Domain.Common;

namespace QrDine.Domain.Expenses;

/// <summary>Money spent running the restaurant that isn't a supplier's bill: rent, salaries, bijli.</summary>
public class Expense : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int? BranchId { get; set; }

    /// <summary>The Pakistan calendar day this belongs to, at midnight (no time zone).</summary>
    public DateTime Date { get; set; }
    public string Category { get; set; } = "Other";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string PaidTo { get; set; } = "";
    public string Note { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static readonly string[] DefaultCategories =
    {
        "Rent", "Salaries", "Electricity", "Gas", "Water", "Internet & phone", "Maintenance",
        "Cleaning", "Transport", "Packaging", "Marketing", "Staff meals", "Other",
    };
}
