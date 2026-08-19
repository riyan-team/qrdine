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
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(60)
            .Matches("^[a-zA-Z0-9_.-]+$").WithMessage("Username: letters, numbers, . _ - only.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(4).MaximumLength(200);
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
    public ResetPasswordInputValidator() => RuleFor(x => x.Password).NotEmpty().MinimumLength(4).MaximumLength(200);
}

public class OnboardRequestValidator : AbstractValidator<OnboardRequest>
{
    public OnboardRequestValidator()
    {
        RuleFor(x => x.RestaurantName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).NotEmpty().MinimumLength(3).MaximumLength(60)
            .Matches("^[a-z0-9-]+$").WithMessage("Address: lowercase letters, numbers and dashes only.");
        RuleFor(x => x.OwnerDisplayName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.OwnerUsername).NotEmpty().MinimumLength(3).MaximumLength(60)
            .Matches("^[a-zA-Z0-9_.-]+$");
        RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(6).MaximumLength(200);
    }
}
