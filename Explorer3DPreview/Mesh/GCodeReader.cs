using System.Numerics;

namespace Explorer3DPreview.Mesh;

internal static class GCodeReader
{
    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    internal static MeshData Read(Stream stream)
    {
        var triangles = new List<Triangle>();
        var position = Vector3.Zero;
        var extrusion = 0f;
        var axesAbsolute = true;
        var extrusionAbsolute = true;
        const float width = 0.4f;

        using var reader = new StreamReader(stream, leaveOpen: true);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Split(';')[0].Trim().ToUpperInvariant();
            if (line.Length == 0) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var command = words[0];
            if (command == "G90") { axesAbsolute = true; continue; }
            if (command == "G91") { axesAbsolute = false; continue; }
            if (command == "M82") { extrusionAbsolute = true; continue; }
            if (command == "M83") { extrusionAbsolute = false; continue; }
            if (command == "G92")
            {
                foreach (var word in words.Skip(1))
                    if (word.StartsWith('E')) extrusion = MeshFactory.ParseFloat(word[1..]);
                continue;
            }
            if (command is not ("G0" or "G00" or "G1" or "G01")) continue;

            var next = position;
            var nextExtrusion = extrusion;
            var hasExtrusion = false;
            foreach (var word in words.Skip(1))
            {
                if (word.Length < 2) continue;
                var value = MeshFactory.ParseFloat(word[1..]);
                switch (word[0])
                {
                    case 'X': next.X = axesAbsolute ? value : next.X + value; break;
                    case 'Y': next.Y = axesAbsolute ? value : next.Y + value; break;
                    case 'Z': next.Z = axesAbsolute ? value : next.Z + value; break;
                    case 'E': nextExtrusion = extrusionAbsolute ? value : extrusion + value; hasExtrusion = true; break;
                }
            }
            if (hasExtrusion && nextExtrusion > extrusion + 0.00001f && Vector3.DistanceSquared(next, position) > 0.000001f)
                AddSegment(triangles, position, next, width);
            position = next;
            extrusion = nextExtrusion;
        }
        return new MeshData(triangles);
    }

    private static void AddSegment(List<Triangle> triangles, Vector3 a, Vector3 b, float width)
    {
        var direction = new Vector2(b.X - a.X, b.Y - a.Y);
        if (direction.LengthSquared() < 0.000001f) return;
        direction = Vector2.Normalize(direction);
        var offset = new Vector3(-direction.Y * width * 0.5f, direction.X * width * 0.5f, 0);
        triangles.Add(MeshFactory.Triangle(a - offset, b - offset, b + offset));
        triangles.Add(MeshFactory.Triangle(a - offset, b + offset, a + offset));
        if (triangles.Count > MeshFactory.MaxTriangles) throw new InvalidDataException("G-code слишком велик.");
    }
}
