using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The shape verbs, taught that what they are rearranging may not be doubles (M122, M137).
/// </summary>
/// <remarks>
/// <para>
/// The capability probe named one refusal here — <c>reshape</c> of a char array — and the family
/// behind it had twelve. Every verb in it moves elements about without looking at them, and every one
/// of them read its argument as a block of doubles: a char row, a string array and a cell were each
/// refused by name. Worse than the refusals were the four that did not refuse. <c>permute</c> and
/// <c>transpose</c> hand back their argument untouched when it is not a numeric array, so
/// <c>permute({1,2;3,4}, [2 1])</c> answered the cell it was given, unrotated, and said nothing. A
/// verb that quietly does nothing is harder to find than one that stops.
/// </para>
/// <para>
/// Two mechanisms, because the two containers are not the same problem. A <b>char row</b> is
/// characters — MATLAB's <c>'ABCD'</c> is 1-by-4, not one value — so it is <em>promoted</em> to the
/// char matrix of its code points, the verb runs on numbers exactly as it always has, and
/// <see cref="WrapCharMatrix"/> puts a single row back as a char row on the way out. That lane also
/// serves the verbs that read values rather than only move them: sorting characters is sorting their
/// code points, which is MATLAB's own rule.
/// </para>
/// <para>
/// A <b>string array or cell</b> cannot be promoted — its elements are not numbers and no arrangement
/// of them is. But every verb in the second list is a <em>permutation of positions</em>: what it does
/// to a value depends on where the value sits and never on what it is. So the verb is run on the
/// positions themselves — 1 to N in the source's shape — and its answer is read as where each element
/// went. One gather then puts the real elements there. No verb needs to learn what a cell is, and the
/// fourteenth verb added to that list gets the behaviour for free.
/// </para>
/// <para>
/// The value-reading verbs are deliberately absent from the second list. <c>sort</c> of positions
/// sorts the positions, which says nothing about the text at those positions, and MATLAB refuses
/// <c>triu</c> of a string array outright rather than inventing a zero for text.
/// </para>
/// <para>
/// M137 found the third container the same way, from a probe script doing an ordinary
/// <c>circshift</c> on a column of an STFT: a <b>complex</b> array was refused by nine of these
/// names — <c>flipud needs numbers, but element (1, 0) was a complex</c> — while <c>flip</c>,
/// <c>fliplr</c> and <c>ctranspose</c> happened to have grown a complex path of their own. Complex
/// is neither of the first two cases: it cannot be promoted to a code point, and its elements are
/// numbers, so the boxes lane would not have it. But it is the same <em>shape</em> of answer, and it
/// rides the same positions — run the verb on 1 to N, read where each position went, gather the
/// complex elements there. Adding a name to the list is again the whole of the work.
/// </para>
/// <para>
/// <c>ctranspose</c> is the one name that does something beyond moving its elements: for text its
/// apostrophe is a plain transpose, which is why it is on the positions list at all, but for numbers
/// it conjugates. A permutation of positions cannot conjugate anything, so the conjugation happens
/// on the way out, once the gather has put each element where the transpose sent it.
/// </para>
/// <para>
/// The one detail MATLAB does not leave to symmetry is whether the answer stays complex when every
/// imaginary part is zero, and it splits the family in two — measured in R2025b, not recalled.
/// <c>isreal(reshape(complex(1,0)))</c> is false, and so is permute's, squeeze's, shiftdim's and
/// transpose's: those hand back the same data with a new shape stamped on it, complexity flag and
/// all. <c>isreal(flipud(complex(1,0)))</c> is <em>true</em>, and so is flip's, fliplr's,
/// flipdim's, circshift's and rot90's: those copy their elements into a new array, and the new array
/// is real if what lands in it is. <see cref="ComplexNarrowingBuiltins"/> is that second list.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The verbs that read a char row as its characters. A superset of
    /// <see cref="PositionRearrangingBuiltins"/>: sorting and the triangular parts look at the values,
    /// which for characters means their code points, and that is what the promotion hands them.
    /// </summary>
    private static readonly string[] CharRowShapeBuiltins =
    [
        "reshape", "permute", "ipermute", "squeeze", "shiftdim", "circshift", "rot90",
        "fliplr", "flipud", "flip", "flipdim", "transpose", "ctranspose",
        "sort", "unique", "sortrows", "triu", "tril",
    ];

    /// <summary>
    /// The verbs that move elements without reading them, so running them on positions and gathering
    /// through the answer is the same operation as running them on the elements themselves.
    /// </summary>
    private static readonly string[] PositionRearrangingBuiltins =
    [
        "reshape", "permute", "ipermute", "squeeze", "shiftdim", "circshift", "rot90",
        "fliplr", "flipud", "flip", "flipdim", "transpose", "ctranspose",
    ];

    /// <summary>
    /// The verbs that gather complex elements through their own answer — the positions list entire,
    /// with <c>ctranspose</c>'s conjugation applied after the gather.
    /// </summary>
    private static readonly string[] ComplexRearrangingBuiltins =
    [
        "reshape", "permute", "ipermute", "squeeze", "shiftdim", "circshift", "rot90",
        "fliplr", "flipud", "flip", "flipdim", "transpose", "ctranspose",
    ];

    /// <summary>
    /// The verbs whose answer is a fresh array rather than a re-stamped one, so a complex argument
    /// with nothing but zeros for imaginary parts comes back <em>real</em> — MATLAB's own seam, and
    /// the reason this is a list and not a rule (see the remarks above).
    /// </summary>
    private static readonly string[] ComplexNarrowingBuiltins =
    [
        "circshift", "rot90", "fliplr", "flipud", "flip", "flipdim",
    ];

    /// <summary>
    /// Wraps the shape verbs so a char row, a string array, a cell or a complex array reaches them as
    /// something they can rearrange, and leaves in the container it arrived in.
    /// </summary>
    /// <param name="env">The environment whose bindings are re-declared.</param>
    /// <param name="dialect">
    /// Only a MATLAB script gets the char-row promotion. It is a rule about MATLAB's char type — that
    /// a quoted word is an array of characters — where a JGS string is one value whose transpose is
    /// itself.
    /// </param>
    private static void Rearrange(JgsEnvironment env, JgsDialect dialect)
    {
        foreach (string name in CharRowShapeBuiltins)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                continue;
            }

            bool promotesCharRows = dialect.IsMatlab;
            bool gathersPositions = Array.IndexOf(PositionRearrangingBuiltins, name) >= 0;
            bool gathersComplex = Array.IndexOf(ComplexRearrangingBuiltins, name) >= 0;
            bool keepsComplexFlag = Array.IndexOf(ComplexNarrowingBuiltins, name) < 0;
            bool conjugates = name == "ctranspose";

            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                if (promotesCharRows && IsCharRow(args))
                {
                    return WrapCharMatrix(inner.Call(WithFirst(args, CharRowOf(args[0])), line, col));
                }

                if (gathersPositions && args.Count > 0 && TryReadBoxes(args[0], out JgsValue[] boxes))
                {
                    return GatheredBoxes(
                        args[0], boxes, inner.Call(WithFirst(args, PositionsLike(args[0])), line, col));
                }

                if (gathersComplex && args.Count > 0 && TryReadComplexElements(
                        name, args[0], line, col, out Complex[] parts, out bool complexFlag))
                {
                    return GatheredComplex(
                        parts,
                        inner.Call(WithFirst(args, PositionsFor(args[0])), line, col),
                        complexFlag && keepsComplexFlag,
                        conjugates);
                }

                return inner.Call(args, line, col);
            })
            {
                // Carried whole, for the reason M105's wrapper carries them whole: a flag dropped here
                // changes how the name may be *called*, which no test of what it answers would see.
                // The position-gathering verbs keep a string scalar as the 1-by-1 array it is, so that
                // fliplr("abc") is "abc" and not the reversed char row it was demoted to (measured).
                KeepsStringArguments = inner.KeepsStringArguments || gathersPositions,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
                {
                    // Only the first output is a rearrangement of the input. shiftdim's second is how
                    // many dimensions it moved and sort's is where each element came from, and those
                    // are numbers whatever the first output holds.
                    if (promotesCharRows && IsCharRow(args))
                    {
                        JgsValue[] outputs = inner.MultiOutput(
                            WithFirst(args, CharRowOf(args[0])), wanted, line, col);
                        if (outputs.Length > 0)
                        {
                            outputs[0] = WrapCharMatrix(outputs[0]);
                        }

                        return outputs;
                    }

                    if (gathersPositions && args.Count > 0 && TryReadBoxes(args[0], out JgsValue[] boxes))
                    {
                        JgsValue[] outputs = inner.MultiOutput(
                            WithFirst(args, PositionsLike(args[0])), wanted, line, col);
                        if (outputs.Length > 0)
                        {
                            outputs[0] = GatheredBoxes(args[0], boxes, outputs[0]);
                        }

                        return outputs;
                    }

                    if (gathersComplex && args.Count > 0 && TryReadComplexElements(
                            name, args[0], line, col, out Complex[] parts, out bool complexFlag))
                    {
                        JgsValue[] outputs = inner.MultiOutput(
                            WithFirst(args, PositionsFor(args[0])), wanted, line, col);
                        if (outputs.Length > 0)
                        {
                            outputs[0] = GatheredComplex(
                                parts, outputs[0], complexFlag && keepsComplexFlag, conjugates);
                        }

                        return outputs;
                    }

                    return inner.MultiOutput(args, wanted, line, col);
                },
            }));
        }
    }

    /// <summary>Whether the value being rearranged is a char row.</summary>
    private static bool IsCharRow(IReadOnlyList<JgsValue> args) =>
        args.Count > 0 && args[0].Type == JgsType.String;

    /// <summary>The same arguments with a different first one.</summary>
    private static IReadOnlyList<JgsValue> WithFirst(IReadOnlyList<JgsValue> args, JgsValue first)
    {
        var copy = new JgsValue[args.Count];
        copy[0] = first;
        for (int i = 1; i < args.Count; i++)
        {
            copy[i] = args[i];
        }

        return copy;
    }

    /// <summary>A char row as the 1-by-n char matrix of its code points.</summary>
    private static JgsValue CharRowOf(JgsValue text) => JgsValue.CharMatrix([text.AsString]);

    /// <summary>
    /// The elements of a container whose contents a shape verb cannot read as numbers, or false. A
    /// char matrix is deliberately not one: it <em>is</em> numbers, and the verbs already answer it
    /// correctly with only their tag to put back (M105).
    /// </summary>
    private static bool TryReadBoxes(JgsValue value, out JgsValue[] boxes)
    {
        if (value.Type == JgsType.Cell || (value.Type == JgsType.Array && value.IsStringArray))
        {
            boxes = value.BoxedElements();
            return boxes.Length > 0;
        }

        boxes = [];
        return false;
    }

    /// <summary>1 to N laid out in the container's own shape — what the verb is actually asked about.</summary>
    private static JgsValue PositionsLike(JgsValue source)
    {
        var positions = new double[source.ArrayLength];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = i + 1;
        }

        return JgsMatrix.FromColumnMajorDims(positions, source.Dims);
    }

    /// <summary>
    /// The elements the verb's answer points at, back in the container they came from and in the shape
    /// the verb chose. A verb that answered one position answers one element, and that element is
    /// still a container of one rather than the bare value inside it — <c>reshape(c, 1, 1)</c> is a
    /// 1-by-1 cell in MATLAB, not its contents.
    /// </summary>
    private static JgsValue GatheredBoxes(JgsValue source, JgsValue[] boxes, JgsValue positions)
    {
        int count = positions.Type == JgsType.Array ? positions.ArrayLength : 1;
        var picked = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            JgsValue at = positions.Type == JgsType.Array ? positions.ElementAt(i) : positions;
            int index = (int)at.AsNumber - 1;
            picked[i] = index >= 0 && index < boxes.Length ? boxes[index] : JgsValue.Number(0);
        }

        int[] dims = positions.Type == JgsType.Array ? positions.Dims : [1, 1];
        if (source.Type == JgsType.Cell)
        {
            JgsValue cell = JgsValue.Cell(picked);
            cell.ReshapeDims(dims);
            return cell;
        }

        JgsValue array = JgsValue.StringArray(picked);
        array.ReshapeDims(dims);
        return array;
    }

    /// <summary>
    /// The elements of a complex array, in column-major order, or false. <paramref name="keepsFlag"/>
    /// says whether the argument was one MATLAB calls complex despite every imaginary part being
    /// zero — <c>complex(1, 0)</c> — which some of these verbs carry and the rest drop.
    /// </summary>
    private static bool TryReadComplexElements(
        string name, JgsValue value, int line, int col, out Complex[] elements, out bool keepsFlag)
    {
        if (!HasComplexElements(value))
        {
            elements = [];
            keepsFlag = false;
            return false;
        }

        elements = ColumnComplex(name, value, line, col);
        keepsFlag = value.Type == JgsType.Array
            && value.IsPackedComplex
            && value.AsPackedComplex.PreserveComplex;
        return true;
    }

    /// <summary>
    /// The positions of a complex argument. A complex <em>scalar</em> is not an array here, and
    /// asking one for its dimensions would be asking the wrong object: it is position 1, and a verb
    /// answers about a single position the same way it answers about a single number.
    /// </summary>
    private static JgsValue PositionsFor(JgsValue source) =>
        source.Type == JgsType.Array ? PositionsLike(source) : JgsValue.Number(1);

    /// <summary>
    /// The complex elements the verb's answer points at, in the shape the verb chose. Storing them
    /// packed is what every other complex answer in the interpreter is, whichever representation the
    /// argument arrived in.
    /// </summary>
    private static JgsValue GatheredComplex(
        Complex[] elements, JgsValue positions, bool keepsFlag, bool conjugate)
    {
        bool many = positions.Type == JgsType.Array;
        int count = many ? positions.ArrayLength : 1;

        // Nothing to gather, and the verb's own answer is the one to keep: the empty rules are the
        // verbs' — flip of a 0-by-3 is a 0-by-3 and reshape may have been told a new shape — and
        // rebuilding one from a dimension list here flattened both to 1-by-0.
        if (count == 0)
        {
            return positions;
        }

        var picked = new Complex[count];
        for (int i = 0; i < count; i++)
        {
            JgsValue at = many ? positions.ElementAt(i) : positions;
            int index = (int)at.AsNumber - 1;
            Complex moved = index >= 0 && index < elements.Length ? elements[index] : Complex.Zero;
            picked[i] = conjugate ? Complex.Conjugate(moved) : moved;
        }

        return ComplexStorage(picked, many ? positions.Dims : [1, 1], keepsFlag);
    }
}
