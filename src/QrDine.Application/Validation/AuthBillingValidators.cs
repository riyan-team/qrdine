using FluentValidation;
using QrDine.Application.Auth;
using QrDine.Application.Billing;

namespace QrDine.Application.Validation;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Enter your mobile number or username.")
            .MaximumLength(60).WithMessage("That username is too long.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter your password.")
            .MaximumLength(200).WithMessage("That password is too long.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Enter your current password.")
            .MaximumLength(200).WithMessage("That password is too long.");
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("Enter a new password.")
            .MinimumLength(6).WithMessage("New password must be at least 6 characters.")
            .MaximumLength(200).WithMessage("New password is too long.");
    }
}

public class SettleRequestValidator : AbstractValidator<SettleRequest>
{
    public SettleRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().WithMessage("Choose how the bill was paid.")
            .MaximumLength(30).WithMessage("Payment method is too long.");
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0).WithMessage("Discount can't be negative.");
        RuleFor(x => x.AmountTendered).GreaterThanOrEqualTo(0).WithMessage("Amount received can't be negative.");
    }
}

public class PayPartRequestValidator : AbstractValidator<PayPartRequest>
{
    public PayPartRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().WithMessage("Choose how this part was paid.")
            .MaximumLength(30).WithMessage("Payment method is too long.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter an amount above 0.");
        RuleFor(x => x.Discount!.Value).GreaterThanOrEqualTo(0).When(x => x.Discount is not null)
            .WithMessage("Discount can't be negative.");
    }
}
