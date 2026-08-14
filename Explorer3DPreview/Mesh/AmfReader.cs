using System.Numerics;
using System.Xml.Linq;

namespace Explorer3DPreview.Mesh;

internal static class AmfReader
{
    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    internal static MeshData Read(Stream stream)
    {
        var document = XDocument.Load(stream, LoadOptions.None);
        var vertices = document.Descendants().Where(node => node.Name.LocalName == "vertex")
            .Select(vertex =>
            {
                var coordinates = vertex.Descendants().First(node => node.Name.LocalName == "coordinates");
                float Value(string name) => MeshFactory.ParseFloat(coordinates.Elements().First(node => node.Name.LocalName == name).Value);
                return new Vector3(Value("x"), Value("y"), Value("z"));
            }).ToList();
        var triangles = new List<Triangle>();
        foreach (var element in document.Descendants().Where(node => node.Name.LocalName == "triangle"))
        {
            int Value(string name) => int.Parse(element.Elements().First(node => node.Name.LocalName == name).Value);
            MeshFactory.AddFace(triangles, vertices, new[] { Value("v1"), Value("v2"), Value("v3") });
        }
        return new MeshData(triangles);
    }
}
