using FluentValidation;
using QrDine.Application.Admin;
using QrDine.Application.Common;

namespace QrDine.Application.Validation;

public class TableInputValidator : AbstractValidator<TableInput>
{
    public TableInputValidator()
    {
        // The code goes into the table's QR link, so keep it to letters, numbers, dashes and underscores.
        RuleFor(x => x.Code).NotEmpty().WithMessage("Enter a table code, like T1.")
            .MaximumLength(20).WithMessage("Table code must be 20 characters or fewer.")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Table code can only have letters, numbers, dashes and underscores. No spaces.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a table name.")
            .MaximumLength(60).WithMessage("Table name must be 60 characters or fewer.");
    }
}

public class BranchInputValidator : AbstractValidator<BranchInput>
{
    public BranchInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the branch name.")
            .MaximumLength(80).WithMessage("Branch name must be 80 characters or fewer.");
        RuleFor(x => x.Address).MaximumLength(200).WithMessage("Address must be 200 characters or fewer.");
        RuleFor(x => x.FbrMode).Must(m => string.IsNullOrWhiteSpace(m) || Enum.TryParse<Domain.Restaurants.FbrMode>(m, true, out _))
            .WithMessage("Choose how this branch connects to FBR.");
        RuleFor(x => x.FbrPosId).GreaterThan(0).When(x => x.FbrPosId is not null).WithMessage("POS ID must be a number above 0.");
        RuleFor(x => x.FbrToken).MaximumLength(200).WithMessage("FBR token must be 200 characters or fewer.");
        RuleFor(x => x.FbrEndpointOverride).MaximumLength(200).WithMessage("FBR address must be 200 characters or fewer.")
            .Must(BeWebAddress).When(x => !string.IsNullOrWhiteSpace(x.FbrEndpointOverride))
            .WithMessage("FBR address must start with http:// or https://.");
        RuleFor(x => x.FbrStaticIp).MaximumLength(45).WithMessage("Static IP is too long.")
            .Must(ip => System.Net.IPAddress.TryParse(ip!.Trim(), out _)).When(x => !string.IsNullOrWhiteSpace(x.FbrStaticIp))
            .WithMessage("Enter the static IP like 203.0.113.10.");
    }

    private static bool BeWebAddress(string? s) =>
        Uri.TryCreate(s!.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}

/// <summary>Staff sign in with a mobile number or a short username, so both spellings are allowed.</summary>
internal static class StaffUsername
{
    public const string Message = "Use a mobile number, or letters, numbers, dots, dashes and underscores (no spaces).";
    private static readonly System.Text.RegularExpressions.Regex Plain = new("^[a-zA-Z0-9_.-]+$");
    public static bool IsValid(string? u) => !string.IsNullOrWhiteSpace(u) && (Plain.IsMatch(u.Trim()) || Phone.IsValid(u));
}

public class StaffInputValidator : AbstractValidator<StaffInput>
{
    public StaffInputValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Enter a username.")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Username must be 60 characters or fewer.")
            .Must(StaffUsername.IsValid).WithMessage(StaffUsername.Message);
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter a display name.")
            .MaximumLength(60).WithMessage("Name must be 60 characters or fewer.");
        RuleFor(x => x.Role).NotEmpty().WithMessage("Choose a role.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter a password.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.")
            .MaximumLength(200).WithMessage("Password is too long.");
        RuleFor(x => x.FbrPosId).GreaterThan(0).When(x => x.FbrPosId is not null).WithMessage("POS ID must be a number above 0.");
        RuleFor(x => x.FbrToken).MaximumLength(200).WithMessage("FBR token must be 200 characters or fewer.");
    }
}

public class StaffUpdateInputValidator : AbstractValidator<StaffUpdateInput>
{
    public StaffUpdateInputValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter a display name.")
            .MaximumLength(60).WithMessage("Name must be 60 characters or fewer.");
        RuleFor(x => x.Role).NotEmpty().WithMessage("Choose a role.");
        // Leaving the username out keeps the old one; anything sent follows the same rules as a new one.
        RuleFor(x => x.Username!).MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Username must be 60 characters or fewer.")
            .Must(StaffUsername.IsValid).WithMessage(StaffUsername.Message)
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
        RuleFor(x => x.FbrPosId).GreaterThan(0).When(x => x.FbrPosId is not null).WithMessage("POS ID must be a number above 0.");
        RuleFor(x => x.FbrToken).MaximumLength(200).WithMessage("FBR token must be 200 characters or fewer.");
    }
}

public class ResetPasswordInputValidator : AbstractValidator<ResetPasswordInput>
{
    public ResetPasswordInputValidator() => RuleFor(x => x.Password).NotEmpty().WithMessage("Enter a new password.")
        .MinimumLength(6).WithMessage("Password must be at least 6 characters.").MaximumLength(200).WithMessage("Password is too long.");
}

public class BillingSettingsInputValidator : AbstractValidator<BillingSettingsInput>
{
    public BillingSettingsInputValidator()
    {
        RuleFor(x => x.TaxName).MaximumLength(40).WithMessage("Tax name must be 40 characters or fewer.");
        RuleFor(x => x.TaxPercent).InclusiveBetween(0, 100).WithMessage(BillingRules.TaxMessage);
        RuleFor(x => x.TaxPercentCard!.Value).InclusiveBetween(0, 100).When(x => x.TaxPercentCard is not null)
            .WithMessage(BillingRules.CardTaxMessage);
        RuleFor(x => x.ServiceChargePercent).InclusiveBetween(0, 100).WithMessage(BillingRules.ServiceMessage);
        RuleFor(x => x.DiscountValue).GreaterThanOrEqualTo(0).WithMessage(BillingRules.DiscountNegativeMessage);
        RuleFor(x => x.DiscountValue).LessThanOrEqualTo(100).When(x => x.DiscountIsPercent)
            .WithMessage(BillingRules.DiscountPercentMessage);
        RuleFor(x => x.DiscountValue).LessThan(BillingRules.MaxAmount).When(x => !x.DiscountIsPercent)
            .WithMessage(BillingRules.DiscountTooBigMessage);
    }
}

public class FbrSettingsInputValidator : AbstractValidator<FbrSettingsInput>
{
    public FbrSettingsInputValidator()
    {
        RuleFor(x => x.BusinessName).MaximumLength(150).WithMessage("Business name must be 150 characters or fewer.");
        RuleFor(x => x.Ntn).MaximumLength(15).WithMessage("NTN must be 15 characters or fewer.")
            .Matches("^[0-9 -]*$").WithMessage("NTN can only have numbers and dashes.");
        RuleFor(x => x.Strn).MaximumLength(20).WithMessage("STRN must be 20 characters or fewer.")
            .Matches("^[0-9 -]*$").WithMessage("STRN can only have numbers and dashes.");
    }
}

public class ServiceSettingsInputValidator : AbstractValidator<ServiceSettingsInput>
{
    public ServiceSettingsInputValidator()
    {
        RuleFor(x => x.DeliveryFee!.Value).InclusiveBetween(0, 100000).When(x => x.DeliveryFee is not null)
            .WithMessage("The delivery charge must be between 0 and 100,000.");
        RuleFor(x => x.DeliveryRadiusKm!.Value).InclusiveBetween(0, 100).When(x => x.DeliveryRadiusKm is not null)
            .WithMessage("The delivery area must be between 0 and 100 km (0 means no limit).");
        RuleFor(x => x.DeliveryNote).Must(n => n!.Trim().Length <= 120).When(x => x.DeliveryNote is not null)
            .WithMessage("Keep the delivery note under 120 characters.");
    }
}

public class ReceiptSettingsInputValidator : AbstractValidator<ReceiptSettingsInput>
{
    public ReceiptSettingsInputValidator()
    {
        RuleFor(x => x.ReceiptHeader).MaximumLength(500).WithMessage("The receipt header must be 500 characters or fewer.");
        RuleFor(x => x.ReceiptFooter).MaximumLength(500).WithMessage("The receipt footer must be 500 characters or fewer.");
    }
}

public class OnboardRequestValidator : AbstractValidator<OnboardRequest>
{
    public OnboardRequestValidator()
    {
        RuleFor(x => x.RestaurantName).NotEmpty().WithMessage("Enter your restaurant name.")
            .MaximumLength(120).WithMessage("Restaurant name must be 120 characters or fewer.");
        RuleFor(x => x.Slug!).MinimumLength(3).WithMessage("Address must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Address must be 60 characters or fewer.")
            .Matches("^[a-z0-9-]+$").WithMessage("Address can only contain lowercase letters, numbers and dashes.")
            .When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.OwnerDisplayName).NotEmpty().WithMessage("Enter your name.")
            .MaximumLength(60).WithMessage("Name must be 60 characters or fewer.");
        RuleFor(x => x.OwnerUsername).NotEmpty().WithMessage("Enter your mobile number.")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(60).WithMessage("Username must be 60 characters or fewer.")
            .Matches("^[a-zA-Z0-9_.+() -]+$").WithMessage("Use only letters, numbers, dots, underscores and dashes.");
        RuleFor(x => x.OwnerPassword).NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.")
            .MaximumLength(200).WithMessage("Password is too long.");
    }
}
