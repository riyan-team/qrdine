using Microsoft.EntityFrameworkCore;

namespace QrDine.Application.Common;

/// <summary>
/// What a failed save means, for both databases (Postgres on the server, SQLite on the restaurant PC), without this
/// layer depending on either driver.
/// </summary>
public static class DbErrors
{
    /// <summary>Two saves wanted the same unique value (an invoice or ticket number): try again with the next one.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (Prop(e, "SqlState") is string state) return state == "23505";
            if (Prop(e, "SqliteExtendedErrorCode") is int ext) return ext is 2067 or 1555;   // UNIQUE / PRIMARY KEY
            if (e.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>A number too big for its column, or a text too long for it: the person's input, not a fault here.</summary>
    public static bool IsValueTooBig(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (Prop(e, "SqlState") is "22003" or "22001") return true;   // numeric_value_out_of_range, string_data_right_truncation
        return false;
    }

    private static object? Prop(Exception e, string name) => e.GetType().GetProperty(name)?.GetValue(e);
}
