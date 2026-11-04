using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using JGraph.Scripting.Editing;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;

namespace JGraph.Scripting.Designer;

/// <summary>What one difference between two designs is.</summary>
public enum DesignDifferenceKind
{
    /// <summary>A component the right app has and the left does not.</summary>
    Added,

    /// <summary>A component the left app has and the right does not.</summary>
    Removed,

    /// <summary>A component under another parent, or at another place among its siblings.</summary>
    Moved,

    /// <summary>A component of the same code name but another class.</summary>
    TypeChanged,

    /// <summary>A property with another value on the right.</summary>
    PropertyChanged,
}

/// <summary>
/// One difference between the designs of two apps (App Designer window plan W14), by code name: a component added,
/// removed or moved, its class changed, or one of its properties changed, with both values written as the code writes them.
/// </summary>
/// <param name="Kind">What changed.</param>
/// <param name="CodeName">The component's code name.</param>
/// <param name="Type">The component's class (the right's for a type change).</param>
/// <param name="Property">The property that changed; empty otherwise.</param>
/// <param name="Left">The left side's value, place or class; empty when the left has none.</param>
/// <param name="Right">The right side's; empty when the right has none.</param>
public sealed record DesignDifference(DesignDifferenceKind Kind, string CodeName, string Type, string Property, string Left, string Right)
{
    /// <summary>The difference in words, for a list.</summary>
    public string Text => Kind switch
    {
        DesignDifferenceKind.Added => $"{CodeName} ({Type}) added",
        DesignDifferenceKind.Removed => $"{CodeName} ({Type}) removed",
        DesignDifferenceKind.Moved => $"{CodeName} moved from {Left} to {Right}",
        DesignDifferenceKind.TypeChanged => $"{CodeName} is a {Left} on the left and a {Right} on the right",
        _ => $"{CodeName}.{Property}: {Left} on the left, {Right} on the right",
    };
}

/// <summary>Which of R2025b's comparison types a pair of files takes.</summary>
public enum ComparisonKind
{
    /// <summary>Two text files: R2025b's "Text comparison" (<c>comparisons.TextComparison</c>).</summary>
    Text,

    /// <summary>Two apps, of either format: R2025b's "App code comparison" (<c>comparisons.MlappComparison</c>) over their class text.</summary>
    App,
}

/// <summary>
/// The comparison of two files as R2025b's Comparison Tool shows it (App Designer window plan W14): the rows of
/// <see cref="TextDiff"/> over their texts - for an app, the class text, as <c>getAppFileCode</c> gives it to
/// <c>comparisons.internal.mlapp.mcosDiff</c> - the differences to navigate, the lines of the right side a merge may
/// replace (R2025b's <c>isLinesMergeable</c>: the user's regions, never the generated code), why merging is refused
/// when it is, the merge itself (left to right, as the Comparison Tool's Replace Content does), Save Result, and the
/// report <c>publish</c> writes. JGraph adds the design's differences by code name beside the code's.
/// </summary>
public sealed class FileComparison
{
    private FileComparison(string leftPath, string rightPath, ComparisonKind kind, IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines,
        IReadOnlyList<DesignDifference> designDifferences, IReadOnlySet<int> mergeableRightLines, string? mergeRefusal, string rightNewline, bool rightTrailingNewline)
    {
        LeftPath = leftPath;
        RightPath = rightPath;
        Kind = kind;
        LeftLines = leftLines;
        RightLines = rightLines;
        Rows = TextDiff.Compare(leftLines, rightLines);
        Hunks = TextDiff.Hunks(Rows);
        DesignDifferences = designDifferences;
        MergeableRightLines = mergeableRightLines;
        MergeRefusal = mergeRefusal;
        RightNewline = rightNewline;
        RightTrailingNewline = rightTrailingNewline;
    }

    /// <summary>The left file's full path.</summary>
    public string LeftPath { get; }

    /// <summary>The right file's full path.</summary>
    public string RightPath { get; }

    /// <summary>Which comparison this is.</summary>
    public ComparisonKind Kind { get; }

    /// <summary>The left text's lines.</summary>
    public IReadOnlyList<string> LeftLines { get; }

    /// <summary>The right text's lines, as merged so far.</summary>
    public IReadOnlyList<string> RightLines { get; }

    /// <summary>The rows, side by side.</summary>
    public IReadOnlyList<DiffRow> Rows { get; }

    /// <summary>The differences, each a run of rows that are not the same.</summary>
    public IReadOnlyList<DiffHunk> Hunks { get; }

    /// <summary>The designs' differences by code name; empty for a text comparison.</summary>
    public IReadOnlyList<DesignDifference> DesignDifferences { get; }

    /// <summary>The one-based lines of the right text a merge may replace: every line of a text file, the user's regions of an app.</summary>
    public IReadOnlySet<int> MergeableRightLines { get; }

    /// <summary>Why no difference may be merged (an app saved in another release), or null.</summary>
    public string? MergeRefusal { get; }

    /// <summary>The right file's line break, kept when the result is written.</summary>
    public string RightNewline { get; }

    /// <summary>Whether the right file ended in a line break (its last line is then the empty one the rows show).</summary>
    public bool RightTrailingNewline { get; }

    /// <summary>Whether the texts are the same.</summary>
    public bool IsIdentical => Hunks.Count == 0;

    /// <summary>R2025b's display type for the kind (<c>DisplayType</c>).</summary>
    public string DisplayType => Kind == ComparisonKind.App ? "App code comparison" : "Text comparison";

    /// <summary>
    /// The Comparison Tool's summary line (<c>TextdiffNoDiffs</c>, <c>TextdiffNumDiffsOne</c>, <c>TextdiffNumDiffs</c>).
    /// </summary>
    public string Summary => Hunks.Count switch
    {
        0 => Kind == ComparisonKind.App ? "No changes detected" : "The files are identical.",
        1 => "1 difference found. Use the toolstrip buttons to navigate to it.",
        int n => $"{n} differences found. Use the toolstrip buttons to navigate between differences.",
    };

    /// <summary>The right text as it now stands.</summary>
    public string RightText => string.Join(RightNewline, RightLines);

    /// <summary>
    /// Compares the files at <paramref name="leftPath"/> and <paramref name="rightPath"/>: two apps as an app code
    /// comparison with the designs' differences, anything else as a text comparison.
    /// </summary>
    /// <exception cref="IOException">A file could not be read.</exception>
    /// <exception cref="InvalidDataException">An <c>.mlapp</c> holds no code.</exception>
    public static FileComparison Compare(string leftPath, string rightPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(leftPath);
        ArgumentException.ThrowIfNullOrEmpty(rightPath);
        leftPath = Path.GetFullPath(leftPath);
        rightPath = Path.GetFullPath(rightPath);
        bool apps = DesignCompare.IsApp(leftPath) && DesignCompare.IsApp(rightPath);
        string leftText = DesignCompare.CodeTextOf(leftPath, apps);
        string rightText = DesignCompare.CodeTextOf(rightPath, apps);
        IReadOnlyList<DesignDifference> differences = [];
        string? refusal = null;
        if (apps)
        {
            differences = DesignCompare.Differences(leftPath, rightPath);
            string leftRelease = DesignCompare.SavedRelease(leftPath);
            string rightRelease = DesignCompare.SavedRelease(rightPath);
            if ((leftRelease.Length > 0 && leftRelease != DesignPlainTextDetails.Release) || (rightRelease.Length > 0 && rightRelease != DesignPlainTextDetails.Release))
            {
                refusal = DesignCompare.MergeRefusalText;
            }
        }

        return OfTexts(leftPath, rightPath, leftText, rightText, apps ? ComparisonKind.App : ComparisonKind.Text, differences, refusal);
    }

    /// <summary>The comparison of two texts standing for the files named, as <see cref="Compare"/> would read them.</summary>
    public static FileComparison OfTexts(string leftPath, string rightPath, string leftText, string rightText, ComparisonKind kind,
        IReadOnlyList<DesignDifference>? designDifferences = null, string? mergeRefusal = null)
    {
        ArgumentNullException.ThrowIfNull(leftText);
        ArgumentNullException.ThrowIfNull(rightText);
        IReadOnlyList<string> left = TextDiff.SplitLines(leftText);
        IReadOnlyList<string> right = TextDiff.SplitLines(rightText);
        string newline = rightText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool trailing = rightText.EndsWith('\n') || rightText.EndsWith('\r');
        IReadOnlySet<int> mergeable = kind == ComparisonKind.App ? DesignCompare.EditableLinesOf(rightText) : new HashSet<int>(Enumerable.Range(1, right.Count));
        return new FileComparison(leftPath, rightPath, kind, left, right, designDifferences ?? [], mergeable, mergeRefusal, newline, trailing);
    }

    /// <summary>
    /// Whether <paramref name="hunk"/> may be merged left to right: no refusal stands, and every right line it holds is
    /// one the user may edit; a pure insertion is merged where the line before or after it is the user's.
    /// </summary>
    public bool IsMergeable(DiffHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        if (MergeRefusal is not null)
        {
            return false;
        }

        if (Kind == ComparisonKind.Text)
        {
            return true;
        }

        var rightLines = new List<int>();
        for (int r = hunk.FirstRow; r < hunk.EndRow; r++)
        {
            if (Rows[r].Right > 0)
            {
                rightLines.Add(Rows[r].Right);
            }
        }

        if (rightLines.Count > 0)
        {
            return rightLines.All(MergeableRightLines.Contains);
        }

        // Nothing on the right: the lines go in between the right lines around the hunk.
        int before = hunk.FirstRow > 0 ? Rows[hunk.FirstRow - 1].Right : 0;
        int after = hunk.EndRow < Rows.Count ? Rows[hunk.EndRow].Right : 0;
        return (before > 0 && MergeableRightLines.Contains(before)) || (after > 0 && MergeableRightLines.Contains(after));
    }

    /// <summary>Why <paramref name="hunk"/> cannot be merged, in R2025b's words, or null when it can.</summary>
    public string? WhyNotMergeable(DiffHunk hunk) => IsMergeable(hunk) ? null : MergeRefusal ?? DesignCompare.GeneratedCodeText;

    /// <summary>
    /// Replace Content: the right lines of <paramref name="hunk"/> replaced by its left lines (none for a pure deletion),
    /// giving a new comparison over the merged right text, with this one's designs' differences.
    /// </summary>
    /// <exception cref="InvalidOperationException">The hunk cannot be merged.</exception>
    public FileComparison Merge(DiffHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        if (WhyNotMergeable(hunk) is { } why)
        {
            throw new InvalidOperationException(why);
        }

        var merged = new List<string>(RightLines);
        int firstRight = 0;
        int lastRight = 0;
        var leftLines = new List<string>();
        for (int r = hunk.FirstRow; r < hunk.EndRow; r++)
        {
            DiffRow row = Rows[r];
            if (row.Right > 0)
            {
                firstRight = firstRight == 0 ? row.Right : firstRight;
                lastRight = row.Right;
            }

            if (row.Left > 0)
            {
                leftLines.Add(LeftLines[row.Left - 1]);
            }
        }

        int at;
        if (firstRight > 0)
        {
            at = firstRight - 1;
            merged.RemoveRange(at, lastRight - firstRight + 1);
        }
        else
        {
            // A pure insertion goes after the right line before the hunk.
            at = hunk.FirstRow > 0 ? Rows[hunk.FirstRow - 1].Right : 0;
        }

        merged.InsertRange(at, leftLines);
        return new FileComparison(LeftPath, RightPath, Kind, LeftLines, merged, DesignDifferences, MergeableOf(string.Join(RightNewline, merged), merged.Count), MergeRefusal, RightNewline, RightTrailingNewline);
    }

    /// <summary>
    /// Save Result: writes the right text as it now stands into the right file, as it is (<c>saveAppCode</c> writes the
    /// merged text itself): an <c>.mlapp</c>'s class text with App Designer's model brought up to date with it, a
    /// plain-text app's class with its appendix kept, a text file whole.
    /// </summary>
    /// <returns>What was written, for the status line.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="InvalidDataException">The <c>.mlapp</c> could not be read again.</exception>
    public string SaveResult()
    {
        string text = RightText;
        if (Kind == ComparisonKind.App && JgsMlapp.IsMlapp(RightPath))
        {
            (_, MlappSaveResult result) = AppDesignerDocument.Save(RightPath, RightPath, text);
            return $"Saved {RightPath}." + (result.GeneratedCodeChanged ? " The generated code changed with it." : string.Empty);
        }

        if (Kind == ComparisonKind.App)
        {
            string whole = File.ReadAllText(RightPath);
            if (whole.IndexOf(JgsMlappLayout.AppendixMarker, StringComparison.Ordinal) is int marker and >= 0)
            {
                // The class text was compared alone: the appendix stays as it is.
                text = text.TrimEnd('\r', '\n') + RightNewline + RightNewline + whole[marker..];
            }
        }

        File.WriteAllText(RightPath, text);
        return $"Saved {RightPath}.";
    }

    /// <summary>
    /// The report <c>publish</c> writes (<c>comparisons.internal.report.text.ComparisonReport</c>): "File Comparison
    /// Report" with the files, the environment and the rows side by side in the report's colours, as R2025b lays it out.
    /// </summary>
    /// <param name="user">Who generated it; the current user when null.</param>
    /// <param name="now">When; now when null.</param>
    public string HtmlReport(string? user = null, DateTime? now = null)
    {
        var sb = new StringBuilder();
        DateTime when = now ?? DateTime.Now;
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<html>\n<head>\n<meta content=\"text/html; charset=UTF-8\" http-equiv=\"content-type\"></meta>\n<title></title>\n");
        sb.Append("<style>p, span { font-family: sans-serif; font-size: 11pt; } p.ReportTitle { margin-bottom:12pt; } p.ReportTitle > span { font-size: 18pt; font-weight:bold; } p.Date { margin:0pt; } "
            + "p.ReportSectionHeading { width:100%; border-top:1pt solid; padding-top:6pt; } p.ReportSectionHeading > span { font-size: 12pt; font-weight: bold; } "
            + "table { table-layout:auto; border-collapse:collapse; border-spacing:0; word-wrap: break-word; tab-size: 4; width: 100% } td p { white-space: break-spaces; margin:0; } "
            + "table.EnvironmentInfoTable { width:auto; } span.EnvironmentName, span.FileInfoName { font-weight:bold; } table.FileInfoTable > thead { font-weight:bold; } "
            + "table.TextDiffTable span { font-family:'Courier', 'monospace'; }</style>\n</head>\n<body>\n");
        sb.Append("<p class=\"ReportTitle\"><span class=\"ReportTitle\">File Comparison Report</span></p>\n");
        sb.Append(CultureInfo.InvariantCulture, $"<p class=\"Date\"><span>Generated by {WebUtility.HtmlEncode(user ?? Environment.UserName)} on {when:dd-MMM-yyyy}</span></p>\n");
        sb.Append("<p class=\"ReportSectionHeading\"><span class=\"ReportSectionHeading\">Files</span></p>\n<div><table class=\"FileInfoTable\" style=\"width:100%;word-wrap:break-word;table-layout:fixed;\">\n");
        sb.Append("<thead class=\"FileInfoTable\" style=\"width:100%;\"><tr><td><span></span></td><td><span>Left File</span></td><td><span>Right File</span></td></tr></thead>\n<tbody class=\"FileInfoTable\" style=\"width:100%;\">\n");
        FileRow(sb, "File name", Path.GetFileName(LeftPath), Path.GetFileName(RightPath));
        FileRow(sb, "File path", Path.GetDirectoryName(LeftPath) ?? string.Empty, Path.GetDirectoryName(RightPath) ?? string.Empty);
        FileRow(sb, "Last modified", Modified(LeftPath), Modified(RightPath));
        sb.Append("</tbody></table></div>\n");
        sb.Append("<p class=\"ReportSectionHeading\"><span class=\"ReportSectionHeading\">Environment</span></p>\n<div><table class=\"EnvironmentInfoTable\">\n");
        sb.Append(CultureInfo.InvariantCulture, $"<tr><td><span class=\"EnvironmentName\">JGraph</span></td><td><span>{WebUtility.HtmlEncode(typeof(FileComparison).Assembly.GetName().Version?.ToString(3) ?? string.Empty)} (MATLAB {DesignPlainTextDetails.Release} dialect)</span></td></tr>\n");
        sb.Append("</table></div>\n<p class=\"ReportSectionHeading\"></p>\n<div id=\"filters\"></div>\n");
        sb.Append("<p class=\"ReportSectionHeading\"><span class=\"ReportSectionHeading\">Comparison Results</span></p>\n<div>\n");
        if (IsIdentical)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<p><span>{WebUtility.HtmlEncode(Summary)}</span></p>\n");
        }

        sb.Append("<table style=\"width:8cm;\"><tr><td><span style=\"white-space:pre-wrap;\"> </span></td></tr><tr>");
        Legend(sb, "#1171be", "+", "Insertion");
        Legend(sb, "#745a26", "−", "Deletion");
        Legend(sb, "#a709f5", "≠", "Modification");
        sb.Append("</tr><tr><td><span style=\"white-space:pre-wrap;\"> </span></td></tr></table>\n");
        sb.Append("<table class=\"TextDiffTable\" style=\"background-color:#FFFFFF;\">\n<colgroup span=\"6\"><col style=\"width:0.8%;\"/><col style=\"width:3.6%;\"/><col style=\"width:45.6%;\"/><col style=\"width:0.8%;\"/><col style=\"width:3.6%;\"/><col style=\"width:45.6%;\"/></colgroup>\n");
        foreach (DiffRow row in Rows)
        {
            sb.Append("<tr>");
            Side(sb, row.Kind, row.Left, row.Left > 0 ? LeftLines[row.Left - 1] : null, row.Right > 0 ? RightLines[row.Right - 1] : null, left: true);
            Side(sb, row.Kind, row.Right, row.Right > 0 ? RightLines[row.Right - 1] : null, row.Left > 0 ? LeftLines[row.Left - 1] : null, left: false);
            sb.Append("</tr>\n");
        }

        sb.Append("</table>\n</div>\n<p class=\"ReportSectionHeading\"></p>\n<div></div>\n</body>\n</html>\n");
        return sb.ToString();

        static void FileRow(StringBuilder sb, string name, string left, string right) =>
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td><span class=\"FileInfoName\">{name}</span></td><td><span>{WebUtility.HtmlEncode(left)}</span></td><td><span>{WebUtility.HtmlEncode(right)}</span></td></tr>\n");

        static string Modified(string path)
        {
            try
            {
                return File.GetLastWriteTime(path).ToString("dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        static void Legend(StringBuilder sb, string colour, string glyph, string word) =>
            sb.Append(CultureInfo.InvariantCulture, $"<td style=\"background-color:{colour};text-align:center;\" valign=\"middle\"><p style=\"white-space:pre-wrap;text-align:center;\"><span style=\"color:#FFFFFF;text-align:center;vertical-align:middle;\">{glyph}</span></p></td>"
                + $"<td style=\"background-color:#FFFFFF;text-align:center;\" valign=\"middle\"><span style=\"white-space:pre-wrap;vertical-align:middle;\"> {word} </span></td>");
    }

    /// <summary>One side of a report row: the glyph cell, the number cell and the text cell, as R2025b's report lays them out (what the W14 probe parsed).</summary>
    private static void Side(StringBuilder sb, DiffRowKind kind, int line, string? text, string? other, bool left)
    {
        (string glyph, string glyphColour, string rowColour) = kind switch
        {
            DiffRowKind.Inserted => (left ? " " : "+", left ? "#ffffff" : "#1171be", left ? "#e0e0e0" : "#c9e7ff"),
            DiffRowKind.Deleted => (left ? "−" : " ", left ? "#745a26" : "#ffffff", left ? "#fff4e0" : "#e0e0e0"),
            DiffRowKind.Changed => ("≠", "#a709f5", "#f3e3ff"),
            _ => (string.Empty, string.Empty, "#ffffff"),
        };
        if (kind != DiffRowKind.Same)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<td rowspan=\"1\" style=\"background-color:{glyphColour};text-align:center;border-left-style:solid;border-left-color:#000000;border-left-width:1px;\" valign=\"middle\"><span style=\"color:#FFFFFF;text-align:center;vertical-align:middle;\">{glyph}</span></td>");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<td style=\"text-align:center;background-color:#FFFFFF;\" valign=\"top\"><span style=\"text-align:center;vertical-align:top;\">{(line > 0 ? line.ToString(CultureInfo.InvariantCulture) : string.Empty)}</span></td>");
        sb.Append(CultureInfo.InvariantCulture, $"<td style=\"background-color:{rowColour};border-left-style:solid;border-right-style:solid;border-left-color:#000000;border-right-color:#000000;border-left-width:1px;border-right-width:1px;\" valign=\"top\"><p>");
        if (text is null)
        {
            sb.Append("<span> </span>");
        }
        else if (kind == DiffRowKind.Changed && other is not null)
        {
            (IReadOnlyList<DiffSegment> mine, IReadOnlyList<DiffSegment> theirs) = TextDiff.Inline(text, other);
            _ = theirs;
            foreach (DiffSegment segment in mine)
            {
                sb.Append(segment.Changed
                    ? "<span style=\"border-style:solid;border-color:#a709f5;border-width:1px;\">" + WebUtility.HtmlEncode(segment.Text) + "</span>"
                    : "<span>" + WebUtility.HtmlEncode(segment.Text) + "</span>");
            }
        }
        else
        {
            sb.Append("<span>").Append(WebUtility.HtmlEncode(text)).Append("</span>");
        }

        sb.Append("</p></td>");
    }

    private IReadOnlySet<int> MergeableOf(string rightText, int count) =>
        Kind == ComparisonKind.App ? DesignCompare.EditableLinesOf(rightText) : new HashSet<int>(Enumerable.Range(1, count));
}

/// <summary>
/// What a comparison of two apps reads from them (App Designer window plan W14): whether a file is an app, the text R2025b
/// compares (<c>getAppFileCode</c>: the class text, which for a plain-text app stops before the appendix), the release an app
/// was saved in, the lines a merge may replace, the designs' differences, and R2025b's refusal texts (<c>comparison.xml</c>).
/// </summary>
public static class DesignCompare
{
    /// <summary>R2025b's <c>viewConflictsComptibilityError</c> / <c>enterMergeModeActionDisabledDescriptionHtml</c>.</summary>
    public const string MergeRefusalText = "Merging is not supported for apps saved in versions of MATLAB other than the current version or for apps created with components from a product that requires an additional license. To enable merging, open and resave the files in App Designer using the current version of MATLAB.";

    /// <summary>R2025b's <c>acceptActionDisabledDescription</c>.</summary>
    public const string GeneratedCodeText = "Merging generated code is not supported.";

    /// <summary>Whether <paramref name="path"/> is an app: an <c>.mlapp</c>, or a <c>.m</c> in App Designer's plain-text format.</summary>
    public static bool IsApp(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (JgsMlapp.IsMlapp(path))
        {
            return true;
        }

        if (!Path.GetExtension(path).Equals(".m", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return DesignPlainText.IsPlainTextApp(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The text the comparison reads from <paramref name="path"/>: an <c>.mlapp</c>'s class text, a plain-text app's class
    /// without its appendix (the design's part, compared as a design), any other file's text.
    /// </summary>
    public static string CodeTextOf(string path, bool asApp)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (JgsMlapp.IsMlapp(path))
        {
            return JgsMlapp.ReadCode(path);
        }

        string text = File.ReadAllText(path);
        if (asApp && text.IndexOf(JgsMlappLayout.AppendixMarker, StringComparison.Ordinal) is int marker and >= 0)
        {
            return text[..marker].TrimEnd('\r', '\n');
        }

        return text;
    }

    /// <summary>The release an app was saved in (<c>MATLABRelease</c>), empty when the file does not say.</summary>
    public static string SavedRelease(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (JgsMlapp.IsMlapp(path))
        {
            try
            {
                using ZipArchive package = ZipFile.OpenRead(path);
                if (package.GetEntry("metadata/mwcoreProperties.xml") is { } entry)
                {
                    using Stream stream = entry.Open();
                    XElement? root = XDocument.Load(stream).Root;
                    return root?.Elements().FirstOrDefault(static e => e.Name.LocalName == "matlabRelease")?.Value.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        try
        {
            DesignPlainTextReadResult read = DesignPlainText.Read(File.ReadAllText(path));
            return read.Details.MatlabRelease;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The one-based lines of the class <paramref name="text"/> that are the user's: the editable section - without the
    /// one empty line App Designer keeps on each side of it, which are its own - and the callbacks' bodies. What the
    /// Code View lets the user type into, and what a merge may replace (<c>isLinesMergeable</c>).
    /// </summary>
    public static IReadOnlySet<int> EditableLinesOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var editable = new HashSet<int>();
        JgsMlappLayout layout = JgsMlappLayout.Of(text);
        foreach (MlappRegion region in layout.Regions.Where(static r => r.IsUserCode))
        {
            int first = region.FirstLine;
            int count = region.LineCount;
            if (region.Kind == MlappRegionKind.EditableSection)
            {
                if (count > 0 && layout.Lines[first].Length == 0)
                {
                    first++;
                    count--;
                }

                if (count > 0 && layout.Lines[first + count - 1].Length == 0)
                {
                    count--;
                }
            }

            for (int i = first; i < first + count; i++)
            {
                editable.Add(i + 1);
            }
        }

        return editable;
    }

    /// <summary>The designs' differences of the apps at <paramref name="leftPath"/> and <paramref name="rightPath"/>, read as designs; empty when either cannot be read as one.</summary>
    public static IReadOnlyList<DesignDifference> Differences(string leftPath, string rightPath)
    {
        try
        {
            using DesignDocument left = DesignDocument.Open(leftPath);
            using DesignDocument right = DesignDocument.Open(rightPath);
            return Differences(left.Design, right.Design);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    /// <summary>
    /// The differences between two designs by code name: the left's components in order (removed, their class, their
    /// place and their properties against the right's), then the components only the right has.
    /// </summary>
    public static IReadOnlyList<DesignDifference> Differences(AppDesign left, AppDesign right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var result = new List<DesignDifference>();
        var rightByName = right.Components.ToDictionary(static c => c.CodeName, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DesignComponent a in left.Components)
        {
            seen.Add(a.CodeName);
            if (!rightByName.TryGetValue(a.CodeName, out DesignComponent? b))
            {
                result.Add(new DesignDifference(DesignDifferenceKind.Removed, a.CodeName, a.Type, string.Empty, PlaceOf(a), string.Empty));
                continue;
            }

            if (!string.Equals(a.Type, b.Type, StringComparison.Ordinal))
            {
                result.Add(new DesignDifference(DesignDifferenceKind.TypeChanged, a.CodeName, b.Type, string.Empty, a.Type, b.Type));
            }

            string placeA = PlaceOf(a);
            string placeB = PlaceOf(b);
            if (!string.Equals(placeA, placeB, StringComparison.Ordinal))
            {
                result.Add(new DesignDifference(DesignDifferenceKind.Moved, a.CodeName, b.Type, string.Empty, placeA, placeB));
            }

            var names = new List<string>(a.Properties.Select(static p => p.Key));
            names.AddRange(b.Properties.Select(static p => p.Key).Where(n => !names.Contains(n, StringComparer.Ordinal)));
            foreach (string name in names)
            {
                DesignValue? va = a.Get(name);
                DesignValue? vb = b.Get(name);
                if (va is null && vb is null)
                {
                    continue;
                }

                if (va is not null && vb is not null && DesignValueText.IsEqual(va, vb, nanEqual: true))
                {
                    continue;
                }

                result.Add(new DesignDifference(DesignDifferenceKind.PropertyChanged, a.CodeName, b.Type, name,
                    va is null ? "(default)" : DesignValueText.Format(va), vb is null ? "(default)" : DesignValueText.Format(vb)));
            }
        }

        foreach (DesignComponent b in right.Components)
        {
            if (!seen.Contains(b.CodeName))
            {
                result.Add(new DesignDifference(DesignDifferenceKind.Added, b.CodeName, b.Type, string.Empty, string.Empty, PlaceOf(b)));
            }
        }

        return result;

        static string PlaceOf(DesignComponent c) => c.Parent is { } parent
            ? $"{parent.CodeName}[{parent.Children.ToList().IndexOf(c) + 1}]"
            : "the figure";
    }
}
