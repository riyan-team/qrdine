using QrDine.Domain.Ordering;

namespace QrDine.Application.Ordering.Dtos;

/// <summary>The shape of an order as sent to any screen (staff or customer).</summary>
public record OrderDto(
    int Id,
    string TableCode,
    string TableName,
    string BranchName,
    string Status,
    string Source,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    int EtaMinutes,
    string? WaiterName,
    string? CustomerNote,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto From(Order o) => new(
        o.Id, o.TableCode, o.TableName, o.BranchName, o.Status.ToString(), o.Source.ToString(), AsUtc(o.CreatedAt),
        AsUtc(o.AcceptedAt), o.EtaMinutes, o.WaiterName, o.CustomerNote, o.Total,
        o.Items.Select(i => new OrderItemDto(i.Name, i.VariantName, i.Price, i.Qty, i.Note,
            i.Modifiers.Select(m => m.Name).ToList())).ToList());

    // CreatedAt/AcceptedAt are stored as UTC (DateTime.UtcNow) but come back from the
    // DB as Kind=Unspecified, so System.Text.Json serialises them WITHOUT a trailing
    // 'Z'. The browser then parses them as *local* time, throwing the kitchen countdown
    // off by the client's UTC offset (00:00 for anyone ahead of UTC). Tagging them Utc
    // makes the JSON carry 'Z' so `new Date(...)` is correct everywhere.
    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
    private static DateTime? AsUtc(DateTime? d) => d is null ? null : AsUtc(d.Value);
}

public record OrderItemDto(string Name, string? VariantName, decimal Price, int Qty, string? Note,
    IReadOnlyList<string> Modifiers);

/// <summary>A page of orders for the Kitchen/Waiter/Reception history views.</summary>
public record OrderHistoryPage(IReadOnlyList<OrderDto> Rows, int Page, int PageSize, int Total, int TotalPages);
