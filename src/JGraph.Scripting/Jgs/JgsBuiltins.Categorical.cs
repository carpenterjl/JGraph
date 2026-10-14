using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs of <c>categorical</c> (open item 115): the constructor and its refusals, the category
/// verbs, the counts and summaries, the conversions, and the ordinary verbs taught to answer over a
/// categorical — each measured in R2025b (probe_115, probe_115b in the batch 19 scratch).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterCategoricalBuiltins(JgsEnvironment env, Action<string> print)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // A verb that already exists learns the categorical case: the wrapper answers it and hands
        // everything else to the verb as it was, outputs and all.
        void Wrap(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> categorical)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            IJgsCallable inner = existing.AsCallable;
            JgsValue[] Both(IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                categorical(args, wanted, line, col)
                ?? (inner is IJgsMultiCallable multi ? multi.CallMultiple(args, Math.Max(wanted, 1), line, col) : [inner.Call(args, line, col)]);
            env.Builtins.Register(name, JgsValue.Function(LikeInner(inner, new BuiltinFunction(name, (args, line, col) => Both(args, 1, line, col)[0])
            {
                MultiOutput = Both,
                BindsAnsAsStatement = inner is not BuiltinFunction { BindsAnsAsStatement: false },
                AutoCallsBare = inner is BuiltinFunction { AutoCallsBare: true },
                KnowsWhenDiscarded = inner is BuiltinFunction { KnowsWhenDiscarded: true },
                TakesOutputCount = inner is BuiltinFunction { TakesOutputCount: true },
            })));
        }

        static bool AnyCategorical(IReadOnlyList<JgsValue> args) => args.Any(static a => a.IsCategorical);
        static BuiltinFunction LikeInner(IJgsCallable inner, BuiltinFunction wrapper)
        {
            if (inner is BuiltinFunction { KeepsStringArguments: true })
            {
                wrapper.KeepsStringArguments = true; // the only flag not decided by the name
            }

            return wrapper;
        }
        static JgsCategoricalTag TagOf(JgsValue value) => value.CategoricalTag!;

        // A string scalar is text to categorical, not a char row (open item 123: categorical("a", ...)).
        env.Builtins.Register("categorical", JgsValue.Function(new BuiltinFunction("categorical", (args, line, col) => Construct(
            [.. args.Select(static a => BuiltinFunction.IsStringScalar(a) && a.ElementAt(0).AsString is var text
                && (text.Equals("Ordinal", StringComparison.OrdinalIgnoreCase) || text.Equals("Protected", StringComparison.OrdinalIgnoreCase))
                ? a.ElementAt(0) : a)], line, col)) { KeepsStringArguments = true }));
        Define("iscategorical", (args, line, col) => { Arity("iscategorical", args, 1, line, col); return JgsValue.Bool(args[0].IsCategorical); });
        Define("isordinal", (args, line, col) => { Arity("isordinal", args, 1, line, col); return JgsValue.Bool(RequireCategorical("isordinal", args[0], line, col).Ordinal); });
        Define("isprotected", (args, line, col) => { Arity("isprotected", args, 1, line, col); return JgsValue.Bool(RequireCategorical("isprotected", args[0], line, col).Protected); });
        Define("categories", (args, line, col) => { Arity("categories", args, 1, line, col); return JgsCategorical.CategoriesColumn(RequireCategorical("categories", args[0], line, col)); });
        Define("isundefined", (args, line, col) =>
        {
            Arity("isundefined", args, 1, line, col);
            RequireCategorical("isundefined", args[0], line, col);
            return MapCodes(args[0], static code => double.IsNaN(code));
        });
        Define("iscategory", (args, line, col) =>
        {
            Arity("iscategory", args, 2, line, col);
            JgsCategoricalTag tag = RequireCategorical("iscategory", args[0], line, col);
            string?[] names = JgsCategorical.TextNames(args[1]) ?? throw new JgsRuntimeException(line, col, "iscategory expects category names as text.");
            var flags = new JgsValue[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                flags[i] = JgsValue.Bool(names[i] is not null && tag.CodeOf(names[i]!) != 0);
            }

            return flags.Length == 1 ? flags[0] : JgsMatrix.FromElements(flags, 1, flags.Length);
        });
        Define("countcats", (args, line, col) => CountCats(args, line, col));
        Define("addcats", (args, line, col) => AddCats(args, line, col));
        Define("removecats", (args, line, col) => RemoveCats(args, line, col));
        Define("mergecats", (args, line, col) => MergeCats(args, line, col));
        Define("renamecats", (args, line, col) => RenameCats(args, line, col));
        Define("reordercats", (args, line, col) => ReorderCats(args, line, col));
        Define("setcats", (args, line, col) => SetCats(args, line, col));

        // summary over a categorical prints R2025b's table when nothing is asked for, and answers
        // its struct otherwise; a table or anything else keeps the verb it had.
        if (env.Builtins.TryGet("summary", out JgsValue priorSummary) && priorSummary.Type == JgsType.Function)
        {
            IJgsCallable prior = priorSummary.AsCallable;
            env.Builtins.Register("summary", JgsValue.Function(new BuiltinFunction("summary",
                (args, line, col) => args.Count == 1 && args[0].IsCategorical ? SummaryStruct(args[0]) : prior.Call(args, line, col))
            {
                MultiOutput = (args, wanted, line, col) =>
                {
                    if (args.Count != 1 || !args[0].IsCategorical)
                    {
                        return prior is IJgsMultiCallable multi ? multi.CallMultiple(args, Math.Max(wanted, 1), line, col) : [prior.Call(args, line, col)];
                    }

                    if (wanted == 0)
                    {
                        print(SummaryText(args[0]));
                        return [];
                    }

                    return [SummaryStruct(args[0])];
                },
                TakesOutputCount = true,
            }));
        }

        Wrap("histcounts", (args, wanted, line, col) =>
        {
            if (args.Count != 1 || !args[0].IsCategorical)
            {
                return null;
            }

            JgsCategoricalTag tag = TagOf(args[0]);
            double[] counts = Counts(args[0]);
            JgsValue cats = JgsValue.Cell([.. tag.Categories.Select(JgsValue.Str)]);
            cats.Reshape(1, tag.Categories.Length);
            return [JgsMatrix.FromColumnMajor(counts, 1, counts.Length), cats];
        });

        // --- Conversions -----------------------------------------------------------------------

        Wrap("string", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsCategorical.ToStringArray(args[0])] : null);
        Wrap("cellstr", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsCategorical.ToCellstr(args[0])] : null);
        // One name is a char row, several a char matrix padded with blanks (open item 122: arrayfun's char(x)).
        Wrap("char", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical
            ? [JgsCategorical.NamesOf(args[0]) is [string only] ? JgsValue.Str(only) : JgsValue.CharMatrix(JgsCategorical.NamesOf(args[0]))]
            : null);
        Wrap("double", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsCategorical.ToDouble(args[0])] : null);
        foreach (string numeric in (string[])["single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64"])
        {
            string verb = numeric;
            Wrap(verb, (args, wanted, line, col) =>
            {
                if (args.Count != 1 || !args[0].IsCategorical)
                {
                    return null;
                }

                IJgsCallable cast = env.Builtins.TryGet(verb, out JgsValue f) ? f.AsCallable : throw new JgsRuntimeException(line, col, $"{verb} is not available.");
                return [cast.Call([JgsCategorical.ToDouble(args[0])], line, col)];
            });
        }

        Wrap("logical", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion", "Conversion to logical from categorical is not possible.")
            : null);
        Wrap("cell", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion", "Conversion to cell from categorical is not possible.")
            : null);
        Wrap("num2str", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:num2str:nonNumericInput", "Input to num2str must be numeric.")
            : null);
        Wrap("mat2str", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:mat2str:NumericInput", "Input matrix must be a numeric array, character array, or string array.")
            : null);
        Wrap("num2cell", (args, wanted, line, col) =>
        {
            if (args.Count != 1 || !args[0].IsCategorical)
            {
                return null;
            }

            JgsValue cell = JgsValue.Cell(Elements(args[0]));
            cell.Reshape(args[0].Rows, args[0].Cols);
            return [cell];
        });
        Wrap("jsonencode", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            IJgsCallable inner = env.Builtins.TryGet("jsonencode", out JgsValue f) ? f.AsCallable : throw new JgsRuntimeException(line, col, "jsonencode is not available.");
            var replaced = new List<JgsValue>(args) { [0] = JgsCategorical.ToStringArray(args[0]) };
            return [inner.Call(replaced, line, col)];
        });

        // --- Predicates --------------------------------------------------------------------------

        Wrap("isobject", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsValue.Bool(true)] : null);
        Wrap("strcat", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:strcat:InvalidInputType", "Inputs must be character vectors, cell arrays of character vectors, or string arrays.")
            : null);
        Wrap("strrep", (args, wanted, line, col) => args.Count > 0 && args[0].IsCategorical ? [args[0]] : null); // R2025b warns and answers the array as it was
        foreach (string printer in (string[])["sprintf", "fprintf"])
        {
            string verb = printer;
            Wrap(verb, (args, wanted, line, col) =>
            {
                if (args.Count < 2 || !AnyCategorical(args))
                {
                    return null;
                }

                // %s reads the names and a numeric conversion the codes, as R2025b's sprintf and
                // fprintf do. fprintf's format may sit after a file id.
                IJgsCallable inner = env.Builtins.TryGet(verb, out JgsValue f) ? f.AsCallable : throw new JgsRuntimeException(line, col, $"{verb} is not available.");
                int formatAt = verb == "fprintf" && args[0].Type is JgsType.Number or JgsType.Bool ? 1 : 0;
                string format = IsTextScalar(args[formatAt]) ? TextOf(args[formatAt]) : string.Empty;
                bool numeric = format.Contains("%d") || format.Contains("%i") || format.Contains("%u") || format.Contains("%f") || format.Contains("%g") || format.Contains("%e");
                var replaced = new JgsValue[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    replaced[i] = args[i].IsCategorical ? (numeric ? JgsCategorical.ToDouble(args[i]) : JgsCategorical.ToStringArray(args[i])) : args[i];
                }

                return [inner.Call(replaced, line, col)];
            });
        }
        Wrap("isreal", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsValue.Bool(false)] : null);
        Wrap("isfloat", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsValue.Bool(false)] : null);
        Wrap("isinteger", (args, wanted, line, col) => args.Count == 1 && args[0].IsCategorical ? [JgsValue.Bool(false)] : null);
        Wrap("ismissing", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            if (args.Count == 1)
            {
                return [MapCodes(args[0], static code => double.IsNaN(code))];
            }

            string?[] indicators = JgsCategorical.TextNames(args[1]) ?? [];
            string[] names = JgsCategorical.NamesOf(args[0]);
            return [ShapedFlags(args[0], names.Select(n => indicators.Contains(n, StringComparer.Ordinal)))];
        });
        foreach (string compare in (string[])["strcmp", "strcmpi", "strncmp", "strncmpi"])
        {
            Wrap(compare, (args, wanted, line, col) =>
            {
                if (!AnyCategorical(args))
                {
                    return null;
                }

                // R2025b compares a categorical with nothing: false, in the shape of the other side.
                JgsValue other = args[0].IsCategorical ? (args.Count > 1 ? args[1] : args[0]) : args[0];
                int count = other.Type == JgsType.Cell ? other.AsCell.Length : other.IsStringArray ? other.ArrayLength : 1;
                var flags = new JgsValue[count];
                Array.Fill(flags, JgsValue.Bool(false));
                return [count == 1 ? flags[0] : JgsMatrix.FromElements(flags, other.Rows, other.Cols)];
            });
        }

        // --- Set and order verbs -------------------------------------------------------------------

        Wrap("ismember", (args, wanted, line, col) =>
        {
            if (args.Count < 2 || (!args[0].IsCategorical && !args[1].IsCategorical))
            {
                return null;
            }

            string?[] wantedNames = args[0].IsCategorical ? JgsCategorical.NamesOf(args[0]).Select(static n => n == JgsCategorical.Undefined ? null : n).ToArray()
                : JgsCategorical.TextNames(args[0]) ?? throw new JgsRuntimeException(line, col, "ismember with a categorical takes text or a categorical.");
            string?[] set = args[1].IsCategorical ? JgsCategorical.NamesOf(args[1]).Select(static n => n == JgsCategorical.Undefined ? null : n).ToArray()
                : JgsCategorical.TextNames(args[1]) ?? throw new JgsRuntimeException(line, col, "ismember with a categorical takes text or a categorical.");
            var flags = new JgsValue[wantedNames.Length];
            var where = new double[wantedNames.Length];
            for (int i = 0; i < wantedNames.Length; i++)
            {
                int at = wantedNames[i] is null ? -1 : Array.IndexOf(set, wantedNames[i]);
                flags[i] = JgsValue.Bool(at >= 0);
                where[i] = at + 1;
            }

            JgsValue shapeOf = args[0].IsCategorical || args[0].Type is JgsType.Cell || args[0].IsStringArray ? args[0] : JgsValue.Number(0);
            JgsValue tf = flags.Length == 1 ? flags[0] : JgsMatrix.FromElements(flags, shapeOf.Rows, shapeOf.Cols);
            JgsValue loc = where.Length == 1 ? JgsValue.Number(where[0]) : JgsMatrix.FromColumnMajor(where, shapeOf.Rows, shapeOf.Cols);
            return [tf, loc];
        });

        Wrap("sort", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            bool descend = args.Skip(1).Any(a => IsTextScalar(a) && TextOf(a).Equals("descend", StringComparison.OrdinalIgnoreCase));
            int dim = args.Count > 1 && args[1].Type == JgsType.Number ? (int)args[1].AsNumber : (args[0].Rows == 1 ? 2 : 1);
            (double[] codes, double[] order) = SortCodes(args[0], descend, dim);
            return [JgsCategorical.FromCodesLike(codes, args[0], TagOf(args[0])), JgsMatrix.FromColumnMajor(order, args[0].Rows, args[0].Cols)];
        });
        Wrap("issorted", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            double[] codes = JgsCategorical.CodesOf(args[0]);
            for (int i = 1; i < codes.Length; i++)
            {
                double a = double.IsNaN(codes[i - 1]) ? double.PositiveInfinity : codes[i - 1];
                double b = double.IsNaN(codes[i]) ? double.PositiveInfinity : codes[i];
                if (a > b)
                {
                    return [JgsValue.Bool(false)];
                }
            }

            return [JgsValue.Bool(true)];
        });
        Wrap("unique", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            bool stable = args.Skip(1).Any(a => IsTextScalar(a) && TextOf(a).Equals("stable", StringComparison.OrdinalIgnoreCase));
            double[] codes = JgsCategorical.CodesOf(args[0]);
            var firstAt = new Dictionary<double, int>();
            var seen = new List<double>();
            var ic = new double[codes.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                double key = double.IsNaN(codes[i]) ? double.PositiveInfinity : codes[i];
                if (!firstAt.ContainsKey(key))
                {
                    firstAt[key] = i;
                    seen.Add(key);
                }
            }

            // Every undefined element is its own unique value in R2025b (probe_115: two of them stay two).
            var ordered = stable ? seen : [.. seen.OrderBy(static k => k)];
            var kept = new List<double>();
            var ia = new List<double>();
            foreach (double key in ordered)
            {
                if (double.IsPositiveInfinity(key))
                {
                    for (int i = 0; i < codes.Length; i++)
                    {
                        if (double.IsNaN(codes[i]))
                        {
                            kept.Add(double.NaN);
                            ia.Add(i + 1);
                        }
                    }

                    continue;
                }

                kept.Add(key);
                ia.Add(firstAt[key] + 1);
            }

            for (int i = 0; i < codes.Length; i++)
            {
                double key = double.IsNaN(codes[i]) ? double.NaN : codes[i];
                ic[i] = double.IsNaN(key) ? kept.FindIndex(k => double.IsNaN(k)) + 1 : kept.IndexOf(key) + 1;
            }

            bool column = args[0].Cols == 1 && args[0].Rows > 1;
            JgsValue values = column
                ? JgsCategorical.FromCodes([.. kept], kept.Count, 1, TagOf(args[0]))
                : JgsCategorical.FromCodes([.. kept], 1, kept.Count, TagOf(args[0]));
            return [values, JgsMatrix.FromColumnMajor([.. ia], ia.Count, 1), JgsMatrix.FromColumnMajor(ic, ic.Length, 1)];
        });
        foreach (string extreme in (string[])["min", "max"])
        {
            bool takeMin = extreme == "min";
            Wrap(extreme, (args, wanted, line, col) =>
            {
                if (args.Count == 0 || !args[0].IsCategorical)
                {
                    return null;
                }

                JgsCategoricalTag tag = TagOf(args[0]);
                if (!tag.Ordinal)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:categorical:NotOrdinal",
                        "Relational comparisons are not allowed for categorical arrays that are not ordinal.");
                }

                bool includeMissing = args.Skip(1).Any(a => IsTextScalar(a) && TextOf(a).Equals("includemissing", StringComparison.OrdinalIgnoreCase));
                if (args.Count >= 2 && args[1].IsCategorical)
                {
                    // max(a, b) element by element, a scalar expanded.
                    double[] a = JgsCategorical.CodesOf(args[0]);
                    double[] b = JgsCategorical.CodesOf(args[1]);
                    int n = Math.Max(a.Length, b.Length);
                    var picked = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double x = a[a.Length == 1 ? 0 : i];
                        double y = b[b.Length == 1 ? 0 : i];
                        picked[i] = double.IsNaN(x) ? y : double.IsNaN(y) ? x : takeMin ? Math.Min(x, y) : Math.Max(x, y);
                    }

                    JgsValue shapeOf = a.Length >= b.Length ? args[0] : args[1];
                    return [JgsCategorical.FromCodesLike(picked, shapeOf, tag)];
                }

                double[] codes = JgsCategorical.CodesOf(args[0]);
                double best = double.NaN;
                int at = 0;
                bool anyMissing = false;
                for (int i = 0; i < codes.Length; i++)
                {
                    if (double.IsNaN(codes[i]))
                    {
                        anyMissing = true;
                        continue;
                    }

                    if (double.IsNaN(best) || (takeMin ? codes[i] < best : codes[i] > best))
                    {
                        best = codes[i];
                        at = i + 1;
                    }
                }

                if (includeMissing && anyMissing)
                {
                    best = double.NaN;
                }

                return [JgsCategorical.FromCodes([best], 1, 1, tag), JgsValue.Number(at)];
            });
        }

        Wrap("median", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            JgsCategoricalTag tag = TagOf(args[0]);
            if (!tag.Ordinal)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:median:NotOrdinal", "Input categorical must be ordinal.");
            }

            double[] present = [.. JgsCategorical.CodesOf(args[0]).Where(static c => !double.IsNaN(c)).OrderBy(static c => c)];
            double mid = present.Length == 0 ? double.NaN
                : present.Length % 2 == 1 ? present[present.Length / 2]
                : Math.Round((present[(present.Length / 2) - 1] + present[present.Length / 2]) / 2, MidpointRounding.AwayFromZero);
            return [JgsCategorical.FromCodes([mid], 1, 1, tag)];
        });
        Wrap("mode", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            double[] counts = Counts(args[0]);
            int best = -1;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0 && (best < 0 || counts[i] > counts[best]))
                {
                    best = i;
                }
            }

            return [JgsCategorical.FromCodes([best < 0 ? double.NaN : best + 1], 1, 1, TagOf(args[0]))];
        });
        Wrap("mean", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:sum:wrongInput", "Invalid data type. First argument must be numeric or logical.")
            : null);
        Wrap("sum", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:sum:wrongInput", "Invalid data type. First argument must be numeric or logical.")
            : null);
        // R2025b's own words for three of the arithmetic verbs (open item 123, probe_123).
        Wrap("cumsum", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:cumsum:wrongInput", "Invalid data type. First input argument must be numeric or logical.")
            : null);
        Wrap("round", (args, wanted, line, col) => AnyCategorical(args)
            ? throw new JgsRuntimeException(line, col, "MATLAB:round:FirstArg", "First argument must be a numeric, logical, or char array.")
            : null);
        Wrap("times", (args, wanted, line, col) => args.Count == 2 && args[0].IsCategorical != args[1].IsCategorical
            ? throw JgsCategorical.TimesMismatch(line, col)
            : AnyCategorical(args) ? throw JgsCategorical.NotNumeric(line, col) : null);
        foreach (string arithmetic in (string[])["abs", "sqrt", "exp", "log", "log10", "floor", "ceil", "fix", "sign", "prod", "diff", "mod", "rem", "power", "plus", "minus", "rdivide", "mtimes"])
        {
            // The arithmetic verbs are R2025b's refusal on a categorical (probe_115b).
            Wrap(arithmetic, (args, wanted, line, col) => AnyCategorical(args)
                ? throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical.")
                : null);
        }

        foreach (string setVerb in (string[])["intersect", "union", "setdiff"])
        {
            string verb = setVerb;
            Wrap(verb, (args, wanted, line, col) =>
            {
                if (args.Count < 2 || (!args[0].IsCategorical && !args[1].IsCategorical))
                {
                    return null;
                }

                JgsValue first = args[0].IsCategorical ? args[0] : AsCategoricalSide(args[0], TagOf(args[1]), line, col);
                JgsValue second = args[1].IsCategorical ? args[1] : AsCategoricalSide(args[1], TagOf(args[0]), line, col);
                JgsCategoricalTag tag = verb == "union" ? JgsCategorical.Merge(TagOf(first), TagOf(second), line, col) : TagOf(first);
                string[] a = JgsCategorical.NamesOf(first);
                string[] b = JgsCategorical.NamesOf(second);
                IEnumerable<string> picked = verb switch
                {
                    "intersect" => a.Where(n => n != JgsCategorical.Undefined && b.Contains(n, StringComparer.Ordinal)),
                    "union" => a.Concat(b).Where(static n => n != JgsCategorical.Undefined),
                    _ => a.Where(n => n != JgsCategorical.Undefined && !b.Contains(n, StringComparer.Ordinal)),
                };
                double[] codes = [.. picked.Select(n => (double)tag.CodeOf(n)).Distinct().OrderBy(static c => c)];
                bool column = first.Cols == 1 && first.Rows > 1;
                return [column ? JgsCategorical.FromCodes(codes, codes.Length, 1, tag) : JgsCategorical.FromCodes(codes, 1, codes.Length, tag)];
            });
        }

        // The text side of a set verb, read as names over the categorical side's categories: a
        // char row is one name, a cellstr or string array one each (R2025b's setdiff(c, 'a')).
        static JgsValue AsCategoricalSide(JgsValue text, JgsCategoricalTag like, int line, int col)
        {
            string?[] names = JgsCategorical.TextNames(text)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:categorical:setmembership:InvalidTypes",
                    "Inputs must be categorical arrays, or a categorical array and text.");
            (double[] codes, JgsCategoricalTag tag) = JgsCategorical.Absorb(new JgsCategoricalTag(like.Categories, like.Ordinal, false), names, line, col);
            return text.Type == JgsType.Cell || text.IsStringArray
                ? JgsCategorical.FromCodes(codes, text.Rows, text.Cols, tag)
                : JgsCategorical.FromCodes(codes, 1, codes.Length, tag);
        }

        Wrap("fillmissing", (args, wanted, line, col) =>
        {
            if (args.Count < 3 || !args[0].IsCategorical || !IsTextScalar(args[1]) || !TextOf(args[1]).Equals("constant", StringComparison.OrdinalIgnoreCase))
            {
                return args.Count > 0 && args[0].IsCategorical ? throw new JgsRuntimeException(line, col, "fillmissing over a categorical takes 'constant' and a category name.") : null;
            }

            string?[] fill = JgsCategorical.TextNames(args[2]) ?? throw new JgsRuntimeException(line, col, "fillmissing: the constant is a category name.");
            (double[] fillCodes, JgsCategoricalTag tag) = JgsCategorical.Absorb(TagOf(args[0]), fill, line, col);
            double[] codes = JgsCategorical.CodesOf(args[0]);
            for (int i = 0; i < codes.Length; i++)
            {
                if (double.IsNaN(codes[i]))
                {
                    codes[i] = fillCodes[0];
                }
            }

            return [JgsCategorical.FromCodesLike(codes, args[0], tag)];
        });
        Wrap("rmmissing", (args, wanted, line, col) =>
        {
            if (args.Count == 0 || !args[0].IsCategorical)
            {
                return null;
            }

            double[] codes = [.. JgsCategorical.CodesOf(args[0]).Where(static c => !double.IsNaN(c))];
            bool column = args[0].Cols == 1 && args[0].Rows > 1;
            return [column ? JgsCategorical.FromCodes(codes, codes.Length, 1, TagOf(args[0])) : JgsCategorical.FromCodes(codes, 1, codes.Length, TagOf(args[0]))];
        });
        Wrap("standardizeMissing", (args, wanted, line, col) =>
        {
            if (args.Count < 2 || !args[0].IsCategorical)
            {
                return null;
            }

            string?[] indicators = JgsCategorical.TextNames(args[1]) ?? [];
            JgsCategoricalTag tag = TagOf(args[0]);
            double[] codes = JgsCategorical.CodesOf(args[0]);
            for (int i = 0; i < codes.Length; i++)
            {
                if (!double.IsNaN(codes[i]) && indicators.Contains(JgsCategorical.NameOf(tag, codes[i]), StringComparer.Ordinal))
                {
                    codes[i] = double.NaN;
                }
            }

            return [JgsCategorical.FromCodesLike(codes, args[0], tag)];
        });
        Wrap("findgroups", (args, wanted, line, col) =>
        {
            if (args.Count != 1 || !args[0].IsCategorical)
            {
                return null;
            }

            // Groups are the categories present, in category order; the names come back as a categorical.
            JgsCategoricalTag tag = TagOf(args[0]);
            double[] codes = JgsCategorical.CodesOf(args[0]);
            double[] present = [.. codes.Where(static c => !double.IsNaN(c)).Distinct().OrderBy(static c => c)];
            var groups = new double[codes.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                groups[i] = double.IsNaN(codes[i]) ? double.NaN : Array.IndexOf(present, codes[i]) + 1;
            }

            bool column = args[0].Cols == 1 && args[0].Rows > 1;
            JgsValue g = column ? JgsMatrix.FromColumnMajor(groups, groups.Length, 1) : JgsMatrix.FromColumnMajor(groups, 1, groups.Length);
            JgsValue names = column ? JgsCategorical.FromCodes(present, present.Length, 1, tag) : JgsCategorical.FromCodes(present, 1, present.Length, tag);
            return [g, names];
        });
        Wrap("discretize", (args, wanted, line, col) =>
        {
            int at = -1;
            for (int i = 2; i < args.Count; i++)
            {
                if (IsTextScalar(args[i]) && TextOf(args[i]).Equals("categorical", StringComparison.OrdinalIgnoreCase))
                {
                    at = i;
                }
            }

            if (at < 0)
            {
                return null;
            }

            IJgsCallable inner = env.Builtins.TryGet("discretize", out JgsValue f) ? f.AsCallable : throw new JgsRuntimeException(line, col, "discretize is not available.");
            JgsValue bins = inner.Call([args[0], args[1]], line, col);
            double[] edges = ToDoubles("discretize", args[1], line, col);
            string?[] names = at + 1 < args.Count ? JgsCategorical.TextNames(args[at + 1]) ?? [] : [];
            var categories = new string[Math.Max(edges.Length - 1, 0)];
            for (int i = 0; i < categories.Length; i++)
            {
                categories[i] = i < names.Length && names[i] is not null
                    ? names[i]!
                    : $"[{JgsCategorical.NumberName(edges[i])}, {JgsCategorical.NumberName(edges[i + 1])}{(i == categories.Length - 1 ? "]" : ")")}";
            }

            var tag = new JgsCategoricalTag(categories, Ordinal: true, Protected: true);
            double[] codes = bins.Type == JgsType.Number ? [bins.AsNumber] : ToDoubles("discretize", bins, line, col);
            return [JgsCategorical.FromCodesLike(codes, bins.Type == JgsType.Number ? args[0] : bins, tag)];
        });
    }

    /// <summary>The bare <c>missing</c> value: the missing string's sentinel as a char row.</summary>
    internal static bool IsBareMissingValue(JgsValue value) => value.Type == JgsType.String && IsMissingText(value.AsString);

    // --- Construction ------------------------------------------------------------------------

    private static JgsCategoricalTag RequireCategorical(string verb, JgsValue value, int line, int col) =>
        value.CategoricalTag ?? throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
            $"Undefined function '{verb}' for input arguments of type '{ClassOf(value, JgsDialect.Matlab)}'.");

    /// <summary>The elements of a categorical as one-element categoricals.</summary>
    internal static JgsValue[] Elements(JgsValue categorical)
    {
        JgsCategoricalTag tag = categorical.CategoricalTag!;
        double[] codes = JgsCategorical.CodesOf(categorical);
        var elements = new JgsValue[codes.Length];
        for (int i = 0; i < codes.Length; i++)
        {
            elements[i] = JgsCategorical.FromCodes([codes[i]], 1, 1, tag);
        }

        return elements;
    }

    private static JgsValue MapCodes(JgsValue categorical, Func<double, bool> test) =>
        ShapedFlags(categorical, JgsCategorical.CodesOf(categorical).Select(test));

    private static JgsValue ShapedFlags(JgsValue shapeOf, IEnumerable<bool> flags)
    {
        JgsValue[] values = [.. flags.Select(JgsValue.Bool)];
        return values.Length == 1 ? values[0] : JgsMatrix.FromElements(values, shapeOf.Rows, shapeOf.Cols);
    }

    /// <summary>How many elements fall in each category, in category order.</summary>
    private static double[] Counts(JgsValue categorical)
    {
        JgsCategoricalTag tag = categorical.CategoricalTag!;
        var counts = new double[tag.Categories.Length];
        foreach (double code in JgsCategorical.CodesOf(categorical))
        {
            if (!double.IsNaN(code))
            {
                counts[(int)code - 1]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// <c>categorical(data)</c>, <c>categorical(data, valueset)</c>, <c>categorical(data, valueset,
    /// catnames)</c>, each with <c>'Ordinal'</c> and <c>'Protected'</c> after (open item 115).
    /// </summary>
    private static JgsValue Construct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            return JgsCategorical.FromCodes([], 0, 0, new JgsCategoricalTag([], false, false));
        }

        var positional = new List<JgsValue>();
        bool? ordinal = null;
        bool? isProtected = null;
        for (int i = 0; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]) && i + 1 < args.Count
                && TextOf(args[i]).Equals("Ordinal", StringComparison.OrdinalIgnoreCase))
            {
                ordinal = IsTruthy(args[++i]);
                continue;
            }

            if (IsTextScalar(args[i]) && i + 1 < args.Count
                && TextOf(args[i]).Equals("Protected", StringComparison.OrdinalIgnoreCase))
            {
                isProtected = IsTruthy(args[++i]);
                continue;
            }

            positional.Add(args[i]);
        }

        if (positional.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:parseArgs:WrongNumberArgs", "Incorrect number of arguments.");
        }

        if (ordinal == true && isProtected == false)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:UnprotectedOrdinal", "An ordinal array must be protected.");
        }

        JgsValue data = positional[0];
        CategoricalData read = ReadData(data, line, col);

        // The value set: the categories in the order given, read in the data's own kind.
        string[] valueNames;
        if (positional.Count >= 2)
        {
            JgsValue valueset = positional[1];
            bool valuesetNumeric = valueset.Type is JgsType.Number or JgsType.Bool || (valueset.Type == JgsType.Array && !valueset.IsStringArray && !valueset.IsCategorical);
            if (valueset.Type == JgsType.Cell && valueset.AsCell.Any(static e => e.Type != JgsType.String))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:UniqueMethodFailedValueset", "Could not find unique values in VALUESET using the UNIQUE function.");
            }

            if (read.Numeric && !valuesetNumeric)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:NumericTypeMismatchValueSet", "VALUESET must be numeric when DATA is numeric.");
            }

            if (!read.Numeric && valuesetNumeric && valueset.ArrayLength > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:TypeMismatchValueset", "VALUESET must be the same class as DATA.");
            }

            valueNames = valuesetNumeric
                ? [.. ToDoubles("categorical", valueset, line, col).Select(JgsCategorical.NumberName)]
                : [.. (JgsCategorical.TextNames(valueset) ?? []).Select(n => (n ?? string.Empty).Trim())];
            if (valueNames.Any(static n => n.Length == 0))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:EmptyCategoryName",
                    "Unable to create a category name from empty or missing text. Specify category names using the CATEGORYNAMES input argument.");
            }

            if (valueNames.Any(static n => n == JgsCategorical.Undefined))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:UndefinedLabelCategoryName",
                    $"Unable to create a category name equal to the text '{JgsCategorical.Undefined}'. Specify category names using the CATEGORYNAMES input argument.");
            }

            if (valueNames.Distinct(StringComparer.Ordinal).Count() != valueNames.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:DuplicatedValues", "VALUESET contains duplicated values.");
            }
        }
        else if (read.Tag is { } own)
        {
            valueNames = own.Categories;
        }
        else
        {
            IEnumerable<string> present = read.Names.Where(static n => n is not null)!;
            valueNames = JgsCategorical.SortedNames(present!, read.Numeric && !read.Logical);
            if (read.Logical)
            {
                valueNames = [.. valueNames.OrderBy(static n => n == "true")];
            }
        }

        // The category names: the value set's, or the names given, duplicates among them merging.
        string[] categoryNames = valueNames;
        if (positional.Count == 3)
        {
            string?[] given = JgsCategorical.TextNames(positional[2])
                ?? throw new JgsRuntimeException(line, col, "MATLAB:categorical:InvalidNames",
                    "CATEGORYNAMES must be a string array or cell array of character vectors containing non-blank category names.");
            if (given.Length != valueNames.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:WrongNumCategoryNamesValueset", "CATEGORYNAMES and VALUESET must have the same length.");
            }

            categoryNames = new string[given.Length];
            for (int i = 0; i < given.Length; i++)
            {
                string name = (given[i] ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:categorical:EmptyName", "CATEGORYNAMES contains an empty character vector or zero-length string.");
                }

                if (name == JgsCategorical.Undefined)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:categorical:UndefinedLabel", $"CATEGORYNAMES contains an invalid name '{JgsCategorical.Undefined}'.");
                }

                categoryNames[i] = name;
            }
        }
        else if (read.Names.Any(static n => n == JgsCategorical.Undefined))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:UndefinedLabelCategoryName",
                $"Unable to create a category name equal to the text '{JgsCategorical.Undefined}'. Specify category names using the CATEGORYNAMES input argument.");
        }

        string[] categories = [.. categoryNames.Distinct(StringComparer.Ordinal)];
        var codes = new double[read.Names.Length];
        for (int i = 0; i < codes.Length; i++)
        {
            string? name = read.Names[i];
            int at = name is null ? -1 : Array.IndexOf(valueNames, name);
            codes[i] = at < 0 ? double.NaN : Array.IndexOf(categories, categoryNames[at]) + 1;
        }

        bool isOrdinal = ordinal ?? false;
        var tag = new JgsCategoricalTag(categories, isOrdinal, isProtected ?? isOrdinal);
        return JgsCategorical.FromCodesLike(codes, read.ShapeOf, tag);
    }

    private sealed record CategoricalData(string?[] Names, bool Numeric, bool Logical, JgsValue ShapeOf, JgsCategoricalTag? Tag);

    /// <summary>The data a categorical is made from: names (null for a missing one) in storage order.</summary>
    private static CategoricalData ReadData(JgsValue data, int line, int col)
    {
        if (data.IsCategorical)
        {
            string[] names = JgsCategorical.NamesOf(data);
            return new([.. names.Select(static n => n == JgsCategorical.Undefined ? null : n)], false, false, data, data.CategoricalTag);
        }

        if (data.Type == JgsType.String || data.IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:CharData",
                "Conversion from character array not supported.  First, convert the character array to a cell array of character vectors using cellstr.  Then convert the cell array using categorical.");
        }

        if (data.Type == JgsType.Cell)
        {
            if (data.AsCell.Any(static e => e.Type != JgsType.String))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:UniqueMethodFailedData", "Could not find unique values in DATA using the UNIQUE function.");
            }

            return new([.. data.AsCell.Select(static e => Trimmed(e.AsString))], false, false, data, null);
        }

        if (data.IsStringArray)
        {
            var names = new string?[data.ArrayLength];
            for (int i = 0; i < names.Length; i++)
            {
                JgsValue element = data.ElementAt(i);
                names[i] = element.Type == JgsType.String && !IsMissingText(element.AsString) ? Trimmed(element.AsString) : null;
            }

            return new(names, false, false, data, null);
        }

        if (data.IsTime)
        {
            string[] shown = [.. Enumerable.Range(0, data.ArrayLength).Select(i => data.ElementAt(i).Display())];
            return new(shown, false, false, data, null);
        }

        if (data.Type is JgsType.Number or JgsType.Bool or JgsType.Array)
        {
            bool logical = IsLogicalValue(data);
            double[] values = data.Type == JgsType.Number ? [data.AsNumber] : data.Type == JgsType.Bool ? [data.AsBool ? 1 : 0] : ToDoubles("categorical", data, line, col);
            var names = new string?[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                names[i] = double.IsNaN(values[i]) ? null : logical ? (values[i] != 0 ? "true" : "false") : JgsCategorical.NumberName(values[i]);
            }

            return new(names, !logical, logical, data.Type == JgsType.Array ? data : JgsValue.Array([data]), null);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:categorical:UniqueMethodFailedData", "Could not find unique values in DATA using the UNIQUE function.");
    }

    private static string? Trimmed(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length == 0 || IsMissingText(text) ? null : trimmed;
    }

    private static bool IsTruthy(JgsValue value) => value.Type switch
    {
        JgsType.Bool => value.AsBool,
        JgsType.Number => value.AsNumber != 0,
        JgsType.Array => value.ArrayLength > 0 && value.ElementAt(0).AsNumber != 0,
        _ => false,
    };

    // --- Category verbs ------------------------------------------------------------------------

    private static string[] NamesArgument(string verb, JgsValue value, int line, int col) =>
        [.. (JgsCategorical.TextNames(value) ?? throw new JgsRuntimeException(line, col, $"{verb} takes category names as text.")).Select(n => (n ?? string.Empty).Trim())];

    private static JgsValue CountCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("countcats", args, 1, 2, line, col);
        JgsCategoricalTag tag = RequireCategorical("countcats", args[0], line, col);
        JgsValue c = args[0];
        int rows = c.Rows;
        int cols = c.Cols;
        int dim = args.Count == 2 ? (int)NumOf("countcats", args[1], line, col) : (rows == 1 ? 2 : 1);
        double[] codes = JgsCategorical.CodesOf(c);
        int n = tag.Categories.Length;
        if (dim == 1)
        {
            // A count per category down each column.
            var counts = new double[n * cols];
            for (int col2 = 0; col2 < cols; col2++)
            {
                for (int r = 0; r < rows; r++)
                {
                    double code = codes[r + (col2 * rows)];
                    if (!double.IsNaN(code))
                    {
                        counts[((int)code - 1) + (col2 * n)]++;
                    }
                }
            }

            return JgsMatrix.FromColumnMajor(counts, n, cols);
        }

        var across = new double[rows * n];
        for (int r = 0; r < rows; r++)
        {
            for (int col2 = 0; col2 < cols; col2++)
            {
                double code = codes[r + (col2 * rows)];
                if (!double.IsNaN(code))
                {
                    across[r + (((int)code - 1) * rows)]++;
                }
            }
        }

        return JgsMatrix.FromColumnMajor(across, rows, n);
    }

    private static JgsValue AddCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("addcats", args, 2, 4, line, col);
        JgsCategoricalTag tag = RequireCategorical("addcats", args[0], line, col);
        string[] added = NamesArgument("addcats", args[1], line, col);
        var categories = new List<string>(tag.Categories);
        int at = categories.Count;
        if (args.Count == 4)
        {
            string where = TextOf(args[2]);
            string anchor = NamesArgument("addcats", args[3], line, col)[0];
            int index = categories.IndexOf(anchor);
            if (index < 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:addcats:UnknownCategory", $"'{anchor}' is not a category.");
            }

            at = where.Equals("After", StringComparison.OrdinalIgnoreCase) ? index + 1 : index;
        }
        else if (tag.Ordinal)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:addcats:NoBeforeOrAfter", "You must specify either 'Before' or 'After' for an ordinal array.");
        }

        foreach (string name in added)
        {
            if (!categories.Contains(name, StringComparer.Ordinal))
            {
                categories.Insert(at++, name);
            }
        }

        var moved = tag with { Categories = [.. categories] };
        return JgsCategorical.FromCodesLike(JgsCategorical.Remap(JgsCategorical.CodesOf(args[0]), tag, moved), args[0], moved);
    }

    private static JgsValue RemoveCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("removecats", args, 1, 2, line, col);
        JgsCategoricalTag tag = RequireCategorical("removecats", args[0], line, col);
        double[] codes = JgsCategorical.CodesOf(args[0]);
        string[] dropped = args.Count == 2
            ? NamesArgument("removecats", args[1], line, col)
            : [.. tag.Categories.Where((n, i) => !codes.Contains(i + 1))];
        var kept = tag with { Categories = [.. tag.Categories.Where(n => !dropped.Contains(n, StringComparer.Ordinal))] };
        return JgsCategorical.FromCodesLike(JgsCategorical.Remap(codes, tag, kept), args[0], kept);
    }

    private static JgsValue MergeCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mergecats", args, 2, 3, line, col);
        JgsCategoricalTag tag = RequireCategorical("mergecats", args[0], line, col);
        string[] merged = NamesArgument("mergecats", args[1], line, col);
        string target = args.Count == 3 ? NamesArgument("mergecats", args[2], line, col)[0] : merged[0];
        int[] positions = [.. merged.Select(n => Array.IndexOf(tag.Categories, n)).Where(static i => i >= 0).OrderBy(static i => i)];
        if (tag.Ordinal && positions.Length > 0 && positions[^1] - positions[0] + 1 != positions.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:mergecats:NonconsecutiveCategories", "Cannot merge nonconsecutive categories when the categories are ordinal.");
        }

        var categories = new List<string>();
        bool placed = false;
        for (int i = 0; i < tag.Categories.Length; i++)
        {
            if (merged.Contains(tag.Categories[i], StringComparer.Ordinal))
            {
                if (!placed)
                {
                    categories.Add(target);
                    placed = true;
                }

                continue;
            }

            categories.Add(tag.Categories[i]);
        }

        var moved = tag with { Categories = [.. categories] };
        double[] codes = JgsCategorical.CodesOf(args[0]);
        var remapped = new double[codes.Length];
        int targetCode = moved.CodeOf(target);
        for (int i = 0; i < codes.Length; i++)
        {
            string name = JgsCategorical.NameOf(tag, codes[i]);
            remapped[i] = double.IsNaN(codes[i]) ? double.NaN : merged.Contains(name, StringComparer.Ordinal) ? targetCode : moved.CodeOf(name);
        }

        return JgsCategorical.FromCodesLike(remapped, args[0], moved);
    }

    private static JgsValue RenameCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("renamecats", args, 2, 3, line, col);
        JgsCategoricalTag tag = RequireCategorical("renamecats", args[0], line, col);
        string[] oldNames = args.Count == 3 ? NamesArgument("renamecats", args[1], line, col) : tag.Categories;
        string[] newNames = NamesArgument("renamecats", args[^1], line, col);
        if (oldNames.Any(n => !tag.Categories.Contains(n, StringComparer.Ordinal)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:renamecats:InvalidNames", "OLDNAMES must be a subset of the existing categories.");
        }

        if (oldNames.Length != newNames.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:renamecats:InvalidNames", "OLDNAMES and NEWNAMES must have the same length.");
        }

        string[] categories = [.. tag.Categories];
        for (int i = 0; i < oldNames.Length; i++)
        {
            int at = Array.IndexOf(categories, oldNames[i]);
            if (newNames[i] != oldNames[i] && categories.Contains(newNames[i], StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:renamecats:DuplicateNames",
                    $"Unable to rename the category '{oldNames[i]}' to '{newNames[i]}' because that category name exists already.");
            }

            categories[at] = newNames[i];
        }

        return JgsCategorical.FromCodesLike(JgsCategorical.CodesOf(args[0]), args[0], tag with { Categories = categories });
    }

    private static JgsValue ReorderCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("reordercats", args, 1, 2, line, col);
        JgsCategoricalTag tag = RequireCategorical("reordercats", args[0], line, col);
        string[] order = args.Count == 2 ? NamesArgument("reordercats", args[1], line, col) : JgsCategorical.SortedNames(tag.Categories, false);
        if (order.Length != tag.Categories.Length || order.Any(n => !tag.Categories.Contains(n, StringComparer.Ordinal)) || order.Distinct(StringComparer.Ordinal).Count() != order.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:reordercats:InvalidNeworder", "The new order must be a permutation of the existing categories.");
        }

        var moved = tag with { Categories = order };
        return JgsCategorical.FromCodesLike(JgsCategorical.Remap(JgsCategorical.CodesOf(args[0]), tag, moved), args[0], moved);
    }

    private static JgsValue SetCats(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("setcats", args, 2, line, col);
        JgsCategoricalTag tag = RequireCategorical("setcats", args[0], line, col);
        string[] names = [.. NamesArgument("setcats", args[1], line, col).Distinct(StringComparer.Ordinal)];
        var moved = tag with { Categories = names };
        return JgsCategorical.FromCodesLike(JgsCategorical.Remap(JgsCategorical.CodesOf(args[0]), tag, moved), args[0], moved);
    }

    // --- Summary -------------------------------------------------------------------------------

    private static JgsValue SummaryStruct(JgsValue categorical)
    {
        JgsCategoricalTag tag = categorical.CategoricalTag!;
        double[] counts = Counts(categorical);
        int missing = JgsCategorical.CodesOf(categorical).Count(double.IsNaN);
        bool column = categorical.Cols == 1 && categorical.Rows > 1;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Size"] = JgsMatrix.FromColumnMajor([categorical.Rows, categorical.Cols], 1, 2),
            ["Type"] = JgsValue.Str("categorical"),
            ["Categories"] = JgsCategorical.CategoriesColumn(tag),
            ["Counts"] = column ? JgsMatrix.FromColumnMajor(counts, counts.Length, 1) : JgsMatrix.FromColumnMajor(counts, 1, counts.Length),
            ["NumMissing"] = JgsValue.Number(missing),
        };
        if (tag.Ordinal)
        {
            double[] present = [.. JgsCategorical.CodesOf(categorical).Where(static c => !double.IsNaN(c)).OrderBy(static c => c)];
            double mid = present.Length == 0 ? double.NaN : present.Length % 2 == 1 ? present[present.Length / 2]
                : Math.Round((present[(present.Length / 2) - 1] + present[present.Length / 2]) / 2, MidpointRounding.AwayFromZero);
            fields["Min"] = JgsCategorical.FromCodes([present.Length == 0 ? double.NaN : present[0]], 1, 1, tag);
            fields["Median"] = JgsCategorical.FromCodes([mid], 1, 1, tag);
            fields["Max"] = JgsCategorical.FromCodes([present.Length == 0 ? double.NaN : present[^1]], 1, 1, tag);
        }

        return JgsValue.Struct(fields);
    }

    /// <summary>R2025b's printed summary: the size and kind, then each category's count, with the undefined count last.</summary>
    private static string SummaryText(JgsValue categorical)
    {
        JgsCategoricalTag tag = categorical.CategoricalTag!;
        double[] counts = Counts(categorical);
        double[] codes = JgsCategorical.CodesOf(categorical);
        int rows = categorical.Rows;
        int cols = categorical.Cols;
        string[] labels = [.. tag.Categories, JgsCategorical.Undefined];
        var sb = new StringBuilder();
        sb.Append('\n').Append(rows).Append('×').Append(cols).Append(tag.Ordinal ? " ordinal categorical" : " categorical").Append("\n\n");
        if (rows == 1 || cols == 1 && rows == 1)
        {
            double[] line = [.. counts, codes.Count(double.IsNaN)];
            var widths = new int[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                widths[i] = Math.Max(labels[i].Length, line[i].ToString(CultureInfo.InvariantCulture).Length);
            }

            sb.Append("     ").Append(string.Join("      ", labels.Select((l, i) => l.PadRight(widths[i])))).Append(" \n");
            sb.Append("     ").Append(string.Join("      ", line.Select((c, i) => c.ToString(CultureInfo.InvariantCulture).PadRight(widths[i])))).Append(" \n");
        }
        else
        {
            int width = labels.Max(static l => l.Length);
            var perColumn = new double[labels.Length, cols];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    double code = codes[r + (c * rows)];
                    perColumn[double.IsNaN(code) ? labels.Length - 1 : (int)code - 1, c]++;
                }
            }

            for (int i = 0; i < labels.Length; i++)
            {
                sb.Append("     ").Append(labels[i].PadRight(width));
                for (int c = 0; c < cols; c++)
                {
                    sb.Append("      ").Append(perColumn[i, c].ToString(CultureInfo.InvariantCulture));
                }

                sb.Append(" \n");
            }
        }

        if (tag.Ordinal)
        {
            JgsValue stats = SummaryStruct(categorical);
            string Name(string key) => JgsCategorical.NamesOf(stats.AsStruct[key])[0];
            int width = Math.Max(Name("Min").Length, Math.Max(Name("Median").Length, Name("Max").Length));
            sb.Append("\nAdditional statistics:\n\n");
            sb.Append("    Min         ").Append(Name("Min").PadRight(width)).Append(" \n");
            sb.Append("    Median      ").Append(Name("Median").PadRight(width)).Append(" \n");
            sb.Append("    Max         ").Append(Name("Max").PadRight(width)).Append(" \n");
        }

        return sb.ToString();
    }

    private static (double[] Codes, double[] Order) SortCodes(JgsValue categorical, bool descend, int dim)
    {
        double[] codes = JgsCategorical.CodesOf(categorical);
        int rows = categorical.Rows;
        int cols = categorical.Cols;
        var sorted = new double[codes.Length];
        var order = new double[codes.Length];
        double Key(double code) => double.IsNaN(code) ? double.PositiveInfinity : code; // undefined sorts last ascending, first descending (probe_115)
        void SortRun(int start, int step, int count)
        {
            int[] indices = [.. Enumerable.Range(0, count)];
            Array.Sort(indices, (x, y) =>
            {
                int byKey = descend ? Key(codes[start + (y * step)]).CompareTo(Key(codes[start + (x * step)])) : Key(codes[start + (x * step)]).CompareTo(Key(codes[start + (y * step)]));
                return byKey != 0 ? byKey : x.CompareTo(y);
            });
            for (int k = 0; k < count; k++)
            {
                sorted[start + (k * step)] = codes[start + (indices[k] * step)];
                order[start + (k * step)] = indices[k] + 1;
            }
        }

        if (dim == 2)
        {
            for (int r = 0; r < rows; r++)
            {
                SortRun(r, rows, cols);
            }
        }
        else
        {
            for (int c = 0; c < cols; c++)
            {
                SortRun(c * rows, 1, rows);
            }
        }

        return (sorted, order);
    }
}
