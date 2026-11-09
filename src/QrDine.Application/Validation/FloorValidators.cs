using FluentValidation;
using QrDine.Application.Billing;
using QrDine.Application.Customers;
using QrDine.Application.Ordering;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Staffing;

namespace QrDine.Application.Validation;

public class TicketRequestValidator : AbstractValidator<TicketRequest>
{
    public TicketRequestValidator()
    {
        RuleFor(x => x.CustomerName).MaximumLength(120).WithMessage("Name is too long.");
        RuleFor(x => x.CustomerPhone).MaximumLength(30).WithMessage("Phone number is too long.");
        RuleFor(x => x.Address).MaximumLength(300).WithMessage("Address is too long.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add something to the order first.");
    }
}

public class TicketUpdateValidator : AbstractValidator<TicketUpdate>
{
    public TicketUpdateValidator()
    {
        RuleFor(x => x.CustomerName).MaximumLength(120).WithMessage("Name is too long.");
        RuleFor(x => x.CustomerPhone).MaximumLength(30).WithMessage("Phone number is too long.");
        RuleFor(x => x.Address).MaximumLength(300).WithMessage("Address is too long.");
        RuleFor(x => x.RiderName).MaximumLength(80).WithMessage("Rider's name is too long.");
    }
}

public class CustomerInputValidator : AbstractValidator<CustomerInput>
{
    public CustomerInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the customer's name.")
            .MaximumLength(120).WithMessage("Name must be 120 characters or fewer.");
        RuleFor(x => x.Phone).MaximumLength(30).WithMessage("Phone number is too long.");
        RuleFor(x => x.Address).MaximumLength(300).WithMessage("Address is too long.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Notes are too long.");
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0).WithMessage("The khata limit can't be negative.");
    }
}

public class CustomerPaymentInputValidator : AbstractValidator<CustomerPaymentInput>
{
    public CustomerPaymentInputValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter how much they paid.");
        RuleFor(x => x.Method).MaximumLength(30);
        RuleFor(x => x.Reference).MaximumLength(60).WithMessage("Reference is too long.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
    }
}

public class EmployeeInputValidator : AbstractValidator<EmployeeInput>
{
    public EmployeeInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the person's name.")
            .MaximumLength(120).WithMessage("Name must be 120 characters or fewer.");
        RuleFor(x => x.Phone).MaximumLength(30).WithMessage("Phone number is too long.");
        RuleFor(x => x.Designation).MaximumLength(60).WithMessage("Job title is too long.");
        RuleFor(x => x.Notes).MaximumLength(300).WithMessage("Notes are too long.");
        RuleFor(x => x.MonthlySalary).GreaterThanOrEqualTo(0).WithMessage("Salary can't be negative.");
    }
}

public class PayrollEntryInputValidator : AbstractValidator<PayrollEntryInput>
{
    public PayrollEntryInputValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter an amount greater than zero.");
        RuleFor(x => x.Method).MaximumLength(30);
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
    }
}

public class DayCloseInputValidator : AbstractValidator<DayCloseInput>
{
    public DayCloseInputValidator()
    {
        RuleFor(x => x.OpeningCash).GreaterThanOrEqualTo(0).WithMessage("Opening cash can't be negative.");
        RuleFor(x => x.CountedCash).GreaterThanOrEqualTo(0).WithMessage("Counted cash can't be negative.");
        RuleFor(x => x.TakenOut).GreaterThanOrEqualTo(0).WithMessage("Cash taken out can't be negative.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
    }
}

public class VoidItemRequestValidator : AbstractValidator<VoidItemRequest>
{
    public VoidItemRequestValidator()
    {
        RuleFor(x => x.Qty).GreaterThan(0).WithMessage("Choose how many to remove.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Say why this is being removed.")
            .MaximumLength(200).WithMessage("Reason is too long.");
    }
}

public class BulkPriceRequestValidator : AbstractValidator<Menu.Dtos.BulkPriceRequest>
{
    public BulkPriceRequestValidator()
    {
        RuleFor(x => x.Percent).InclusiveBetween(-90, 300).WithMessage("Enter a percentage between -90 and 300.");
        RuleFor(x => x.RoundTo).InclusiveBetween(0, 1000).WithMessage("Rounding must be between 0 and 1000.");
    }
}
