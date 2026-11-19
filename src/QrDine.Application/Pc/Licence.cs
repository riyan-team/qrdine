using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QrDine.Application.Pc;

/// <summary>
/// What a PC edition licence says. Signed by the seller's private key; the app only has the public key, so a
/// licence can be checked on a PC with no internet but can't be made or changed there.
/// </summary>
public sealed record LicenceInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("r")] string Restaurant,
    [property: JsonPropertyName("m")] string Machine,
    [property: JsonPropertyName("i")] DateOnly Issued,
    [property: JsonPropertyName("e")] DateOnly? Expires,
    [property: JsonPropertyName("f")] IReadOnlyList<string> Features)
{
    [JsonPropertyName("v")] public int Version { get; init; } = 1;
}

public enum LicenceState
{
    Missing,        // nothing entered yet
    Invalid,        // not a licence, or changed after it was signed
    WrongComputer,  // a real licence, for another PC
    Active,
    EndingSoon,     // works; the owner is reminded
    Grace,          // past the date: still works for a few days, with a loud warning
    Expired,        // grace is over: records can be read, nothing new can be made
    ClockBack,      // the PC's clock is earlier than a time it already saw: fix the clock
}

public sealed record LicenceStatus(LicenceState State, LicenceInfo? Licence, int DaysLeft, string Message)
{
    /// <summary>Orders, bills and every other change are allowed.</summary>
    public bool CanWork => State is LicenceState.Active or LicenceState.EndingSoon or LicenceState.Grace;

    /// <summary>Signing in and reading the records still work, so a restaurant is never cut off from its own data.</summary>
    public bool CanRead => CanWork || State is LicenceState.Expired or LicenceState.ClockBack;
}

/// <summary>Reads and checks licence keys: <c>DINEIN1.&lt;payload&gt;.&lt;signature&gt;</c>, both base64url.</summary>
public sealed class LicenceVerifier
{
    public const string Prefix = "DINEIN1.";
    public const int WarnDays = 14;
    public const int GraceDays = 7;

    /// <summary>Every feature switch a licence can turn on.</summary>
    public static readonly string[] AllFeatures = ["ordering", "kitchen", "waiter", "billing", "inventory", "reports", "fbr"];

    private readonly byte[] _publicKey;

    /// <param name="publicKeySpki">The seller's ECDSA P-256 public key (SubjectPublicKeyInfo, base64).</param>
    public LicenceVerifier(string publicKeySpki) => _publicKey = Convert.FromBase64String(publicKeySpki);

    internal static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>The licence a key holds, or null with the reason when it isn't a real one.</summary>
    public LicenceInfo? Read(string? key, out string why)
    {
        why = "";
        var k = Clean(key);
        if (k.Length == 0) { why = "No licence key was entered."; return null; }
        if (!k.StartsWith(Prefix, StringComparison.Ordinal)) { why = "That isn't a Dine In licence key. It starts with DINEIN1."; return null; }
        var parts = k[Prefix.Length..].Split('.');
        if (parts.Length != 2) { why = "The licence key is cut short or has extra text. Copy all of it again."; return null; }
        byte[] payload, sig;
        try { payload = B64.Decode(parts[0]); sig = B64.Decode(parts[1]); }
        catch (FormatException) { why = "The licence key is damaged. Copy all of it again."; return null; }

        using (var ec = ECDsa.Create())
        {
            ec.ImportSubjectPublicKeyInfo(_publicKey, out _);
            if (sig.Length != 64 || !ec.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                why = "This licence key isn't genuine, or it was changed. Ask your Dine In seller for a new one.";
                return null;
            }
        }
        try
        {
            var info = JsonSerializer.Deserialize<LicenceInfo>(payload, Json);
            if (info is null || info.Version != 1 || string.IsNullOrWhiteSpace(info.Machine)) { why = "This licence is for a newer Dine In. Update the app."; return null; }
            return info;
        }
        catch (JsonException) { why = "This licence is for a newer Dine In. Update the app."; return null; }
    }

    /// <summary>What a key means on this PC today.</summary>
    /// <param name="lastSeenUtc">The latest time this PC has already run at, to catch a clock moved back.</param>
    public LicenceStatus Evaluate(string? key, string machineCode, DateTime nowUtc, DateTime? lastSeenUtc = null)
    {
        if (Clean(key).Length == 0) return new(LicenceState.Missing, null, 0, "This PC isn't activated yet. Enter the licence key from your Dine In seller.");
        var info = Read(key, out var why);
        if (info is null) return new(LicenceState.Invalid, null, 0, why);
        if (MachineCodeText.Normalize(info.Machine) != MachineCodeText.Normalize(machineCode))
            return new(LicenceState.WrongComputer, info, 0,
                $"This licence is for another computer ({MachineCodeText.Pretty(info.Machine)}). This PC's code is {MachineCodeText.Pretty(machineCode)}: ask your seller for a licence for it.");
        if (lastSeenUtc is DateTime seen && seen > DateTime.MinValue.AddDays(2) && nowUtc < seen.AddHours(-24))
            return new(LicenceState.ClockBack, info, 0,
                $"This PC's date is set earlier than {seen:d MMM yyyy}, when Dine In last ran. Set the correct date and time, then restart Dine In.");
        if (info.Expires is not DateOnly until)
            return new(LicenceState.Active, info, 0, $"Licensed to {info.Restaurant}, with no end date.");

        // The licence works through its last day in Pakistan (it ends at 5 am the next morning, Pakistan time).
        var endsUtc = until.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);
        var lockUtc = endsUtc.AddDays(GraceDays);
        if (nowUtc >= lockUtc)
            return new(LicenceState.Expired, info, 0,
                $"The licence ended on {until:d MMM yyyy}. You can still sign in and see your records, but new orders and bills need a renewed licence.");
        if (nowUtc >= endsUtc)
        {
            var graceLeft = DaysThrough(until.AddDays(GraceDays), nowUtc);
            return new(LicenceState.Grace, info, graceLeft,
                $"The licence ended on {until:d MMM yyyy}. Dine In keeps working for {graceLeft} more day(s), counting today. Renew it now.");
        }
        var left = DaysThrough(until, nowUtc);
        if (left <= WarnDays)
            return new(LicenceState.EndingSoon, info, left, $"The licence ends on {until:d MMM yyyy} ({left} day(s) left, counting today). Ask your seller to renew it.");
        return new(LicenceState.Active, info, left, $"Licensed to {info.Restaurant} until {until:d MMM yyyy}.");
    }

    /// <summary>Whole days in Pakistan from today through <paramref name="lastDay"/>, today included (never under 1 while it still works).</summary>
    private static int DaysThrough(DateOnly lastDay, DateTime nowUtc) =>
        Math.Max(1, lastDay.DayNumber - DateOnly.FromDateTime(nowUtc.AddHours(5)).DayNumber + 1);
    private static string Clean(string? key) => new((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
}

/// <summary>Makes licence keys. Used only by the seller's licence tool (and the tests), never by the app.</summary>
public static class LicenceIssuer
{
    public static string Issue(ECDsa privateKey, LicenceInfo info)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(info with { Machine = MachineCodeText.Normalize(info.Machine) }, LicenceVerifier.Json);
        var sig = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return LicenceVerifier.Prefix + B64.Encode(payload) + "." + B64.Encode(sig);
    }
}

/// <summary>A machine code as people read and type it: 16 letters and digits in groups of four.</summary>
public static class MachineCodeText
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no I/O/0/1

    public static string Normalize(string? code) =>
        new((code ?? "").ToUpperInvariant().Where(c => Alphabet.Contains(c)).ToArray());

    public static string Pretty(string? code)
    {
        var n = Normalize(code);
        return string.Join("-", Enumerable.Range(0, (n.Length + 3) / 4).Select(i => n.Substring(i * 4, Math.Min(4, n.Length - i * 4))));
    }

    /// <summary>The code for a computer's own id: a hash, so the id itself is never shown or sent anywhere.</summary>
    public static string FromId(string machineId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("dinein-machine|" + machineId.Trim().ToLowerInvariant()));
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in hash)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5 && sb.Length < 16) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            if (sb.Length == 16) break;
        }
        return Pretty(sb.ToString());
    }
}

internal static class B64
{
    public static string Encode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }
}
