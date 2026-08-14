using System.Numerics;

namespace Explorer3DPreview.Mesh;

internal static class OffReader
{
    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    internal static MeshData Read(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        IEnumerable<string> Lines()
        {
            while (reader.ReadLine() is { } line) yield return line;
        }
        using var tokens = Lines()
            .Select(line => line.Split('#')[0])
            .SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .GetEnumerator();
        string Next() => tokens.MoveNext() ? tokens.Current : throw new InvalidDataException("Неожиданный конец OFF.");
        if (!Next().Equals("OFF", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Отсутствует заголовок OFF.");
        var vertexCount = int.Parse(Next());
        var faceCount = int.Parse(Next());
        _ = Next();
        var vertices = new List<Vector3>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
            vertices.Add(new Vector3(MeshFactory.ParseFloat(Next()), MeshFactory.ParseFloat(Next()), MeshFactory.ParseFloat(Next())));
        var triangles = new List<Triangle>();
        for (var i = 0; i < faceCount; i++)
        {
            var count = int.Parse(Next());
            var indices = new int[count];
            for (var j = 0; j < count; j++) indices[j] = int.Parse(Next());
            MeshFactory.AddFace(triangles, vertices, indices);
        }
        return new MeshData(triangles);
    }
}
