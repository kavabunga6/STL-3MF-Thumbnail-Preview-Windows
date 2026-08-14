using System.Numerics;

namespace Explorer3DPreview.Mesh;

internal static class ObjReader
{
    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    internal static MeshData Read(Stream stream)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<Triangle>();
        using var reader = new StreamReader(stream, leaveOpen: true);
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[0] == "v")
            {
                vertices.Add(new Vector3(MeshFactory.ParseFloat(parts[1]),
                    MeshFactory.ParseFloat(parts[2]), MeshFactory.ParseFloat(parts[3])));
            }
            else if (parts.Length >= 4 && parts[0] == "f")
            {
                var face = new List<int>(parts.Length - 1);
                for (var i = 1; i < parts.Length; i++)
                {
                    var token = parts[i].Split('/')[0];
                    var index = int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
                    face.Add(index < 0 ? vertices.Count + index : index - 1);
                }
                MeshFactory.AddFace(triangles, vertices, face);
            }
        }
        return new MeshData(triangles);
    }
}
