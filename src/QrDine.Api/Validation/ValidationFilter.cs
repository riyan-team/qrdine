using FluentValidation;

namespace QrDine.Api.Validation;

/// <summary>
/// A generic endpoint filter: for every argument passed to an endpoint, if a
/// FluentValidation validator is registered for its type, run it. On failure it
/// short-circuits with a 400 ValidationProblem (RFC-7807), before the handler runs.
/// </summary>
public class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var sp = ctx.HttpContext.RequestServices;
        foreach (var arg in ctx.Arguments)
        {
            if (arg is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(arg.GetType());
            if (sp.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(arg));
            if (!result.IsValid)
            {
                // Keys are camelCased so the SPA can map each error to its form field
                // (ownerUsername → the "Username" input). A friendly `detail` (the first
                // message) is included so any client that only reads `detail` still shows
                // a human sentence rather than a raw property name.
                var errors = result.Errors
                    .GroupBy(e => CamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                var detail = result.Errors.FirstOrDefault()?.ErrorMessage ?? "Please check the highlighted fields.";
                return Results.ValidationProblem(errors, detail: detail,
                    title: "Please check the highlighted fields.");
            }
        }
        return await next(ctx);
    }

    // "OwnerUsername" → "ownerUsername"; nested paths keep their segments ("Lines[0].Name").
    private static string CamelCase(string property)
    {
        if (string.IsNullOrEmpty(property)) return property;
        if (property.Length == 1) return property.ToLowerInvariant();
        return char.ToLowerInvariant(property[0]) + property.Substring(1);
    }
}
