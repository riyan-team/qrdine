using QrDine.Domain.Common;

namespace QrDine.Domain.Ordering.Events;

// Every state change raises one of these. Infrastructure turns them into live
// SignalR pushes — so the domain stays ignorant of how notification happens.
public record OrderPlaced(int OrderId, DateTime OccurredAt) : IDomainEvent;
public record OrderApproved(int OrderId, DateTime OccurredAt) : IDomainEvent;
public record CookingStarted(int OrderId, int EtaMinutes, DateTime OccurredAt) : IDomainEvent;
public record OrderReady(int OrderId, DateTime OccurredAt) : IDomainEvent;
public record OrderServed(int OrderId, DateTime OccurredAt) : IDomainEvent;
public record OrderPaid(int OrderId, DateTime OccurredAt) : IDomainEvent;
public record OrderCancelled(int OrderId, string Reason, DateTime OccurredAt) : IDomainEvent;
