using System.Runtime.InteropServices;

namespace Explorer3DPreview.Shell;

internal static class NativeMethods
{
    internal const int S_OK = 0;
    internal const int E_FAIL = unchecked((int)0x80004005);
    internal const int E_INVALIDARG = unchecked((int)0x80070057);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);
}
