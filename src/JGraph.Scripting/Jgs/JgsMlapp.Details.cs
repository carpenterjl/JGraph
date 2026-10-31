using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using JGraph.Scripting.Designer;

namespace JGraph.Scripting.Jgs;

/// <content>
/// An app's details and thumbnail in its package (App Designer window plan W8a), as R2025b's <c>FileWriter.writeAppMetadata</c>
/// and <c>writeAppScreenshot</c> leave them (measured, ui-probes/w8/w8_details): each element in its place, the image as
/// <c>metadata/appScreenshot.&lt;format&gt;</c> under two relationships and its extension's content type.
/// </content>
public static partial class JgsMlapp
{
    private const string ScreenshotPrefix = "metadata/appScreenshot.";
    private const string ScreenshotRelationship = "http://schemas.mathworks.com/appDesigner/app/2017/relationships/appScreenshot";
    private const string ThumbnailRelationship = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";

    /// <summary>The package's thumbnail as a data URI (<c>AppThumbnailUtils.getThumbnailDataURI</c>); empty when it has none.</summary>
    private static string ReadScreenshot(ZipArchive package)
    {
        ZipArchiveEntry? entry = package.Entries.FirstOrDefault(static e => e.FullName.StartsWith(ScreenshotPrefix, StringComparison.Ordinal));
        if (entry is null)
        {
            return string.Empty;
        }

        using Stream stream = entry.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return DesignAppDetails.DataUri(bytes.ToArray(), entry.FullName[ScreenshotPrefix.Length..]);
    }

    /// <summary>The argument sets the package's model keeps (<c>loadApp</c>); null when it keeps none.</summary>
    /// <exception cref="InvalidDataException">The file is not a package.</exception>
    public static IReadOnlyList<string>? ReadRunConfigurations(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using ZipArchive package = ZipFile.OpenRead(path);
        if (package.GetEntry(ModelPart) is not { } entry)
        {
            return null;
        }

        using Stream stream = entry.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return JgsMlappModel.ReadRunConfigurations(bytes.ToArray());
    }

    /// <summary>The thumbnail Run took written into the package at once (<c>FileWriter.writeAppScreenshot</c>), every other part left as it is.</summary>
    /// <exception cref="IOException">The file could not be read or replaced.</exception>
    public static void WriteThumbnail(string path, string uri)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(uri);
        List<Part> parts;
        using (ZipArchive package = ZipFile.OpenRead(path))
        {
            parts = package.Entries.Select(Part.Of).ToList();
        }

        PutScreenshot(parts, uri);
        Replace(path, parts, null);
    }

    /// <summary>The details written into a package's parts, the rest of each part kept as its bytes; a package without those parts, one holding its code alone, has nowhere to keep them.</summary>
    private static void ApplyDetails(List<Part> parts, MlappMetadata details)
    {
        static string X(string value) => SecurityElement.Escape(value);
        if (parts.FirstOrDefault(static p => p.Name == "metadata/coreProperties.xml") is { } core)
        {
            string xml = Encoding.UTF8.GetString(core.Bytes);
            xml = SetElement(xml, "dc:creator", X(details.Author));
            xml = SetElement(xml, "dc:description", X(details.Summary));
            xml = SetElement(xml, "dc:title", X(details.Name));
            xml = SetElement(xml, "cp:version", X(DesignAppDetails.VersionForPackage(details.Version)));
            xml = SetElement(xml, "dcterms:modified", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), keepAttributes: true);
            core.Bytes = Encoding.UTF8.GetBytes(xml);
        }

        if (parts.FirstOrDefault(static p => p.Name == "metadata/appMetadata.xml") is { } app)
        {
            string xml = Encoding.UTF8.GetString(app.Bytes);
            xml = SetElement(xml, "description", X(details.Description));
            xml = SetElement(xml, "screenshotMode", X(details.ScreenshotMode));
            app.Bytes = Encoding.UTF8.GetBytes(xml);
            PutScreenshot(parts, details.Screenshot);
        }
    }

    /// <summary>An element's text put in place, whether the part has it empty or not; one the part lacks is added before its root's end.</summary>
    private static string SetElement(string xml, string name, string escaped, bool keepAttributes = false)
    {
        string pattern = $@"<{Regex.Escape(name)}(?<attributes>(\s[^<>]*?)?)(/>|>.*?</{Regex.Escape(name)}>)";
        Match match = Regex.Match(xml, pattern, RegexOptions.Singleline);
        if (match.Success)
        {
            string attributes = keepAttributes ? match.Groups["attributes"].Value : string.Empty;
            return string.Concat(xml.AsSpan(0, match.Index), $"<{name}{attributes}>{escaped}</{name}>", xml.AsSpan(match.Index + match.Length));
        }

        int end = xml.LastIndexOf("</", StringComparison.Ordinal);
        return end < 0 ? xml : xml.Insert(end, $"<{name}>{escaped}</{name}>");
    }

    /// <summary>The thumbnail put into the parts in place of the one they hold, or taken out with nothing to put; a data URI that is not one leaves them as they are.</summary>
    private static void PutScreenshot(List<Part> parts, string uri)
    {
        (byte[] Bytes, string Format)? image = uri.Length == 0 ? null : DesignAppDetails.OfDataUri(uri);
        if (uri.Length > 0 && image is null)
        {
            return;
        }

        Part? held = parts.FirstOrDefault(static p => p.Name.StartsWith(ScreenshotPrefix, StringComparison.Ordinal));
        if (image is null && held is null)
        {
            return;
        }

        string? target = image is { } put ? ScreenshotPrefix + put.Format : null;
        if (held is not null && held.Name == target)
        {
            held.Bytes = image!.Value.Bytes;
            return;
        }

        if (held is not null)
        {
            parts.Remove(held);
        }

        if (image is { } added)
        {
            parts.Add(new Part { Name = target!, Bytes = added.Bytes, Written = DateTimeOffset.Now, Stored = false, Attributes = 0 });
        }

        if (parts.FirstOrDefault(static p => p.Name == "_rels/.rels") is { } relationships)
        {
            string xml = Encoding.UTF8.GetString(relationships.Bytes);
            xml = Regex.Replace(xml, $@"\s*<Relationship\b[^>]*Type=""({Regex.Escape(ScreenshotRelationship)}|{Regex.Escape(ThumbnailRelationship)})""[^>]*/>", string.Empty);
            if (target is not null)
            {
                int next = Regex.Matches(xml, @"Id=""rId(\d+)""").Select(static m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
                string lines = $"  <Relationship Id=\"rId{next}\" Target=\"{target}\" Type=\"{ScreenshotRelationship}\"/>\n"
                    + $"  <Relationship Id=\"rId{next + 1}\" Target=\"{target}\" Type=\"{ThumbnailRelationship}\"/>\n";
                int end = xml.LastIndexOf("</Relationships>", StringComparison.Ordinal);
                xml = end < 0 ? xml : xml.Insert(end, lines);
            }

            relationships.Bytes = Encoding.UTF8.GetBytes(xml);
        }

        if (image is { } typed && parts.FirstOrDefault(static p => p.Name == "[Content_Types].xml") is { } types)
        {
            string xml = Encoding.UTF8.GetString(types.Bytes);
            if (!Regex.IsMatch(xml, $@"Extension=""{Regex.Escape(typed.Format)}""", RegexOptions.IgnoreCase))
            {
                string kind = typed.Format.ToLowerInvariant() is "jpg" or "jpeg" ? "image/jpeg" : "image/" + typed.Format.ToLowerInvariant();
                Match first = Regex.Match(xml, @"<Default\b");
                if (first.Success)
                {
                    xml = xml.Insert(first.Index, $"<Default ContentType=\"{kind}\" Extension=\"{typed.Format}\"/>\n  ");
                }
            }

            types.Bytes = Encoding.UTF8.GetBytes(xml);
        }
    }
}
