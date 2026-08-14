using System.IO.Compression;
using System.Numerics;
using System.Xml.Linq;

namespace Explorer3DPreview.Mesh;

internal static class ThreeMfReader
{
    private sealed record ObjectMesh(List<Vector3> Vertices, List<int[]> Faces);

    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    internal static MeshData Read(Stream source)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var modelEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("В 3MF отсутствует модель.");
        if (modelEntry.Length > 16L * 1024 * 1024)
            throw new InvalidDataException("Распакованная 3MF-модель слишком велика для безопасной миниатюры.");
        using var stream = modelEntry.Open();
        var document = XDocument.Load(stream, LoadOptions.None);
        var objects = new Dictionary<string, ObjectMesh>(StringComparer.Ordinal);
        foreach (var element in document.Descendants().Where(node => node.Name.LocalName == "object"))
        {
            var id = element.Attribute("id")?.Value;
            var mesh = element.Elements().FirstOrDefault(node => node.Name.LocalName == "mesh");
            if (id is null || mesh is null) continue;
            var verticesElement = mesh.Elements().First(node => node.Name.LocalName == "vertices");
            var vertices = verticesElement.Elements().Where(node => node.Name.LocalName == "vertex")
                .Select(vertex => new Vector3(
                    MeshFactory.ParseFloat(vertex.Attribute("x")!.Value),
                    MeshFactory.ParseFloat(vertex.Attribute("y")!.Value),
                    MeshFactory.ParseFloat(vertex.Attribute("z")!.Value))).ToList();
            var faces = mesh.Elements().First(node => node.Name.LocalName == "triangles").Elements()
                .Where(node => node.Name.LocalName == "triangle")
                .Select(face => new[] { int.Parse(face.Attribute("v1")!.Value), int.Parse(face.Attribute("v2")!.Value), int.Parse(face.Attribute("v3")!.Value) })
                .ToList();
            objects[id] = new ObjectMesh(vertices, faces);
        }

        var output = new List<Triangle>();
        var items = document.Descendants().Where(node => node.Name.LocalName == "build")
            .SelectMany(build => build.Elements().Where(node => node.Name.LocalName == "item")).ToList();
        if (items.Count == 0)
        {
            foreach (var mesh in objects.Values) AddMesh(output, mesh, Matrix4x4.Identity);
        }
        else
        {
            foreach (var item in items)
            {
                var objectId = item.Attribute("objectid")?.Value;
                if (objectId is null || !objects.TryGetValue(objectId, out var mesh)) continue;
                AddMesh(output, mesh, ParseTransform(item.Attribute("transform")?.Value));
            }
        }
        return new MeshData(output);
    }

    private static void AddMesh(List<Triangle> output, ObjectMesh mesh, Matrix4x4 transform)
    {
        var vertices = mesh.Vertices.Select(vertex => Vector3.Transform(vertex, transform)).ToList();
        foreach (var face in mesh.Faces) MeshFactory.AddFace(output, vertices, face);
    }

    private static Matrix4x4 ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Matrix4x4.Identity;
        var numbers = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(MeshFactory.ParseFloat).ToArray();
        if (numbers.Length != 12) return Matrix4x4.Identity;
        return new Matrix4x4(numbers[0], numbers[1], numbers[2], 0,
            numbers[3], numbers[4], numbers[5], 0,
            numbers[6], numbers[7], numbers[8], 0,
            numbers[9], numbers[10], numbers[11], 1);
    }
}
