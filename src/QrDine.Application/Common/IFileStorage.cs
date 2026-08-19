namespace QrDine.Application.Common;

/// <summary>
/// Stores uploaded files (menu photos) and returns a public URL. Local disk today;
/// swap for S3 / Cloudflare R2 in the cloud without touching callers.
/// </summary>
public interface IFileStorage
{
    Task<string> Save(Stream content, string extension, string folder, CancellationToken ct = default);
}
