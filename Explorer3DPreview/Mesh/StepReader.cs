using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Explorer3DPreview.Mesh;

/// <summary>
/// Lightweight ISO 10303-21 reader for thumbnails. It intentionally does not
/// evaluate trimmed CAD surfaces. Ready-made AP242 tessellation is rendered as
/// a solid mesh; ordinary B-rep files fall back to their topological edge graph.
/// </summary>
internal static class StepReader
{
    private const int MaxEntities = 300_000;
    private const int MaxTriangles = 200_000;
    private const int MaxSegments = 100_000;
    private const int MaxCurveSamples = 64;

    private static readonly HashSet<string> InterestingTypes = new(StringComparer.Ordinal)
    {
        "CARTESIAN_POINT", "DIRECTION", "AXIS2_PLACEMENT_3D", "VERTEX_POINT",
        "EDGE_CURVE", "LINE", "CIRCLE", "ELLIPSE", "POLYLINE", "SURFACE_CURVE",
        "TRIMMED_CURVE", "B_SPLINE_CURVE_WITH_KNOTS", "COORDINATES_LIST",
        "TRIANGULATED_FACE", "TRIANGULATED_SURFACE_SET", "POLY_LOOP"
    };

    private const string NumberPattern = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][-+]?\d+)?";
    private static readonly Regex NumberRegex = new(NumberPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex VectorRegex = new(
        @"\(\s*(?<x>" + NumberPattern + @")\s*,\s*(?<y>" + NumberPattern + @")\s*,\s*(?<z>" + NumberPattern + @")\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TriangleRegex = new(
        @"\(\s*(?<a>\d+)\s*,\s*(?<b>\d+)\s*,\s*(?<c>\d+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private sealed record Entity(string Type, string Body);
    private readonly record struct Placement(Vector3 Center, Vector3 X, Vector3 Y, Vector3 Z);

    internal static MeshData Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(stream);
    }

    internal static MeshData Read(Stream stream)
    {
        if (!stream.CanSeek) throw new NotSupportedException("STEP-поток должен поддерживать позиционирование.");
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
        var text = reader.ReadToEnd();
        if (!text.Contains("ISO-10303-21", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Файл не является STEP Part 21.");

        var entities = ParseEntities(text);
        var points = ParseVectors(entities, "CARTESIAN_POINT");
        var directions = ParseVectors(entities, "DIRECTION");
        var vertices = ParseReferences(entities, "VERTEX_POINT", 1, points);
        var placements = ParsePlacements(entities, points, directions);
        var triangles = ReadTessellation(entities);
        ReadFacetedLoops(entities, points, triangles);
        var segments = ReadEdges(entities, points, vertices, placements);
        return new MeshData(triangles, segments);
    }

    private static Dictionary<int, Entity> ParseEntities(string text)
    {
        var result = new Dictionary<int, Entity>();
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? text.Length : end + 1;
                continue;
            }
            if (text[index] != '#') continue;

            var cursor = index + 1;
            while (cursor < text.Length && char.IsDigit(text[cursor])) cursor++;
            if (cursor == index + 1 || !int.TryParse(text.AsSpan(index + 1, cursor - index - 1), out var id)) continue;
            SkipWhitespace(text, ref cursor);
            if (cursor >= text.Length || text[cursor] != '=') continue;
            cursor++;
            SkipWhitespace(text, ref cursor);
            var typeStart = cursor;
            while (cursor < text.Length && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_')) cursor++;
            if (cursor == typeStart) continue; // Complex entity instance: not needed by the lightweight reader.
            var type = text[typeStart..cursor].ToUpperInvariant();
            SkipWhitespace(text, ref cursor);
            if (cursor >= text.Length || text[cursor] != '(') continue;

            var bodyStart = ++cursor;
            var depth = 1;
            var quoted = false;
            for (; cursor < text.Length && depth > 0; cursor++)
            {
                var character = text[cursor];
                if (character == '\'')
                {
                    if (quoted && cursor + 1 < text.Length && text[cursor + 1] == '\'') { cursor++; continue; }
                    quoted = !quoted;
                    continue;
                }
                if (quoted) continue;
                if (character == '(') depth++;
                else if (character == ')') depth--;
            }
            if (depth != 0) throw new InvalidDataException("Незавершённая STEP-сущность.");
            if (++count > MaxEntities) throw new InvalidDataException("STEP содержит слишком много сущностей.");
            if (InterestingTypes.Contains(type)) result[id] = new Entity(type, text[bodyStart..(cursor - 1)]);
            index = cursor;
        }
        return result;
    }

    private static Dictionary<int, Vector3> ParseVectors(Dictionary<int, Entity> entities, string type)
    {
        var result = new Dictionary<int, Vector3>();
        foreach (var pair in entities)
        {
            if (pair.Value.Type != type) continue;
            var arguments = SplitArguments(pair.Value.Body);
            if (arguments.Count < 2) continue;
            var values = ParseNumbers(arguments[1]);
            if (values.Count >= 3 && values.Take(3).All(double.IsFinite))
                result[pair.Key] = new Vector3((float)values[0], (float)values[1], (float)values[2]);
        }
        return result;
    }

    private static Dictionary<int, Vector3> ParseReferences(Dictionary<int, Entity> entities, string type,
        int argumentIndex, Dictionary<int, Vector3> targets)
    {
        var result = new Dictionary<int, Vector3>();
        foreach (var pair in entities)
        {
            if (pair.Value.Type != type) continue;
            var arguments = SplitArguments(pair.Value.Body);
            if (arguments.Count <= argumentIndex) continue;
            var reference = ParseReference(arguments[argumentIndex]);
            if (reference is { } id && targets.TryGetValue(id, out var value)) result[pair.Key] = value;
        }
        return result;
    }

    private static Dictionary<int, Placement> ParsePlacements(Dictionary<int, Entity> entities,
        Dictionary<int, Vector3> points, Dictionary<int, Vector3> directions)
    {
        var result = new Dictionary<int, Placement>();
        foreach (var pair in entities)
        {
            if (pair.Value.Type != "AXIS2_PLACEMENT_3D") continue;
            var arguments = SplitArguments(pair.Value.Body);
            if (arguments.Count < 2 || ParseReference(arguments[1]) is not { } centerId ||
                !points.TryGetValue(centerId, out var center)) continue;
            var z = arguments.Count > 2 && ParseReference(arguments[2]) is { } zId && directions.TryGetValue(zId, out var zv)
                ? Normalize(zv, Vector3.UnitZ) : Vector3.UnitZ;
            var x = arguments.Count > 3 && ParseReference(arguments[3]) is { } xId && directions.TryGetValue(xId, out var xv)
                ? xv : Vector3.UnitX;
            x -= z * Vector3.Dot(x, z);
            x = Normalize(x, Math.Abs(z.X) < 0.8f ? Vector3.UnitX : Vector3.UnitY);
            var y = Normalize(Vector3.Cross(z, x), Vector3.UnitY);
            result[pair.Key] = new Placement(center, x, y, z);
        }
        return result;
    }

    private static List<Triangle> ReadTessellation(Dictionary<int, Entity> entities)
    {
        var coordinateLists = new Dictionary<int, List<Vector3>>();
        foreach (var pair in entities)
        {
            if (pair.Value.Type != "COORDINATES_LIST") continue;
            var arguments = SplitArguments(pair.Value.Body);
            if (arguments.Count < 3) continue;
            var coordinates = new List<Vector3>();
            foreach (Match match in VectorRegex.Matches(arguments[2]))
                coordinates.Add(new Vector3(ParseFloat(match.Groups["x"].Value), ParseFloat(match.Groups["y"].Value),
                    ParseFloat(match.Groups["z"].Value)));
            if (coordinates.Count > 0) coordinateLists[pair.Key] = coordinates;
        }

        var output = new List<Triangle>();
        foreach (var entity in entities.Values)
        {
            if (entity.Type is not ("TRIANGULATED_FACE" or "TRIANGULATED_SURFACE_SET")) continue;
            var arguments = SplitArguments(entity.Body);
            if (arguments.Count < 6 || ParseReference(arguments[1]) is not { } coordinatesId ||
                !coordinateLists.TryGetValue(coordinatesId, out var coordinates)) continue;
            var pointIndex = ParseIntegers(arguments[^2]);
            foreach (Match match in TriangleRegex.Matches(arguments[^1]))
            {
                var a = MapIndex(int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture), pointIndex);
                var b = MapIndex(int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture), pointIndex);
                var c = MapIndex(int.Parse(match.Groups["c"].Value, CultureInfo.InvariantCulture), pointIndex);
                if (a < 0 || b < 0 || c < 0 || a >= coordinates.Count || b >= coordinates.Count || c >= coordinates.Count)
                    continue;
                output.Add(MeshFactory.Triangle(coordinates[a], coordinates[b], coordinates[c]));
                if (output.Count >= MaxTriangles) return output;
            }
        }
        return output;
    }

    private static void ReadFacetedLoops(Dictionary<int, Entity> entities, Dictionary<int, Vector3> points,
        List<Triangle> output)
    {
        foreach (var entity in entities.Values)
        {
            if (entity.Type != "POLY_LOOP") continue;
            var arguments = SplitArguments(entity.Body);
            if (arguments.Count < 2) continue;
            var vertices = ParseReferences(arguments[1]).Where(points.ContainsKey).Select(id => points[id]).ToList();
            if (vertices.Count > 2 && Vector3.DistanceSquared(vertices[0], vertices[^1]) < 1e-12f) vertices.RemoveAt(vertices.Count - 1);
            for (var index = 1; index + 1 < vertices.Count && output.Count < MaxTriangles; index++)
                output.Add(MeshFactory.Triangle(vertices[0], vertices[index], vertices[index + 1]));
        }
    }

    private static List<LineSegment> ReadEdges(Dictionary<int, Entity> entities, Dictionary<int, Vector3> points,
        Dictionary<int, Vector3> vertices, Dictionary<int, Placement> placements)
    {
        var output = new List<LineSegment>();
        foreach (var entity in entities.Values)
        {
            if (entity.Type != "EDGE_CURVE") continue;
            var arguments = SplitArguments(entity.Body);
            if (arguments.Count < 5 || ParseReference(arguments[1]) is not { } startId ||
                ParseReference(arguments[2]) is not { } endId || ParseReference(arguments[3]) is not { } curveId ||
                !vertices.TryGetValue(startId, out var start) || !vertices.TryGetValue(endId, out var end)) continue;
            var sameSense = !arguments[4].Contains(".F.", StringComparison.OrdinalIgnoreCase);
            var curve = ResolveCurve(curveId, entities);
            var curvePoints = curve is null
                ? new List<Vector3> { start, end }
                : SampleCurve(curve.Value, start, end, sameSense, entities, points, placements);
            if (curvePoints.Count < 2) curvePoints = new List<Vector3> { start, end };
            curvePoints[0] = start;
            curvePoints[^1] = end;
            AddSegments(output, curvePoints);
            if (output.Count >= MaxSegments) break;
        }
        return output;
    }

    private static (int Id, Entity Entity)? ResolveCurve(int id, Dictionary<int, Entity> entities, int depth = 0)
    {
        if (depth > 4 || !entities.TryGetValue(id, out var entity)) return null;
        if (entity.Type is "SURFACE_CURVE" or "TRIMMED_CURVE")
        {
            var arguments = SplitArguments(entity.Body);
            if (arguments.Count > 1 && ParseReference(arguments[1]) is { } nested)
                return ResolveCurve(nested, entities, depth + 1);
        }
        return (id, entity);
    }

    private static List<Vector3> SampleCurve((int Id, Entity Entity) curve, Vector3 start, Vector3 end, bool sameSense,
        Dictionary<int, Entity> entities, Dictionary<int, Vector3> points, Dictionary<int, Placement> placements)
    {
        var arguments = SplitArguments(curve.Entity.Body);
        switch (curve.Entity.Type)
        {
            case "CIRCLE" when arguments.Count >= 3 && ParseReference(arguments[1]) is { } circlePlacement &&
                                    placements.TryGetValue(circlePlacement, out var circleAxis):
                return SampleConic(circleAxis, ParseFloat(arguments[2]), ParseFloat(arguments[2]), start, end, sameSense);
            case "ELLIPSE" when arguments.Count >= 4 && ParseReference(arguments[1]) is { } ellipsePlacement &&
                                     placements.TryGetValue(ellipsePlacement, out var ellipseAxis):
                return SampleConic(ellipseAxis, ParseFloat(arguments[2]), ParseFloat(arguments[3]), start, end, sameSense);
            case "POLYLINE" when arguments.Count >= 2:
                return ParseReferences(arguments[1]).Where(points.ContainsKey).Select(id => points[id]).ToList();
            case "B_SPLINE_CURVE_WITH_KNOTS":
                return SampleBSpline(arguments, points, start, end);
            default:
                return new List<Vector3> { start, end };
        }
    }

    private static List<Vector3> SampleConic(Placement placement, float radiusX, float radiusY,
        Vector3 start, Vector3 end, bool sameSense)
    {
        if (!float.IsFinite(radiusX) || !float.IsFinite(radiusY) || radiusX <= 0 || radiusY <= 0)
            return new List<Vector3> { start, end };
        var startAngle = ConicAngle(placement, start, radiusX, radiusY);
        var endAngle = ConicAngle(placement, end, radiusX, radiusY);
        var closed = Vector3.DistanceSquared(start, end) <= Math.Max(radiusX, radiusY) * Math.Max(radiusX, radiusY) * 1e-10f;
        double sweep;
        if (closed) sweep = sameSense ? Math.Tau : -Math.Tau;
        else
        {
            sweep = endAngle - startAngle;
            if (sameSense) while (sweep <= 0) sweep += Math.Tau;
            else while (sweep >= 0) sweep -= Math.Tau;
        }
        var samples = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / Math.Tau * 40), 6, 40);
        var result = new List<Vector3>(samples + 1);
        for (var index = 0; index <= samples; index++)
        {
            var angle = startAngle + sweep * index / samples;
            result.Add(placement.Center + placement.X * (float)(Math.Cos(angle) * radiusX) +
                       placement.Y * (float)(Math.Sin(angle) * radiusY));
        }
        return result;
    }

    private static double ConicAngle(Placement placement, Vector3 point, float radiusX, float radiusY)
    {
        var offset = point - placement.Center;
        return Math.Atan2(Vector3.Dot(offset, placement.Y) / radiusY, Vector3.Dot(offset, placement.X) / radiusX);
    }

    private static List<Vector3> SampleBSpline(IReadOnlyList<string> arguments, Dictionary<int, Vector3> points,
        Vector3 start, Vector3 end)
    {
        if (arguments.Count < 8 || !int.TryParse(arguments[1].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var degree)) return new List<Vector3> { start, end };
        var controls = ParseReferences(arguments[2]).Where(points.ContainsKey).Select(id => points[id]).ToList();
        var multiplicities = ParseIntegers(arguments[6]);
        var distinctKnots = ParseNumbers(arguments[7]);
        if (degree < 1 || degree > 8 || controls.Count <= degree || multiplicities.Count != distinctKnots.Count)
            return controls.Count >= 2 ? controls : new List<Vector3> { start, end };
        var knots = new List<double>();
        for (var index = 0; index < multiplicities.Count; index++)
            for (var repeat = 0; repeat < multiplicities[index] && knots.Count <= controls.Count + degree + 1; repeat++)
                knots.Add(distinctKnots[index]);
        var n = controls.Count - 1;
        if (knots.Count != n + degree + 2) return controls;
        var first = knots[degree];
        var last = knots[n + 1];
        if (!double.IsFinite(first) || !double.IsFinite(last) || last <= first) return controls;
        var sampleCount = Math.Clamp(controls.Count * 3, 12, MaxCurveSamples);
        var result = new List<Vector3>(sampleCount + 1);
        for (var index = 0; index <= sampleCount; index++)
            result.Add(EvaluateBSpline(controls, knots, degree, first + (last - first) * index / sampleCount));
        result[0] = start;
        result[^1] = end;
        return result;
    }

    private static Vector3 EvaluateBSpline(IReadOnlyList<Vector3> controls, IReadOnlyList<double> knots, int degree, double parameter)
    {
        var n = controls.Count - 1;
        var span = n;
        if (parameter < knots[n + 1])
            for (var index = degree; index <= n; index++)
                if (parameter >= knots[index] && parameter < knots[index + 1]) { span = index; break; }
        var work = new Vector3[degree + 1];
        for (var index = 0; index <= degree; index++) work[index] = controls[span - degree + index];
        for (var level = 1; level <= degree; level++)
        for (var index = degree; index >= level; index--)
        {
            var knotIndex = span - degree + index;
            var denominator = knots[knotIndex + degree - level + 1] - knots[knotIndex];
            var alpha = Math.Abs(denominator) < 1e-15 ? 0 : (parameter - knots[knotIndex]) / denominator;
            work[index] = Vector3.Lerp(work[index - 1], work[index], (float)Math.Clamp(alpha, 0, 1));
        }
        return work[degree];
    }

    private static void AddSegments(List<LineSegment> output, IReadOnlyList<Vector3> points)
    {
        for (var index = 0; index + 1 < points.Count && output.Count < MaxSegments; index++)
            if (IsFinite(points[index]) && IsFinite(points[index + 1]) &&
                Vector3.DistanceSquared(points[index], points[index + 1]) > 1e-12f)
                output.Add(new LineSegment(points[index], points[index + 1]));
    }

    private static List<string> SplitArguments(string body)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quoted = false;
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character == '\'')
            {
                if (quoted && index + 1 < body.Length && body[index + 1] == '\'') { index++; continue; }
                quoted = !quoted;
            }
            else if (!quoted && character == '(') depth++;
            else if (!quoted && character == ')') depth--;
            else if (!quoted && depth == 0 && character == ',')
            {
                result.Add(body[start..index].Trim());
                start = index + 1;
            }
        }
        result.Add(body[start..].Trim());
        return result;
    }

    private static int? ParseReference(string value)
    {
        var marker = value.IndexOf('#');
        if (marker < 0) return null;
        var end = marker + 1;
        while (end < value.Length && char.IsDigit(value[end])) end++;
        return int.TryParse(value.AsSpan(marker + 1, end - marker - 1), out var id) ? id : null;
    }

    private static List<int> ParseReferences(string value)
    {
        var result = new List<int>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '#') continue;
            var end = index + 1;
            while (end < value.Length && char.IsDigit(value[end])) end++;
            if (int.TryParse(value.AsSpan(index + 1, end - index - 1), out var id)) result.Add(id);
            index = end - 1;
        }
        return result;
    }

    private static List<double> ParseNumbers(string value) => NumberRegex.Matches(value)
        .Select(match => double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();

    private static List<int> ParseIntegers(string value) => NumberRegex.Matches(value)
        .Select(match => int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : -1)
        .Where(number => number >= 0).ToList();

    private static int MapIndex(int index, IReadOnlyList<int> pointIndex)
    {
        if (index <= 0) return -1;
        var mapped = pointIndex.Count == 0 ? index : index <= pointIndex.Count ? pointIndex[index - 1] : -1;
        return mapped - 1;
    }

    private static float ParseFloat(string value) => float.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static Vector3 Normalize(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;
    private static void SkipWhitespace(string text, ref int index) { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
}
