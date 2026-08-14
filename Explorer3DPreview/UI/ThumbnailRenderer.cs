using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Explorer3DPreview.Mesh;

namespace Explorer3DPreview.UI;

internal static class ThumbnailRenderer
{
    private readonly record struct Projected(PointF A, PointF B, PointF C, float Depth, int Shade);
    private readonly record struct ProjectedSegment(PointF A, PointF B, float Depth);

    internal static Bitmap Render(MeshData mesh, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = size >= 96 ? SmoothingMode.AntiAlias : SmoothingMode.HighSpeed;
        graphics.Clear(Color.White);
        using (var background = new LinearGradientBrush(new Rectangle(0, 0, size, size),
                   Color.FromArgb(248, 251, 254), Color.FromArgb(220, 230, 239), 90f))
            graphics.FillRectangle(background, 0, 0, size, size);

        var rotation = Matrix4x4.CreateRotationY(-0.68f) * Matrix4x4.CreateRotationX(0.52f);
        var scale = size * 0.40f / mesh.Radius;
        var centerX = size * 0.5f;
        var centerY = size * 0.52f;
        var light = Vector3.Normalize(new Vector3(-0.35f, 0.5f, 1f));
        var triangles = new List<Projected>(mesh.Triangles.Count);
        foreach (var triangle in mesh.Triangles)
        {
            var a = Vector3.Transform(triangle.A - mesh.Center, rotation);
            var b = Vector3.Transform(triangle.B - mesh.Center, rotation);
            var c = Vector3.Transform(triangle.C - mesh.Center, rotation);
            var normal = Vector3.TransformNormal(triangle.Normal, rotation);
            var brightness = 0.24f + 0.76f * Math.Abs(Vector3.Dot(normal, light));
            triangles.Add(new Projected(
                new PointF(centerX + a.X * scale, centerY - a.Y * scale),
                new PointF(centerX + b.X * scale, centerY - b.Y * scale),
                new PointF(centerX + c.X * scale, centerY - c.Y * scale),
                (a.Z + b.Z + c.Z) / 3f,
                Math.Clamp((int)(brightness * 31), 0, 31)));
        }
        triangles.Sort(static (left, right) => left.Depth.CompareTo(right.Depth));

        var segments = new List<ProjectedSegment>(mesh.Segments.Count);
        foreach (var segment in mesh.Segments)
        {
            var a = Vector3.Transform(segment.A - mesh.Center, rotation);
            var b = Vector3.Transform(segment.B - mesh.Center, rotation);
            segments.Add(new ProjectedSegment(
                new PointF(centerX + a.X * scale, centerY - a.Y * scale),
                new PointF(centerX + b.X * scale, centerY - b.Y * scale),
                (a.Z + b.Z) * 0.5f));
        }
        segments.Sort(static (left, right) => left.Depth.CompareTo(right.Depth));

        var brushes = Enumerable.Range(0, 32).Select(index =>
        {
            var factor = index / 31f;
            return new SolidBrush(Color.FromArgb(
                Math.Clamp((int)(28 + factor * 45), 0, 255),
                Math.Clamp((int)(115 + factor * 92), 0, 255),
                Math.Clamp((int)(162 + factor * 82), 0, 255)));
        }).ToArray();
        using var edge = new Pen(Color.FromArgb(70, 20, 48, 64), Math.Max(0.5f, size / 420f));
        try
        {
            foreach (var triangle in triangles)
            {
                var points = new[] { triangle.A, triangle.B, triangle.C };
                graphics.FillPolygon(brushes[triangle.Shade], points);
                if (mesh.Triangles.Count <= 20_000 && size >= 64) graphics.DrawPolygon(edge, points);
            }
            if (mesh.Triangles.Count >= 25_000 && size >= 64)
            {
                var pointSize = Math.Max(1.15f, size / 220f);
                foreach (var triangle in triangles)
                {
                    var brush = brushes[triangle.Shade];
                    graphics.FillRectangle(brush, triangle.A.X, triangle.A.Y, pointSize, pointSize);
                    graphics.FillRectangle(brush, triangle.B.X, triangle.B.Y, pointSize, pointSize);
                    graphics.FillRectangle(brush, triangle.C.X, triangle.C.Y, pointSize, pointSize);
                }
            }
            if (segments.Count > 0)
            {
                using var wire = new Pen(Color.FromArgb(225, 31, 83, 118), Math.Max(1.0f, size / 190f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                foreach (var segment in segments) graphics.DrawLine(wire, segment.A, segment.B);
            }
        }
        finally
        {
            foreach (var brush in brushes) brush.Dispose();
        }
        return bitmap;
    }

    internal static Bitmap FitImage(Image source, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var scale = Math.Min(size / (float)source.Width, size / (float)source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
        return result;
    }
}
