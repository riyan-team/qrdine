using FluentValidation;
using QrDine.Application.Inventory;

namespace QrDine.Application.Validation;

public class StockItemInputValidator : AbstractValidator<StockItemInput>
{
    public StockItemInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowThreshold).GreaterThanOrEqualTo(0);
    }
}

public class RestockInputValidator : AbstractValidator<RestockInput>
{
    public RestockInputValidator() => RuleFor(x => x.Quantity).GreaterThan(0);
}

public class AdjustInputValidator : AbstractValidator<AdjustInput>
{
    public AdjustInputValidator() => RuleFor(x => x.NewQuantity).GreaterThanOrEqualTo(0);
}
