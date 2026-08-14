using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Explorer3DPreview.Mesh;
using Explorer3DPreview.UI;

namespace Explorer3DPreview.Shell;

[ComVisible(true)]
[Guid(ClassId)]
[ProgId("Explorer3DPreview.MeshThumbnail")]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IThumbnailProvider))]
public sealed class MeshThumbnailProvider : IThumbnailProvider, IInitializeWithFile, IInitializeWithStream
{
    public const string ClassId = "16EAEC3D-A095-4F3D-9D29-FEAC9D26520D";
    private string? _filePath;
    private IStream? _stream;

    public int Initialize(string filePath, uint mode)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return NativeMethods.E_INVALIDARG;
        _filePath = filePath;
        _stream = null;
        return NativeMethods.S_OK;
    }

    int IInitializeWithStream.Initialize(IStream stream, uint mode)
    {
        if (stream is null) return NativeMethods.E_INVALIDARG;
        _stream = stream;
        _filePath = null;
        return NativeMethods.S_OK;
    }

    public int GetThumbnail(uint size, out IntPtr bitmap, out ThumbnailAlphaType alphaType)
    {
        bitmap = IntPtr.Zero;
        alphaType = ThumbnailAlphaType.Rgb;
        if (_filePath is null && _stream is null) return NativeMethods.E_FAIL;

        try
        {
            var requestedSize = Math.Clamp((int)size, 32, 1024);
            var mesh = _filePath is not null
                ? ThumbnailMeshLoader.Read(_filePath)
                : ThumbnailMeshLoader.Read(new ComReadStream(_stream!));
            using var rendered = ThumbnailRenderer.Render(mesh, requestedSize);
            bitmap = rendered.GetHbitmap(Color.White);
            return bitmap == IntPtr.Zero ? NativeMethods.E_FAIL : NativeMethods.S_OK;
        }
        catch
        {
            return NativeMethods.E_FAIL;
        }
    }
}

[ComVisible(true)]
[Guid(ClassId)]
[ProgId("Explorer3DPreview.EmbeddedThumbnail")]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IThumbnailProvider))]
public sealed class EmbeddedThumbnailProvider : IThumbnailProvider, IInitializeWithFile, IInitializeWithStream
{
    public const string ClassId = "961034D4-4D2D-4AD2-AE59-0672E6A3AF99";
    private string? _filePath;
    private IStream? _stream;

    public int Initialize(string filePath, uint mode)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return NativeMethods.E_INVALIDARG;
        _filePath = filePath;
        _stream = null;
        return NativeMethods.S_OK;
    }

    int IInitializeWithStream.Initialize(IStream stream, uint mode)
    {
        if (stream is null) return NativeMethods.E_INVALIDARG;
        _stream = stream;
        _filePath = null;
        return NativeMethods.S_OK;
    }

    public int GetThumbnail(uint size, out IntPtr bitmap, out ThumbnailAlphaType alphaType)
    {
        bitmap = IntPtr.Zero;
        alphaType = ThumbnailAlphaType.Rgb;
        if (_filePath is null && _stream is null) return NativeMethods.E_FAIL;

        try
        {
            using var embedded = _filePath is not null
                ? CompoundPreviewExtractor.TryExtract(_filePath)
                : CompoundPreviewExtractor.TryExtractRaw(new ComReadStream(_stream!));
            if (embedded is null) return NativeMethods.E_FAIL;
            using var rendered = ThumbnailRenderer.FitImage(embedded, Math.Clamp((int)size, 32, 1024));
            bitmap = rendered.GetHbitmap(Color.White);
            return bitmap == IntPtr.Zero ? NativeMethods.E_FAIL : NativeMethods.S_OK;
        }
        catch
        {
            return NativeMethods.E_FAIL;
        }
    }
}
