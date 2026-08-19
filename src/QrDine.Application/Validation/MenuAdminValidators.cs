using FluentValidation;
using QrDine.Application.Menu.Dtos;

namespace QrDine.Application.Validation;

public class CategoryInputValidator : AbstractValidator<CategoryInput>
{
    public CategoryInputValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
}

public class ItemInputValidator : AbstractValidator<ItemInput>
{
    public ItemInputValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PrepMinutes).InclusiveBetween(1, 180);
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(i => i.Name).NotEmpty().MaximumLength(40);
            v.RuleFor(i => i.Price).GreaterThanOrEqualTo(0);
        });
        RuleForEach(x => x.ModifierGroups).ChildRules(g =>
        {
            g.RuleFor(i => i.Name).NotEmpty().MaximumLength(40);
            g.RuleFor(i => i.MaxSelect).GreaterThanOrEqualTo(1);
            g.RuleFor(i => i.MinSelect).GreaterThanOrEqualTo(0).LessThanOrEqualTo(i => i.MaxSelect)
                .WithMessage("Min selections cannot exceed max.");
        });
    }
}
