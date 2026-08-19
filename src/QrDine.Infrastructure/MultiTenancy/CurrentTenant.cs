using QrDine.Application.Common;

namespace QrDine.Infrastructure.MultiTenancy;

/// <summary>
/// Scoped holder for the request's tenant. Middleware calls Set() once it has
/// resolved the restaurant; the DbContext and services read it thereafter.
/// </summary>
public class CurrentTenant : ICurrentTenant
{
    public int? RestaurantId { get; private set; }
    public string? Slug { get; private set; }
    public bool IsResolved => RestaurantId.HasValue;

    public void Set(int restaurantId, string slug)
    {
        RestaurantId = restaurantId;
        Slug = slug;
    }
}
