using System.Numerics;
using System.Text;

namespace Explorer3DPreview.Mesh;

internal static class PlyReader
{
    private sealed record Property(string Name, string Type, bool IsList, string? CountType = null);

    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    internal static MeshData Read(Stream stream)
    {
        var header = ReadHeader(stream);
        if (header.Lines.Count == 0 || !header.Lines[0].Equals("ply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Отсутствует заголовок PLY.");

        var format = header.Lines.First(line => line.StartsWith("format ", StringComparison.OrdinalIgnoreCase)).Split(' ')[1];
        var vertexCount = 0;
        var faceCount = 0;
        var currentElement = "";
        var vertexProperties = new List<Property>();
        var faceProperties = new List<Property>();
        foreach (var line in header.Lines)
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0] == "element")
            {
                currentElement = parts[1];
                if (currentElement == "vertex") vertexCount = int.Parse(parts[2]);
                if (currentElement == "face") faceCount = int.Parse(parts[2]);
            }
            else if (parts.Length >= 3 && parts[0] == "property")
            {
                var property = parts[1] == "list"
                    ? new Property(parts[4], parts[3], true, parts[2])
                    : new Property(parts[2], parts[1], false);
                if (currentElement == "vertex") vertexProperties.Add(property);
                if (currentElement == "face") faceProperties.Add(property);
            }
        }
        if (vertexCount <= 0 || vertexCount > 10_000_000)
            throw new InvalidDataException("Некорректное число вершин PLY.");

        return format switch
        {
            "ascii" => ReadAscii(stream, vertexCount, faceCount, vertexProperties, faceProperties),
            "binary_little_endian" => ReadBinary(stream, vertexCount, faceCount, vertexProperties, faceProperties, false),
            "binary_big_endian" => ReadBinary(stream, vertexCount, faceCount, vertexProperties, faceProperties, true),
            _ => throw new NotSupportedException($"Формат PLY {format} не поддерживается.")
        };
    }

    private static MeshData ReadAscii(Stream stream, int vertexCount, int faceCount,
        IReadOnlyList<Property> vertexProperties, IReadOnlyList<Property> faceProperties)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
        var vertices = new List<Vector3>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var parts = NextDataLine(reader).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var cursor = 0;
            var x = 0f; var y = 0f; var z = 0f;
            foreach (var property in vertexProperties)
            {
                if (property.IsList)
                {
                    var count = int.Parse(parts[cursor++]);
                    cursor += count;
                    continue;
                }
                var value = MeshFactory.ParseFloat(parts[cursor++]);
                if (property.Name == "x") x = value;
                else if (property.Name == "y") y = value;
                else if (property.Name == "z") z = value;
            }
            vertices.Add(new Vector3(x, y, z));
        }
        var triangles = new List<Triangle>();
        for (var i = 0; i < faceCount; i++)
        {
            var parts = NextDataLine(reader).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var cursor = 0;
            foreach (var property in faceProperties)
            {
                if (!property.IsList) { cursor++; continue; }
                var count = int.Parse(parts[cursor++]);
                var indices = new int[count];
                for (var j = 0; j < count; j++) indices[j] = int.Parse(parts[cursor++]);
                if (property.Name is "vertex_indices" or "vertex_index")
                    MeshFactory.AddFace(triangles, vertices, indices);
            }
        }
        return new MeshData(triangles);
    }

    private static MeshData ReadBinary(Stream stream, int vertexCount, int faceCount,
        IReadOnlyList<Property> vertexProperties, IReadOnlyList<Property> faceProperties, bool bigEndian)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var vertices = new List<Vector3>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var x = 0f; var y = 0f; var z = 0f;
            foreach (var property in vertexProperties)
            {
                if (property.IsList)
                {
                    var count = (int)ReadNumber(reader, property.CountType!, bigEndian);
                    for (var j = 0; j < count; j++) _ = ReadNumber(reader, property.Type, bigEndian);
                    continue;
                }
                var value = (float)ReadNumber(reader, property.Type, bigEndian);
                if (property.Name == "x") x = value;
                else if (property.Name == "y") y = value;
                else if (property.Name == "z") z = value;
            }
            vertices.Add(new Vector3(x, y, z));
        }
        var triangles = new List<Triangle>();
        for (var i = 0; i < faceCount; i++)
        {
            foreach (var property in faceProperties)
            {
                if (!property.IsList) { _ = ReadNumber(reader, property.Type, bigEndian); continue; }
                var count = (int)ReadNumber(reader, property.CountType!, bigEndian);
                var indices = new int[count];
                for (var j = 0; j < count; j++) indices[j] = (int)ReadNumber(reader, property.Type, bigEndian);
                if (property.Name is "vertex_indices" or "vertex_index")
                    MeshFactory.AddFace(triangles, vertices, indices);
            }
        }
        return new MeshData(triangles);
    }

    private static double ReadNumber(BinaryReader reader, string type, bool bigEndian)
    {
        var size = type switch
        {
            "char" or "int8" or "uchar" or "uint8" => 1,
            "short" or "int16" or "ushort" or "uint16" => 2,
            "int" or "int32" or "uint" or "uint32" or "float" or "float32" => 4,
            "double" or "float64" => 8,
            _ => throw new NotSupportedException($"Тип PLY {type} не поддерживается.")
        };
        var bytes = reader.ReadBytes(size);
        if (bytes.Length != size) throw new EndOfStreamException();
        if (bigEndian && size > 1) Array.Reverse(bytes);
        return type switch
        {
            "char" or "int8" => (sbyte)bytes[0],
            "uchar" or "uint8" => bytes[0],
            "short" or "int16" => BitConverter.ToInt16(bytes, 0),
            "ushort" or "uint16" => BitConverter.ToUInt16(bytes, 0),
            "int" or "int32" => BitConverter.ToInt32(bytes, 0),
            "uint" or "uint32" => BitConverter.ToUInt32(bytes, 0),
            "float" or "float32" => BitConverter.ToSingle(bytes, 0),
            "double" or "float64" => BitConverter.ToDouble(bytes, 0),
            _ => 0
        };
    }

    private static (List<string> Lines, long Offset) ReadHeader(Stream stream)
    {
        var lines = new List<string>();
        var bytes = new List<byte>();
        while (stream.Position < stream.Length && stream.Position < 1024 * 1024)
        {
            var value = stream.ReadByte();
            if (value < 0) break;
            if (value == '\n')
            {
                var line = Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                lines.Add(line);
                bytes.Clear();
                if (line == "end_header") return (lines, stream.Position);
            }
            else bytes.Add((byte)value);
        }
        throw new InvalidDataException("Заголовок PLY не завершён.");
    }

    private static string NextDataLine(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
            if (!string.IsNullOrWhiteSpace(line)) return line;
        throw new EndOfStreamException();
    }
}
