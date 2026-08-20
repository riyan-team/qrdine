using Microsoft.EntityFrameworkCore;
using QrDine.Application.Common;

namespace QrDine.Api.Endpoints;

// Public: serve uploaded files (menu photos) stored in the database. The id is an
// unguessable GUID; responses are cached hard since a file at an id never changes.
public static class FilesEndpoints
{
    public static void MapFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/files/{id}", async (string id, HttpContext http, IAppDbContext db, CancellationToken ct) =>
        {
            var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(f.Data, f.ContentType);
        });
    }
}
