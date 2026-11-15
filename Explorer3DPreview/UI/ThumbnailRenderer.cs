using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Explorer3DPreview.Mesh;

namespace Explorer3DPreview.UI;

internal static class ThumbnailRenderer
{
    /// <summary>
    /// Single-colour, lit z-buffer render of the model (see <see cref="MeshRasterizer"/>); shares its look with the
    /// G-code toolpath thumbnails.
    /// </summary>
    internal static Bitmap Render(MeshData mesh, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        return PixelBitmap.Create(MeshRasterizer.RenderPixels(mesh, size), size);
    }

    internal static Bitmap FitImage(Image source, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var scale = Math.Min(size / (float)source.Width, size / (float)source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
        return result;
    }
}
