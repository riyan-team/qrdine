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

    private readonly Billing.IFiscalCapabilities? _caps;

    public RestaurantAdminService(IAppDbContext db, IPasswordHasher hasher, ICurrentTenant tenant, Billing.IFiscalCapabilities? caps = null)
    {
        _db = db; _hasher = hasher; _tenant = tenant; _caps = caps;
    }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");

    // ---------- branches ----------
    public async Task<IReadOnlyList<BranchDto>> ListBranches(CancellationToken ct = default)
    {
        var tableCounts = (await _db.Tables.AsNoTracking().Where(t => t.Kind == Domain.Ordering.OrderChannel.Dine).Select(t => t.BranchId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        // The token is a password for filing in the restaurant's name: screens only learn that one is saved.
        return branches.Select(b => new BranchDto(b.Id, b.Name, b.Address, b.IsActive, tableCounts.GetValueOrDefault(b.Id),
            b.FbrMode.ToString(), b.FbrSandbox, b.FbrPosId, null, b.FbrEndpointOverride, b.FbrStaticIp,
            b.FiscalAuthority.ToString(), b.FiscalEnvironment.ToString(), !string.IsNullOrWhiteSpace(b.FbrToken),
            string.IsNullOrWhiteSpace(b.FbrToken) ? "" : b.FbrToken.Trim()[^Math.Min(4, b.FbrToken.Trim().Length)..])).ToList();
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

    // A name-and-address edit (KeepFiscal) never touches the tax office setup.
    private static void ApplyFbr(Branch b, BranchInput i)
    {
        if (i.KeepFiscal) return;
        if (i.FiscalAuthority is not null)
            b.FiscalAuthority = Enum.TryParse<FiscalAuthority>(i.FiscalAuthority, true, out var a) ? a
                : throw new DomainException("Choose FBR or PRA.");
        if (i.FbrMode is not null)
            b.FbrMode = Enum.TryParse<FbrMode>(i.FbrMode, true, out var m) ? m : FbrMode.Cloud;
        if (b.FiscalAuthority == FiscalAuthority.PRA) b.FbrMode = FbrMode.Cloud;   // PRA takes bills online only
        if (i.FbrPosId is <= 0) throw new DomainException("POSID must be a whole number above 0.");
        b.FbrPosId = i.FbrPosId;
        if (i.FbrToken is not null) b.FbrToken = string.IsNullOrWhiteSpace(i.FbrToken) ? null : i.FbrToken.Trim();
        if (b.FbrToken is { Length: > 200 }) throw new DomainException("That token is too long. Paste only the token.");
        var url = string.IsNullOrWhiteSpace(i.FbrEndpointOverride) ? null : i.FbrEndpointOverride.Trim();
        if (url is not null && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")))
            throw new DomainException("The test server address must start with https://");
        b.FbrEndpointOverride = url;
        b.FbrStaticIp = i.FbrStaticIp?.Trim() ?? "";

        if (i.FiscalEnvironment is not null)
            b.FiscalEnvironment = Enum.TryParse<FiscalEnvironment>(i.FiscalEnvironment, true, out var env) ? env
                : throw new DomainException("Choose Practice, Test or Live.");
        else if (i.FiscalAuthority is null)
            b.FiscalEnvironment = i.FbrSandbox ? (b.FiscalEnvironment == FiscalEnvironment.Live ? FiscalEnvironment.Test : b.FiscalEnvironment) : FiscalEnvironment.Live;
        // Live files real tax invoices: it needs the POSID and (online) the token before it can be chosen.
        if (b.FiscalEnvironment == FiscalEnvironment.Live)
        {
            var office = b.FiscalAuthority.ToString();
            if (b.FbrPosId is null) throw new DomainException($"Add this branch's live POSID from {office} before choosing Live.");
            var online = b.FiscalAuthority == FiscalAuthority.PRA || b.FbrMode == FbrMode.Cloud;
            if (online && string.IsNullOrWhiteSpace(b.FbrToken)) throw new DomainException($"Add this branch's live {office} token before choosing Live.");
            if (url is not null) throw new DomainException("Live always goes to the tax office itself. Clear the test server address first.");
        }
        b.FbrSandbox = b.FiscalEnvironment != FiscalEnvironment.Live;
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
        var tables = await _db.Tables.AsNoTracking().Where(t => t.Kind == Domain.Ordering.OrderChannel.Dine).OrderBy(t => t.Code).ToListAsync(ct);
        return tables.Select(t => new TableAdminDto(t.Id, t.Code, t.Name, t.BranchId, branches.GetValueOrDefault(t.BranchId, ""))).ToList();
    }

    /// <summary>
    /// Gives a table a new QR key. Its printed QR codes stop working and every guest pass for it ends, so use it when
    /// a QR code was photographed and misused. Returns the table's code, for the live feed.
    /// </summary>
    public async Task<string> NewTableQr(int id, CancellationToken ct = default)
    {
        var t = await _db.Tables.FirstOrDefaultAsync(x => x.Id == id && x.Kind == Domain.Ordering.OrderChannel.Dine, ct)
            ?? throw new DomainException("Table not found.");
        t.QrKey = DiningTable.NewQrKey();
        t.EndGuestPasses(DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
        return t.Code;
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
        var username = Auth.LoginName.Normalize(input.Username);
        // Staff sign in without saying which restaurant, so a name can only be used once anywhere.
        if (await _db.Staff.IgnoreQueryFilters().AnyAsync(s => s.Username == username, ct))
        {
            var suffix = (await _db.Restaurants.AsNoTracking().Where(r => r.Id == _tenant.RestaurantId)
                .Select(r => r.Slug).FirstOrDefaultAsync(ct)) ?? "team";
            throw new DomainException($"The username '{username}' is already in use. Try '{username}.{suffix}'.");
        }
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
        // Optional rename of the sign-in username. Same spelling rule as a new one ("0300 1234567"
        // and "03001234567" are one name), and unique across every restaurant, because staff
        // sign in without saying which restaurant they belong to.
        if (!string.IsNullOrWhiteSpace(input.Username))
        {
            var uname = Auth.LoginName.Normalize(input.Username);
            if (uname != s.Username)
            {
                if (await _db.Staff.IgnoreQueryFilters().AnyAsync(x => x.Username == uname && x.Id != id, ct))
                    throw new DomainException($"The username '{uname}' is already in use. Try another one.");
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

    public async Task<DateTime?> GetPaidThrough(CancellationToken ct = default) =>
        await _db.Restaurants.AsNoTracking().Where(x => x.Id == TenantId).Select(x => x.PlanExpiresAt).FirstAsync(ct);

    // ---------- FBR restaurant settings (owner enable + seller identity) ----------
    public async Task<FbrSettingsDto> GetFbrSettings(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        return new FbrSettingsDto(r.FeatureFbr, r.FbrEnabled,
            string.IsNullOrWhiteSpace(r.BusinessName) ? r.Name : r.BusinessName, r.Ntn, r.Strn,
            r.FiscalSendAtSettle, r.FiscalAutoRetry, _caps?.ForcedPractice ?? false, _caps?.LocalAvailable ?? false);
    }

    public async Task SetFbrSettings(FbrSettingsInput i, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        if (!r.FeatureFbr && i.FbrEnabled)
            throw new DomainException("FBR integration isn't included in your plan. Contact the operator to enable it.");
        var business = i.BusinessName?.Trim() ?? "";
        var ntn = i.Ntn?.Trim() ?? "";
        var strn = i.Strn?.Trim() ?? "";
        if (business.Length > 150) throw new DomainException("Business name must be 150 characters or fewer.");
        if (ntn.Length > 15) throw new DomainException("NTN must be 15 characters or fewer.");
        if (strn.Length > 20) throw new DomainException("STRN must be 20 characters or fewer.");
        r.FbrEnabled = i.FbrEnabled;
        r.BusinessName = business;
        r.Ntn = ntn;
        r.Strn = strn;
        if (i.SendAtSettle is bool send) r.FiscalSendAtSettle = send;
        if (i.AutoRetry is bool auto) r.FiscalAutoRetry = auto;
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
        return new ServiceSettingsDto(r.WaiterAutoAssign, r.GuestTakeaway, r.GuestDelivery, r.DeliveryFee, r.DeliveryNote ?? "",
            Menu.MenuService.PickupInPlan(r), r.Latitude, r.Longitude, r.DeliveryRadiusKm);
    }

    public async Task SetServiceSettings(ServiceSettingsInput i, CancellationToken ct = default)
    {
        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        if (i.WaiterAutoAssign is bool w) r.WaiterAutoAssign = w;
        if (i.GuestTakeaway is bool t) r.GuestTakeaway = t;
        if (i.GuestDelivery is bool d) r.GuestDelivery = d;
        if (i.DeliveryFee is decimal fee)
        {
            if (fee < 0 || fee > 100000) throw new DomainException("The delivery charge must be between 0 and 100,000.");
            r.DeliveryFee = fee;
        }
        if (i.Latitude is not null || i.Longitude is not null)
        {
            if (!Ordering.Geo.Valid(i.Latitude, i.Longitude)) throw new DomainException("That location doesn't look right.");
            r.Latitude = i.Latitude; r.Longitude = i.Longitude;
        }
        if (i.DeliveryRadiusKm is decimal km)
        {
            if (km < 0 || km > 100) throw new DomainException("The delivery area must be between 0 and 100 km (0 means no limit).");
            r.DeliveryRadiusKm = km;
        }
        if (i.DeliveryNote is not null)
        {
            var note = i.DeliveryNote.Trim();
            if (note.Length > 120) throw new DomainException("Keep the delivery note under 120 characters.");
            r.DeliveryNote = note;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---------- billing settings (tax/GST, discount, receipt, stock) ----------
    public async Task<BillingSettingsDto> GetBillingSettings(CancellationToken ct = default)
    {
        var r = await _db.Restaurants.AsNoTracking().FirstAsync(x => x.Id == TenantId, ct);
        return new BillingSettingsDto(r.TaxEnabled, string.IsNullOrWhiteSpace(r.TaxName) ? "GST" : r.TaxName,
            r.TaxPercent, r.ServiceChargePercent,
            r.DiscountEnabled, r.DiscountIsPercent, r.DiscountValue,
            r.ReceiptEnabled, r.StockTrackingEnabled, r.Currency, r.TaxPercentCard,
            r.ServiceChargeTaxable, r.ServiceAfterDiscount, r.RoundTotals, r.CardRateForAllItems,
            !r.NoCancelOnceCooking, !r.KeepStockOnCookedCancel, r.CashierCancelAnyStage, r.OneTaxForAllItems);
    }

    /// <param name="callerIsOwner">The cancelling rules say what everyone but the owner may do, so only the owner
    /// can change them. A manager saving this card with them unchanged is fine.</param>
    public async Task SetBillingSettings(BillingSettingsInput i, CancellationToken ct = default, bool callerIsOwner = true)
    {
        // A wrong number is refused, never quietly changed: the owner must see what will be charged.
        if (!BillingRules.IsPercent(i.TaxPercent)) throw new DomainException(BillingRules.TaxMessage);
        if (i.TaxPercentCard is decimal c && !BillingRules.IsPercent(c)) throw new DomainException(BillingRules.CardTaxMessage);
        if (!BillingRules.IsPercent(i.ServiceChargePercent)) throw new DomainException(BillingRules.ServiceMessage);
        if (i.DiscountValue < 0) throw new DomainException(BillingRules.DiscountNegativeMessage);
        if (i.DiscountIsPercent && i.DiscountValue > 100) throw new DomainException(BillingRules.DiscountPercentMessage);
        if (!i.DiscountIsPercent && i.DiscountValue >= BillingRules.MaxAmount) throw new DomainException(BillingRules.DiscountTooBigMessage);
        var taxName = string.IsNullOrWhiteSpace(i.TaxName) ? "GST" : i.TaxName.Trim();
        if (taxName.Length > 40) throw new DomainException("Tax name must be 40 characters or fewer.");

        var r = await _db.Restaurants.FirstAsync(x => x.Id == TenantId, ct);
        r.TaxEnabled = i.TaxEnabled;
        r.TaxName = taxName;
        r.TaxPercent = i.TaxPercent;
        r.TaxPercentCard = i.TaxPercentCard;
        r.ServiceChargePercent = i.ServiceChargePercent;
        r.DiscountEnabled = i.DiscountEnabled;
        r.DiscountIsPercent = i.DiscountIsPercent;
        r.DiscountValue = i.DiscountValue;
        r.ReceiptEnabled = i.ReceiptEnabled;
        r.StockTrackingEnabled = i.StockTrackingEnabled;

        if (i.ServiceChargeTaxable is bool st) r.ServiceChargeTaxable = st;
        if (i.ServiceAfterDiscount is bool sad) r.ServiceAfterDiscount = sad;
        if (i.RoundTotals is bool rt) r.RoundTotals = rt;
        if (i.CardRateForAllItems is bool cr) r.CardRateForAllItems = cr;
        if (i.OneTaxForAllItems is bool one) r.OneTaxForAllItems = one;

        var noCancel = i.AllowCancelAfterCooking is bool allow ? !allow : r.NoCancelOnceCooking;
        var keepStock = i.RestockCookedCancel is bool restock ? !restock : r.KeepStockOnCookedCancel;
        var anyStage = i.CashierCancelAnyStage ?? r.CashierCancelAnyStage;
        if (!callerIsOwner && (noCancel != r.NoCancelOnceCooking || keepStock != r.KeepStockOnCookedCancel
                               || anyStage != r.CashierCancelAnyStage))
            throw new DomainException("Only the owner can change the cancelling rules.");
        r.NoCancelOnceCooking = noCancel;
        r.KeepStockOnCookedCancel = keepStock;
        r.CashierCancelAnyStage = anyStage;
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
