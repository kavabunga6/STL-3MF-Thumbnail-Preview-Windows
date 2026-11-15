using System.Numerics;
using System.IO.Compression;
using System.Text;
using Explorer3DPreview.Mesh;
using Explorer3DPreview.Shell;

if (args.Length == 3 && args[0].Equals("--probe-shell", StringComparison.OrdinalIgnoreCase))
{
    using var shellImage = ShellThumbnailReader.TryLoad(args[1], 256);
    if (shellImage is null)
    {
        Console.WriteLine("IShellItemImageFactory returned no thumbnail.");
        return 3;
    }
    shellImage.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"Shell thumbnail: {shellImage.Width}x{shellImage.Height}");
    return 0;
}

if (args.Length == 3 && args[0].Equals("--probe-com", StringComparison.OrdinalIgnoreCase))
{
    object? comObject = null;
    IntPtr comBitmap = IntPtr.Zero;
    try
    {
        var type = Type.GetTypeFromCLSID(new Guid(MeshThumbnailProvider.ClassId), throwOnError: true)!;
        comObject = Activator.CreateInstance(type) ?? throw new InvalidOperationException("COM activation returned null.");
        var initializer = (IInitializeWithFile)comObject;
        var provider = (IThumbnailProvider)comObject;
        var initializeResult = initializer.Initialize(args[1], 0);
        var thumbnailResult = provider.GetThumbnail(256, out comBitmap, out var alphaType);
        Console.WriteLine($"Initialize=0x{initializeResult:X8}; GetThumbnail=0x{thumbnailResult:X8}; HBITMAP=0x{comBitmap.ToInt64():X}; Alpha={alphaType}");
        if (initializeResult != 0 || thumbnailResult != 0 || comBitmap == IntPtr.Zero) return 2;
        using var image = Image.FromHbitmap(comBitmap);
        image.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
        return 0;
    }
    finally
    {
        if (comBitmap != IntPtr.Zero) NativeMethods.DeleteObject(comBitmap);
        if (comObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject);
    }
}

if (args.Length == 3 && args[0].Equals("--render", StringComparison.OrdinalIgnoreCase))
{
    var provider = new MeshThumbnailProvider();
    if (provider.Initialize(args[1], 0) != 0 ||
        provider.GetThumbnail(256, out var renderedHandle, out _) != 0 || renderedHandle == IntPtr.Zero)
        throw new InvalidOperationException("Не удалось сгенерировать миниатюру.");
    try
    {
        using var renderedImage = Image.FromHbitmap(renderedHandle);
        renderedImage.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
    }
    finally { NativeMethods.DeleteObject(renderedHandle); }
    Console.WriteLine(args[2]);
    return 0;
}

var testDirectory = Path.Combine(Path.GetTempPath(), $"Explorer3DPreview.Tests.{Guid.NewGuid():N}");
Directory.CreateDirectory(testDirectory);

try
{
    TestAscii(testDirectory);
    TestBinary(testDirectory);
    TestInvalid(testDirectory);
    TestAdditionalFormats(testDirectory);
    TestStep(testDirectory);
    TestGCodeThumbnail(testDirectory);
    TestThreeMfEmbeddedThumbnail(testDirectory);
    TestMeshRendering();
    TestLargeObjStream();
    TestThumbnailProvider(testDirectory);
    TestEmbeddedImageDecoder();
    Console.WriteLine("Все smoke-тесты форматов и Thumbnail Provider пройдены.");
    return 0;
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}

static void TestAscii(string directory)
{
    var path = Path.Combine(directory, "triangle-ascii.stl");
    File.WriteAllText(path, @"solid test
  facet normal 0 0 1
    outer loop
      vertex 0 0 0
      vertex 2 0 0
      vertex 0 3 0
    endloop
  endfacet
endsolid test", Encoding.ASCII);

    var mesh = StlReader.Read(path);
    Assert(mesh.Triangles.Count == 1, "ASCII: число треугольников");
    Assert(mesh.Min == Vector3.Zero, "ASCII: minimum");
    Assert(mesh.Max == new Vector3(2, 3, 0), "ASCII: maximum");
}

static void TestBinary(string directory)
{
    var path = Path.Combine(directory, "triangle-binary.stl");
    using (var stream = File.Create(path))
    using (var writer = new BinaryWriter(stream))
    {
        writer.Write(new byte[80]);
        writer.Write((uint)1);
        WriteVector(writer, Vector3.Zero);
        WriteVector(writer, new Vector3(-1, -2, 0));
        WriteVector(writer, new Vector3(1, -2, 0));
        WriteVector(writer, new Vector3(0, 2, 0));
        writer.Write((ushort)0);
    }

    var mesh = StlReader.Read(path);
    Assert(mesh.Triangles.Count == 1, "Binary: число треугольников");
    Assert(Math.Abs(mesh.Triangles[0].Normal.Z - 1f) < 0.001f, "Binary: расчёт нормали");
}

static void TestInvalid(string directory)
{
    var path = Path.Combine(directory, "invalid.stl");
    File.WriteAllText(path, "solid broken\nvertex 0 x 0\nendsolid", Encoding.ASCII);
    try
    {
        _ = StlReader.Read(path);
        throw new Exception("Повреждённый STL был принят.");
    }
    catch (InvalidDataException)
    {
        // Expected.
    }
}

static void TestThumbnailProvider(string directory)
{
    var path = Path.Combine(directory, "thumbnail-smoke.STL");
    File.WriteAllText(path, @"solid test
facet normal 0 0 1
outer loop
vertex 0 0 0
vertex 1 0 0
vertex 0 1 0
endloop
endfacet
endsolid test", Encoding.ASCII);

    var provider = new MeshThumbnailProvider();
    Assert(provider.Initialize(path, 0) == 0, "Thumbnail Provider: Initialize");
    var started = System.Diagnostics.Stopwatch.StartNew();
    Assert(provider.GetThumbnail(256, out var handle, out var alpha) == 0 && handle != IntPtr.Zero,
        "Thumbnail Provider: GetThumbnail");
    try
    {
        using var image = Image.FromHbitmap(handle);
        Assert(image.Width == 256 && image.Height == 256, "Thumbnail Provider: размер");
        Assert(alpha == ThumbnailAlphaType.Rgb, "Thumbnail Provider: alpha type");
    }
    finally { NativeMethods.DeleteObject(handle); }
    Assert(started.Elapsed < TimeSpan.FromSeconds(3), "Thumbnail Provider: время генерации");

    var shellStream = ShellStream.Open(path);
    try
    {
        var streamProvider = new MeshThumbnailProvider();
        Assert(((IInitializeWithStream)streamProvider).Initialize(shellStream, 0) == 0,
            "Thumbnail Provider: IInitializeWithStream");
        Assert(streamProvider.GetThumbnail(256, out var streamHandle, out _) == 0 && streamHandle != IntPtr.Zero,
            "Thumbnail Provider: stream GetThumbnail");
        NativeMethods.DeleteObject(streamHandle);
    }
    finally
    {
        if (System.Runtime.InteropServices.Marshal.IsComObject(shellStream))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shellStream);
    }
}

static void TestEmbeddedImageDecoder()
{
    using var source = new Bitmap(64, 48);
    using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.CornflowerBlue);
    using var stream = new MemoryStream();
    source.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
    var wrapped = new byte[stream.Length + 13];
    Buffer.BlockCopy(stream.ToArray(), 0, wrapped, 13, (int)stream.Length);
    using var decoded = CompoundPreviewExtractor.DecodeImage(wrapped);
    Assert(decoded is { Width: 64, Height: 48 }, "Embedded preview PNG decoder");
}

static void TestAdditionalFormats(string directory)
{
    var obj = Path.Combine(directory, "quad.obj");
    File.WriteAllText(obj, "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n");
    Assert(MeshFileLoader.Read(obj).Triangles.Count == 2, "OBJ: triangulation");

    var off = Path.Combine(directory, "quad.off");
    File.WriteAllText(off, "OFF\n4 1 0\n0 0 0\n1 0 0\n1 1 0\n0 1 0\n4 0 1 2 3\n");
    Assert(MeshFileLoader.Read(off).Triangles.Count == 2, "OFF: triangulation");

    var ply = Path.Combine(directory, "triangle.ply");
    File.WriteAllText(ply, "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n" +
        "element face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n");
    Assert(MeshFileLoader.Read(ply).Triangles.Count == 1, "PLY: ASCII");

    var amf = Path.Combine(directory, "triangle.amf");
    File.WriteAllText(amf, "<amf><object id=\"0\"><mesh><vertices>" +
        "<vertex><coordinates><x>0</x><y>0</y><z>0</z></coordinates></vertex>" +
        "<vertex><coordinates><x>1</x><y>0</y><z>0</z></coordinates></vertex>" +
        "<vertex><coordinates><x>0</x><y>1</y><z>0</z></coordinates></vertex>" +
        "</vertices><volume><triangle><v1>0</v1><v2>1</v2><v3>2</v3></triangle></volume></mesh></object></amf>");
    Assert(MeshFileLoader.Read(amf).Triangles.Count == 1, "AMF");

    var threeMf = Path.Combine(directory, "triangle.3MF");
    using (var archive = ZipFile.Open(threeMf, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(archive.CreateEntry("3D/3dmodel.model").Open()))
        writer.Write("<model><resources><object id=\"1\"><mesh><vertices>" +
            "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>" +
            "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object></resources>" +
            "<build><item objectid=\"1\"/></build></model>");
    Assert(MeshFileLoader.Read(threeMf).Triangles.Count == 1, "3MF");

    var gcode = Path.Combine(directory, "path.gcode");
    File.WriteAllText(gcode, "G90\nM82\nG1 X0 Y0 Z0.2 E0\nG1 X10 Y0 E1\nG1 X10 Y10 E2\n");
    Assert(MeshFileLoader.Read(gcode).Triangles.Count == 4, "G-code toolpath");

    foreach (var streamPath in new[] { obj, off, ply, amf, threeMf, gcode })
    {
        using var stream = new FileStream(streamPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert(ThumbnailMeshLoader.Read(stream).Triangles.Count > 0,
            $"Stream thumbnail: {Path.GetExtension(streamPath)}");
    }
}

static void TestGCodeThumbnail(string directory)
{
    // A 20 mm square tower (40 layers) plus a purge line far away from it, as slicers write it.
    var text = new StringBuilder("G90\nM83\nG1 X2 Y2 Z0.3\nG1 X60 Y2 E2\nG1 X60 Y2.4 E0.1\nG1 X2 Y2.4 E2\n");
    for (var layer = 1; layer <= 40; layer++)
    {
        text.Append(System.FormattableString.Invariant($"G1 Z{layer * 0.2:F2}\nG1 X100 Y100\n"));
        text.Append("G1 X120 Y100 E1\nG1 X120 Y120 E1\nG1 X100 Y120 E1\nG1 X100 Y100 E1\n");
    }
    var plain = Path.Combine(directory, "tower.gcode");
    File.WriteAllText(plain, text.ToString(), Encoding.ASCII);

    List<GCodeToolpath.Seg> toolpath;
    using (var plainStream = File.OpenRead(plain)) toolpath = GCodeToolpath.ReadToolpath(plainStream);
    Assert(toolpath.Count == 3 + 40 * 4, "G-code thumbnail: extruding segments parsed");
    var filtered = GCodeToolpath.RemoveIsolatedLines(toolpath);
    Assert(filtered.Count == 40 * 4 && filtered.All(segment => segment.X0 >= 100),
        "G-code thumbnail: purge line removed, tower kept");

    var provider = new MeshThumbnailProvider();
    Assert(provider.Initialize(plain, 0) == 0, "G-code thumbnail: Initialize");
    Assert(provider.GetThumbnail(128, out var toolpathHandle, out _) == 0 && toolpathHandle != IntPtr.Zero,
        "G-code thumbnail: toolpath render via provider");
    using (var rendered = Image.FromHbitmap(toolpathHandle))
    {
        Assert(rendered.Width == 128 && rendered.Height == 128, "G-code thumbnail: requested size");
        // The tower has no infill, so its middle may be empty: measure how much of the picture is covered instead.
        var bitmap = new Bitmap(rendered);
        int painted = 0, minX = bitmap.Width, maxX = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var background = bitmap.GetPixel(2, y);   // the backdrop only changes from row to row
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) <= 60) continue;
                painted++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
        }
        Assert(painted > bitmap.Width * bitmap.Height / 10 && maxX - minX > bitmap.Width * 0.8,
            "G-code thumbnail: model fills the picture (purge line not shrinking it)");
    }
    NativeMethods.DeleteObject(toolpathHandle);

    // Slicer-embedded preview wins over the toolpath: here a solid red PNG.
    using var source = new Bitmap(40, 40);
    using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.Red);
    using var png = new MemoryStream();
    source.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    var base64 = Convert.ToBase64String(png.ToArray());
    var withPreview = new StringBuilder("; generated by PrusaSlicer 2.7\n;\n; thumbnail begin 40x40 " + base64.Length + "\n");
    for (var i = 0; i < base64.Length; i += 78)
        withPreview.Append("; ").Append(base64, i, Math.Min(78, base64.Length - i)).Append('\n');
    withPreview.Append("; thumbnail end\n").Append(text);
    var embeddedPath = Path.Combine(directory, "with-preview.gcode");
    File.WriteAllText(embeddedPath, withPreview.ToString(), Encoding.ASCII);

    Assert(provider.Initialize(embeddedPath, 0) == 0, "G-code thumbnail: Initialize (embedded preview)");
    Assert(provider.GetThumbnail(64, out var embeddedHandle, out _) == 0 && embeddedHandle != IntPtr.Zero,
        "G-code thumbnail: embedded preview via provider");
    using (var rendered = Image.FromHbitmap(embeddedHandle))
    {
        var pixel = new Bitmap(rendered).GetPixel(32, 32);
        Assert(pixel.R > 200 && pixel.G < 60 && pixel.B < 60, "G-code thumbnail: embedded PNG used instead of toolpath");
    }
    NativeMethods.DeleteObject(embeddedHandle);
}

static void TestLargeObjStream()
{
    // Exporters write every vertex first, so the first "f" line lies far beyond the 64 KB the format detector reads.
    var text = new StringBuilder("# exported\nmtllib part.mtl\no Part\n");
    for (var i = 0; i < 8000; i++)
        text.Append(FormattableString.Invariant($"v {i % 40} {i / 40} {(i * 7) % 13}\n"));
    text.Append("usemtl Part\n");
    for (var i = 1; i <= 100; i++) text.Append(FormattableString.Invariant($"f {i} {i + 1} {i + 2}\n"));
    var content = text.ToString();
    Assert(content.IndexOf("\nf ", StringComparison.Ordinal) > 70_000, "OBJ stream: faces start beyond the detector window");

    using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));
    Assert(StreamFormatDetector.Detect(stream) == ".obj", "OBJ stream: format recognised from vertex lines alone");
    Assert(ThumbnailMeshLoader.Read(stream).Triangles.Count > 50, "OBJ stream: thumbnail loader reads the large OBJ");
}

static void TestMeshRendering()
{
    // Unit cube: every triangle is drawn, one colour, three visible faces lit differently.
    var corners = new Vector3[8];
    for (var i = 0; i < 8; i++)
        corners[i] = new Vector3((i & 1) == 0 ? 0 : 10, (i & 2) == 0 ? 0 : 10, (i & 4) == 0 ? 0 : 10);
    int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
    var triangles = new List<Triangle>();
    Triangle MakeTriangle(Vector3 a, Vector3 b, Vector3 c) => new(a, b, c, Vector3.Normalize(Vector3.Cross(b - a, c - a)));
    foreach (var face in faces)
    {
        triangles.Add(MakeTriangle(corners[face[0]], corners[face[1]], corners[face[2]]));
        triangles.Add(MakeTriangle(corners[face[0]], corners[face[2]], corners[face[3]]));
    }
    using var image = Explorer3DPreview.UI.ThumbnailRenderer.Render(new MeshData(triangles), 128);
    Assert(image.Width == 128 && image.Height == 128, "Mesh render: requested size");

    int painted = 0, minX = image.Width, maxX = -1;
    var levels = new HashSet<int>();
    for (var y = 0; y < image.Height; y++)
    {
        var background = image.GetPixel(1, y);   // the backdrop only changes from row to row
        for (var x = 0; x < image.Width; x++)
        {
            var pixel = image.GetPixel(x, y);
            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) < 30) continue;
            painted++;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            levels.Add((pixel.R + pixel.G + pixel.B) / 40);
        }
    }
    Assert(painted > image.Width * image.Height / 10, "Mesh render: cube drawn");
    Assert(maxX - minX > image.Width * 0.6, "Mesh render: cube fills the picture");
    Assert(levels.Count >= 3, "Mesh render: shading (faces lit differently)");
}

static void TestThreeMfEmbeddedThumbnail(string directory)
{
    using var source = new Bitmap(40, 40);
    using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.Red);
    using var png = new MemoryStream();
    source.Save(png, System.Drawing.Imaging.ImageFormat.Png);

    const string model = "<?xml version=\"1.0\"?><model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
        "<resources><object id=\"1\" type=\"model\"><mesh><vertices>" +
        "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"10\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"10\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"10\"/>" +
        "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/><triangle v1=\"0\" v2=\"1\" v3=\"3\"/>" +
        "<triangle v1=\"1\" v2=\"2\" v3=\"3\"/><triangle v1=\"0\" v2=\"2\" v3=\"3\"/></triangles></mesh></object></resources>" +
        "<build><item objectid=\"1\"/></build></model>";

    string WritePackage(string name, bool withPreview)
    {
        var path = Path.Combine(directory, name);
        using var file = File.Create(path);
        using var archive = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("3D/3dmodel.model").Open(), new UTF8Encoding(false)))
            writer.Write(model);
        if (withPreview)
        {
            using var entry = archive.CreateEntry("Metadata/thumbnail.png").Open();
            entry.Write(png.ToArray(), 0, (int)png.Length);
        }
        return path;
    }

    var withPreviewPath = WritePackage("with-preview.3mf", withPreview: true);
    var provider = new MeshThumbnailProvider();
    Assert(provider.Initialize(withPreviewPath, 0) == 0, "3MF thumbnail: Initialize (slicer preview)");
    Assert(provider.GetThumbnail(64, out var previewHandle, out _) == 0 && previewHandle != IntPtr.Zero,
        "3MF thumbnail: slicer preview via provider");
    using (var rendered = Image.FromHbitmap(previewHandle))
    {
        var pixel = new Bitmap(rendered).GetPixel(32, 32);
        Assert(pixel.R > 200 && pixel.G < 60 && pixel.B < 60, "3MF thumbnail: Metadata/thumbnail.png used instead of the mesh render");
    }
    NativeMethods.DeleteObject(previewHandle);

    var plainPath = WritePackage("without-preview.3mf", withPreview: false);
    Assert(provider.Initialize(plainPath, 0) == 0, "3MF thumbnail: Initialize (no preview)");
    Assert(provider.GetThumbnail(64, out var meshHandle, out _) == 0 && meshHandle != IntPtr.Zero,
        "3MF thumbnail: package without preview falls back to the mesh render");
    NativeMethods.DeleteObject(meshHandle);
}

static void TestStep(string directory)
{
    var tessellated = Path.Combine(directory, "triangle.STEP");
    File.WriteAllText(tessellated, @"ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('thumbnail test'),'2;1');
ENDSEC;
DATA;
#1=COORDINATES_LIST('',3,((0.,0.,0.),(2.,0.,0.),(0.,3.,0.)));
#2=TRIANGULATED_SURFACE_SET('',#1,3,(),(),((1,2,3)));
ENDSEC;
END-ISO-10303-21;", Encoding.ASCII);
    var tessellatedMesh = MeshFileLoader.Read(tessellated);
    Assert(tessellatedMesh.Triangles.Count == 1, "STEP AP242: triangulated surface set");
    var provider = new MeshThumbnailProvider();
    Assert(provider.Initialize(tessellated, 0) == 0, "STEP AP242: Thumbnail Provider Initialize");
    Assert(provider.GetThumbnail(256, out var stepHandle, out _) == 0 && stepHandle != IntPtr.Zero,
        "STEP AP242: Thumbnail Provider GetThumbnail");
    NativeMethods.DeleteObject(stepHandle);

    var wireframe = Path.Combine(directory, "circle.stp");
    File.WriteAllText(wireframe, @"ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('thumbnail test'),'2;1');
ENDSEC;
DATA;
#1=CARTESIAN_POINT('',(0.,0.,0.));
#2=DIRECTION('',(0.,0.,1.));
#3=DIRECTION('',(1.,0.,0.));
#4=AXIS2_PLACEMENT_3D('',#1,#2,#3);
#5=CARTESIAN_POINT('',(5.,0.,0.));
#6=VERTEX_POINT('',#5);
#7=CIRCLE('',#4,5.);
#8=EDGE_CURVE('',#6,#6,#7,.T.);
ENDSEC;
END-ISO-10303-21;", Encoding.ASCII);
    var wireframeMesh = MeshFileLoader.Read(wireframe);
    Assert(wireframeMesh.Triangles.Count == 0 && wireframeMesh.Segments.Count >= 32,
        "STEP B-rep: circle wireframe");
    using (var stream = new FileStream(wireframe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        var streamMesh = ThumbnailMeshLoader.Read(stream);
        Assert(streamMesh.Segments.Count >= 32, "STEP B-rep: stream format detection");
    }
    using var image = Explorer3DPreview.UI.ThumbnailRenderer.Render(wireframeMesh, 256);
    Assert(image.Width == 256 && image.Height == 256, "STEP B-rep: thumbnail render");
}

static void WriteVector(BinaryWriter writer, Vector3 value)
{
    writer.Write(value.X);
    writer.Write(value.Y);
    writer.Write(value.Z);
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new Exception($"Тест не пройден: {name}");
}

internal static class ShellStream
{
    internal static System.Runtime.InteropServices.ComTypes.IStream Open(string path)
    {
        const uint stgmReadShareDenyWrite = 0x20;
        var result = SHCreateStreamOnFileEx(path, stgmReadShareDenyWrite, 0, false, null, out var stream);
        if (result != 0) throw new InvalidOperationException($"SHCreateStreamOnFileEx: 0x{result:X8}");
        return stream;
    }

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(string path, uint mode, uint attributes,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool create,
        System.Runtime.InteropServices.ComTypes.IStream? template,
        out System.Runtime.InteropServices.ComTypes.IStream stream);
}
