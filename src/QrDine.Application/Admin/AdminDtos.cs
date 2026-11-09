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
// Username is optional on update — supply it to rename the sign-in username (validated
// for format + uniqueness); leave null to keep the existing one.
public record StaffUpdateInput(string DisplayName, string Role,
    long? FbrPosId = null, string? FbrToken = null, string? Username = null);
public record ResetPasswordInput(string Password);
public record StaffDto(int Id, string Username, string DisplayName, string Role,
    long? FbrPosId, bool HasFbrToken);

// Receipt branding (logo + printed header/footer). Logo is uploaded separately and
// its URL echoed back here; sending LogoUrl="" on save clears it.
public record ReceiptSettingsDto(string LogoUrl, string ReceiptHeader, string ReceiptFooter, string RestaurantName);
public record ReceiptSettingsInput(string ReceiptHeader, string ReceiptFooter, string? LogoUrl = null);

// FBR restaurant-level settings (owner enable + seller identity)
public record FbrSettingsDto(bool FeatureFbr, bool FbrEnabled, string BusinessName, string Ntn, string Strn);
public record FbrSettingsInput(bool FbrEnabled, string BusinessName, string Ntn, string Strn);

// Front-of-house service settings (currently: how waiters get assigned to tables).
public record ServiceSettingsDto(bool WaiterAutoAssign);
public record ServiceSettingsInput(bool WaiterAutoAssign);

// Billing settings: Tax/GST, discount, receipt and stock behaviour. Owner-managed.
public record BillingSettingsDto(
    bool TaxEnabled, string TaxName, decimal TaxPercent, decimal ServiceChargePercent,
    bool DiscountEnabled, bool DiscountIsPercent, decimal DiscountValue,
    bool ReceiptEnabled, bool StockTrackingEnabled, string Currency, decimal? TaxPercentCard = null);
public record BillingSettingsInput(
    bool TaxEnabled, string TaxName, decimal TaxPercent, decimal ServiceChargePercent,
    bool DiscountEnabled, bool DiscountIsPercent, decimal DiscountValue,
    bool ReceiptEnabled, bool StockTrackingEnabled, decimal? TaxPercentCard = null);

// Onboarding
public record OnboardRequest(string RestaurantName, string? Slug, string OwnerDisplayName, string OwnerUsername, string OwnerPassword, int? PlanId = null);
