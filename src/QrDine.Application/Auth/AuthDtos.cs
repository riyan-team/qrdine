namespace QrDine.Application.Auth;

public record LoginRequest(string Username, string Password);
public record AuthResponse(string Token, string DisplayName, string Role, IReadOnlyList<string> Permissions);

public record GuestSessionResponse(string Token, string TableCode);
