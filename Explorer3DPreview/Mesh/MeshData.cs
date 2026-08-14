using System.Numerics;

namespace Explorer3DPreview.Mesh;

public readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal);

public sealed class MeshData
{
    public MeshData(IReadOnlyList<Triangle> triangles)
    {
        if (triangles.Count == 0)
            throw new InvalidDataException("Модель не содержит треугольников.");

        Triangles = triangles;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var triangle in triangles)
        {
            min = Vector3.Min(min, Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C)));
            max = Vector3.Max(max, Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C)));
        }

        Min = min;
        Max = max;
        Center = (min + max) * 0.5f;
        var size = max - min;
        Radius = Math.Max(0.00001f, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f);
    }

    public IReadOnlyList<Triangle> Triangles { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Center { get; }
    public float Radius { get; }
}
