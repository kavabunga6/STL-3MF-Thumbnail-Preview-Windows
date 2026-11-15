using Explorer3DPreview.UI;

namespace Explorer3DPreview.Mesh;

/// <summary>
/// Thumbnails for G-code files (GDI+ wrapper around <see cref="GCodeToolpath"/>):
/// 1) use the preview image the slicer embedded in the file, if there is one;
/// 2) otherwise render the extruded toolpath (purge lines removed, one colour, shaded).
/// </summary>
internal static class GCodeThumbnail
{
    internal static bool IsGCodeExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".gcode", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".gco", StringComparison.OrdinalIgnoreCase);
    }

    internal static Bitmap Render(string path, int size)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Render(stream, size);
    }

    internal static Bitmap Render(Stream stream, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        if (stream.Length > GCodeToolpath.MaxFileSize) throw new InvalidDataException("G-code file too large.");

        var embedded = GCodeToolpath.TryReadEmbeddedImage(stream);
        if (embedded is not null)
        {
            try
            {
                using var memory = new MemoryStream(embedded);
                using var image = Image.FromStream(memory);
                return ThumbnailRenderer.FitImage(image, size);
            }
            catch (Exception) { /* unreadable preview: fall back to the toolpath */ }
        }

        stream.Position = 0;
        var segments = GCodeToolpath.ReadToolpath(stream);
        segments = GCodeToolpath.RemoveIsolatedLines(segments);
        var pixels = GCodeToolpath.RenderPixels(segments, size);

        return PixelBitmap.Create(pixels, size);
    }
}
