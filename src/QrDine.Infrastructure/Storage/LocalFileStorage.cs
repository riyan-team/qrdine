using Microsoft.AspNetCore.Hosting;
using QrDine.Application.Common;

namespace QrDine.Infrastructure.Storage;

/// <summary>Saves files under wwwroot/uploads and serves them as /uploads/... .</summary>
public class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    public LocalFileStorage(IWebHostEnvironment env) => _env = env;

    public async Task<string> Save(Stream content, string extension, string folder, CancellationToken ct = default)
    {
        var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(root, "uploads", folder);
        Directory.CreateDirectory(dir);

        var name = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(dir, name);
        await using (var file = File.Create(path))
            await content.CopyToAsync(file, ct);

        return $"/uploads/{folder}/{name}";
    }
}
