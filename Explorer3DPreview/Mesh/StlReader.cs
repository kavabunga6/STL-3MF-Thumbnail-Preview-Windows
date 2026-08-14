using System.Globalization;
using System.Numerics;
using System.Text;

namespace Explorer3DPreview.Mesh;

public static class StlReader
{
    private const int MaxTriangles = 2_000_000;

    public static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < 6)
            throw new InvalidDataException("Файл слишком мал для STL.");

        return LooksLikeBinary(stream) ? ReadBinary(stream) : ReadAscii(stream);
    }

    internal static MeshData ReadForThumbnail(string path, int triangleLimit)
    {
        if (triangleLimit < 1) throw new ArgumentOutOfRangeException(nameof(triangleLimit));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadForThumbnail(stream, triangleLimit);
    }

    internal static MeshData ReadForThumbnail(Stream stream, int triangleLimit)
    {
        if (triangleLimit < 1) throw new ArgumentOutOfRangeException(nameof(triangleLimit));
        if (stream.Length < 6 || stream.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Размер STL не подходит для безопасной генерации миниатюры.");
        return LooksLikeBinary(stream)
            ? ReadBinarySampled(stream, triangleLimit)
            : ReadAsciiSampled(stream, triangleLimit);
    }

    private static bool LooksLikeBinary(Stream stream)
    {
        if (stream.Length < 84)
            return false;

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = 80;
        var count = reader.ReadUInt32();
        var expectedLength = 84L + count * 50L;
        stream.Position = 0;
        return expectedLength == stream.Length;
    }

    private static MeshData ReadBinary(Stream stream)
    {
        stream.Position = 80;
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var count = reader.ReadUInt32();
        if (count == 0 || count > MaxTriangles)
            throw new InvalidDataException($"Недопустимое число треугольников: {count}.");

        var triangles = new Triangle[(int)count];
        for (var i = 0; i < triangles.Length; i++)
        {
            var suppliedNormal = ReadVector(reader);
            var a = ReadVector(reader);
            var b = ReadVector(reader);
            var c = ReadVector(reader);
            _ = reader.ReadUInt16();
            Validate(a, b, c);
            triangles[i] = new Triangle(a, b, c, NormalizeOrCalculate(suppliedNormal, a, b, c));
        }

        return new MeshData(triangles);
    }

    private static MeshData ReadBinarySampled(Stream stream, int limit)
    {
        stream.Position = 80;
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var count = reader.ReadUInt32();
        if (count == 0) throw new InvalidDataException("STL не содержит треугольников.");
        var stride = Math.Max(1L, (long)Math.Ceiling(count / (double)limit));
        var triangles = new List<Triangle>((int)Math.Min(count, (uint)limit));
        for (uint index = 0; index < count; index++)
        {
            if (index % stride != 0)
            {
                stream.Seek(50, SeekOrigin.Current);
                continue;
            }
            var suppliedNormal = ReadVector(reader);
            var a = ReadVector(reader);
            var b = ReadVector(reader);
            var c = ReadVector(reader);
            _ = reader.ReadUInt16();
            Validate(a, b, c);
            triangles.Add(new Triangle(a, b, c, NormalizeOrCalculate(suppliedNormal, a, b, c)));
        }
        return new MeshData(triangles);
    }

    private static MeshData ReadAscii(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var vertices = new List<Vector3>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException("Некорректная вершина в ASCII STL.");

            vertices.Add(new Vector3(x, y, z));
            if (vertices.Count > MaxTriangles * 3)
                throw new InvalidDataException("STL превышает допустимый размер.");
        }

        if (vertices.Count == 0 || vertices.Count % 3 != 0)
            throw new InvalidDataException("Некорректная структура ASCII STL.");

        var triangles = new Triangle[vertices.Count / 3];
        for (var i = 0; i < triangles.Length; i++)
        {
            var a = vertices[i * 3];
            var b = vertices[i * 3 + 1];
            var c = vertices[i * 3 + 2];
            Validate(a, b, c);
            triangles[i] = new Triangle(a, b, c, CalculateNormal(a, b, c));
        }

        return new MeshData(triangles);
    }

    private static MeshData ReadAsciiSampled(Stream stream, int limit)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var triangles = new List<Triangle>(limit);
        var vertices = new Vector3[3];
        var vertexIndex = 0;
        var seen = 0;
        uint randomState = 0x9E3779B9;
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException("Некорректная вершина в ASCII STL.");

            vertices[vertexIndex++] = new Vector3(x, y, z);
            if (vertexIndex != 3) continue;
            vertexIndex = 0;
            var triangle = new Triangle(vertices[0], vertices[1], vertices[2],
                CalculateNormal(vertices[0], vertices[1], vertices[2]));
            if (triangles.Count < limit)
                triangles.Add(triangle);
            else
            {
                randomState = unchecked(randomState * 1664525u + 1013904223u);
                var replacement = (int)(randomState % (uint)(seen + 1));
                if (replacement < limit) triangles[replacement] = triangle;
            }
            seen++;
        }

        if (vertexIndex != 0 || triangles.Count == 0)
            throw new InvalidDataException("Некорректная структура ASCII STL.");
        return new MeshData(triangles);
    }

    private static Vector3 ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void Validate(params Vector3[] vertices)
    {
        if (vertices.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
            throw new InvalidDataException("STL содержит нечисловые координаты.");
    }

    private static Vector3 NormalizeOrCalculate(Vector3 supplied, Vector3 a, Vector3 b, Vector3 c) =>
        supplied.LengthSquared() > 0.0000001f && IsFinite(supplied)
            ? Vector3.Normalize(supplied)
            : CalculateNormal(a, b, c);

    private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        return cross.LengthSquared() > 0.0000001f ? Vector3.Normalize(cross) : Vector3.UnitZ;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
