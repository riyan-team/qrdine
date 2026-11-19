namespace QrDine.Application.Common;

/// <summary>
/// One rule for a phone number typed into any form: digits, with spaces, dashes, brackets or a
/// leading + allowed, and 7 to 15 digits in all. That fits mobiles ("0300 1234567", "+92 300 1234567")
/// and city landlines ("021-1234567"), and stops a name or a typo being saved as a number.
/// </summary>
public static class Phone
{
    public const string Message = "Enter a phone number with 7 to 15 digits.";

    public static bool IsValid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var digits = 0;
        foreach (var c in raw.Trim())
        {
            if (char.IsAsciiDigit(c)) digits++;
            else if (c is not ('+' or '-' or '(' or ')' or ' ')) return false;
        }
        return digits is >= 7 and <= 15;
    }

    /// <summary>For optional phone fields: blank is fine, anything typed must be a real number.</summary>
    public static bool IsBlankOrValid(string? raw) => string.IsNullOrWhiteSpace(raw) || IsValid(raw);
}
