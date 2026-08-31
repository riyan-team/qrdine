using QrDine.Application.Common;
using QrDine.Domain.Files;

namespace QrDine.Infrastructure.Storage;

/// <summary>
/// Stores uploaded files as rows in the database and serves them at /api/files/{id}.
/// Unlike LocalFileStorage, these survive redeploys/restarts on an ephemeral-disk
/// host like Render — no external object store or credentials needed.
/// </summary>
public class DbFileStorage : IFileStorage
{
    private readonly IAppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public DbFileStorage(IAppDbContext db, ICurrentTenant tenant)
    {
        _db = db; _tenant = tenant;
    }

    public async Task<string> Save(Stream content, string extension, string folder, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        var id = Guid.NewGuid().ToString("N");
        _db.StoredFiles.Add(new StoredFile
        {
            Id = id,
            ContentType = ContentTypeFor(extension),
            Data = ms.ToArray(),
            RestaurantId = _tenant.RestaurantId ?? 0,
        });
        await _db.SaveChangesAsync(ct);
        return $"/api/files/{id}";
    }

    private static string ContentTypeFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "image/jpeg",
    };
}
