using System.Numerics;

namespace Explorer3DPreview.Mesh;

/// <summary>
/// Software z-buffer renderer for thumbnails (no System.Drawing): one colour, key + fill light, 2x supersampling.
/// Every triangle is drawn, so dense meshes come out as closed surfaces. The same look (colour, backdrop, view)
/// is shared with the G-code toolpath renderer. Z is "up", like in slicers.
/// </summary>
internal static class MeshRasterizer
{
    // ---- shared look -------------------------------------------------------------------------------------
    private const int BaseRed = 48, BaseGreen = 150, BaseBlue = 215;   // the single model colour

    internal const float Yaw = -0.6109f;    // -35 deg around Z
    internal const float Elevation = 0.5585f;   // 32 deg above the horizon

    /// <summary>Opaque ARGB for a backdrop row (light vertical gradient).</summary>
    internal static int Background(int y, int height)
    {
        var t = y / (float)Math.Max(1, height - 1);
        return Pack((int)(248 + (220 - 248) * t), (int)(251 + (230 - 251) * t), (int)(254 + (239 - 254) * t));
    }

    /// <summary>The model colour at a given brightness (1 = as defined, lower = shadow).</summary>
    internal static int Shaded(float brightness) =>
        Pack((int)(BaseRed * brightness), (int)(BaseGreen * brightness), (int)(BaseBlue * brightness));

    internal static int Pack(int r, int g, int b) =>
        unchecked((int)0xFF000000) | (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);

    // ---- rendering ---------------------------------------------------------------------------------------

    /// <summary>Returns size*size opaque ARGB pixels (row-major, top row first).</summary>
    internal static int[] RenderPixels(MeshData mesh, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        var ss = size <= 512 ? 2 : 1;
        var w = size * ss;

        var cosYaw = MathF.Cos(Yaw); var sinYaw = MathF.Sin(Yaw);
        var cosEl = MathF.Cos(Elevation); var sinEl = MathF.Sin(Elevation);
        var center = mesh.Center;

        // x = screen right, y = screen up, z = depth (larger = farther from the viewer)
        Vector3 Project(Vector3 p)
        {
            var rx = p.X - center.X; var ry = p.Y - center.Y; var rz = p.Z - center.Z;
            var x1 = rx * cosYaw - ry * sinYaw;
            var y1 = rx * sinYaw + ry * cosYaw;
            return new Vector3(x1, rz * cosEl + y1 * sinEl, y1 * cosEl - rz * sinEl);
        }

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        float minD = float.MaxValue, maxD = float.MinValue;
        void Extend(Vector3 q)
        {
            minX = MathF.Min(minX, q.X); maxX = MathF.Max(maxX, q.X);
            minY = MathF.Min(minY, q.Y); maxY = MathF.Max(maxY, q.Y);
            minD = MathF.Min(minD, q.Z); maxD = MathF.Max(maxD, q.Z);
        }
        foreach (var t in mesh.Triangles) { Extend(Project(t.A)); Extend(Project(t.B)); Extend(Project(t.C)); }
        foreach (var s in mesh.Segments) { Extend(Project(s.A)); Extend(Project(s.B)); }

        var scale = w * 0.88f / MathF.Max(MathF.Max(maxX - minX, maxY - minY), 0.00001f);
        var offsetX = w * 0.5f - (minX + maxX) * 0.5f * scale;
        var offsetY = w * 0.5f + (minY + maxY) * 0.5f * scale;

        var depth = new float[w * w];
        var brightness = new float[w * w];   // < 0 marks a wireframe pixel
        Array.Fill(depth, float.PositiveInfinity);

        // Key light from the upper left front, soft fill from the lower right (view space, depth towards the scene).
        var key = Vector3.Normalize(new Vector3(-0.5f, 0.6f, -0.62f));
        var fill = Vector3.Normalize(new Vector3(0.7f, -0.25f, -0.67f));

        foreach (var triangle in mesh.Triangles)
        {
            var normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            var length = normal.Length();
            if (length < 1e-20f) continue;   // degenerate: no surface
            normal /= length;

            // Normal in view space; two-sided: flip it towards the viewer.
            var nx1 = normal.X * cosYaw - normal.Y * sinYaw;
            var ny1 = normal.X * sinYaw + normal.Y * cosYaw;
            var view = new Vector3(nx1, normal.Z * cosEl + ny1 * sinEl, ny1 * cosEl - normal.Z * sinEl);
            if (view.Z > 0) view = -view;
            var shade = Math.Min(1.12f, 0.33f + 0.72f * MathF.Max(0f, Vector3.Dot(view, key)) +
                                        0.20f * MathF.Max(0f, Vector3.Dot(view, fill)));

            var a = Project(triangle.A); var b = Project(triangle.B); var c = Project(triangle.C);
            FillTriangle(depth, brightness, w,
                a.X * scale + offsetX, offsetY - a.Y * scale, a.Z,
                b.X * scale + offsetX, offsetY - b.Y * scale, b.Z,
                c.X * scale + offsetX, offsetY - c.Y * scale, c.Z, shade);
        }

        if (mesh.Segments.Count > 0)
        {
            var bias = 0.004f * MathF.Max(maxD - minD, 0.00001f);
            var radius = MathF.Max(1.1f, w * 0.0035f);
            foreach (var segment in mesh.Segments)
            {
                var a = Project(segment.A); var b = Project(segment.B);
                DrawLine(depth, brightness, w, bias, radius,
                    a.X * scale + offsetX, offsetY - a.Y * scale, a.Z,
                    b.X * scale + offsetX, offsetY - b.Y * scale, b.Z);
            }
        }

        FillHoles(depth, brightness, w);
        var pixels = Compose(depth, brightness, w);
        return ss == 1 ? pixels : Downsample2(pixels, w);
    }

    private static void FillTriangle(float[] depth, float[] payload, int w,
        float x0, float y0, float d0, float x1, float y1, float d1, float x2, float y2, float d2, float value)
    {
        var minX = MathF.Min(x0, MathF.Min(x1, x2)); var maxX = MathF.Max(x0, MathF.Max(x1, x2));
        var minY = MathF.Min(y0, MathF.Min(y1, y2)); var maxY = MathF.Max(y0, MathF.Max(y1, y2));
        if (maxX < 0 || maxY < 0 || minX > w || minY > w) return;
        if (!float.IsFinite(minX + maxX + minY + maxY)) return;

        // Pixel i has its centre at i + 0.5.
        var ix0 = Math.Max(0, (int)MathF.Ceiling(minX - 0.5f));
        var ix1 = Math.Min(w - 1, (int)MathF.Floor(maxX - 0.5f));
        var iy0 = Math.Max(0, (int)MathF.Ceiling(minY - 0.5f));
        var iy1 = Math.Min(w - 1, (int)MathF.Floor(maxY - 0.5f));

        if (ix1 < ix0 || iy1 < iy0)
        {
            // Smaller than a pixel and missing every pixel centre: still plot it so fine meshes stay closed.
            var px = (int)MathF.Floor((x0 + x1 + x2) / 3f);
            var py = (int)MathF.Floor((y0 + y1 + y2) / 3f);
            if ((uint)px >= (uint)w || (uint)py >= (uint)w) return;
            var index = py * w + px;
            var d = (d0 + d1 + d2) / 3f;
            if (d < depth[index]) { depth[index] = d; payload[index] = value; }
            return;
        }

        var area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        if (MathF.Abs(area) < 1e-12f) return;   // edge-on
        var inverse = 1f / area;
        const float epsilon = 0.002f;   // a hair of overdraw so shared edges leave no pin-holes

        for (var iy = iy0; iy <= iy1; iy++)
        {
            var py = iy + 0.5f;
            for (var ix = ix0; ix <= ix1; ix++)
            {
                var px = ix + 0.5f;
                var w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) * inverse;
                var w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) * inverse;
                var w2 = 1f - w0 - w1;
                if (w0 < -epsilon || w1 < -epsilon || w2 < -epsilon) continue;
                var d = w0 * d0 + w1 * d1 + w2 * d2;
                var index = iy * w + ix;
                if (d < depth[index]) { depth[index] = d; payload[index] = value; }
            }
        }
    }

    private static void DrawLine(float[] depth, float[] payload, int w, float bias, float radius,
        float x0, float y0, float d0, float x1, float y1, float d1)
    {
        if (!float.IsFinite(x0 + y0 + x1 + y1)) return;
        var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0))));
        steps = Math.Min(steps, 4 * w);
        var r = (int)MathF.Ceiling(radius);
        for (var k = 0; k <= steps; k++)
        {
            var t = k / (float)steps;
            var px = (int)MathF.Round(x0 + (x1 - x0) * t);
            var py = (int)MathF.Round(y0 + (y1 - y0) * t);
            var d = d0 + (d1 - d0) * t;
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius + 0.5f) continue;
                    var ix = px + dx; var iy = py + dy;
                    if ((uint)ix >= (uint)w || (uint)iy >= (uint)w) continue;
                    var index = iy * w + ix;
                    if (d > depth[index] + bias) continue;   // hidden behind a surface
                    depth[index] = MathF.Min(depth[index], d);
                    payload[index] = -1f;
                }
        }
    }

    /// <summary>
    /// Closes pin-holes: an empty pixel with at least 5 painted neighbours takes the nearest neighbour's depth and
    /// payload. <paramref name="payload"/> may be null when only the depth matters.
    /// </summary>
    internal static void FillHoles(float[] depth, float[]? payload, int w)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var newDepth = (float[])depth.Clone();
            var newPayload = payload is null ? null : (float[])payload.Clone();
            for (var y = 1; y < w - 1; y++)
                for (var x = 1; x < w - 1; x++)
                {
                    var index = y * w + x;
                    if (!float.IsPositiveInfinity(depth[index])) continue;
                    var count = 0;
                    var best = float.PositiveInfinity;
                    var bestPayload = 0f;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var n = index + dy * w + dx;
                            if (float.IsPositiveInfinity(depth[n])) continue;
                            count++;
                            if (depth[n] < best) { best = depth[n]; bestPayload = payload is null ? 0f : payload[n]; }
                        }
                    if (count < 5) continue;
                    newDepth[index] = best;
                    if (newPayload is not null) newPayload[index] = bestPayload;
                }
            Array.Copy(newDepth, depth, depth.Length);
            if (payload is not null && newPayload is not null) Array.Copy(newPayload, payload, payload.Length);
        }
    }

    private static int[] Compose(float[] depth, float[] brightness, int w)
    {
        var pixels = new int[w * w];
        var wire = Pack(31, 83, 118);
        for (var y = 0; y < w; y++)
        {
            var background = Background(y, w);
            for (var x = 0; x < w; x++)
            {
                var index = y * w + x;
                if (float.IsPositiveInfinity(depth[index])) { pixels[index] = background; continue; }
                if (brightness[index] < 0f) { pixels[index] = wire; continue; }

                var value = brightness[index];
                // Slightly darker rim where the model meets the backdrop: gives the silhouette some definition.
                if (x == 0 || y == 0 || x == w - 1 || y == w - 1 ||
                    float.IsPositiveInfinity(depth[index - 1]) || float.IsPositiveInfinity(depth[index + 1]) ||
                    float.IsPositiveInfinity(depth[index - w]) || float.IsPositiveInfinity(depth[index + w]))
                    value *= 0.78f;
                pixels[index] = Shaded(value);
            }
        }
        return pixels;
    }

    /// <summary>2x2 box filter: w*w -> (w/2)*(w/2).</summary>
    internal static int[] Downsample2(int[] source, int w)
    {
        var size = w / 2;
        var result = new int[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                int r = 0, g = 0, b = 0;
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var p = source[(y * 2 + dy) * w + x * 2 + dx];
                        r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF;
                    }
                result[y * size + x] = Pack(r / 4, g / 4, b / 4);
            }
        return result;
    }
}
