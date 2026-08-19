namespace QrDine.Application.Admin;

// Branches (Fbr* fields are the branch's FBR POS registration; optional)
public record BranchInput(string Name, string Address, bool IsActive,
    string FbrMode = "Local", bool FbrSandbox = true, long? FbrPosId = null,
    string? FbrToken = null, string? FbrEndpointOverride = null, string? FbrStaticIp = null);
public record BranchDto(int Id, string Name, string Address, bool IsActive, int Tables,
    string FbrMode, bool FbrSandbox, long? FbrPosId, string? FbrToken, string? FbrEndpointOverride, string FbrStaticIp);

// Tables
public record TableInput(string Code, string Name, int BranchId);
public record TableAdminDto(int Id, string Code, string Name, int BranchId, string BranchName);

// Staff (FbrPosId/FbrToken assigned at creation for POS operators)
public record StaffInput(string Username, string DisplayName, string Role, string Password,
    long? FbrPosId = null, string? FbrToken = null);
public record StaffUpdateInput(string DisplayName, string Role,
    long? FbrPosId = null, string? FbrToken = null);
public record ResetPasswordInput(string Password);
public record StaffDto(int Id, string Username, string DisplayName, string Role,
    long? FbrPosId, bool HasFbrToken);

// FBR restaurant-level settings (owner enable + seller identity)
public record FbrSettingsDto(bool FeatureFbr, bool FbrEnabled, string BusinessName, string Ntn, string Strn);
public record FbrSettingsInput(bool FbrEnabled, string BusinessName, string Ntn, string Strn);

// Onboarding
public record OnboardRequest(string RestaurantName, string Slug, string OwnerDisplayName, string OwnerUsername, string OwnerPassword, int? PlanId = null);
