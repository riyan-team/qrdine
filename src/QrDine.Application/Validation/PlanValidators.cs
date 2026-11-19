using FluentValidation;
using QrDine.Application.Platform;

namespace QrDine.Application.Validation;

public class PlanInputValidator : AbstractValidator<PlanInput>
{
    public PlanInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the plan name.")
            .MaximumLength(60).WithMessage("Plan name must be 60 characters or fewer.");
        RuleFor(x => x.Description).MaximumLength(200).WithMessage("Description must be 200 characters or fewer.");
        RuleFor(x => x.PricePkr).GreaterThanOrEqualTo(0).WithMessage("The monthly price can't be negative.");
        RuleFor(x => x.SetupFeePkr).GreaterThanOrEqualTo(0).WithMessage("The setup fee can't be negative.");
    }
}

public class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => Enum.TryParse<Domain.Subscriptions.SubscriptionPaymentKind>(k, true, out _))
            .WithMessage("Choose setup fee or monthly.");
        RuleFor(x => x.Months).InclusiveBetween(1, 36)
            .When(x => string.Equals(x.Kind, "Monthly", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Months must be a whole number from 1 to 36.");
        RuleFor(x => x.AmountPkr).GreaterThan(0).WithMessage("Enter the amount received.");
        RuleFor(x => x.Note).MaximumLength(200).WithMessage("Note must be 200 characters or fewer.");
    }
}
