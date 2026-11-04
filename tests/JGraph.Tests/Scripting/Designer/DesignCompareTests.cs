using JGraph.Deploy.Apps;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The comparison of two apps and of two texts (App Designer window plan W14): the kind by the files, the designs'
/// differences by code name, which differences may be merged (the user's lines, never the generated code, and none
/// for an app saved in another release), Replace Content and Save Result through the design, and the report.
/// </summary>
[Collection("JG facade")]
public sealed class DesignCompareTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Deploy", "fixtures");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-compare-" + Guid.NewGuid().ToString("N"));

    public DesignCompareTests()
    {
        Directory.CreateDirectory(_folder);
        ToolboxPackage.Extract(Path.Combine(Fixtures, "AcceptForm-r2025b.mltbx"), Path.Combine(_folder, "apps"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Original => Path.Combine(_folder, "apps", "AcceptForm.mlapp");

    /// <summary>A copy of the acceptance app with its button's font size changed, its tree deleted and one callback line edited.</summary>
    private string Edited()
    {
        string copy = Path.Combine(_folder, "edited", "AcceptForm.mlapp");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(Original, copy, overwrite: true);
        using DesignDocument document = DesignDocument.Open(copy);
        DesignComponent button = document.Design.Find("ComputeButton")!;
        Assert.Null(document.SetProperty(button, "FontSize", NumericValue.Scalar(14)));
        DesignComponent tree = document.Design.Components.First(static c => c.Type == "matlab.ui.container.CheckBoxTree");
        document.Delete([tree]);
        Assert.True(document.ApplyEditedText(document.CurrentText.Replace("r = a + b;", "r = a + b + 0;", StringComparison.Ordinal)));
        document.Save();
        return copy;
    }

    [Fact]
    public void TwoAppsCompareAsAnAppCodeComparisonWithTheDesignsDifferences()
    {
        string edited = Edited();
        FileComparison comparison = FileComparison.Compare(Original, edited);
        Assert.Equal(ComparisonKind.App, comparison.Kind);
        Assert.Equal("App code comparison", comparison.DisplayType);
        Assert.Null(comparison.MergeRefusal);
        Assert.Contains(comparison.DesignDifferences, d => d.Kind == DesignDifferenceKind.PropertyChanged && d.CodeName == "ComputeButton" && d.Property == "FontSize" && d.Right == "14");
        Assert.Contains(comparison.DesignDifferences, d => d.Kind == DesignDifferenceKind.Removed && d.Type == "matlab.ui.container.CheckBoxTree");
        Assert.Contains(comparison.DesignDifferences, d => d.Kind == DesignDifferenceKind.Removed && d.Type == "matlab.ui.container.TreeNode");
        Assert.DoesNotContain(comparison.DesignDifferences, static d => d.Kind == DesignDifferenceKind.Added);
        // The edited callback line is a changed row the user may merge; the generated lines are not.
        DiffHunk callback = comparison.Hunks.Single(h => Enumerable.Range(h.FirstRow, h.RowCount).Any(r => comparison.Rows[r].Kind == DiffRowKind.Changed
            && comparison.Rows[r].Right > 0 && comparison.RightLines[comparison.Rows[r].Right - 1].Contains("r = a + b + 0;", StringComparison.Ordinal)));
        Assert.True(comparison.IsMergeable(callback));
        Assert.Null(comparison.WhyNotMergeable(callback));
        DiffHunk generated = comparison.Hunks.First(h => !comparison.IsMergeable(h));
        Assert.Equal("Merging generated code is not supported.", comparison.WhyNotMergeable(generated));
        Assert.Contains("differences found. Use the toolstrip buttons to navigate between differences.", comparison.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceContentAndSaveResultWriteTheLeftLineIntoTheRightApp()
    {
        string edited = Edited();
        FileComparison comparison = FileComparison.Compare(Original, edited);
        DiffHunk callback = comparison.Hunks.Single(h => Enumerable.Range(h.FirstRow, h.RowCount).Any(r => comparison.Rows[r].Right > 0
            && comparison.RightLines[comparison.Rows[r].Right - 1].Contains("r = a + b + 0;", StringComparison.Ordinal)));
        FileComparison merged = comparison.Merge(callback);
        Assert.DoesNotContain(merged.RightLines, static l => l.Contains("r = a + b + 0;", StringComparison.Ordinal));
        Assert.Contains(merged.RightLines, static l => l.Contains("r = a + b;", StringComparison.Ordinal));
        Assert.Equal(comparison.Hunks.Count - 1, merged.Hunks.Count);
        Assert.Equal(comparison.DesignDifferences, merged.DesignDifferences);
        Assert.StartsWith("Saved ", merged.SaveResult(), StringComparison.Ordinal);
        using DesignDocument reopened = DesignDocument.Open(edited);
        Assert.Contains("r = a + b;", reopened.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("r = a + b + 0;", reopened.Text, StringComparison.Ordinal);
        Assert.Equal("14", DesignValueText.Format(reopened.Design.Find("ComputeButton")!.Get("FontSize")!));
        Assert.Throws<InvalidOperationException>(() => comparison.Merge(comparison.Hunks.First(h => !comparison.IsMergeable(h))));
    }

    [Fact]
    public void AnAppSavedInAnotherReleaseRefusesMerging()
    {
        string copy = Path.Combine(_folder, "old", "AcceptForm.mlapp");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(Original, copy, overwrite: true);
        using (var package = System.IO.Compression.ZipFile.Open(copy, System.IO.Compression.ZipArchiveMode.Update))
        {
            System.IO.Compression.ZipArchiveEntry entry = package.GetEntry("metadata/mwcoreProperties.xml")!;
            string text;
            using (var reader = new StreamReader(entry.Open()))
            {
                text = reader.ReadToEnd();
            }

            entry.Delete();
            using var writer = new StreamWriter(package.CreateEntry("metadata/mwcoreProperties.xml").Open());
            writer.Write(text.Replace("R2025b", "R2024b", StringComparison.Ordinal));
        }

        Assert.Equal("R2024b", DesignCompare.SavedRelease(copy));
        Assert.Equal("R2025b", DesignCompare.SavedRelease(Original));
        FileComparison comparison = FileComparison.Compare(Original, copy);
        Assert.Equal(DesignCompare.MergeRefusalText, comparison.MergeRefusal);
        Assert.True(comparison.IsIdentical);
        Assert.Equal("No changes detected", comparison.Summary);
    }

    [Fact]
    public void TwoTextFilesCompareAsTextAndEveryLineMayBeMerged()
    {
        string left = Path.Combine(_folder, "a.m");
        string right = Path.Combine(_folder, "b.m");
        File.WriteAllText(left, "x = 1;\r\ny = 2;\r\nz = x + y;\r\n");
        File.WriteAllText(right, "x = 1;\r\ny = 3;\r\nz = x + y;\r\n");
        FileComparison comparison = FileComparison.Compare(left, right);
        Assert.Equal(ComparisonKind.Text, comparison.Kind);
        Assert.Equal("Text comparison", comparison.DisplayType);
        Assert.Equal("1 difference found. Use the toolstrip buttons to navigate to it.", comparison.Summary);
        Assert.Equal([new DiffRow(DiffRowKind.Same, 1, 1), new DiffRow(DiffRowKind.Changed, 2, 2), new DiffRow(DiffRowKind.Same, 3, 3), new DiffRow(DiffRowKind.Same, 4, 4)], comparison.Rows);
        Assert.True(comparison.IsMergeable(comparison.Hunks[0]));
        FileComparison merged = comparison.Merge(comparison.Hunks[0]);
        Assert.True(merged.IsIdentical);
        Assert.Equal("The files are identical.", merged.Summary);
        merged.SaveResult();
        Assert.Equal("x = 1;\r\ny = 2;\r\nz = x + y;\r\n", File.ReadAllText(right));
    }

    [Fact]
    public void TheReportIsLaidOutAsR2025bsIs()
    {
        string left = Path.Combine(_folder, "l.m");
        string right = Path.Combine(_folder, "r.m");
        File.WriteAllText(left, "a = 1;\nb = 2;\n");
        File.WriteAllText(right, "a = 1;\nb = 2 + 0;\nc = 3;\n");
        string html = FileComparison.Compare(left, right).HtmlReport("Tester", new DateTime(2026, 11, 3));
        Assert.Contains("<span class=\"ReportTitle\">File Comparison Report</span>", html, StringComparison.Ordinal);
        Assert.Contains("Generated by Tester on 03-Nov-2026", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"ReportSectionHeading\">Files</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"ReportSectionHeading\">Comparison Results</span>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"TextDiffTable\"", html, StringComparison.Ordinal);
        // The legend's glyphs, then one changed row with its glyph on both sides and one inserted row with the right's glyph; the changed characters boxed.
        Assert.Equal(3, html.Split("vertical-align:middle;\">≠<").Length - 1);
        Assert.Equal(2, html.Split("vertical-align:middle;\">+<").Length - 1);
        Assert.Contains("border-color:#a709f5;border-width:1px;\"> + 0</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDesignsDifferencesAreByCodeName()
    {
        using DesignDocument left = DesignDocument.Open(Original);
        using DesignDocument right = DesignDocument.Open(Original);
        Assert.Empty(DesignCompare.Differences(left.Design, right.Design));
        DesignComponent button = right.Design.Find("ComputeButton")!;
        Assert.Null(right.SetProperty(button, "FontSize", NumericValue.Scalar(20)));
        IReadOnlyList<DesignDifference> differences = DesignCompare.Differences(left.Design, right.Design);
        DesignDifference change = Assert.Single(differences);
        Assert.Equal((DesignDifferenceKind.PropertyChanged, "ComputeButton", "FontSize", "20"), (change.Kind, change.CodeName, change.Property, change.Right));
        Assert.Contains("ComputeButton.FontSize:", change.Text, StringComparison.Ordinal);
        Assert.True(DesignCompare.IsApp(Original));
        Assert.False(DesignCompare.IsApp(Path.Combine(_folder, "nowhere.m")));
    }

    [Fact]
    public void TheEditableLinesAreTheUsersRegions()
    {
        string text = JGraph.Scripting.Jgs.JgsMlapp.ReadCode(Original);
        IReadOnlySet<int> editable = DesignCompare.EditableLinesOf(text);
        string[] lines = text.Split('\n');
        Assert.Contains(editable, l => lines[l - 1].Contains("r = a + b;", StringComparison.Ordinal));
        Assert.DoesNotContain(editable, l => lines[l - 1].Contains("classdef", StringComparison.Ordinal));
        Assert.DoesNotContain(editable, l => lines[l - 1].Contains("createComponents", StringComparison.Ordinal));
    }
}
