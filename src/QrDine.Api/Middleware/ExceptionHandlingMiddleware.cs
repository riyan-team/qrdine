using QrDine.Domain.Common;

namespace QrDine.Api.Middleware;

/// <summary>
/// Turns a broken business rule (DomainException) into a clean 400 with a
/// ProblemDetails body, instead of a raw 500. Everything else is logged and
/// returned as a generic 500 without leaking internals.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log)
    {
        _next = next; _log = log;
    }

    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (DomainException ex)
        {
            _log.LogInformation("Rule rejected: {Message}", ex.Message);
            await Write(ctx, StatusCodes.Status400BadRequest, "Cannot complete action", ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled error");
            await Write(ctx, StatusCodes.Status500InternalServerError, "Server error", "Something went wrong.");
        }
    }

    private static async Task Write(HttpContext ctx, int status, string title, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new { type = "about:blank", title, status, detail });
    }
}
