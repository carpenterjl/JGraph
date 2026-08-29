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
        "upper", "lower", "strtrim", "strip", "strrep", "replace", "reverse", "str2double",
        "contains", "startsWith", "endsWith", "strcmp", "strcmpi", "strncmp", "strncmpi",
        "erase", "pad", "insertAfter", "insertBefore", "extractAfter", "extractBefore",
    ];

    /// <summary>Registers the editing family and applies the elementwise retrofit.</summary>
    internal static void RegisterStringEditingBuiltins(JgsEnvironment env)
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
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < only.ArrayLength; i++)
                {
                    JgsValue element = only.ElementAt(i);
                    text.Append(element.Type == JgsType.String
                        ? element.AsString
                        : ((char)(int)element.AsNumber).ToString());
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

        Define("strip", (args, line, col) =>
        {
            ArityRange("strip", args, 1, 2, line, col);
            string text = Str("strip", args, 0, line, col);
            string side = args.Count > 1 ? Str("strip", args, 1, line, col) : "both";
            return JgsValue.Str(side switch
            {
                "left" => text.TrimStart(),
                "right" => text.TrimEnd(),
                "both" => text.Trim(),
                _ => throw new JgsRuntimeException(line, col,
                    $"strip: '{side}' is not a side — use 'left', 'right', or 'both'."),
            });
        });

        Define("pad", (args, line, col) =>
        {
            ArityRange("pad", args, 1, 3, line, col);
            string text = Str("pad", args, 0, line, col);

            // pad(s) with no width pads to the longest, which for one string is its own length; the
            // width only means something once the elementwise wrapper has several to compare.
            int width = args.Count > 1 && args[1].Type != JgsType.String
                ? Count("pad", args, 1, line, col)
                : text.Length;
            string side = args.Count > 1 && args[^1].Type == JgsType.String
                ? Str("pad", args, args.Count - 1, line, col)
                : "right";

            return JgsValue.Str(side switch
            {
                "left" => text.PadLeft(width),
                "right" => text.PadRight(width),
                "both" => text.PadLeft(text.Length + ((width - text.Length) / 2)).PadRight(width),
                _ => throw new JgsRuntimeException(line, col,
                    $"pad: '{side}' is not a side — use 'left', 'right', or 'both'."),
            });
        });

        Define("erase", (args, line, col) =>
        {
            Arity("erase", args, 2, line, col);
            return JgsValue.Str(Str("erase", args, 0, line, col)
                .Replace(Str("erase", args, 1, line, col), string.Empty, StringComparison.Ordinal));
        });

        Define("insertAfter", (args, line, col) => Insert("insertAfter", args, after: true, line, col));
        Define("insertBefore", (args, line, col) => Insert("insertBefore", args, after: false, line, col));

        Define("extractAfter", (args, line, col) => Extract("extractAfter", args, after: true, line, col));
        Define("extractBefore", (args, line, col) => Extract("extractBefore", args, after: false, line, col));

        Define("extractBetween", (args, line, col) =>
        {
            Arity("extractBetween", args, 3, line, col);
            string text = Str("extractBetween", args, 0, line, col);

            // Two numbers are positions and two strings are delimiters, which is the same overload
            // MATLAB has and the reason the arguments are read rather than declared.
            if (args[1].Type != JgsType.String)
            {
                int from = Count("extractBetween", args, 1, line, col) - 1;
                int to = Count("extractBetween", args, 2, line, col) - 1;
                return JgsValue.StringScalar(Slice(text, from, to - from + 1));
            }

            string open = Str("extractBetween", args, 1, line, col);
            string close = Str("extractBetween", args, 2, line, col);
            int start = text.IndexOf(open, StringComparison.Ordinal);
            if (start < 0)
            {
                // A marker that is not there gives back empty text rather than no text: the answer
                // is still one string, and an array of none breaks every caller that goes on to
                // index it.
                return JgsValue.StringScalar(string.Empty);
            }

            start += open.Length;
            int stop = text.IndexOf(close, start, StringComparison.Ordinal);
            return JgsValue.StringScalar(stop < 0 ? string.Empty : text[start..stop]);
        });

        // str2num is not here: it evaluates its text as an expression, so it needs the running
        // interpreter and is declared beside eval, which is the only thing that has one.
        MapOverText(env);
        SortAndUniqueOverText(env);
        KeepTextKind(env);
    }

    /// <summary>The text one argument of <c>char</c> contributes.</summary>
    private static string TextForChar(JgsValue value, int line, int col) => value.Type switch
    {
        JgsType.String => value.AsString,
        JgsType.Number or JgsType.Bool => ((char)(int)value.AsNumber).ToString(),
        _ when IsStringScalar(value) => TextOf(value),
        _ => throw new JgsRuntimeException(line, col, $"char cannot convert a {value.TypeName}."),
    };

    private static JgsValue Insert(string name, IReadOnlyList<JgsValue> args, bool after, int line, int col)
    {
        Arity(name, args, 3, line, col);
        string text = Str(name, args, 0, line, col);
        string insert = Str(name, args, 2, line, col);

        if (args[1].Type != JgsType.String)
        {
            int at = Count(name, args, 1, line, col);
            int cut = Math.Clamp(after ? at : at - 1, 0, text.Length);
            return JgsValue.Str(text[..cut] + insert + text[cut..]);
        }

        string marker = Str(name, args, 1, line, col);
        int found = text.IndexOf(marker, StringComparison.Ordinal);
        if (found < 0)
        {
            return JgsValue.Str(text); // no marker, nothing inserted — MATLAB's answer too
        }

        int point = after ? found + marker.Length : found;
        return JgsValue.Str(text[..point] + insert + text[point..]);
    }

    private static JgsValue Extract(string name, IReadOnlyList<JgsValue> args, bool after, int line, int col)
    {
        Arity(name, args, 2, line, col);
        string text = Str(name, args, 0, line, col);

        if (args[1].Type != JgsType.String)
        {
            int at = Count(name, args, 1, line, col);
            int cut = Math.Clamp(after ? at : at - 1, 0, text.Length);
            return JgsValue.StringScalar(after ? text[cut..] : text[..cut]);
        }

        string marker = Str(name, args, 1, line, col);
        int found = text.IndexOf(marker, StringComparison.Ordinal);
        if (found < 0)
        {
            return JgsValue.StringScalar(string.Empty); // see extractBetween: empty text, not no text
        }

        return JgsValue.StringScalar(after ? text[(found + marker.Length)..] : text[..found]);
    }

    /// <summary>A substring clamped to what is there, so an out-of-range request answers rather than throws.</summary>
    private static string Slice(string text, int from, int length)
    {
        int start = Math.Clamp(from, 0, text.Length);
        return text.Substring(start, Math.Clamp(length, 0, text.Length - start));
    }

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
                int slot = FirstTextContainer(args);
                if (slot < 0)
                {
                    return inner.Call(args, line, col);
                }

                JgsValue container = args[slot];
                bool asStrings = container.IsStringArray;
                JgsValue[] pieces = asStrings ? container.BoxedElements() : container.AsCell;

                var answers = new JgsValue[pieces.Length];
                var one = args.ToArray();
                for (int i = 0; i < pieces.Length; i++)
                {
                    one[slot] = pieces[i];
                    answers[i] = inner.Call(one, line, col);
                }

                return Reassemble(answers, container, asStrings);
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
        ["join", "split", "strsplit", "strjoin", "compose"];

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
                    bool wasStringInput = args.Count > 0 && args[0].IsStringArray;
                    JgsValue[] outputs = inner.MultiOutput(args, wanted, line, col);
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

        return answer.Type == JgsType.Array && !answer.IsStringArray && answer.ArrayLength > 0
               && Array.TrueForAll(answer.BoxedElements(), static e => e.Type == JgsType.String)
            ? answer.MarkStringArray()
            : answer;
    }

    /// <summary>
    /// Teaches <c>sort</c> and <c>unique</c> about string arrays. Both reach for numbers otherwise,
    /// and a list of labels is precisely the thing a script most wants to sort.
    /// </summary>
    private static void SortAndUniqueOverText(JgsEnvironment env)
    {
        void Wrap(string name, Func<string[], string[]> over)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                if (args.Count == 0 || !args[0].IsStringArray)
                {
                    return inner.Call(args, line, col);
                }

                string[] texts = Array.ConvertAll(args[0].BoxedElements(), static e => e.AsString);
                string[] answer = over(texts);
                JgsValue built = JgsValue.StringArray(Array.ConvertAll(answer, JgsValue.Str));

                // A sorted array keeps its orientation; a smaller one cannot, so only sort does.
                if (answer.Length == texts.Length)
                {
                    built.TakeShapeOf(args[0]);
                }
                else if (args[0].Rows > 1 && args[0].Cols == 1)
                {
                    built.Reshape(answer.Length, 1);
                }

                return built;
            })
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                MultiOutput = inner.MultiOutput,
            }));
        }

        Wrap("sort", static texts => [.. texts.OrderBy(static t => t, StringComparer.Ordinal)]);
        Wrap("unique", static texts => [.. texts.Distinct(StringComparer.Ordinal).OrderBy(static t => t, StringComparer.Ordinal)]);
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
