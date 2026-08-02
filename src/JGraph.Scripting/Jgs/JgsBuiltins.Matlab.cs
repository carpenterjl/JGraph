using System.Globalization;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The builtins a MATLAB script expects but JGS never needed, plus the multiple-output forms of the ones
/// it already had. They are registered in both dialects — a JGS script is welcome to call <c>strcmp</c>
/// — because a second, dialect-gated name table would be one more thing to drift.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// MATLAB functions JGraph knows about but does not implement. Naming them explicitly turns a
    /// baffling "not recognized" into an answer: this script needs something JGraph does not have.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnsupportedFunctions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["syms"] = "symbolic math",
        ["solve"] = "symbolic math",
        ["ode23"] = "stiffness-tuned ODE solvers — ode45 is implemented",
        ["fmincon"] = "optimization",
        ["fminsearch"] = "optimization",
        ["lsqcurvefit"] = "optimization",
        ["readmatrix"] = "readmatrix — use readcsv or readtable",
        ["uifigure"] = "app building",
        ["uicontrol"] = "app building",
        ["parfeval"] = "parallel execution",
        ["gpuArray"] = "GPU arrays",
        ["addpath"] = "a search path — files resolve against the script's folder and the workspace root",
        ["rmpath"] = "a search path — files resolve against the script's folder and the workspace root",
    };

    /// <summary>Whether <paramref name="name"/> is a MATLAB function JGraph deliberately does not have.</summary>
    internal static bool IsUnsupportedMatlabFunction(string name, out string what) =>
        UnsupportedFunctions.TryGetValue(name, out what!);

    /// <summary>Registers the MATLAB-facing builtins into <paramref name="env"/>.</summary>
    private static void RegisterMatlabBuiltins(
        JgsEnvironment env, JGraphScriptGlobals host, Random random, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // --- Numeric ----------------------------------------------------------------------------
        Define("rem", (args, line, col) =>
        {
            Arity("rem", args, 2, line, col);

            // rem keeps the sign of the dividend, where mod keeps the sign of the divisor.
            double divisor = Num("rem", args, 1, line, col);
            return MapNumeric("rem", args[0],
                x => divisor == 0 ? double.NaN : x - (divisor * Math.Truncate(x / divisor)), line, col);
        });

        Define("randn", (args, line, col) =>
        {
            ArityRange("randn", args, 0, 2, line, col);

            // randn(n), randn(r, c), and randn(size(x)) — the last is how a script matches an existing
            // vector's length, which is the common case in a measurement script.
            int count = args.Count switch
            {
                0 => 1,
                1 when args[0].Type == JgsType.Array => (int)ToDoubles("randn", args[0], line, col).Aggregate(1.0, static (a, b) => a * b),
                1 => Count("randn", args, 0, line, col),
                _ => Count("randn", args, 0, line, col) * Count("randn", args, 1, line, col),
            };

            var samples = new double[Math.Max(count, 0)];
            for (int i = 0; i < samples.Length; i++)
            {
                // Box-Muller: two uniforms in, one standard normal out.
                double u1 = 1.0 - random.NextDouble(); // in (0, 1], so Log is finite
                double u2 = random.NextDouble();
                samples[i] = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            }

            return Numbers(samples);
        });

        Define("fix", (args, line, col) =>
        {
            Arity("fix", args, 1, line, col);
            return MapNumeric("fix", args[0], Math.Truncate, line, col);
        });

        // repmat tiles in two dimensions: repmat(A, m, n) is m copies down by n across, and
        // repmat(A, m) is m by m. It predates shaped arrays (M40) and used to read only the last
        // count and lay the copies end to end, so repmat([1 2; 3 4], 2, 1) came back as a flat
        // four-element row instead of a 4-by-2.
        Define("repmat", (args, line, col) =>
        {
            ArityRange("repmat", args, 2, 3, line, col);
            int down;
            int across;
            if (args.Count == 3)
            {
                down = Count("repmat", args, 1, line, col);
                across = Count("repmat", args, 2, line, col);
            }
            else
            {
                double[] counts = NumericVector("repmat", args, 1, line, col);
                if (counts.Length is not (1 or 2))
                {
                    throw new JgsRuntimeException(line, col,
                        "repmat takes a count, a [down across] pair, or two counts.");
                }

                down = (int)Math.Round(counts[0]);
                across = (int)Math.Round(counts[^1]);
            }

            if (down < 0 || across < 0)
            {
                throw new JgsRuntimeException(line, col, "repmat counts cannot be negative.");
            }

            JgsValue source = args[0];
            if (source.Type != JgsType.Array)
            {
                return JgsMatrix.BuildValues(down, across, (_, _) => source);
            }

            int rows = JgsMatrix.RowCount(source);
            int cols = JgsMatrix.ColCount(source);
            return JgsMatrix.BuildValues(
                rows * down, cols * across, (r, c) => JgsMatrix.At(source, r % rows, c % cols));
        });

        // --- Type predicates --------------------------------------------------------------------
        Define("isnumeric", (args, line, col) =>
        {
            Arity("isnumeric", args, 1, line, col);
            return JgsValue.Bool(args[0].Type is JgsType.Number or JgsType.Complex
                || (args[0].Type == JgsType.Array && AllOfType(args[0], JgsType.Number)));
        });

        Define("ischar", (args, line, col) =>
        {
            Arity("ischar", args, 1, line, col);
            return JgsValue.Bool(args[0].Type == JgsType.String);
        });

        Define("islogical", (args, line, col) =>
        {
            Arity("islogical", args, 1, line, col);

            // A mask an imaging builtin produced — edge, imbinarize, bwareaopen — is tagged logical, so
            // the predicate every MATLAB masking example opens with answers true for it.
            return JgsValue.Bool(args[0].Type == JgsType.Bool
                || (args[0].Type == JgsType.Array && AllOfType(args[0], JgsType.Bool))
                || (args[0].Type == JgsType.Image
                    && args[0].AsImage.Class == JGraph.Imaging.ImageClass.Logical));
        });

        Define("iscell", (args, line, col) =>
        {
            Arity("iscell", args, 1, line, col);
            return JgsValue.Bool(args[0].Type == JgsType.Cell);
        });

        Define("isstruct", (args, line, col) =>
        {
            Arity("isstruct", args, 1, line, col);
            return JgsValue.Bool(args[0].Type == JgsType.Struct);
        });

        // --- Strings ----------------------------------------------------------------------------
        Define("strcmp", (args, line, col) => StringCompare("strcmp", args, line, col, StringComparison.Ordinal));
        Define("strcmpi", (args, line, col) => StringCompare("strcmpi", args, line, col, StringComparison.OrdinalIgnoreCase));

        Define("strrep", (args, line, col) =>
        {
            Arity("strrep", args, 3, line, col);
            return JgsValue.Str(Str("strrep", args, 0, line, col)
                .Replace(Str("strrep", args, 1, line, col), Str("strrep", args, 2, line, col), StringComparison.Ordinal));
        });

        Define("strtrim", (args, line, col) =>
        {
            Arity("strtrim", args, 1, line, col);
            return JgsValue.Str(Str("strtrim", args, 0, line, col).Trim());
        });

        Define("strsplit", (args, line, col) =>
        {
            ArityRange("strsplit", args, 1, 2, line, col);
            string text = Str("strsplit", args, 0, line, col);
            string[] parts = args.Count == 2
                ? text.Split(Str("strsplit", args, 1, line, col), StringSplitOptions.None)
                : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return JgsValue.Cell(parts.Select(JgsValue.Str).ToArray());
        });

        Define("strjoin", (args, line, col) =>
        {
            ArityRange("strjoin", args, 1, 2, line, col);
            string separator = args.Count == 2 ? Str("strjoin", args, 1, line, col) : " ";
            IEnumerable<string> parts = Elements("strjoin", args[0], line, col).Select(static v => v.Display());
            return JgsValue.Str(string.Join(separator, parts));
        });

        Define("num2str", (args, line, col) =>
        {
            ArityRange("num2str", args, 1, 2, line, col);
            if (args.Count == 2 && args[1].Type == JgsType.String)
            {
                // num2str(x, '%08.3f'): the second argument is a sprintf format (M43).
                try
                {
                    return JgsValue.Str(JgsSprintf.FormatMatlab(args[1].AsString, [args[0]]));
                }
                catch (FormatException ex)
                {
                    throw new JgsRuntimeException(line, col, ex.Message);
                }
            }

            if (args.Count == 2)
            {
                int digits = Count("num2str", args, 1, line, col);
                return JgsValue.Str(Num("num2str", args, 0, line, col)
                    .ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
            }

            return JgsValue.Str(args[0].Display());
        });

        Define("str2double", (args, line, col) =>
        {
            Arity("str2double", args, 1, line, col);
            return JgsValue.Number(double.TryParse(
                Str("str2double", args, 0, line, col), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : double.NaN); // MATLAB answers NaN for text that is not a number
        });

        // --- Errors -----------------------------------------------------------------------------
        Define("error", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "error");
            }

            // error('id:sub', 'message', ...) — an identifier is a first argument with a colon and no
            // spaces, which is how MATLAB itself tells the two forms apart.
            string first = Str("error", args, 0, line, col);
            bool hasIdentifier = args.Count > 1 && first.Contains(':', StringComparison.Ordinal)
                && !first.Contains(' ', StringComparison.Ordinal) && !first.Contains('%', StringComparison.Ordinal);
            int start = hasIdentifier ? 1 : 0;
            throw new JgsRuntimeException(line, col, FormatMessage("error", args, start, line, col));
        });

        Define("warning", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                return JgsValue.Null;
            }

            string first = Str("warning", args, 0, line, col);
            if (first is "on" or "off")
            {
                return JgsValue.Null; // warning('off', ...) toggles state JGraph does not keep
            }

            host.WriteErr("Warning: " + FormatMessage("warning", args, 0, line, col));
            return JgsValue.Null;
        });

        Define("assert", (args, line, col) =>
        {
            ArityRange("assert", args, 1, 8, line, col);
            if (args[0].IsTruthy)
            {
                return JgsValue.Null;
            }

            throw new JgsRuntimeException(line, col,
                args.Count > 1 ? FormatMessage("assert", args, 1, line, col) : "Assertion failed.");
        });

        // --- Cells and structs ------------------------------------------------------------------
        Define("cell", (args, line, col) =>
        {
            ArityRange("cell", args, 1, 2, line, col);

            // cell(n) is an n-element row; cell(r, c) carries its grid shape (M41), so C{r, c}
            // addresses the element MATLAB means — column-major over the flat storage, like arrays.
            int rows = Count("cell", args, 0, line, col);
            int cols = args.Count == 2 ? Count("cell", args, 1, line, col) : rows;
            if (args.Count == 1)
            {
                (rows, cols) = (1, rows);
            }

            var elements = new JgsValue[rows * cols];
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i] = JgsValue.Array(System.Array.Empty<JgsValue>());
            }

            JgsValue built = JgsValue.Cell(elements);
            built.Reshape(rows, cols);
            return built;
        });

        Define("struct", (args, line, col) =>
        {
            if (args.Count % 2 != 0)
            {
                throw new JgsRuntimeException(line, col, "struct takes name/value pairs.");
            }

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < args.Count; i += 2)
            {
                fields[Str("struct", args, i, line, col)] = args[i + 1];
            }

            return JgsValue.Struct(fields);
        });

        Define("fieldnames", (args, line, col) =>
        {
            Arity("fieldnames", args, 1, line, col);
            return JgsValue.Cell(StructOf("fieldnames", args[0], line, col).Keys.Select(JgsValue.Str).ToArray());
        });

        Define("isfield", (args, line, col) =>
        {
            Arity("isfield", args, 2, line, col);
            return JgsValue.Bool(args[0].Type == JgsType.Struct
                && args[0].AsStruct.ContainsKey(Str("isfield", args, 1, line, col)));
        });

        Define("rmfield", (args, line, col) =>
        {
            Arity("rmfield", args, 2, line, col);
            var fields = new Dictionary<string, JgsValue>(StructOf("rmfield", args[0], line, col), StringComparer.Ordinal);
            string name = Str("rmfield", args, 1, line, col);
            if (!fields.Remove(name))
            {
                throw new JgsRuntimeException(line, col, $"rmfield: this struct has no field '{name}'.");
            }

            return JgsValue.Struct(fields);
        });

        Define("num2cell", (args, line, col) =>
        {
            Arity("num2cell", args, 1, line, col);
            return JgsValue.Cell(Elements("num2cell", args[0], line, col).ToArray());
        });

        Define("cell2mat", (args, line, col) =>
        {
            Arity("cell2mat", args, 1, line, col);
            if (args[0].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, $"cell2mat expects a cell array, but got a {args[0].TypeName}.");
            }

            var flat = new List<JgsValue>();
            foreach (JgsValue element in args[0].AsCell)
            {
                if (element.Type == JgsType.Array)
                {
                    for (int i = 0; i < element.ArrayLength; i++)
                    {
                        flat.Add(element.ElementAt(i));
                    }
                }
                else
                {
                    flat.Add(element);
                }
            }

            return JgsValue.Array(flat.ToArray());
        });

        // --- Applying functions -----------------------------------------------------------------
        Define("feval", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "feval needs a function to call.");
            }

            if (args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, $"feval expects a function handle, but got a {args[0].TypeName}.");
            }

            return args[0].AsCallable.Call(args.Skip(1).ToArray(), line, col);
        });

        Define("cellfun", (args, line, col) =>
        {
            if (args.Count < 2 || args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "cellfun(f, c) applies a function handle to each cell.");
            }

            if (args[1].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, $"cellfun expects a cell array, but got a {args[1].TypeName}.");
            }

            // cellfun(..., 'UniformOutput', false) hands back a cell instead of an array — without it,
            // every result has to be a scalar, exactly as MATLAB insists.
            bool uniform = true;
            for (int i = 2; i + 1 < args.Count; i += 2)
            {
                if (args[i].Type == JgsType.String
                    && string.Equals(args[i].AsString, "UniformOutput", StringComparison.OrdinalIgnoreCase))
                {
                    uniform = args[i + 1].IsTruthy;
                }
            }

            JgsValue[] source = args[1].AsCell;
            var results = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                results[i] = args[0].AsCallable.Call([source[i]], line, col);
                if (uniform && results[i].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col,
                        $"cellfun: element {i + 1} produced a {results[i].TypeName}. Add 'UniformOutput', false to collect a cell.");
                }
            }

            return uniform ? JgsValue.Array(results) : JgsValue.Cell(results);
        });

        // --- Index arithmetic -------------------------------------------------------------------
        Define("sub2ind", (args, line, col) =>
        {
            Arity("sub2ind", args, 3, line, col);
            double[] shape = ToDoubles("sub2ind", args[0], line, col);
            if (shape.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "sub2ind: the size must have at least two dimensions.");
            }

            int rows = (int)shape[0];
            int row = Count("sub2ind", args, 1, line, col) - dialect.IndexBase;
            int column = Count("sub2ind", args, 2, line, col) - dialect.IndexBase;
            return JgsValue.Number((column * rows) + row + dialect.IndexBase);
        });

        Define("ind2sub", (args, line, col) =>
        {
            Arity("ind2sub", args, 2, line, col);
            double[] shape = ToDoubles("ind2sub", args[0], line, col);
            if (shape.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "ind2sub: the size must have at least two dimensions.");
            }

            int rows = (int)shape[0];
            int flat = Count("ind2sub", args, 1, line, col) - dialect.IndexBase;
            return JgsValue.Array([
                JgsValue.Number((flat % rows) + dialect.IndexBase),
                JgsValue.Number((flat / rows) + dialect.IndexBase),
            ]);
        });

        if (dialect.IsMatlab)
        {
            WrapFormatters(env);
        }

        RegisterMultiOutputForms(env, dialect);
    }

    /// <summary>
    /// MATLAB's quotes do not decode escapes, but its formatting functions do: <c>fprintf('a\n')</c>
    /// prints a line break even though the literal holds a backslash and an 'n'. JGS decodes escapes in
    /// the literal itself, so only the MATLAB side needs this pass — and only on the format string.
    /// </summary>
    private static void WrapFormatters(JgsEnvironment env)
    {
        foreach (string name in new[] { "sprintf", "fprintf" })
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable inner = existing.AsCallable;
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                // The format is normally first; fprintf(fid, fmt, …) shifts it one slot right.
                int at = args.Count > 0 && args[0].Type == JgsType.String
                    ? 0
                    : args.Count > 1 && args[0].Type is JgsType.Number or JgsType.Bool
                        && args[1].Type == JgsType.String
                        ? 1
                        : -1;
                if (at < 0)
                {
                    return inner.Call(args, line, col);
                }

                var unescaped = new JgsValue[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    unescaped[i] = i == at ? JgsValue.Str(UnescapeFormat(args[i].AsString)) : args[i];
                }

                return inner.Call(unescaped, line, col);
            })));
        }
    }

    /// <summary>Decodes the escape sequences MATLAB's formatting functions understand.</summary>
    private static string UnescapeFormat(string format)
    {
        if (!format.Contains('\\', StringComparison.Ordinal))
        {
            return format;
        }

        var sb = new System.Text.StringBuilder(format.Length);
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '\\' || i + 1 >= format.Length)
            {
                sb.Append(format[i]);
                continue;
            }

            char next = format[++i];
            switch (next)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '0': sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                default:
                    // Not an escape MATLAB knows: both characters stand as written.
                    sb.Append('\\').Append(next);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Wraps the builtins MATLAB scripts routinely call with two outputs — <c>[r, c] = size(x)</c>,
    /// <c>[v, i] = max(x)</c>, <c>[s, i] = sort(x)</c> — so that form works while the single-value form
    /// keeps behaving exactly as it did.
    /// </summary>
    private static void RegisterMultiOutputForms(JgsEnvironment env, JgsDialect dialect)
    {
        void Wrap(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both)
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            IJgsCallable single = existing.AsCallable;
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, single.Call) { MultiOutput = both }));
        }

        Wrap("size", (args, wanted, line, col) =>
        {
            JgsValue result = SingleOf(env, "size", args, line, col);
            if (result.Type != JgsType.Array)
            {
                return [result];
            }

            var dimensions = new double[result.ArrayLength];
            for (int i = 0; i < dimensions.Length; i++)
            {
                dimensions[i] = result.ElementAt(i).AsNumber;
            }

            // MATLAB pads missing dimensions with 1 and folds the ones past the last requested
            // output into it: [r, c] = size(rgb) reports c = width * channels.
            var outputs = new JgsValue[wanted];
            for (int i = 0; i < wanted; i++)
            {
                double dim = i < dimensions.Length ? dimensions[i] : 1;
                if (i == wanted - 1)
                {
                    for (int j = wanted; j < dimensions.Length; j++)
                    {
                        dim *= dimensions[j];
                    }
                }

                outputs[i] = JgsValue.Number(dim);
            }

            return outputs;
        });

        Wrap("max", (args, _, line, col) => ExtremeWithIndex(env, "max", args, dialect, line, col));
        Wrap("min", (args, _, line, col) => ExtremeWithIndex(env, "min", args, dialect, line, col));

        Wrap("sort", (args, _, line, col) =>
        {
            JgsValue sorted = SingleOf(env, "sort", args, line, col);
            double[] original = ToDoubles("sort", args[0], line, col);
            double[] ordered = ToDoubles("sort", sorted, line, col);

            // The permutation: for each sorted position, where its value came from. Values already
            // taken are skipped so repeated values map to distinct sources.
            var used = new bool[original.Length];
            var order = new JgsValue[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
            {
                for (int j = 0; j < original.Length; j++)
                {
                    if (!used[j] && original[j].Equals(ordered[i]))
                    {
                        used[j] = true;
                        order[i] = JgsValue.Number(j + dialect.IndexBase);
                        break;
                    }
                }
            }

            return [sorted, JgsValue.Array(order)];
        });

        Wrap("find", (args, wanted, line, col) => FindSubscripts(args, dialect, wanted, line, col));

        Wrap("ind2sub", (args, _, line, col) =>
        {
            JgsValue pair = SingleOf(env, "ind2sub", args, line, col);
            return [pair.ElementAt(0), pair.ElementAt(1)];
        });

        // meshgrid computes its [X, Y] pair as one array value; the wrapped form peels the pair
        // apart for MATLAB's [X, Y] = meshgrid(x, y). The one-output form is X alone in MATLAB,
        // while JGS keeps the pair — that is what 'let [X, Y] = meshgrid(x, y)' destructures.
        if (env.TryGet("meshgrid", out JgsValue mesh) && mesh.Type == JgsType.Function)
        {
            IJgsCallable pairForm = mesh.AsCallable;
            Func<IReadOnlyList<JgsValue>, int, int, JgsValue> single = dialect.IsMatlab
                ? (args, line, col) => pairForm.Call(args, line, col).ElementAt(0)
                : pairForm.Call;
            env.Declare("meshgrid", JgsValue.Function(new BuiltinFunction("meshgrid", single)
            {
                MultiOutput = (args, _, line, col) =>
                {
                    JgsValue pair = pairForm.Call(args, line, col);
                    return [pair.ElementAt(0), pair.ElementAt(1)];
                },
            }));
        }
    }

    /// <summary>
    /// MATLAB's <c>[row, col] = find(x)</c> (optionally <c>[row, col, v]</c>): subscripts of the truthy
    /// elements, column-major over a matrix. JGraph vectors have no orientation and are treated as row
    /// vectors, so their row subscripts are all the dialect's first index.
    /// </summary>
    private static JgsValue[] FindSubscripts(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int wanted, int line, int col)
    {
        ArityRange("find", args, 1, 2, line, col);
        int origin = args.Count == 2 ? IndexOrigin("find", args, 1, line, col) : dialect.IndexBase;

        var rows = new List<JgsValue>();
        var cols = new List<JgsValue>();
        var values = new List<JgsValue>();
        JgsValue subject = args[0];

        if (JgsMatrix.IsMatrix(subject))
        {
            int height = JgsMatrix.RowCount(subject);
            int width = JgsMatrix.ColCount(subject);
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    JgsValue element = JgsMatrix.At(subject, r, c);
                    if (element.IsTruthy)
                    {
                        rows.Add(JgsValue.Number(r + origin));
                        cols.Add(JgsValue.Number(c + origin));
                        values.Add(element);
                    }
                }
            }
        }
        else
        {
            JgsValue[] elements = Arr("find", args, 0, line, col);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].IsTruthy)
                {
                    rows.Add(JgsValue.Number(origin));
                    cols.Add(JgsValue.Number(i + origin));
                    values.Add(elements[i]);
                }
            }
        }

        return wanted >= 3
            ? [JgsValue.Array(rows.ToArray()), JgsValue.Array(cols.ToArray()), JgsValue.Array(values.ToArray())]
            : [JgsValue.Array(rows.ToArray()), JgsValue.Array(cols.ToArray())];
    }

    private static JgsValue[] ExtremeWithIndex(
        JgsEnvironment env, string name, IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        JgsValue best = SingleOf(env, name, args, line, col);
        if (args.Count != 1 || args[0].Type != JgsType.Array)
        {
            return [best]; // the two-argument form is elementwise, and has no index to report
        }

        double[] values = ToDoubles(name, args[0], line, col);
        double target = best.AsNumber;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Equals(target))
            {
                return [best, JgsValue.Number(i + dialect.IndexBase)];
            }
        }

        return [best];
    }

    private static JgsValue SingleOf(
        JgsEnvironment env, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        env.TryGet(name, out JgsValue value);
        return value is { Type: JgsType.Function } callable
            ? callable.AsCallable.Call(args, line, col)
            : throw new JgsRuntimeException(line, col, $"'{name}' is not available.");
    }

    private static bool AllOfType(JgsValue array, JgsType type)
    {
        if (array.IsPacked)
        {
            return (array.PackedKind == JgsPackedKind.Bool) == (type == JgsType.Bool);
        }

        for (int i = 0; i < array.ArrayLength; i++)
        {
            if (array.ElementAt(i).Type != type)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<JgsValue> Elements(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Cell)
        {
            return value.AsCell;
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an array or cell, but got a {value.TypeName}.");
        }

        var elements = new JgsValue[value.ArrayLength];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = value.ElementAt(i);
        }

        return elements;
    }

    private static Dictionary<string, JgsValue> StructOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Struct)
        {
            return value.AsStruct;
        }

        // A struct array is a cell of structs (M41), and every element carries the same fields — so
        // fieldnames and isfield answer for the array as readily as for one element, which is what
        // MATLAB does and what a script asking about a regionprops result needs.
        if (value.Type == JgsType.Cell && value.AsCell is [{ Type: JgsType.Struct } first, ..])
        {
            return first.AsStruct;
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a struct, but got a {value.TypeName}.");
    }

    /// <summary>
    /// String comparison in MATLAB's shape: two strings give a single answer, and a cell of strings on
    /// either side gives one answer per element.
    /// </summary>
    private static JgsValue StringCompare(
        string name, IReadOnlyList<JgsValue> args, int line, int col, StringComparison comparison)
    {
        Arity(name, args, 2, line, col);
        if (args[0].Type == JgsType.Cell || args[1].Type == JgsType.Cell)
        {
            JgsValue[] cell = (args[0].Type == JgsType.Cell ? args[0] : args[1]).AsCell;
            JgsValue other = args[0].Type == JgsType.Cell ? args[1] : args[0];
            var mask = new JgsValue[cell.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = JgsValue.Bool(Same(cell[i], other, comparison));
            }

            return JgsValue.Array(mask);
        }

        return JgsValue.Bool(Same(args[0], args[1], comparison));
    }

    private static bool Same(JgsValue a, JgsValue b, StringComparison comparison) =>
        a.Type == JgsType.String && b.Type == JgsType.String
        && string.Equals(a.AsString, b.AsString, comparison);

    /// <summary>Formats an <c>error</c>/<c>warning</c>/<c>assert</c> message, honouring a format string.</summary>
    private static string FormatMessage(string name, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        string format = UnescapeFormat(Str(name, args, start, line, col));
        return args.Count > start + 1
            ? JgsSprintf.Format(format, args.Skip(start + 1).ToArray())
            : format;
    }

}
