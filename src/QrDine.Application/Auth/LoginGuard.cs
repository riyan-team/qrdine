using System.Collections.Concurrent;
using QrDine.Domain.Common;

namespace QrDine.Application.Auth;

/// <summary>
/// Stops password guessing against one account. The per-address limit alone isn't enough, because
/// a guesser can switch addresses. After 10 wrong passwords within 15 minutes the account refuses
/// every password, right or wrong, for 15 minutes. A right password resets the count.
/// One app process holds the counts (see the one-process rule in deploy/hostworld/README.md).
/// </summary>
public class LoginGuard
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaxTracked = 20_000;

    private sealed class Entry
    {
        public int Failures;
        public DateTime Since;
        public DateTime? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> _now;

    public LoginGuard() : this(() => DateTime.UtcNow) { }
    public LoginGuard(Func<DateTime> now) => _now = now;

    /// <summary>Throws when this account is resting after too many wrong passwords.</summary>
    public void EnsureAllowed(string account)
    {
        if (!_entries.TryGetValue(account, out var e)) return;
        DateTime? until;
        lock (e) until = e.LockedUntil;
        if (until is DateTime u && u > _now())
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((u - _now()).TotalMinutes));
            throw new DomainException($"Too many wrong passwords for this account. Please wait {minutes} minute{(minutes == 1 ? "" : "s")} and try again.");
        }
    }

    public void Failed(string account)
    {
        var now = _now();
        if (_entries.Count > MaxTracked) Sweep(now);
        var e = _entries.GetOrAdd(account, _ => new Entry { Since = now });
        lock (e)
        {
            if (e.LockedUntil is DateTime u && u <= now) { e.LockedUntil = null; e.Failures = 0; e.Since = now; }
            if (now - e.Since > Window) { e.Failures = 0; e.Since = now; }
            e.Failures++;
            if (e.Failures >= MaxFailures) { e.LockedUntil = now + Window; e.Failures = 0; e.Since = now; }
        }
    }

    public void Succeeded(string account) => _entries.TryRemove(account, out _);

    // Someone trying thousands of made-up names mustn't grow this without end.
    private void Sweep(DateTime now)
    {
        foreach (var (key, e) in _entries)
        {
            bool stale;
            lock (e) stale = (e.LockedUntil is null || e.LockedUntil <= now) && now - e.Since > Window;
            if (stale) _entries.TryRemove(key, out _);
        }
    }
}
