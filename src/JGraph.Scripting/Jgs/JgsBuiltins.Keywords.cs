using System;
using System.Collections.Generic;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>iskeyword</c> (open item 329, ADR 0368): R2025b's <c>lang/iskeyword.m</c>, which holds the
/// twenty words a variable may not be named (<c>MatlabKeywordList</c>, in its order), as a cell column, and answers
/// <c>any(strcmp(s, list))</c> for a text, false whenever that <c>strcmp</c> refuses.
/// <c>true</c> and <c>false</c> are not among them; they are functions a workspace may bind (ADR 0363).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>iskeyword</c> with no argument is the list; with one, whether it names a keyword: a char row,
    /// a string scalar or a one-element cell holding a char row is compared whole and exactly; a cell
    /// or string array of the list's own size (20-by-1) element by element, as <c>strcmp</c> pairs
    /// them; anything else is false, where R2025b's <c>strcmp</c> answers false or refuses and
    /// <c>iskeyword</c> catches the refusal (measured).
    /// </summary>
    private static JgsValue IsKeyword(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (args.Count == 0)
        {
            return CellColumn((IEnumerable<string>)MatlabKeywordList);
        }

        JgsValue s = args[0];
        if (IsTextScalar(s))
        {
            return JgsValue.Bool(!IsMissingText(TextOf(s)) && Array.IndexOf(MatlabKeywordList, TextOf(s)) >= 0);
        }

        string?[]? pieces = s.Type == JgsType.Cell
            ? Array.ConvertAll(s.AsCell, static e => e.Type == JgsType.String ? e.AsString : null)
            : s.IsStringArray
                ? Array.ConvertAll(JgsTextKernels.Texts(s.BoxedElements()), static t => IsMissingText(t) ? null : t)
                : null;
        if (pieces is null)
        {
            return JgsValue.Bool(false);
        }

        if (pieces.Length == 1)
        {
            return JgsValue.Bool(pieces[0] is { } one && Array.IndexOf(MatlabKeywordList, one) >= 0);
        }

        // strcmp pairs two arrays of one size element by element and refuses any other pairing.
        if (s.Dims.Length == 2 && s.Rows == MatlabKeywordList.Length && s.Cols == 1)
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                if (string.Equals(pieces[i], MatlabKeywordList[i], StringComparison.Ordinal))
                {
                    return JgsValue.Bool(true);
                }
            }
        }

        return JgsValue.Bool(false);
    }
}
