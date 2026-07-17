using System.Globalization;
using System.Text;
using JGraph.Data.Import.Internal;

namespace JGraph.Data.Import;

/// <summary>
/// Reads a <see cref="Table"/> from delimited text (CSV/TSV and friends). The delimiter, header row,
/// and number culture are auto-detected unless overridden by <see cref="ImportOptions"/>. The reader is
/// BOM-aware and understands RFC 4180 quoting.
/// </summary>
public static class DelimitedTextReader
{
    private const int CultureSampleRows = 200;

    private static readonly CultureInfo CommaDecimalCulture = CreateCommaDecimalCulture();

    /// <summary>Reads and parses a delimited-text file.</summary>
    public static ImportResult Read(string path, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new ImportException($"File not found: {path}");
        }

        string text;
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            throw new ImportException($"Could not read '{path}': {ex.Message}", ex);
        }

        return ParseCore(text, options);
    }

    /// <summary>Reads and parses delimited text from a reader.</summary>
    public static ImportResult Read(TextReader reader, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return ParseCore(reader.ReadToEnd(), options);
    }

    /// <summary>Parses delimited text held in a string.</summary>
    public static ImportResult Parse(string text, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseCore(text, options);
    }

    private static ImportResult ParseCore(string text, ImportOptions? options)
    {
        options ??= new ImportOptions();

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        if (options.SkipRows > 0)
        {
            text = SkipLeadingLines(text, options.SkipRows);
        }

        if (text.Length == 0)
        {
            throw new ImportException("The input is empty.");
        }

        char delimiter = options.Delimiter ?? DelimiterDetector.Detect(text);
        List<string?[]> records = Rfc4180Tokenizer.Tokenize(text, delimiter);
        if (records.Count == 0)
        {
            throw new ImportException("No rows were found to import.");
        }

        CultureInfo culture = options.Culture ?? DetectCulture(records, delimiter);
        (Table table, bool hasHeader, List<string> warnings) = TableBuilder.Build(records, options.HasHeader, culture);

        return new ImportResult(table, delimiter, hasHeader, culture, warnings);
    }

    private static string SkipLeadingLines(string text, int count)
    {
        int i = 0;
        int removed = 0;
        int n = text.Length;
        while (i < n && removed < count)
        {
            char c = text[i];
            if (c == '\r')
            {
                i++;
                if (i < n && text[i] == '\n')
                {
                    i++;
                }

                removed++;
            }
            else if (c == '\n')
            {
                i++;
                removed++;
            }
            else
            {
                i++;
            }
        }

        return text[i..];
    }

    private static CultureInfo DetectCulture(List<string?[]> records, char delimiter)
    {
        // When comma is the field delimiter a comma cannot also be a decimal mark, so only invariant applies.
        if (delimiter == ',')
        {
            return CultureInfo.InvariantCulture;
        }

        int invariantHits = CountNumericCells(records, CultureInfo.InvariantCulture);
        int commaHits = CountNumericCells(records, CommaDecimalCulture);
        return commaHits > invariantHits ? CommaDecimalCulture : CultureInfo.InvariantCulture;
    }

    private static int CountNumericCells(List<string?[]> records, CultureInfo culture)
    {
        int sample = System.Math.Min(records.Count, CultureSampleRows);
        int count = 0;
        for (int r = 0; r < sample; r++)
        {
            foreach (string? cell in records[r])
            {
                if (!string.IsNullOrEmpty(cell) && ColumnTypeInference.TryParseNumber(cell, culture, out _))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static CultureInfo CreateCommaDecimalCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return CultureInfo.ReadOnly(culture);
    }
}
