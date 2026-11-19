using System.Collections.Concurrent;
using QrDine.Domain.Common;

namespace QrDine.Application.Auth;

/// <summary>Where the current request comes from (the client's address), so a lock-out can stay with the guesser.</summary>
public interface IRequestSource
{
    string? Current { get; }
}

/// <summary>
/// Stops password guessing against one account without letting a stranger lock the owner out.
/// Wrong passwords count per account AND per address they come from:
/// - 10 wrong passwords from one address within 15 minutes rest the account for 15 minutes, but only for that address,
///   so someone who knows the owner's mobile number can't lock the owner out of their own phone or the counter PC;
/// - 50 wrong passwords from anywhere (a guesser switching addresses) rest the account everywhere for 15 minutes.
/// A right password resets both counts. One app process holds the counts (see the one-process rule in
/// deploy/hostworld/README.md).
/// </summary>
public class LoginGuard
{
    public const int MaxFailures = 10;
    public const int MaxFailuresAnywhere = 50;
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
    private readonly IRequestSource? _source;

    public LoginGuard() : this(() => DateTime.UtcNow) { }
    public LoginGuard(Func<DateTime> now, IRequestSource? source = null) { _now = now; _source = source; }

    private string Here(string account) => account + "|" + (_source?.Current ?? "");
    private static string Anywhere(string account) => account + "|*";

    /// <summary>Throws when this account is resting after too many wrong passwords (from here, or from everywhere).</summary>
    public void EnsureAllowed(string account)
    {
        var until = LockedUntil(Here(account)) ?? LockedUntil(Anywhere(account));
        if (until is DateTime u)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((u - _now()).TotalMinutes));
            throw new DomainException($"Too many wrong passwords for this account. Please wait {minutes} minute{(minutes == 1 ? "" : "s")} and try again.");
        }
    }

    private DateTime? LockedUntil(string key)
    {
        if (!_entries.TryGetValue(key, out var e)) return null;
        DateTime? until;
        lock (e) until = e.LockedUntil;
        return until is DateTime u && u > _now() ? u : null;
    }

    public void Failed(string account)
    {
        var now = _now();
        if (_entries.Count > MaxTracked) Sweep(now);
        Count(Here(account), MaxFailures, now);
        Count(Anywhere(account), MaxFailuresAnywhere, now);
    }

    private void Count(string key, int limit, DateTime now)
    {
        var e = _entries.GetOrAdd(key, _ => new Entry { Since = now });
        lock (e)
        {
            if (e.LockedUntil is DateTime u && u <= now) { e.LockedUntil = null; e.Failures = 0; e.Since = now; }
            if (now - e.Since > Window) { e.Failures = 0; e.Since = now; }
            e.Failures++;
            if (e.Failures >= limit) { e.LockedUntil = now + Window; e.Failures = 0; e.Since = now; }
        }
    }

    public void Succeeded(string account)
    {
        _entries.TryRemove(Here(account), out _);
        _entries.TryRemove(Anywhere(account), out _);
    }

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
