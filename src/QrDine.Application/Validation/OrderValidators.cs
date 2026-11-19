using FluentValidation;
using QrDine.Application.Ordering.Dtos;

namespace QrDine.Application.Validation;

public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.TableCode).NotEmpty().WithMessage("Choose a table.")
            .MaximumLength(20).WithMessage("Table code is too long.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Your order is empty.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(100).When(x => x.Items != null).WithMessage("That's too many lines for one order.");
        RuleForEach(x => x.Items).SetValidator(new OrderLineRequestValidator());
        RuleFor(x => x.Note).MaximumLength(200).WithMessage("The note must be 200 characters or fewer.");
    }
}

public class CookRequestValidator : AbstractValidator<CookRequest>
{
    public CookRequestValidator() =>
        RuleFor(x => x.EtaMinutes).InclusiveBetween(1, 180).WithMessage("Cooking time must be 1 to 180 minutes.");
}

public class AssignRequestValidator : AbstractValidator<AssignRequest>
{
    public AssignRequestValidator() =>
        RuleFor(x => x.WaiterName).NotEmpty().WithMessage("Choose a waiter.")
            .MaximumLength(60).WithMessage("Waiter's name is too long.");
}
