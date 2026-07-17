using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace JGraph.Tests.DataImport;

/// <summary>The kind of value a fixture cell holds.</summary>
internal enum XKind { Empty, Number, Text, Inline, Date, DateCustom, Bool, Error }

/// <summary>One cell of a fixture worksheet.</summary>
internal readonly record struct XCell(XKind Kind, double Num = 0, string? Str = null, DateTime When = default, bool Flag = false)
{
    public static readonly XCell Empty = new(XKind.Empty);

    public static XCell Number(double value) => new(XKind.Number, Num: value);

    public static XCell Text(string value) => new(XKind.Text, Str: value);

    public static XCell Inline(string value) => new(XKind.Inline, Str: value);

    /// <summary>A date-formatted cell using the builtin format 14.</summary>
    public static XCell Date(DateTime value) => new(XKind.Date, When: value);

    /// <summary>A date-formatted cell using a custom "yyyy-mm-dd" number format.</summary>
    public static XCell DateCustom(DateTime value) => new(XKind.DateCustom, When: value);

    public static XCell Bool(bool value) => new(XKind.Bool, Flag: value);

    public static XCell Error(string code) => new(XKind.Error, Str: code);
}

/// <summary>
/// Builds a minimal but valid <c>.xlsx</c> workbook in memory (via <see cref="ZipArchive"/>) for the
/// xlsx-reader tests. Supports the cell kinds the reader understands: shared/inline strings, numbers,
/// date-formatted numbers (builtin and custom formats), booleans, and errors.
/// </summary>
internal sealed class XlsxFixture
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly List<(string Name, XCell[][] Rows)> _sheets = new();
    private readonly List<string> _shared = new();
    private readonly Dictionary<string, int> _sharedIndex = new(StringComparer.Ordinal);

    public XlsxFixture Sheet(string name, params XCell[][] rows)
    {
        _sheets.Add((name, rows));
        return this;
    }

    public MemoryStream BuildStream()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Build sheet XML first so shared strings are populated before that part is written.
            var sheetXml = new List<string>();
            foreach ((string _, XCell[][] rows) in _sheets)
            {
                sheetXml.Add(BuildSheetXml(rows));
            }

            WriteEntry(zip, "[Content_Types].xml", BuildContentTypes());
            WriteEntry(zip, "_rels/.rels", BuildRootRels());
            WriteEntry(zip, "xl/workbook.xml", BuildWorkbook());
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());
            WriteEntry(zip, "xl/styles.xml", BuildStyles());
            WriteEntry(zip, "xl/sharedStrings.xml", BuildSharedStrings());
            for (int i = 0; i < sheetXml.Count; i++)
            {
                WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", sheetXml[i]);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private int SharedIndex(string value)
    {
        if (!_sharedIndex.TryGetValue(value, out int index))
        {
            index = _shared.Count;
            _shared.Add(value);
            _sharedIndex[value] = index;
        }

        return index;
    }

    private string BuildSheetXml(XCell[][] rows)
    {
        var sb = new StringBuilder();
        sb.Append($"<worksheet xmlns=\"{Main}\"><sheetData>");
        for (int r = 0; r < rows.Length; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            XCell[] row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                AppendCell(sb, row[c], ColumnLetter(c) + (r + 1));
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private void AppendCell(StringBuilder sb, XCell cell, string reference)
    {
        switch (cell.Kind)
        {
            case XKind.Empty:
                break;
            case XKind.Number:
                sb.Append($"<c r=\"{reference}\"><v>{cell.Num.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                break;
            case XKind.Text:
                sb.Append($"<c r=\"{reference}\" t=\"s\"><v>{SharedIndex(cell.Str!)}</v></c>");
                break;
            case XKind.Inline:
                sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{Escape(cell.Str!)}</t></is></c>");
                break;
            case XKind.Date:
                sb.Append($"<c r=\"{reference}\" s=\"1\"><v>{cell.When.ToOADate().ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                break;
            case XKind.DateCustom:
                sb.Append($"<c r=\"{reference}\" s=\"2\"><v>{cell.When.ToOADate().ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                break;
            case XKind.Bool:
                sb.Append($"<c r=\"{reference}\" t=\"b\"><v>{(cell.Flag ? 1 : 0)}</v></c>");
                break;
            case XKind.Error:
                sb.Append($"<c r=\"{reference}\" t=\"e\"><v>{Escape(cell.Str!)}</v></c>");
                break;
        }
    }

    private string BuildSharedStrings()
    {
        var sb = new StringBuilder();
        sb.Append($"<sst xmlns=\"{Main}\">");
        foreach (string value in _shared)
        {
            // Split into two rich-text runs to exercise run concatenation.
            if (value.Length > 1)
            {
                int mid = value.Length / 2;
                sb.Append($"<si><r><t xml:space=\"preserve\">{Escape(value[..mid])}</t></r><r><t xml:space=\"preserve\">{Escape(value[mid..])}</t></r></si>");
            }
            else
            {
                sb.Append($"<si><t>{Escape(value)}</t></si>");
            }
        }

        sb.Append("</sst>");
        return sb.ToString();
    }

    private static string BuildStyles() =>
        $"<styleSheet xmlns=\"{Main}\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/></numFmts>" +
        "<cellXfs count=\"3\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"164\"/></cellXfs>" +
        "</styleSheet>";

    private string BuildWorkbook()
    {
        var sb = new StringBuilder();
        sb.Append($"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets>");
        for (int i = 0; i < _sheets.Count; i++)
        {
            sb.Append($"<sheet name=\"{Escape(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        }

        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private string BuildWorkbookRels()
    {
        var sb = new StringBuilder();
        sb.Append($"<Relationships xmlns=\"{PackageRel}\">");
        for (int i = 0; i < _sheets.Count; i++)
        {
            sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"{Rel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        }

        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildContentTypes() =>
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "</Types>";

    private static string BuildRootRels() =>
        $"<Relationships xmlns=\"{PackageRel}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{Rel}/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            int remainder = (index - 1) % 26;
            sb.Insert(0, (char)('A' + remainder));
            index = (index - 1) / 26;
        }

        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
