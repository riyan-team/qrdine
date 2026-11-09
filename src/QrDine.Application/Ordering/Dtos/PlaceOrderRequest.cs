namespace QrDine.Application.Ordering.Dtos;

public record MoveTableRequest(string ToCode);
public record VoidItemRequest(int Qty, string Reason);
public record PlaceOrderRequest(string TableCode, List<OrderLineRequest> Items, string? Note);
public record OrderLineRequest(int MenuItemId, int Qty, string? Note,
    int? VariantId = null, List<int>? ModifierOptionIds = null);

public record CookRequest(int EtaMinutes);
public record AssignRequest(string WaiterName);
public record CancelRequest(string? Reason);
