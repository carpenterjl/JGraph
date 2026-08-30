using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The string-array type (M63): the handful of names for which the difference between a string and a
/// char row <em>is</em> the answer, and the one pass that tells the other ~2,500 builtins they may
/// keep believing every piece of text is a char row.
/// </summary>
/// <remarks>
/// A string array is a <see cref="JgsType.Array"/> of <see cref="JgsType.String"/> elements carrying
/// <see cref="JgsValue.IsStringArray"/>. There is no new <c>JgsType</c> member, because the
/// representation was already there: <c>["a", "b"]</c> has built an array of strings since the MATLAB
/// dialect landed, and shape, indexing, growth, deletion, reshape, transpose, <c>end</c> and logical
/// masks all already worked on it. A new member would have had to reimplement every one of them.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The builtins that must see a string scalar as the string it is, rather than the char row it
    /// stands for. Everything not on this list is handed the char, which is what let the milestone
    /// flip the meaning of <c>"..."</c> without touching the rest of the surface.
    /// </summary>
    /// <remarks>
    /// Most of these need no code of their own — <c>numel</c>, <c>size</c>, <c>isscalar</c> and the
    /// rest already answer correctly once they can see the 1-by-1 array, because that is genuinely
    /// what a string scalar is. They are on the list to stop the demotion, not to be special-cased.
    /// </remarks>
    internal static readonly HashSet<string> StringAwareBuiltins = new(StringComparer.Ordinal)
    {
        // What kind of thing is this? Demoted, every one of these would answer for the char row.
        "class", "isa", "isstring", "ischar", "isstr",

        // How big is it? A string scalar is one element; the char row it stands for is several, and
        // that is the single question on which the two representations genuinely disagree.
        "numel", "length", "size", "ndims", "isempty", "isscalar", "isvector", "isrow", "iscolumn",
        "height", "width",

        // The one text operation that has to tell a missing string from the text of one.
        "ismissing",

        // repmat answers in whichever container it was handed, so the two representations part
        // company at the answer as well as at the question: repmat('a', 1, 3) is the 1-by-3 char
        // 'aaa', where repmat("a", 1, 3) is three separate strings. Demoted, a string scalar took
        // the char road and came back as one longer piece of text.
        "repmat",
    };

    /// <summary>
    /// Whether <paramref name="value"/> is a string scalar — the 1-by-1 string array a double-quoted
    /// literal means.
    /// </summary>
    internal static bool IsStringScalar(JgsValue value) => value.IsStringArray && value.ArrayLength == 1;

    /// <summary>
    /// Whether <paramref name="value"/> reads as one piece of text: a char row or a string scalar.
    /// This is the question nearly every builtin is really asking when it tests for a string.
    /// </summary>
    internal static bool IsTextScalar(JgsValue value) => value.Type == JgsType.String || IsStringScalar(value);

    /// <summary>The text of a char row or a string scalar; anything else is a caller error.</summary>
    internal static string TextOf(JgsValue value) =>
        value.Type == JgsType.String ? value.AsString : value.ElementAt(0).AsString;

    /// <summary>
    /// The elements of a value read as text, one entry per string: a char row is one, a string array
    /// is its elements, and a cell of char is its cells. Null when the value is none of those, so a
    /// caller can fall through to whatever it does for non-text.
    /// </summary>
    internal static string[]? TextElementsOf(JgsValue value)
    {
        if (value.Type == JgsType.String)
        {
            return [value.AsString];
        }

        if (value.IsStringArray)
        {
            return Array.ConvertAll(value.BoxedElements(), static e => e.AsString);
        }

        // A char matrix is its rows (M105), which is the same reading every other text container gets
        // here: one entry per element of the container, and a char matrix's elements are its rows.
        if (value.IsCharMatrix)
        {
            return value.CharMatrixRows();
        }

        if (value.Type == JgsType.Cell && Array.TrueForAll(value.AsCell, static e => e.Type == JgsType.String))
        {
            return Array.ConvertAll(value.AsCell, static e => e.AsString);
        }

        return null;
    }

    /// <summary>
    /// Registers the string-array surface and marks the string-aware names. Runs last, after every
    /// other define: three of the names below are re-declared by later registrars, and the mark has
    /// to land on whichever wrapper ends up holding the name.
    /// </summary>
    internal static void RegisterStringArrayBuiltins(JgsEnvironment env)
    {
        // strings(n) / strings(r, c): an array of empty strings, the way zeros(n) is one of zeros.
        env.Declare("strings", JgsValue.Function(new BuiltinFunction("strings", (args, line, col) =>
        {
            ArityRange("strings", args, 0, 2, line, col);
            int rows = args.Count == 0 ? 1 : Count("strings", args, 0, line, col);
            int cols = args.Count switch
            {
                0 => 1,
                1 => rows, // strings(n) is n-by-n, exactly as zeros(n) is
                _ => Count("strings", args, 1, line, col),
            };

            if (rows < 0 || cols < 0)
            {
                throw new JgsRuntimeException(line, col, "strings needs non-negative sizes.");
            }

            var empty = new JgsValue[rows * cols];
            Array.Fill(empty, JgsValue.Str(string.Empty));
            return JgsValue.StringArray(empty, rows, cols);
        })));
    }

    /// <summary>
    /// MATLAB's char matrix over several strings: as many rows as strings, as many columns as the
    /// longest, short rows padded with spaces. It is the reason <c>char</c> of a string array is
    /// rarely what a script wants and <c>cellstr</c> usually is.
    /// </summary>
    private static JgsValue PadIntoCharMatrix(string[] texts) => JgsValue.CharMatrix(texts);

    /// <summary>
    /// Gives a value read out of a char matrix its character back (M105). One element is a one-character
    /// char row, a single row is the char row it spells, and anything taller stays a char matrix —
    /// which is exactly MATLAB's own reading, where <c>A(2, :)</c> is a 1-by-n char and <c>A(:, 2)</c>
    /// is an n-by-1 one.
    /// </summary>
    /// <remarks>
    /// Collapsing a one-row result to <see cref="JgsType.String"/> is what keeps the rest of the text
    /// surface working unchanged: <c>A(2, :)</c> comes back as the same char row a literal would have
    /// been, so every builtin that has always taken one goes on taking it.
    /// </remarks>
    internal static JgsValue WrapCharMatrix(JgsValue picked)
    {
        if (picked.Type == JgsType.Number)
        {
            return JgsValue.Str(((char)(int)picked.AsNumber).ToString());
        }

        if (picked.Type != JgsType.Array)
        {
            return picked;
        }

        // Only a genuinely 2-D single row collapses: a 1-by-n-by-m char keeps its dimensions, and
        // reading Rows alone would have flattened it into a char row.
        if (picked.Rows == 1 && picked.Dims.Length <= 2)
        {
            var row = new char[picked.ArrayLength];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = (char)(int)picked.ElementAt(i).AsNumber;
            }

            return JgsValue.Str(new string(row));
        }

        return picked.MarkCharMatrix();
    }

    /// <summary>
    /// The builtins that rearrange a value without changing what its elements are — so a char matrix
    /// going in means a char matrix coming out, exactly as it does in MATLAB.
    /// </summary>
    /// <remarks>
    /// Every one of these already answered the right <em>shape</em> once a char matrix became a real
    /// 2-D array (M105); what each dropped was only the tag, because each mints a fresh wrapper from
    /// the code points. Retrofitting them by name here is the same move M63 made for the
    /// text-kind-preserving verbs, and for the same reason: fifteen call sites across five files
    /// would each have had to remember, and the sixteenth would not have.
    /// </remarks>
    private static readonly string[] CharPreservingBuiltins =
    [
        "sortrows", "fliplr", "flipud", "flip", "flipdim", "rot90", "circshift", "repmat",
        "horzcat", "vertcat", "cat", "sort", "unique", "permute", "ipermute", "squeeze",
        "shiftdim", "triu", "tril", "transpose", "ctranspose",
    ];

    /// <summary>Whether any argument is a char matrix.</summary>
    /// <remarks>
    /// Any argument, and not just the first: <c>cat</c> takes the dimension in front of the values,
    /// so asking <c>args[0]</c> alone would have missed <c>cat(1, A, A)</c>.
    /// </remarks>
    private static bool AnyCharMatrix(IReadOnlyList<JgsValue> args)
    {
        foreach (JgsValue arg in args)
        {
            if (arg.IsCharMatrix)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wraps <see cref="CharPreservingBuiltins"/> so a char matrix argument produces a char answer.
    /// </summary>
    /// <remarks>
    /// Only the first output is re-tagged: <c>sort</c> and <c>unique</c> answer positions in their
    /// second and third, and those are numbers however char the first one is.
    /// </remarks>
    private static void KeepCharMatrixKind(JgsEnvironment env)
    {
        foreach (string name in CharPreservingBuiltins)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                continue;
            }

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                bool wasChar = AnyCharMatrix(args);
                JgsValue answer = inner.Call(args, line, col);
                return wasChar ? WrapCharMatrix(answer) : answer;
            })
            {
                // Every one of the inner builtin's flags is carried, not just the two that looked
                // relevant: a wrapper that forgets one silently changes how the name is called rather
                // than what it answers, which is the hardest kind of regression to see.
                KeepsStringArguments = inner.KeepsStringArguments,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
                {
                    bool wasChar = AnyCharMatrix(args);
                    JgsValue[] outputs = inner.MultiOutput(args, wanted, line, col);
                    if (wasChar && outputs.Length > 0)
                    {
                        outputs[0] = WrapCharMatrix(outputs[0]);
                    }

                    return outputs;
                },
            }));
        }
    }

    // --- Operators ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether <c>+</c> on this pair means string concatenation. MATLAB's rule is the string's, not
    /// the char's: <c>"a" + "b"</c> is <c>"ab"</c> while <c>'a' + 'b'</c> is 195, and the difference
    /// is exactly which of the two was written.
    /// </summary>
    internal static bool ConcatenatesWithPlus(JgsValue left, JgsValue right) =>
        left.IsStringArray || right.IsStringArray;

    /// <summary>
    /// <c>"a" + x</c> elementwise, with a scalar on either side expanding over the other — MATLAB's
    /// implicit expansion, restricted to the one shape combination string concatenation can meet.
    /// </summary>
    internal static JgsValue ConcatenateStrings(JgsValue left, JgsValue right, int line, int col)
    {
        JgsValue[] a = PartsForConcat(left);
        JgsValue[] b = PartsForConcat(right);
        if (a.Length != b.Length && a.Length != 1 && b.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"'+' joins a string array of {a.Length} to one of {b.Length}, which do not line up.");
        }

        int count = System.Math.Max(a.Length, b.Length);
        var joined = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            joined[i] = JgsValue.Str(
                PieceText(a[a.Length == 1 ? 0 : i]) + PieceText(b[b.Length == 1 ? 0 : i]));
        }

        JgsValue result = JgsValue.StringArray(joined);
        result.TakeShapeOf(a.Length >= b.Length ? left : right);
        return result;
    }

    /// <summary>The pieces one side of a <c>+</c> contributes: a string array's elements, or itself.</summary>
    private static JgsValue[] PartsForConcat(JgsValue value) =>
        value.IsStringArray ? value.BoxedElements()
        : value.Type == JgsType.Array ? value.BoxedElements()
        : [value];

    /// <summary>The text a single piece of a concatenation contributes.</summary>
    private static string PieceText(JgsValue piece) =>
        piece.Type == JgsType.String ? piece.AsString : piece.Display();
}
