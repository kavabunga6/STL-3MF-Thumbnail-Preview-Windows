using System.Runtime.InteropServices;

namespace Explorer3DPreview.Shell;

internal static class ShellThumbnailReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindContext,
        ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    internal static Bitmap? TryLoad(string path, int size = 1400)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            const uint thumbnailOnly = 0x8;
            const uint biggerSizeOk = 0x1;
            var result = factory.GetImage(new NativeSize { Width = size, Height = size }, thumbnailOnly | biggerSizeOk, out var handle);
            if (result != 0 || handle == IntPtr.Zero) return null;
            try
            {
                using var source = Image.FromHbitmap(handle);
                return new Bitmap(source);
            }
            finally { DeleteObject(handle); }
        }
        catch { return null; }
    }
}
