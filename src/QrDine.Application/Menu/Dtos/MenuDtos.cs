namespace QrDine.Application.Menu.Dtos;

public record RestaurantDto(string Slug, string Name, string Currency, FeaturesDto Features, string LogoUrl = "", PickupDto? Pickup = null);
/// <summary>What the restaurant's takeaway/delivery QR offers a guest.</summary>
public record PickupDto(bool Takeaway, bool Delivery, decimal DeliveryFee, string DeliveryNote,
    double? ShopLat = null, double? ShopLng = null, decimal RadiusKm = 0);
public record FeaturesDto(bool Ordering, bool Kitchen, bool Waiter, bool Billing);

public record MenuDto(RestaurantDto Restaurant, IReadOnlyList<MenuCategoryDto> Menu);
public record MenuCategoryDto(int Id, string Name, IReadOnlyList<MenuItemDto> Items);
public record MenuItemDto(int Id, string Name, string NameUrdu, string Description,
    decimal Price, string Emoji, string? ImageUrl, bool Available, int PrepMinutes,
    IReadOnlyList<VariantDto> Variants, IReadOnlyList<ModifierGroupDto> ModifierGroups,
    decimal? TaxRate = null, decimal FurtherTaxRate = 0, string PctCode = "",
    // Deal / offer
    decimal? DealPrice = null, bool DealActive = false, string DealDescription = "",
    string DealDays = "", int? DealStartMinutes = null, int? DealEndMinutes = null, bool DealHideWhenOff = false);

public record VariantDto(int Id, string Name, decimal Price);
public record ModifierGroupDto(int Id, string Name, int MinSelect, int MaxSelect, IReadOnlyList<ModifierOptionDto> Options);
public record ModifierOptionDto(int Id, string Name, decimal Price);

public record TableDto(string Code, string Name, string BranchName);
