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

        // The mustBe… family (M62). They are validators inside an arguments block and ordinary
        // builtins outside one, which is how MATLAB's are, so they register with everything else.
        RegisterValidators(Define);

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

            // Through the shared helper rather than a test of its own (M64). The two had drifted:
            // this one asked whether the elements were numbers, which a datetime's milliseconds are,
            // so isnumeric(datetime) answered true where class(datetime) said 'datetime'. That is the
            // same disagreement islogical and class were made to share a helper over.
            return JgsValue.Bool(IsNumericValue(args[0]));
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
            // the predicate every MATLAB masking example opens with answers true for it. class() reads
            // the same helper, so the two can no longer drift apart.
            return JgsValue.Bool(IsLogicalValue(args[0]));
        });

        Define("iscell", (args, line, col) =>
        {
            Arity("iscell", args, 1, line, col);
            return JgsValue.Bool(args[0].Type == JgsType.Cell);
        });

        Define("isstruct", (args, line, col) =>
        {
            Arity("isstruct", args, 1, line, col);
            return JgsValue.Bool(IsStructValue(args[0]));
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

        Define("strsplit", (args, line, col) => SplitText(args, 1, line, col)[0]);
        Define("strjoin", (args, line, col) => JoinText(args, line, col));
        Define("num2str", (args, line, col) => NumberText(args, line, col));

        // num2str writes a number for a person to read; mat2str writes one for the language to read
        // back, which is why it keeps the brackets and the semicolons (M52 wave E).
        Define("mat2str", (args, line, col) => MatrixText(args, line, col));
        Define("int2str", (args, line, col) => WholeNumberText(args, line, col));

        Define("str2double", (args, line, col) =>
        {
            Arity("str2double", args, 1, line, col);
            return JgsValue.Number(double.TryParse(
                Str("str2double", args, 0, line, col), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : double.NaN); // MATLAB answers NaN for text that is not a number
        });

        // --- Errors -----------------------------------------------------------------------------
        // error itself lives in JgsBuiltins.Errors.cs (M62), where the identifier and the MException
        // form are defined together. It is declared there rather than here because it is re-declared
        // over this registration, and two implementations of one name is one too many.
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

            return BuildStruct(args, line, col);
        });

        Define("fieldnames", (args, line, col) =>
        {
            Arity("fieldnames", args, 1, line, col);
            return JgsValue.Cell(StructOf("fieldnames", args[0], line, col).Keys.Select(JgsValue.Str).ToArray());
        });

        Define("isfield", (args, line, col) =>
        {
            Arity("isfield", args, 2, line, col);
            if (args[0].Type != JgsType.Struct)
            {
                return JgsValue.Bool(false);
            }

            // A cell of names asks about each in turn, and the answer is a logical of the same shape.
            Dictionary<string, JgsValue> fields = args[0].AsStruct;
            if (args[1].Type == JgsType.Cell)
            {
                JgsValue[] names = args[1].AsCell;
                var flags = new JgsValue[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    flags[i] = JgsValue.Bool(names[i].Type == JgsType.String && fields.ContainsKey(names[i].AsString));
                }

                return JgsValue.Shaped(flags, args[1].Rows, args[1].Cols);
            }

            return JgsValue.Bool(fields.ContainsKey(Str("isfield", args, 1, line, col)));
        });

        Define("rmfield", (args, line, col) =>
        {
            Arity("rmfield", args, 2, line, col);
            string[] doomed = FieldNameList("rmfield", args[1], line, col);
            return MapStructElements("rmfield", args[0], line, col, element =>
            {
                var fields = new Dictionary<string, JgsValue>(element, StringComparer.Ordinal);
                foreach (string name in doomed)
                {
                    if (!fields.Remove(name))
                    {
                        throw new JgsRuntimeException(line, col, $"rmfield: this struct has no field '{name}'.");
                    }
                }

                return fields;
            });
        });

        Define("orderfields", (args, line, col) =>
        {
            ArityRange("orderfields", args, 1, 2, line, col);

            // With no order given the fields sort by name, which is the whole point of the verb:
            // two structs built in different orders compare and display alike afterwards.
            string[] order = args.Count > 1
                ? FieldNameList("orderfields", args[1], line, col)
                : [.. StructOf("orderfields", args[0], line, col).Keys.OrderBy(n => n, StringComparer.Ordinal)];

            return MapStructElements("orderfields", args[0], line, col, element =>
            {
                var ordered = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (string name in order)
                {
                    if (!element.TryGetValue(name, out JgsValue? held))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"orderfields: this struct has no field '{name}'.");
                    }

                    ordered[name] = held;
                }

                if (ordered.Count != element.Count)
                {
                    throw new JgsRuntimeException(line, col,
                        "orderfields: the order must name every field exactly once.");
                }

                return ordered;
            });
        });

        Define("getfield", (args, line, col) =>
        {
            ArityRange("getfield", args, 2, 2, line, col);
            Dictionary<string, JgsValue> fields = StructOf("getfield", args[0], line, col);
            string name = Str("getfield", args, 1, line, col);
            return fields.TryGetValue(name, out JgsValue? held)
                ? held
                : throw new JgsRuntimeException(line, col, $"getfield: this struct has no field '{name}'.");
        });

        Define("setfield", (args, line, col) =>
        {
            Arity("setfield", args, 3, line, col);
            string name = Str("setfield", args, 1, line, col);
            JgsValue held = args[2];

            // setfield answers with a changed copy and leaves its argument alone, which is what
            // makes it usable in an expression: s = setfield(s, 'a', 1).
            return MapStructElements("setfield", args[0], line, col, element =>
                new Dictionary<string, JgsValue>(element, StringComparer.Ordinal) { [name] = held });
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

        // cellfun(..., 'UniformOutput', false) hands back a cell instead of an array — without it,
        // every result has to be a scalar, exactly as MATLAB insists.
        Define("cellfun", (args, line, col) => ApplyOverCells(env, args, 1, line, col)[0]);

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

        // deal exists only to feed several outputs at once, so it is declared where the several-output
        // forms are (M52 wave E). One value is handed to every output; several must match them.
        env.Declare("deal", JgsValue.Function(new BuiltinFunction(
            "deal",
            (args, line, col) => Dealt(args, 1, line, col)[0])
        {
            MultiOutput = Dealt,
        }));

        // An animated line's points come back one coordinate per output, and how many there are is
        // fixed by the line rather than by the call — so a flat line asked for three says so.
        Wrap("getpoints", (args, wanted, line, col) =>
        {
            JgsValue[] coordinates = GetPoints(args, line, col);
            return wanted <= coordinates.Length
                ? coordinates
                : throw new JgsRuntimeException(line, col,
                    $"getpoints: this line has {coordinates.Length} coordinates, not {wanted}.");
        });

        Wrap("size", (args, wanted, line, col) =>
        {
            JgsValue result = SingleOf(env, "size", args, line, col);
            if (result.Type != JgsType.Array)
            {
                return [result];
            }

            if (args.Count > 1)
            {
                // The call already said which dimensions it wants, so each output is one of them
                // rather than a share of every dimension there is — no folding, no padding.
                var picked = new JgsValue[System.Math.Max(1, wanted)];
                for (int i = 0; i < picked.Length; i++)
                {
                    picked[i] = i < result.ArrayLength ? result.ElementAt(i) : JgsValue.Number(1);
                }

                return picked;
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

        // [C, ia, ic] = unique(...). Wrapped here rather than declared with its outputs because the
        // one-output form is registered with the base builtins, before this file runs.
        Wrap("unique", (args, wanted, line, col) => UniqueParts(args, dialect, wanted, line, col));

        // [C, matches] = strsplit(...) reports the delimiters it actually cut on, and
        // [a, b] = cellfun(...) asks each element's function for that many answers.
        Wrap("strsplit", (args, wanted, line, col) => SplitText(args, wanted, line, col));
        Wrap("cellfun", (args, wanted, line, col) => ApplyOverCells(env, args, wanted, line, col));

        Wrap("ind2sub", (args, _, line, col) =>
        {
            JgsValue pair = SingleOf(env, "ind2sub", args, line, col);
            return [pair.ElementAt(0), pair.ElementAt(1)];
        });

        // meshgrid and ndgrid compute their whole set of grids as one array value; the wrapped form
        // peels that set apart for MATLAB's [X, Y, Z] = meshgrid(x, y, z). The one-output form is X
        // alone in MATLAB, while JGS keeps the set — that is what 'let [X, Y] = meshgrid(x, y)'
        // destructures. M59 made both names N-dimensional, so the peeling counts the grids rather
        // than assuming there are two of them.
        foreach (string name in (string[])["meshgrid", "ndgrid"])
        {
            if (!env.TryGet(name, out JgsValue grid) || grid.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable setForm = grid.AsCallable;
            Func<IReadOnlyList<JgsValue>, int, int, JgsValue> single = dialect.IsMatlab
                ? (args, line, col) => setForm.Call(args, line, col).ElementAt(0)
                : setForm.Call;
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, single)
            {
                MultiOutput = (args, wanted, line, col) =>
                {
                    JgsValue set = setForm.Call(args, line, col);
                    int available = set.ArrayLength;
                    int answers = System.Math.Clamp(wanted, 1, available);
                    var grids = new JgsValue[answers];
                    for (int i = 0; i < answers; i++)
                    {
                        grids[i] = set.ElementAt(i);
                    }

                    return grids;
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
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        ArityRange("find", args, 1, dialect.IsMatlab ? 3 : 2, line, col);

        // A sparse matrix answers find from what it stores rather than from what it stands for, which
        // is the reason it is stored that way: a pattern with a thousand entries in a matrix of a
        // billion places should cost a thousand.
        if (args.Count > 0 && args[0].Type == JgsType.Sparse)
        {
            return SparseFind(args[0].AsSparse, outputs, dialect, line, col);
        }

        (int origin, int? wanted, bool fromEnd) = FindLimit("find", args, dialect, line, col);

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

        // All three lists were filled in step, so limiting them the same way keeps them in step.
        rows = Limited(rows, wanted, fromEnd);
        cols = Limited(cols, wanted, fromEnd);
        values = Limited(values, wanted, fromEnd);

        // Subscripts stand up the same way the single-output form's linear indices do: a row for a row
        // vector, a column for anything else. Without this the two forms of the same call disagreed
        // about shape, which stess_24 caught (M52).
        JgsValue Shaped(List<JgsValue> found) => FoundIndices(JgsValue.Array(found.ToArray()), subject);

        return outputs >= 3
            ? [Shaped(rows), Shaped(cols), Shaped(values)]
            : [Shaped(rows), Shaped(cols)];
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

    /// <summary>
    /// <c>struct(...)</c>: name/value pairs, where a cell value spreads across the elements of a
    /// struct array (M65). <c>struct('a', {1, 2, 3})</c> is three elements, and <c>struct('a', {})</c>
    /// is an empty struct array that still has the field — the two forms a script uses to preallocate.
    /// </summary>
    private static JgsValue BuildStruct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var names = new string[args.Count / 2];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = Str("struct", args, i * 2, line, col);
        }

        // The element count is the size of the cell values, which must agree; a non-cell value is
        // the same in every element, and all-scalar arguments make a 1-by-1.
        int count = 1;
        int rows = 1;
        int cols = 1;
        for (int i = 0; i < names.Length; i++)
        {
            if (args[(i * 2) + 1].Type != JgsType.Cell)
            {
                continue;
            }

            JgsValue cell = args[(i * 2) + 1];
            if (count != 1 && cell.AsCell.Length != count)
            {
                throw new JgsRuntimeException(line, col,
                    $"struct: '{names[i]}' has {cell.AsCell.Length} values but another field has {count}.");
            }

            count = cell.AsCell.Length;
            rows = cell.Rows;
            cols = cell.Cols;
        }

        var elements = new Dictionary<string, JgsValue>[count];
        for (int e = 0; e < count; e++)
        {
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                JgsValue given = args[(i * 2) + 1];
                fields[names[i]] = given.Type == JgsType.Cell ? given.AsCell[e] : given;
            }

            elements[e] = fields;
        }

        return JgsValue.StructArray(new JgsStructArray(elements, names), count == 0 ? 0 : rows, count == 0 ? 0 : cols);
    }

    /// <summary>One field name, or a cell of them — the shape <c>rmfield</c> and friends accept.</summary>
    private static string[] FieldNameList(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Cell)
        {
            var names = new string[value.AsCell.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = value.AsCell[i].Type == JgsType.String
                    ? value.AsCell[i].AsString
                    : throw new JgsRuntimeException(line, col, $"{name} expects field names as text.");
            }

            return names;
        }

        return value.Type == JgsType.String
            ? [value.AsString]
            : throw new JgsRuntimeException(line, col, $"{name} expects a field name, but got a {value.TypeName}.");
    }

    /// <summary>
    /// Rebuilds a struct value element by element, keeping its shape. This is what makes the
    /// field-editing verbs array-aware in one place: before M65 they read the first element and
    /// answered with a lone struct, so <c>rmfield</c> on a struct array silently dropped every
    /// element but one.
    /// </summary>
    private static JgsValue MapStructElements(
        string name,
        JgsValue value,
        int line,
        int col,
        Func<Dictionary<string, JgsValue>, Dictionary<string, JgsValue>> edit)
    {
        if (value.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a struct, but got a {value.TypeName}.");
        }

        JgsStructArray payload = value.AsStructArray;
        var edited = new Dictionary<string, JgsValue>[payload.Length];
        for (int i = 0; i < edited.Length; i++)
        {
            edited[i] = edit(payload.Elements[i]);
        }

        string[] fields = edited.Length > 0 ? [.. edited[0].Keys] : payload.EmptyFields;
        return JgsValue.StructArray(new JgsStructArray(edited, fields), value.Rows, value.Cols);
    }

    private static Dictionary<string, JgsValue> StructOf(string name, JgsValue value, int line, int col)
    {
        // Every element of a struct array carries the same fields (M65), so fieldnames and isfield
        // answer for the array as readily as for one element — which is what MATLAB does and what a
        // script asking about a regionprops result needs.
        if (value.Type == JgsType.Struct)
        {
            return value.AsStruct;
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
