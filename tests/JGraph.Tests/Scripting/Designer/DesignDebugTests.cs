using JGraph.Api;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs.Debug;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The debugger's view of an app's class text (App Designer window plan W11, ADR 0303): where a breakpoint
/// lands in the code view, which lines it can sit on, and how breakpoints follow their functions when App
/// Designer writes the class again around the user's code.
/// </summary>
[Collection("JG facade")]
public sealed class DesignDebugTests : IDisposable
{
    private readonly DesignDocument _document;

    public DesignDebugTests()
    {
        JG.Reset();
        _document = DesignDocument.FromText(AppDesignerDocument.NewAppText(), null);
    }

    public void Dispose()
    {
        _document.Dispose();
        JG.Reset();
    }

    private string Text => _document.CurrentText;

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private int LineOf(string fragment)
    {
        string[] lines = Text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(fragment, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException($"'{fragment}' is not in the class text.");
    }

    /// <summary>Types a body under a function's signature, as the code view's edit does.</summary>
    private void Type(string signature, string body)
    {
        string text = Text;
        int start = text.IndexOf(signature + "\n", StringComparison.Ordinal);
        int end = start < 0 ? -1 : text.IndexOf("\n        end", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The code has no function " + signature);
        Assert.True(_document.ApplyEditedText(text[..(start + signature.Length)] + "\n" + body + text[end..]));
    }

    /// <summary>A blank app's class: createComponents' statements are executable, the classdef, the properties and the function line are not; a breakpoint asked for on them lands on the next statement.</summary>
    [Fact]
    public void ExecutableLinesAreTheStatementsAndTheFunctionEnds()
    {
        IReadOnlyList<int> executable = DesignDebug.ExecutableLines(Text);
        int createComponents = LineOf("function createComponents(app)");
        int firstStatement = LineOf("app.UIFigure = uifigure(");
        Assert.DoesNotContain(1, executable);
        Assert.DoesNotContain(createComponents, executable);
        Assert.Contains(firstStatement, executable);
        Assert.Equal(firstStatement, DesignDebug.Snap(Text, createComponents));
        Assert.Equal(firstStatement, DesignDebug.Snap(Text, firstStatement));
        Assert.Null(DesignDebug.Snap(Text, 10_000));
        Assert.Equal("app1.createComponents", DesignDebug.FunctionAt(Text, firstStatement));
    }

    /// <summary>A breakpoint in a callback keeps its place in the callback when a component is added and the generated code around it moves.</summary>
    [Fact]
    public void BreakpointsFollowTheirFunctionAcrossARewriteOfTheGeneratedCode()
    {
        DesignComponent button = _document.Add(Entry("Button"), _document.Design.Figure, new Point2D(120, 240)).Single();
        Assert.Null(_document.AddCallback(button, "ButtonPushedFcn"));
        string callback = _document.CallbackOf(button, "ButtonPushedFcn")!;
        Type($"        function {callback}(app, event)", "            a = 1;\n            b = a + 1;");
        string before = Text;
        int bodyLine = LineOf("b = a + 1;");
        string file = @"C:\apps\app1.mlapp";
        var breakpoints = new List<JgsBreakpoint> { new(file, bodyLine, "a > 0", Enabled: false) };
        IReadOnlyList<DesignBreakpointAnchor> anchors = DesignDebug.Anchor(before, breakpoints);
        DesignBreakpointAnchor anchor = Assert.Single(anchors);
        Assert.Equal($"app1.{callback}", anchor.Function);
        Assert.Equal(2, anchor.Offset);
        Assert.Equal("a > 0", anchor.Condition);
        Assert.False(anchor.Enabled);

        // A second component adds a property and creation lines above the callbacks; the callback moves down.
        _document.Add(Entry("Label"), _document.Design.Figure, new Point2D(20, 20));
        string after = Text;
        int moved = LineOf("b = a + 1;");
        Assert.NotEqual(bodyLine, moved);
        JgsBreakpoint resolved = Assert.Single(DesignDebug.Resolve(after, file, anchors));
        Assert.Equal(moved, resolved.Line);
        Assert.Equal("a > 0", resolved.Condition);
        Assert.False(resolved.Enabled);
        Assert.Equal(file, resolved.File);
    }

    /// <summary>A plain-text app written with CRLF line endings renders each line once: the code view's lines are the file's (W11 found them doubled).</summary>
    [Theory]
    [InlineData("crlf")]
    [InlineData("mixed")]
    public void APlainTextAppWithCrlfLineEndingsRendersEachLineOnce(string endings)
    {
        string lf = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "runtime-checks", "w4", "W4App.m")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] original = lf.Split('\n');
        string source = endings == "crlf"
            ? lf.Replace("\n", "\r\n", StringComparison.Ordinal)
            : string.Concat(original.Select((line, i) => line + (i == original.Length - 1 ? string.Empty : i % 2 == 0 ? "\r\n" : "\n")));
        using DesignDocument document = DesignDocument.FromPlainText(source, null);
        string text = document.CurrentText;
        int doubled = text.IndexOf("\r\r", StringComparison.Ordinal);
        Assert.True(doubled < 0, doubled < 0 ? string.Empty : "doubled at " + doubled + ": " + text[Math.Max(0, doubled - 160)..Math.Min(text.Length, doubled + 80)].Replace("\r", "<CR>", StringComparison.Ordinal).Replace("\n", "<LF>\n", StringComparison.Ordinal));
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.DoesNotContain("\r", string.Concat(lines));
        int b = Array.FindIndex(lines, l => l.Contains("B          matlab.ui.control.Button", StringComparison.Ordinal));
        int l = Array.FindIndex(lines, l => l.Contains("L          matlab.ui.control.Label", StringComparison.Ordinal));
        Assert.Equal(b + 1, l);
    }

    /// <summary>An anchor whose function is gone is dropped; one whose offset runs past the function's end is dropped too; one landing on a function line moves to its first statement.</summary>
    [Fact]
    public void AnchorsWithoutAHomeAreDropped()
    {
        string file = @"C:\apps\app1.mlapp";
        IReadOnlyList<JgsBreakpoint> resolved = DesignDebug.Resolve(Text, file,
        [
            new DesignBreakpointAnchor("app1.NoSuchCallback", 1, string.Empty, true),
            new DesignBreakpointAnchor("app1.createComponents", 10_000, string.Empty, true),
            new DesignBreakpointAnchor("app1.createComponents", 1, string.Empty, true),
        ]);
        JgsBreakpoint kept = Assert.Single(resolved);
        Assert.Equal(LineOf("app.UIFigure = uifigure("), kept.Line);
    }
}
