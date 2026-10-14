using System.Runtime.CompilerServices;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's <c>CategoricalRuler</c> (open item 120): a categorical handed to a chart puts its
/// categories on the ruler the values stand along, category <c>k</c> at position <c>k</c>; the chart
/// draws positions, and its data, the axes' limits and ticks read back as categoricals. A held axes
/// adds the new categories at the end; a categorical onto a numeric ruler that holds data is
/// R2025b's <c>MATLAB:graphics:configureAxes:Mixing</c>, and numbers onto a categorical ruler are
/// positions. Measured on R2025b (probe_120). The MATLAB dialect's alone; JGS keeps its codes.
/// </summary>
internal static class JgsCategoricalCharts
{
    /// <summary>A chart's categorical properties: each one's categories, and whether it stands on the y ruler.</summary>
    private sealed class Sides
    {
        public readonly Dictionary<string, (JgsCategoricalTag Tag, bool OnY)> Properties = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<GraphObject, Sides> Charts = new();

    /// <summary>Which leading data arguments of a verb stand along which ruler.</summary>
    private enum Layout
    {
        /// <summary>x then y: plot, bar, scatter and their kind.</summary>
        XThenY,

        /// <summary>barh: the first along the y ruler.</summary>
        Horizontal,

        /// <summary>xline, xregion: every leading value along x.</summary>
        AllX,

        /// <summary>yline, yregion: every leading value along y.</summary>
        AllY,
    }

    private static readonly Dictionary<string, Layout> Verbs = new(StringComparer.Ordinal)
    {
        ["plot"] = Layout.XThenY, ["line"] = Layout.XThenY, ["scatter"] = Layout.XThenY, ["stem"] = Layout.XThenY,
        ["area"] = Layout.XThenY, ["stairs"] = Layout.XThenY, ["errorbar"] = Layout.XThenY, ["swarmchart"] = Layout.XThenY,
        ["boxchart"] = Layout.XThenY, ["bar"] = Layout.XThenY, ["plot3"] = Layout.XThenY, ["semilogy"] = Layout.XThenY,
        ["text"] = Layout.XThenY, ["barh"] = Layout.Horizontal,
        ["xline"] = Layout.AllX, ["xregion"] = Layout.AllX, ["yline"] = Layout.AllY, ["yregion"] = Layout.AllY,
    };

    /// <summary>Wraps the chart verbs; registered after every other wrapper of them.</summary>
    internal static void Register(JgsEnvironment env)
    {
        // bar and barh over a matrix answer a row of bars, one a column of data (R2025b, probe_120's
        // bar_cat_two; plot answers a column of lines as it always did).
        foreach (string barVerb in (string[])["bar", "barh"])
        {
            if (env.Builtins.TryGet(barVerb, out JgsValue existingBar) && existingBar.Type == JgsType.Function)
            {
                IJgsCallable bars = existingBar.AsCallable;
                var rowWrapper = new BuiltinFunction(barVerb, (args, line, col) =>
                {
                    JgsValue made = bars.Call(args, line, col);
                    if (JgsRunningDialect.ThreadIsMatlab && made.Type == JgsType.Array && made.Cols == 1 && made.Rows > 1)
                    {
                        made.Reshape(1, made.Rows);
                    }

                    return made;
                })
                {
                    BindsAnsAsStatement = bars is not BuiltinFunction { BindsAnsAsStatement: false },
                    KnowsWhenDiscarded = bars is BuiltinFunction { KnowsWhenDiscarded: true },
                    KeepsStringArguments = bars is BuiltinFunction { KeepsStringArguments: true },
                };
                env.Builtins.Register(barVerb, JgsValue.Function(rowWrapper));
            }
        }

        foreach ((string name, Layout layout) in Verbs)
        {
            Func<IReadOnlyList<JgsValue>, bool>? also = name switch
            {
                "bar" or "barh" => static args => TextX(args) >= 0,
                "xline" or "xregion" or "yline" or "yregion" => static args => args.Any(static a => a.Type == JgsType.Cell),
                _ => null,
            };
            Wrap(env, name, (inner, args, wanted, line, col) => Chart(name, layout, inner, TextAsCategorical(name, args, line, col), wanted, line, col), also);
        }

        Wrap(env, "pie3", static (inner, args, wanted, line, col) =>
            throw new JgsRuntimeException(line, col, "MATLAB:pie:NonNumericData", "Must have numeric data in the pie chart."));
        Wrap(env, "histcounts", HistCounts);

        Wrap(env, "histogram", Histogram, alsoWhen: static args => args.Any(static a => a.Type == JgsType.String && a.AsString.Equals("Categories", StringComparison.OrdinalIgnoreCase)));
        Wrap(env, "pie", Pie);
    }

    private delegate JgsValue[]? Body(IJgsCallable inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col);

    private static void Wrap(JgsEnvironment env, string name, Body body, Func<IReadOnlyList<JgsValue>, bool>? alsoWhen = null)
    {
        if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
        {
            return;
        }

        IJgsCallable inner = existing.AsCallable;
        JgsValue[] Both(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            if (JgsRunningDialect.ThreadIsMatlab && (args.Any(static a => a.IsCategorical) || (alsoWhen?.Invoke(args) ?? false))
                && body(inner, args, wanted, line, col) is { } answered)
            {
                return answered;
            }

            return Call(inner, args, wanted, line, col);
        }

        var wrapper = new BuiltinFunction(name, (args, line, col) => Both(args, 1, line, col)[0])
        {
            MultiOutput = Both,
            BindsAnsAsStatement = inner is not BuiltinFunction { BindsAnsAsStatement: false },
            AutoCallsBare = inner is BuiltinFunction { AutoCallsBare: true },
            KnowsWhenDiscarded = inner is BuiltinFunction { KnowsWhenDiscarded: true },
            TakesOutputCount = inner is BuiltinFunction { TakesOutputCount: true },
        };
        if (inner is BuiltinFunction { KeepsStringArguments: true })
        {
            wrapper.KeepsStringArguments = true;
        }

        env.Builtins.Register(name, JgsValue.Function(wrapper));
    }

    /// <summary>Where a bar's x stands when it is text (a cell of names or a string array) followed by data, or -1.</summary>
    private static int TextX(IReadOnlyList<JgsValue> args)
    {
        int at = args.Count > 0 && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry) && entry.Target is AxesModel ? 1 : 0;
        return at + 1 < args.Count && args[at + 1].Type != JgsType.String
            && ((args[at].Type == JgsType.Cell && args[at].AsCell.All(static c => c.Type == JgsType.String)) || (args[at].IsStringArray && args[at].ArrayLength > 1))
            ? at : -1;
    }

    /// <summary>
    /// <c>bar(["a" "b"], y)</c> draws over a categorical made of the names (probe_120); and a cell
    /// handed to a constant line is R2025b's refusal of the Value.
    /// </summary>
    private static IReadOnlyList<JgsValue> TextAsCategorical(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (verb is "xline" or "xregion" or "yline" or "yregion" && args.Any(static a => a.Type == JgsType.Cell))
        {
            string words = "Value must be one of the following: 'numeric' | 'datetime' | 'categorical' | 'duration'";
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:InvalidData", verb is "xregion" or "yregion"
                ? $"Error setting property 'Value' of class 'matlab.graphics.chart.decoration.ConstantRegion'. {words}"
                : words);
        }

        int at = verb is "bar" or "barh" ? TextX(args) : -1;
        if (at < 0)
        {
            return args;
        }

        JgsValue text = args[at];
        string[] names = text.Type == JgsType.Cell ? [.. text.AsCell.Select(static c => c.AsString)] : [.. text.BoxedElements().Select(static c => c.AsString)];
        string[] categories = JgsCategorical.SortedNames(names.Distinct(StringComparer.Ordinal), false);
        double[] codes = [.. names.Select(n => (double)(Array.IndexOf(categories, n) + 1))];
        var given = args.ToArray();
        given[at] = JgsCategorical.FromCodes(codes, text.Rows, text.Cols, new JgsCategoricalTag(categories, false, false));
        return given;
    }

    /// <summary><c>histcounts(C, names)</c>: the counts of the named categories only, in that order (probe_120).</summary>
    private static JgsValue[]? HistCounts(IJgsCallable inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count != 2 || !args[0].IsCategorical || NamesOf(args[1]) is not { } named)
        {
            return null;
        }

        string[] names = JgsCategorical.NamesOf(args[0]);
        double[] counts = [.. named.Select(n => (double)names.Count(m => m == n))];
        JgsValue cats = JgsValue.Cell([.. named.Select(JgsValue.Str)]);
        cats.Reshape(1, named.Length);
        return [JgsMatrix.FromColumnMajor(counts, 1, counts.Length), cats];
    }

    private static JgsValue[] Call(IJgsCallable inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
        inner is IJgsMultiCallable multi ? multi.CallMultiple(args, Math.Max(wanted, 1), line, col) : [inner.Call(args, line, col)];

    /// <summary>The axes a call draws into, and where its data arguments start.</summary>
    private static (AxesModel Axes, int Start) Target(IReadOnlyList<JgsValue> args)
    {
        if (args.Count > 0 && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry) && entry.Target is AxesModel named)
        {
            return (named, 1);
        }

        return (JG.Gca(), 0);
    }

    private static JgsRuntimeException Mixing(int line, int col) =>
        new(line, col, "MATLAB:graphics:configureAxes:Mixing",
            "Data inputs must match the axis configuration. A numeric axis must have numeric data inputs or data inputs which can be converted to double.");

    /// <summary>
    /// The categories a ruler takes for <paramref name="value"/>: a held axes keeps its own and adds
    /// the new ones at the end; anything else starts from the value's.
    /// </summary>
    private static IReadOnlyList<string> RulerFor(AxesModel axes, AxisModel ruler, JgsValue value, bool held, int line, int col)
    {
        string[] own = value.CategoricalTag!.Categories;
        if (!held)
        {
            return own;
        }

        if (ruler.RulerCategories is not { } existing)
        {
            if (axes.Plots.Count > 0)
            {
                throw Mixing(line, col);
            }

            return own;
        }

        var merged = new List<string>(existing);
        merged.AddRange(own.Where(n => !merged.Contains(n, StringComparer.Ordinal)));
        return merged;
    }

    /// <summary>Each element's position on a ruler of <paramref name="categories"/>, NaN for one it lacks.</summary>
    private static JgsValue Positions(JgsValue value, IReadOnlyList<string> categories)
    {
        string[] names = JgsCategorical.NamesOf(value);
        var positions = new double[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int at = IndexOf(categories, names[i]);
            positions[i] = at < 0 ? double.NaN : at + 1;
        }

        return JgsMatrix.Like(value, JgsMatrix.FromColumnMajor(positions, value.Rows, value.Cols));
    }

    private static int IndexOf(IReadOnlyList<string> list, string name)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A chart verb with categorical data: positions drawn, the rulers named, the chart remembered.</summary>
    private static JgsValue[]? Chart(string verb, Layout layout, IJgsCallable inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        (AxesModel axes, int start) = Target(args);
        bool held = axes.Hold;
        var given = args.ToArray();
        IReadOnlyList<string>? xCategories = null;
        IReadOnlyList<string>? yCategories = null;
        var sides = new List<(string Property, JgsCategoricalTag Tag, bool OnY)>();

        int dataCount = 0;
        while (start + dataCount < given.Length && given[start + dataCount].Type != JgsType.String)
        {
            dataCount++;
        }

        for (int k = 0; k < dataCount; k++)
        {
            JgsValue value = given[start + k];
            if (!value.IsCategorical)
            {
                continue;
            }

            bool onY = layout switch
            {
                Layout.Horizontal => k == 0,
                Layout.AllX => false,
                Layout.AllY => true,
                _ => k == 1,
            };
            if (layout is Layout.XThenY && k > 1)
            {
                return null; // a z or a size that is categorical is the verb's own refusal
            }

            AxisModel ruler = onY ? axes.PrimaryYAxis : axes.PrimaryXAxis;
            IReadOnlyList<string> categories = onY ? yCategories ??= RulerFor(axes, ruler, value, held, line, col)
                : xCategories ??= RulerFor(axes, ruler, value, held, line, col);
            given[start + k] = Positions(value, categories);
            string property = layout switch
            {
                Layout.AllX or Layout.AllY => "Value",
                Layout.Horizontal => "XData",
                _ => onY ? "YData" : "XData",
            };
            JgsCategoricalTag tag = value.CategoricalTag!;
            int earlier = sides.FindIndex(s => s.Property == property);
            if (earlier >= 0)
            {
                tag = JgsCategorical.Merge(sides[earlier].Tag, tag, line, col); // xregion's two ends make one Value
                sides.RemoveAt(earlier);
            }

            sides.Add((property, tag, onY));
        }

        if (sides.Count == 0)
        {
            return null;
        }

        JgsValue[] answered = Call(inner, given, wanted, line, col);
        if (xCategories is not null)
        {
            axes.PrimaryXAxis.RulerCategories = [.. xCategories];
        }

        if (yCategories is not null)
        {
            axes.PrimaryYAxis.RulerCategories = [.. yCategories];
        }

        axes.RecomputeDataBounds();
        if (answered.Length > 0 && verb != "text")
        {
            Remember(answered[0], sides);
        }

        return answered;
    }

    private static void Remember(JgsValue handles, List<(string Property, JgsCategoricalTag Tag, bool OnY)> sides)
    {
        int count = handles.Type == JgsType.Array ? handles.ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            JgsValue one = handles.Type == JgsType.Array ? handles.ElementAt(i) : handles;
            if (!JgsHandleRegistry.TryGet(one, out JgsHandleEntry? entry))
            {
                continue;
            }

            Sides chart = Charts.GetOrCreateValue(entry.Target);
            foreach ((string property, JgsCategoricalTag tag, bool onY) in sides)
            {
                chart.Properties[property] = (tag, onY);
            }
        }
    }

    // ---- histogram and pie ----------------------------------------------------------------------

    /// <summary>
    /// <c>histogram(C)</c> of a categorical counts its categories, the unused ones included and the
    /// undefined left out, or the named ones only (<c>histogram(C, {'y', 'x'})</c>); the bars stand on
    /// a categorical ruler of the categories shown, 0.9 wide (probe_120).
    /// </summary>
    private static JgsValue[]? Histogram(IJgsCallable inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        (AxesModel axes, int start) = Target(args);
        if (start < args.Count && args[start].Type == JgsType.String
            && OptionValue(args, start, "Categories") is { } given && OptionValue(args, start, "BinCounts") is { } binCounts)
        {
            // histogram('Categories', names, 'BinCounts', counts): the counting done already (probe_120).
            string[] listed = given.Type == JgsType.Cell ? [.. given.AsCell.Select(static c => c.AsString)] : [.. given.BoxedElements().Select(static c => c.AsString)];
            double[] counted = JgsCategorical.CodesOf(binCounts);
            var others = new List<JgsValue>();
            for (int i = start; i + 1 < args.Count; i += 2)
            {
                if (!(args[i].AsString.Equals("Categories", StringComparison.OrdinalIgnoreCase) || args[i].AsString.Equals("BinCounts", StringComparison.OrdinalIgnoreCase)))
                {
                    others.Add(args[i]);
                    others.Add(args[i + 1]);
                }
            }

            JgsValue made = JgsBuiltins.CategoricalHistogram(start == 1 ? axes : null, others, listed, counted, line, col);
            if (JgsHandleRegistry.TryGet(made, out JgsHandleEntry? madeEntry) && madeEntry.Target is HistogramPlot madePlot)
            {
                HistogramData.AddOrUpdate(madePlot, JgsCategorical.FromCodes([], 0, 0, new JgsCategoricalTag(listed, false, false)));
                Name(axes, madePlot, listed);
            }

            return [made];
        }

        if (start >= args.Count || !args[start].IsCategorical)
        {
            return null;
        }

        JgsValue data = args[start];
        string[] categories = data.CategoricalTag!.Categories;
        int rest = start + 1;
        if (rest < args.Count && (args[rest].Type == JgsType.Cell || args[rest].IsStringArray || args[rest].IsCategorical))
        {
            categories = args[rest].IsCategorical ? JgsCategorical.NamesOf(args[rest])
                : args[rest].Type == JgsType.Cell ? [.. args[rest].AsCell.Select(static c => c.AsString)]
                : [.. args[rest].BoxedElements().Select(static c => c.AsString)];
            rest++;
        }
        else if (rest < args.Count && args[rest].Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:histogram:invalidType",
                $"Expected input number 2, categories, to be one of these types:  string, cell, categorical, pattern  Instead its type was {JgsBuiltins.ClassOf(args[rest], JgsDialect.Matlab)}.");
        }

        string[] names = JgsCategorical.NamesOf(data);
        var counts = new double[categories.Length];
        foreach (string name in names)
        {
            int at = Array.IndexOf(categories, name);
            if (at >= 0)
            {
                counts[at]++;
            }
        }

        bool held = axes.Hold;
        if (held && axes.PrimaryXAxis.RulerCategories is null && axes.Plots.Count > 0)
        {
            throw Mixing(line, col);
        }

        JgsValue[] answered = [JgsBuiltins.CategoricalHistogram(start == 1 ? axes : null, [.. args.Skip(rest)], categories, counts, line, col)];
        if (answered.Length > 0 && JgsHandleRegistry.TryGet(answered[0], out JgsHandleEntry? entry) && entry.Target is HistogramPlot histogram)
        {
            HistogramData.AddOrUpdate(histogram, data);
            Name(axes, histogram, categories);
        }

        return answered;
    }

    /// <summary>The ruler a categorical histogram stands on takes the categories it shows, 0.9 of a category wide.</summary>
    private static void Name(AxesModel axes, HistogramPlot histogram, string[] categories)
    {
        histogram.BarWidth = 0.9;
        AxisModel standsOn = histogram.Orientation == HistogramOrientation.Vertical ? axes.PrimaryXAxis : axes.PrimaryYAxis;
        standsOn.RulerCategories = standsOn.TickLabelOverrides is { } shown ? [.. shown] : categories;
        standsOn.TickPositions = null;
        standsOn.TickLabelOverrides = null;
        axes.RecomputeDataBounds();
    }

    /// <summary>The value after a name in a name/value tail, or null.</summary>
    private static JgsValue? OptionValue(IReadOnlyList<JgsValue> args, int from, string option)
    {
        for (int i = from; i + 1 < args.Count; i++)
        {
            if (args[i].Type == JgsType.String && args[i].AsString.Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>The categorical each categorical histogram counted, which its <c>Data</c> answers.</summary>
    private static readonly ConditionalWeakTable<HistogramPlot, JgsValue> HistogramData = new();

    /// <summary>
    /// <c>pie(C)</c> of a categorical: a slice a category, the unused ones included and the undefined
    /// left out, each labelled with its name and its share, <c>x (67%)</c>; an explode given as names
    /// pulls those slices out (probe_120).
    /// </summary>
    private static JgsValue[]? Pie(IJgsCallable inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        int start = args.Count > 0 && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry) && entry.Target is AxesModel ? 1 : 0;
        if (start >= args.Count || !args[start].IsCategorical)
        {
            return null;
        }

        JgsValue data = args[start];
        string[] categories = data.CategoricalTag!.Categories;
        string[] names = JgsCategorical.NamesOf(data);
        var counts = new double[categories.Length];
        foreach (string name in names)
        {
            int at = Array.IndexOf(categories, name);
            if (at >= 0)
            {
                counts[at]++;
            }
        }

        double total = counts.Sum();
        var labels = new JgsValue[categories.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            double share = total == 0 ? 0 : Math.Round(100 * counts[i] / total, MidpointRounding.AwayFromZero);
            labels[i] = JgsValue.Str($"{categories[i]} ({share.ToString(System.Globalization.CultureInfo.InvariantCulture)}%)");
        }

        var forwarded = new List<JgsValue>(args.Take(start)) { JgsMatrix.FromColumnMajor(counts, 1, counts.Length) };
        if (start + 1 < args.Count)
        {
            JgsValue explode = args[start + 1];
            if (explode.Type == JgsType.Cell || explode.IsStringArray || explode.Type == JgsType.String)
            {
                string[] pulled = explode.Type == JgsType.Cell ? [.. explode.AsCell.Select(static c => c.AsString)]
                    : explode.Type == JgsType.String ? [explode.AsString]
                    : [.. explode.BoxedElements().Select(static c => c.AsString)];
                if (pulled.Any(p => !categories.Contains(p, StringComparer.Ordinal)))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:pie:InvalidExplodeName", "Explode contains invalid category names.");
                }

                forwarded.Add(JgsMatrix.FromColumnMajor([.. categories.Select(c => pulled.Contains(c, StringComparer.Ordinal) ? 1.0 : 0.0)], 1, categories.Length));
            }
            else
            {
                forwarded.Add(explode);
            }
        }
        else
        {
            forwarded.Add(JgsMatrix.FromColumnMajor(new double[categories.Length], 1, categories.Length));
        }

        JgsValue labelCell = JgsValue.Cell(labels);
        labelCell.Reshape(1, labels.Length);
        forwarded.Add(labelCell);
        return Call(inner, forwarded, wanted, line, col);
    }

    // ---- reading and writing ---------------------------------------------------------------------

    /// <summary>
    /// A property read the categorical ruler answers: a chart's categorical data, an axes' limits,
    /// ticks and tick labels on a categorical ruler, a ruler's categories, limits and tick values.
    /// </summary>
    internal static bool TryRead(JgsHandleEntry entry, string name, int line, int col, bool dot, out JgsValue value)
    {
        value = JgsValue.Null;
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return false;
        }

        switch (entry.Target)
        {
            case HistogramPlot histogram when name.Equals("Data", StringComparison.OrdinalIgnoreCase)
                && HistogramData.TryGetValue(histogram, out JgsValue? counted):
                value = counted;
                return true;

            case HistogramPlot others when name.Equals("OthersValue", StringComparison.OrdinalIgnoreCase)
                && HistogramData.TryGetValue(others, out _):
            {
                // What the categories not shown hold between them, shown as Others or not (probe_120).
                double all = others.CategoryCounts.Sum();
                double[] drawn = others.BinCounts;
                double shownCount = drawn.Sum() - (others.ShowOthers && drawn.Length > 0 ? drawn[^1] : 0);
                value = JgsValue.Number(all - shownCount);
                return true;
            }

            case HistogramPlot numbered when dot && HistogramData.TryGetValue(numbered, out _)
                && name.ToLowerInvariant() is "binedges" or "numbins" or "binwidth" or "binlimits" or "binmethod" or "binlimitsmode":
                throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
                    $"Unrecognized method, property, or field '{name}' for class 'matlab.graphics.chart.primitive.categorical.Histogram'.");

            case HistogramPlot histogram when name.Equals("Categories", StringComparison.OrdinalIgnoreCase)
                && HistogramData.TryGetValue(histogram, out _):
            {
                string[] shown = histogram.Orientation == HistogramOrientation.Vertical && histogram.Axes is { } holder
                    && holder.PrimaryXAxis.RulerCategories is { } onRuler ? [.. onRuler] : histogram.CategoryNames;
                if (histogram.ShowOthers && shown.Length > 0 && shown[^1] == "Others")
                {
                    shown = shown[..^1]; // the Others bar is not a category (probe_120)
                }
                JgsValue row = JgsValue.Cell([.. shown.Select(JgsValue.Str)]);
                row.Reshape(1, shown.Length);
                value = row;
                return true;
            }

            case PlotObject chart when Charts.TryGetValue(chart, out Sides? sides)
                && sides.Properties.TryGetValue(name, out (JgsCategoricalTag Tag, bool OnY) side) && chart.Axes is { } axes:
            {
                AxisModel ruler = side.OnY ? axes.PrimaryYAxis : axes.PrimaryXAxis;
                if (ruler.RulerCategories is not { } categories
                    || !JgsGraphicsProperties.TryFind(chart, name, out GraphicsProperty property))
                {
                    return false;
                }

                JgsValue numbers = property.Read(entry);
                value = AsCategorical(numbers, categories, side.Tag);
                return true;
            }

            case AxesModel axes:
            {
                (AxisModel? ruler, string part) = AxesPart(axes, name);
                return ruler?.RulerCategories is { } categories && ReadRulerPart(ruler, categories, part, out value);
            }

            case AxisModel ruler when ruler.RulerCategories is { } categories:
                if (name.Equals("Categories", StringComparison.OrdinalIgnoreCase))
                {
                    value = JgsCategorical.CategoriesColumn(new JgsCategoricalTag([.. categories], false, false));
                    return true;
                }

                return name.ToLowerInvariant() switch
                {
                    "limits" => ReadRulerPart(ruler, categories, "Lim", out value),
                    "tickvalues" => ReadRulerPart(ruler, categories, "Tick", out value),
                    _ => false,
                };

            default:
                return false;
        }
    }

    /// <summary>A ruler verb's reading (<c>xlim</c>, <c>xticks</c>, <c>xticklabels</c>) of a categorical ruler.</summary>
    internal static bool TryReadRuler(AxisModel ruler, string part, out JgsValue value)
    {
        value = JgsValue.Null;
        return JgsRunningDialect.ThreadIsMatlab && ruler.RulerCategories is { } categories && ReadRulerPart(ruler, categories, part, out value);
    }

    /// <summary>A ruler verb's write (<c>xlim(c)</c>, <c>xticks(c)</c>) of a categorical ruler.</summary>
    internal static bool TryWriteRuler(AxisModel ruler, string part, JgsValue value, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || ruler.RulerCategories is not { } categories)
        {
            return false;
        }

        WriteRulerPart(ruler, categories, part, value, line, col);
        return true;
    }

    /// <summary>
    /// <c>ruler2num</c> and <c>num2ruler</c> on a categorical ruler (probe_120): a categorical is its
    /// positions, text is refused, and a number is the category nearest it, clamped to the ruler.
    /// </summary>
    internal static bool TryConvert(string verb, AxisModel ruler, JgsValue value, int line, int col, out JgsValue converted)
    {
        converted = JgsValue.Null;
        if (!JgsRunningDialect.ThreadIsMatlab || ruler.RulerCategories is not { } categories)
        {
            return false;
        }

        if (verb == "ruler2num")
        {
            if (!value.IsCategorical)
            {
                if (value.Type is JgsType.Number or JgsType.Bool || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix))
                {
                    return false;
                }

                throw new JgsRuntimeException(line, col, "MATLAB:graphics:CategoricalRuler:NonNumericType", "Value to convert must be a numeric or categorical array.");
            }

            converted = Positions(value, categories);
            return true;
        }

        double[] numbers = JgsCategorical.CodesOf(value);
        var codes = new double[numbers.Length];
        for (int i = 0; i < codes.Length; i++)
        {
            codes[i] = double.IsNaN(numbers[i]) ? double.NaN : Math.Clamp(Math.Round(numbers[i], MidpointRounding.AwayFromZero), 1, categories.Count);
        }

        converted = JgsCategorical.FromCodes(codes, value.Type == JgsType.Number ? 1 : value.Rows, value.Type == JgsType.Number ? 1 : value.Cols,
            new JgsCategoricalTag([.. categories], false, false));
        return true;
    }

    /// <summary>The ruler an axes property is about and which of its parts: XLim is the x ruler's Lim.</summary>
    private static (AxisModel? Ruler, string Part) AxesPart(AxesModel axes, string name)
    {
        if (name.Length < 2)
        {
            return (null, string.Empty);
        }

        char side = char.ToUpperInvariant(name[0]);
        string part = name[1..].ToLowerInvariant() switch
        {
            "lim" => "Lim",
            "tick" => "Tick",
            "ticklabel" => "TickLabel",
            _ => string.Empty,
        };
        AxisModel? ruler = side switch
        {
            'X' => axes.PrimaryXAxis,
            'Y' => axes.ActiveYAxis,
            _ => null,
        };
        return part.Length == 0 ? (null, part) : (ruler, part);
    }

    private static bool ReadRulerPart(AxisModel ruler, IReadOnlyList<string> categories, string part, out JgsValue value)
    {
        var tag = new JgsCategoricalTag([.. categories], false, false);
        switch (part)
        {
            case "Lim":
            {
                DataRange range = ruler.Range;
                int low = Math.Clamp((int)Math.Ceiling(range.Min - 1e-9), 1, Math.Max(categories.Count, 1));
                int high = Math.Clamp((int)Math.Floor(range.Max + 1e-9), 1, Math.Max(categories.Count, 1));
                value = JgsCategorical.FromCodes([low, Math.Max(low, high)], 1, 2, tag);
                return true;
            }

            case "Tick":
            {
                double[] ticks = TickPositions(ruler, categories);
                value = JgsCategorical.FromCodes(ticks, 1, ticks.Length, tag);
                return true;
            }

            case "TickLabel" when ruler.TickLabelOverrides is null:
            {
                double[] ticks = TickPositions(ruler, categories);
                JgsValue column = JgsValue.Cell([.. ticks.Select(t => JgsValue.Str(categories[(int)t - 1]))]);
                column.Reshape(ticks.Length, ticks.Length == 0 ? 0 : 1);
                value = column;
                return true;
            }

            default:
                value = JgsValue.Null;
                return false;
        }
    }

    /// <summary>The positions the ruler's ticks stand at: those set, or every category in view.</summary>
    private static double[] TickPositions(AxisModel ruler, IReadOnlyList<string> categories)
    {
        if (ruler.TickPositions is { } set)
        {
            return [.. set.Where(p => p >= 1 && p <= categories.Count && p == Math.Floor(p))];
        }

        DataRange range = ruler.Range;
        return [.. Enumerable.Range(1, categories.Count).Where(k => k >= range.Min - 1e-9 && k <= range.Max + 1e-9).Select(static k => (double)k)];
    }

    /// <summary>Positions on a ruler as a categorical of <paramref name="tag"/>'s categories; a position between or off them is undefined.</summary>
    private static JgsValue AsCategorical(JgsValue numbers, IReadOnlyList<string> ruler, JgsCategoricalTag tag)
    {
        double[] positions = JgsCategorical.CodesOf(numbers);
        var codes = new double[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            double p = positions[i];
            int at = double.IsNaN(p) || p != Math.Floor(p) || p < 1 || p > ruler.Count ? -1 : Array.IndexOf(tag.Categories, ruler[(int)p - 1]);
            codes[i] = at < 0 ? double.NaN : at + 1;
        }

        return JgsCategorical.FromCodes(codes, numbers.Type == JgsType.Number ? 1 : numbers.Rows, numbers.Type == JgsType.Number ? 1 : numbers.Cols, tag);
    }

    /// <summary>
    /// A property write the categorical ruler answers: an axes' limits and ticks as categories, a
    /// ruler's categories (the charts on it move with their names), a chart's categorical data.
    /// </summary>
    internal static bool TryWrite(JgsHandleEntry entry, string name, JgsValue value, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return false;
        }

        switch (entry.Target)
        {
            case AxesModel axes:
            {
                (AxisModel? ruler, string part) = AxesPart(axes, name);
                if (ruler?.RulerCategories is not { } categories || part == "TickLabel")
                {
                    return false;
                }

                WriteRulerPart(ruler, categories, part, value, line, col);
                return true;
            }

            case AxisModel ruler when ruler.RulerCategories is { } categories:
                if (name.Equals("Categories", StringComparison.OrdinalIgnoreCase))
                {
                    Reorder(entry, ruler, NamesOf(value) ?? throw new JgsRuntimeException(line, col, "MATLAB:graphics:CategoricalRuler:Categories",
                        "Value must be a cell array of character vectors or a string array."), line, col);
                    return true;
                }

                if (name.Equals("Limits", StringComparison.OrdinalIgnoreCase) || name.Equals("TickValues", StringComparison.OrdinalIgnoreCase))
                {
                    WriteRulerPart(ruler, categories, name.Equals("Limits", StringComparison.OrdinalIgnoreCase) ? "Lim" : "Tick", value, line, col);
                    return true;
                }

                return false;

            case PlotObject chart when Charts.TryGetValue(chart, out Sides? sides)
                && sides.Properties.TryGetValue(name, out (JgsCategoricalTag Tag, bool OnY) side) && chart.Axes is { } holder:
            {
                AxisModel ruler = side.OnY ? holder.PrimaryYAxis : holder.PrimaryXAxis;
                if (ruler.RulerCategories is not { } categories || !JgsGraphicsProperties.TryFind(chart, name, out GraphicsProperty property)
                    || property.Write is null)
                {
                    return false;
                }

                if (value.IsCategorical || value.Type == JgsType.Cell || value.IsStringArray)
                {
                    JgsValue named = value.IsCategorical ? value : JgsCategorical.FromCodes([.. (NamesOf(value) ?? []).Select((_, i) => (double)(i + 1))], 1, (NamesOf(value) ?? []).Length,
                        new JgsCategoricalTag(NamesOf(value) ?? [], false, false));
                    sides.Properties[name] = (named.CategoricalTag!, side.OnY);
                    property.Write(entry, Positions(named, categories), line, col);
                }
                else
                {
                    property.Write(entry, value, line, col); // numbers on a categorical ruler are positions
                }

                return true;
            }

            default:
                return false;
        }
    }

    private static string[]? NamesOf(JgsValue value) =>
        value.IsCategorical ? JgsCategorical.NamesOf(value)
        : value.Type == JgsType.Cell ? [.. value.AsCell.Select(static c => c.AsString)]
        : value.IsStringArray ? [.. value.BoxedElements().Select(static c => c.AsString)]
        : value.Type == JgsType.String ? [value.AsString]
        : null;

    private static void WriteRulerPart(AxisModel ruler, IReadOnlyList<string> categories, string part, JgsValue value, int line, int col)
    {
        string[]? names = NamesOf(value);
        if (part == "Lim")
        {
            int low = names is { Length: 2 } ? IndexOf(categories, names[0]) : -1;
            int high = names is { Length: 2 } ? IndexOf(categories, names[1]) : -1;
            if (low < 0 || high < 0 || high < low)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:graphics:CategoricalRuler:Limits",
                    "Value must be a 2-element vector of increasing or equal categorical values as ordered by the ruler Categories property.");
            }

            ruler.AutoScale = false;
            ruler.Range = new DataRange(low + 0.5, high + 1.5);
            return;
        }

        bool empty = value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 0 && !value.IsCategorical;
        if (empty)
        {
            ruler.TickPositions = [];
            return;
        }

        int[] positions = names is null ? [-1] : [.. names.Select(n => IndexOf(categories, n))];
        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i] < 0 || (i > 0 && positions[i] <= positions[i - 1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:graphics:CategoricalRuler:Ticks",
                    "Value must be a vector of increasing categorical values as ordered by the ruler Categories property.");
            }
        }

        ruler.TickPositions = [.. positions.Select(static p => (double)(p + 1))];
    }

    /// <summary>
    /// A ruler given its categories anew: every chart on it keeps its names and moves to where they
    /// now stand (a name the ruler no longer has is undefined there), and the limits follow.
    /// </summary>
    private static void Reorder(JgsHandleEntry rulerEntry, AxisModel ruler, string[] categories, int line, int col)
    {
        IReadOnlyList<string> before = ruler.RulerCategories!;
        if (ruler.Parent is AxesModel axes)
        {
            bool onY = !ReferenceEquals(ruler, axes.PrimaryXAxis);
            foreach (PlotObject chart in axes.Plots)
            {
                if (!Charts.TryGetValue(chart, out Sides? sides))
                {
                    continue;
                }

                JgsHandleEntry entry = JgsHandleRegistry.Require(JgsHandleRegistry.For(chart), line, col);
                foreach ((string property, (JgsCategoricalTag tag, bool sideOnY)) in sides.Properties.ToArray())
                {
                    if (sideOnY != onY || !JgsGraphicsProperties.TryFind(chart, property, out GraphicsProperty found) || found.Write is null)
                    {
                        continue;
                    }

                    JgsValue named = AsCategorical(found.Read(entry), before, tag);
                    found.Write(entry, Positions(named, categories), line, col);
                }
            }
        }

        ruler.RulerCategories = categories;
        if (ruler.Parent is AxesModel owner)
        {
            owner.RecomputeDataBounds();
        }
    }
}
