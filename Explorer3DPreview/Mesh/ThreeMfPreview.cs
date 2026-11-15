using System.IO.Compression;
using System.Xml.Linq;

namespace Explorer3DPreview.Mesh;

/// <summary>
/// Finds the preview image a slicer stored inside a 3MF package (PrusaSlicer, OrcaSlicer, Bambu Studio, Cura...).
/// No System.Drawing here: it only returns the PNG/JPEG bytes.
/// </summary>
internal static class ThreeMfPreview
{
    private const long MaxImageBytes = 24L * 1024 * 1024;
    private const long MaxRelationshipsBytes = 1024 * 1024;

    // Used when the package has no thumbnail relationship, in order of preference.
    private static readonly string[] FallbackNames =
    {
        "Metadata/thumbnail.png",
        "Metadata/thumbnail.jpg",
        "Metadata/plate_1.png",
        "Thumbnails/thumbnail.png",
    };

    internal static byte[]? TryReadBytes(Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var name in CandidateNames(archive))
            {
                var entry = archive.Entries.FirstOrDefault(item =>
                    string.Equals(item.FullName, name, StringComparison.OrdinalIgnoreCase));
                if (entry is null || entry.Length <= 0 || entry.Length > MaxImageBytes) continue;
                var bytes = ReadAll(entry, (int)entry.Length);
                if (LooksLikeImage(bytes)) return bytes;
            }
        }
        catch (InvalidDataException) { }
        catch (IOException) { }
        return null;
    }

    private static IEnumerable<string> CandidateNames(ZipArchive archive)
    {
        // 1) The package's own pointer: a relationship of type ".../metadata/thumbnail" in _rels/.rels.
        var rels = archive.Entries.FirstOrDefault(item =>
            string.Equals(item.FullName, "_rels/.rels", StringComparison.OrdinalIgnoreCase));
        if (rels is not null && rels.Length > 0 && rels.Length <= MaxRelationshipsBytes)
        {
            string? target = null;
            try
            {
                using var relsStream = rels.Open();
                var document = XDocument.Load(relsStream, LoadOptions.None);
                target = document.Descendants()
                    .Where(node => node.Name.LocalName == "Relationship" &&
                                   (node.Attribute("Type")?.Value ?? string.Empty)
                                   .EndsWith("/metadata/thumbnail", StringComparison.OrdinalIgnoreCase))
                    .Select(node => node.Attribute("Target")?.Value)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
            catch (System.Xml.XmlException) { }
            if (target is not null) yield return Uri.UnescapeDataString(target.TrimStart('/'));
        }

        // 2) Well-known locations.
        foreach (var name in FallbackNames) yield return name;
    }

    private static byte[] ReadAll(ZipArchiveEntry entry, int length)
    {
        var buffer = new byte[length];
        using var source = entry.Open();
        var total = 0;
        while (total < length)
        {
            var read = source.Read(buffer, total, length - total);
            if (read <= 0) break;
            total += read;
        }
        return total == length ? buffer : buffer[..total];
    }

    private static bool LooksLikeImage(byte[] bytes) =>
        (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
        (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8);
}
