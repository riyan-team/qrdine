namespace QrDine.Domain.Files;

/// <summary>
/// An uploaded file (menu photo) kept in the database so it survives restarts and
/// redeploys — the app's disk on Render is ephemeral, so local files would vanish.
/// Served publicly by its unguessable id at /api/files/{id}. Not tenant-filtered
/// (guests scanning a menu must read it without auth); RestaurantId is kept for
/// ownership/cleanup only.
/// </summary>
public class StoredFile
{
    public string Id { get; set; } = "";          // GUID hex — the URL segment
    public string ContentType { get; set; } = "";  // image/jpeg, image/png, image/webp
    public byte[] Data { get; set; } = System.Array.Empty<byte>();
    public int RestaurantId { get; set; }
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
}
