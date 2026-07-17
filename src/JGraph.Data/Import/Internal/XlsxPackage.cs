using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JGraph.Data.Import.Internal;

/// <summary>
/// A worksheet within an <see cref="XlsxPackage"/>: its display name and the ZIP part path that holds
/// its XML.
/// </summary>
internal sealed record SheetInfo(string Name, string PartPath);

/// <summary>
/// Opens the ZIP container of an <c>.xlsx</c> workbook and exposes the parts the reader needs: the
/// ordered list of worksheets, the shared-string table, and which cell styles are date-formatted.
/// Parts are parsed with <see cref="XDocument"/> and matched by local name so namespaces are ignored.
/// </summary>
internal sealed class XlsxPackage : IDisposable
{
    private static readonly int[] BuiltinDateFormatIds = { 14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47 };

    private readonly ZipArchive _zip;
    private string[]? _sharedStrings;
    private HashSet<int>? _dateStyles;

    public XlsxPackage(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            _zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException ex)
        {
            throw new ImportException("The file is not a valid .xlsx workbook (not a ZIP archive).", ex);
        }

        try
        {
            XDocument workbook = LoadXml("xl/workbook.xml")
                ?? throw new ImportException("The workbook is missing xl/workbook.xml.");
            Date1904 = ReadDate1904(workbook);
            Sheets = ReadSheets(workbook, LoadXml("xl/_rels/workbook.xml.rels"));
        }
        catch (ImportException)
        {
            _zip.Dispose();
            throw;
        }
        catch (XmlException ex)
        {
            _zip.Dispose();
            throw new ImportException("The workbook's XML could not be parsed: " + ex.Message, ex);
        }
    }

    /// <summary>Whether the workbook uses the 1904 date system (serials offset by 1462 days).</summary>
    public bool Date1904 { get; }

    /// <summary>The worksheets, in workbook order.</summary>
    public IReadOnlyList<SheetInfo> Sheets { get; } = Array.Empty<SheetInfo>();

    /// <summary>The shared-string table (rich-text runs concatenated).</summary>
    public string[] SharedStrings => _sharedStrings ??= ReadSharedStrings();

    /// <summary>Whether the given cell-style index (<c>s</c> attribute) is date-formatted.</summary>
    public bool IsDateStyle(int styleIndex) => (_dateStyles ??= ReadDateStyles()).Contains(styleIndex);

    /// <summary>Loads and parses a ZIP part as XML, or returns null when the part is absent.</summary>
    public XDocument? LoadXml(string partPath)
    {
        if (string.IsNullOrEmpty(partPath))
        {
            return null;
        }

        ZipArchiveEntry? entry = _zip.GetEntry(partPath);
        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    public void Dispose() => _zip.Dispose();

    private static bool ReadDate1904(XDocument workbook)
    {
        XElement? pr = workbook.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "workbookPr");
        string? value = pr?.Attribute("date1904")?.Value;
        return value is "1" or "true";
    }

    private static IReadOnlyList<SheetInfo> ReadSheets(XDocument workbook, XDocument? rels)
    {
        var relMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rels?.Root is not null)
        {
            foreach (XElement rel in rels.Root.Elements().Where(e => e.Name.LocalName == "Relationship"))
            {
                string? id = rel.Attribute("Id")?.Value;
                string? target = rel.Attribute("Target")?.Value;
                if (!string.IsNullOrEmpty(id) && target is not null)
                {
                    relMap[id] = target;
                }
            }
        }

        var sheets = new List<SheetInfo>();
        XElement? sheetsEl = workbook.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "sheets");
        if (sheetsEl is not null)
        {
            foreach (XElement sheet in sheetsEl.Elements().Where(e => e.Name.LocalName == "sheet"))
            {
                string name = sheet.Attribute("name")?.Value ?? $"Sheet{sheets.Count + 1}";
                string? rid = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                string target = rid is not null && relMap.TryGetValue(rid, out string? t) ? t : string.Empty;
                sheets.Add(new SheetInfo(name, NormalizePart(target)));
            }
        }

        return sheets;
    }

    private static string NormalizePart(string target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return string.Empty;
        }

        target = target.Replace('\\', '/');
        return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
    }

    private string[] ReadSharedStrings()
    {
        XDocument? doc = LoadXml("xl/sharedStrings.xml");
        if (doc?.Root is null)
        {
            return Array.Empty<string>();
        }

        var strings = new List<string>();
        foreach (XElement si in doc.Root.Elements().Where(e => e.Name.LocalName == "si"))
        {
            strings.Add(ConcatText(si));
        }

        return strings.ToArray();
    }

    private HashSet<int> ReadDateStyles()
    {
        var styles = new HashSet<int>();
        XDocument? doc = LoadXml("xl/styles.xml");
        if (doc?.Root is null)
        {
            return styles;
        }

        var dateFormatIds = new HashSet<int>(BuiltinDateFormatIds);
        XElement? numFmts = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "numFmts");
        if (numFmts is not null)
        {
            foreach (XElement numFmt in numFmts.Elements().Where(e => e.Name.LocalName == "numFmt"))
            {
                int id = ParseInt(numFmt.Attribute("numFmtId")?.Value);
                string code = numFmt.Attribute("formatCode")?.Value ?? string.Empty;
                if (id >= 0 && IsDateFormatCode(code))
                {
                    dateFormatIds.Add(id);
                }
            }
        }

        XElement? cellXfs = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "cellXfs");
        if (cellXfs is not null)
        {
            int index = 0;
            foreach (XElement xf in cellXfs.Elements().Where(e => e.Name.LocalName == "xf"))
            {
                int numFmtId = ParseInt(xf.Attribute("numFmtId")?.Value);
                if (dateFormatIds.Contains(numFmtId))
                {
                    styles.Add(index);
                }

                index++;
            }
        }

        return styles;
    }

    /// <summary>Concatenates the text of every <c>t</c> descendant (handles rich-text runs).</summary>
    public static string ConcatText(XElement element)
    {
        var sb = new StringBuilder();
        foreach (XElement t in element.Descendants().Where(e => e.Name.LocalName == "t"))
        {
            sb.Append(t.Value);
        }

        return sb.ToString();
    }

    /// <summary>True when a number-format code contains an unquoted date/time token (y, m, d, h, s).</summary>
    private static bool IsDateFormatCode(string code)
    {
        bool inBracket = false;
        bool inQuote = false;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (inQuote)
            {
                if (c == '"')
                {
                    inQuote = false;
                }

                continue;
            }

            if (inBracket)
            {
                if (c == ']')
                {
                    inBracket = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuote = true;
                    break;
                case '[':
                    inBracket = true;
                    break;
                case '\\':
                    i++; // skip the escaped character
                    break;
                case 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S' or 'm' or 'M':
                    return true;
            }
        }

        return false;
    }

    public static int ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1;
}
