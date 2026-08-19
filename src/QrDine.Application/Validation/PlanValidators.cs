using FluentValidation;
using QrDine.Application.Platform;

namespace QrDine.Application.Validation;

public class PlanInputValidator : AbstractValidator<PlanInput>
{
    public PlanInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.PricePkr).GreaterThanOrEqualTo(0);
    }
}
