using QrDine.Domain.Common;

namespace QrDine.Domain.Customers;

/// <summary>
/// Someone the restaurant knows: a delivery regular, or an office that eats on khata. What they
/// owe is never stored, only worked out from the opening balance and their entries, so it can't drift.
/// </summary>
public class Customer : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public decimal OpeningBalance { get; set; }

    /// <summary>The most this customer may owe. Zero means no limit.</summary>
    public decimal CreditLimit { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum CustomerEntryType { Sale, Payment, Adjustment }

public class CustomerEntry : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int CustomerId { get; set; }
    public CustomerEntryType Type { get; set; }

    /// <summary>The Pakistan calendar day this belongs to, at midnight.</summary>
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public int? InvoiceId { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Note { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>What this entry does to what the customer owes.</summary>
    public decimal BalanceEffect => Type switch
    {
        CustomerEntryType.Sale => Amount,
        CustomerEntryType.Payment => -Amount,
        _ => Amount,
    };
}
