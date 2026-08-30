using FluentValidation;
using QrDine.Application.Admin;

namespace QrDine.Application.Validation;

public class TableInputValidator : AbstractValidator<TableInput>
{
    public TableInputValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
    }
}

public class BranchInputValidator : AbstractValidator<BranchInput>
{
    public BranchInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Address).MaximumLength(200);
    }
}

public class StaffInputValidator : AbstractValidator<StaffInput>
{
    public StaffInputValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Enter a username.")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Username must be 60 characters or fewer.")
            .Matches("^[a-zA-Z0-9_.-]+$").WithMessage("Username can only contain letters, numbers, dots, underscores and dashes.");
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter a display name.")
            .MaximumLength(60).WithMessage("Name must be 60 characters or fewer.");
        RuleFor(x => x.Role).NotEmpty().WithMessage("Choose a role.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter a password.")
            .MinimumLength(4).WithMessage("Password must be at least 4 characters.")
            .MaximumLength(200).WithMessage("Password is too long.");
    }
}

public class StaffUpdateInputValidator : AbstractValidator<StaffUpdateInput>
{
    public StaffUpdateInputValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Role).NotEmpty();
    }
}

public class ResetPasswordInputValidator : AbstractValidator<ResetPasswordInput>
{
    public ResetPasswordInputValidator() => RuleFor(x => x.Password).NotEmpty().WithMessage("Enter a new password.")
        .MinimumLength(4).WithMessage("Password must be at least 4 characters.").MaximumLength(200).WithMessage("Password is too long.");
}

public class OnboardRequestValidator : AbstractValidator<OnboardRequest>
{
    public OnboardRequestValidator()
    {
        RuleFor(x => x.RestaurantName).NotEmpty().WithMessage("Enter your restaurant name.")
            .MaximumLength(120).WithMessage("Restaurant name must be 120 characters or fewer.");
        RuleFor(x => x.Slug).NotEmpty().WithMessage("Choose an address for your menu link.")
            .MinimumLength(3).WithMessage("Address must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Address must be 60 characters or fewer.")
            .Matches("^[a-z0-9-]+$").WithMessage("Address can only contain lowercase letters, numbers and dashes.");
        RuleFor(x => x.OwnerDisplayName).NotEmpty().WithMessage("Enter your name.")
            .MaximumLength(60).WithMessage("Name must be 60 characters or fewer.");
        RuleFor(x => x.OwnerUsername).NotEmpty().WithMessage("Choose a username to sign in with.")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Username must be 60 characters or fewer.")
            .Matches("^[a-zA-Z0-9_.-]+$").WithMessage("Username can only contain letters, numbers, dots, underscores and dashes (no spaces).");
        RuleFor(x => x.OwnerPassword).NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.")
            .MaximumLength(200).WithMessage("Password is too long.");
    }
}
