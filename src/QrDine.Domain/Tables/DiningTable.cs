using System.Security.Cryptography;
using QrDine.Domain.Common;

namespace QrDine.Domain.Tables;

/// <summary>A physical table. Its Code and QrKey are printed into the QR sticker.</summary>
public class DiningTable : Entity, ITenantOwned
{
    public int RestaurantId { get; set; }
    public int BranchId { get; set; }
    public string Code { get; set; } = "";   // e.g. "T1"
    public string Name { get; set; } = "";   // e.g. "Table 1"

    /// <summary>A real table, or the private one a parcel or delivery ticket gets. Ticket tables
    /// never appear in table lists or QR sheets, and guests can't open them.</summary>
    public Ordering.OrderChannel Kind { get; set; } = Ordering.OrderChannel.Dine;

    /// <summary>
    /// The secret part of the table's QR code. A guest pass is only given to someone who has the key,
    /// so nobody can order for (or watch) a table just by guessing "T1" from home. A new key makes the
    /// printed QR codes of this table stop working, so it only changes when the owner asks.
    /// Every new table gets one; a row saved by an older build has none until staff open the QR page.
    /// </summary>
    public string QrKey { get; set; } = NewQrKey();

    /// <summary>
    /// Guest passes issued before this moment no longer work (the table's bill was settled, its guests
    /// moved, or its QR code was changed). The next guests scan again and get a fresh pass, so the
    /// people who sat here before can't see or change the new guests' orders.
    /// </summary>
    public DateTime? GuestPassesFrom { get; set; }

    private const string KeyAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";   // no 0/o, 1/l/i

    /// <summary>A fresh random key: 10 characters, about 49 bits.</summary>
    public static string NewQrKey()
    {
        Span<char> c = stackalloc char[10];
        for (var i = 0; i < c.Length; i++) c[i] = KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)];
        return new string(c);
    }

    /// <summary>True when the key from a scanned QR code is this table's key. Compared in constant time.</summary>
    public bool KeyMatches(string? key)
    {
        if (string.IsNullOrEmpty(QrKey) || string.IsNullOrWhiteSpace(key)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(QrKey);
        var b = System.Text.Encoding.UTF8.GetBytes(key.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Whether a guest pass issued at this moment (unix seconds) still works. A pass from the very second the bill
    /// was settled counts as from before it (a pass only says the second): those guests just scan again.
    /// </summary>
    public bool PassStillGood(long issuedAtUnix) =>
        GuestPassesFrom is not DateTime from
        || issuedAtUnix > new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)).ToUnixTimeSeconds();

    /// <summary>Ends every guest pass issued for this table until now.</summary>
    public void EndGuestPasses(DateTime nowUtc) => GuestPassesFrom = nowUtc;
}
