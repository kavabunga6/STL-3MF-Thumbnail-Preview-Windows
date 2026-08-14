using System.Text;

namespace Explorer3DPreview.Mesh;

internal static class StreamFormatDetector
{
    internal static string Detect(Stream stream)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var prefix = new byte[(int)Math.Min(64 * 1024, stream.Length)];
            var total = 0;
            while (total < prefix.Length)
            {
                var read = stream.Read(prefix, total, prefix.Length - total);
                if (read <= 0) break;
                total += read;
            }
            var bytes = total == prefix.Length ? prefix : prefix[..total];
            if (bytes.Length >= 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' && bytes[2] == 3 && bytes[3] == 4)
                return ".3mf";

            if (stream.Length >= 84 && bytes.Length >= 84)
            {
                var triangleCount = BitConverter.ToUInt32(bytes, 80);
                if (84L + triangleCount * 50L == stream.Length) return ".stl";
            }

            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (text.StartsWith("ply", StringComparison.OrdinalIgnoreCase)) return ".ply";
            if (text.StartsWith("OFF", StringComparison.OrdinalIgnoreCase)) return ".off";
            if (text.Contains("<amf", StringComparison.OrdinalIgnoreCase)) return ".amf";
            if (text.StartsWith("solid", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("facet", StringComparison.OrdinalIgnoreCase)) return ".stl";

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimStart()).ToArray();
            if (lines.Any(line => line.StartsWith("v ", StringComparison.Ordinal)) &&
                lines.Any(line => line.StartsWith("f ", StringComparison.Ordinal))) return ".obj";
            if (lines.Any(line => line.StartsWith("G0", StringComparison.OrdinalIgnoreCase) ||
                                  line.StartsWith("G1", StringComparison.OrdinalIgnoreCase) ||
                                  line.StartsWith("M82", StringComparison.OrdinalIgnoreCase))) return ".gcode";
            return ".stl";
        }
        finally { stream.Position = originalPosition; }
    }
}
