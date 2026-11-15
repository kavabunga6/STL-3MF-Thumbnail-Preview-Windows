using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Explorer3DPreview.UI;

internal static class PixelBitmap
{
    /// <summary>Creates a 32bpp bitmap from size*size ARGB pixels (row-major, top row first).</summary>
    internal static Bitmap Create(int[] pixels, int size)
    {
        if (pixels.Length != size * size) throw new ArgumentException("Pixel buffer does not match the size.", nameof(pixels));
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Rows are copied one by one because Stride may be padded.
            for (var y = 0; y < size; y++)
                Marshal.Copy(pixels, y * size, data.Scan0 + y * data.Stride, size);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}
