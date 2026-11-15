using Explorer3DPreview.UI;

namespace Explorer3DPreview.Mesh;

/// <summary>GDI+ wrapper around <see cref="ThreeMfPreview"/>: the slicer's own picture, or null when the package has none.</summary>
internal static class ThreeMfThumbnail
{
    internal static Bitmap? TryRender(string path, int size)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return TryRender(stream, size);
    }

    internal static Bitmap? TryRender(Stream stream, int size)
    {
        var bytes = ThreeMfPreview.TryReadBytes(stream);
        if (bytes is null) return null;
        try
        {
            using var memory = new MemoryStream(bytes);
            using var image = Image.FromStream(memory);
            return ThumbnailRenderer.FitImage(image, Math.Clamp(size, 32, 1024));
        }
        catch (Exception) { return null; }   // unreadable picture: let the caller render the mesh instead
    }
}
