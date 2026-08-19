namespace QrDine.Application.Common;

/// <summary>
/// The restaurant (tenant) the current request belongs to. Resolved once per
/// request by middleware (from the route slug, a subdomain, or a JWT claim) and
/// consumed by the DbContext's global query filter so all data is auto-scoped.
/// </summary>
public interface ICurrentTenant
{
    int? RestaurantId { get; }
    string? Slug { get; }
    bool IsResolved { get; }
    void Set(int restaurantId, string slug);
}
