using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Explorer3DPreview.Benchmarks;

internal static class RenderWorker
{
    internal static WorkerResult Run(WorkerRequest request)
    {
        var times = new List<double>();
        var cold = 0.0;
        var foreground = 0;
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(request.Assembly));
            var type = assembly.GetType("Explorer3DPreview.Shell.MeshThumbnailProvider", throwOnError: true)!;
            var initializeFile = type.GetMethod("Initialize", new[] { typeof(string), typeof(uint) })!;
            var initializeStream = type.GetInterface("Explorer3DPreview.Shell.IInitializeWithStream")!.GetMethod("Initialize")!;
            var getThumbnail = type.GetMethod("GetThumbnail")!;
            for (var i = 0; i < request.Warmups + request.Iterations; i++)
            {
                IntPtr handle = IntPtr.Zero;
                IStream? input = null;
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var provider = Activator.CreateInstance(type)!;
                    int initializeResult;
                    if (request.Mode == "stream")
                    {
                        // Open exactly as a read-only Shell stream. No registration or installation is needed.
                        Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(request.Input, 0x20, 0, false, null, out input));
                        initializeResult = (int)initializeStream.Invoke(provider, new object?[] { input, (uint)0 })!;
                    }
                    else initializeResult = (int)initializeFile.Invoke(provider, new object[] { request.Input, (uint)0 })!;
                    if (initializeResult != 0) throw new InvalidOperationException($"Initialize: 0x{initializeResult:X8}");
                    var arguments = new object?[] { (uint)request.Size, IntPtr.Zero, null };
                    var hr = (int)getThumbnail.Invoke(provider, arguments)!;
                    handle = (IntPtr)arguments[1]!;
                    stopwatch.Stop();
                    var elapsed = stopwatch.Elapsed.TotalMilliseconds;
                    if (i == 0) cold = elapsed;
                    if (i >= request.Warmups) times.Add(elapsed);
                    if (elapsed > Comparison.MaxRenderMs)
                        throw new TimeoutException($"A render took {elapsed:F1} ms (limit {Comparison.MaxRenderMs:F0} ms).");
                    if (!request.ExpectedSuccess)
                    {
                        if (hr == 0 || handle != IntPtr.Zero)
                            throw new InvalidDataException("Malformed input unexpectedly produced a thumbnail.");
                        continue;
                    }
                    if (hr != 0 || handle == IntPtr.Zero)
                        throw new InvalidOperationException($"GetThumbnail: 0x{hr:X8}; HBITMAP={handle}.");
                    if (i == request.Warmups + request.Iterations - 1)
                    {
                        using var bitmap = Image.FromHbitmap(handle);
                        if (bitmap.Width != request.Size || bitmap.Height != request.Size)
                            throw new InvalidDataException($"Incorrect image size: {bitmap.Width}x{bitmap.Height}.");
                        foreground = CountForeground(bitmap);
                        if (foreground < 8) throw new InvalidDataException("The thumbnail contains no visible model.");
                        bitmap.Save(request.Image, ImageFormat.Png);
                    }
                }
                finally
                {
                    if (handle != IntPtr.Zero) DeleteObject(handle);
                    if (input is not null && Marshal.IsComObject(input)) Marshal.FinalReleaseComObject(input);
                }
            }
            using var process = Process.GetCurrentProcess();
            return new WorkerResult(true, null, cold, Comparison.Percentile(times.ToArray(), 0.5),
                Comparison.Percentile(times.ToArray(), 0.95), times.ToArray(), process.PeakWorkingSet64, foreground);
        }
        catch (Exception error)
        {
            return new WorkerResult(false, error.GetBaseException().Message, cold, 0, 0, times.ToArray(), 0, foreground);
        }
    }

    private static int CountForeground(Bitmap bitmap)
    {
        using var normalized = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized)) graphics.DrawImageUnscaled(bitmap, 0, 0);
        var data = normalized.LockBits(new Rectangle(0, 0, normalized.Width, normalized.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[normalized.Width];
            var count = 0;
            for (var y = 0; y < normalized.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                foreach (var pixel in row)
                {
                    var r = (pixel >> 16) & 255; var g = (pixel >> 8) & 255; var b = pixel & 255;
                    // The backdrop is near-grey. Accept both rendered blue models and coloured slicer previews.
                    if (Math.Max(r, Math.Max(g,b)) - Math.Min(r, Math.Min(g,b)) > 40) count++;
                }
            }
            return count;
        }
        finally { normalized.UnlockBits(data); }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(string path, uint mode, uint attributes,
        [MarshalAs(UnmanagedType.Bool)] bool create, IStream? template, out IStream stream);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
