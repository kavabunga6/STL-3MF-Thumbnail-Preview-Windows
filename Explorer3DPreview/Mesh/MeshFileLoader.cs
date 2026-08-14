namespace Explorer3DPreview.Mesh;

internal static class MeshFileLoader
{
    internal static readonly HashSet<string> DirectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".stl", ".obj", ".ply", ".off", ".amf", ".3mf", ".gcode", ".gco"
    };

    internal static MeshData Read(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".stl" => StlReader.Read(path),
        ".obj" => ObjReader.Read(path),
        ".ply" => PlyReader.Read(path),
        ".off" => OffReader.Read(path),
        ".amf" => AmfReader.Read(path),
        ".3mf" => ThreeMfReader.Read(path),
        ".gcode" or ".gco" => GCodeReader.Read(path),
        _ => throw new NotSupportedException("Для этого формата требуется SolidWorks.")
    };

    internal static MeshData Read(Stream stream, string extension) => extension.ToLowerInvariant() switch
    {
        ".stl" => StlReader.ReadForThumbnail(stream, 30_000),
        ".obj" => ObjReader.Read(stream),
        ".ply" => PlyReader.Read(stream),
        ".off" => OffReader.Read(stream),
        ".amf" => AmfReader.Read(stream),
        ".3mf" => ThreeMfReader.Read(stream),
        ".gcode" or ".gco" => GCodeReader.Read(stream),
        _ => throw new NotSupportedException("Неизвестный формат потока.")
    };
}
