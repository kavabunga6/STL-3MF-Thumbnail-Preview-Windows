using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Explorer3DPreview.Shell;

internal static class CompoundPreviewExtractor
{
    private const int StgmReadShareExclusive = 0x10;
    private const int StorageType = 1;
    private const int StreamType = 2;
    private const int MaximumStreams = 256;
    private const long MaximumStreamBytes = 24L * 1024 * 1024;

    internal static Bitmap? TryExtract(string path)
    {
        if (!File.Exists(path)) return null;
        IStorage? storage = null;
        try
        {
            if (StgOpenStorage(path, null, StgmReadShareExclusive, IntPtr.Zero, 0, out storage) != 0 || storage is null)
                return null;

            var candidates = new List<(string Name, byte[] Bytes)>();
            Enumerate(storage, string.Empty, candidates, 0);
            Bitmap? best = null;
            long bestArea = 0;
            foreach (var candidate in candidates
                         .OrderByDescending(item => item.Name.Contains("preview", StringComparison.OrdinalIgnoreCase)))
            {
                using var image = DecodeImage(candidate.Bytes);
                if (image is null) continue;
                var area = (long)image.Width * image.Height;
                if (area <= bestArea) continue;
                best?.Dispose();
                best = new Bitmap(image);
                bestArea = area;
            }
            return best;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (storage is not null && Marshal.IsComObject(storage)) Marshal.FinalReleaseComObject(storage);
        }
    }

    internal static Bitmap? TryExtractRaw(Stream stream)
    {
        if (!stream.CanSeek || stream.Length <= 0 || stream.Length > 64L * 1024 * 1024) return null;
        try
        {
            stream.Position = 0;
            var bytes = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
            return DecodeImage(offset == bytes.Length ? bytes : bytes[..offset]);
        }
        catch { return null; }
    }

    private static void Enumerate(IStorage storage, string prefix,
        List<(string Name, byte[] Bytes)> candidates, int depth)
    {
        if (depth > 4 || candidates.Count >= MaximumStreams) return;
        if (storage.EnumElements(0, IntPtr.Zero, 0, out var enumerator) != 0 || enumerator is null) return;
        try
        {
            var entries = new STATSTG[1];
            while (candidates.Count < MaximumStreams && enumerator.Next(1, entries, out var fetched) == 0 && fetched == 1)
            {
                var entry = entries[0];
                if (entry.type == StreamType && entry.cbSize > 0 && entry.cbSize <= MaximumStreamBytes &&
                    storage.OpenStream(entry.pwcsName, IntPtr.Zero, StgmReadShareExclusive, 0, out var stream) == 0)
                {
                    try
                    {
                        var bytes = ReadStream(stream, (int)entry.cbSize);
                        if (LooksInteresting(entry.pwcsName, bytes)) candidates.Add(($"{prefix}{entry.pwcsName}", bytes));
                    }
                    finally
                    {
                        if (Marshal.IsComObject(stream)) Marshal.FinalReleaseComObject(stream);
                    }
                }
                else if (entry.type == StorageType && depth < 4 &&
                         storage.OpenStorage(entry.pwcsName, IntPtr.Zero, StgmReadShareExclusive,
                             IntPtr.Zero, 0, out var child) == 0)
                {
                    try { Enumerate(child, $"{prefix}{entry.pwcsName}/", candidates, depth + 1); }
                    finally
                    {
                        if (Marshal.IsComObject(child)) Marshal.FinalReleaseComObject(child);
                    }
                }
            }
        }
        finally
        {
            if (Marshal.IsComObject(enumerator)) Marshal.FinalReleaseComObject(enumerator);
        }
    }

    private static byte[] ReadStream(IStream stream, int length)
    {
        var result = new byte[length];
        var readPointer = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            var offset = 0;
            while (offset < result.Length)
            {
                var chunkLength = Math.Min(1024 * 1024, result.Length - offset);
                var chunk = new byte[chunkLength];
                stream.Read(chunk, chunkLength, readPointer);
                var read = Marshal.ReadInt32(readPointer);
                if (read <= 0) break;
                Buffer.BlockCopy(chunk, 0, result, offset, read);
                offset += read;
            }
            return offset == result.Length ? result : result[..offset];
        }
        finally { Marshal.FreeCoTaskMem(readPointer); }
    }

    private static bool LooksInteresting(string name, byte[] bytes) =>
        name.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
        FindImageStart(bytes) >= 0;

    internal static Bitmap? DecodeImage(byte[] bytes)
    {
        if (bytes.Length < 16) return null;
        var start = FindImageStart(bytes);
        if (start < 0) return null;
        try
        {
            using var stream = new MemoryStream(bytes, start, bytes.Length - start, writable: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (image.Width < 8 || image.Height < 8 || (long)image.Width * image.Height > 64_000_000) return null;
            return new Bitmap(image);
        }
        catch { return null; }
    }

    private static int FindImageStart(byte[] bytes)
    {
        for (var index = 0; index <= bytes.Length - 8; index++)
        {
            if (bytes[index] == 0x89 && bytes[index + 1] == 0x50 && bytes[index + 2] == 0x4E &&
                bytes[index + 3] == 0x47 && bytes[index + 4] == 0x0D && bytes[index + 5] == 0x0A &&
                bytes[index + 6] == 0x1A && bytes[index + 7] == 0x0A) return index;
            if (bytes[index] == 0xFF && bytes[index + 1] == 0xD8 && bytes[index + 2] == 0xFF) return index;
            if (bytes[index] == 0x42 && bytes[index + 1] == 0x4D) return index;
            if (bytes[index] == 0x47 && bytes[index + 1] == 0x49 && bytes[index + 2] == 0x46 &&
                bytes[index + 3] == 0x38) return index;
        }
        return -1;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgOpenStorage(string name, IStorage? priority, int mode,
        IntPtr exclude, int reserved, out IStorage storage);

    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        [PreserveSig] int CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, int mode, int reserved1,
            int reserved2, out IStream stream);
        [PreserveSig] int OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved1, int mode,
            int reserved2, out IStream stream);
        [PreserveSig] int CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, int mode, int reserved1,
            int reserved2, out IStorage storage);
        [PreserveSig] int OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr priority, int mode,
            IntPtr exclude, int reserved, out IStorage storage);
        [PreserveSig] int CopyTo(int count, IntPtr excludeIds, IntPtr excludeNames, IStorage destination);
        [PreserveSig] int MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage destination,
            [MarshalAs(UnmanagedType.LPWStr)] string newName, int flags);
        [PreserveSig] int Commit(int flags);
        [PreserveSig] int Revert();
        [PreserveSig] int EnumElements(int reserved1, IntPtr reserved2, int reserved3, out IEnumStatStg enumerator);
        [PreserveSig] int DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int RenameElement([MarshalAs(UnmanagedType.LPWStr)] string oldName,
            [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr creation,
            IntPtr access, IntPtr modification);
        [PreserveSig] int SetClass(ref Guid classId);
        [PreserveSig] int SetStateBits(int stateBits, int mask);
        [PreserveSig] int Stat(out STATSTG stat, int flags);
    }

    [ComImport, Guid("0000000D-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumStatStg
    {
        [PreserveSig] int Next(uint count,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] STATSTG[] elements,
            out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumStatStg clone);
    }
}
