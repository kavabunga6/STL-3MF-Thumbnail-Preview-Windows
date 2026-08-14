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

        new("SOLIDWORKS", ".sldprt", "Деталь SOLIDWORKS"),
        new("SOLIDWORKS", ".sdlprt", "Алиас/опечатка SLDPRT", false),
        new("SOLIDWORKS", ".sldasm", "Сборка SOLIDWORKS"),
        new("SOLIDWORKS", ".slddrw", "Чертёж SOLIDWORKS"),
        new("SOLIDWORKS", ".prtdot", "Шаблон детали"),
        new("SOLIDWORKS", ".asmdot", "Шаблон сборки"),
        new("SOLIDWORKS", ".drwdot", "Шаблон чертежа"),
        new("SOLIDWORKS / eDrawings", ".eprt", "Опубликованная деталь eDrawings"),
        new("SOLIDWORKS / eDrawings", ".easm", "Опубликованная сборка eDrawings"),
        new("SOLIDWORKS / eDrawings", ".edrw", "Опубликованный чертёж eDrawings"),

        new("Обменные CAD", ".step", "STEP", false),
        new("Обменные CAD", ".stp", "STEP", false),
        new("Обменные CAD", ".stpz", "Сжатый STEP", false),
        new("Обменные CAD", ".iges", "IGES", false),
        new("Обменные CAD", ".igs", "IGES", false),
        new("Обменные CAD", ".x_t", "Parasolid текстовый", false),
        new("Обменные CAD", ".x_b", "Parasolid бинарный", false),
        new("Обменные CAD", ".xmt_txt", "Parasolid XMT", false),
        new("Обменные CAD", ".xmt_bin", "Parasolid XMT binary", false),
        new("Обменные CAD", ".sat", "ACIS SAT", false),
        new("Обменные CAD", ".sab", "ACIS SAB", false),
        new("Обменные CAD", ".ifc", "IFC BIM", false),
        new("Обменные CAD", ".vda", "VDA-FS", false),
        new("Обменные CAD", ".wrl", "VRML", false),
        new("Обменные CAD", ".vrml", "VRML", false),
        new("Обменные CAD", ".3dxml", "3DXML", false),
        new("Обменные CAD", ".jt", "JT", false),
        new("Обменные CAD", ".3dm", "Rhino 3DM", false),

        new("Другие CAD-системы", ".catpart", "CATIA Part", false),
        new("Другие CAD-системы", ".catproduct", "CATIA Product", false),
        new("Другие CAD-системы", ".ipt", "Autodesk Inventor Part", false),
        new("Другие CAD-системы", ".iam", "Autodesk Inventor Assembly", false),
        new("Другие CAD-системы", ".prt", "Creo / NX Part", false),
        new("Другие CAD-системы", ".asm", "Creo / Solid Edge Assembly", false),
        new("Другие CAD-системы", ".neu", "Creo Neutral", false),
        new("Другие CAD-системы", ".xpr", "Creo Part", false),
        new("Другие CAD-системы", ".xas", "Creo Assembly", false),
        new("Другие CAD-системы", ".par", "Solid Edge Part", false),
        new("Другие CAD-системы", ".psm", "Solid Edge Sheet Metal", false),
        new("Другие CAD-системы", ".pwd", "Solid Edge Weldment", false),

        new("2D CAD / чертежи", ".dxf", "AutoCAD DXF", false),
        new("2D CAD / чертежи", ".dwg", "AutoCAD DWG", false)
    };
}
