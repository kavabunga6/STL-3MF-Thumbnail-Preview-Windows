using System.Numerics;

namespace Explorer3DPreview.Mesh;

internal static class MeshFactory
{
    internal const int MaxTriangles = 2_000_000;

    internal static Triangle Triangle(Vector3 a, Vector3 b, Vector3 c)
    {
        if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
            throw new InvalidDataException("Модель содержит некорректные координаты.");
        var cross = Vector3.Cross(b - a, c - a);
        var normal = cross.LengthSquared() > 0.0000001f ? Vector3.Normalize(cross) : Vector3.UnitZ;
        return new Triangle(a, b, c, normal);
    }

    internal static void AddFace(List<Triangle> output, IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices)
    {
        if (indices.Count < 3) return;
        if (indices.Any(index => index < 0 || index >= vertices.Count))
            throw new InvalidDataException("Грань ссылается на отсутствующую вершину.");
        for (var i = 1; i < indices.Count - 1; i++)
        {
            output.Add(Triangle(vertices[indices[0]], vertices[indices[i]], vertices[indices[i + 1]]));
            if (output.Count > MaxTriangles)
                throw new InvalidDataException("Модель превышает допустимое число треугольников.");
        }
    }

    internal static float ParseFloat(string value) =>
        float.Parse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
