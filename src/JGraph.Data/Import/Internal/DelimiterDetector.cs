namespace JGraph.Data.Import.Internal;

/// <summary>
/// Guesses the field delimiter of a delimited-text document by tokenizing a sample with each candidate
/// (comma, semicolon, tab, pipe) and choosing the one that splits the sample into more than one field
/// with the most consistent field count. Returns <c>'\0'</c> when nothing splits (a single-column file).
/// </summary>
internal static class DelimiterDetector
{
    private static readonly char[] Candidates = { ',', ';', '\t', '|' };

    private const int SampleSize = 50;

    public static char Detect(string text)
    {
        char best = '\0';
        double bestScore = double.NegativeInfinity;

        foreach (char candidate in Candidates)
        {
            List<string?[]> records = Rfc4180Tokenizer.Tokenize(text, candidate);
            int sample = System.Math.Min(records.Count, SampleSize);
            if (sample == 0)
            {
                continue;
            }

            double sum = 0;
            for (int i = 0; i < sample; i++)
            {
                sum += records[i].Length;
            }

            double mean = sum / sample;
            if (mean <= 1.0)
            {
                continue; // this candidate does not split the rows
            }

            double variance = 0;
            for (int i = 0; i < sample; i++)
            {
                double d = records[i].Length - mean;
                variance += d * d;
            }

            variance /= sample;

            // Prefer the lowest field-count variance; iterating candidates in order makes ties resolve
            // to the earlier candidate because a later equal score does not exceed the incumbent.
            double score = -variance;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}
