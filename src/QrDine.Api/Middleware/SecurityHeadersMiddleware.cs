using System.Text.RegularExpressions;

namespace QrDine.Api.Middleware;

/// <summary>
/// Browser protections sent with every answer:
/// - Content-Security-Policy: scripts only from this site, so injected text can never run as code;
///   no other site may show this app in a frame (stops click-jacking a cashier into a refund).
/// - nosniff, a strict referrer, and no camera, microphone or location for the pages.
/// - HSTS: browsers only ever use https for this address.
/// Inline styles stay allowed: the printed sheets and React's style props use them.
/// </summary>
public partial class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    [GeneratedRegex("^[A-Za-z0-9.-]+(:[0-9]{1,5})?$")]
    private static partial Regex SafeHost();

    public Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        // The live updates use a websocket to this same address.
        var host = ctx.Request.Host.Value ?? "";
        var sockets = SafeHost().IsMatch(host) ? $" wss://{host} ws://{host}" : "";
        h["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; font-src 'self' data:; " +
            $"connect-src 'self'{sockets}; manifest-src 'self'; worker-src 'self' blob:; " +
            "object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
        h["Strict-Transport-Security"] = "max-age=31536000";
        return _next(ctx);
    }
}
