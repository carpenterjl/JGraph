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

        // The class constructors read a string as the number it spells: double("5") is 5, where
        // the char row it would be demoted to is the code 53.
        "double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64",
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
        (string?[] a, int aRows, int aCols) = ConcatSide(left, line, col);
        (string?[] b, int bRows, int bCols) = ConcatSide(right, line, col);

        // Implicit expansion over both dimensions: ["a" "b"] + ["1"; "2"] is 2-by-2 (measured).
        int rows = Expand("+", aRows, bRows, line, col);
        int cols = Expand("+", aCols, bCols, line, col);
        var joined = new JgsValue[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                string? x = a[(aRows == 1 ? 0 : r) + ((aCols == 1 ? 0 : c) * aRows)];
                string? y = b[(bRows == 1 ? 0 : r) + ((bCols == 1 ? 0 : c) * bRows)];

                // A missing string joined to anything is missing (measured).
                joined[r + (c * rows)] = JgsValue.Str(x is null || y is null ? MissingSentinel : x + y);
            }
        }

        return JgsValue.StringArray(joined, rows, cols);
    }

    /// <summary>
    /// One side of a string <c>+</c>: its texts (null for a missing string) and its shape. A number
    /// is written as <c>string</c> writes it, so "abc" + pi is "abc3.1416"; a cell contributes the
    /// text of each element.
    /// </summary>
    private static (string?[] Texts, int Rows, int Cols) ConcatSide(JgsValue value, int line, int col)
    {
        if (value.IsStringArray)
        {
            return (Array.ConvertAll(value.BoxedElements(), static e => IsMissingText(e.AsString) ? null : e.AsString),
                value.Rows, value.Cols);
        }

        if (value.Type == JgsType.String)
        {
            return ([IsMissingText(value.AsString) ? null : value.AsString], 1, 1);
        }

        if (value.IsCharMatrix)
        {
            // Each row of a char matrix is one string: "abc" + ['ab'; 'cd'] is 2-by-1 (measured).
            string[] rows = value.CharMatrixRows();
            return (rows, rows.Length, 1);
        }

        if (value.Type == JgsType.Cell)
        {
            // Each cell must hold one string's worth: "abc" + {1; 2} is two strings, "abc" + {[1 2]}
            // is refused (measured).
            JgsValue[] cells = value.AsCell;
            var texts = new string?[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                JgsValue cell = cells[i];
                bool scalar = cell.Type is JgsType.String or JgsType.Number or JgsType.Bool or JgsType.Complex
                    || (cell.IsStringArray && cell.ArrayLength == 1);
                if (!scalar)
                {
                    throw new JgsRuntimeException(line, col,
                        $"Conversion from cell failed. Element {i + 1} must be convertible to a string scalar.");
                }

                texts[i] = PieceText(cell, line, col);
            }

            return (texts, value.Rows, value.Cols);
        }

        if (value.Type == JgsType.Array)
        {
            // One complex element makes every element complex: "abc" + [1+2i 3] ends in "3+0i" (measured).
            JgsValue[] elements = value.BoxedElements();
            return (HasComplexElements(value)
                    ? Array.ConvertAll(elements, static e => (string?)ComplexText(e))
                    : Array.ConvertAll(elements, e => PieceText(e, line, col)),
                value.Rows, value.Cols);
        }

        return ([PieceText(value, line, col)], 1, 1);
    }

    /// <summary>
    /// The text a single piece of a concatenation contributes: a number as string() writes it, a
    /// logical as true or false, NaN and the missing string as null. A struct or a function has no
    /// text to give and is refused in MATLAB's words.
    /// </summary>
    private static string? PieceText(JgsValue piece, int line, int col)
    {
        switch (piece.Type)
        {
            case JgsType.String:
                return IsMissingText(piece.AsString) ? null : piece.AsString;
            case JgsType.Number or JgsType.Complex:
            {
                string text = StringElementOf(piece).AsString;
                return IsMissingText(text) ? null : text;
            }

            case JgsType.Bool:
                return piece.AsBool ? "true" : "false";
            case JgsType.Array when piece.IsStringArray && piece.ArrayLength == 1:
                return IsMissingText(piece.ElementAt(0).AsString) ? null : piece.ElementAt(0).AsString;
            default:
                throw new JgsRuntimeException(line, col,
                    $"Conversion to string from {MatlabKindWord(piece)} is not possible.");
        }
    }

    private static string MatlabKindWord(JgsValue value) => value.Type switch
    {
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Function => "function_handle",
        JgsType.Bool => "logical",
        _ => value.TypeName,
    };
}
