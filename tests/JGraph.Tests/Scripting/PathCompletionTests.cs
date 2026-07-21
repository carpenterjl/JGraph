using JGraph.Scripting.Completion;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M17: workspace filename completion inside the string arguments of the file-reading builtins
/// (readcsv/readxlsx/readtable/audioread/imread/sparameters/loadfigure, and run in JGS) — detection,
/// prefix segmentation, extension filtering, and the workspace-tree flattening, all through the
/// public engine-agnostic API.
/// </summary>
public sealed class PathCompletionTests
{
    private static readonly IReadOnlyList<WorkspaceFileEntry> Workspace = new[]
    {
        new WorkspaceFileEntry("lib", IsDirectory: true),
        new WorkspaceFileEntry("lib/helpers.jgs", IsDirectory: false),
        new WorkspaceFileEntry("lib/extra.csv", IsDirectory: false),
        new WorkspaceFileEntry("main.jgs", IsDirectory: false),
        new WorkspaceFileEntry("sample-measurement.csv", IsDirectory: false),
        new WorkspaceFileEntry("Samples.TSV", IsDirectory: false),
        new WorkspaceFileEntry("report.xlsx", IsDirectory: false),
        new WorkspaceFileEntry("notes.txt", IsDirectory: false),
        new WorkspaceFileEntry("picture.png", IsDirectory: false),
        new WorkspaceFileEntry("amp.s2p", IsDirectory: false),
        new WorkspaceFileEntry("saved-figure.graph", IsDirectory: false),
        new WorkspaceFileEntry("clip.wav", IsDirectory: false),
    };

    // --- Detect -------------------------------------------------------------------------------

    [Theory]
    [InlineData("let t = readcsv(\"sam", "JGS")]
    [InlineData("var t = readcsv(\"sam", "C#")]
    [InlineData("t = readcsv('sam", "Python")]
    public void Detect_Hits_InAllLanguages_AndBothQuoteKinds(string code, string language)
    {
        PathCompletionContext? context = PathCompletion.Detect(code, code.Length, language);

        Assert.NotNull(context);
        Assert.Equal("readcsv", context!.FunctionName);
        Assert.Equal(string.Empty, context.DirectoryPrefix);
        Assert.Equal("sam", context.PartialName);
        Assert.Equal(code.Length - 3, context.ReplaceStart);
    }

    [Fact]
    public void Detect_SegmentsDirectoryPrefix_AtTheLastSeparator()
    {
        const string code = "let t = readtable(\"lib/hel";
        PathCompletionContext? context = PathCompletion.Detect(code, code.Length, "JGS");

        Assert.Equal("lib/", context!.DirectoryPrefix);
        Assert.Equal("hel", context.PartialName);
        Assert.Equal(code.Length - 3, context.ReplaceStart);
    }

    [Fact]
    public void Detect_NormalizesBackslashSeparators()
    {
        const string code = "readtable(\"lib\\hel";
        PathCompletionContext? context = PathCompletion.Detect(code, code.Length, "JGS");

        Assert.Equal("lib/", context!.DirectoryPrefix);
        Assert.Equal("hel", context.PartialName);
    }

    [Fact]
    public void Detect_Run_IsJgsOnly()
    {
        const string code = "run(\"hel";
        Assert.NotNull(PathCompletion.Detect(code, code.Length, "JGS"));
        Assert.Null(PathCompletion.Detect(code, code.Length, "C#"));
        Assert.Null(PathCompletion.Detect(code, code.Length, "Python"));
    }

    [Fact]
    public void Detect_AllowsWhitespace_BetweenNameParenAndQuote()
    {
        const string code = "let t = readcsv ( \"sam";
        Assert.Equal("readcsv", PathCompletion.Detect(code, code.Length, "JGS")!.FunctionName);
    }

    [Theory]
    [InlineData("let t = plot(\"b-")]          // not a file function
    [InlineData("let t = readcsv(x, \"sam")]   // not argument 0 (previous token isn't '(')
    [InlineData("let t = readcsv(sam")]        // not inside a string
    [InlineData("# readcsv(\"sam")]            // comment
    [InlineData("// readcsv(\"sam")]           // comment
    [InlineData("let t = readcsv(\"sam\") ")]  // after the closing quote
    [InlineData("let t = readcsv(\"C:\\data\\sam")] // rooted path
    [InlineData("let t = readcsv(\"/etc/sam")] // rooted path
    [InlineData("let t = readcsv(\"../sam")]   // escapes the workspace
    public void Detect_ReturnsNull_OutsideAPathArgument(string code)
    {
        Assert.Null(PathCompletion.Detect(code, code.Length, "JGS"));
    }

    // --- GetCompletions -----------------------------------------------------------------------

    [Fact]
    public void Completions_FilterByTheFunctionsExtensions_AndAlwaysOfferFolders()
    {
        var context = new PathCompletionContext("readcsv", "", "", ReplaceStart: 0);
        IReadOnlyList<CompletionItem> items = PathCompletion.GetCompletions(context, Workspace);

        Assert.Contains(items, static i => i.Text == "lib/" && i.Kind == CompletionItemKind.Folder);
        Assert.Contains(items, static i => i.Text == "sample-measurement.csv" && i.Kind == CompletionItemKind.File);
        Assert.Contains(items, static i => i.Text == "Samples.TSV"); // .tsv, case-insensitive
        Assert.Contains(items, static i => i.Text == "notes.txt");
        Assert.DoesNotContain(items, static i => i.Text == "report.xlsx");
        Assert.DoesNotContain(items, static i => i.Text == "picture.png");
        Assert.DoesNotContain(items, static i => i.Text == "main.jgs");
        Assert.Equal(CompletionItemKind.Folder, items[0].Kind); // folders first
    }

    [Theory]
    [InlineData("imread", "picture.png")]
    [InlineData("sparameters", "amp.s2p")]
    [InlineData("loadfigure", "saved-figure.graph")]
    [InlineData("audioread", "clip.wav")]
    public void Completions_CoverEveryFileReadingBuiltin_NotJustTheTableReaders(string function, string expected)
    {
        string code = $"let x = {function}('";
        PathCompletionContext? context = PathCompletion.Detect(code, code.Length, "JGS");

        Assert.Equal(function, context!.FunctionName);
        IReadOnlyList<CompletionItem> items = PathCompletion.GetCompletions(context, Workspace);
        Assert.Contains(items, i => i.Text == expected && i.Kind == CompletionItemKind.File);
        Assert.DoesNotContain(items, static i => i.Text == "notes.txt"); // each reader keeps its own extensions
    }

    [Fact]
    public void Completions_ForReadxlsx_OfferOnlyXlsx()
    {
        var context = new PathCompletionContext("readxlsx", "", "", 0);
        IReadOnlyList<CompletionItem> items = PathCompletion.GetCompletions(context, Workspace);

        Assert.Contains(items, static i => i.Text == "report.xlsx");
        Assert.DoesNotContain(items, static i => i.Kind == CompletionItemKind.File && i.Text.EndsWith(".csv"));
    }

    [Fact]
    public void Completions_ListTheTypedSubfolder_Only()
    {
        var context = new PathCompletionContext("readtable", "lib/", "", 0);
        IReadOnlyList<CompletionItem> items = PathCompletion.GetCompletions(context, Workspace);

        Assert.Contains(items, static i => i.Text == "extra.csv");
        Assert.DoesNotContain(items, static i => i.Text == "sample-measurement.csv"); // root file
        Assert.DoesNotContain(items, static i => i.Text == "lib/");                   // not its own child
    }

    [Fact]
    public void Completions_PrefixMatch_IgnoresCase()
    {
        var context = new PathCompletionContext("run", "lib/", "HEL", 0);
        IReadOnlyList<CompletionItem> items = PathCompletion.GetCompletions(context, Workspace);

        CompletionItem item = Assert.Single(items);
        Assert.Equal("helpers.jgs", item.Text);
    }

    [Fact]
    public void Completions_WithNoMatch_AreEmpty()
    {
        var context = new PathCompletionContext("readcsv", "", "zzz", 0);
        Assert.Empty(PathCompletion.GetCompletions(context, Workspace));
    }

    // --- Flatten ------------------------------------------------------------------------------

    [Fact]
    public void Flatten_WalksTheTree_AndNormalizesSeparators()
    {
        var tree = new[]
        {
            new JGraph.Scripting.Workspace.WorkspaceEntry(
                @"C:\ws\lib", @"lib", IsDirectory: true,
                new[]
                {
                    new JGraph.Scripting.Workspace.WorkspaceEntry(
                        @"C:\ws\lib\helpers.jgs", @"lib\helpers.jgs", IsDirectory: false,
                        Array.Empty<JGraph.Scripting.Workspace.WorkspaceEntry>()),
                }),
            new JGraph.Scripting.Workspace.WorkspaceEntry(
                @"C:\ws\main.jgs", "main.jgs", IsDirectory: false,
                Array.Empty<JGraph.Scripting.Workspace.WorkspaceEntry>()),
        };

        IReadOnlyList<WorkspaceFileEntry> flat = PathCompletion.Flatten(tree);

        Assert.Equal(3, flat.Count);
        Assert.Contains(flat, static e => e.RelativePath == "lib" && e.IsDirectory);
        Assert.Contains(flat, static e => e.RelativePath == "lib/helpers.jgs" && !e.IsDirectory);
        Assert.Contains(flat, static e => e.RelativePath == "main.jgs");
    }
}
