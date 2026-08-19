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
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.ValidationProblem(errors);
            }
        }
        return await next(ctx);
    }
}
