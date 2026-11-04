using System.Text.RegularExpressions;

namespace JGraph.Scripting.Editing;

/// <summary>What a row of a side-by-side comparison is.</summary>
public enum DiffRowKind
{
    /// <summary>The same line on both sides.</summary>
    Same,

    /// <summary>A line of the right text with no line of the left beside it.</summary>
    Inserted,

    /// <summary>A line of the left text with no line of the right beside it.</summary>
    Deleted,

    /// <summary>A line of each side, paired as one line changed into the other.</summary>
    Changed,
}

/// <summary>One row of a side-by-side comparison: the one-based line of each side, or 0 for no line there.</summary>
/// <param name="Kind">What the row is.</param>
/// <param name="Left">The left text's line, one-based; 0 for an inserted row.</param>
/// <param name="Right">The right text's line, one-based; 0 for a deleted row.</param>
public sealed record DiffRow(DiffRowKind Kind, int Left, int Right);

/// <summary>A difference: a run of rows that are not the same, which the Comparison Tool navigates as one.</summary>
/// <param name="FirstRow">The zero-based index of its first row.</param>
/// <param name="RowCount">How many rows it spans.</param>
public sealed record DiffHunk(int FirstRow, int RowCount)
{
    /// <summary>The zero-based index after its last row.</summary>
    public int EndRow => FirstRow + RowCount;
}

/// <summary>A piece of a changed line: its text, and whether it is among what changed.</summary>
/// <param name="Text">The characters.</param>
/// <param name="Changed">True for characters the other side does not have.</param>
public sealed record DiffSegment(string Text, bool Changed);

/// <summary>
/// The line comparison R2025b's Comparison Tool draws (App Designer window plan W14), measured through
/// <c>visdiff</c>'s reports over 60 pairs of texts and 48 small tie cases (probes <c>w14_compare</c> and
/// <c>w14_ties</c>, <c>CompareOracle.json</c> and <c>CompareTiesOracle.json</c>): the lines are matched by an
/// exact longest common subsequence (white space, case and a trailing line break all count), and inside each run
/// of unmatched lines a left line and a right line pair up as one changed line only where they share a word - an
/// identifier or a number - the rest of a run being its deleted lines, then its inserted lines.
/// </summary>
/// <remarks>
/// Where two matchings are equally long, R2025b's choice follows a two-ended search that meets in the middle:
/// a text that occurs whole in the other is matched where it first occurs, and otherwise the choice flips with the
/// parity of the difference in line counts - with an even difference the later left lines keep their match (a
/// swapped pair keeps the first line of the left in place, showing the other as inserted above and deleted
/// below), with an odd one the earlier right lines do (a line duplicated beside itself shows the second copy as
/// the new one). That is how <see cref="Compare(System.Collections.Generic.IReadOnlyList{string}, System.Collections.Generic.IReadOnlyList{string})"/> chooses, and it
/// reproduces 102 of the 108 measured pairs; the six it does not are ties deeper in R2025b's search (open item).
/// </remarks>
public static class TextDiff
{
    private static readonly Regex WordPattern = new("[A-Za-z0-9_]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The most cells the exact matching fills before the texts are anchored first (see <see cref="Matches"/>).</summary>
    private const long MaxCells = 30_000_000;

    /// <summary>
    /// The lines of <paramref name="text"/> as the Comparison Tool counts them: broken at CRLF, LF or CR, a trailing
    /// line break making an empty last line (measured: a file ending in a line break has one more line than one that
    /// does not, and only that line differs between them).
    /// </summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n')
            {
                lines.Add(text[start..i]);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        lines.Add(text[start..]);
        return lines;
    }

    /// <summary>The rows of the comparison of <paramref name="left"/> with <paramref name="right"/>.</summary>
    public static IReadOnlyList<DiffRow> Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var rows = new List<DiffRow>(Math.Max(left.Count, right.Count));
        // Lines as integers, so that the matching compares numbers.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] a = [.. left.Select(l => IdOf(ids, l))];
        int[] b = [.. right.Select(l => IdOf(ids, l))];
        IReadOnlyList<(int Left, int Right)> matches = Matches(a, b, 0, a.Length, 0, b.Length);
        int i = 0;
        int j = 0;
        foreach ((int mi, int mj) in matches)
        {
            AddGap(rows, left, right, i, mi, j, mj);
            rows.Add(new DiffRow(DiffRowKind.Same, mi + 1, mj + 1));
            i = mi + 1;
            j = mj + 1;
        }

        AddGap(rows, left, right, i, left.Count, j, right.Count);
        return rows;
    }

    /// <summary>The rows of the comparison of two texts.</summary>
    public static IReadOnlyList<DiffRow> Compare(string left, string right) => Compare(SplitLines(left), SplitLines(right));

    /// <summary>The differences among <paramref name="rows"/>: each maximal run of rows that are not the same.</summary>
    public static IReadOnlyList<DiffHunk> Hunks(IReadOnlyList<DiffRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var hunks = new List<DiffHunk>();
        int at = 0;
        while (at < rows.Count)
        {
            if (rows[at].Kind == DiffRowKind.Same)
            {
                at++;
                continue;
            }

            int end = at;
            while (end < rows.Count && rows[end].Kind != DiffRowKind.Same)
            {
                end++;
            }

            hunks.Add(new DiffHunk(at, end - at));
            at = end;
        }

        return hunks;
    }

    /// <summary>
    /// The characters of two changed lines, each side cut into what both have and what only it has, by a longest
    /// common subsequence of their characters (the report boxes the characters a side alone has).
    /// </summary>
    public static (IReadOnlyList<DiffSegment> Left, IReadOnlyList<DiffSegment> Right) Inline(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        int[] a = [.. left.Select(static c => (int)c)];
        int[] b = [.. right.Select(static c => (int)c)];
        IReadOnlyList<(int Left, int Right)> matches = (long)(a.Length + 1) * (b.Length + 1) <= MaxCells ? LcsBackward(a, b, 0, a.Length, 0, b.Length) : [];
        var common = new bool[a.Length];
        var commonRight = new bool[b.Length];
        foreach ((int i, int j) in matches)
        {
            common[i] = true;
            commonRight[j] = true;
        }

        return (Segments(left, common), Segments(right, commonRight));
    }

    /// <summary>The words of a line - runs of letters, digits and underscores - as the pairing of changed lines reads them.</summary>
    public static IReadOnlyList<string> Words(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return [.. WordPattern.Matches(line).Select(static m => m.Value)];
    }

    private static int IdOf(Dictionary<string, int> ids, string line)
    {
        if (!ids.TryGetValue(line, out int id))
        {
            id = ids.Count;
            ids[line] = id;
        }

        return id;
    }

    private static IReadOnlyList<DiffSegment> Segments(string text, bool[] common)
    {
        var segments = new List<DiffSegment>();
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || common[i] != common[start])
            {
                segments.Add(new DiffSegment(text[start..i], !common[start]));
                start = i;
            }
        }

        return segments;
    }

    /// <summary>
    /// The rows of one run of unmatched lines: left lines <paramref name="i0"/> to <paramref name="i1"/> (exclusive) and
    /// right lines <paramref name="j0"/> to <paramref name="j1"/>, paired where they share a word, deleted before inserted otherwise.
    /// </summary>
    private static void AddGap(List<DiffRow> rows, IReadOnlyList<string> left, IReadOnlyList<string> right, int i0, int i1, int j0, int j1)
    {
        if (i0 >= i1 && j0 >= j1)
        {
            return;
        }

        if (i0 < i1 && j0 < j1)
        {
            // Each left line in turn takes the first right line still free that shares a word with it (measured: the
            // pairs R2025b's rows show never cross, and every pair shares a word).
            var pairs = new List<(int Left, int Right)>();
            int next = j0;
            for (int i = i0; i < i1 && next < j1; i++)
            {
                var words = new HashSet<string>(Words(left[i]), StringComparer.Ordinal);
                if (words.Count == 0)
                {
                    continue;
                }

                for (int j = next; j < j1; j++)
                {
                    if (Words(right[j]).Any(words.Contains))
                    {
                        pairs.Add((i, j));
                        next = j + 1;
                        break;
                    }
                }
            }

            int at = i0;
            int bt = j0;
            foreach ((int li, int rj) in pairs)
            {
                AddUnpaired(rows, at, li, bt, rj);
                rows.Add(new DiffRow(DiffRowKind.Changed, li + 1, rj + 1));
                at = li + 1;
                bt = rj + 1;
            }

            AddUnpaired(rows, at, i1, bt, j1);
            return;
        }

        AddUnpaired(rows, i0, i1, j0, j1);
    }

    private static void AddUnpaired(List<DiffRow> rows, int i0, int i1, int j0, int j1)
    {
        for (int i = i0; i < i1; i++)
        {
            rows.Add(new DiffRow(DiffRowKind.Deleted, i + 1, 0));
        }

        for (int j = j0; j < j1; j++)
        {
            rows.Add(new DiffRow(DiffRowKind.Inserted, 0, j + 1));
        }
    }

    /// <summary>
    /// The matched pairs of two integer sequences over the given ranges, in order: the shorter matched where it first
    /// occurs whole in the longer; otherwise a longest common subsequence by the table, read from the end when the
    /// ranges differ by an even number of lines and from the start when by an odd one (see the class remarks); or, for
    /// ranges too large for the table, the lines unique to both as anchors and the table between them.
    /// </summary>
    private static IReadOnlyList<(int Left, int Right)> Matches(int[] a, int[] b, int a0, int a1, int b0, int b1)
    {
        int n = a1 - a0;
        int m = b1 - b0;
        if (n == 0 || m == 0)
        {
            return [];
        }

        if (Contained(a, b, a0, a1, b0, b1) is { } contained)
        {
            return contained;
        }

        if ((long)(n + 1) * (m + 1) > MaxCells)
        {
            return Anchored(a, b, a0, a1, b0, b1);
        }

        return (n - m) % 2 == 0 ? LcsBackward(a, b, a0, a1, b0, b1) : LcsForward(a, b, a0, a1, b0, b1);
    }

    /// <summary>The shorter range matched where it first occurs whole inside the longer one; null when it does not.</summary>
    private static IReadOnlyList<(int Left, int Right)>? Contained(int[] a, int[] b, int a0, int a1, int b0, int b1)
    {
        int n = a1 - a0;
        int m = b1 - b0;
        if (n <= m)
        {
            for (int at = b0; at + n <= b1; at++)
            {
                if (Same(a, a0, b, at, n))
                {
                    return [.. Enumerable.Range(0, n).Select(k => (a0 + k, at + k))];
                }
            }
        }
        else
        {
            for (int at = a0; at + m <= a1; at++)
            {
                if (Same(a, at, b, b0, m))
                {
                    return [.. Enumerable.Range(0, m).Select(k => (at + k, b0 + k))];
                }
            }
        }

        return null;
    }

    private static bool Same(int[] a, int ai, int[] b, int bj, int count)
    {
        for (int k = 0; k < count; k++)
        {
            if (a[ai + k] != b[bj + k])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Large ranges: the values that occur once on each side are matched by a longest increasing run of their
    /// positions, and the ranges between them matched by the table; when there are none, the longer range is split in
    /// two at its middle (a divergence: R2025b's tool matches such texts whole).
    /// </summary>
    private static IReadOnlyList<(int Left, int Right)> Anchored(int[] a, int[] b, int a0, int a1, int b0, int b1)
    {
        var countA = new Dictionary<int, int>();
        var countB = new Dictionary<int, int>();
        for (int i = a0; i < a1; i++)
        {
            countA[a[i]] = countA.GetValueOrDefault(a[i]) + 1;
        }

        for (int j = b0; j < b1; j++)
        {
            countB[b[j]] = countB.GetValueOrDefault(b[j]) + 1;
        }

        var positionB = new Dictionary<int, int>();
        for (int j = b0; j < b1; j++)
        {
            if (countB[b[j]] == 1 && countA.GetValueOrDefault(b[j]) == 1)
            {
                positionB[b[j]] = j;
            }
        }

        var uniqueA = new List<int>();
        var uniqueBOfA = new List<int>();
        for (int i = a0; i < a1; i++)
        {
            if (countA[a[i]] == 1 && positionB.TryGetValue(a[i], out int j))
            {
                uniqueA.Add(i);
                uniqueBOfA.Add(j);
            }
        }

        var result = new List<(int, int)>();
        if (uniqueA.Count == 0)
        {
            if (a1 - a0 >= b1 - b0)
            {
                int mid = (a0 + a1) / 2;
                result.AddRange(Matches(a, b, a0, mid, b0, b1));
                int lastB = result.Count > 0 ? result[^1].Item2 + 1 : b0;
                result.AddRange(Matches(a, b, mid, a1, lastB, b1));
            }
            else
            {
                int mid = (b0 + b1) / 2;
                result.AddRange(Matches(a, b, a0, a1, b0, mid));
                int lastA = result.Count > 0 ? result[^1].Item1 + 1 : a0;
                result.AddRange(Matches(a, b, lastA, a1, mid, b1));
            }

            return result;
        }

        // The anchors in both orders: the longest increasing run of right positions over the left's order.
        int[] sortedB = [.. uniqueBOfA.Order()];
        IReadOnlyList<(int Left, int Right)> anchors = LcsBackward([.. uniqueBOfA], sortedB, 0, uniqueBOfA.Count, 0, sortedB.Length);
        int ai = a0;
        int bj = b0;
        foreach ((int ui, _) in anchors)
        {
            int ia = uniqueA[ui];
            int jb = uniqueBOfA[ui];
            result.AddRange(Matches(a, b, ai, ia, bj, jb));
            result.Add((ia, jb));
            ai = ia + 1;
            bj = jb + 1;
        }

        result.AddRange(Matches(a, b, ai, a1, bj, b1));
        return result;
    }

    /// <summary>
    /// A longest common subsequence of <paramref name="a"/>[a0..a1) and <paramref name="b"/>[b0..b1) by the prefix table,
    /// read back from its end; where moving up the left and moving back the right are equally good, the left is moved.
    /// </summary>
    private static IReadOnlyList<(int Left, int Right)> LcsBackward(int[] a, int[] b, int a0, int a1, int b0, int b1)
    {
        int n = a1 - a0;
        int m = b1 - b0;
        var result = new List<(int, int)>();
        if (n == 0 || m == 0)
        {
            return result;
        }

        int width = m + 1;
        var table = new int[(n + 1) * width];
        for (int i = 1; i <= n; i++)
        {
            int ai = a[a0 + i - 1];
            int row = i * width;
            int above = (i - 1) * width;
            for (int j = 1; j <= m; j++)
            {
                table[row + j] = ai == b[b0 + j - 1]
                    ? table[above + j - 1] + 1
                    : Math.Max(table[above + j], table[row + j - 1]);
            }
        }

        int x = n;
        int y = m;
        while (x > 0 && y > 0)
        {
            if (a[a0 + x - 1] == b[b0 + y - 1])
            {
                result.Add((a0 + x - 1, b0 + y - 1));
                x--;
                y--;
            }
            else if (table[(x - 1) * width + y] >= table[x * width + y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        result.Reverse();
        return result;
    }

    /// <summary>
    /// A longest common subsequence by the suffix table, read from the start: equal lines are matched at once, and
    /// where skipping a left line and skipping a right line are equally good, the right line is skipped.
    /// </summary>
    private static IReadOnlyList<(int Left, int Right)> LcsForward(int[] a, int[] b, int a0, int a1, int b0, int b1)
    {
        int n = a1 - a0;
        int m = b1 - b0;
        var result = new List<(int, int)>();
        if (n == 0 || m == 0)
        {
            return result;
        }

        int width = m + 2;
        var table = new int[(n + 2) * width];
        for (int i = n - 1; i >= 0; i--)
        {
            int ai = a[a0 + i];
            int row = i * width;
            int below = (i + 1) * width;
            for (int j = m - 1; j >= 0; j--)
            {
                table[row + j] = ai == b[b0 + j]
                    ? table[below + j + 1] + 1
                    : Math.Max(table[below + j], table[row + j + 1]);
            }
        }

        int x = 0;
        int y = 0;
        while (x < n && y < m)
        {
            if (a[a0 + x] == b[b0 + y])
            {
                result.Add((a0 + x, b0 + y));
                x++;
                y++;
            }
            else if (table[x * width + y + 1] >= table[(x + 1) * width + y])
            {
                y++;
            }
            else
            {
                x++;
            }
        }

        return result;
    }
}
