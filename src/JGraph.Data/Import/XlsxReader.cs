using System.Globalization;
using System.Xml.Linq;
using JGraph.Data.Import.Internal;

namespace JGraph.Data.Import;

/// <summary>
/// Reads a <see cref="Table"/> from a worksheet of an <c>.xlsx</c> workbook. The reader supports the
/// common cell kinds (shared/inline/formula strings, numbers, booleans, and date-formatted numbers) and
/// uses each cell's cached value — it does not evaluate formulas, apply styles beyond date detection, or
/// resolve merged cells. Date cells are recognised by their number format and converted to the
/// framework's date/time convention.
/// </summary>
public static class XlsxReader
{
    /// <summary>Reads a table from an <c>.xlsx</c> file.</summary>
    public static ImportResult Read(string path, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new ImportException($"File not found: {path}");
        }

        using FileStream stream = OpenRead(path);
        return Read(stream, options);
    }

    /// <summary>Reads a table from an <c>.xlsx</c> stream.</summary>
    public static ImportResult Read(Stream stream, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new ImportOptions();

        using var package = new XlsxPackage(stream);
        SheetInfo sheet = SelectSheet(package, options.SheetName);

        List<string?[]> records = ReadSheetRecords(package, sheet, out List<string> warnings);
        if (options.SkipRows > 0)
        {
            records = records.Skip(options.SkipRows).ToList();
        }

        if (records.Count == 0)
        {
            throw new ImportException($"Worksheet '{sheet.Name}' has no rows to import.");
        }

        CultureInfo culture = options.Culture ?? CultureInfo.InvariantCulture;
        (Table table, bool hasHeader, List<string> buildWarnings) = TableBuilder.Build(records, options.HasHeader, culture);
        warnings.AddRange(buildWarnings);

        return new ImportResult(table, '\0', hasHeader, culture, warnings);
    }

    /// <summary>Returns the worksheet names in workbook order.</summary>
    public static IReadOnlyList<string> GetSheetNames(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new ImportException($"File not found: {path}");
        }

        using FileStream stream = OpenRead(path);
        using var package = new XlsxPackage(stream);
        return package.Sheets.Select(s => s.Name).ToList();
    }

    private static FileStream OpenRead(string path)
    {
        try
        {
            return File.OpenRead(path);
        }
        catch (IOException ex)
        {
            throw new ImportException($"Could not read '{path}': {ex.Message}", ex);
        }
    }

    private static SheetInfo SelectSheet(XlsxPackage package, string? name)
    {
        if (package.Sheets.Count == 0)
        {
            throw new ImportException("The workbook contains no worksheets.");
        }

        if (name is null)
        {
            return package.Sheets[0];
        }

        foreach (SheetInfo sheet in package.Sheets)
        {
            if (string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return sheet;
            }
        }

        throw new ImportException(
            $"Worksheet '{name}' not found. Available worksheets: {string.Join(", ", package.Sheets.Select(s => s.Name))}.");
    }

    private static List<string?[]> ReadSheetRecords(XlsxPackage package, SheetInfo sheet, out List<string> warnings)
    {
        warnings = new List<string>();
        XDocument doc = package.LoadXml(sheet.PartPath)
            ?? throw new ImportException($"Worksheet part '{sheet.PartPath}' is missing from the workbook.");

        XElement? sheetData = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "sheetData");
        var rows = new List<List<string?>>();
        int width = 0;

        if (sheetData is not null)
        {
            foreach (XElement rowEl in sheetData.Elements().Where(e => e.Name.LocalName == "row"))
            {
                var cells = new List<string?>();
                foreach (XElement cell in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    int col = XlsxCellRef.ColumnIndex(cell.Attribute("r")?.Value ?? string.Empty);
                    if (col < 0)
                    {
                        col = cells.Count;
                    }

                    while (cells.Count <= col)
                    {
                        cells.Add(null);
                    }

                    cells[col] = DecodeCell(cell, package, warnings);
                }

                rows.Add(cells);
                width = System.Math.Max(width, cells.Count);
            }
        }

        // Pad every row to the sheet's used width so trailing empty cells don't read as ragged rows.
        var records = new List<string?[]>(rows.Count);
        foreach (List<string?> row in rows)
        {
            var record = new string?[width];
            for (int i = 0; i < row.Count; i++)
            {
                record[i] = row[i];
            }

            records.Add(record);
        }

        return records;
    }

    private static string? DecodeCell(XElement cell, XlsxPackage package, List<string> warnings)
    {
        string? type = cell.Attribute("t")?.Value;
        XElement? valueEl = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v");

        switch (type)
        {
            case "s":
                if (valueEl is null)
                {
                    return null;
                }

                int index = XlsxPackage.ParseInt(valueEl.Value);
                string[] shared = package.SharedStrings;
                return index >= 0 && index < shared.Length ? NullIfEmpty(shared[index]) : null;

            case "inlineStr":
                XElement? inline = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
                return inline is null ? null : NullIfEmpty(XlsxPackage.ConcatText(inline));

            case "str":
                return valueEl is null ? null : NullIfEmpty(valueEl.Value);

            case "b":
                return valueEl is null ? null : (valueEl.Value.Trim() == "1" ? "TRUE" : "FALSE");

            case "e":
                warnings.Add($"Cell {cell.Attribute("r")?.Value ?? "?"}: error value '{valueEl?.Value}' treated as missing.");
                return null;

            default:
                return DecodeNumber(cell, valueEl, package);
        }
    }

    private static string? DecodeNumber(XElement cell, XElement? valueEl, XlsxPackage package)
    {
        if (valueEl is null)
        {
            return null;
        }

        string raw = valueEl.Value;
        int styleIndex = XlsxPackage.ParseInt(cell.Attribute("s")?.Value);
        if (styleIndex >= 0
            && package.IsDateStyle(styleIndex)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial))
        {
            try
            {
                double oaDate = package.Date1904 ? serial + 1462.0 : serial;
                DateTime dateTime = DateTime.FromOADate(oaDate);
                return dateTime.ToString("o", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException)
            {
                // Serial out of the representable range — fall back to the raw number.
            }
        }

        return NullIfEmpty(raw);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
