using System.Globalization;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The string-editing family (M63) and the one pass that teaches the text builtins already here to
/// work elementwise over a string array or a cell of char rows.
/// </summary>
/// <remarks>
/// The retrofit is a wrapper rather than an edit to each definition, for the same reason the
/// string-aware marking is a pass: the fifteen names below live in five different files, and the rule
/// they all now obey — <em>a text function applied to several pieces of text answers once per
/// piece</em> — is one rule and belongs in one place.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The text builtins whose first text argument may be several pieces of text at once. Each is
    /// wrapped to map over them; none of them consumes a whole array the way <c>join</c> does, which
    /// is exactly the test for belonging on this list.
    /// </summary>
    private static readonly string[] ElementwiseTextBuiltins =
    [
        "upper", "lower", "replace", "reverse", "str2double", "erase",
    ];

    /// <summary>
    /// The mapped names that answer text, for which a missing string answers a missing string:
    /// <c>upper(string(missing))</c> is missing, not <c>"&lt;MISSING&gt;"</c> (measured).
    /// </summary>
    private static readonly HashSet<string> MissingKeepingBuiltins = new(StringComparer.Ordinal)
    {
        "upper", "lower", "replace", "reverse", "erase",
    };

    /// <summary>
    /// The names whose arguments after the subject are a list of patterns rather than a partner to
    /// pair elementwise with. MATLAB lets every one of them take several, and each does its own
    /// thing with the list: <c>contains</c> asks whether any matched, <c>count</c> adds them up,
    /// <c>replace</c> and <c>erase</c> apply them all in one pass.
    /// </summary>
    private static readonly HashSet<string> PatternTakingBuiltins = new(StringComparer.Ordinal)
    {
        "contains", "startsWith", "endsWith", "matches", "count", "replace", "erase", "regexprep",
    };

    /// <summary>Registers the editing family and applies the elementwise retrofit.</summary>
    internal static void RegisterStringEditingBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // char is the char-row constructor, and it was missing entirely: nothing declared it before
        // M63, so char('abc') and char(65) both failed on a name rather than on an argument.
        Define("char", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                return JgsValue.Str(string.Empty);
            }

            // char of several arguments stacks them, which is how MATLAB builds a char matrix.
            if (args.Count > 1)
            {
                return PadIntoCharMatrix([.. args.Select(a => TextForChar(a, line, col))]);
            }

            JgsValue only = args[0];

            // char of a char matrix is that char matrix (M105). Asked before the numeric arm below,
            // which would otherwise read its code points as one long char row — which is what
            // char(char('a', 'bcd')) used to answer, a 1-by-6 where MATLAB says 2-by-3.
            if (only.IsCharMatrix)
            {
                return only;
            }

            // A time is its display text, not its code points (M64) — asked before the numeric arm
            // below, which would otherwise read a datetime's milliseconds as characters.
            if (only.IsTime)
            {
                var moments = new string[only.ArrayLength];
                for (int i = 0; i < moments.Length; i++)
                {
                    moments[i] = TimeText(only, i);
                }

                return moments.Length == 1 ? JgsValue.Str(moments[0]) : PadIntoCharMatrix(moments);
            }

            if (only.IsStringArray)
            {
                string[] texts = Array.ConvertAll(only.BoxedElements(), static e => e.AsString);
                return texts.Length == 1 ? JgsValue.Str(texts[0]) : PadIntoCharMatrix(texts);
            }

            if (only.Type == JgsType.Cell)
            {
                return PadIntoCharMatrix(Array.ConvertAll(only.AsCell, e => TextForChar(e, line, col)));
            }

            // A numeric array is code points, which is the other half of what char means.
            if (only.Type == JgsType.Array)
            {
                static string Glyph(JgsValue element) => element.Type == JgsType.String
                    ? element.AsString
                    : ((char)(int)element.AsNumber).ToString();

                // A matrix of code points keeps its shape, because MATLAB reads each of its ROWS as
                // a row of characters: [72 73 74; 75 76 77] is ['HIJ'; 'KLM'] and not the 1-by-6 that
                // reading storage order end to end gives. The shape was being lost at construction,
                // so every 2-D subscript on the answer was then refused for want of a second row.
                int height = only.Rows;
                int width = only.Cols;
                if (height > 1 && (long)height * width == only.ArrayLength)
                {
                    var rows = new string[height];
                    for (int r = 0; r < height; r++)
                    {
                        var row = new System.Text.StringBuilder(width);
                        for (int c = 0; c < width; c++)
                        {
                            row.Append(Glyph(only.ElementAt((c * height) + r)));
                        }

                        rows[r] = row.ToString();
                    }

                    return JgsValue.CharMatrix(rows);
                }

                var text = new System.Text.StringBuilder();
                for (int i = 0; i < only.ArrayLength; i++)
                {
                    text.Append(Glyph(only.ElementAt(i)));
                }

                return JgsValue.Str(text.ToString());
            }

            return only.Type switch
            {
                JgsType.String => only,
                JgsType.Number or JgsType.Bool => JgsValue.Str(((char)(int)only.AsNumber).ToString()),
                _ => JgsValue.Str(only.Display()),
            };
        });

        Define("erase", (args, line, col) =>
        {
            Arity("erase", args, 2, line, col);
            string text = Str("erase", args, 0, line, col);

            // Nothing is erased for an empty pattern: erase('abc', '') is 'abc' (measured), where
            // .NET's Replace would refuse the empty search text.
            if (IsOnePattern(args[1], out string onlyGone))
            {
                return JgsValue.Str(onlyGone.Length == 0 ? text : text.Replace(onlyGone, string.Empty, StringComparison.Ordinal));
            }

            string[] gone = Array.FindAll(PatternsOf("erase", args, 1, line, col), static p => p.Length > 0);
            return JgsValue.Str(gone.Length == 0 ? text : ReplacedAtOnce(text, gone, new string?[gone.Length]));
        });

        // insertAfter, insertBefore, extractAfter, extractBefore, extractBetween and strrep read their
        // own containers and are declared beside each other.
        RegisterTextPositionBuiltins(env);

        // str2num is not here: it evaluates its text as an expression, so it needs the running
        // interpreter and is declared beside eval, which is the only thing that has one.
        MapOverText(env);
        KeepTextKind(env);

        // The comparing, searching, trimming, padding and ordering verbs read their own containers
        // and are declared over whatever the retrofits above left, so they hold each name last.
        RegisterTextFamilyBuiltins(env, dialect);
        RegisterTextOrderBuiltins(env, dialect);
    }

    /// <summary>The text one argument of <c>char</c> contributes.</summary>
    private static string TextForChar(JgsValue value, int line, int col) => value.Type switch
    {
        JgsType.String => value.AsString,
        JgsType.Number or JgsType.Bool => ((char)(int)value.AsNumber).ToString(),
        _ when IsStringScalar(value) => TextOf(value),
        _ => throw new JgsRuntimeException(line, col, $"char cannot convert a {value.TypeName}."),
    };

    // --- The elementwise retrofit ------------------------------------------------------------------

    /// <summary>
    /// Wraps each of <see cref="ElementwiseTextBuiltins"/> so that a string array or a cell of char
    /// in its first text position is answered once per element, with the container kept.
    /// </summary>
    private static void MapOverText(JgsEnvironment env)
    {
        foreach (string name in ElementwiseTextBuiltins)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                continue;
            }

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                // Which argument the map walks. For most names it is the first container found —
                // strcmp("a", ["a" "b"]) compares one against each — but for the search-and-edit
                // verbs a container after the subject is a *list of patterns* the body applies
                // together, not a partner to pair with, and mapping over it would answer once per
                // pattern instead of once (M121).
                int slot = FirstTextContainer(args);
                if (slot < 0 || (slot > 0 && PatternTakingBuiltins.Contains(name)))
                {
                    return inner.Call(args, line, col);
                }

                JgsValue container = args[slot];
                bool asStrings = container.IsStringArray;
                JgsValue[] pieces = asStrings ? container.BoxedElements() : container.AsCell;

                // The arguments beside the text are the same at every element, so they are demoted
                // once here rather than walked and copied inside the inner call each time round —
                // and the one exception translation wraps the whole map for the same reason. What
                // reaches the builtin is what reached it before, element for element.
                var one = new JgsValue[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    one[i] = inner.Demote(args[i]);
                }

                return inner.Protect(
                    () =>
                    {
                        var answers = new JgsValue[pieces.Length];
                        bool keepsMissing = asStrings && MissingKeepingBuiltins.Contains(name);
                        for (int i = 0; i < pieces.Length; i++)
                        {
                            if (keepsMissing && pieces[i].Type == JgsType.String && IsMissingText(pieces[i].AsString))
                            {
                                answers[i] = pieces[i];
                                continue;
                            }

                            one[slot] = inner.Demote(pieces[i]);
                            answers[i] = inner.Invoke(one, line, col);
                        }

                        return Reassemble(answers, container, asStrings);
                    },
                    line,
                    col);
            })
            {
                // The wrapper has to see the string array to map over it, so it opts out of the
                // demotion the ordinary builtins rely on — and then hands each element down as the
                // char row the inner function has always expected.
                KeepsStringArguments = true,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,

                // Carried unchanged: none of these names has a multi-output form today, and dropping
                // one silently is a worse failure than not mapping over a second answer.
                MultiOutput = inner.MultiOutput,
            }));
        }
    }

    /// <summary>
    /// The first argument holding several pieces of text, or -1 when none does. A one-element string
    /// array counts: <c>upper("ab")</c> answers <c>"AB"</c>, a string, not a char row.
    /// </summary>
    private static int FirstTextContainer(IReadOnlyList<JgsValue> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].IsStringArray)
            {
                return i;
            }

            if (args[i].Type == JgsType.Cell && args[i].AsCell.Length > 0
                && Array.TrueForAll(args[i].AsCell, static e => e.Type == JgsType.String))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The builtins that take text in and give text back without changing what kind of text it is:
    /// join a string array and you get a string, split a string and you get a string array. They are
    /// not on the elementwise list because each consumes the whole array rather than mapping over it.
    /// </summary>
    private static readonly string[] TextKindPreservingBuiltins =
        ["join", "strsplit", "strjoin", "sprintf"];

    /// <summary>
    /// Wraps <see cref="TextKindPreservingBuiltins"/> so a string argument produces string answers.
    /// The inner builtin still sees the char rows it was written against; only the answers change.
    /// </summary>
    private static void KeepTextKind(JgsEnvironment env)
    {
        foreach (string name in TextKindPreservingBuiltins)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                continue;
            }

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                bool wasString = args.Count > 0 && args[0].IsStringArray;
                JgsValue answer = inner.Call(args, line, col);
                return wasString ? PromoteText(answer) : answer;
            })
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,

                // Carried, and promoted the same way: strsplit's second output is the delimiters it
                // cut on, and a wrapper that dropped it would silently turn [c, m] = strsplit(...)
                // into a shortfall — which is exactly what the first version of this did.
                MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
                {
                    // Through CallMultiple rather than the delegate, so the string scalars this
                    // wrapper kept are demoted for the body exactly as the one-output road demotes
                    // them: [c, m] = strsplit("a,b", ",") used to hand the body a string array.
                    bool wasStringInput = args.Count > 0 && args[0].IsStringArray;
                    JgsValue[] outputs = inner.CallMultiple(args, wanted, line, col);
                    return wasStringInput ? Array.ConvertAll(outputs, PromoteText) : outputs;
                },
            }));
        }
    }

    /// <summary>Re-marks an answer as text of the same kind its input was, or hands it back unchanged.</summary>
    private static JgsValue PromoteText(JgsValue answer)
    {
        if (answer.Type == JgsType.String)
        {
            return JgsValue.StringScalar(answer.AsString);
        }

        if (answer.Type == JgsType.Array && !answer.IsStringArray && answer.ArrayLength > 0
            && Array.TrueForAll(answer.BoxedElements(), static e => e.Type == JgsType.String))
        {
            return answer.MarkStringArray();
        }

        // strsplit answers its pieces in a cell, which for a string subject is a string array of
        // the same shape: strsplit("a,b", ",") is ["a" "b"] (measured).
        if (answer.Type == JgsType.Cell && Array.TrueForAll(answer.AsCell, static e => e.Type == JgsType.String))
        {
            return JgsValue.StringArray(answer.AsCell, answer.Rows, answer.Cols);
        }

        return answer;
    }

    /// <summary>
    /// Puts the per-element answers back into the container they came from. Text answers keep the
    /// container — a string array in, a string array out — and anything else (a logical from
    /// <c>contains</c>, a number from <c>str2double</c>) becomes a plain array, because that is what
    /// those answers are.
    /// </summary>
    private static JgsValue Reassemble(JgsValue[] answers, JgsValue container, bool asStrings)
    {
        bool text = Array.TrueForAll(answers, static a => a.Type == JgsType.String || IsStringScalar(a));
        if (text)
        {
            var flattened = new JgsValue[answers.Length];
            for (int i = 0; i < answers.Length; i++)
            {
                flattened[i] = answers[i].Type == JgsType.String ? answers[i] : answers[i].ElementAt(0);
            }

            JgsValue built = asStrings ? JgsValue.StringArray(flattened) : JgsValue.Cell(flattened);
            built.TakeShapeOf(container);
            return built;
        }

        JgsValue plain = JgsValue.Array(answers);
        plain.TakeShapeOf(container);
        return plain;
    }
}
