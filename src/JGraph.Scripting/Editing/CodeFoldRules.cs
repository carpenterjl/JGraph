namespace JGraph.Scripting.Editing;

/// <summary>What a fold candidate is (R2025b's <c>FoldableMTreeNodeTypes</c> and <c>CodeFoldingSettingKeys</c>).</summary>
public enum FoldKind
{
    Function,
    Classdef,
    Properties,
    Methods,
    Events,
    Enumeration,
    For,
    Parfor,
    While,
    Try,
    Spmd,
    If,
    Switch,
    Case,
    Arguments,
    BlockComment,
    Comment,
    Section,
}

/// <summary>A block the editor can fold: its kind and its first and last lines, one-based; folding hides the lines after the first.</summary>
public readonly record struct FoldCandidate(FoldKind Kind, int First, int Last)
{
    /// <summary>Whether <paramref name="line"/> (one-based) is in the candidate, its first line included.</summary>
    public bool Contains(int line) => line >= First && line <= Last;
}

/// <summary>
/// Code folding, free of a window (App Designer window plan W9c, R2025b's <c>rtc/plugins/folding</c> with the MATLAB language's
/// <c>MatlabMTreeFoldCandidateContributor</c>, <c>FoldPreferences</c> and <c>FoldableMTreeNodeTypes</c>): every block of the text that
/// spans more than one line is a candidate when its kind's setting is on - functions, classes and their blocks, loops, try, spmd, block
/// comments and runs of comment lines by default; if, switch and its cases, arguments blocks and sections off - and the candidate at a
/// line is the innermost one holding it.
/// </summary>
public static class CodeFoldRules
{
    /// <summary>The kinds folded by default (<c>matlab.editor.codefolding.Fold*</c> as R2025b ships them).</summary>
    public static IReadOnlySet<FoldKind> DefaultKinds { get; } = new HashSet<FoldKind>
    {
        FoldKind.Function, FoldKind.Classdef, FoldKind.Properties, FoldKind.Methods, FoldKind.Events, FoldKind.Enumeration,
        FoldKind.For, FoldKind.Parfor, FoldKind.While, FoldKind.Try, FoldKind.Spmd, FoldKind.BlockComment, FoldKind.Comment,
    };

    /// <summary>Every block of <paramref name="lines"/> that spans more than one line, whatever its kind, in text order.</summary>
    public static IReadOnlyList<FoldCandidate> AllBlocks(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var found = new List<FoldCandidate>();
        foreach (SmartIndent.Block block in SmartIndent.Blocks(lines))
        {
            if (block.Last > block.First && KindOf(block.Kind) is { } kind)
            {
                found.Add(new FoldCandidate(kind, block.First + 1, block.Last + 1));
            }
        }

        found.AddRange(CommentRuns(lines));
        found.AddRange(Sections(lines));
        return [.. found.OrderBy(static c => c.First).ThenByDescending(static c => c.Last)];
    }

    /// <summary>The candidates of <paramref name="lines"/> whose kinds are in <paramref name="kinds"/> (the defaults when null).</summary>
    public static IReadOnlyList<FoldCandidate> Candidates(IReadOnlyList<string> lines, IReadOnlySet<FoldKind>? kinds = null)
    {
        kinds ??= DefaultKinds;
        return [.. AllBlocks(lines).Where(c => kinds.Contains(c.Kind))];
    }

    /// <summary>The innermost candidate holding <paramref name="line"/> (one-based), or null (<c>getFoldCandidateForDocumentLine</c>).</summary>
    public static FoldCandidate? At(IReadOnlyList<FoldCandidate> candidates, int line)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        FoldCandidate? best = null;
        foreach (FoldCandidate c in candidates)
        {
            if (c.Contains(line) && (best is not { } b || c.First > b.First || (c.First == b.First && c.Last < b.Last)))
            {
                best = c;
            }
        }

        return best;
    }

    /// <summary>The candidate and every one holding its first line, outermost first (<c>getCandidateLineage</c>).</summary>
    public static IReadOnlyList<FoldCandidate> Lineage(IReadOnlyList<FoldCandidate> candidates, FoldCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return [.. candidates.Where(c => c.Contains(candidate.First)).OrderBy(static c => c.First).ThenByDescending(static c => c.Last)];
    }

    private static FoldKind? KindOf(string keyword) => keyword switch
    {
        "function" => FoldKind.Function,
        "classdef" => FoldKind.Classdef,
        "properties" => FoldKind.Properties,
        "methods" => FoldKind.Methods,
        "events" => FoldKind.Events,
        "enumeration" => FoldKind.Enumeration,
        "for" => FoldKind.For,
        "parfor" => FoldKind.Parfor,
        "while" => FoldKind.While,
        "try" => FoldKind.Try,
        "spmd" => FoldKind.Spmd,
        "if" => FoldKind.If,
        "switch" => FoldKind.Switch,
        "case" or "otherwise" => FoldKind.Case,
        "arguments" => FoldKind.Arguments,
        "blockComment" => FoldKind.BlockComment,
        _ => null,
    };

    /// <summary>Runs of two or more lines that are each a whole-line comment (<c>CommentMTreeNode</c>), a section title or a block comment's line not among them.</summary>
    private static IEnumerable<FoldCandidate> CommentRuns(IReadOnlyList<string> lines)
    {
        var inBlock = new bool[lines.Count];
        foreach (SmartIndent.Block block in SmartIndent.Blocks(lines))
        {
            if (block.Kind == "blockComment")
            {
                for (int i = block.First; i <= block.Last && i < lines.Count; i++)
                {
                    inBlock[i] = true;
                }
            }
        }

        int start = -1;
        for (int i = 0; i <= lines.Count; i++)
        {
            bool comment = i < lines.Count && !inBlock[i] && IsCommentLine(lines[i]) && !IsSectionTitle(lines[i]);
            if (comment)
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else
            {
                if (start >= 0 && i - start >= 2)
                {
                    yield return new FoldCandidate(FoldKind.Comment, start + 1, i);
                }

                start = -1;
            }
        }
    }

    /// <summary>A <c>%%</c> section: from its title to the line before the next, or the text's end (<c>SectionBlockMTreeNode</c>).</summary>
    private static IEnumerable<FoldCandidate> Sections(IReadOnlyList<string> lines)
    {
        int start = -1;
        for (int i = 0; i <= lines.Count; i++)
        {
            if (i == lines.Count || IsSectionTitle(lines[i]))
            {
                if (start >= 0 && i - 1 > start)
                {
                    yield return new FoldCandidate(FoldKind.Section, start + 1, i);
                }

                start = i;
            }
        }
    }

    private static bool IsCommentLine(string line) => line.TrimStart().StartsWith('%');

    private static bool IsSectionTitle(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("%%", StringComparison.Ordinal) && (trimmed.Length == 2 || char.IsWhiteSpace(trimmed[2]));
    }
}
