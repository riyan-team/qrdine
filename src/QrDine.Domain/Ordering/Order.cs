using QrDine.Domain.Common;
using QrDine.Domain.Ordering.Events;

namespace QrDine.Domain.Ordering;

/// <summary>
/// The order aggregate — the heart of the product. It owns its line items and
/// its own lifecycle. Every transition is a method that (a) checks the move is
/// legal from the current state and (b) raises a domain event. Illegal moves
/// throw a DomainException instead of silently corrupting state. This is what
/// stops "served" before "cooked", or approving the same order twice.
/// </summary>
public class Order : AggregateRoot, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = "";
    public int TableSessionId { get; set; }
    public int DiningTableId { get; set; }
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";

    public OrderStatus Status { get; private set; } = OrderStatus.Placed;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; private set; }   // when the kitchen started cooking
    public int EtaMinutes { get; private set; }         // kitchen's promised time → phone countdown
    public string? WaiterName { get; private set; }
    public string? CustomerNote { get; private set; }
    public string? CancelReason { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    public decimal Total
    {
        get { decimal t = 0; foreach (var i in _items) t += i.LineTotal; return t; }
    }

    // EF needs a parameterless ctor; keep it non-public so app code uses Place().
    private Order() { }

    /// <summary>Create a new order in the Placed state.</summary>
    public static Order Place(int restaurantId, TableSessionInfo table, string? customerNote)
    {
        var order = new Order
        {
            RestaurantId = restaurantId,
            BranchId = table.BranchId,
            BranchName = table.BranchName,
            TableSessionId = table.SessionId,
            DiningTableId = table.TableId,
            TableCode = table.TableCode,
            TableName = table.TableName,
            Status = OrderStatus.Placed,
            CreatedAt = DateTime.UtcNow,
            CustomerNote = customerNote,
        };
        return order;
    }

    public void AddItem(int menuItemId, string name, string? variantName, decimal unitPrice,
        int qty, string? note, IEnumerable<(string Name, decimal Price)>? modifiers = null)
    {
        if (Status != OrderStatus.Placed)
            throw new DomainException("Items can only be added while the order is being built.");
        if (qty < 1) qty = 1;
        var item = new OrderItem
        {
            MenuItemId = menuItemId, Name = name, VariantName = variantName,
            Price = unitPrice, Qty = qty, Note = note,
        };
        if (modifiers is not null)
            foreach (var m in modifiers)
                item.Modifiers.Add(new OrderItemModifier { Name = m.Name, Price = m.Price });
        _items.Add(item);
    }

    /// <summary>Called once the caller has finished adding items.</summary>
    public void ConfirmPlaced()
    {
        if (_items.Count == 0)
            throw new DomainException("An order must have at least one item.");
        Raise(new OrderPlaced(Id, DateTime.UtcNow));
    }

    // ---- guarded transitions ----

    public void Approve()
    {
        Require(OrderStatus.Placed, "approve");
        Status = OrderStatus.Approved;
        Raise(new OrderApproved(Id, DateTime.UtcNow));
    }

    public void StartCooking(int etaMinutes)
    {
        // Allow starting straight from Placed too, in case reception is bypassed.
        if (Status is not (OrderStatus.Approved or OrderStatus.Placed))
            throw Illegal("start cooking");
        EtaMinutes = Math.Clamp(etaMinutes, 1, 180);
        AcceptedAt = DateTime.UtcNow;
        Status = OrderStatus.Cooking;
        Raise(new CookingStarted(Id, EtaMinutes, DateTime.UtcNow));
    }

    public void MarkReady()
    {
        Require(OrderStatus.Cooking, "mark ready");
        Status = OrderStatus.Ready;
        Raise(new OrderReady(Id, DateTime.UtcNow));
    }

    public void AssignWaiter(string waiterName) => WaiterName = waiterName;

    public void Serve()
    {
        Require(OrderStatus.Ready, "serve");
        Status = OrderStatus.Served;
        Raise(new OrderServed(Id, DateTime.UtcNow));
    }

    public void Pay()
    {
        if (Status != OrderStatus.Served)
            throw Illegal("take payment for");
        Status = OrderStatus.Paid;
        Raise(new OrderPaid(Id, DateTime.UtcNow));
    }

    /// <summary>
    /// Close out this order as paid as part of settling the whole table. Unlike
    /// Pay() (single order, must be Served), settling the table accepts any still-
    /// active order — the cashier is closing the seating.
    /// </summary>
    public void Settle()
    {
        if (Status is OrderStatus.Paid or OrderStatus.Cancelled)
            throw new DomainException("This order is already closed.");
        Status = OrderStatus.Paid;
        Raise(new OrderPaid(Id, DateTime.UtcNow));
    }

    public void Cancel(string reason)
    {
        if (Status is OrderStatus.Served or OrderStatus.Paid or OrderStatus.Cancelled)
            throw new DomainException("A served or paid order cannot be cancelled.");
        CancelReason = reason;
        Status = OrderStatus.Cancelled;
        Raise(new OrderCancelled(Id, reason, DateTime.UtcNow));
    }

    private void Require(OrderStatus expected, string action)
    {
        if (Status != expected) throw Illegal(action);
    }

    private DomainException Illegal(string action) =>
        new($"Cannot {action} an order that is {Status}.");
}

/// <summary>Small carrier so Order.Place doesn't need a reference to the Tables namespace.</summary>
public readonly record struct TableSessionInfo(int SessionId, int TableId, string TableCode, string TableName, int BranchId, string BranchName);
