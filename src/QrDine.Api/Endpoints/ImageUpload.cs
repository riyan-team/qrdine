using QrDine.Domain.Common;

namespace QrDine.Api.Endpoints;

/// <summary>
/// Robust, dependency-free intake for uploaded images. Reads the file from the
/// multipart request defensively (any field name, never throwing a raw 500),
/// validates it by its actual content (magic bytes) rather than a trusted file
/// extension, and returns friendly, actionable errors. This is the single place
/// menu photos and logos are accepted, so behaviour is identical everywhere.
/// </summary>
public static class ImageUpload
{
    /// <summary>A validated image: raw bytes, a canonical extension and content type.</summary>
    public readonly record struct Image(byte[] Data, string Extension, string ContentType);

    /// <summary>
    /// Pull the first uploaded file off the request and validate it as an image.
    /// Throws <see cref="DomainException"/> (→ friendly 400) for anything wrong —
    /// no file, wrong type, too big — so a bad upload never surfaces as a 500.
    /// </summary>
    public static async Task<Image> ReadAsync(HttpRequest request, long maxBytes, CancellationToken ct = default)
    {
        if (!request.HasFormContentType)
            throw new DomainException("Please choose an image to upload and try again.");

        IFormFileCollection files;
        try { files = (await request.ReadFormAsync(ct)).Files; }
        catch { throw new DomainException("We couldn't read that upload. Please choose an image and try again."); }

        // Take the first non-empty file regardless of the form field name — some
        // browsers/clients name the field unexpectedly, and that should never 500.
        var file = files.FirstOrDefault(f => f.Length > 0);
        if (file is null) throw new DomainException("Please choose an image to upload and try again.");
        if (file.Length > maxBytes)
            throw new DomainException($"That image is too large (max {maxBytes / (1024 * 1024)} MB). Please choose a smaller photo.");

        byte[] data;
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }
        catch { throw new DomainException("We couldn't read that image. Please try again with a different photo."); }

        var kind = Sniff(data);
        if (kind is null)
        {
            // HEIC/HEIF is the iPhone default and can't be shown in most browsers.
            if (IsHeic(data))
                throw new DomainException("iPhone “HEIC” photos aren’t supported. In your Camera settings choose “Most Compatible”, or upload a JPG or PNG.");
            throw new DomainException("Unable to upload image. Please use a clear JPG, PNG or WebP photo and try again.");
        }
        return kind.Value;
    }

    // Identify a supported image purely from its leading bytes (never trust the name).
    private static Image? Sniff(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return new Image(b, ".jpg", "image/jpeg");
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return new Image(b, ".png", "image/png");
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
            && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P')
            return new Image(b, ".webp", "image/webp");
        if (b.Length >= 6 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F')
            return new Image(b, ".gif", "image/gif");
        return null;
    }

    // HEIC/HEIF: an ISO-BMFF file whose 'ftyp' brand is one of the HEIF brands.
    private static bool IsHeic(byte[] b)
    {
        if (b.Length < 12 || b[4] != (byte)'f' || b[5] != (byte)'t' || b[6] != (byte)'y' || b[7] != (byte)'p') return false;
        var brand = System.Text.Encoding.ASCII.GetString(b, 8, 4);
        return brand is "heic" or "heix" or "hevc" or "heif" or "mif1" or "msf1";
    }
}
