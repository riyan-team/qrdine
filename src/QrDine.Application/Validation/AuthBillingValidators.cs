using FluentValidation;
using QrDine.Application.Auth;
using QrDine.Application.Billing;

namespace QrDine.Application.Validation;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Enter your username.").MaximumLength(60);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.").MaximumLength(200);
    }
}

public class SettleRequestValidator : AbstractValidator<SettleRequest>
{
    public SettleRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0);
    }
}

public class PayPartRequestValidator : AbstractValidator<PayPartRequest>
{
    public PayPartRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
