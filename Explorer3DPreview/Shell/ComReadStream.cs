using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Explorer3DPreview.Shell;

internal sealed class ComReadStream : Stream
{
    private readonly IStream _stream;

    internal ComReadStream(IStream stream) => _stream = stream;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            _stream.Stat(out var stat, 1);
            return stat.cbSize;
        }
    }

    public override long Position
    {
        get => Seek(0, SeekOrigin.Current);
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
        var target = offset == 0 ? buffer : new byte[count];
        var readPointer = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            _stream.Read(target, count, readPointer);
            var read = Marshal.ReadInt32(readPointer);
            if (offset != 0 && read > 0) Buffer.BlockCopy(target, 0, buffer, offset, read);
            return read;
        }
        finally { Marshal.FreeCoTaskMem(readPointer); }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var positionPointer = Marshal.AllocCoTaskMem(sizeof(long));
        try
        {
            _stream.Seek(offset, (int)origin, positionPointer);
            return Marshal.ReadInt64(positionPointer);
        }
        finally { Marshal.FreeCoTaskMem(positionPointer); }
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
