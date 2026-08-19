using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;

namespace QrDine.Application.Auth;

public record PermissionMatrixDto(IReadOnlyList<string> All, IReadOnlyDictionary<string, string[]> Roles);
public record SetRolePermissionsRequest(string Role, List<string> Permissions);

/// <summary>Reads and edits the per-role permission grants for a restaurant.</summary>
public class PermissionService
{
    private static readonly StaffRole[] Configurable = { StaffRole.Manager, StaffRole.Cashier, StaffRole.Kitchen, StaffRole.Waiter };

    private readonly IAppDbContext _db;
    public PermissionService(IAppDbContext db) => _db = db;

    /// <summary>The permissions a role currently holds. Owner always has everything.</summary>
    public async Task<string[]> GetForRole(StaffRole role, CancellationToken ct = default)
    {
        if (role == StaffRole.Owner) return Permissions.All;
        return await _db.RolePermissions.AsNoTracking()
            .Where(p => p.Role == role).Select(p => p.Permission).ToArrayAsync(ct);
    }

    public async Task<PermissionMatrixDto> GetMatrix(CancellationToken ct = default)
    {
        var rows = await _db.RolePermissions.AsNoTracking().ToListAsync(ct);
        var roles = Configurable.ToDictionary(
            r => r.ToString(),
            r => rows.Where(x => x.Role == r).Select(x => x.Permission).ToArray());
        return new PermissionMatrixDto(Permissions.All, roles);
    }

    public async Task SetRole(string roleName, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        if (!Enum.TryParse<StaffRole>(roleName, true, out var role)) throw new DomainException($"Unknown role '{roleName}'.");
        if (role == StaffRole.Owner) throw new DomainException("Owner always has full access — it can't be changed.");

        var valid = permissions.Where(p => Permissions.All.Contains(p)).Distinct().ToList();
        var existing = await _db.RolePermissions.Where(p => p.Role == role).ToListAsync(ct);
        _db.RolePermissions.RemoveRange(existing);
        foreach (var p in valid) _db.RolePermissions.Add(new RolePermission { Role = role, Permission = p });
        await _db.SaveChangesAsync(ct);
    }
}
