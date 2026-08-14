namespace Explorer3DPreview.Mesh;

internal static class ThumbnailMeshLoader
{
    private const long MaximumNonStlFileSize = 64L * 1024 * 1024;

    internal static MeshData Read(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".stl", StringComparison.OrdinalIgnoreCase))
            return StlReader.ReadForThumbnail(path, 30_000);

        var info = new FileInfo(path);
        if (info.Length > MaximumNonStlFileSize)
            throw new InvalidDataException("Файл слишком велик для безопасной генерации миниатюры.");

        return Sample(MeshFileLoader.Read(path), 30_000);
    }

    internal static MeshData Read(Stream stream)
    {
        if (!stream.CanSeek) throw new NotSupportedException("Поток миниатюры должен поддерживать позиционирование.");
        if (stream.Length < 6 || stream.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Файл слишком велик для безопасной генерации миниатюры.");
        var extension = StreamFormatDetector.Detect(stream);
        stream.Position = 0;
        if (extension == ".stl") return StlReader.ReadForThumbnail(stream, 30_000);
        if (stream.Length > MaximumNonStlFileSize)
            throw new InvalidDataException("Файл слишком велик для безопасной генерации миниатюры.");
        return Sample(MeshFileLoader.Read(stream, extension), 30_000);
    }

    private static MeshData Sample(MeshData mesh, int limit)
    {
        if (mesh.Triangles.Count <= limit) return mesh;
        var sampled = new Triangle[limit];
        var step = mesh.Triangles.Count / (double)limit;
        for (var i = 0; i < sampled.Length; i++)
            sampled[i] = mesh.Triangles[Math.Min(mesh.Triangles.Count - 1, (int)(i * step))];
        return new MeshData(sampled);
    }
}
