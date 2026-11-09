using Microsoft.EntityFrameworkCore;

namespace QrDine.Application.Common;

/// <summary>
/// Bills and removals record who did them by login, and a login is often a mobile number. On anything
/// an owner reads, this turns that login into the person's name. Someone since removed keeps their login.
/// </summary>
public static class StaffNames
{
    public static async Task<Func<string?, string>> Lookup(IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.Staff.AsNoTracking().Select(s => new { s.Username, s.DisplayName }).ToListAsync(ct);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            names[r.Username] = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Username : r.DisplayName;
        return login => string.IsNullOrWhiteSpace(login) ? "" : names.GetValueOrDefault(login, login);
    }
}
