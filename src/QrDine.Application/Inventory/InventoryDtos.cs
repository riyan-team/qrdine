namespace QrDine.Application.Inventory;

public record StockItemDto(int Id, string Name, string Unit, decimal Quantity, decimal LowThreshold, bool IsLow, decimal LastCost = 0);
public record StockItemInput(string Name, string Unit, decimal Quantity, decimal LowThreshold);
public record RestockInput(decimal Quantity, string? Reason);
public record AdjustInput(decimal NewQuantity, string? Reason);
public record WasteInput(decimal Quantity, string? Reason);

public record RecipeLineDto(int StockItemId, string StockItemName, string Unit, decimal QtyPerServing);
public record RecipeInput(List<RecipeLineInput> Lines);
public record RecipeLineInput(int StockItemId, decimal QtyPerServing);

public record MovementDto(string Item, decimal Delta, string Type, string? Reason, DateTime CreatedAt);
