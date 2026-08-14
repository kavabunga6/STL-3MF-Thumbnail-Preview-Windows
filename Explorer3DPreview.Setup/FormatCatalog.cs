namespace Explorer3DPreview.Setup;

internal sealed record FormatOption(string Group, string Extension, string Description, bool Recommended = true);

internal static class FormatCatalog
{
    internal static readonly FormatOption[] All =
    {
        new("3D-печать — напрямую", ".stl", "STL"),
        new("3D-печать — напрямую", ".3mf", "3D Manufacturing Format"),
        new("3D-печать — напрямую", ".obj", "Wavefront OBJ"),
        new("3D-печать — напрямую", ".ply", "Polygon File Format"),
        new("3D-печать — напрямую", ".amf", "Additive Manufacturing Format"),
        new("3D-печать — напрямую", ".off", "Object File Format"),
        new("3D-печать — напрямую", ".gcode", "G-code траектории"),
        new("3D-печать — напрямую", ".gco", "G-code (короткое расширение)"),

        new("CAD — облегчённый предпросмотр", ".step", "STEP: сетка или каркас B-rep"),
        new("CAD — облегчённый предпросмотр", ".stp", "STEP (короткое расширение)"),

        new("SOLIDWORKS", ".sldprt", "Деталь SOLIDWORKS"),
        new("SOLIDWORKS", ".sdlprt", "Алиас/опечатка SLDPRT", false),
        new("SOLIDWORKS", ".sldasm", "Сборка SOLIDWORKS"),
        new("SOLIDWORKS", ".slddrw", "Чертёж SOLIDWORKS"),
        new("SOLIDWORKS", ".prtdot", "Шаблон детали"),
        new("SOLIDWORKS", ".asmdot", "Шаблон сборки"),
        new("SOLIDWORKS", ".drwdot", "Шаблон чертежа"),
        new("SOLIDWORKS / eDrawings", ".eprt", "Опубликованная деталь eDrawings"),
        new("SOLIDWORKS / eDrawings", ".easm", "Опубликованная сборка eDrawings"),
        new("SOLIDWORKS / eDrawings", ".edrw", "Опубликованный чертёж eDrawings")
    };
}
