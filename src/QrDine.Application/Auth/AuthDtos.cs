namespace QrDine.Application.Auth;

public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// Which modules the restaurant's plan (and the FBR gate) enable. Drives the
// frontend nav so staff only see what the subscription actually includes.
public record FeaturesDto(bool Ordering, bool Kitchen, bool Waiter, bool Billing,
    bool Inventory, bool Reports, bool Fbr);

public record AuthResponse(string Token, string DisplayName, string Role,
    IReadOnlyList<string> Permissions, FeaturesDto? Features = null, string RestaurantName = "");

public record GuestSessionResponse(string Token, string TableCode);
