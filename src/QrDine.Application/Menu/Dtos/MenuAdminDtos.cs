namespace QrDine.Application.Menu.Dtos;

// Inputs for the owner's menu-management screen.

public record CategoryInput(string Name, int SortOrder);

public record ItemInput(
    int CategoryId, string Name, string NameUrdu, string Description,
    decimal Price, string Emoji, bool Available, int PrepMinutes,
    List<VariantInput>? Variants, List<ModifierGroupInput>? ModifierGroups,
    decimal? TaxRate = null, decimal FurtherTaxRate = 0, string? PctCode = null,
    // Deal / offer
    decimal? DealPrice = null, string? DealDescription = null, string? DealDays = null,
    int? DealStartMinutes = null, int? DealEndMinutes = null, bool DealHideWhenOff = false);

public record VariantInput(string Name, decimal Price, int SortOrder);
public record ModifierGroupInput(string Name, int MinSelect, int MaxSelect, int SortOrder, List<ModifierOptionInput>? Options);
public record ModifierOptionInput(string Name, decimal Price, int SortOrder);

public record AvailabilityInput(bool Available);
