using QrDine.Domain.Common;

namespace QrDine.Domain.Staffing;

/// <summary>A person on the payroll. They don't need a login: most kitchen helpers never have one.</summary>
public class Employee : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Designation { get; set; } = "";
    public decimal MonthlySalary { get; set; }
    public DateTime? JoinedOn { get; set; }
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum EmployeeEntryType { Advance, Salary, Bonus, Deduction }

/// <summary>
/// A payment to, or deduction from, someone's pay for a month. Money that actually leaves the
/// till also writes an Expense, so salaries show in profit and in the day's cash without being
/// entered twice.
/// </summary>
public class EmployeeEntry : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int EmployeeId { get; set; }
    public EmployeeEntryType Type { get; set; }
    public DateTime Date { get; set; }

    /// <summary>The month this counts against, as yyyy-MM.</summary>
    public string Month { get; set; } = "";
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string Note { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public int? ExpenseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool PaysOutCash => Type is not EmployeeEntryType.Deduction;
}

/// <summary>One person, one day. No row means nobody has marked that day yet.</summary>
public class AttendanceMark : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int EmployeeId { get; set; }
    public DateTime Date { get; set; }

    /// <summary>Present, Absent, Leave or HalfDay.</summary>
    public string Status { get; set; } = "Present";
}
