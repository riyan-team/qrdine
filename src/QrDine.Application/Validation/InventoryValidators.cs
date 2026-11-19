using FluentValidation;
using QrDine.Application.Inventory;

namespace QrDine.Application.Validation;

public class StockItemInputValidator : AbstractValidator<StockItemInput>
{
    public StockItemInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the item's name.")
            .MaximumLength(80).WithMessage("Name must be 80 characters or fewer.");
        RuleFor(x => x.Unit).NotEmpty().WithMessage("Enter the unit, like kg or pcs.")
            .MaximumLength(20).WithMessage("Unit must be 20 characters or fewer.");
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0).WithMessage("Quantity can't be negative.");
        RuleFor(x => x.LowThreshold).GreaterThanOrEqualTo(0).WithMessage("The low-stock alert can't be negative.");
    }
}

public class RestockInputValidator : AbstractValidator<RestockInput>
{
    public RestockInputValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Enter how much came in.");
        RuleFor(x => x.Reason).MaximumLength(200).WithMessage("Reason must be 200 characters or fewer.");
    }
}

public class AdjustInputValidator : AbstractValidator<AdjustInput>
{
    public AdjustInputValidator()
    {
        RuleFor(x => x.NewQuantity).GreaterThanOrEqualTo(0).WithMessage("The count can't be negative.");
        RuleFor(x => x.Reason).MaximumLength(200).WithMessage("Reason must be 200 characters or fewer.");
    }
}
