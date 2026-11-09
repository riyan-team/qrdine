namespace QrDine.Application.Auth;

/// <summary>
/// One spelling for a sign-in name, so "0300 1234567", "+92 300 1234567" and "03001234567"
/// are the same person. Anything that isn't a phone number is simply trimmed and lowercased.
/// </summary>
public static class LoginName
{
    public static string Normalize(string? input)
    {
        var text = (input ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0) return text;

        var phoneLike = text.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')');
        if (!phoneLike) return text;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 10) return digits;
        if (digits.StartsWith("0092")) digits = digits[4..];
        else if (digits.StartsWith("92") && digits.Length == 12) digits = digits[2..];
        if (digits.Length == 10 && digits[0] == '3') digits = "0" + digits;
        return digits;
    }
}
