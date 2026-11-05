using System.Text.RegularExpressions;

namespace JGraph.Scripting.Analysis;

/// <summary>
/// App Designer's coding alerts (App Designer window plan W10): the programming tips its Code View adds over
/// the Code Analyzer's messages, read from R2025b's <c>ProgrammingAlertPlugin</c>, <c>SupportedGraphicsFunctionTip</c>
/// and <c>GraphicsFunctionList.json</c>. A graphics function called in the user's code without an axes - no
/// <c>app.UIAxes</c> as its first argument and no <c>'Parent', app.UIAxes</c> - draws into a figure of its own
/// rather than the app, so the line is marked: "Specify an Axes handle as first argument." or, for a function
/// that takes only a parent, "Specify an Axes as the value for 'Parent'." The search is R2025b's, line by line
/// over the editable lines with comments stripped and continuations joined: a name that is in a string, in a
/// command, in a <c>function</c> line, after a dot, or called with <c>app</c> itself is no call; one alert per line.
/// </summary>
public static class DesignCodingAlerts
{
    /// <summary>The tip for a function whose first argument may be an axes.</summary>
    public const string MissingAxesHandle = "Specify an Axes handle as first argument.";

    /// <summary>The tip for a function that takes an axes only as its <c>Parent</c>.</summary>
    public const string MissingAxesParent = "Specify an Axes as the value for 'Parent'.";

    /// <summary>The check box R2025b puts under the tip.</summary>
    public const string DisableText = "Disable coding alerts";

    /// <summary>The id the alerts carry in a message list (R2025b's have none; this one is JGraph's name for them).</summary>
    public const string Id = "APPAXES";

    private static readonly IReadOnlyDictionary<string, (bool FirstArg, bool Parent)> Functions = new Dictionary<string, (bool, bool)>(StringComparer.Ordinal)
    {
        ["alim"] = (true, false), ["alphamap"] = (true, false), ["animatedline"] = (true, true), ["area"] = (true, true), ["bar"] = (true, true),
        ["barh"] = (true, true), ["box"] = (true, false), ["camdolly"] = (true, false), ["camlookat"] = (true, false), ["camorbit"] = (true, false),
        ["campan"] = (true, false), ["campos"] = (true, false), ["camproj"] = (true, false), ["camroll"] = (true, false), ["camtarget"] = (true, false),
        ["camup"] = (true, false), ["camva"] = (true, false), ["camzoom"] = (true, false), ["colorbar"] = (true, false), ["colormap"] = (true, false),
        ["comet"] = (true, false), ["comet3"] = (true, true), ["contour"] = (true, true), ["contourf"] = (true, true), ["errorbar"] = (true, true),
        ["fcontour"] = (true, true), ["feather"] = (true, true), ["fill"] = (true, true), ["fimplicit"] = (true, true), ["fplot"] = (true, true),
        ["grid"] = (true, false), ["hggroup"] = (true, true), ["hgtransform"] = (true, true), ["histogram"] = (true, true), ["hold"] = (true, false),
        ["image"] = (true, true), ["imagesc"] = (true, true), ["imshow"] = (false, true), ["legend"] = (true, false), ["light"] = (true, true),
        ["line"] = (true, true), ["loglog"] = (true, true), ["meshc"] = (true, true), ["patch"] = (true, true), ["pcolor"] = (true, true),
        ["pie"] = (true, true), ["plot"] = (true, true), ["plot3"] = (true, true), ["quiver"] = (true, true), ["rectangle"] = (true, true),
        ["rotate3d"] = (true, false), ["scatter"] = (true, true), ["semilogx"] = (true, true), ["semilogy"] = (true, true), ["stairs"] = (true, true),
        ["stem"] = (true, true), ["surf"] = (true, true), ["surfc"] = (true, true), ["surfl"] = (true, true), ["text"] = (true, true),
        ["title"] = (true, true), ["triplot"] = (false, true), ["voronoi"] = (true, true), ["xlabel"] = (true, true), ["xlim"] = (true, false),
        ["ylabel"] = (true, true), ["ylim"] = (true, false), ["yyaxis"] = (true, false), ["zlabel"] = (true, true), ["zlim"] = (true, false),
    };

    private static readonly Regex Word = new("\\b(" + string.Join("|", Functions.Keys) + ")\\b", RegexOptions.Compiled);

    /// <summary>The graphics functions the tips watch for.</summary>
    public static IReadOnlyCollection<string> WatchedFunctions => Functions.Keys.ToList();

    /// <summary>
    /// The alerts over <paramref name="lines"/>: one per line at most, as a warning without a check id, on the lines
    /// in <paramref name="editableLines"/> (1-based; null for every line). <paramref name="axesProperties"/> are the
    /// app's properties that hold an axes (<c>UIAxes</c>, ...), which satisfy the tip when passed.
    /// </summary>
    public static IReadOnlyList<CodeMessage> Find(IReadOnlyList<string> lines, IReadOnlySet<int>? editableLines, IReadOnlyCollection<string> axesProperties)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(axesProperties);
        List<string> content = StripComments(lines);
        var alerts = new List<CodeMessage>();
        for (int i = 0; i < content.Count; i++)
        {
            int number = i + 1;
            if (editableLines is not null && !editableLines.Contains(number))
            {
                continue;
            }

            string joined = AppendContinuation(i, content);
            CodeMessage? best = null;
            foreach (Match match in Word.Matches(joined))
            {
                string name = match.Value;
                int column = match.Index + 1;
                string prefix = joined[..match.Index];
                string suffix = joined[(match.Index + name.Length)..];
                if (InString(joined, match.Index, name.Length) || InCommand(prefix) || prefix.Contains("function") || HasAppArgument(suffix))
                {
                    continue;
                }

                bool parens = suffix.TrimStart().StartsWith('(') && suffix.Contains(')');
                bool dotted = Regex.IsMatch(prefix, "\\.\\s*$");
                if (!parens || dotted)
                {
                    continue;
                }

                (bool firstArg, bool parent) = Functions[name];
                if (AxesGiven(suffix, firstArg, parent, axesProperties))
                {
                    continue;
                }

                string text = firstArg ? MissingAxesHandle : MissingAxesParent;
                var alert = new CodeMessage(Id, CodeSeverity.Warning, number, column, column + name.Length - 1, text);
                if (best is null || alert.StartColumn < best.StartColumn)
                {
                    best = alert;
                }
            }

            if (best is not null)
            {
                alerts.Add(best);
            }
        }

        return alerts;
    }

    private static bool AxesGiven(string suffix, bool firstArg, bool parent, IReadOnlyCollection<string> axesProperties)
    {
        string packed = suffix.Replace(" ", string.Empty);
        foreach (string property in axesProperties)
        {
            string escaped = Regex.Escape(property);
            if (firstArg && Regex.IsMatch(packed, "^\\((app|handles)\\." + escaped))
            {
                return true;
            }

            if (parent && Regex.IsMatch(packed, "[Pp][Aa][Rr][Ee][Nn][Tt](['\"],|=)(app|handles)\\." + escaped))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAppArgument(string suffix) => Regex.IsMatch(suffix, "\\(\\s*app(?![\\w|\\.])");

    /// <summary>R2025b's test for a name inside a command: the text before it starts with an identifier not followed by <c>=</c>.</summary>
    private static bool InCommand(string prefix)
    {
        string[] words = prefix.Trim().Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        string first = words.Length > 0 ? words[0] : string.Empty;
        string second = words.Length > 1 ? words[1] : string.Empty;
        bool identifier = first.Length > 0 && Regex.IsMatch(first, "^[a-z][a-z0-9_]*$", RegexOptions.IgnoreCase);
        bool assigned = second.Length > 0 && second[0] == '=';
        return identifier && !assigned;
    }

    /// <summary>Whether the name at <paramref name="index"/> is inside a quoted string (R2025b blanks strings and compares).</summary>
    private static bool InString(string line, int index, int length)
    {
        char? quote = null;
        for (int i = 0; i < index + length && i < line.Length; i++)
        {
            char c = line[i];
            if (c is '\'' or '"')
            {
                if (quote == c)
                {
                    quote = null;
                }
                else if (quote is null)
                {
                    quote = c;
                }
            }

            if (i >= index && quote is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static string AppendContinuation(int index, List<string> content)
    {
        string text = content[index];
        int next = index + 1;
        int at;
        while ((at = text.IndexOf("...", StringComparison.Ordinal)) >= 0 && next < content.Count)
        {
            text = text[..at] + content[next];
            next++;
        }

        return text;
    }

    private static List<string> StripComments(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        bool inBlock = false;
        foreach (string line in lines)
        {
            string content = string.Empty;
            if (Regex.IsMatch(line, "^\\s*%}"))
            {
                inBlock = false;
            }
            else if (Regex.IsMatch(line, "^\\s*%{") || inBlock)
            {
                inBlock = true;
            }
            else
            {
                int percent = line.IndexOf('%');
                content = percent >= 0 ? line[..percent] : line;
            }

            result.Add(content);
        }

        return result;
    }
}
