using System.Globalization;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Builds the global environment a JGS script runs in: the built-in functions. They mirror the JGraph
/// functional API — data helpers (<c>linspace</c>, <c>range</c>, element-wise math, reductions), table
/// readers, and the plotting verbs (<c>plot</c>, <c>title</c>, <c>legend</c>, <c>show</c>, …) — bridging to
/// the static <see cref="JG"/> facade and the host's <see cref="JGraphScriptGlobals"/>. This is the only IO
/// surface a JGS script has: there is no file, network, or reflection access beyond the table readers.
/// </summary>
internal static class JgsBuiltins
{
    /// <summary>Creates the global scope over the run's <paramref name="host"/> helpers, seeded with every built-in.</summary>
    public static JgsEnvironment CreateGlobals(JGraphScriptGlobals host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var env = new JgsEnvironment();
        var random = new Random();

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void Math1(string name, Func<double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapNumeric(name, args[0], f, line, col); });

        // --- Element-wise math ---------------------------------------------------------------
        Math1("sin", System.Math.Sin);
        Math1("cos", System.Math.Cos);
        Math1("tan", System.Math.Tan);
        Math1("asin", System.Math.Asin);
        Math1("acos", System.Math.Acos);
        Math1("atan", System.Math.Atan);
        Math1("exp", System.Math.Exp);
        Math1("log", System.Math.Log);
        Math1("log10", System.Math.Log10);
        Math1("sqrt", System.Math.Sqrt);
        Math1("abs", System.Math.Abs);
        Math1("floor", System.Math.Floor);
        Math1("ceil", System.Math.Ceiling);
        Math1("round", x => System.Math.Round(x, MidpointRounding.AwayFromZero));
        Math1("sign", x => System.Math.Sign(x));

        Define("pow", (args, line, col) =>
        {
            Arity("pow", args, 2, line, col);
            double exponent = Num("pow", args, 1, line, col);
            return MapNumeric("pow", args[0], x => System.Math.Pow(x, exponent), line, col);
        });

        Define("atan2", (args, line, col) =>
        {
            Arity("atan2", args, 2, line, col);
            return JgsValue.Number(System.Math.Atan2(Num("atan2", args, 0, line, col), Num("atan2", args, 1, line, col)));
        });

        // --- Array construction --------------------------------------------------------------
        Define("linspace", (args, line, col) =>
        {
            Arity("linspace", args, 3, line, col);
            double start = Num("linspace", args, 0, line, col);
            double stop = Num("linspace", args, 1, line, col);
            int count = Count("linspace", args, 2, line, col);
            if (count < 1)
            {
                throw new JgsRuntimeException(line, col, "linspace needs a count of at least 1.");
            }

            var result = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                double t = count == 1 ? 0 : (double)i / (count - 1);
                result[i] = JgsValue.Number(start + ((stop - start) * t));
            }

            return JgsValue.Array(result);
        });

        Define("range", (args, line, col) =>
        {
            ArityRange("range", args, 2, 3, line, col);
            double start = Num("range", args, 0, line, col);
            double stop = Num("range", args, 1, line, col);
            double step = args.Count == 3 ? Num("range", args, 2, line, col) : 1.0;
            if (step == 0)
            {
                throw new JgsRuntimeException(line, col, "range step must not be zero.");
            }

            var result = new List<JgsValue>();
            if (step > 0)
            {
                for (double v = start; v < stop; v += step)
                {
                    result.Add(JgsValue.Number(v));
                }
            }
            else
            {
                for (double v = start; v > stop; v += step)
                {
                    result.Add(JgsValue.Number(v));
                }
            }

            return JgsValue.Array(result.ToArray());
        });

        Define("zeros", (args, line, col) => Filled("zeros", args, 0.0, line, col));
        Define("ones", (args, line, col) => Filled("ones", args, 1.0, line, col));

        Define("rand", (args, line, col) =>
        {
            Arity("rand", args, 1, line, col);
            int count = Count("rand", args, 0, line, col);
            var result = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = JgsValue.Number(random.NextDouble());
            }

            return JgsValue.Array(result);
        });

        // --- Reductions and inspection -------------------------------------------------------
        Define("length", (args, line, col) =>
        {
            Arity("length", args, 1, line, col);
            return args[0].Type switch
            {
                JgsType.Array => JgsValue.Number(args[0].AsArray.Length),
                JgsType.String => JgsValue.Number(args[0].AsString.Length),
                _ => throw new JgsRuntimeException(line, col, $"length expects an array or string, but got a {args[0].TypeName}."),
            };
        });

        Define("sum", (args, line, col) => Reduce("sum", args, line, col, (acc, v) => acc + v, 0.0));
        Define("mean", (args, line, col) =>
        {
            double[] values = ArrayOfNumbers("mean", args, line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "mean needs a non-empty array.");
            }

            double total = 0;
            foreach (double v in values)
            {
                total += v;
            }

            return JgsValue.Number(total / values.Length);
        });

        Define("min", (args, line, col) => MinMax("min", args, line, col, takeMin: true));
        Define("max", (args, line, col) => MinMax("max", args, line, col, takeMin: false));

        Define("numel", (args, line, col) =>
        {
            Arity("numel", args, 1, line, col);
            return args[0].Type switch
            {
                JgsType.Array => JgsValue.Number(args[0].AsArray.Length),
                JgsType.String => JgsValue.Number(args[0].AsString.Length),
                _ => throw new JgsRuntimeException(line, col, $"numel expects an array or string, but got a {args[0].TypeName}."),
            };
        });

        // --- Statistics ----------------------------------------------------------------------
        Define("std", (args, line, col) => JgsValue.Number(System.Math.Sqrt(SampleVariance("std", args, line, col))));
        Define("variance", (args, line, col) => JgsValue.Number(SampleVariance("variance", args, line, col)));
        Define("median", (args, line, col) => JgsValue.Number(JgsStdlib.Median(NonEmpty("median", args, line, col))));
        Define("mode", (args, line, col) => JgsValue.Number(JgsStdlib.Mode(NonEmpty("mode", args, line, col))));

        Define("percentile", (args, line, col) =>
        {
            Arity("percentile", args, 2, line, col);
            double p = Num("percentile", args, 1, line, col);
            if (p < 0 || p > 100)
            {
                throw new JgsRuntimeException(line, col, "percentile expects p between 0 and 100.");
            }

            double[] values = DoubleArray("percentile", args, 0, line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "percentile needs a non-empty array.");
            }

            return JgsValue.Number(JgsStdlib.Percentile(values, p));
        });

        Define("cumsum", (args, line, col) => Numbers(JgsStdlib.CumulativeSum(ArrayOfNumbers("cumsum", args, line, col))));
        Define("cumprod", (args, line, col) => Numbers(JgsStdlib.CumulativeProduct(ArrayOfNumbers("cumprod", args, line, col))));
        Define("diff", (args, line, col) => Numbers(JgsStdlib.Differences(ArrayOfNumbers("diff", args, line, col))));

        // --- Array operations ----------------------------------------------------------------
        Define("sort", (args, line, col) =>
        {
            ArityRange("sort", args, 1, 2, line, col);
            bool descending = args.Count == 2 && OrderIsDescending("sort", args, line, col);
            return JgsValue.Array(JgsStdlib.Sort(Arr("sort", args, 0, line, col), descending)
                ?? throw new JgsRuntimeException(line, col, "sort needs an array of all numbers or all strings."));
        });

        Define("unique", (args, line, col) =>
        {
            Arity("unique", args, 1, line, col);
            return JgsValue.Array(JgsStdlib.Unique(Arr("unique", args, 0, line, col))
                ?? throw new JgsRuntimeException(line, col, "unique needs an array of all numbers or all strings."));
        });

        Define("find", (args, line, col) =>
        {
            Arity("find", args, 1, line, col);
            JgsValue[] elements = Arr("find", args, 0, line, col);
            var indices = new List<JgsValue>();
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].IsTruthy)
                {
                    indices.Add(JgsValue.Number(i));
                }
            }

            return JgsValue.Array(indices.ToArray());
        });

        Define("any", (args, line, col) =>
        {
            Arity("any", args, 1, line, col);
            return JgsValue.Bool(System.Array.Exists(Arr("any", args, 0, line, col), static v => v.IsTruthy));
        });

        Define("all", (args, line, col) =>
        {
            Arity("all", args, 1, line, col);
            return JgsValue.Bool(System.Array.TrueForAll(Arr("all", args, 0, line, col), static v => v.IsTruthy));
        });

        Define("concat", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, $"concat expects at least 2 arguments, but got {args.Count}.");
            }

            var joined = new List<JgsValue>();
            foreach (JgsValue arg in args)
            {
                if (arg.Type == JgsType.Array)
                {
                    joined.AddRange(arg.AsArray);
                }
                else
                {
                    joined.Add(arg); // A scalar appends as one element: concat(a, 5).
                }
            }

            return JgsValue.Array(joined.ToArray());
        });

        Define("slice", (args, line, col) =>
        {
            ArityRange("slice", args, 2, 3, line, col);
            JgsValue[] source = Arr("slice", args, 0, line, col);
            int start = Count("slice", args, 1, line, col);
            int stop = args.Count == 3 ? Count("slice", args, 2, line, col) : source.Length;
            if (start < 0 || stop < start || stop > source.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"slice range [{start}, {stop}) is invalid for an array of length {source.Length}.");
            }

            var section = new JgsValue[stop - start];
            System.Array.Copy(source, start, section, 0, section.Length);
            return JgsValue.Array(section);
        });

        Define("indexof", (args, line, col) =>
        {
            Arity("indexof", args, 2, line, col);
            JgsValue[] elements = Arr("indexof", args, 0, line, col);
            for (int i = 0; i < elements.Length; i++)
            {
                if (JgsValue.AreEqual(elements[i], args[1]))
                {
                    return JgsValue.Number(i);
                }
            }

            return JgsValue.Number(-1);
        });

        Define("reverse", (args, line, col) =>
        {
            Arity("reverse", args, 1, line, col);
            var reversed = (JgsValue[])Arr("reverse", args, 0, line, col).Clone();
            System.Array.Reverse(reversed);
            return JgsValue.Array(reversed);
        });

        Define("isnan", (args, line, col) =>
        {
            Arity("isnan", args, 1, line, col);
            return MapToBool("isnan", args[0], static x => double.IsNaN(x), line, col);
        });

        Define("isequal", (args, line, col) =>
        {
            Arity("isequal", args, 2, line, col);
            return JgsValue.Bool(JgsStdlib.DeepEquals(args[0], args[1]));
        });

        Define("and", (args, line, col) => Logical2("and", args, line, col, static (a, b) => a && b));
        Define("or", (args, line, col) => Logical2("or", args, line, col, static (a, b) => a || b));
        Define("not", (args, line, col) =>
        {
            Arity("not", args, 1, line, col);
            if (args[0].Type != JgsType.Array)
            {
                return JgsValue.Bool(!args[0].IsTruthy);
            }

            JgsValue[] source = args[0].AsArray;
            var flipped = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                flipped[i] = JgsValue.Bool(!source[i].IsTruthy);
            }

            return JgsValue.Array(flipped);
        });

        // --- Strings -------------------------------------------------------------------------
        Define("sprintf", (args, line, col) =>
        {
            if (args.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "sprintf expects a format string first.");
            }

            string format = Str("sprintf", args, 0, line, col);
            try
            {
                return JgsValue.Str(JgsSprintf.Format(format, args.Skip(1).ToArray()));
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        Define("str", (args, line, col) => { Arity("str", args, 1, line, col); return JgsValue.Str(args[0].Display()); });

        Define("num", (args, line, col) =>
        {
            Arity("num", args, 1, line, col);
            // MATLAB str2double: unparseable text is NaN, so bad cells filter with isnan.
            return JgsValue.Number(double.TryParse(Str("num", args, 0, line, col).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN);
        });

        Define("upper", (args, line, col) => { Arity("upper", args, 1, line, col); return JgsValue.Str(Str("upper", args, 0, line, col).ToUpperInvariant()); });
        Define("lower", (args, line, col) => { Arity("lower", args, 1, line, col); return JgsValue.Str(Str("lower", args, 0, line, col).ToLowerInvariant()); });
        Define("trim", (args, line, col) => { Arity("trim", args, 1, line, col); return JgsValue.Str(Str("trim", args, 0, line, col).Trim()); });

        Define("split", (args, line, col) =>
        {
            Arity("split", args, 2, line, col);
            string separator = Str("split", args, 1, line, col);
            if (separator.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "split separator must not be empty.");
            }

            string[] parts = Str("split", args, 0, line, col).Split(separator, StringSplitOptions.None);
            var result = new JgsValue[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = JgsValue.Str(parts[i]);
            }

            return JgsValue.Array(result);
        });

        Define("join", (args, line, col) =>
        {
            Arity("join", args, 2, line, col);
            JgsValue[] parts = Arr("join", args, 0, line, col);
            string separator = Str("join", args, 1, line, col);
            return JgsValue.Str(string.Join(separator, parts.Select(static p => p.Display())));
        });

        Define("startswith", (args, line, col) =>
        {
            Arity("startswith", args, 2, line, col);
            return JgsValue.Bool(Str("startswith", args, 0, line, col).StartsWith(Str("startswith", args, 1, line, col), StringComparison.Ordinal));
        });

        Define("endswith", (args, line, col) =>
        {
            Arity("endswith", args, 2, line, col);
            return JgsValue.Bool(Str("endswith", args, 0, line, col).EndsWith(Str("endswith", args, 1, line, col), StringComparison.Ordinal));
        });

        Define("replace", (args, line, col) =>
        {
            Arity("replace", args, 3, line, col);
            string oldText = Str("replace", args, 1, line, col);
            if (oldText.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "replace cannot search for an empty string.");
            }

            return JgsValue.Str(Str("replace", args, 0, line, col).Replace(oldText, Str("replace", args, 2, line, col), StringComparison.Ordinal));
        });

        Define("contains", (args, line, col) =>
        {
            Arity("contains", args, 2, line, col);
            // Polymorphic: substring test on strings, membership test on arrays.
            if (args[0].Type == JgsType.String)
            {
                return JgsValue.Bool(args[0].AsString.Contains(Str("contains", args, 1, line, col), StringComparison.Ordinal));
            }

            JgsValue[] haystack = Arr("contains", args, 0, line, col);
            return JgsValue.Bool(System.Array.Exists(haystack, v => JgsValue.AreEqual(v, args[1])));
        });

        // --- Table access --------------------------------------------------------------------
        Define("readcsv", (args, line, col) => ReadTable("readcsv", args, line, col, host.readcsv, host.readcsv));
        Define("readxlsx", (args, line, col) => ReadTable("readxlsx", args, line, col, host.readxlsx, host.readxlsx));
        Define("readtable", (args, line, col) => ReadTable("readtable", args, line, col, host.readtable, host.readtable));

        Define("colnames", (args, line, col) =>
        {
            Arity("colnames", args, 1, line, col);
            IReadOnlyList<string> names = Tbl("colnames", args, 0, line, col).ColumnNames;
            var result = new JgsValue[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                result[i] = JgsValue.Str(names[i]);
            }

            return JgsValue.Array(result);
        });

        Define("rowcount", (args, line, col) =>
        {
            Arity("rowcount", args, 1, line, col);
            return JgsValue.Number(Tbl("rowcount", args, 0, line, col).RowCount);
        });

        Define("textcolumn", (args, line, col) =>
        {
            Arity("textcolumn", args, 2, line, col);
            Table table = Tbl("textcolumn", args, 0, line, col);
            string name = Str("textcolumn", args, 1, line, col);
            if (!table.TryGetColumn(name, out TableColumn? textColumn))
            {
                throw new JgsRuntimeException(line, col,
                    $"The table has no column named '{name}'. Available: {string.Join(", ", table.ColumnNames)}.");
            }

            var result = new JgsValue[table.RowCount];
            for (int row = 0; row < result.Length; row++)
            {
                result[row] = JgsValue.Str(textColumn.IsMissing(row) ? "" : textColumn.GetText(row));
            }

            return JgsValue.Array(result);
        });

        Define("column", (args, line, col) =>
        {
            Arity("column", args, 2, line, col);
            Table table = Tbl("column", args, 0, line, col);
            string name = Str("column", args, 1, line, col);
            double[] values;
            try
            {
                values = TableSeries.GetNumbers(table, name);
            }
            catch (KeyNotFoundException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            var result = new JgsValue[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = JgsValue.Number(values[i]);
            }

            return JgsValue.Array(result);
        });

        // --- Output --------------------------------------------------------------------------
        Define("print", (args, line, col) =>
        {
            host.print(string.Join(" ", args.Select(a => a.Display())));
            return JgsValue.Null;
        });

        // --- Figure setup and plotting -------------------------------------------------------
        Define("figure", (args, line, col) =>
        {
            ArityRange("figure", args, 0, 1, line, col);
            if (args.Count == 1)
            {
                int number = Count("figure", args, 0, line, col);
                if (number < 1)
                {
                    throw new JgsRuntimeException(line, col, "Figure numbers start at 1.");
                }

                JG.Figure(number);
                return JgsValue.Number(number);
            }

            JG.Figure();
            return JgsValue.Number(JG.CurrentFigureNumber);
        });
        Define("subplot", (args, line, col) =>
        {
            Arity("subplot", args, 3, line, col);
            JG.Subplot(Count("subplot", args, 0, line, col), Count("subplot", args, 1, line, col), Count("subplot", args, 2, line, col));
            return JgsValue.Null;
        });

        Define("plot", (args, line, col) => Plot(args, line, col));
        Define("scatter", (args, line, col) => XyOrTable("scatter", args, line, col,
            (x, y) => JG.Scatter(x, y), (t, xc, yc) => JG.Scatter(t, xc, yc)));
        Define("bar", (args, line, col) => XyOrTable("bar", args, line, col,
            (x, y) => JG.Bar(x, y), (t, xc, yc) => JG.Bar(t, xc, yc)));
        Define("stem", (args, line, col) => Stem(args, line, col));
        Define("histogram", (args, line, col) => Histogram(args, line, col));
        Define("errorbar", (args, line, col) => ErrorBar(args, line, col));

        Define("semilogy", (args, line, col) => Semilog("semilogy", args, line, col, (x, y, s) => JG.SemilogY(x, y, s)));
        Define("semilogx", (args, line, col) => Semilog("semilogx", args, line, col, (x, y, s) => JG.SemilogX(x, y, s)));
        Define("loglog", (args, line, col) => Semilog("loglog", args, line, col, (x, y, s) => JG.LogLog(x, y, s)));

        Define("title", (args, line, col) => { Arity("title", args, 1, line, col); JG.Title(Str("title", args, 0, line, col)); return JgsValue.Null; });
        Define("xlabel", (args, line, col) => { Arity("xlabel", args, 1, line, col); JG.XLabel(Str("xlabel", args, 0, line, col)); return JgsValue.Null; });
        Define("ylabel", (args, line, col) => { Arity("ylabel", args, 1, line, col); JG.YLabel(Str("ylabel", args, 0, line, col)); return JgsValue.Null; });
        Define("xlim", (args, line, col) => { Arity("xlim", args, 2, line, col); JG.XLim(Num("xlim", args, 0, line, col), Num("xlim", args, 1, line, col)); return JgsValue.Null; });
        Define("ylim", (args, line, col) => { Arity("ylim", args, 2, line, col); JG.YLim(Num("ylim", args, 0, line, col), Num("ylim", args, 1, line, col)); return JgsValue.Null; });

        Define("grid", (args, line, col) => { ArityRange("grid", args, 0, 1, line, col); JG.Grid(args.Count == 0 || Truthy(args, 0)); return JgsValue.Null; });
        Define("hold", (args, line, col) => { ArityRange("hold", args, 0, 1, line, col); JG.Hold(args.Count == 0 || Truthy(args, 0)); return JgsValue.Null; });

        Define("legend", (args, line, col) =>
        {
            var names = new string[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                names[i] = Str("legend", args, i, line, col);
            }

            JG.Legend(names);
            return JgsValue.Null;
        });

        Define("show", (args, line, col) =>
        {
            ArityRange("show", args, 0, 1, line, col);
            if (args.Count == 1)
            {
                int number = Count("show", args, 0, line, col);
                if (!JG.TryGetFigure(number, out _))
                {
                    throw new JgsRuntimeException(line, col, $"There is no figure {number} to show.");
                }

                host.show(number);
            }
            else
            {
                host.show();
            }

            return JgsValue.Null;
        });

        // --- Figure files --------------------------------------------------------------------
        Define("savefigure", (args, line, col) => FigureFile("savefigure", args, line, col,
            (path, figure) => host.savefigure(path, figure)));
        Define("exportfigure", (args, line, col) => FigureFile("exportfigure", args, line, col,
            (path, figure) => host.exportfigure(path, figure)));

        Define("loadfigure", (args, line, col) =>
        {
            Arity("loadfigure", args, 1, line, col);
            try
            {
                FigureModel figure = host.loadfigure(Str("loadfigure", args, 0, line, col));
                return JgsValue.Number(JG.GetFigureNumber(figure));
            }
            catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        return env;
    }

    /// <summary>Dispatches savefigure/exportfigure: (path) targets the current figure, (path, fig)
    /// a figure by 1-based handle. Host/IO failures become script diagnostics.</summary>
    private static JgsValue FigureFile(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Action<string, FigureModel> apply)
    {
        ArityRange(name, args, 1, 2, line, col);
        string path = Str(name, args, 0, line, col);

        FigureModel figure;
        if (args.Count == 2)
        {
            int number = Count(name, args, 1, line, col);
            if (!JG.TryGetFigure(number, out figure))
            {
                throw new JgsRuntimeException(line, col, $"There is no figure {number}.");
            }
        }
        else
        {
            figure = JG.CurrentFigure;
        }

        try
        {
            apply(path, figure);
        }
        catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return JgsValue.Null;
    }

    // --- Plotting dispatch -----------------------------------------------------------------------

    private static JgsValue Plot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            ArityRange("plot", args, 3, 4, line, col);
            Table table = Tbl("plot", args, 0, line, col);
            string xColumn = Str("plot", args, 1, line, col);
            string yColumn = Str("plot", args, 2, line, col);
            string? spec = args.Count == 4 ? Str("plot", args, 3, line, col) : null;
            JG.Plot(table, xColumn, yColumn, spec);
            return JgsValue.Null;
        }

        switch (args.Count)
        {
            case 1:
                JG.Plot(DoubleArray("plot", args, 0, line, col));
                return JgsValue.Null;
            case 2 when args[1].Type == JgsType.String:
                JG.Plot(DoubleArray("plot", args, 0, line, col), Str("plot", args, 1, line, col));
                return JgsValue.Null;
            case 2:
                JG.Plot(DoubleArray("plot", args, 0, line, col), DoubleArray("plot", args, 1, line, col));
                return JgsValue.Null;
            case 3:
                JG.Plot(DoubleArray("plot", args, 0, line, col), DoubleArray("plot", args, 1, line, col), Str("plot", args, 2, line, col));
                return JgsValue.Null;
            default:
                throw new JgsRuntimeException(line, col, "plot expects (y), (x, y), (x, y, spec), or (table, xColumn, yColumn[, spec]).");
        }
    }

    private static JgsValue XyOrTable(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Action<double[], double[]> arrays, Action<Table, string, string> table)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            Arity(name, args, 3, line, col);
            table(Tbl(name, args, 0, line, col), Str(name, args, 1, line, col), Str(name, args, 2, line, col));
            return JgsValue.Null;
        }

        Arity(name, args, 2, line, col);
        arrays(DoubleArray(name, args, 0, line, col), DoubleArray(name, args, 1, line, col));
        return JgsValue.Null;
    }

    private static JgsValue Stem(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("stem", args, 1, 2, line, col);
        if (args.Count == 1)
        {
            JG.Stem(DoubleArray("stem", args, 0, line, col));
        }
        else
        {
            JG.Stem(DoubleArray("stem", args, 0, line, col), DoubleArray("stem", args, 1, line, col));
        }

        return JgsValue.Null;
    }

    private static JgsValue Histogram(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            ArityRange("histogram", args, 2, 3, line, col);
            int tableBins = args.Count == 3 ? Count("histogram", args, 2, line, col) : 10;
            JG.Histogram(Tbl("histogram", args, 0, line, col), Str("histogram", args, 1, line, col), tableBins);
            return JgsValue.Null;
        }

        ArityRange("histogram", args, 1, 2, line, col);
        int bins = args.Count == 2 ? Count("histogram", args, 1, line, col) : 10;
        JG.Histogram(DoubleArray("histogram", args, 0, line, col), bins);
        return JgsValue.Null;
    }

    private static JgsValue ErrorBar(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            Arity("errorbar", args, 4, line, col);
            JG.ErrorBar(Tbl("errorbar", args, 0, line, col), Str("errorbar", args, 1, line, col), Str("errorbar", args, 2, line, col), Str("errorbar", args, 3, line, col));
            return JgsValue.Null;
        }

        Arity("errorbar", args, 3, line, col);
        JG.ErrorBar(DoubleArray("errorbar", args, 0, line, col), DoubleArray("errorbar", args, 1, line, col), DoubleArray("errorbar", args, 2, line, col));
        return JgsValue.Null;
    }

    private static JgsValue Semilog(string name, IReadOnlyList<JgsValue> args, int line, int col, Action<double[], double[], string?> apply)
    {
        ArityRange(name, args, 2, 3, line, col);
        string? spec = args.Count == 3 ? Str(name, args, 2, line, col) : null;
        apply(DoubleArray(name, args, 0, line, col), DoubleArray(name, args, 1, line, col), spec);
        return JgsValue.Null;
    }

    private static JgsValue Filled(string name, IReadOnlyList<JgsValue> args, double value, int line, int col)
    {
        Arity(name, args, 1, line, col);
        int count = Count(name, args, 0, line, col);
        if (count < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-negative count.");
        }

        var result = new JgsValue[count];
        JgsValue element = JgsValue.Number(value);
        for (int i = 0; i < count; i++)
        {
            result[i] = element;
        }

        return JgsValue.Array(result);
    }

    private static JgsValue Reduce(string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double, double, double> op, double seed)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        double acc = seed;
        foreach (double v in values)
        {
            acc = op(acc, v);
        }

        return JgsValue.Number(acc);
    }

    private static JgsValue MinMax(string name, IReadOnlyList<JgsValue> args, int line, int col, bool takeMin)
    {
        double[] values;
        if (args.Count == 1 && args[0].Type == JgsType.Array)
        {
            values = DoubleArray(name, args, 0, line, col);
        }
        else
        {
            values = new double[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                values[i] = Num(name, args, i, line, col);
            }
        }

        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one value.");
        }

        double best = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            best = takeMin ? System.Math.Min(best, values[i]) : System.Math.Max(best, values[i]);
        }

        return JgsValue.Number(best);
    }

    /// <summary>Dispatches a reader builtin: (path) or (path, skiprows) discarding leading junk rows.</summary>
    private static JgsValue ReadTable(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<string, Table> read, Func<string, int, Table> readSkipping)
    {
        ArityRange(name, args, 1, 2, line, col);
        string path = Str(name, args, 0, line, col);
        return JgsValue.Table(args.Count == 2
            ? readSkipping(path, Count(name, args, 1, line, col))
            : read(path));
    }

    // --- Stdlib glue -----------------------------------------------------------------------------

    /// <summary>Wraps a double[] back into a JGS numeric array value.</summary>
    private static JgsValue Numbers(double[] values)
    {
        var result = new JgsValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = JgsValue.Number(values[i]);
        }

        return JgsValue.Array(result);
    }

    private static double SampleVariance(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        if (values.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least 2 values.");
        }

        return JgsStdlib.Variance(values);
    }

    private static double[] NonEmpty(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-empty array.");
        }

        return values;
    }

    private static bool OrderIsDescending(string name, IReadOnlyList<JgsValue> args, int line, int col) =>
        Str(name, args, 1, line, col) switch
        {
            "asc" => false,
            "desc" => true,
            var other => throw new JgsRuntimeException(line, col, $"{name} order must be \"asc\" or \"desc\", but got \"{other}\"."),
        };

    private static JgsValue MapToBool(string name, JgsValue value, Func<double, bool> test, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Bool(test(value.AsNumber));
        }

        if (value.Type == JgsType.Array)
        {
            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col, $"{name} expects numeric array elements, but one was a {source[i].TypeName}.");
                }

                result[i] = JgsValue.Bool(test(source[i].AsNumber));
            }

            return JgsValue.Array(result);
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>Element-wise logic over truthiness, broadcasting a scalar across an array.</summary>
    private static JgsValue Logical2(string name, IReadOnlyList<JgsValue> args, int line, int col, Func<bool, bool, bool> op)
    {
        Arity(name, args, 2, line, col);
        JgsValue left = args[0];
        JgsValue right = args[1];

        if (left.Type != JgsType.Array && right.Type != JgsType.Array)
        {
            return JgsValue.Bool(op(left.IsTruthy, right.IsTruthy));
        }

        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            JgsValue[] a = left.AsArray;
            JgsValue[] b = right.AsArray;
            if (a.Length != b.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} cannot combine arrays of different lengths ({a.Length} and {b.Length}).");
            }

            var pairwise = new JgsValue[a.Length];
            for (int i = 0; i < pairwise.Length; i++)
            {
                pairwise[i] = JgsValue.Bool(op(a[i].IsTruthy, b[i].IsTruthy));
            }

            return JgsValue.Array(pairwise);
        }

        bool arrayOnLeft = left.Type == JgsType.Array;
        JgsValue[] array = (arrayOnLeft ? left : right).AsArray;
        bool scalar = (arrayOnLeft ? right : left).IsTruthy;
        var result = new JgsValue[array.Length];
        for (int i = 0; i < result.Length; i++)
        {
            bool element = array[i].IsTruthy;
            result[i] = JgsValue.Bool(arrayOnLeft ? op(element, scalar) : op(scalar, element));
        }

        return JgsValue.Array(result);
    }

    // --- Argument helpers ------------------------------------------------------------------------

    private static JgsValue[] Arr(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an array, but got a {value.TypeName}.");
        }

        return value.AsArray;
    }


    private static JgsValue MapNumeric(string name, JgsValue value, Func<double, double> f, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Number(f(value.AsNumber));
        }

        if (value.Type == JgsType.Array)
        {
            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col, $"{name} expects numeric array elements, but one was a {source[i].TypeName}.");
                }

                result[i] = JgsValue.Number(f(source[i].AsNumber));
            }

            return JgsValue.Array(result);
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    private static void Arity(string name, IReadOnlyList<JgsValue> args, int count, int line, int col)
    {
        if (args.Count != count)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects {count} argument(s), but got {args.Count}.");
        }
    }

    private static void ArityRange(string name, IReadOnlyList<JgsValue> args, int min, int max, int line, int col)
    {
        if (args.Count < min || args.Count > max)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects between {min} and {max} argument(s), but got {args.Count}.");
        }
    }

    private static double Num(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a number, but got a {value.TypeName}.");
        }

        return value.AsNumber;
    }

    private static int Count(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double raw = Num(name, args, index, line, col);
        if (raw != System.Math.Floor(raw) || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a whole number.");
        }

        return (int)raw;
    }

    private static string Str(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a string, but got a {value.TypeName}.");
        }

        return value.AsString;
    }

    private static Table Tbl(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Table)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a table, but got a {value.TypeName}.");
        }

        return value.AsTable;
    }

    private static bool Truthy(IReadOnlyList<JgsValue> args, int index) => args[index].IsTruthy;

    private static double[] DoubleArray(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an array, but got a {value.TypeName}.");
        }

        return ToDoubles(name, value.AsArray, line, col);
    }

    private static double[] ArrayOfNumbers(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(name, args, 1, line, col);
        if (args[0].Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an array, but got a {args[0].TypeName}.");
        }

        return ToDoubles(name, args[0].AsArray, line, col);
    }

    private static double[] ToDoubles(string name, JgsValue[] elements, int line, int col)
    {
        // Bools read as 0/1, so a mask is a numeric array: sum(mask) counts its matches.
        var result = new double[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Type is not (JgsType.Number or JgsType.Bool))
            {
                throw new JgsRuntimeException(line, col, $"{name} expects an array of numbers, but element {i} was a {elements[i].TypeName}.");
            }

            result[i] = elements[i].AsNumber;
        }

        return result;
    }
}
