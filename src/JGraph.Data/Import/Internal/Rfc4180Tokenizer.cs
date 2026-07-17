using System.Text;

namespace JGraph.Data.Import.Internal;

/// <summary>
/// A small RFC 4180 tokenizer: splits delimited text into records of fields, honouring quoted fields
/// (<c>"…"</c>), the doubled-quote escape (<c>""</c>), and delimiters/line breaks that appear inside
/// quotes. Line breaks may be <c>\r\n</c>, <c>\n</c>, or <c>\r</c>. Fully blank lines are dropped.
/// </summary>
internal static class Rfc4180Tokenizer
{
    public static List<string?[]> Tokenize(string text, char delimiter)
    {
        var records = new List<string?[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == delimiter && delimiter != '\0')
            {
                fields.Add(field.ToString());
                field.Clear();
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                AddRecord(records, fields);
                fields.Clear();

                if (c == '\r' && i + 1 < n && text[i + 1] == '\n')
                {
                    i += 2;
                }
                else
                {
                    i++;
                }

                continue;
            }

            field.Append(c);
            i++;
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            AddRecord(records, fields);
        }

        return records;
    }

    private static void AddRecord(List<string?[]> records, List<string> fields)
    {
        // A blank line tokenizes to a single empty field; drop it.
        if (fields.Count == 1 && fields[0].Length == 0)
        {
            return;
        }

        records.Add(fields.ToArray());
    }
}
