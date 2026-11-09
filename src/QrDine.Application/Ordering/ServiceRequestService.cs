using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Ordering;

namespace QrDine.Application.Ordering;

public record CallStaffRequest(string Kind);
public record ServiceRequestDto(int Id, string TableCode, string TableName, string Kind, DateTime CreatedAt, bool Done);

/// <summary>"Call waiter" and "ask for the bill" from the guest's phone.</summary>
public class ServiceRequestService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IOrderNotifier _notifier;

    public ServiceRequestService(IAppDbContext db, ICurrentTenant tenant, IOrderNotifier notifier)
    {
        _db = db; _tenant = tenant; _notifier = notifier;
    }

    private string Slug => _tenant.Slug ?? "";

    public async Task<ServiceRequestDto> Call(string tableCode, string kind, CancellationToken ct = default)
    {
        var wanted = string.Equals(kind, "Bill", StringComparison.OrdinalIgnoreCase) ? "Bill" : "Waiter";
        var table = await _db.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.Code == tableCode, ct)
            ?? throw new DomainException("Unknown table.");

        // Tapping twice doesn't call the waiter twice.
        var open = await _db.ServiceRequests.FirstOrDefaultAsync(r => r.TableCode == table.Code && r.Kind == wanted && r.DoneAt == null, ct);
        if (open is not null) return ToDto(open);

        var request = new ServiceRequest { BranchId = table.BranchId, TableCode = table.Code, TableName = table.Name, Kind = wanted };
        _db.ServiceRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        var dto = ToDto(request);
        await _notifier.ServiceRequestChanged(Slug, dto, ct);
        return dto;
    }

    public async Task<IReadOnlyList<ServiceRequestDto>> Open(CancellationToken ct = default)
    {
        var rows = await _db.ServiceRequests.AsNoTracking().Where(r => r.DoneAt == null).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task Done(int id, string by, CancellationToken ct = default)
    {
        var request = await _db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || request.DoneAt is not null) return;
        request.DoneAt = DateTime.UtcNow;
        request.DoneBy = by;
        await _db.SaveChangesAsync(ct);
        await _notifier.ServiceRequestChanged(Slug, ToDto(request), ct);
    }

    private static ServiceRequestDto ToDto(ServiceRequest r) => new(r.Id, r.TableCode, r.TableName, r.Kind,
        DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), r.DoneAt is not null);
}
