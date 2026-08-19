using FluentValidation;
using QrDine.Application.Ordering.Dtos;

namespace QrDine.Application.Validation;

public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.TableCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Your order is empty.");
        RuleForEach(x => x.Items).ChildRules(line =>
        {
            line.RuleFor(i => i.MenuItemId).GreaterThan(0);
            line.RuleFor(i => i.Qty).InclusiveBetween(1, 50);
        });
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public class CookRequestValidator : AbstractValidator<CookRequest>
{
    public CookRequestValidator() =>
        RuleFor(x => x.EtaMinutes).InclusiveBetween(1, 180);
}

public class AssignRequestValidator : AbstractValidator<AssignRequest>
{
    public AssignRequestValidator() =>
        RuleFor(x => x.WaiterName).NotEmpty().MaximumLength(60);
}
