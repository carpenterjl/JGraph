namespace JGraph.Scripting.Analysis;

/// <summary>A Code Analyzer message's severity, as R2025b's <c>matlab.codeanalysis.IssueSeverity</c> names them.</summary>
public enum CodeSeverity
{
    /// <summary>A suggestion (R2025b's <c>info</c>; <c>checkcode -severity</c> says 0).</summary>
    Info,

    /// <summary>Code that is likely wrong or slow (<c>warning</c>; severity 1).</summary>
    Warning,

    /// <summary>Code that cannot run (<c>error</c>; severity 2).</summary>
    Error,
}

/// <summary>How a message was suppressed, as R2025b's <c>matlab.codeanalysis.IssueSuppression</c> names it.</summary>
public enum CodeSuppression
{
    /// <summary>Not suppressed.</summary>
    No,

    /// <summary>By a <c>%#ok</c> or <c>%#ok&lt;ID&gt;</c> comment on its line.</summary>
    Line,

    /// <summary>By a <c>%#ok&lt;*ID&gt;</c> comment anywhere in the file.</summary>
    File,
}

/// <summary>
/// One edit of a fix: the text between two 1-based columns of one line, inclusive, replaced; an
/// <paramref name="EndColumn"/> one less than <paramref name="StartColumn"/> inserts before the start.
/// </summary>
public sealed record CodeEdit(int Line, int StartColumn, int EndColumn, string Replacement);

/// <summary>A message's automatic fix: one or more edits, applied together (App Designer window plan W10).</summary>
public sealed record CodeFix(IReadOnlyList<CodeEdit> Edits)
{
    /// <summary>A fix of one edit.</summary>
    public static CodeFix Of(int line, int startColumn, int endColumn, string replacement) => new([new CodeEdit(line, startColumn, endColumn, replacement)]);

    /// <summary>A fix that deletes the whole line.</summary>
    public static CodeFix DeleteLine(int line) => new([new CodeEdit(line, 0, 0, string.Empty)]);

    /// <summary>Whether the fix deletes its line whole.</summary>
    public bool DeletesLine => Edits.Count == 1 && Edits[0].StartColumn == 0;
}

/// <summary>
/// One Code Analyzer message (App Designer window plan W10): R2025b's check id, severity and text, the
/// 1-based line and the inclusive 1-based columns it marks, the fix when the check has one, and whether a
/// <c>%#ok</c> comment suppressed it.
/// </summary>
public sealed record CodeMessage(string Id, CodeSeverity Severity, int Line, int StartColumn, int EndColumn, string Text, CodeFix? Fix = null)
{
    /// <summary>How the message was suppressed; <see cref="CodeSuppression.No"/> for one that stands.</summary>
    public CodeSuppression Suppression { get; init; }

    /// <summary>Whether a fix is offered: R2025b's <c>auto</c> fixability.</summary>
    public bool Fixable => Fix is not null;

    /// <summary>The column range as <c>checkcode</c> prints it: <c>C 5</c> for one column, <c>C 5-12</c> for a range.</summary>
    public string Columns => StartColumn == EndColumn ? $"C {StartColumn}" : $"C {StartColumn}-{EndColumn}";

    /// <summary>The order <c>checkcode</c> lists messages in: by line, a function's McCabe line first on its line and its modified one last, then by column.</summary>
    internal int Rank => Id == "CABE" ? 0 : Id == "MCABE" ? 2 : 1;
}

/// <summary>A function's cyclomatic complexity, what <c>checkcode -cyc</c> and <c>-modcyc</c> print.</summary>
/// <param name="Name">The function's name, or the file's stem for a script.</param>
/// <param name="Line">The line of the function's name, or 1 for a script.</param>
/// <param name="StartColumn">The name's first column, or 1 for a script.</param>
/// <param name="EndColumn">The name's last column, or 1 for a script.</param>
/// <param name="McCabe">The McCabe count: one more than the decisions (<c>if</c>, <c>elseif</c>, <c>for</c>, <c>while</c>, each <c>case</c>, <c>catch</c>, <c>&amp;&amp;</c>, <c>||</c>).</param>
/// <param name="Modified">The modified count, where a <c>switch</c> counts once however many cases it has.</param>
/// <param name="IsScript">Whether this is a script's count (<c>SCABE</c>), which names no function.</param>
public sealed record CodeComplexity(string Name, int Line, int StartColumn, int EndColumn, int McCabe, int Modified, bool IsScript)
{
    /// <summary>The <c>-cyc</c> message.</summary>
    public CodeMessage McCabeMessage => IsScript
        ? new CodeMessage("SCABE", CodeSeverity.Info, Line, StartColumn, EndColumn, $"The McCabe cyclomatic complexity is {McCabe}.")
        : new CodeMessage("CABE", CodeSeverity.Info, Line, StartColumn, EndColumn, $"The McCabe cyclomatic complexity of '{Name}' is {McCabe}.");

    /// <summary>The <c>-modcyc</c> message.</summary>
    public CodeMessage ModifiedMessage => IsScript
        ? new CodeMessage("SMCABE", CodeSeverity.Info, Line, StartColumn, EndColumn, $"The modified cyclomatic complexity is {Modified}.")
        : new CodeMessage("MCABE", CodeSeverity.Info, Line, StartColumn, EndColumn, $"The modified cyclomatic complexity of '{Name}' is {Modified}.");
}

/// <summary>What the Code Analyzer found in one file.</summary>
public sealed class CodeAnalysis
{
    internal CodeAnalysis(IReadOnlyList<CodeMessage> messages, IReadOnlyList<CodeMessage> suppressed, IReadOnlyList<CodeComplexity> complexity, bool isScript)
    {
        Messages = messages;
        Suppressed = suppressed;
        Complexity = complexity;
        IsScript = isScript;
    }

    /// <summary>The messages that stand, in <c>checkcode</c>'s order.</summary>
    public IReadOnlyList<CodeMessage> Messages { get; }

    /// <summary>The messages a <c>%#ok</c> comment suppressed, in the same order, each saying how.</summary>
    public IReadOnlyList<CodeMessage> Suppressed { get; }

    /// <summary>Each function's complexity, the script's first when the file is one.</summary>
    public IReadOnlyList<CodeComplexity> Complexity { get; }

    /// <summary>Whether the file is a script (its first statement neither <c>function</c> nor <c>classdef</c>).</summary>
    public bool IsScript { get; }

    /// <summary>The messages with the complexity lines <c>-cyc</c> and <c>-modcyc</c> add, in <c>checkcode</c>'s order.</summary>
    public IReadOnlyList<CodeMessage> With(bool mcCabe, bool modified)
    {
        if (!mcCabe && !modified)
        {
            return Messages;
        }

        var all = new List<CodeMessage>(Messages);
        foreach (CodeComplexity c in Complexity)
        {
            if (mcCabe)
            {
                all.Add(c.McCabeMessage);
            }

            if (modified)
            {
                all.Add(c.ModifiedMessage);
            }
        }

        return Sort(all);
    }

    /// <summary><c>checkcode</c>'s order: by line, then CABE first and MCABE last, then by column.</summary>
    internal static List<CodeMessage> Sort(List<CodeMessage> messages)
    {
        var sorted = messages.Select((m, i) => (m, i)).ToList();
        sorted.Sort((a, b) =>
        {
            int c = a.m.Line.CompareTo(b.m.Line);
            if (c == 0)
            {
                c = a.m.Rank.CompareTo(b.m.Rank);
            }

            if (c == 0)
            {
                c = a.m.StartColumn.CompareTo(b.m.StartColumn);
            }

            return c == 0 ? a.i.CompareTo(b.i) : c;
        });
        return sorted.Select(static p => p.m).ToList();
    }
}
