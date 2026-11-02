using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Editing;

/// <summary>What a search looks for and from where (R2025b's <c>FindConfiguration</c> as <c>FindUtilities._getConfiguration</c> fills it).</summary>
public sealed record SearchOptions
{
    /// <summary>The text looked for, or the regular expression when <see cref="Regex"/>.</summary>
    public required string Text { get; init; }

    /// <summary>Match case.</summary>
    public bool MatchCase { get; init; }

    /// <summary>Whole word.</summary>
    public bool WholeWord { get; init; }

    /// <summary>Whether <see cref="Text"/> is a regular expression.</summary>
    public bool Regex { get; init; }

    /// <summary>Find Previous rather than Find Next.</summary>
    public bool Backward { get; init; }

    /// <summary>The line the search starts from, zero-based.</summary>
    public int StartLine { get; init; }

    /// <summary>The column the search starts from, zero-based.</summary>
    public int StartColumn { get; init; }

    /// <summary>The first line of Find in selection, zero-based, or -1 for the whole text.</summary>
    public int SelectionFirstLine { get; init; } = -1;

    /// <summary>The last line of Find in selection, zero-based, or -1.</summary>
    public int SelectionLastLine { get; init; } = -1;

    /// <summary>Whether the search is held to the selection's lines.</summary>
    public bool InSelection => SelectionFirstLine >= 0;
}

/// <summary>One occurrence: its zero-based line and columns, and for a regular expression where its scan began (<c>searchIndex</c>), else -1.</summary>
public readonly record struct SearchMatch(int Line, int StartColumn, int EndColumn, int SearchIndex);

/// <summary>A search's outcome (R2025b's <c>FindResult</c>): every occurrence, the current one, and what was looked for, so a later search can go on from it.</summary>
public sealed class SearchResult
{
    internal SearchResult(IReadOnlyList<SearchMatch> matches, SearchOptions options, bool effectiveRegex, int currentIndex)
    {
        Matches = matches;
        Options = options;
        EffectiveRegex = effectiveRegex;
        CurrentIndex = currentIndex;
    }

    /// <summary>Every occurrence, in text order.</summary>
    public IReadOnlyList<SearchMatch> Matches { get; }

    /// <summary>What was looked for.</summary>
    public SearchOptions Options { get; }

    /// <summary>Whether the search ran as a regular expression, which Whole word makes of plain text (<c>_getConfiguration</c>).</summary>
    public bool EffectiveRegex { get; }

    /// <summary>The current occurrence's place in <see cref="Matches"/>; <see cref="TextSearch.NoResult"/> with none, <see cref="TextSearch.CountOnly"/> when nothing lies ahead and the search does not wrap.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>The current occurrence, or null.</summary>
    public SearchMatch? Current => CurrentIndex >= 0 && CurrentIndex < Matches.Count ? Matches[CurrentIndex] : null;

    /// <summary>Whether an occurrence is current.</summary>
    public bool HasCurrent => Current is not null;

    /// <summary>Forgets the current occurrence (the caret moved by itself): the next search starts from the caret.</summary>
    public void ClearCurrent() => CurrentIndex = TextSearch.NoResult;

    /// <summary>Whether <paramref name="options"/> look for the same thing (<c>FindResult.findOptionsMatched</c>): the text, compared without case when neither matches case, and every option.</summary>
    public bool MatchesOptions(SearchOptions options, bool effectiveRegex)
    {
        ArgumentNullException.ThrowIfNull(options);
        string mine = Options.Text;
        string theirs = options.Text;
        if (!Options.MatchCase && Options.MatchCase == options.MatchCase)
        {
            mine = mine.ToLowerInvariant();
            theirs = theirs.ToLowerInvariant();
        }

        return mine == theirs && Options.MatchCase == options.MatchCase && Options.WholeWord == options.WholeWord && EffectiveRegex == effectiveRegex
            && Options.InSelection == options.InSelection
            && (!options.InSelection || (Options.SelectionFirstLine == options.SelectionFirstLine && Options.SelectionLastLine == options.SelectionLastLine));
    }
}

/// <summary>
/// The editor's Find and Replace, free of a window (App Designer window plan W9a): what R2025b's <c>rtc/plugins/find/FindUtilities</c> finds in
/// a text's lines - plain, without case, whole words by its list of word-breaking characters, or a regular expression - where it goes from the
/// caret, how it wraps, how it steps on from the last occurrence (<c>_getNextOccurrenceFromCache</c>), and what a regular expression's
/// replacement writes with its groups (<c>FindReplaceActions._processCaptureGroupsInRegexReplaceText</c>).
/// </summary>
public static class TextSearch
{
    /// <summary>No occurrence is current (<c>NO_RESULT_FOUND</c>).</summary>
    public const int NoResult = -1;

    /// <summary>Occurrences exist but none lies ahead and the search does not wrap (<c>SHOW_ONLY_COUNT</c>): the count is shown alone.</summary>
    public const int CountOnly = -2;

    /// <summary>How many occurrences the result label counts before saying "+" (<c>FindPreference._findDeadEnd</c>).</summary>
    public const int DeadEnd = 19999;

    /// <summary>The dialog's message for a regular expression that cannot be compiled (<c>findDialog.regularExpressionError</c>).</summary>
    public const string InvalidRegexMessage = "Invalid regular expression";

    /// <summary>The dialog's message for a regular expression that asks for a line break (<c>findDialog.regularExpressionNewLineError</c>).</summary>
    public const string NewLineRegexMessage = "Multiline search, including the use of the control characters ‘\\n’ and ‘\\r’, is not supported.";

    /// <summary>The characters that break a word for Whole word, beside white space (<c>FindUtilities._SPECIAL_CHARS</c>).</summary>
    public static IReadOnlyList<char> SpecialChars { get; } = ['!', '(', ')', '[', ']', '{', '}', '-', '+', '*', '=', '.', '?', '#', '$', '/', ':', ';', '%', '\'', '&', '<', '>', '^', '@', '\\', '~', ',', '"', '|', '`'];

    private static readonly string Specials = string.Concat(SpecialChars.Select(static c => "\\" + c));
    private static readonly string SpecialClass = "[\\s" + Specials + "]";
    private static readonly Regex HasSpecial = new(SpecialClass, RegexOptions.CultureInvariant);
    private static readonly Regex AsksNewLine = new(@"\\r|\\n|\\r\\n", RegexOptions.CultureInvariant);

    /// <summary>Why <paramref name="text"/> cannot be a regular expression (<c>FindReplaceRegexHandler._getRegexErrorObject</c>), or null when it can.</summary>
    public static string? WhyNotRegex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (AsksNewLine.IsMatch(text))
        {
            return NewLineRegexMessage;
        }

        try
        {
            _ = new Regex(text, RegexOptions.CultureInvariant);
            return null;
        }
        catch (ArgumentException)
        {
            return InvalidRegexMessage;
        }
    }

    /// <summary>
    /// Finds <paramref name="options"/> in <paramref name="lines"/> (<c>FindUtilities.find</c>): every occurrence and the first at or after the
    /// start, going round when <paramref name="wrapAround"/>. Given the last search's <paramref name="cached"/> result over the same thing, steps
    /// on from its current occurrence, or reuses its occurrences when it has none.
    /// </summary>
    public static SearchResult Find(IReadOnlyList<string> lines, SearchOptions options, bool wrapAround, SearchResult? cached = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);
        (string searchFor, bool regex) = SearchFor(options);
        if (cached is not null && !cached.MatchesOptions(options, regex))
        {
            cached = null;
        }

        if (cached is { HasCurrent: true })
        {
            return Step(cached, options.Backward, wrapAround);
        }

        IReadOnlyList<SearchMatch> matches = cached?.Matches ?? FindAll(lines, options, searchFor, regex);
        int current = NextIndex(matches, options.StartLine, options.StartColumn, options.Backward, wrapAround);
        return new SearchResult(matches, options, regex, current);
    }

    /// <summary>Every occurrence in <paramref name="lines"/>, in text order (<c>_findAll</c>).</summary>
    public static IReadOnlyList<SearchMatch> FindAll(IReadOnlyList<string> lines, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);
        (string searchFor, bool regex) = SearchFor(options);
        return FindAll(lines, options, searchFor, regex);
    }

    /// <summary>The occurrence after (or before) the last current one (<c>_getNextOccurrenceFromCache</c>): round when <paramref name="wrapAround"/>, else held at the end.</summary>
    public static SearchResult Step(SearchResult from, bool backward, bool wrapAround)
    {
        ArgumentNullException.ThrowIfNull(from);
        int count = from.Matches.Count;
        int index = from.CurrentIndex;
        if (count == 0 || index < 0)
        {
            return new SearchResult(from.Matches, from.Options with { Backward = backward }, from.EffectiveRegex, count == 0 ? NoResult : from.CurrentIndex);
        }

        int next;
        if (!backward)
        {
            int round = (index + 1) % count;
            next = wrapAround ? round : index + 1 == count ? index : round;
        }
        else
        {
            int round = (index + count - 1) % count;
            next = wrapAround ? round : index == 0 ? index : round;
        }

        return new SearchResult(from.Matches, from.Options with { Backward = backward }, from.EffectiveRegex, next);
    }

    /// <summary>
    /// The place among <paramref name="matches"/> of the first at or after the start (<c>_getNextIndexForPosition</c>), or of the last before it
    /// going backward; the first or last when none and the search wraps; <see cref="CountOnly"/> when none and it does not; <see cref="NoResult"/> with no matches.
    /// </summary>
    public static int NextIndex(IReadOnlyList<SearchMatch> matches, int line, int column, bool backward, bool wrapAround)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count == 0)
        {
            return NoResult;
        }

        int found = -1;
        if (!backward)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Line > line || (matches[i].Line == line && matches[i].StartColumn >= column))
                {
                    found = i;
                    break;
                }
            }
        }
        else
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].Line < line || (matches[i].Line == line && matches[i].StartColumn < column))
                {
                    found = i;
                    break;
                }
            }
        }

        if (found >= 0)
        {
            return found;
        }

        return wrapAround ? (backward ? matches.Count - 1 : 0) : CountOnly;
    }

    /// <summary>
    /// What replaces the occurrence <paramref name="match"/> of the regular expression <paramref name="pattern"/> on <paramref name="lineText"/>
    /// (<c>_processCaptureGroupsInRegexReplaceText</c>): <paramref name="replaceWith"/> with its groups filled in as JavaScript fills them - <c>$1</c>,
    /// <c>$&amp;</c>, <c>$$</c>, a group that does not exist left as written - and a doubled backslash made one.
    /// </summary>
    public static string Replacement(string lineText, SearchMatch match, string pattern, bool matchCase, string replaceWith)
    {
        ArgumentNullException.ThrowIfNull(lineText);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replaceWith);
        var regex = new Regex(pattern, (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant);
        int searchIndex = Math.Clamp(match.SearchIndex < 0 ? match.StartColumn : match.SearchIndex, 0, lineText.Length);
        int tail = lineText.Length - match.EndColumn;
        Match first = regex.Match(lineText[searchIndex..]);
        if (first.Success && first.Length == 0)
        {
            while (searchIndex <= match.StartColumn && first.Success && first.Length == 0)
            {
                searchIndex++;
                first = regex.Match(lineText[Math.Min(searchIndex, lineText.Length)..]);
            }
        }

        if (!first.Success)
        {
            return replaceWith;
        }

        string rest = lineText[searchIndex..];
        string replacement = JavaScriptReplacement(replaceWith.Replace("\\\\", "\\", StringComparison.Ordinal), regex);
        string replaced = regex.Replace(rest, replacement, 1);
        int start = Math.Clamp(match.StartColumn - searchIndex, 0, replaced.Length);
        int end = Math.Clamp(replaced.Length - tail, start, replaced.Length);
        return replaced[start..end];
    }

    /// <summary>The result label (<c>FindStrip.updateMatchResults</c>): the current occurrence's number and the count, "?" for no current one, "19999+" past the dead end.</summary>
    public static string Label(int currentIndex, int count)
    {
        if (count <= 0)
        {
            return "0 / 0";
        }

        string total = count >= DeadEnd ? DeadEnd + "+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int number = currentIndex + 1;
        string current = number <= 0 || number > DeadEnd ? "?" : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return current + " / " + total;
    }

    /// <summary>The seed a selection gives the find box (<c>FindPreference.getFirstNonBlankText</c>): its first line that is not empty, as it is, or "".</summary>
    public static string FirstNonBlankLine(string? selected)
    {
        if (string.IsNullOrEmpty(selected))
        {
            return string.Empty;
        }

        string[] lines = selected.Split('\n');
        int first = 0;
        while (lines.Length - first > 1 && lines[first].Length == 0)
        {
            first++;
        }

        return lines[first];
    }

    /// <summary>Whether <paramref name="c"/> breaks a word: white space or one of <see cref="SpecialChars"/>.</summary>
    public static bool BreaksWord(char c) => c == ' ' || SpecialChars.Contains(c);

    private static (string SearchFor, bool Regex) SearchFor(SearchOptions options)
    {
        string text = options.Text;
        bool special = HasSpecial.IsMatch(text);
        if ((options.WholeWord && !special) || (options.Regex && options.WholeWord))
        {
            return ("(?:^|\\s|[" + Specials + "])" + text + "(?=$|\\s|[" + Specials + "])", true);
        }

        return (text, options.Regex);
    }

    private static IReadOnlyList<SearchMatch> FindAll(IReadOnlyList<string> lines, SearchOptions options, string searchFor, bool regex)
    {
        int first = 0;
        int last = lines.Count - 1;
        if (options.InSelection)
        {
            first = options.SelectionFirstLine;
            last = options.SelectionLastLine;
        }

        var found = new List<SearchMatch>();
        Regex? compiled = null;
        if (regex && options.Text.Length > 0)
        {
            compiled = new Regex(searchFor, (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant);
        }

        Regex? boundary = regex && options.WholeWord ? new Regex("^" + options.Text, (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant) : null;
        for (int line = first; line <= last && line < lines.Count; line++)
        {
            if (line < 0)
            {
                continue;
            }

            if (regex)
            {
                if (compiled is not null)
                {
                    InLineByRegex(lines[line], line, compiled, boundary, found);
                }
            }
            else
            {
                InLineByText(lines[line], line, searchFor, options, found);
            }
        }

        return found;
    }

    /// <summary><c>_findAllInLineByText</c>: the needle without case unless Match case, one hit after another, each checked for Whole word by what stands beside it.</summary>
    private static void InLineByText(string lineText, int line, string needle, SearchOptions options, List<SearchMatch> found)
    {
        if (needle.Length == 0)
        {
            return;
        }

        string text = lineText;
        if (!options.MatchCase)
        {
            text = text.ToLowerInvariant();
            needle = needle.ToLowerInvariant();
        }

        int at = 0;
        while (at != -1)
        {
            at = text.IndexOf(needle, at, StringComparison.Ordinal);
            if (at == -1)
            {
                break;
            }

            if (!options.WholeWord || (Adjacent(text, needle, 0, at - 1) && Adjacent(text, needle, needle.Length - 1, at + needle.Length)))
            {
                found.Add(new SearchMatch(line, at, at + needle.Length, -1));
            }

            at += needle.Length;
        }
    }

    /// <summary><c>_checkAdjacentForWholeWord</c>: the needle's end character itself breaks a word, or the character beside it in the text does, or there is none.</summary>
    private static bool Adjacent(string text, string needle, int needleAt, int textAt)
    {
        char end = needle[needleAt];
        if (BreaksWord(end))
        {
            return true;
        }

        if (textAt < 0 || textAt >= text.Length)
        {
            return true;
        }

        return BreaksWord(text[textAt]);
    }

    /// <summary><c>_findAllInLineByRegExp</c>: each match in turn, an empty one passed over, a Whole word match losing the word-breaking character it took before the word.</summary>
    private static void InLineByRegex(string lineText, int line, Regex regex, Regex? boundary, List<SearchMatch> found)
    {
        int length = lineText.Length;
        int lastIndex = 0;
        int searchIndex = 0;
        int at = 0;
        while (at < length)
        {
            if (lastIndex > length)
            {
                return;
            }

            Match match = regex.Match(lineText, lastIndex);
            if (!match.Success)
            {
                return;
            }

            if (match.Length == 0)
            {
                lastIndex = match.Index + 1;
                continue;
            }

            if (match.Index >= length)
            {
                return;
            }

            int start = match.Index;
            int end = match.Index + match.Length;
            int index = start;
            if (boundary is not null && !boundary.IsMatch(match.Value))
            {
                start++;
            }

            found.Add(new SearchMatch(line, start, end, searchIndex));
            lastIndex = end;
            searchIndex = lastIndex;
            at = index + 1;
        }
    }

    /// <summary>A JavaScript replacement string written as .NET reads it: <c>$n</c> for a group that exists, <c>$&amp;</c>, <c>$$</c>, the rest literal.</summary>
    internal static string JavaScriptReplacement(string text, Regex regex)
    {
        int groups = regex.GetGroupNumbers().Max();
        string[] names = regex.GetGroupNames();
        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '$' || i + 1 >= text.Length)
            {
                sb.Append(c == '$' ? "$$" : c.ToString());
                continue;
            }

            char next = text[i + 1];
            if (next is '$' or '&' or '`' or '\'')
            {
                sb.Append('$').Append(next);
                i++;
                continue;
            }

            if (next == '<' && text.IndexOf('>', i + 2) is int close and > 0)
            {
                string name = text[(i + 2)..close];
                if (names.Contains(name, StringComparer.Ordinal) && !int.TryParse(name, out _))
                {
                    sb.Append("${").Append(name).Append('}');
                    i = close;
                    continue;
                }

                sb.Append("$$");
                continue;
            }

            if (char.IsAsciiDigit(next))
            {
                if (i + 2 < text.Length && char.IsAsciiDigit(text[i + 2]))
                {
                    int two = (next - '0') * 10 + (text[i + 2] - '0');
                    if (two > 0 && two <= groups)
                    {
                        sb.Append("${").Append(two).Append('}');
                        i += 2;
                        continue;
                    }
                }

                int one = next - '0';
                if (one > 0 && one <= groups)
                {
                    sb.Append("${").Append(one).Append('}');
                    i++;
                    continue;
                }
            }

            sb.Append("$$");
        }

        return sb.ToString();
    }
}
