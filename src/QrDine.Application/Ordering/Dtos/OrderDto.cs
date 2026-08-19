using QrDine.Domain.Ordering;

namespace QrDine.Application.Ordering.Dtos;

/// <summary>The shape of an order as sent to any screen (staff or customer).</summary>
public record OrderDto(
    int Id,
    string TableCode,
    string TableName,
    string BranchName,
    string Status,
    DateTime CreatedAt,
    DateTime? AcceptedAt,
    int EtaMinutes,
    string? WaiterName,
    string? CustomerNote,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto From(Order o) => new(
        o.Id, o.TableCode, o.TableName, o.BranchName, o.Status.ToString(), o.CreatedAt,
        o.AcceptedAt, o.EtaMinutes, o.WaiterName, o.CustomerNote, o.Total,
        o.Items.Select(i => new OrderItemDto(i.Name, i.VariantName, i.Price, i.Qty, i.Note,
            i.Modifiers.Select(m => m.Name).ToList())).ToList());
}

public record OrderItemDto(string Name, string? VariantName, decimal Price, int Qty, string? Note,
    IReadOnlyList<string> Modifiers);
