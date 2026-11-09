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
}
