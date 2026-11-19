using FluentValidation;
using QrDine.Application.Common;
using QrDine.Application.Expenses;
using QrDine.Application.Inventory;
using QrDine.Application.Purchasing;

namespace QrDine.Application.Validation;

public class SupplierInputValidator : AbstractValidator<SupplierInput>
{
    public SupplierInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the supplier's name.")
            .MaximumLength(120).WithMessage("Name must be 120 characters or fewer.");
        RuleFor(x => x.Phone).MaximumLength(30).WithMessage("Phone number is too long.")
            .Must(Phone.IsBlankOrValid).WithMessage(Phone.Message);
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Notes are too long.");
    }
}

public class SupplierPaymentInputValidator : AbstractValidator<SupplierPaymentInput>
{
    public SupplierPaymentInputValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter how much you paid.");
        RuleFor(x => x.Method).MaximumLength(30).WithMessage("Payment method is too long.");
        RuleFor(x => x.Reference).MaximumLength(60).WithMessage("Reference is too long.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
    }
}

/// <summary>One line on a supplier's bill or return.</summary>
public class PurchaseLineInputValidator : AbstractValidator<PurchaseLineInput>
{
    public PurchaseLineInputValidator()
    {
        RuleFor(x => x.Name).MaximumLength(120).WithMessage("Item name must be 120 characters or fewer.");
        RuleFor(x => x.Qty).GreaterThan(0).WithMessage("Quantity must be more than zero.");
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).WithMessage("Price can't be negative.");
    }
}

public class PurchaseInputValidator : AbstractValidator<PurchaseInput>
{
    public PurchaseInputValidator()
    {
        RuleFor(x => x.Reference).MaximumLength(60).WithMessage("Bill number is too long.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
        RuleFor(x => x.PaidMethod).MaximumLength(30).WithMessage("Payment method is too long.");
        RuleFor(x => x.Amount!.Value).GreaterThanOrEqualTo(0).When(x => x.Amount is not null).WithMessage("The bill amount can't be negative.");
        RuleFor(x => x.PaidNow!.Value).GreaterThanOrEqualTo(0).When(x => x.PaidNow is not null).WithMessage("The amount paid can't be negative.");
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(100).When(x => x.Lines != null).WithMessage("That's too many lines for one bill.");
        RuleForEach(x => x.Lines).SetValidator(new PurchaseLineInputValidator());
    }
}

public class SupplierReturnInputValidator : AbstractValidator<SupplierReturnInput>
{
    public SupplierReturnInputValidator()
    {
        RuleFor(x => x.Reference).MaximumLength(60).WithMessage("Reference is too long.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
        RuleFor(x => x.Amount!.Value).GreaterThanOrEqualTo(0).When(x => x.Amount is not null).WithMessage("The return amount can't be negative.");
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(100).When(x => x.Lines != null).WithMessage("That's too many lines for one return.");
        RuleForEach(x => x.Lines).SetValidator(new PurchaseLineInputValidator());
    }
}

public class ExpenseInputValidator : AbstractValidator<ExpenseInput>
{
    public ExpenseInputValidator()
    {
        RuleFor(x => x.Category).NotEmpty().WithMessage("Choose a category.")
            .MaximumLength(60).WithMessage("Category must be 60 characters or fewer.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter how much was spent.");
        RuleFor(x => x.PaymentMethod).MaximumLength(30).WithMessage("Payment method is too long.");
        RuleFor(x => x.PaidTo).MaximumLength(120).WithMessage("That name is too long.");
        RuleFor(x => x.Note).MaximumLength(300).WithMessage("Note is too long.");
    }
}

public class WasteInputValidator : AbstractValidator<WasteInput>
{
    public WasteInputValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Enter how much was wasted.");
        RuleFor(x => x.Reason).MaximumLength(200).WithMessage("Reason must be 200 characters or fewer.");
    }
}
