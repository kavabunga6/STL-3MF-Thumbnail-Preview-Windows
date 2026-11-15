namespace Explorer3DPreview.Mesh;

internal static class ThumbnailMeshLoader
{
    private const long MaximumNonStlFileSize = 64L * 1024 * 1024;

    // The z-buffer renderer draws every triangle, so dense models stay closed surfaces instead of turning into dots.
    private const int MaxThumbnailTriangles = 750_000;

    internal static MeshData Read(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".stl", StringComparison.OrdinalIgnoreCase))
            return StlReader.ReadForThumbnail(path, MaxThumbnailTriangles);

        var info = new FileInfo(path);
        if (info.Length > MaximumNonStlFileSize)
            throw new InvalidDataException("Файл слишком велик для безопасной генерации миниатюры.");

        return Sample(MeshFileLoader.Read(path), MaxThumbnailTriangles);
    }

    internal static MeshData Read(Stream stream)
    {
        if (!stream.CanSeek) throw new NotSupportedException("Поток миниатюры должен поддерживать позиционирование.");
        if (stream.Length < 6 || stream.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Файл слишком велик для безопасной генерации миниатюры.");
        var extension = StreamFormatDetector.Detect(stream);
        stream.Position = 0;
        if (extension == ".stl") return StlReader.ReadForThumbnail(stream, MaxThumbnailTriangles);
        if (stream.Length > MaximumNonStlFileSize)
            throw new InvalidDataException("Файл слишком велик для безопасной генерации миниатюры.");
        return Sample(MeshFileLoader.Read(stream, extension), MaxThumbnailTriangles);
    }

    private static MeshData Sample(MeshData mesh, int limit)
    {
        var triangles = mesh.Triangles;
        if (triangles.Count > limit)
        {
            var sampled = new Triangle[limit];
            var step = triangles.Count / (double)limit;
            for (var i = 0; i < sampled.Length; i++)
                sampled[i] = triangles[Math.Min(triangles.Count - 1, (int)(i * step))];
            triangles = sampled;
        }

        var segments = mesh.Segments;
        if (segments.Count > limit)
        {
            var sampled = new LineSegment[limit];
            var step = segments.Count / (double)limit;
            for (var i = 0; i < sampled.Length; i++)
                sampled[i] = segments[Math.Min(segments.Count - 1, (int)(i * step))];
            segments = sampled;
        }
        return triangles == mesh.Triangles && segments == mesh.Segments
            ? mesh
            : new MeshData(triangles, segments);
    }
}
