using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QrDine.Application.Auth;
using QrDine.Application.Common;
using QrDine.Application.Ordering.Dtos;
using QrDine.Application.Platform;
using QrDine.Domain.Common;
using QrDine.Domain.Identity;
using QrDine.Domain.Restaurants;
using QrDine.Infrastructure.Identity;
using QrDine.Infrastructure.MultiTenancy;
using QrDine.Infrastructure.Persistence;
using Xunit;

namespace QrDine.Tests;

/// <summary>
/// Who may see and do what. A sign-in is a 12-hour token, so these check that it is re-checked
/// against the database (fired staff, new passwords, changed roles) and that guests only ever see
/// their own table. Tokens go through the real issuer and a real validator, so the claim names
/// are the ones the app actually reads.
/// </summary>
public class SecurityTests : IDisposable
{
    private const string Secret = "a-test-secret-that-is-only-for-these-tests";
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly CurrentTenant _tenant = new();
    private readonly JwtTokenService _tokens = new(Options.Create(new JwtOptions { Secret = Secret }));
    private readonly PlainHasher _hasher = new();

    private readonly StaffMember _owner;
    private readonly StaffMember _cashier;

    public SecurityTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, _tenant);
        _db.Database.EnsureCreated();
        _db.Restaurants.AddRange(new Restaurant { Slug = "cafe", Name = "Cafe" }, new Restaurant { Slug = "other", Name = "Other" });
        _db.SaveChanges();
        _owner = new StaffMember { RestaurantId = 1, Username = "03001234567", DisplayName = "Owner", Role = StaffRole.Owner, PasswordHash = "owner-pass" };
        _cashier = new StaffMember { RestaurantId = 1, Username = "cashier", DisplayName = "Cashier", Role = StaffRole.Cashier, PasswordHash = "cash-pass" };
        _db.Staff.AddRange(_owner, _cashier);
        _db.RolePermissions.Add(new RolePermission { RestaurantId = 1, Role = StaffRole.Cashier, Permission = Permissions.OrdersPay });
        _db.SaveChanges();
        _tenant.Set(1, "cafe");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    // The same checks the app's JWT middleware makes.
    private static ClaimsPrincipal Read(string token) => new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "qrdine", ValidateAudience = true, ValidAudience = "qrdine",
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Secret))),
    }, out _);

    private ClaimsPrincipal StaffSignIn(StaffMember s, params string[] perms) => Read(_tokens.CreateStaffToken(s, "cafe", perms));
    private StaffSessions Sessions() => new(_db);

    private static OrderDto Order(string table, string status, DateTime created) =>
        new(1, table, table, "Main", status, "Qr", created, null, 0, null, null, 100m, Array.Empty<OrderItemDto>());

    // ---------- a staff sign-in is re-checked every time ----------

    [Fact]
    public async Task A_staff_sign_in_is_checked_against_the_staff_member_as_they_are_now()
    {
        var now = await Sessions().Check(StaffSignIn(_cashier, Permissions.OrdersPay));
        Assert.NotNull(now);
        Assert.Equal(_cashier.Id, now!.Staff.Id);
        Assert.Equal(new[] { Permissions.OrdersPay }, now.Permissions);
    }

    [Fact]
    public async Task A_new_password_ends_every_older_sign_in()
    {
        var before = StaffSignIn(_cashier, Permissions.OrdersPay);
        _cashier.PasswordHash = "new-pass";
        await _db.SaveChangesAsync();
        Assert.Null(await Sessions().Check(before));
        Assert.NotNull(await Sessions().Check(StaffSignIn(_cashier)));
    }

    [Fact]
    public async Task A_removed_staff_member_is_signed_out_at_once()
    {
        var signedIn = StaffSignIn(_cashier, Permissions.OrdersPay);
        _db.Staff.Remove(_cashier);
        await _db.SaveChangesAsync();
        Assert.Null(await Sessions().Check(signedIn));
    }

    [Fact]
    public async Task A_role_or_permission_change_applies_on_the_next_request()
    {
        var signedIn = StaffSignIn(_cashier, Permissions.OrdersPay);
        _db.RolePermissions.RemoveRange(_db.RolePermissions.IgnoreQueryFilters());
        _db.RolePermissions.Add(new RolePermission { RestaurantId = 1, Role = StaffRole.Waiter, Permission = Permissions.ReportsView });
        _cashier.Role = StaffRole.Waiter;
        await _db.SaveChangesAsync();

        var now = await Sessions().Check(signedIn);
        var user = StaffSessions.WithCurrentAccess(signedIn, now!);
        Assert.True(user.IsInRole("Waiter"));
        Assert.False(user.IsInRole("Cashier"));
        Assert.Contains(user.FindAll(AppClaims.Permission), c => c.Value == Permissions.ReportsView);
        Assert.DoesNotContain(user.FindAll(AppClaims.Permission), c => c.Value == Permissions.OrdersPay);
        Assert.Equal("cafe", user.FindFirst(AppClaims.Slug)?.Value);   // everything else is kept
    }

    [Fact]
    public async Task An_owner_always_has_every_permission()
    {
        var now = await Sessions().Check(StaffSignIn(_owner));
        Assert.Equal(Permissions.All.OrderBy(p => p), now!.Permissions.OrderBy(p => p));
    }

    [Fact]
    public async Task Old_style_or_borrowed_tokens_are_refused()
    {
        // Issued before this change: no staff id or password stamp.
        var old = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AppClaims.TokenType, AppClaims.TypeStaff), new Claim(AppClaims.Slug, "cafe"), new Claim(AppClaims.RestaurantId, "1"),
        }, "test"));
        Assert.Null(await Sessions().Check(old));

        // A real staff id, but claiming to be from the other restaurant.
        var good = StaffSignIn(_cashier);
        var mixed = new ClaimsPrincipal(new ClaimsIdentity(good.Claims.Where(c => c.Type != AppClaims.RestaurantId)
            .Append(new Claim(AppClaims.RestaurantId, "2")), "test"));
        Assert.Null(await Sessions().Check(mixed));

        // Guests and operators are never staff.
        Assert.Null(await Sessions().Check(Read(_tokens.CreateGuestToken(1, "cafe", "T1"))));
    }

    [Fact]
    public async Task Changing_your_own_password_gives_this_screen_a_new_sign_in_and_ends_the_others()
    {
        var other = StaffSignIn(_cashier, Permissions.OrdersPay);
        var auth = new AuthService(_db, _tenant, _tokens, _hasher, new PermissionService(_db));
        var token = await auth.ChangePassword(_cashier.Id, new ChangePasswordRequest("cash-pass", "brand-new"));
        Assert.Null(await Sessions().Check(other));
        Assert.NotNull(await Sessions().Check(Read(token)));
    }

    // ---------- guests see only their own table ----------

    [Fact]
    public void A_guest_reads_only_their_own_tables_orders()
    {
        var guest = Read(_tokens.CreateGuestToken(1, "cafe", "T1"));
        Assert.True(Access.MayReadTable(guest, "T1"));
        Assert.True(Access.MayReadTable(guest, "t1 "));
        Assert.False(Access.MayReadTable(guest, "T2"));
        Assert.True(Access.MayReadOrder(guest, Order("T1", "Cooking", DateTime.UtcNow.AddHours(-3))));
        Assert.False(Access.MayReadOrder(guest, Order("T2", "Cooking", DateTime.UtcNow)));
    }

    [Fact]
    public void The_next_guests_at_a_table_cant_open_earlier_guests_bills()
    {
        var guest = Read(_tokens.CreateGuestToken(1, "cafe", "T1"));
        Assert.False(Access.MayReadOrder(guest, Order("T1", "Paid", DateTime.UtcNow.AddHours(-2))));
        Assert.False(Access.MayReadOrder(guest, Order("T1", "Cancelled", DateTime.UtcNow.AddMinutes(-30))));
        // Their own order, finished after they scanned, stays visible.
        Assert.True(Access.MayReadOrder(guest, Order("T1", "Paid", DateTime.UtcNow.AddSeconds(5))));
    }

    [Fact]
    public void A_guest_pass_says_when_it_was_issued_once()
    {
        var guest = Read(_tokens.CreateGuestToken(1, "cafe", "T1"));
        var issued = Assert.Single(guest.FindAll(AppClaims.IssuedAt));
        var at = DateTimeOffset.FromUnixTimeSeconds(long.Parse(issued.Value)).UtcDateTime;
        Assert.InRange(at, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Staff_read_any_order_and_other_tokens_read_none()
    {
        Assert.True(Access.MayReadOrder(StaffSignIn(_cashier), Order("T9", "Paid", DateTime.UtcNow.AddDays(-30))));
        var platform = Read(_tokens.CreatePlatformToken(new PlatformAdmin { Username = "boss", PasswordHash = "x" }));
        Assert.False(Access.MayReadOrder(platform, Order("T1", "Cooking", DateTime.UtcNow)));
        Assert.False(Access.MayReadTable(platform, "T1"));
        Assert.False(Access.MayReadOrder(null, Order("T1", "Cooking", DateTime.UtcNow)));
    }

    // ---------- the live feed ----------

    [Fact]
    public void Only_a_restaurants_own_staff_get_its_whole_live_feed()
    {
        Assert.True(Access.MayJoinStaffFeed(StaffSignIn(_cashier), "cafe"));
        Assert.True(Access.MayJoinStaffFeed(StaffSignIn(_cashier), "CAFE"));
        Assert.False(Access.MayJoinStaffFeed(StaffSignIn(_cashier), "other"));
        Assert.False(Access.MayJoinStaffFeed(Read(_tokens.CreateGuestToken(1, "cafe", "T1")), "cafe"));
        Assert.False(Access.MayJoinStaffFeed(new ClaimsPrincipal(new ClaimsIdentity()), "cafe"));
        Assert.False(Access.MayJoinStaffFeed(null, "cafe"));
    }

    [Fact]
    public void A_guest_hears_only_their_own_table()
    {
        var guest = Read(_tokens.CreateGuestToken(1, "cafe", "T1"));
        Assert.True(Access.MayJoinTableFeed(guest, "cafe", "T1"));
        Assert.False(Access.MayJoinTableFeed(guest, "cafe", "T2"));
        Assert.False(Access.MayJoinTableFeed(guest, "other", "T1"));
        Assert.True(Access.MayJoinTableFeed(StaffSignIn(_cashier), "cafe", "T7"));
        Assert.False(Access.MayJoinTableFeed(null, "cafe", "T1"));
    }

    // ---------- password guessing ----------

    [Fact]
    public async Task Ten_wrong_passwords_rest_the_account_even_for_the_right_one()
    {
        var clock = DateTime.UtcNow;
        var guard = new LoginGuard(() => clock);
        var auth = new AuthService(_db, _tenant, _tokens, _hasher, new PermissionService(_db), guard);

        for (var i = 0; i < 9; i++)
            await Assert.ThrowsAsync<DomainException>(() => auth.FindLogin(new LoginRequest("0300-1234567", "guess" + i)));
        Assert.NotNull(await auth.FindLogin(new LoginRequest("03001234567", "owner-pass")));   // right one resets the count

        for (var i = 0; i < 10; i++)
            await Assert.ThrowsAsync<DomainException>(() => auth.FindLogin(new LoginRequest("03001234567", "guess" + i)));
        var e = await Assert.ThrowsAsync<DomainException>(() => auth.FindLogin(new LoginRequest("+92 300 1234567", "owner-pass")));
        Assert.Contains("Too many wrong passwords", e.Message);

        clock = clock.AddMinutes(16);
        Assert.NotNull(await auth.FindLogin(new LoginRequest("03001234567", "owner-pass")));
    }

    [Fact]
    public void One_accounts_lock_doesnt_touch_another()
    {
        var guard = new LoginGuard();
        for (var i = 0; i < LoginGuard.MaxFailures; i++) guard.Failed("staff:a");
        Assert.Throws<DomainException>(() => guard.EnsureAllowed("staff:a"));
        guard.EnsureAllowed("staff:b");
    }

    [Fact]
    public void Wrong_tries_spread_over_more_than_15_minutes_dont_add_up()
    {
        var clock = DateTime.UtcNow;
        var guard = new LoginGuard(() => clock);
        for (var i = 0; i < 9; i++) guard.Failed("staff:a");
        clock = clock.AddMinutes(16);
        guard.Failed("staff:a");
        guard.EnsureAllowed("staff:a");
    }

    // ---------- the operator ----------

    [Fact]
    public async Task The_operator_password_follows_the_settings_file_and_old_operator_sign_ins_end()
    {
        await DbSeeder.SeedAsync(_db, _hasher, "Boss", "first-pass");
        var platform = new PlatformService(_db, _tokens, _hasher);
        var first = Read((await platform.Login(new PlatformLoginRequest("BOSS", "first-pass"))).Token);
        Assert.True(await platform.StillSignedIn(first));

        await DbSeeder.SeedAsync(_db, _hasher, "Boss", "second-pass");
        Assert.False(await platform.StillSignedIn(first));
        await Assert.ThrowsAsync<DomainException>(() => platform.Login(new PlatformLoginRequest("boss", "first-pass")));
        var second = Read((await platform.Login(new PlatformLoginRequest("boss", "second-pass"))).Token);
        Assert.True(await platform.StillSignedIn(second));
        Assert.Single(await _db.PlatformAdmins.ToListAsync());
    }

    [Fact]
    public async Task Operator_password_guessing_is_limited_too()
    {
        await DbSeeder.SeedAsync(_db, _hasher, "boss", "right-pass");
        var platform = new PlatformService(_db, _tokens, _hasher, new LoginGuard());
        for (var i = 0; i < LoginGuard.MaxFailures; i++)
            await Assert.ThrowsAsync<DomainException>(() => platform.Login(new PlatformLoginRequest("boss", "guess" + i)));
        var e = await Assert.ThrowsAsync<DomainException>(() => platform.Login(new PlatformLoginRequest("boss", "right-pass")));
        Assert.Contains("Too many wrong passwords", e.Message);
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string hash) => password == hash;
    }
}
