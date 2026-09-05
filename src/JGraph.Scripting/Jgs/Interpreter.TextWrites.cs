// SPDX-License-Identifier: MIT
// Copyright (c) JGraph contributors

using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Indexed writes into text and cells: <c>s(k) = "c"</c> on a string array, <c>c(k) = 'x'</c> on a
/// char row, and <c>c(k) = {v}</c> on a cell. The three share one shape — read the subscripts, grow or
/// delete, write — and differ in what a right-hand side may be and what a grown slot is filled with.
/// </summary>
/// <remarks>
/// A string array stores one <see cref="JgsType.String"/> element per string. The plain array write
/// stored whatever it was handed, so <c>x(2) = "c"</c> — whose right-hand side is itself a 1-by-1
/// string array — put an array inside an array; the display hid it, <c>isequal</c> said false, and
/// <c>strlength</c> died on the cast (audit 6.1). The reconciliation below hands the array write a
/// plain element in every case MATLAB accepts, and MATLAB's own refusal otherwise.
///
/// The rules here were measured in MATLAB R2025b, not inferred: a number written into a string array
/// is spelled as <c>string</c> spells it and NaN is the missing string; a string written into a
/// double array is read as <c>str2double</c> reads it, and into an integer, single or logical array
/// is refused; a string written into a char row spreads its characters; a slot a string array grows
/// into is the missing string, a char row's is <c>char(0)</c>, and a cell's is <c>[]</c>.
/// </remarks>
internal sealed partial class Interpreter
{
    private const string CountMismatch =
        "Unable to perform assignment because the left and right sides have a different number of elements.";

    /// <summary>
    /// The value an <c>A(...) = rhs</c> write actually stores when either side is a string array: the
    /// element of a 1-by-1 string array rather than the array, a number spelled as text for a string
    /// array target, a text read as a number for a double target, and MATLAB's refusal for the
    /// pairs it does not convert. Anything else passes through unchanged.
    /// </summary>
    private JgsValue ReconcileWrittenValue(JgsValue target, JgsValue rhs, Node at)
    {
        // [] on the right is a deletion for every target; the write paths handle it themselves.
        if (rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray)
        {
            return rhs;
        }

        if (target.IsStringArray)
        {
            return StringElementsFor(rhs, at);
        }

        if (!rhs.IsStringArray)
        {
            return Dialect.IsMatlab ? IntoNumericArray(target, rhs, at) : rhs;
        }

        // A string written into a char matrix is its characters (c(1, 2) = "x" is measured to work).
        if (target.IsCharMatrix)
        {
            string chars = string.Concat(Array.ConvertAll(rhs.BoxedElements(), static e => e.AsString));
            return chars.Length == 1
                ? JgsValue.Number(chars[0])
                : JgsValue.Array(Array.ConvertAll(chars.ToCharArray(), static ch => JgsValue.Number(ch)));
        }

        if (JgsBuiltins.IsLogicalValue(target))
        {
            throw NotConvertible("string", "logical", at);
        }

        if (target.NumericClass != JgsNumericClass.Double)
        {
            throw NotConvertible("string", target.NumericClass.MatlabName(), at);
        }

        // Into a double array a string is read as a number: n(2) = "5" stores 5, n(2) = "a" NaN
        // (measured). A missing string spells nothing and is NaN too.
        JgsValue[] numbers = Array.ConvertAll(rhs.BoxedElements(), static e =>
            JgsBuiltins.IsMissingText(e.AsString) ? JgsValue.Number(double.NaN) : JgsBuiltins.NumberSpelledBy(e.AsString));
        if (numbers.Length == 1)
        {
            return numbers[0];
        }

        JgsValue shaped = JgsValue.Array(numbers);
        shaped.Reshape(rhs.Rows, rhs.Cols);
        return shaped;
    }

    /// <summary>
    /// A char row written into a numeric or char array is its character codes (n(2) = '5' stores 53,
    /// measured), and a cell is refused in MATLAB's words. Everything else is stored as it is.
    /// </summary>
    private static JgsValue IntoNumericArray(JgsValue target, JgsValue rhs, Node at)
    {
        if (rhs.Type == JgsType.String)
        {
            string chars = rhs.AsString;
            return chars.Length == 1
                ? JgsValue.Number(chars[0])
                : JgsValue.Array(Array.ConvertAll(chars.ToCharArray(), static ch => JgsValue.Number(ch)));
        }

        if (rhs.Type == JgsType.Cell)
        {
            string kind = JgsBuiltins.IsLogicalValue(target) ? "logical"
                : target.IsCharMatrix ? "char"
                : target.NumericClass.MatlabName();
            throw new JgsRuntimeException(at.Line, at.Column, $"Conversion to {kind} from cell is not possible.");
        }

        return rhs;
    }

    /// <summary>
    /// What a right-hand side contributes to a string array: one string element when it stands for one
    /// string, an array of string elements in its own shape otherwise.
    /// </summary>
    private static JgsValue StringElementsFor(JgsValue rhs, Node at)
    {
        if (rhs.IsStringArray)
        {
            return rhs.ArrayLength == 1 ? rhs.ElementAt(0) : rhs;
        }

        switch (rhs.Type)
        {
            case JgsType.String:
                return rhs; // a char row is one string, the missing sentinel included

            case JgsType.Number or JgsType.Bool or JgsType.Complex:
                return TextElementOf(rhs);

            case JgsType.Array when rhs.IsCharMatrix:
            {
                string[] rows = rhs.CharMatrixRows();
                return rows.Length == 1
                    ? JgsValue.Str(rows[0])
                    : JgsValue.StringArray(Array.ConvertAll(rows, JgsValue.Str), rows.Length, 1);
            }

            case JgsType.Array:
            {
                JgsValue[] texts = Array.ConvertAll(rhs.BoxedElements(), TextElementOf);
                return texts.Length == 1 ? texts[0] : JgsValue.StringArray(texts, rhs.Rows, rhs.Cols);
            }

            case JgsType.Cell:
            {
                JgsValue[] pieces = rhs.AsCell;
                var texts = new JgsValue[pieces.Length];
                for (int i = 0; i < pieces.Length; i++)
                {
                    JgsValue piece = pieces[i];
                    bool scalar = piece.Type is JgsType.String or JgsType.Number or JgsType.Bool or JgsType.Complex
                        || (piece.IsStringArray && piece.ArrayLength == 1);
                    if (!scalar)
                    {
                        throw new JgsRuntimeException(at.Line, at.Column,
                            $"Conversion from cell failed. Element {i + 1} must be convertible to a string scalar.");
                    }

                    texts[i] = piece.IsStringArray ? piece.ElementAt(0) : piece.Type == JgsType.String ? piece : TextElementOf(piece);
                }

                return texts.Length == 1 ? texts[0] : JgsValue.StringArray(texts, rhs.Rows, rhs.Cols);
            }

            default:
                throw NotConvertible(MatlabClassWord(rhs), "string", at);
        }
    }

    /// <summary>A number, logical or complex as a string array element: NaN is missing, true is "true".</summary>
    private static JgsValue TextElementOf(JgsValue value) => value.Type == JgsType.Bool
        ? JgsValue.Str(value.AsBool ? "true" : "false")
        : JgsBuiltins.StringElementOf(value);

    private static JgsRuntimeException NotConvertible(string from, string to, Node at) =>
        new(at.Line, at.Column,
            $"Unable to perform assignment because value of type '{from}' is not convertible to '{to}'.");

    /// <summary>MATLAB's word for a value's class, for the messages that name one.</summary>
    private static string MatlabClassWord(JgsValue value) => value switch
    {
        { IsStringArray: true } => "string",
        { Type: JgsType.Cell } => "cell",
        { Type: JgsType.Bool } => "logical",
        { Type: JgsType.Struct } => "struct",
        { Type: JgsType.Function } => "function_handle",
        { Type: JgsType.String } => "char",
        { Type: JgsType.Number or JgsType.Complex or JgsType.Array } => value.NumericClass.MatlabName(),
        _ => value.TypeName,
    };

    // --- char rows ----------------------------------------------------------------------------

    /// <summary>
    /// <c>c(k) = 'x'</c> on a char row (MATLAB): the characters are written by position, the row grows
    /// with <c>char(0)</c> past its end, and <c>c(k) = []</c> or <c>c(k) = ''</c> deletes. A char row
    /// is an immutable string here, so every write rebuilds it and rebinds the name.
    /// </summary>
    private JgsValue AssignIntoCharRow(
        Expr target, JgsValue callee, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (op != TokenType.Assign)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "A char row takes a plain assignment, c(k) = 'x'.");
        }

        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Assigning into a char row needs a plain variable on the left.");
        }

        string text = callee.AsString;
        Expr subscript = subscripts[0];
        if (subscripts.Count == 2)
        {
            // c(1, k): the one row there is. Growing a char row downwards into a char matrix by
            // assignment is not done here; char() and [;] build one.
            int[] extents = [1, text.Length];
            JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
            int[] rowPicks = WritePicks(rowIndex, 1, at);
            if (rowPicks.Length != 1 || rowPicks[0] != 0)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    "Assigning outside the one row of a char row would make a char matrix; build it with char() or [;] instead.");
            }

            subscript = subscripts[1];
        }
        else if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "A char row takes one subscript, or a row and a column.");
        }

        JgsValue? index = EvaluateIndexArgument(subscript, text.Length, env);
        bool deleting = (rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray)
            || (rhs.Type == JgsType.String && rhs.AsString.Length == 0);
        if (deleting)
        {
            string kept = string.Empty;
            if (index is not null)
            {
                var drop = new HashSet<int>(ComputePicks(AsIndexArray(index), text.Length, "char", at.Line, at.Column));
                var builder = new StringBuilder(text.Length);
                for (int i = 0; i < text.Length; i++)
                {
                    if (!drop.Contains(i))
                    {
                        builder.Append(text[i]);
                    }
                }

                kept = builder.ToString();
            }

            JgsValue shortened = JgsValue.Str(kept);
            Rebind(variable.Name, shortened, env);
            return rhs;
        }

        char[] written = CharsWritten(rhs, at);
        int[] picks = WritePicks(index, text.Length, at);
        if (written.Length != 1 && written.Length != picks.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column, CountMismatch);
        }

        int needed = Math.Max(text.Length, Highest(picks) + 1);
        var chars = new char[needed];
        text.CopyTo(0, chars, 0, text.Length);
        for (int i = text.Length; i < needed; i++)
        {
            chars[i] = '\0'; // a grown char row is padded with char(0) (measured: double(c) shows 0)
        }

        for (int i = 0; i < picks.Length; i++)
        {
            chars[picks[i]] = written[written.Length == 1 ? 0 : i];
        }

        JgsValue rebuilt = JgsValue.Str(new string(chars));
        Rebind(variable.Name, rebuilt, env);
        return rhs;
    }

    /// <summary>The characters a right-hand side writes into a char row: text as itself, numbers as codes.</summary>
    private static char[] CharsWritten(JgsValue rhs, Node at)
    {
        if (rhs.IsStringArray)
        {
            // A string spreads its characters over the slots (c(2:3) = "xy" is measured to work); a
            // missing string has none to give.
            var builder = new StringBuilder();
            foreach (JgsValue element in rhs.BoxedElements())
            {
                if (JgsBuiltins.IsMissingText(element.AsString))
                {
                    throw NotConvertible("string", "char", at);
                }

                builder.Append(element.AsString);
            }

            return builder.ToString().ToCharArray();
        }

        if (rhs.Type == JgsType.String)
        {
            return rhs.AsString.ToCharArray();
        }

        if (rhs.IsCharMatrix)
        {
            return rhs.CharMatrixText().ToCharArray();
        }

        if (rhs.Type is JgsType.Number or JgsType.Complex || (rhs.Type == JgsType.Array && !JgsBuiltins.IsLogicalValue(rhs)))
        {
            JgsValue[] numbers = rhs.Type == JgsType.Array ? rhs.BoxedElements() : [rhs];
            return Array.ConvertAll(numbers, static n => (char)(int)(n.Type == JgsType.Complex ? n.AsComplex.Real : n.AsNumber));
        }

        throw new JgsRuntimeException(at.Line, at.Column,
            $"Conversion to char from {MatlabClassWord(rhs)} is not possible.");
    }

    // --- cells --------------------------------------------------------------------------------

    /// <summary>
    /// <c>c(k) = {v}</c> and <c>c(i, j) = {v}</c>: the cells on the right are written into the slots on
    /// the left, a one-cell right-hand side fills every slot, <c>[]</c> deletes, and a write past the
    /// end grows the cell with empty <c>[]</c> cells — the <c>c(end + 1) = {v}</c> idiom.
    /// </summary>
    private JgsValue AssignIntoCellParen(
        Expr target, JgsValue callee, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (op != TokenType.Assign)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "A cell takes a plain assignment, c(k) = {v}.");
        }

        bool deleting = rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray;
        if (!deleting && rhs.Type != JgsType.Cell)
        {
            throw rhs.IsStringArray
                ? NotConvertible("string", "cell", at)
                : new JgsRuntimeException(at.Line, at.Column,
                    $"Conversion to cell from {MatlabClassWord(rhs)} is not possible.");
        }

        return subscripts.Count switch
        {
            1 => AssignCellLinear(target, callee, subscripts[0], deleting, rhs, at, env),
            2 => AssignCellTwoSubscripts(target, callee, subscripts, deleting, rhs, at, env),
            _ => throw new JgsRuntimeException(at.Line, at.Column,
                "Cell assignment takes one subscript or a row and a column."),
        };
    }

    private JgsValue AssignCellLinear(
        Expr target, JgsValue callee, Expr subscript, bool deleting, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsValue[] cells = callee.AsCell;
        JgsValue? index = EvaluateIndexArgument(subscript, cells.Length, env);
        bool column = callee.Cols == 1 && callee.Rows != 1;

        if (deleting)
        {
            JgsValue emptied;
            if (index is null)
            {
                emptied = ShapedCell([], 0, 0);
            }
            else
            {
                if (callee.Rows > 1 && callee.Cols > 1)
                {
                    throw new JgsRuntimeException(at.Line, at.Column,
                        "Deleting from a cell matrix takes a whole row or column: c(i, :) = [] or c(:, j) = [].");
                }

                var drop = new HashSet<int>(ComputePicks(AsIndexArray(index), cells.Length, "cell", at.Line, at.Column));
                var kept = new List<JgsValue>(cells.Length);
                for (int i = 0; i < cells.Length; i++)
                {
                    if (!drop.Contains(i))
                    {
                        kept.Add(cells[i]);
                    }
                }

                emptied = column ? ShapedCell(kept, kept.Count, 1) : ShapedCell(kept, 1, kept.Count);
            }

            RebindOrRefuse(target, emptied, at, env, "Deleting cells needs a plain variable on the left.");
            return rhs;
        }

        int[] picks = WritePicks(index, cells.Length, at);
        int needed = Highest(picks) + 1;
        if (needed > cells.Length)
        {
            if (callee.Rows > 1 && callee.Cols > 1)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "Attempt to grow array along ambiguous dimension.");
            }

            var grown = new JgsValue[needed];
            Array.Copy(cells, grown, cells.Length);
            for (int i = cells.Length; i < needed; i++)
            {
                grown[i] = EmptyBracket();
            }

            callee = column ? ShapedCell(grown, needed, 1) : ShapedCell(grown, 1, needed);
            cells = callee.AsCell;
            RebindOrRefuse(target, callee, at, env,
                $"Assigning past the end of a {cells.Length}-cell array would grow it, which needs a plain variable on the left.");
        }

        WriteCells(cells, picks, rhs.AsCell, at);
        return rhs;
    }

    private JgsValue AssignCellTwoSubscripts(
        Expr target, JgsValue callee, IReadOnlyList<Expr> subscripts, bool deleting, JgsValue rhs, Node at, JgsEnvironment env)
    {
        int rows = callee.Rows;
        int cols = callee.Cols;
        int[] extents = [rows, cols];
        JgsValue? rowIndex = EvaluateIndexArgument(subscripts[0], extents, 0, env);
        JgsValue? colIndex = EvaluateIndexArgument(subscripts[1], extents, 1, env);
        JgsValue[] cells = callee.AsCell;

        if (deleting)
        {
            bool deletingRows = colIndex is null;
            if (deletingRows == (rowIndex is null))
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    "Deleting from a cell matrix takes a whole row or column: c(i, :) = [] or c(:, j) = [].");
            }

            var drop = new HashSet<int>(deletingRows
                ? ComputePicks(AsIndexArray(rowIndex!), rows, "row", at.Line, at.Column)
                : ComputePicks(AsIndexArray(colIndex!), cols, "column", at.Line, at.Column));
            int[] keptRows = deletingRows ? Remaining(rows, drop) : AllPicks(rows);
            int[] keptCols = deletingRows ? AllPicks(cols) : Remaining(cols, drop);
            var kept = new JgsValue[keptRows.Length * keptCols.Length];
            for (int c = 0; c < keptCols.Length; c++)
            {
                for (int r = 0; r < keptRows.Length; r++)
                {
                    kept[r + (c * keptRows.Length)] = cells[keptRows[r] + (keptCols[c] * rows)];
                }
            }

            RebindOrRefuse(target, ShapedCell(kept, keptRows.Length, keptCols.Length), at, env,
                "Deleting cells needs a plain variable on the left.");
            return rhs;
        }

        bool shapeless = rows == 0 && cols == 0;
        int[] rowPicks = shapeless && rowIndex is null ? AllPicks(rhs.Rows) : WritePicks(rowIndex, rows, at);
        int[] colPicks = shapeless && colIndex is null ? AllPicks(rhs.Cols) : WritePicks(colIndex, cols, at);
        int neededRows = Math.Max(rows, Highest(rowPicks) + 1);
        int neededCols = Math.Max(cols, Highest(colPicks) + 1);
        if (neededRows > rows || neededCols > cols)
        {
            var grown = new JgsValue[neededRows * neededCols];
            for (int c = 0; c < neededCols; c++)
            {
                for (int r = 0; r < neededRows; r++)
                {
                    grown[r + (c * neededRows)] = r < rows && c < cols ? cells[r + (c * rows)] : EmptyBracket();
                }
            }

            callee = ShapedCell(grown, neededRows, neededCols);
            cells = callee.AsCell;
            rows = neededRows;
            RebindOrRefuse(target, callee, at, env,
                "Assigning outside a cell array would grow it, which needs a plain variable on the left.");
        }

        var slots = new int[rowPicks.Length * colPicks.Length];
        for (int c = 0; c < colPicks.Length; c++)
        {
            for (int r = 0; r < rowPicks.Length; r++)
            {
                slots[r + (c * rowPicks.Length)] = rowPicks[r] + (colPicks[c] * rows);
            }
        }

        WriteCells(cells, slots, rhs.AsCell, at);
        return rhs;
    }

    /// <summary>Writes the cells of a right-hand side into the chosen slots: one fills all, else one each.</summary>
    private static void WriteCells(JgsValue[] cells, int[] slots, JgsValue[] source, Node at)
    {
        if (source.Length != 1 && source.Length != slots.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column, CountMismatch);
        }

        for (int i = 0; i < slots.Length; i++)
        {
            cells[slots[i]] = source[source.Length == 1 ? 0 : i];
        }
    }

    private void RebindOrRefuse(Expr target, JgsValue value, Node at, JgsEnvironment env, string refusal)
    {
        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column, refusal);
        }

        Rebind(variable.Name, value, env);
    }
}
