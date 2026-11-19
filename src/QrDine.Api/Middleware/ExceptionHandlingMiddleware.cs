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
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // The caller hung up (closed the tab, a probe's timeout): nobody is waiting for an answer and nothing broke.
            _log.LogInformation("{Method} {Path}: the caller went away before the answer", ctx.Request.Method, ctx.Request.Path);
            if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 499;
        }
        catch (DomainException ex)
        {
            _log.LogInformation("Rule rejected: {Message}", ex.Message);
            await Write(ctx, StatusCodes.Status400BadRequest, "Cannot complete action", ex.Message);
        }
        catch (Exception ex) when (Application.Common.DbErrors.IsValueTooBig(ex))
        {
            // A typing slip (an extra zero, a pasted paragraph), not a fault: say so instead of "something went wrong".
            _log.LogInformation("Value too big for the database: {Message}", ex.GetBaseException().Message);
            await Write(ctx, StatusCodes.Status400BadRequest, "Cannot complete action",
                "One of the amounts or texts is too big. Please check what you typed and try again.");
        }
        catch (Exception ex)
        {
            // A short reference the customer can quote to support; the same id is on the
            // logged exception so an operator can find the full stack trace. The user
            // never sees the technical detail — only the reference.
            var reference = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            _log.LogError(ex, "Unhandled error (ref {Reference})", reference);
            await Write(ctx, StatusCodes.Status500InternalServerError, "Something went wrong",
                "We couldn't complete your request right now. Please try again. If the problem continues, contact support.",
                reference);
        }
    }

    private static async Task Write(HttpContext ctx, int status, string title, string detail, string? reference = null)
    {
        if (ctx.Response.HasStarted) return;   // can't rewrite a response already on the wire
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new { type = "about:blank", title, status, detail, reference });
    }
}
