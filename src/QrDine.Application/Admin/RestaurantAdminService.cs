using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Restaurants;
using QrDine.Domain.Tables;

namespace QrDine.Application.Admin;

/// <summary>Owner/manager management of branches, tables and staff.</summary>
public class RestaurantAdminService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentTenant _tenant;

    public RestaurantAdminService(IAppDbContext db, IPasswordHasher hasher, ICurrentTenant tenant)
    {
        _db = db; _hasher = hasher; _tenant = tenant;
    }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");

    // ---------- branches ----------
    public async Task<IReadOnlyList<BranchDto>> ListBranches(CancellationToken ct = default)
    {
        var tableCounts = (await _db.Tables.AsNoTracking().Select(t => t.BranchId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        return branches.Select(b => new BranchDto(b.Id, b.Name, b.Address, b.IsActive, tableCounts.GetValueOrDefault(b.Id),
            b.FbrMode.ToString(), b.FbrSandbox, b.FbrPosId, b.FbrToken, b.FbrEndpointOverride, b.FbrStaticIp)).ToList();
    }

    public async Task<int> AddBranch(BranchInput i, CancellationToken ct = default)
    {
        var b = new Branch { Name = i.Name.Trim(), Address = i.Address?.Trim() ?? "", IsActive = i.IsActive };
        ApplyFbr(b, i);
        _db.Branches.Add(b);
        await _db.SaveChangesAsync(ct);
        return b.Id;
    }

    public async Task UpdateBranch(int id, BranchInput i, CancellationToken ct = default)
    {
        var b = await _db.Branches.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Branch not found.");
        b.Name = i.Name.Trim(); b.Address = i.Address?.Trim() ?? ""; b.IsActive = i.IsActive;
        ApplyFbr(b, i);
        await _db.SaveChangesAsync(ct);
    }

    private static void ApplyFbr(Branch b, BranchInput i)
    {
        b.FbrMode = Enum.TryParse<FbrMode>(i.FbrMode, true, out var m) ? m : FbrMode.Local;
        b.FbrSandbox = i.FbrSandbox;
        b.FbrPosId = i.FbrPosId;
        b.FbrToken = string.IsNullOrWhiteSpace(i.FbrToken) ? null : i.FbrToken.Trim();
        b.FbrEndpointOverride = string.IsNullOrWhiteSpace(i.FbrEndpointOverride) ? null : i.FbrEndpointOverride.Trim();
        b.FbrStaticIp = i.FbrStaticIp?.Trim() ?? "";
    }

    public async Task DeleteBranch(int id, CancellationToken ct = default)
    {
        var b = await _db.Branches.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Branch not found.");
        if (await _db.Branches.CountAsync(ct) <= 1) throw new DomainException("A restaurant must have at least one branch.");
        if (await _db.Tables.AnyAsync(t => t.BranchId == id, ct)) throw new DomainException("Move or delete this branch's tables first.");
        _db.Branches.Remove(b);
        await _db.SaveChangesAsync(ct);
    }

    // ---------- tables ----------
    public async Task<IReadOnlyList<TableAdminDto>> ListTables(CancellationToken ct = default)
    {
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var tables = await _db.Tables.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct);
        return tables.Select(t => new TableAdminDto(t.Id, t.Code, t.Name, t.BranchId, branches.GetValueOrDefault(t.BranchId, ""))).ToList();
    }

    private async Task<int> ResolveBranch(int branchId, CancellationToken ct)
    {
        if (branchId > 0 && await _db.Branches.AnyAsync(b => b.Id == branchId, ct)) return branchId;
        var first = await _db.Branches.OrderBy(b => b.Id).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Create a branch first.");
        return first.Id;
    }

    public async Task<int> AddTable(TableInput input, CancellationToken ct = default)
    {
        var code = input.Code.Trim();
        if (await _db.Tables.AnyAsync(t => t.Code == code, ct))
            throw new DomainException($"Table code '{code}' already exists.");
        var t = new DiningTable { Code = code, Name = input.Name.Trim(), BranchId = await ResolveBranch(input.BranchId, ct) };
        _db.Tables.Add(t);
        await _db.SaveChangesAsync(ct);
        return t.Id;
    }

    public async Task UpdateTable(int id, TableInput input, CancellationToken ct = default)
    {
        var t = await _db.Tables.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Table not found.");
        var code = input.Code.Trim();
        if (code != t.Code && await _db.Tables.AnyAsync(x => x.Code == code, ct))
            throw new DomainException($"Table code '{code}' already exists.");
        t.Code = code; t.Name = input.Name.Trim(); t.BranchId = await ResolveBranch(input.BranchId, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteTable(int id, CancellationToken ct = default)
    {
        var t = await _db.Tables.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Table not found.");
        if (await _db.Sessions.AnyAsync(s => s.DiningTableId == id && s.IsOpen, ct))
            throw new DomainException("This table has an open bill — settle it first.");
        _db.Tables.Remove(t);
        await _db.SaveChangesAsync(ct);
    }

    // ---------- staff ----------
    public async Task<IReadOnlyList<StaffDto>> ListStaff(CancellationToken ct = default) =>
        await _db.Staff.AsNoTracking().OrderBy(s => s.Username)
            .Select(s => new StaffDto(s.Id, s.Username, s.DisplayName, s.Role.ToString(),
                s.FbrPosId, s.FbrToken != null && s.FbrToken != "")).ToListAsync(ct);

    public async Task<int> AddStaff(StaffInput input, bool callerIsOwner, CancellationToken ct = default)
    {
        var role = ParseRole(input.Role);
        // Only an owner can create another owner — otherwise a manager (who holds
        // settings.manage by default) could mint an owner account and take over.
        if (role == StaffRole.Owner && !callerIsOwner)
            throw new DomainException("Only an owner can add another owner.");
        var username = input.Username.Trim().ToLowerInvariant();
        if (await _db.Staff.AnyAsync(s => s.Username == username, ct))
            throw new DomainException($"Username '{username}' is taken.");
        var s = new StaffMember
        {
            Username = username, DisplayName = input.DisplayName.Trim(), Role = role,
            PasswordHash = _hasher.Hash(input.Password),
            FbrPosId = input.FbrPosId,
            FbrToken = string.IsNullOrWhiteSpace(input.FbrToken) ? null : input.FbrToken.Trim(),
        };
        _db.Staff.Add(s);
        await _db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task UpdateStaff(int id, StaffUpdateInput input, bool callerIsOwner, CancellationToken ct = default)
    {
        var s = await _db.Staff.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Staff not found.");
        var role = ParseRole(input.Role);
        // A non-owner may neither modify an existing owner nor promote anyone to owner
        // (which would be a self-service privilege escalation).
        if ((s.Role == StaffRole.Owner || role == StaffRole.Owner) && !callerIsOwner)
            throw new DomainException("Only an owner can manage owner accounts.");
        // Don't allow removing the last owner.
        if (s.Role == StaffRole.Owner && role != StaffRole.Owner &&
            await _db.Staff.CountAsync(x => x.Role == StaffRole.Owner, ct) <= 1)
            throw new DomainException("There must be at least one owner.");
        // Optional rename of the sign-in username (unique, lower-cased).
        if (!string.IsNullOrWhiteSpace(input.Username))
        {
            var uname = input.Username.Trim().ToLowerInvariant();
            if (uname != s.Username)
            {
                if (await _db.Staff.AnyAsync(x => x.Username == uname && x.Id != id, ct))
                    throw new DomainException($"Username '{uname}' is taken.");
                s.Username = uname;
            }
        }
        s.DisplayName = input.DisplayName.Trim();
        s.Role = role;
        s.FbrPosId = input.FbrPosId;
        // Empty string clears the token; null leaves it unchanged.
        if (input.FbrToken is not null)
            s.FbrToken = input.FbrToken.Trim() == "" ? null : input.FbrToken.Trim();
        await _db.SaveChangesAsync(ct);
    }

    // ---------- FBR restaurant settings (owner enable + seller identity) ----------
    public async Task<FbrSettingsDto> GetFbrSettings(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        return new FbrSettingsDto(r.FeatureFbr, r.FbrEnabled,
            string.IsNullOrWhiteSpace(r.BusinessName) ? r.Name : r.BusinessName, r.Ntn, r.Strn);
    }

    public async Task SetFbrSettings(FbrSettingsInput i, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        if (!r.FeatureFbr && i.FbrEnabled)
            throw new DomainException("FBR integration isn't included in your plan. Contact the operator to enable it.");
        r.FbrEnabled = i.FbrEnabled;
        r.BusinessName = i.BusinessName?.Trim() ?? "";
        r.Ntn = i.Ntn?.Trim() ?? "";
        r.Strn = i.Strn?.Trim() ?? "";
        await _db.SaveChangesAsync(ct);
    }

    // ---------- receipt branding (logo + printed header/footer) ----------
    public async Task<ReceiptSettingsDto> GetReceiptSettings(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        return new ReceiptSettingsDto(r.LogoUrl, r.ReceiptHeader, r.ReceiptFooter, r.Name);
    }

    public async Task SetReceiptSettings(ReceiptSettingsInput i, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        if (i.LogoUrl is not null) r.LogoUrl = i.LogoUrl.Trim();   // "" clears the logo
        r.ReceiptHeader = i.ReceiptHeader?.Trim() ?? "";
        r.ReceiptFooter = i.ReceiptFooter?.Trim() ?? "";
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetLogoUrl(string url, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        r.LogoUrl = url;
        await _db.SaveChangesAsync(ct);
    }

    // ---------- service settings (waiter assignment) ----------
    public async Task<ServiceSettingsDto> GetServiceSettings(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        return new ServiceSettingsDto(r.WaiterAutoAssign);
    }

    public async Task SetServiceSettings(ServiceSettingsInput i, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        r.WaiterAutoAssign = i.WaiterAutoAssign;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetPassword(int id, string password, bool callerIsOwner, CancellationToken ct = default)
    {
        var s = await _db.Staff.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Staff not found.");
        // Only an owner can reset an owner's password (otherwise a manager could hijack it).
        if (s.Role == StaffRole.Owner && !callerIsOwner)
            throw new DomainException("Only an owner can reset an owner's password.");
        s.PasswordHash = _hasher.Hash(password);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteStaff(int id, bool callerIsOwner, CancellationToken ct = default)
    {
        var s = await _db.Staff.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("Staff not found.");
        if (s.Role == StaffRole.Owner && !callerIsOwner)
            throw new DomainException("Only an owner can remove an owner account.");
        if (s.Role == StaffRole.Owner && await _db.Staff.CountAsync(x => x.Role == StaffRole.Owner, ct) <= 1)
            throw new DomainException("You can't delete the last owner.");
        _db.Staff.Remove(s);
        await _db.SaveChangesAsync(ct);
    }

    private static StaffRole ParseRole(string role) =>
        Enum.TryParse<StaffRole>(role, true, out var r) ? r : throw new DomainException($"Unknown role '{role}'.");
}
