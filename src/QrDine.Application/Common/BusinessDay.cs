using System.Globalization;
using QrDine.Domain.Common;

namespace QrDine.Application.Common;

/// <summary>Calendar days as a Pakistani restaurant counts them: UTC+5, whatever the server's clock says.</summary>
public static class BusinessDay
{
    public const int PktOffsetHours = 5;

    public static DateTime Today => DateTime.UtcNow.AddHours(PktOffsetHours).Date;

    /// <summary>"2026-11-09" → that day. Blank means today. A nonsense date is the user's to fix.</summary>
    public static DateTime Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Today;
        if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        throw new DomainException("That date doesn't look right.");
    }

    public static DateTime ToPkt(DateTime utc) => utc.AddHours(PktOffsetHours);
    public static DateTime StartUtc(DateTime day) => day.Date.AddHours(-PktOffsetHours);

    /// <summary>
    /// A report's from/to as the UTC window to search. A plain date ("2026-11-13", what the reports page sends)
    /// means that whole day in Pakistan, so a bill at 1 am stays on the day it was made. A value with a time is
    /// already a moment and is used as given. No from: the last 31 days; no to: now.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) RangeUtc(DateTime? from, DateTime? to, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var fromUtc = from is DateTime f ? (f.TimeOfDay == TimeSpan.Zero ? StartUtc(f) : f) : now.AddDays(-31);
        var toUtc = to is DateTime t ? (t.TimeOfDay == TimeSpan.Zero ? StartUtc(t.AddDays(1)).AddTicks(-10) : t) : now;
        return (fromUtc, toUtc);
    }
}
