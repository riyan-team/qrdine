using FluentValidation;
using QrDine.Application.Admin;
using QrDine.Application.Menu;
using QrDine.Application.Menu.Dtos;

namespace QrDine.Application.Validation;

public class CategoryInputValidator : AbstractValidator<CategoryInput>
{
    public CategoryInputValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a category name.")
        .MaximumLength(60).WithMessage("Category name must be 60 characters or fewer.");
}

public class ItemInputValidator : AbstractValidator<ItemInput>
{
    public ItemInputValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Choose a category.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the dish name.")
            .MaximumLength(80).WithMessage("Dish name must be 80 characters or fewer.");
        RuleFor(x => x.NameUrdu).MaximumLength(80).WithMessage("Urdu name must be 80 characters or fewer.");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("Price can't be negative.")
            .LessThan(BillingRules.MaxAmount).WithMessage("That price is too big.");
        RuleFor(x => x.PrepMinutes).InclusiveBetween(1, 180).WithMessage("Prep time must be 1 to 180 minutes.");
        RuleFor(x => x.TaxRate!.Value).InclusiveBetween(0, 100).When(x => x.TaxRate is not null)
            .WithMessage(BillingRules.ItemTaxMessage);
        RuleFor(x => x.FurtherTaxRate).InclusiveBetween(0, 100).WithMessage(BillingRules.FurtherTaxMessage);
        RuleFor(x => x.PctCode).MaximumLength(12).WithMessage("PCT code must be 12 characters or fewer.");
        RuleFor(x => x.DealPrice!.Value).Must((x, dp) => MenuAdminService.DealPriceProblem(dp, x.Price) is null)
            .When(x => x.DealPrice is not null)
            .WithMessage(x => MenuAdminService.DealPriceProblem(x.DealPrice!.Value, x.Price)!);
        RuleFor(x => x.DealDescription).MaximumLength(200).WithMessage("Deal text must be 200 characters or fewer.");
        RuleFor(x => x.DealDays).MaximumLength(20).WithMessage("Choose the deal days again.");
        RuleFor(x => x.DealStartMinutes!.Value).InclusiveBetween(0, 1439).When(x => x.DealStartMinutes is not null)
            .WithMessage("Choose a start time for the deal.");
        RuleFor(x => x.DealEndMinutes!.Value).InclusiveBetween(0, 1439).When(x => x.DealEndMinutes is not null)
            .WithMessage("Choose an end time for the deal.");
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(i => i.Name).NotEmpty().WithMessage("Give each size a name.")
                .MaximumLength(40).WithMessage("Size name must be 40 characters or fewer.");
            v.RuleFor(i => i.Price).GreaterThanOrEqualTo(0).WithMessage("Size price can't be negative.")
                .LessThan(BillingRules.MaxAmount).WithMessage("That size price is too big.");
        });
        RuleForEach(x => x.ModifierGroups).ChildRules(g =>
        {
            g.RuleFor(i => i.Name).NotEmpty().WithMessage("Give each option group a name.")
                .MaximumLength(40).WithMessage("Group name must be 40 characters or fewer.");
            g.RuleFor(i => i.MaxSelect).GreaterThanOrEqualTo(1).WithMessage("Max picks must be 1 or more.");
            g.RuleFor(i => i.MinSelect).GreaterThanOrEqualTo(0).WithMessage("Min picks can't be negative.")
                .LessThanOrEqualTo(i => i.MaxSelect).WithMessage("Min picks can't be more than max picks.");
            g.RuleForEach(i => i.Options).ChildRules(o =>
            {
                o.RuleFor(i => i.Name).NotEmpty().WithMessage("Give each option a name.")
                    .MaximumLength(40).WithMessage("Option name must be 40 characters or fewer.");
                o.RuleFor(i => i.Price).GreaterThanOrEqualTo(0).WithMessage("Option price can't be negative.")
                    .LessThan(BillingRules.MaxAmount).WithMessage("That option price is too big.");
            });
        });
    }
}
