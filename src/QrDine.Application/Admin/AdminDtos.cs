namespace QrDine.Application.Admin;

// Branches. The tax office fields are the branch's POS registration with FBR or PRA (all optional).
// FbrToken: null leaves the saved token as it is, "" removes it. The saved token is never sent back to a screen.
// FiscalAuthority/FiscalEnvironment: null leaves them as they are (a screen that doesn't know them yet).
public record BranchInput(string Name, string Address, bool IsActive,
    string? FbrMode = null, bool FbrSandbox = true, long? FbrPosId = null,
    string? FbrToken = null, string? FbrEndpointOverride = null, string? FbrStaticIp = null,
    string? FiscalAuthority = null, string? FiscalEnvironment = null, bool KeepFiscal = false);
public record BranchDto(int Id, string Name, string Address, bool IsActive, int Tables,
    string FbrMode, bool FbrSandbox, long? FbrPosId, string? FbrToken, string? FbrEndpointOverride, string FbrStaticIp,
    string FiscalAuthority = "FBR", string FiscalEnvironment = "Practice", bool HasFbrToken = false, string TokenEnds = "");

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
public record FbrSettingsDto(bool FeatureFbr, bool FbrEnabled, string BusinessName, string Ntn, string Strn,
    bool SendAtSettle = true, bool AutoRetry = true, bool ForcedPractice = false, bool LocalAvailable = false);
// SendAtSettle/AutoRetry: null leaves them as they are.
public record FbrSettingsInput(bool FbrEnabled, string BusinessName, string Ntn, string Strn,
    bool? SendAtSettle = null, bool? AutoRetry = null);

// Front-of-house service settings (currently: how waiters get assigned to tables).
public record ServiceSettingsDto(bool WaiterAutoAssign, bool GuestTakeaway, bool GuestDelivery, decimal DeliveryFee, string DeliveryNote,
    bool PickupInPlan, double? Latitude = null, double? Longitude = null, decimal DeliveryRadiusKm = 0);
// Every field is optional: each settings card sends only its own, and leaves the others as they are.
public record ServiceSettingsInput(bool? WaiterAutoAssign = null, bool? GuestTakeaway = null, bool? GuestDelivery = null,
    decimal? DeliveryFee = null, string? DeliveryNote = null, double? Latitude = null, double? Longitude = null, decimal? DeliveryRadiusKm = null);

// Billing settings: Tax/GST, discount, receipt and stock behaviour. Owner-managed.
// The bill options and cancelling rules are named the way the owner reads them ("allow ...", "put back ..."),
// whatever the column is called. Their defaults are how the app always worked.
public record BillingSettingsDto(
    bool TaxEnabled, string TaxName, decimal TaxPercent, decimal ServiceChargePercent,
    bool DiscountEnabled, bool DiscountIsPercent, decimal DiscountValue,
    bool ReceiptEnabled, bool StockTrackingEnabled, string Currency, decimal? TaxPercentCard = null,
    bool ServiceChargeTaxable = false, bool ServiceAfterDiscount = false, bool RoundTotals = false,
    bool CardRateForAllItems = false, bool AllowCancelAfterCooking = true, bool RestockCookedCancel = true,
    bool CashierCancelAnyStage = false, bool OneTaxForAllItems = false);
// The new fields are optional (null = leave as it is), so a screen that doesn't know them yet can't change them.
public record BillingSettingsInput(
    bool TaxEnabled, string TaxName, decimal TaxPercent, decimal ServiceChargePercent,
    bool DiscountEnabled, bool DiscountIsPercent, decimal DiscountValue,
    bool ReceiptEnabled, bool StockTrackingEnabled, decimal? TaxPercentCard = null,
    bool? ServiceChargeTaxable = null, bool? ServiceAfterDiscount = null, bool? RoundTotals = null,
    bool? CardRateForAllItems = null, bool? AllowCancelAfterCooking = null, bool? RestockCookedCancel = null,
    bool? CashierCancelAnyStage = null, bool? OneTaxForAllItems = null);

// Onboarding
public record OnboardRequest(string RestaurantName, string? Slug, string OwnerDisplayName, string OwnerUsername, string OwnerPassword, int? PlanId = null);
