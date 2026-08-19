namespace QrDine.Domain.Common;

/// <summary>Base type for all persistent entities.</summary>
public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>
/// Marks a row as belonging to a tenant (a restaurant). The EF Core global
/// query filter uses this so one database safely serves many restaurants.
/// </summary>
public interface ITenantOwned
{
    int RestaurantId { get; set; }
}

/// <summary>
/// An aggregate root is the only entry point through which its cluster of
/// entities may be changed. It also collects domain events raised while its
/// invariants were enforced, to be dispatched after the change is saved.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearDomainEvents() => _events.Clear();
}

/// <summary>A fact that has happened in the domain, worth reacting to.</summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
