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
