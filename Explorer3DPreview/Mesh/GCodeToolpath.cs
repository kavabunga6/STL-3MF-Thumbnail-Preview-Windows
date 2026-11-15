using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Explorer3DPreview.Mesh;

/// <summary>
/// Pure (no System.Drawing) G-code thumbnail logic: embedded-preview extraction, toolpath parsing,
/// purge/skirt removal and a small software z-buffer renderer that returns ARGB pixels.
/// </summary>
internal static class GCodeToolpath
{
    internal const long MaxFileSize = 512L * 1024 * 1024;   // streamed line by line, never loaded whole
    private const int MaxStoredSegments = 3_000_000;         // uniform thinning beyond this
    private const int MaxDrawnSegments = 600_000;
    private const int HeadScanBytes = 2 * 1024 * 1024;
    private const int TailScanBytes = 512 * 1024;

    internal struct Seg
    {
        public float X0, Y0, X1, Y1, Z;
    }

    // ---------------------------------------------------------------- embedded image

    private static readonly Regex BeginRegex = new(
        @"^;\s*(?:thumbnail(?:_[A-Za-z]+)?|png|jpg|jpeg)\s+begin\s+(\d+)\s*[x*X]\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EndRegex = new(
        @"^;\s*(?:thumbnail(?:_[A-Za-z]+)?|png|jpg|jpeg)\s+end",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the bytes of the largest PNG/JPEG preview the slicer embedded in the comments
    /// ("; thumbnail begin WxH N" ... "; thumbnail end"), or null. QOI and other formats are ignored.
    /// The head of the file is scanned first; the tail is only scanned when nothing was found there.
    /// </summary>
    internal static byte[]? TryReadEmbeddedImage(Stream stream)
    {
        byte[]? best = null;
        long bestArea = 0;
        foreach (var text in ReadScanWindows(stream))
        {
            StringBuilder? data = null;
            long declaredArea = 0;
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
            {
                if (data is null)
                {
                    var begin = BeginRegex.Match(line);
                    if (!begin.Success) continue;
                    data = new StringBuilder();
                    declaredArea = long.Parse(begin.Groups[1].Value, CultureInfo.InvariantCulture) *
                                   long.Parse(begin.Groups[2].Value, CultureInfo.InvariantCulture);
                    continue;
                }
                if (EndRegex.IsMatch(line))
                {
                    var bytes = TryDecodeBase64Image(data.ToString());
                    data = null;
                    if (bytes is not null && declaredArea >= bestArea) { best = bytes; bestArea = declaredArea; }
                    continue;
                }
                var trimmed = line.TrimStart(';', ' ', '\t').TrimEnd();
                if (trimmed.Length > 0 && data.Length < 8_000_000) data.Append(trimmed);
            }
            if (best is not null) break;
        }
        return best;
    }

    private static IEnumerable<string> ReadScanWindows(Stream stream)
    {
        var length = stream.Length;
        var headLength = (int)Math.Min(length, HeadScanBytes);
        yield return ReadRange(stream, 0, headLength);
        if (length > headLength)
        {
            var tailStart = Math.Max(headLength, length - TailScanBytes);
            var tail = ReadRange(stream, tailStart, (int)(length - tailStart));
            // The tail window may start in the middle of a line: drop the first, possibly partial, line.
            var newline = tail.IndexOf('\n');
            yield return newline >= 0 ? tail[(newline + 1)..] : tail;
        }
    }

    private static string ReadRange(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read <= 0) break;
            total += read;
        }
        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static byte[]? TryDecodeBase64Image(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            var isJpg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
            return isPng || isJpg ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    // ---------------------------------------------------------------- toolpath parsing

    internal static List<Seg> ReadToolpath(Stream stream)
    {
        var list = new List<Seg>(1 << 16);
        float x = 0, y = 0, z = 0, e = 0;
        var absXyz = true;
        var absE = true;
        var stride = 1;
        long counter = 0;

        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1 << 16, leaveOpen: true);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.AsSpan();
            var semicolon = line.IndexOf(';');
            if (semicolon >= 0) line = line[..semicolon];
            line = line.Trim();
            if (line.Length < 2) continue;

            var c0 = line[0];
            if (c0 != 'G' && c0 != 'g' && c0 != 'M' && c0 != 'm') continue;
            var split = line.IndexOf(' ');
            var command = split < 0 ? line : line[..split];
            var args = split < 0 ? ReadOnlySpan<char>.Empty : line[split..];

            if (command.Equals("G90", StringComparison.OrdinalIgnoreCase)) { absXyz = true; continue; }
            if (command.Equals("G91", StringComparison.OrdinalIgnoreCase)) { absXyz = false; continue; }
            if (command.Equals("M82", StringComparison.OrdinalIgnoreCase)) { absE = true; continue; }
            if (command.Equals("M83", StringComparison.OrdinalIgnoreCase)) { absE = false; continue; }
            if (command.Equals("G92", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetWord(args, 'E', out var resetE)) e = resetE;
                continue;
            }

            // Linear moves, plus arcs (G2/G3, e.g. ArcWelder output) approximated by their chord.
            var isMove = command.Equals("G1", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("G0", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("G01", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("G00", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("G2", StringComparison.OrdinalIgnoreCase) ||
                         command.Equals("G3", StringComparison.OrdinalIgnoreCase);
            if (!isMove) continue;

            float nx = x, ny = y, nz = z, ne = e;
            var hasE = false;
            if (TryGetWord(args, 'X', out var vx)) nx = absXyz ? vx : x + vx;
            if (TryGetWord(args, 'Y', out var vy)) ny = absXyz ? vy : y + vy;
            if (TryGetWord(args, 'Z', out var vz)) nz = absXyz ? vz : z + vz;
            if (TryGetWord(args, 'E', out var ve)) { ne = absE ? ve : e + ve; hasE = true; }

            var dx = nx - x;
            var dy = ny - y;
            if (hasE && ne > e + 0.00001f && dx * dx + dy * dy > 0.000001f &&
                float.IsFinite(nx) && float.IsFinite(ny) && float.IsFinite(nz))
            {
                counter++;
                if (counter % stride == 0) list.Add(new Seg { X0 = x, Y0 = y, X1 = nx, Y1 = ny, Z = nz });
                if (list.Count >= MaxStoredSegments)
                {
                    // Keep every second stored segment and sample half as often from now on.
                    var half = new List<Seg>(MaxStoredSegments / 2 + 1);
                    for (var i = 0; i < list.Count; i += 2) half.Add(list[i]);
                    list = half;
                    stride *= 2;
                }
            }
            x = nx; y = ny; z = nz;
            if (hasE) e = ne;
        }
        if (list.Count == 0) throw new InvalidDataException("G-code has no extruding moves.");
        return list;
    }

    /// <summary>Finds the value of a G-code word such as X12.5 among a command's arguments.</summary>
    private static bool TryGetWord(ReadOnlySpan<char> args, char letter, out float value)
    {
        value = 0;
        var i = 0;
        while (i < args.Length)
        {
            while (i < args.Length && (args[i] == ' ' || args[i] == '\t')) i++;
            if (i >= args.Length) break;
            var start = i;
            while (i < args.Length && args[i] != ' ' && args[i] != '\t') i++;
            var word = args.Slice(start, i - start);
            if (word.Length >= 2 && char.ToUpperInvariant(word[0]) == letter &&
                float.TryParse(word[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Drops purge/prime lines, skirts and other detached strokes. Extruded length (and the highest Z) is
    /// accumulated in a 2 mm grid; 8-connected groups of cells are then judged against the largest group:
    /// a group is removed when it holds less than 2% of its length, or when it is flat (never rises above the
    /// first layers) and holds less than 25% of it. A brim stays (it touches the model); a second, taller part
    /// on the plate stays.
    /// </summary>
    internal static List<Seg> RemoveIsolatedLines(List<Seg> segments)
    {
        const float cell = 2f;
        const double tinyFraction = 0.02;
        const double flatFraction = 0.25;
        const float flatHeight = 0.6f;   // "flat" = no higher than this above the lowest layer

        static long KeyOf(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
        static (int, int) CellOf(in Seg s) =>
            ((int)MathF.Floor((s.X0 + s.X1) * 0.5f / cell), (int)MathF.Floor((s.Y0 + s.Y1) * 0.5f / cell));

        var lowest = float.MaxValue;
        var lengths = new Dictionary<long, double>();
        var heights = new Dictionary<long, float>();
        foreach (var s in segments)
        {
            var (cx, cy) = CellOf(s);
            var length = Math.Sqrt((s.X1 - s.X0) * (double)(s.X1 - s.X0) + (s.Y1 - s.Y0) * (double)(s.Y1 - s.Y0));
            var key = KeyOf(cx, cy);
            lengths[key] = lengths.TryGetValue(key, out var existing) ? existing + length : length;
            heights[key] = heights.TryGetValue(key, out var top) ? MathF.Max(top, s.Z) : s.Z;
            lowest = MathF.Min(lowest, s.Z);
        }

        var component = new Dictionary<long, int>(lengths.Count);
        var totals = new List<double>();
        var tops = new List<float>();
        var stack = new Stack<(int, int)>();
        foreach (var start in lengths.Keys)
        {
            if (component.ContainsKey(start)) continue;
            var id = totals.Count;
            var total = 0.0;
            var highest = float.MinValue;
            component[start] = id;
            stack.Push(((int)(start >> 32), unchecked((int)(start & 0xFFFFFFFF))));
            while (stack.Count > 0)
            {
                var (px, py) = stack.Pop();
                var here = KeyOf(px, py);
                total += lengths[here];
                highest = MathF.Max(highest, heights[here]);
                for (var ddx = -1; ddx <= 1; ddx++)
                    for (var ddy = -1; ddy <= 1; ddy++)
                    {
                        var key = KeyOf(px + ddx, py + ddy);
                        if (lengths.ContainsKey(key) && !component.ContainsKey(key))
                        {
                            component[key] = id;
                            stack.Push((px + ddx, py + ddy));
                        }
                    }
            }
            totals.Add(total);
            tops.Add(highest);
        }

        var largest = totals.Max();
        var keep = new bool[totals.Count];
        for (var id = 0; id < keep.Length; id++)
        {
            var tiny = totals[id] < largest * tinyFraction;
            var flatAndSmall = tops[id] <= lowest + flatHeight && totals[id] < largest * flatFraction;
            keep[id] = !tiny && !flatAndSmall;
        }

        var kept = new List<Seg>(segments.Count);
        foreach (var s in segments)
        {
            var (cx, cy) = CellOf(s);
            if (keep[component[KeyOf(cx, cy)]]) kept.Add(s);
        }
        return kept.Count > 0 ? kept : segments;
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>Renders the toolpath and returns size*size opaque ARGB pixels (row-major, top row first).</summary>
    internal static int[] RenderPixels(List<Seg> segments, int size)
    {
        size = Math.Clamp(size, 32, 1024);
        var ss = size <= 512 ? 2 : 1;
        var w = size * ss;

        // Thinning compensation: the fewer segments are drawn, the fatter each stroke must be to keep the surface closed.
        var maxDrawn = size <= 256 ? MaxDrawnSegments / 2 : MaxDrawnSegments;
        var thinning = Math.Max(1.0, segments.Count / (double)maxDrawn);
        if (segments.Count > maxDrawn)
        {
            var step = segments.Count / (double)maxDrawn;
            var thinned = new List<Seg>(maxDrawn);
            for (var i = 0; i < maxDrawn; i++) thinned.Add(segments[(int)(i * step)]);
            segments = thinned;
        }

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var s in segments)
        {
            minX = MathF.Min(minX, MathF.Min(s.X0, s.X1)); maxX = MathF.Max(maxX, MathF.Max(s.X0, s.X1));
            minY = MathF.Min(minY, MathF.Min(s.Y0, s.Y1)); maxY = MathF.Max(maxY, MathF.Max(s.Y0, s.Y1));
            minZ = MathF.Min(minZ, s.Z); maxZ = MathF.Max(maxZ, s.Z);
        }
        var centerX = (minX + maxX) * 0.5f;
        var centerY = (minY + maxY) * 0.5f;
        var centerZ = (minZ + maxZ) * 0.5f;

        // Same view as the mesh renderer: yaw -35 deg around Z, elevation 32 deg.
        const float yaw = MeshRasterizer.Yaw, elev = MeshRasterizer.Elevation;
        var cosYaw = MathF.Cos(yaw); var sinYaw = MathF.Sin(yaw);
        var cosEl = MathF.Cos(elev); var sinEl = MathF.Sin(elev);

        var projected = new (float ax, float ay, float ad, float bx, float by, float bd)[segments.Count];
        float pMinX = float.MaxValue, pMaxX = float.MinValue, pMinY = float.MaxValue, pMaxY = float.MinValue;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            Project(s.X0, s.Y0, s.Z, out var ax, out var ay, out var ad);
            Project(s.X1, s.Y1, s.Z, out var bx, out var by, out var bd);
            projected[i] = (ax, ay, ad, bx, by, bd);
            pMinX = MathF.Min(pMinX, MathF.Min(ax, bx)); pMaxX = MathF.Max(pMaxX, MathF.Max(ax, bx));
            pMinY = MathF.Min(pMinY, MathF.Min(ay, by)); pMaxY = MathF.Max(pMaxY, MathF.Max(ay, by));
        }

        void Project(float px, float py, float pz, out float sx, out float sy, out float depth)
        {
            var rx = px - centerX; var ry = py - centerY; var rz = pz - centerZ;
            var x1 = rx * cosYaw - ry * sinYaw;
            var y1 = rx * sinYaw + ry * cosYaw;
            sx = x1;
            sy = rz * cosEl + y1 * sinEl;
            depth = y1 * cosEl - rz * sinEl;   // larger = farther from the viewer
        }

        var scale = w * 0.88f / MathF.Max(MathF.Max(pMaxX - pMinX, pMaxY - pMinY), 0.001f);
        var offsetX = w * 0.5f - (pMinX + pMaxX) * 0.5f * scale;
        var offsetY = w * 0.5f + (pMinY + pMaxY) * 0.5f * scale;

        var depthBuffer = new float[w * w];
        Array.Fill(depthBuffer, float.PositiveInfinity);

        var radiusMm = Math.Clamp(0.3f * MathF.Sqrt((float)thinning), 0.3f, 0.9f);
        var radius = MathF.Max(1.3f, radiusMm * scale);
        var radiusCeil = (int)MathF.Ceiling(radius);
        var offsets = new List<(int dx, int dy)>();
        for (var ox = -radiusCeil; ox <= radiusCeil; ox++)
            for (var oy = -radiusCeil; oy <= radiusCeil; oy++)
                if (ox * ox + oy * oy <= radius * radius + 0.5f) offsets.Add((ox, oy));

        for (var i = 0; i < segments.Count; i++)
        {
            var (ax, ay, ad, bx, by, bd) = projected[i];
            var x0 = ax * scale + offsetX; var y0 = offsetY - ay * scale;
            var x1 = bx * scale + offsetX; var y1 = offsetY - by * scale;
            var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0))));
            for (var k = 0; k <= steps; k++)
            {
                var t = k / (float)steps;
                var px = (int)MathF.Round(x0 + (x1 - x0) * t);
                var py = (int)MathF.Round(y0 + (y1 - y0) * t);
                var d = ad + (bd - ad) * t;
                foreach (var (dx, dy) in offsets)
                {
                    var ix = px + dx; var iy = py + dy;
                    if ((uint)ix >= (uint)w || (uint)iy >= (uint)w) continue;
                    var index = iy * w + ix;
                    if (d < depthBuffer[index]) depthBuffer[index] = d;
                }
            }
        }

        MeshRasterizer.FillHoles(depthBuffer, null, w);
        var pixels = Shade(depthBuffer, w, scale);
        return ss == 1 ? pixels : MeshRasterizer.Downsample2(pixels, w);
    }

    /// <summary>
    /// Single-colour shading: the surface slope is taken from the depth buffer (central differences), so the
    /// layer lines of the print show up as fine relief, lit by the same light as the mesh renderer.
    /// </summary>
    private static int[] Shade(float[] depth, int w, float scale)
    {
        var pixels = new int[w * w];
        var (lx, ly, lz) = Normalize(-0.5f, -0.6f, 0.62f);

        for (var y = 0; y < w; y++)
        {
            var background = MeshRasterizer.Background(y, w);
            for (var x = 0; x < w; x++)
            {
                var index = y * w + x;
                if (float.IsPositiveInfinity(depth[index])) { pixels[index] = background; continue; }

                float gx = 0, gy = 0;
                if (x > 0 && x < w - 1 && !float.IsPositiveInfinity(depth[index - 1]) && !float.IsPositiveInfinity(depth[index + 1]))
                    gx = (depth[index + 1] - depth[index - 1]) * 0.5f * scale;
                if (y > 0 && y < w - 1 && !float.IsPositiveInfinity(depth[index - w]) && !float.IsPositiveInfinity(depth[index + w]))
                    gy = (depth[index + w] - depth[index - w]) * 0.5f * scale;
                var (nx, ny, nz) = Normalize(-gx, -gy, 1f);
                var lambert = MathF.Max(0f, nx * lx + ny * ly + nz * lz);
                pixels[index] = MeshRasterizer.Shaded(0.55f + 0.55f * lambert);
            }
        }
        return pixels;
    }

    private static (float, float, float) Normalize(float x, float y, float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);
        return length < 1e-6f ? (0f, 0f, 1f) : (x / length, y / length, z / length);
    }
}
