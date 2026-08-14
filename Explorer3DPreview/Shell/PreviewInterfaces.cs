using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Explorer3DPreview.Shell;

[ComVisible(true), ComImport, Guid("B7D14566-0509-4CCE-A71F-0A554233BD9B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithFile
{
    [PreserveSig]
    int Initialize([MarshalAs(UnmanagedType.LPWStr)] string filePath, uint mode);
}

[ComVisible(true), ComImport, Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithStream
{
    [PreserveSig]
    int Initialize([MarshalAs(UnmanagedType.Interface)] IStream stream, uint mode);
}

[ComVisible(true), ComImport, Guid("E357FCCD-A995-4576-B01F-234630154E96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThumbnailProvider
{
    [PreserveSig]
    int GetThumbnail(uint size, out IntPtr bitmap, out ThumbnailAlphaType alphaType);
}

public enum ThumbnailAlphaType
{
    Unknown = 0,
    Rgb = 1,
    Argb = 2
}
