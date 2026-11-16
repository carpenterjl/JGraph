namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>datestr</c>'s format letters as R2025b's <c>cnv2icudf.m</c> reads them (open item 347, ADR 0384;
/// probe p8d2 s1 to s38): <c>Y</c>, <c>D</c>, <c>f</c>, <c>h</c>, <c>q</c> and <c>s</c> are the letters
/// <c>y</c>, <c>d</c>, <c>F</c>, <c>H</c>, <c>Q</c> and <c>S</c> (so <c>YYYY</c> is the year, where it
/// was literal text here), and a field of the wrong count is <c>formatdate</c>'s refusal naming the
/// format as written. An AM or PM marker is taken out first, as there.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The format in this formatter's letters, or R2025b's refusal of it (MATLAB dialect).</summary>
    private static string CanonicalDatestrFormat(string format, int line, int col)
    {
        char[] letters = format.ToCharArray();
        var marker = new bool[letters.Length];
        int markers = 0;
        for (int i = 0; i + 1 < letters.Length; i++)
        {
            if (char.ToLowerInvariant(letters[i]) is 'a' or 'p' && char.ToLowerInvariant(letters[i + 1]) == 'm' && !marker[i])
            {
                marker[i] = marker[i + 1] = true;
                markers++;
                i++;
            }
        }

        JgsRuntimeException Refused(string what) =>
            new(line, col, $"MATLAB:formatdate:{what}Format", $"Unrecognized {(what == "ampm" ? "AM/PM" : what)} format. Format: {format}.");

        if (markers > 1)
        {
            throw Refused("ampm");
        }

        for (int i = 0; i < letters.Length; i++)
        {
            if (!marker[i])
            {
                letters[i] = letters[i] switch { 'Y' => 'y', 'D' => 'd', 'f' => 'F', 'h' => 'H', 'q' => 'Q', 's' => 'S', char other => other };
            }
        }

        // Where a letter stands, outside the marker: its count and whether its run is unbroken.
        (int Count, bool Unbroken) Run(char letter)
        {
            int first = -1;
            int last = -1;
            int count = 0;
            for (int i = 0; i < letters.Length; i++)
            {
                if (!marker[i] && letters[i] == letter)
                {
                    first = first < 0 ? i : first;
                    last = i;
                    count++;
                }
            }

            return (count, count == 0 || last - first == count - 1);
        }

        (int years, bool yearsUnbroken) = Run('y');
        if (years > 0 && (years is not (2 or 4) || !yearsUnbroken))
        {
            throw Refused("year");
        }

        (int quarters, bool quartersUnbroken) = Run('Q');
        if (quarters > 0)
        {
            if (quarters != 2 || !quartersUnbroken)
            {
                throw Refused("quarter");
            }

            if (markers > 0 || "mdHMSF".Any(c => Run(c).Count > 0))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:formatdate:quarterFormatMismatch",
                    $"Only the year format may be used with the quarter format. Format: {format}.");
            }
        }

        (int months, bool monthsUnbroken) = Run('m');
        if (months > 4 || !monthsUnbroken)
        {
            throw Refused("month");
        }

        foreach ((char letter, int count, string what) in new[] { ('H', 2, "hour"), ('M', 2, "minute"), ('S', 2, "second"), ('F', 3, "millisecond") })
        {
            (int found, bool unbroken) = Run(letter);
            if (found > 0 && (found != count || !unbroken))
            {
                throw Refused(what);
            }
        }

        return new string(letters);
    }
}
