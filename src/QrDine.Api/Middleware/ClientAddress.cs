using QrDine.Application.Auth;

namespace QrDine.Api.Middleware;

/// <summary>
/// The address a request comes from, as the limits count it. A home or phone network gets a whole /64 block of IPv6
/// addresses, so the block counts as one visitor; otherwise a guesser could switch address on every try.
/// </summary>
public static class ClientAddress
{
    public static string Of(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }
}

/// <summary>Tells the sign-in guard where the current request comes from.</summary>
public sealed class HttpRequestSource(IHttpContextAccessor http) : IRequestSource
{
    public string? Current => http.HttpContext is HttpContext ctx ? ClientAddress.Of(ctx) : null;
}
