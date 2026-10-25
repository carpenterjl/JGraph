using System.Globalization;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a function's <c>arguments</c> blocks declare about its header (stage K9, ADR 0265): which
/// parameters are positional, which repeat in groups and which are the structures name-value inputs
/// arrive in, and the output lines checked on the way out. Read once per declaration.
/// </summary>
internal sealed class ArgumentsSignature
{
    private ArgumentsSignature(
        IReadOnlyList<string> positional, int required, IReadOnlyList<string> repeating,
        IReadOnlyList<ArgumentSpec> nameValue, IReadOnlyList<ArgumentSpec> outputs, bool declaresInputs)
    {
        Positional = positional;
        Required = required;
        Repeating = repeating;
        NameValue = nameValue;
        Outputs = outputs;
        DeclaresInputs = declaresInputs;
    }

    /// <summary>The positional parameters, in header order.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>How many of the leading positional parameters have no default.</summary>
    public int Required { get; }

    /// <summary>The parameters a <c>(Repeating)</c> block declares, one group of arguments each time round.</summary>
    public IReadOnlyList<string> Repeating { get; }

    /// <summary>The name-value lines, in the order they are declared.</summary>
    public IReadOnlyList<ArgumentSpec> NameValue { get; }

    /// <summary>The <c>(Output)</c> block's lines.</summary>
    public IReadOnlyList<ArgumentSpec> Outputs { get; }

    /// <summary>Whether an input or repeating block exists, so the call is bound by this signature.</summary>
    public bool DeclaresInputs { get; }

    /// <summary>
    /// How many inputs <c>nargin('f')</c> answers: the positional and repeating parameters, the
    /// name-value structures not counted (R2025b: <c>nargin('nv1')</c> is 1, <c>nargin('rep1')</c> 2).
    /// </summary>
    public int DeclaredCount => Positional.Count + Repeating.Count;

    /// <summary>The signature of <paramref name="function"/>'s leading blocks, or null when it has none.</summary>
    public static ArgumentsSignature? Of(FnStmt function)
    {
        var blocks = new List<ArgumentsStmt>();
        foreach (Stmt statement in function.Body)
        {
            if (statement is not ArgumentsStmt block)
            {
                break;
            }

            blocks.Add(block);
        }

        if (blocks.Count == 0)
        {
            return null;
        }

        var inputs = blocks.Where(static b => b.Kind == ArgumentsKind.Input).SelectMany(static b => b.Arguments).ToList();
        var repeating = blocks.Where(static b => b.Kind == ArgumentsKind.Repeating).SelectMany(static b => b.Arguments).Select(static s => s.Name).ToList();
        var outputs = blocks.Where(static b => b.Kind == ArgumentsKind.Output).SelectMany(static b => b.Arguments).ToList();
        var nameValue = inputs.Where(static s => s.Structure is not null).ToList();
        var structures = new HashSet<string>(nameValue.Select(static s => s.Structure!), StringComparer.Ordinal);
        var defaults = inputs.Where(static s => s.Structure is null)
            .ToDictionary(static s => s.Name, static s => s.Default is not null, StringComparer.Ordinal);

        var positional = new List<string>();
        foreach (string parameter in function.Parameters)
        {
            if (!structures.Contains(parameter) && !repeating.Contains(parameter, StringComparer.Ordinal))
            {
                positional.Add(parameter);
            }
        }

        int required = 0;
        while (required < positional.Count && !(defaults.TryGetValue(positional[required], out bool hasDefault) && hasDefault))
        {
            required++;
        }

        bool declaresInputs = blocks.Exists(static b => b.Kind is ArgumentsKind.Input or ArgumentsKind.Repeating);
        return new ArgumentsSignature(positional, required, repeating, nameValue, outputs, declaresInputs);
    }
}

/// <summary>
/// What the binding of one call left for its <c>arguments</c> blocks: where the repeating groups
/// started, how big a group is, and the name-value inputs passed, by structure and declared name.
/// </summary>
internal sealed class ArgumentBinding
{
    /// <summary>The 0-based argument index of the first repeating group.</summary>
    public int RepeatingStart { get; init; }

    /// <summary>The number of arguments in one repeating group.</summary>
    public int GroupSize { get; init; }

    /// <summary>How many arguments came before the name-value pairs, which is what <c>nargin</c> answers in the body.</summary>
    public int BeforeNames { get; set; }

    /// <summary>The name-value inputs passed, keyed by structure then by the declared name.</summary>
    public Dictionary<string, Dictionary<string, JgsValue>> NameValues { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Binds a call to a function whose <c>arguments</c> blocks declare its inputs, as R2025b binds it
/// (measured: k9_arguments_blocks): the required positional inputs, then the optional ones until a
/// name, then the repeating groups until a name, then the name-value pairs, each name matched
/// without regard to case and by any unambiguous prefix, the last of a repeated name winning.
/// </summary>
internal static class ArgumentBinder
{
    /// <summary>
    /// Declares the call's positional and repeating parameters in <paramref name="frame"/>, and
    /// answers what the blocks need to finish the job; the refusals are R2025b's in its words.
    /// </summary>
    public static ArgumentBinding Bind(
        ArgumentsSignature signature, IReadOnlyList<JgsValue> arguments, JgsEnvironment frame,
        Func<JgsValue, JgsValue> copy, int line, int col)
    {
        bool named = signature.NameValue.Count > 0;
        int positional = signature.Positional.Count;
        int i = 0;

        for (; i < signature.Required; i++)
        {
            if (i >= arguments.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs",
                    $"Invalid argument list. Function requires {signature.Required - arguments.Count} more input(s).");
            }

            if (named && IsName(arguments[i]) && Match(signature, JgsBuiltins.TextOf(arguments[i]), out _, line, col, quiet: true) is not null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                    $"Invalid argument at position {i + 1}. Function requires {PositionalCount(signature)} positional input(s) before name '{JgsBuiltins.TextOf(arguments[i])}'.");
            }

            frame.Declare(signature.Positional[i], copy(arguments[i]));
        }

        for (int p = signature.Required; p < positional && i < arguments.Count; p++, i++)
        {
            if (named && IsName(arguments[i]) && Match(signature, JgsBuiltins.TextOf(arguments[i]), out _, line, col, quiet: true) is not null)
            {
                break;
            }

            frame.Declare(signature.Positional[p], copy(arguments[i]));
        }

        var binding = new ArgumentBinding { RepeatingStart = i, GroupSize = signature.Repeating.Count };
        if (signature.Repeating.Count > 0)
        {
            int size = signature.Repeating.Count;
            var groups = new List<JgsValue[]>();
            while (i < arguments.Count && !(named && IsName(arguments[i]) && Match(signature, JgsBuiltins.TextOf(arguments[i]), out _, line, col, quiet: true) is not null))
            {
                if (arguments.Count - i < size)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:functionValidation:RepeatingInputsNotInGroups",
                        $"Invalid argument list. Repeating arguments, if specified, must be in groups of {size}.");
                }

                groups.Add([.. Enumerable.Range(i, size).Select(k => copy(arguments[k]))]);
                i += size;
            }

            for (int j = 0; j < size; j++)
            {
                frame.Declare(signature.Repeating[j], JgsValue.Cell([.. groups.Select(g => g[j])]));
            }
        }

        binding.BeforeNames = i;
        if (!named)
        {
            if (i < arguments.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            return binding;
        }

        for (; i < arguments.Count; i += 2)
        {
            JgsValue name = arguments[i];
            if (i == arguments.Count - 1)
            {
                throw IsName(name)
                    ? new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                        $"Invalid argument at position {i + 1}. Make sure name-value arguments include both the name and the value.")
                    : NotAName(signature, i, line, col);
            }

            if (!IsName(name))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", $"Invalid argument at position {i + 1}. A name is expected.");
            }

            ArgumentSpec spec = Match(signature, JgsBuiltins.TextOf(name), out _, line, col, quiet: false)!;
            if (!binding.NameValues.TryGetValue(spec.Structure!, out Dictionary<string, JgsValue>? values))
            {
                binding.NameValues[spec.Structure!] = values = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            }

            values[spec.Name] = copy(arguments[i + 1]);
        }

        return binding;
    }

    /// <summary>A dangling argument that is not a name: an extra positional one, in R2025b's words.</summary>
    private static JgsRuntimeException NotAName(ArgumentsSignature signature, int index, int line, int col) =>
        signature.Positional.Count == 0 && signature.Repeating.Count == 0
            ? new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", $"Invalid argument at position {index + 1}. A name is expected.")
            : new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                $"Invalid argument at position {index + 1}. Function requires {PositionalCount(signature)} positional input(s).");

    /// <summary>"exactly n" when every positional input is required; "at most n" when some have defaults (not measured).</summary>
    private static string PositionalCount(ArgumentsSignature signature) =>
        signature.Required == signature.Positional.Count
            ? $"exactly {signature.Positional.Count}"
            : $"at most {signature.Positional.Count}";

    /// <summary>Whether a value can be a name: a character row or a string scalar with something in it.</summary>
    private static bool IsName(JgsValue value)
    {
        if (JgsBuiltins.IsStringScalar(value))
        {
            return !string.IsNullOrEmpty(JgsBuiltins.TextOf(value));
        }

        if (value.Type != JgsType.String)
        {
            return false;
        }

        int[] dims = JgsBuiltins.SizeDims(value);
        return dims.Length == 2 && dims[0] == 1 && dims[1] > 0;
    }

    /// <summary>
    /// The name-value line <paramref name="given"/> names: the one it equals ignoring case, else the
    /// one it begins without ambiguity. Quietly, an unknown or ambiguous name answers null; otherwise
    /// it is R2025b's refusal.
    /// </summary>
    private static ArgumentSpec? Match(ArgumentsSignature signature, string given, out bool ambiguous, int line, int col, bool quiet)
    {
        ambiguous = false;
        foreach (ArgumentSpec spec in signature.NameValue)
        {
            if (string.Equals(spec.Name, given, StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        var starts = signature.NameValue.Where(s => s.Name.StartsWith(given, StringComparison.OrdinalIgnoreCase)).ToList();
        if (starts.Count == 1)
        {
            return starts[0];
        }

        ambiguous = starts.Count > 1;
        if (quiet)
        {
            return null;
        }

        if (ambiguous)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:functionValidation:AmbiguousInputName",
                $"Ambiguous argument name '{given}'. Name must be {Alternatives(starts.Select(static s => s.Name).ToList())}.");
        }

        var names = signature.NameValue.Select(static s => s.Name).ToList();
        throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
            names.Count == 1
                ? $"Invalid argument name '{given}'. Use '{names[0]}'."
                : $"Invalid argument name '{given}'. Name must be {Alternatives(names)}.");
    }

    /// <summary>R2025b's list of names: 'A' or 'B', and 'A', 'B', or 'C' for three or more.</summary>
    private static string Alternatives(IReadOnlyList<string> names) => names.Count switch
    {
        1 => $"'{names[0]}'",
        2 => $"'{names[0]}' or '{names[1]}'",
        _ => string.Join(", ", names.Take(names.Count - 1).Select(static n => $"'{n}'")) + $", or '{names[^1]}'",
    };
}

/// <summary>
/// The declared size and class of an <c>arguments</c> line applied as R2025b applies them
/// (measured: k9_argument_messages): the class converted first, then a scalar expanded to a fixed
/// size, an empty fitted where the size can hold nothing, a vector turned to the declared
/// orientation, and anything else refused in R2025b's words after the caller's prefix.
/// </summary>
internal static class DeclaredArgument
{
    /// <summary>The prefix of a refusal of the positional input at 1-based <paramref name="position"/>.</summary>
    public static string AtPosition(int position) => $"Invalid argument at position {position.ToString(CultureInfo.InvariantCulture)}. ";

    /// <summary>The prefix of a refusal of the name-value input <paramref name="name"/>.</summary>
    public static string ForName(string name) => $"Invalid value for '{name}' argument. ";

    /// <summary>The prefix of a refusal of the output <paramref name="name"/>.</summary>
    public static string ForOutput(string name) => $"Invalid output '{name}'. ";

    /// <summary>The prefix of a refusal of the default written for <paramref name="name"/>.</summary>
    public static string ForDefault(string name) => $"Invalid default value for argument '{name}'. ";

    /// <summary>Converts and fits <paramref name="value"/> to <paramref name="spec"/>'s class and size.</summary>
    public static JgsValue Check(ArgumentSpec spec, JgsValue value, string prefix, int line, int col, JgsEnvironment env, Interpreter? interpreter)
    {
        if (spec.ClassName is { } className)
        {
            value = JgsBuiltins.ConvertDeclaredClass(className, value, prefix, line, col, env, interpreter);
        }

        return spec.Dims is { } dims ? Fit(dims, value, prefix, line, col, env) : value;
    }

    private static JgsValue Fit(IReadOnlyList<Expr?> declared, JgsValue value, string prefix, int line, int col, JgsEnvironment env)
    {
        int?[] wanted = new int?[declared.Count];
        for (int i = 0; i < declared.Count; i++)
        {
            wanted[i] = declared[i] switch
            {
                null => null,
                NumberLiteral literal => (int)Math.Round(literal.Value),
                _ => throw new JgsRuntimeException(line, col, $"A declared size must be a number or ':'."),
            };
        }

        int[] actual = JgsBuiltins.SizeDims(value);
        int count = actual.Aggregate(1, static (a, b) => a * b);

        // A scalar is expanded to a size that fixes a length above one: 1 against (1,3) is [1 1 1],
        // against (2,:) a 2-by-1 column.
        if (count == 1 && wanted.Any(static w => w > 1))
        {
            int[] target = [.. wanted.Select(static w => w ?? 1)];
            return JgsBuiltins.RepeatScalar(value, target, env, line, col);
        }

        // [] fits a size with a ':' in it, which then holds nothing: (1,:) a 1-by-0, (2,:) a 2-by-0.
        if (count == 0 && actual.Length == 2 && actual[0] == 0 && actual[1] == 0 && wanted.Any(static w => w is null))
        {
            int[] target = [.. wanted.Select(static w => w ?? 0)];
            return JgsBuiltins.Reshaped(value, target, env, line, col);
        }

        // Against a size with every length fixed, a value whose lengths other than one are the same,
        // in order, is laid out as declared: a column against (1,3), a 2-by-3 against (1,2,3).
        if (wanted.All(static w => w is not null))
        {
            int[] target = [.. wanted.Select(static w => w!.Value)];
            if (target.Where(static d => d != 1).SequenceEqual(actual.Where(static d => d != 1)))
            {
                return target.SequenceEqual(Trimmed(actual, target.Length)) ? value : JgsBuiltins.Reshaped(value, target, env, line, col);
            }
        }

        // A vector takes the declared orientation: a column against (1,:) or (1,3) is laid out as a row.
        if (wanted.Length == 2 && actual.Length == 2 && (actual[0] == 1 || actual[1] == 1)
            && (wanted[0] == 1 || wanted[1] == 1) && !(wanted[0] == 1 && wanted[1] == 1))
        {
            int along = wanted[0] == 1 ? 1 : 0;
            if (wanted[along] is null || wanted[along] == count)
            {
                int[] target = along == 1 ? [1, count] : [count, 1];
                return target[0] == actual[0] && target[1] == actual[1] ? value : JgsBuiltins.Reshaped(value, target, env, line, col);
            }
        }

        bool fits = true;
        for (int i = 0; i < Math.Max(wanted.Length, actual.Length); i++)
        {
            int have = i < actual.Length ? actual[i] : 1;
            int? want = i < wanted.Length ? wanted[i] : 1;
            fits &= want is null || want == have;
        }

        return fits
            ? value
            : throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize", prefix + "Value must be " + Describe(wanted) + ".");
    }

    /// <summary>A size padded with ones, or with its trailing ones removed, to <paramref name="length"/> entries where it can be.</summary>
    private static int[] Trimmed(int[] dims, int length)
    {
        var list = dims.ToList();
        while (list.Count < length)
        {
            list.Add(1);
        }

        while (list.Count > length && list[^1] == 1)
        {
            list.RemoveAt(list.Count - 1);
        }

        return [.. list];
    }

    /// <summary>R2025b's name for a declared size.</summary>
    private static string Describe(int?[] wanted)
    {
        string N(int? n) => n is { } v ? v.ToString(CultureInfo.InvariantCulture) : ":";
        if (wanted.Length != 2)
        {
            return "an array of size " + string.Join("-by-", wanted.Select(N));
        }

        (int? rows, int? cols) = (wanted[0], wanted[1]);
        return (rows, cols) switch
        {
            (1, 1) => "a scalar",
            (1, null) or (null, 1) => "a vector",
            (1, { } n) => $"a vector with {N(n)} elements",
            ({ } n, 1) => $"a vector with {N(n)} elements",
            (null, null) => "a matrix",
            ({ } r, null) => $"a matrix with {N(r)} rows",
            (null, { } c) => $"a matrix with {N(c)} columns",
            _ => $"a matrix of size {N(rows)}-by-{N(cols)}",
        };
    }
}
