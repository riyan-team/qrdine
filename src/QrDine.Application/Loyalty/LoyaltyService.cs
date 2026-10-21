using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;
using QrDine.Domain.Common;
using QrDine.Domain.Loyalty;

namespace QrDine.Application.Loyalty;

/// <summary>
/// Loyalty points, rewards and the review links shown on the table's QR page.
/// <para>
/// Guests join with a phone number; their settled bills earn points (once per seating); they ask for a
/// reward and staff approve it, which is when the points come off. Nothing here touches orders or bill
/// amounts, and earning runs after a sale is saved (see <see cref="AwardForSettledSession"/>).
/// </para>
/// </summary>
public class LoyaltyService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public LoyaltyService(IAppDbContext db, ICurrentTenant tenant) { _db = db; _tenant = tenant; }

    private int TenantId => _tenant.RestaurantId ?? throw new DomainException("No restaurant in context.");

    /// <summary>At most this many reward requests can wait for staff at once, per member.</summary>
    public const int MaxPendingPerMember = 3;
    /// <summary>The feedback bonus is given at most once in this many hours, per member.</summary>
    public const int FeedbackBonusHours = 20;

    // ───────────────────────── programme ─────────────────────────

    /// <summary>The restaurant's programme, or an unsaved default (switched off) when it has none yet.</summary>
    private async Task<LoyaltyProgram> Program(CancellationToken ct, bool track = false)
    {
        var q = track ? _db.LoyaltyPrograms : _db.LoyaltyPrograms.AsNoTracking();
        return await q.FirstOrDefaultAsync(ct) ?? new LoyaltyProgram { RestaurantId = TenantId };
    }

    private async Task<string> Currency(CancellationToken ct) =>
        await _db.Restaurants.AsNoTracking().Where(r => r.Id == TenantId).Select(r => r.Currency).FirstOrDefaultAsync(ct) ?? "Rs";

    private async Task<ReviewStatsDto> ReviewStats(CancellationToken ct)
    {
        var ratings = await _db.Feedbacks.AsNoTracking().Select(f => f.Rating).ToListAsync(ct);
        return ratings.Count == 0 ? new ReviewStatsDto(0, 0) : new ReviewStatsDto(Math.Round(ratings.Average(), 1), ratings.Count);
    }

    public async Task<LoyaltyPublicDto> GetPublic(CancellationToken ct = default)
    {
        var p = await Program(ct);
        var rewards = p.Enabled
            ? await _db.LoyaltyRewards.AsNoTracking().Where(r => r.Active).OrderBy(r => r.PointsCost).ThenBy(r => r.SortOrder).ToListAsync(ct)
            : new List<LoyaltyReward>();
        return new LoyaltyPublicDto(p.Enabled, p.Name, p.Tagline, p.EarnPoints, p.PerAmount, p.MinBillAmount,
            p.WelcomeBonus, p.FeedbackBonus, p.GoogleReviewUrl, p.GoogleMapsUrl, await Currency(ct),
            rewards.Select(ToDto).ToList(), await ReviewStats(ct));
    }

    public async Task<LoyaltyProgramDto> GetProgram(CancellationToken ct = default)
    {
        var p = await Program(ct);
        return new LoyaltyProgramDto(p.Enabled, p.Name, p.Tagline, p.EarnPoints, p.PerAmount, p.MinBillAmount,
            p.WelcomeBonus, p.FeedbackBonus, p.GoogleReviewUrl, p.GoogleMapsUrl, await Currency(ct), Utc(p.UpdatedAt));
    }

    public async Task<LoyaltyProgramDto> UpdateProgram(LoyaltyProgramInput req, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 60) throw new DomainException("Give the programme a name (up to 60 characters).");
        if (req.EarnPoints is < 0 or > 1000) throw new DomainException("Points earned must be between 0 and 1,000.");
        if (req.PerAmount is <= 0 or > 1_000_000) throw new DomainException("The amount per point must be more than 0.");
        if (req.MinBillAmount is < 0 or > 10_000_000) throw new DomainException("The minimum bill can't be negative.");
        if (req.WelcomeBonus is < 0 or > 100_000) throw new DomainException("The welcome bonus must be between 0 and 100,000.");
        if (req.FeedbackBonus is < 0 or > 100_000) throw new DomainException("The feedback bonus must be between 0 and 100,000.");

        var p = await _db.LoyaltyPrograms.FirstOrDefaultAsync(ct);
        if (p is null) { p = new LoyaltyProgram(); _db.LoyaltyPrograms.Add(p); }
        p.Enabled = req.Enabled;
        p.Name = name;
        p.Tagline = Clip(req.Tagline, 160);
        p.EarnPoints = req.EarnPoints;
        p.PerAmount = req.PerAmount;
        p.MinBillAmount = req.MinBillAmount;
        p.WelcomeBonus = req.WelcomeBonus;
        p.FeedbackBonus = req.FeedbackBonus;
        p.GoogleReviewUrl = Url(req.GoogleReviewUrl, "Google review link");
        p.GoogleMapsUrl = Url(req.GoogleMapsUrl, "Google Maps link");
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetProgram(ct);
    }

    // ───────────────────────── rewards ─────────────────────────

    public async Task<IReadOnlyList<LoyaltyRewardDto>> Rewards(CancellationToken ct = default) =>
        (await _db.LoyaltyRewards.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.PointsCost).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<LoyaltyRewardDto> SaveReward(int? id, LoyaltyRewardInput req, CancellationToken ct = default)
    {
        var title = (req.Title ?? "").Trim();
        if (title.Length is 0 or > 60) throw new DomainException("Give the reward a name (up to 60 characters).");
        if (req.PointsCost is < 1 or > 1_000_000) throw new DomainException("A reward must cost at least 1 point.");

        LoyaltyReward r;
        if (id is int rid)
            r = await _db.LoyaltyRewards.FirstOrDefaultAsync(x => x.Id == rid, ct) ?? throw new DomainException("That reward no longer exists.");
        else
        {
            if (await _db.LoyaltyRewards.CountAsync(ct) >= 50) throw new DomainException("You can have up to 50 rewards.");
            r = new LoyaltyReward();
            _db.LoyaltyRewards.Add(r);
        }
        r.Title = title;
        r.Description = Clip(req.Description, 200);
        r.Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? "🎁" : Clip(req.Emoji, 16);
        r.PointsCost = req.PointsCost;
        r.Active = req.Active;
        r.SortOrder = req.SortOrder;
        await _db.SaveChangesAsync(ct);
        return ToDto(r);
    }

    public async Task DeleteReward(int id, CancellationToken ct = default)
    {
        var r = await _db.LoyaltyRewards.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return;
        // Waiting requests keep their snapshot (title, cost) and can still be approved or rejected.
        _db.LoyaltyRewards.Remove(r);
        await _db.SaveChangesAsync(ct);
    }

    // ───────────────────────── guest: join, me, link ─────────────────────────

    public async Task<LoyaltyMeDto> Join(LoyaltyJoinInput req, CancellationToken ct = default)
    {
        var p = await Program(ct);
        if (!p.Enabled) throw new DomainException("Rewards aren't available here right now.");
        var phone = NormalizePhone(req.Phone);
        var (member, created, bonus) = await FindOrCreate(phone, req.Name, p, by: "", ct);
        if (!string.IsNullOrWhiteSpace(req.TableCode)) await LinkSession(member, req.TableCode!, ct);
        await _db.SaveChangesAsync(ct);
        return await Me(member, created, bonus, ct);
    }

    /// <summary>The member a guest's phone remembers, or null when the token is unknown (e.g. deleted).</summary>
    public async Task<LoyaltyMeDto?> Me(string? token, CancellationToken ct = default)
    {
        var m = await ByToken(token, ct);
        return m is null ? null : await Me(m, false, 0, ct);
    }

    /// <summary>Tie the guest's open seating at this table to their membership, so its bill earns points.</summary>
    public async Task<bool> LinkTable(string? token, string tableCode, CancellationToken ct = default)
    {
        var p = await Program(ct);
        if (!p.Enabled) return false;
        var m = await ByToken(token, ct) ?? throw new DomainException("Please join rewards again on this phone.");
        var linked = await LinkSession(m, tableCode, ct);
        await _db.SaveChangesAsync(ct);
        return linked;
    }

    // ───────────────────────── guest: redeem ─────────────────────────

    public async Task<LoyaltyRedemptionDto> Redeem(string? token, LoyaltyRedeemInput req, CancellationToken ct = default)
    {
        var p = await Program(ct);
        if (!p.Enabled) throw new DomainException("Rewards aren't available here right now.");
        var m = await ByToken(token, ct, track: true) ?? throw new DomainException("Please join rewards again on this phone.");
        var reward = await _db.LoyaltyRewards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RewardId && r.Active, ct)
            ?? throw new DomainException("That reward isn't available any more.");

        var pending = await _db.LoyaltyRedemptions.Where(x => x.MemberId == m.Id && x.Status == RedemptionStatus.Pending).ToListAsync(ct);
        if (pending.Count >= MaxPendingPerMember) throw new DomainException("You already have rewards waiting for staff — show them your code first.");
        var available = m.Points - pending.Sum(x => x.PointsCost);
        if (available < reward.PointsCost)
            throw new DomainException($"You need {reward.PointsCost - available:N0} more points for this reward.");

        var red = new LoyaltyRedemption
        {
            MemberId = m.Id,
            RewardId = reward.Id,
            RewardTitle = reward.Title,
            RewardEmoji = reward.Emoji,
            PointsCost = reward.PointsCost,
            Code = await NewCode(ct),
            TableCode = Clip(req.TableCode, 20),
            RequestedAt = DateTime.UtcNow,
        };
        _db.LoyaltyRedemptions.Add(red);
        await _db.SaveChangesAsync(ct);
        return ToDto(red, m);
    }

    public async Task CancelRedemption(string? token, int id, CancellationToken ct = default)
    {
        var m = await ByToken(token, ct) ?? throw new DomainException("Please join rewards again on this phone.");
        var red = await _db.LoyaltyRedemptions.FirstOrDefaultAsync(x => x.Id == id && x.MemberId == m.Id, ct)
            ?? throw new DomainException("That request wasn't found.");
        red.Cancel();
        await _db.SaveChangesAsync(ct);
    }

    // ───────────────────────── earning ─────────────────────────

    /// <summary>
    /// Points for a seating's settled bill. Called by billing after the sale is saved; idempotent (a
    /// seating earns once) and a no-op when the seating has no member or the programme is off.
    /// Returns the points added.
    /// </summary>
    public async Task<int> AwardForSettledSession(int sessionId, int? invoiceId, decimal billTotal, CancellationToken ct = default)
    {
        var memberId = await _db.Sessions.Where(s => s.Id == sessionId).Select(s => s.LoyaltyMemberId).FirstOrDefaultAsync(ct);
        if (memberId is not int mid) return 0;
        var p = await Program(ct);
        if (!p.Enabled) return 0;
        if (await _db.LoyaltyTransactions.AnyAsync(t => t.TableSessionId == sessionId && t.Type == LoyaltyTxnType.Earn, ct)) return 0;
        var m = await _db.LoyaltyMembers.FirstOrDefaultAsync(x => x.Id == mid, ct);
        if (m is null) return 0;

        var points = p.PointsFor(billTotal);
        m.Visits++;
        m.LifetimeSpend += Math.Max(0, billTotal);
        m.LastVisitAt = DateTime.UtcNow;
        // the ledger line is written even for 0 points: it is what marks this seating as counted
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            MemberId = m.Id, Type = LoyaltyTxnType.Earn, Points = m.Apply(points), BillAmount = billTotal,
            TableSessionId = sessionId, InvoiceId = invoiceId, CreatedAt = DateTime.UtcNow,
            Note = points > 0 ? "" : "Bill below the earning amount",
        });
        await _db.SaveChangesAsync(ct);
        return points;
    }

    /// <summary>The in-house rating bonus (at most once every <see cref="FeedbackBonusHours"/> hours). Returns the points added.</summary>
    public async Task<int> AwardFeedback(string? token, CancellationToken ct = default)
    {
        var p = await Program(ct);
        if (!p.Enabled || p.FeedbackBonus <= 0) return 0;
        var m = await ByToken(token, ct, track: true);
        if (m is null) return 0;
        var since = DateTime.UtcNow.AddHours(-FeedbackBonusHours);
        if (await _db.LoyaltyTransactions.AnyAsync(t => t.MemberId == m.Id && t.Type == LoyaltyTxnType.Feedback && t.CreatedAt >= since, ct)) return 0;
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            MemberId = m.Id, Type = LoyaltyTxnType.Feedback, Points = m.Apply(p.FeedbackBonus), Note = "Thanks for your rating",
        });
        await _db.SaveChangesAsync(ct);
        return p.FeedbackBonus;
    }

    // ───────────────────────── staff ─────────────────────────

    public async Task<IReadOnlyList<LoyaltyRedemptionDto>> Redemptions(string? status, CancellationToken ct = default)
    {
        var q = _db.LoyaltyRedemptions.AsNoTracking();
        if (Enum.TryParse<RedemptionStatus>(status, true, out var st)) q = q.Where(x => x.Status == st);
        var list = await q.OrderByDescending(x => x.RequestedAt).Take(100).ToListAsync(ct);
        var ids = list.Select(x => x.MemberId).Distinct().ToList();
        var members = await _db.LoyaltyMembers.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        return list.Select(x => ToDto(x, members.GetValueOrDefault(x.MemberId))).ToList();
    }

    public async Task<LoyaltyRedemptionDto> Approve(int id, string by, CancellationToken ct = default)
    {
        var red = await _db.LoyaltyRedemptions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("That request wasn't found.");
        var m = await _db.LoyaltyMembers.FirstOrDefaultAsync(x => x.Id == red.MemberId, ct) ?? throw new DomainException("This member no longer exists.");
        if (m.Points < red.PointsCost) throw new DomainException($"{m.Name} has only {m.Points:N0} points now — not enough for this reward.");
        red.Approve(by);
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            MemberId = m.Id, Type = LoyaltyTxnType.Redeem, Points = m.Apply(-red.PointsCost), RedemptionId = red.Id,
            Note = $"{red.RewardEmoji} {red.RewardTitle}".Trim(), CreatedBy = by,
        });
        await _db.SaveChangesAsync(ct);
        return ToDto(red, m);
    }

    public async Task<LoyaltyRedemptionDto> Reject(int id, string by, string? note, CancellationToken ct = default)
    {
        var red = await _db.LoyaltyRedemptions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("That request wasn't found.");
        red.Reject(by, Clip(note, 200));
        await _db.SaveChangesAsync(ct);
        var m = await _db.LoyaltyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == red.MemberId, ct);
        return ToDto(red, m);
    }

    /// <summary>Who a table's running bill earns points for, and their waiting reward requests.</summary>
    public async Task<TableMemberDto> TableMember(string tableCode, CancellationToken ct = default)
    {
        var p = await Program(ct);
        var memberId = await OpenSessionMember(tableCode, ct);
        LoyaltyMember? m = memberId is int mid ? await _db.LoyaltyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mid, ct) : null;
        var pending = m is null ? new List<LoyaltyRedemptionDto>()
            : (await _db.LoyaltyRedemptions.AsNoTracking().Where(x => x.MemberId == m.Id && x.Status == RedemptionStatus.Pending)
                .OrderBy(x => x.RequestedAt).ToListAsync(ct)).Select(x => ToDto(x, m)).ToList();
        return new TableMemberDto(p.Enabled, m is null ? null : ToDto(m), pending, p.EarnPoints, p.PerAmount, p.MinBillAmount);
    }

    /// <summary>Reception attaches a member (found or created by phone) to a table's running bill.</summary>
    public async Task<TableMemberDto> AttachToTable(string tableCode, LoyaltyAttachInput req, string by, CancellationToken ct = default)
    {
        var p = await Program(ct);
        if (!p.Enabled) throw new DomainException("Switch rewards on in Loyalty first.");
        var session = await OpenSession(tableCode, ct) ?? throw new DomainException("This table has no open bill.");
        var (member, _, _) = await FindOrCreate(NormalizePhone(req.Phone), req.Name, p, by, ct);
        if (member.Id == 0) await _db.SaveChangesAsync(ct);   // need the id before linking
        session.LoyaltyMemberId = member.Id;                    // reception's choice wins over an earlier link
        await _db.SaveChangesAsync(ct);
        return await TableMember(tableCode, ct);
    }

    public async Task DetachFromTable(string tableCode, CancellationToken ct = default)
    {
        var session = await OpenSession(tableCode, ct);
        if (session is null) return;
        session.LoyaltyMemberId = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<LoyaltyMembersPageDto> Members(string? q, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = _db.LoyaltyMembers.AsNoTracking();
        var term = (q ?? "").Trim();
        if (term.Length > 0)
        {
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var lower = term.ToLower();
            query = query.Where(m => m.Name.ToLower().Contains(lower) || (digits.Length > 0 && m.Phone.Contains(digits)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(m => m.LastVisitAt ?? m.JoinedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new LoyaltyMembersPageDto(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<LoyaltyMemberDetailDto> Member(int id, CancellationToken ct = default)
    {
        var m = await _db.LoyaltyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("That member wasn't found.");
        var history = await _db.LoyaltyTransactions.AsNoTracking().Where(t => t.MemberId == id).OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct);
        var reds = await _db.LoyaltyRedemptions.AsNoTracking().Where(x => x.MemberId == id).OrderByDescending(x => x.RequestedAt).Take(50).ToListAsync(ct);
        return new LoyaltyMemberDetailDto(ToDto(m), history.Select(ToDto).ToList(), reds.Select(x => ToDto(x, m)).ToList());
    }

    public async Task<LoyaltyMemberDetailDto> Adjust(int id, LoyaltyAdjustInput req, string by, CancellationToken ct = default)
    {
        if (req.Points == 0 || Math.Abs(req.Points) > 100_000) throw new DomainException("Enter the points to add (or take off with a minus), up to 100,000.");
        var note = Clip(req.Note, 200);
        if (note.Length == 0) throw new DomainException("Add a short reason, so the history explains the change.");
        var m = await _db.LoyaltyMembers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new DomainException("That member wasn't found.");
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction { MemberId = m.Id, Type = LoyaltyTxnType.Adjust, Points = m.Apply(req.Points), Note = note, CreatedBy = by });
        await _db.SaveChangesAsync(ct);
        return await Member(id, ct);
    }

    public async Task<LoyaltyOverviewDto> Overview(CancellationToken ct = default)
    {
        var p = await Program(ct);
        var since = DateTime.UtcNow.AddDays(-30);
        var members = await _db.LoyaltyMembers.AsNoTracking().ToListAsync(ct);
        var txns = await _db.LoyaltyTransactions.AsNoTracking().Select(t => new { t.Type, t.Points }).ToListAsync(ct);
        var statuses = await _db.LoyaltyRedemptions.AsNoTracking().Select(x => x.Status).ToListAsync(ct);
        var issued = txns.Where(t => t.Points > 0).Sum(t => t.Points);
        var redeemed = -txns.Where(t => t.Type == LoyaltyTxnType.Redeem).Sum(t => t.Points);
        var top = members.OrderByDescending(m => m.LifetimePoints).Take(5).Select(ToDto).ToList();
        return new LoyaltyOverviewDto(p.Enabled, members.Count,
            members.Count(m => (m.LastVisitAt ?? DateTime.MinValue) >= since), members.Count(m => m.JoinedAt >= since),
            issued, redeemed, members.Sum(m => m.Points),
            statuses.Count(s => s == RedemptionStatus.Pending), statuses.Count(s => s == RedemptionStatus.Approved),
            await ReviewStats(ct), top);
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>
    /// Pakistani numbers in any common form (0300 1234567, +92 300 1234567, 92300…, 300…) become
    /// 03001234567; other numbers keep their digits (7–15).
    /// </summary>
    public static string NormalizePhone(string? raw)
    {
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (d.StartsWith("0092")) d = "0" + d[4..];
        else if (d.StartsWith("92") && d.Length == 12) d = "0" + d[2..];
        else if (d.StartsWith("3") && d.Length == 10) d = "0" + d;
        if (d.Length is < 7 or > 15) throw new DomainException("Enter a valid mobile number, e.g. 0300 1234567.");
        return d;
    }

    public static string MaskPhone(string phone) =>
        phone.Length <= 6 ? phone : phone[..4] + new string('•', Math.Max(1, phone.Length - 7)) + phone[^3..];

    private async Task<(LoyaltyMember member, bool created, int bonus)> FindOrCreate(string phone, string? name, LoyaltyProgram p, string by, CancellationToken ct)
    {
        var cleanName = Clip(name, 60);
        var m = await _db.LoyaltyMembers.FirstOrDefaultAsync(x => x.Phone == phone, ct);
        if (m is not null)
        {
            // never overwrite a name someone already gave; fill it in if it was empty
            if (m.Name.Length == 0 && cleanName.Length > 0) m.Name = cleanName;
            if (m.Token.Length == 0) m.Token = NewToken();
            return (m, false, 0);
        }
        m = new LoyaltyMember { Phone = phone, Name = cleanName.Length > 0 ? cleanName : "Guest", Token = NewToken(), JoinedAt = DateTime.UtcNow };
        _db.LoyaltyMembers.Add(m);
        var bonus = 0;
        if (p.WelcomeBonus > 0)
        {
            bonus = m.Apply(p.WelcomeBonus);
            await _db.SaveChangesAsync(ct);   // the member's id, for the ledger line
            _db.LoyaltyTransactions.Add(new LoyaltyTransaction { MemberId = m.Id, Type = LoyaltyTxnType.Welcome, Points = bonus, Note = "Welcome bonus", CreatedBy = by });
        }
        return (m, true, bonus);
    }

    private async Task<bool> LinkSession(LoyaltyMember m, string tableCode, CancellationToken ct)
    {
        var session = await OpenSession(tableCode, ct);
        if (session is null) return false;                 // no order yet: the guest page links again after ordering
        if (session.LoyaltyMemberId is int other && other != m.Id) return false;   // first member at the table keeps it
        if (m.Id == 0) await _db.SaveChangesAsync(ct);
        session.LoyaltyMemberId = m.Id;
        return true;
    }

    private Task<Domain.Tables.TableSession?> OpenSession(string tableCode, CancellationToken ct)
    {
        var code = (tableCode ?? "").Trim();
        return _db.Sessions.Where(s => s.IsOpen && s.TableCode == code).OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct);
    }

    private async Task<int?> OpenSessionMember(string tableCode, CancellationToken ct)
    {
        var code = (tableCode ?? "").Trim();
        return await _db.Sessions.AsNoTracking().Where(s => s.IsOpen && s.TableCode == code).OrderByDescending(s => s.OpenedAt)
            .Select(s => s.LoyaltyMemberId).FirstOrDefaultAsync(ct);
    }

    private async Task<LoyaltyMember?> ByToken(string? token, CancellationToken ct, bool track = false)
    {
        var t = (token ?? "").Trim();
        if (t.Length < 16) return null;
        var q = track ? _db.LoyaltyMembers : _db.LoyaltyMembers.AsNoTracking();
        return await q.FirstOrDefaultAsync(m => m.Token == t, ct);
    }

    private async Task<LoyaltyMeDto> Me(LoyaltyMember m, bool justJoined, int bonus, CancellationToken ct)
    {
        var reds = await _db.LoyaltyRedemptions.AsNoTracking().Where(x => x.MemberId == m.Id)
            .OrderByDescending(x => x.RequestedAt).Take(10).ToListAsync(ct);
        var history = await _db.LoyaltyTransactions.AsNoTracking().Where(t => t.MemberId == m.Id)
            .OrderByDescending(t => t.CreatedAt).Take(15).ToListAsync(ct);
        var waiting = reds.Where(x => x.Status == RedemptionStatus.Pending).Sum(x => x.PointsCost);
        return new LoyaltyMeDto(m.Token, m.Name, MaskPhone(m.Phone), m.Points, Math.Max(0, m.Points - waiting), m.LifetimePoints,
            m.Visits, Utc(m.JoinedAt), justJoined, bonus, reds.Select(x => ToDto(x, m)).ToList(), history.Select(ToDto).ToList());
    }

    private static readonly char[] CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private async Task<string> NewCode(CancellationToken ct)
    {
        for (var i = 0; ; i++)
        {
            var sb = new StringBuilder(6);
            for (var k = 0; k < 6; k++) sb.Append(CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)]);
            var code = sb.ToString();
            if (i > 8 || !await _db.LoyaltyRedemptions.AnyAsync(x => x.Code == code && x.Status == RedemptionStatus.Pending, ct)) return code;
        }
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string Clip(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static string Url(string? raw, string what)
    {
        var u = (raw ?? "").Trim();
        if (u.Length == 0) return "";
        if (u.Length > 400 || !Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new DomainException($"The {what} must be a full web address starting with https://");
        return u;
    }

    /// <summary>SQLite and Npgsql hand dates back without a kind; the UI needs them marked UTC (AGENT_NOTES).</summary>
    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d is DateTime x ? Utc(x) : null;

    private static LoyaltyRewardDto ToDto(LoyaltyReward r) => new(r.Id, r.Title, r.Description, r.Emoji, r.PointsCost, r.Active, r.SortOrder);
    private static LoyaltyTxnDto ToDto(LoyaltyTransaction t) => new(t.Id, t.Type.ToString(), t.Points, t.BillAmount, t.Note, Utc(t.CreatedAt));
    private static LoyaltyMemberDto ToDto(LoyaltyMember m) =>
        new(m.Id, m.Name, m.Phone, m.Points, m.LifetimePoints, m.LifetimeSpend, m.Visits, Utc(m.JoinedAt), Utc(m.LastVisitAt));
    private static LoyaltyRedemptionDto ToDto(LoyaltyRedemption x, LoyaltyMember? m) =>
        new(x.Id, x.RewardTitle, x.RewardEmoji, x.PointsCost, x.Status.ToString(), x.Code, x.TableCode, Utc(x.RequestedAt),
            Utc(x.DecidedAt), x.DecidedBy, x.Note, x.MemberId, m?.Name ?? "", m?.Phone ?? "");
}
