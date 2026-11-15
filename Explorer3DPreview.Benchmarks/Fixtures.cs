using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Explorer3DPreview.Benchmarks;

internal static class Fixtures
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Vector3[] Corners =
    {
        new(0,0,0), new(20,0,0), new(20,20,0), new(0,20,0),
        new(0,0,20), new(20,0,20), new(20,20,20), new(0,20,20)
    };
    private static readonly int[][] Faces =
    {
        new[]{0,2,1}, new[]{0,3,2}, new[]{4,5,6}, new[]{4,6,7},
        new[]{0,1,5}, new[]{0,5,4}, new[]{1,2,6}, new[]{1,6,5},
        new[]{2,3,7}, new[]{2,7,6}, new[]{3,0,4}, new[]{3,4,7}
    };

    internal static List<RenderCase> Create(string directory)
    {
        // Never accept user models or reuse a populated directory. Only synthetic fixtures are written.
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("The fixture directory must be empty.");
        Directory.CreateDirectory(directory);
        var cases = new List<RenderCase>();
        void Add(string name, string extension, Action<string> write, bool success = true, params int[] sizes)
        {
            var file = Path.Combine(directory, name + extension);
            write(file);
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            cases.Add(new RenderCase(name, file, success, sizes.Length == 0 ? new[] { 256 } : sizes));
        }

        Add("binary-cube", ".stl", WriteCubeBinary, true, 256, 1024);
        Add("dense-750k", ".stl", p => WriteSphere(p, 501, 750), true, 256, 1024);
        Add("ascii-cube", ".stl", WriteCubeAscii);
        Add("obj-cube", ".obj", p => File.WriteAllText(p,
            string.Join("", Corners.Select(v => $"v {Vector(v)}\n")) +
            string.Join("", Faces.Select(f => $"f {f[0]+1} {f[1]+1} {f[2]+1}\n"))));
        Add("ply-cube", ".ply", p => File.WriteAllText(p,
            "ply\nformat ascii 1.0\nelement vertex 8\nproperty float x\nproperty float y\nproperty float z\n" +
            "element face 12\nproperty list uchar int vertex_indices\nend_header\n" +
            string.Join("", Corners.Select(v => Vector(v) + "\n")) +
            string.Join("", Faces.Select(f => $"3 {f[0]} {f[1]} {f[2]}\n"))));
        Add("off-cube", ".off", p => File.WriteAllText(p, "OFF\n8 12 0\n" +
            string.Join("", Corners.Select(v => Vector(v) + "\n")) +
            string.Join("", Faces.Select(f => $"3 {f[0]} {f[1]} {f[2]}\n"))));
        Add("amf-cube", ".amf", p => File.WriteAllText(p,
            "<amf unit=\"millimeter\"><object id=\"0\"><mesh><vertices>" +
            string.Join("", Corners.Select(v => FormattableString.Invariant(
                $"<vertex><coordinates><x>{v.X}</x><y>{v.Y}</y><z>{v.Z}</z></coordinates></vertex>"))) +
            "</vertices><volume>" + string.Join("", Faces.Select(f =>
                $"<triangle><v1>{f[0]}</v1><v2>{f[1]}</v2><v3>{f[2]}</v3></triangle>")) +
            "</volume></mesh></object></amf>"));
        Add("3mf-mesh", ".3mf", p => WriteThreeMf(p, false));
        Add("3mf-preview", ".3mf", p => WriteThreeMf(p, true));
        Add("gcode-tower", ".gcode", WriteTower, true, 256, 1024);
        Add("gcode-tiny", ".gcode", p => File.WriteAllText(p,
            "G90\nM83\nG1 X0 Y0 Z0.2\nG1 X0.1 Y0 E1\n"));
        Add("gcode-tabs", ".gcode", p => File.WriteAllText(p,
            "G90\nM83\nG1\tX10\tY10\tZ0.2\nG1\tX30\tY10\tE1\nG1\tX30\tY30\tE1\n" +
            "G1\tX10\tY30\tE1\nG1\tX10\tY10\tE1\n"));
        Add("step-tessellation", ".step", p => File.WriteAllText(p,
            "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n" +
            "#1=COORDINATES_LIST('',4,((0.,0.,0.),(20.,0.,0.),(0.,20.,0.),(0.,0.,20.)));\n" +
            "#2=TRIANGULATED_SURFACE_SET('',#1,4,(),(),((1,2,3),(1,2,4),(2,3,4),(1,3,4)));\n" +
            "ENDSEC;\nEND-ISO-10303-21;"));
        Add("step-wireframe", ".stp", p => File.WriteAllText(p,
            "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n#1=CARTESIAN_POINT('',(0.,0.,0.));\n" +
            "#2=DIRECTION('',(0.,0.,1.));\n#3=DIRECTION('',(1.,0.,0.));\n" +
            "#4=AXIS2_PLACEMENT_3D('',#1,#2,#3);\n#5=CARTESIAN_POINT('',(5.,0.,0.));\n" +
            "#6=VERTEX_POINT('',#5);\n#7=CIRCLE('',#4,5.);\n#8=EDGE_CURVE('',#6,#6,#7,.T.);\n" +
            "ENDSEC;\nEND-ISO-10303-21;"));
        Add("invalid-stl", ".stl", p => File.WriteAllText(p, "solid broken\nvertex 0 x 0\nendsolid"), false);
        return cases;
    }

    internal static Dictionary<string, InputStamp> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(directory, p), p =>
            {
                var info = new FileInfo(p);
                using var input = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var hash = SHA256.Create();
                return new InputStamp(info.Length, info.LastWriteTimeUtc.Ticks,
                    Convert.ToHexString(hash.ComputeHash(input)), info.IsReadOnly);
            });

    internal static void AssertUnchanged(Dictionary<string, InputStamp> before, string directory)
    {
        var after = Snapshot(directory);
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out var stamp) || stamp != p.Value))
            throw new IOException("Input files were modified, deleted, renamed, or accompanied by new files.");
    }

    private static string Vector(Vector3 v) => FormattableString.Invariant($"{v.X} {v.Y} {v.Z}");

    private static void WriteCubeBinary(string file)
    {
        using var writer = new BinaryWriter(File.Create(file));
        writer.Write(new byte[80]);
        writer.Write((uint)Faces.Length);
        foreach (var f in Faces) WriteTriangle(writer, Corners[f[0]], Corners[f[1]], Corners[f[2]]);
    }

    private static void WriteTriangle(BinaryWriter writer, Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b-a, c-a);
        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        foreach (var v in new[] { normal, a, b, c }) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); }
        writer.Write((ushort)0);
    }

    private static void WriteCubeAscii(string file)
    {
        using var writer = new StreamWriter(file, false, Encoding.ASCII);
        writer.WriteLine("solid cube");
        foreach (var f in Faces)
        {
            writer.WriteLine("facet normal 0 0 0\nouter loop");
            foreach (var index in f) writer.WriteLine("vertex " + Vector(Corners[index]));
            writer.WriteLine("endloop\nendfacet");
        }
        writer.WriteLine("endsolid cube");
    }

    private static void WriteSphere(string file, int latitudes, int longitudes)
    {
        using var writer = new BinaryWriter(File.Create(file));
        writer.Write(new byte[80]);
        writer.Write((uint)(2 * (latitudes - 1) * longitudes));
        Vector3 Point(int lat, int lon)
        {
            var phi = Math.PI * lat / latitudes;
            var theta = 2 * Math.PI * lon / longitudes;
            return new Vector3((float)(20*Math.Sin(phi)*Math.Cos(theta)),
                (float)(20*Math.Sin(phi)*Math.Sin(theta)), (float)(20*Math.Cos(phi)));
        }
        for (var lat=0; lat<latitudes; lat++)
            for (var lon=0; lon<longitudes; lon++)
            {
                var a=Point(lat,lon); var b=Point(lat,lon+1);
                var c=Point(lat+1,lon); var d=Point(lat+1,lon+1);
                if (lat>0) WriteTriangle(writer,a,c,b);
                if (lat<latitudes-1) WriteTriangle(writer,b,c,d);
            }
    }

    private static void WriteThreeMf(string file, bool preview)
    {
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("3D/3dmodel.model").Open(), new UTF8Encoding(false)))
            writer.Write("<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                "<resources><object id=\"1\" type=\"model\"><mesh><vertices>" +
                string.Join("", Corners.Select(v => FormattableString.Invariant($"<vertex x=\"{v.X}\" y=\"{v.Y}\" z=\"{v.Z}\"/>"))) +
                "</vertices><triangles>" + string.Join("", Faces.Select(f =>
                    $"<triangle v1=\"{f[0]}\" v2=\"{f[1]}\" v3=\"{f[2]}\"/>")) +
                "</triangles></mesh></object></resources><build><item objectid=\"1\"/></build></model>");
        if (!preview) return;
        using var bitmap = new Bitmap(64,64);
        using (var graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.White); graphics.FillRectangle(Brushes.Red,12,12,40,40); }
        using var image = archive.CreateEntry("Metadata/thumbnail.png").Open();
        bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void WriteTower(string file)
    {
        using var writer = new StreamWriter(file, false, Encoding.ASCII);
        writer.WriteLine("G90\nM83\nG1 X30 Y20 Z0.2");
        for (var layer=1; layer<=80; layer++)
        {
            writer.WriteLine("G1 Z" + (layer*0.2).ToString("F2", Invariant));
            for (var step=1; step<=64; step++)
            {
                var angle=step*2*Math.PI/64;
                writer.WriteLine(FormattableString.Invariant(
                    $"G1 X{20+10*Math.Cos(angle):F4} Y{20+10*Math.Sin(angle):F4} E0.05"));
            }
        }
    }
}
