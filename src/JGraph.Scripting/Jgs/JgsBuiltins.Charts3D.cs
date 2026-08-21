using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M57 wave C: the charts that put an everyday two-dimensional shape into space — <c>stem3</c>,
/// <c>bar3</c>/<c>bar3h</c> and <c>pie3</c>.
/// <para>
/// Each reads its arguments the way its flat namesake does, because a script that knows <c>bar</c>
/// should not have to learn a second grammar for <c>bar3</c>: positional data first, then the lone
/// words that stand alone (a layout, a colour, <c>'filled'</c>), then <c>'Name', value</c> pairs
/// checked against a spelled-out list.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterChart3DBuiltins(JgsEnvironment env)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        DefineSilent("stem3", (args, line, col) => Stem3(args, line, col));
        DefineSilent("bar3", (args, line, col) => Bar3("bar3", horizontal: false, args, line, col));
        DefineSilent("bar3h", (args, line, col) => Bar3("bar3h", horizontal: true, args, line, col));
        DefineSilent("pie3", (args, line, col) => Pie3(args, line, col));
    }

    // --- stem3 ------------------------------------------------------------------------------------

    /// <summary>The properties <c>stem3</c> accepts after its data.</summary>
    private static readonly string[] Stem3OptionNames =
    [
        "Color", "LineStyle", "LineWidth", "Marker", "MarkerSize", "MarkerFaceColor",
        "MarkerEdgeColor", "BaseValue", "DisplayName",
    ];

    /// <summary>
    /// <c>stem3(Z)</c>, <c>stem3(X, Y, Z)</c>, either followed by <c>'filled'</c>, a line spec and
    /// name/value pairs.
    /// <para>
    /// A matrix of Z with no positions stands its stems on the grid the matrix is indexed by — the
    /// column number along X and the row number along Y — which is what makes <c>stem3(Z)</c> and
    /// <c>bar3(Z)</c> two pictures of the same numbers.
    /// </para>
    /// </summary>
    private static JgsValue Stem3(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            int dataEnd = FirstWord(rest);
            if (dataEnd is not (1 or 3))
            {
                throw new JgsRuntimeException(line, col,
                    "stem3 takes stem3(z) or stem3(x, y, z), then 'filled', a line spec and options.");
            }

            double[] x, y, z;
            if (dataEnd == 1)
            {
                (x, y, z) = GridOf("stem3", rest[0], line, col);
            }
            else
            {
                x = DoubleArray("stem3", rest, 0, line, col);
                y = DoubleArray("stem3", rest, 1, line, col);
                z = DoubleArray("stem3", rest, 2, line, col);
                if (x.Length != y.Length || x.Length != z.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        $"stem3: x, y and z must be the same length, but they are "
                            + $"{x.Length}, {y.Length} and {z.Length}.");
                }
            }

            (IReadOnlyList<string> words, int optionStart) = PeelWords(rest, dataEnd, Stem3OptionNames);
            bool filled = false;
            string? spec = null;
            foreach (string word in words)
            {
                if (word.Equals("filled", StringComparison.OrdinalIgnoreCase))
                {
                    filled = true;
                }
                else if (IsLineSpecWord(word))
                {
                    spec = word;
                }
                else
                {
                    // A word that is neither 'filled' nor a line spec is a misspelled option, and
                    // saying so beats quietly parsing the letters of it that happen to mean
                    // something — 'Colour' has an 'o' in it, which is a marker.
                    throw new JgsRuntimeException(line, col,
                        $"stem3 has no option '{word}'. It takes {string.Join(", ", Stem3OptionNames)}.");
                }
            }

            Stem3DPlot plot = JG.Stem3(x, y, z);
            if (plot.Color is null)
            {
                SeatSeries(plot);
            }
            if (spec is not null)
            {
                ApplyStemSpec(plot, LineSpec.Parse(spec));
            }

            // 'filled' means the marker takes the stem's colour inside, which is only decided once
            // the line spec has had its say about what that colour is.
            if (filled)
            {
                plot.MarkerFill = plot.Color;
            }

            Stem3Options(plot, rest, optionStart, line, col);
            return Handle(plot);
        });
    }

    private static void ApplyStemSpec(Stem3DPlot plot, LineSpec spec)
    {
        if (spec.Color is { } color)
        {
            plot.Color = color;
        }

        if (spec.Dash is { } dash)
        {
            plot.DashStyle = dash;
        }

        if (spec.Marker is { } marker)
        {
            plot.Marker = marker;
        }
    }

    private static void Stem3Options(
        Stem3DPlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "stem3: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf("stem3", args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "color":
                    plot.Color = OptionColor(value, line, col, "stem3");
                    break;
                case "linestyle":
                    plot.DashStyle = ParseDashWord(
                        StrOf("stem3: LineStyle", value, line, col), plot.DashStyle);
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf("stem3: LineWidth", value, line, col);
                    break;
                case "marker":
                    plot.Marker = ParseMarkerWord(
                        StrOf("stem3: Marker", value, line, col), plot.Marker);
                    break;
                case "markersize":
                    plot.MarkerSize = NumOf("stem3: MarkerSize", value, line, col);
                    break;
                case "markerfacecolor":
                    plot.MarkerFill = OptionColor(value, line, col, "stem3");
                    break;
                case "markeredgecolor":
                    plot.Color = OptionColor(value, line, col, "stem3");
                    break;
                case "basevalue":
                    plot.Baseline = NumOf("stem3: BaseValue", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("stem3: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"stem3 has no option '{name}'. It takes {string.Join(", ", Stem3OptionNames)}.");
            }
        }
    }

    // --- bar3 and bar3h ---------------------------------------------------------------------------

    /// <summary>The properties <c>bar3</c> and <c>bar3h</c> accept after their data.</summary>
    private static readonly string[] Bar3OptionNames =
    [
        "FaceColor", "EdgeColor", "FaceAlpha", "LineWidth", "BarWidth", "BaseValue",
        "Style", "Horizontal", "Colormap", "DisplayName",
    ];

    /// <summary>The layout words, which stand alone rather than in a name/value pair.</summary>
    private static readonly string[] Bar3StyleWords = ["detached", "grouped", "stacked"];

    /// <summary>
    /// <c>bar3(Z)</c>, <c>bar3(y, Z)</c>, either followed by a width, a layout word, a colour and
    /// name/value pairs. <c>bar3h</c> is the same chart with the bars laid along X, which is a
    /// property of the plot rather than a kind of its own — so the two verbs are one function.
    /// <para>
    /// The whole matrix becomes one plot and one handle. MATLAB answers with a surface per column;
    /// this is the same divergence <c>tetramesh</c> makes, and for the same reason — the boxes are
    /// painted back to front, and the sort has to see all of them.
    /// </para>
    /// </summary>
    private static JgsValue Bar3(
        string verb, bool horizontal, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            int dataEnd = FirstWord(rest);
            int optionStart = dataEnd;

            // A trailing scalar is the bar width — unless everything is a scalar, in which case the
            // call is bar3(y, z) drawing a single bar and there is no width to find.
            double? width = null;
            if (dataEnd > 1 && IsScalar(rest[dataEnd - 1]) && !IsScalar(rest[dataEnd - 2]))
            {
                width = rest[dataEnd - 1].AsNumber;
                dataEnd--;
            }

            if (dataEnd is < 1 or > 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} takes {verb}(z), {verb}(y, z), and either one followed by a bar width.");
            }

            double[,] heights = HeightGrid(verb, rest[dataEnd - 1], line, col);
            double[]? rows = dataEnd == 2 ? ToDoubles(verb, rest[0], line, col) : null;
            if (rows is not null && rows.Length != heights.GetLength(0))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: there are {rows.Length} row positions but z has "
                        + $"{heights.GetLength(0)} rows.");
            }

            (IReadOnlyList<string> words, optionStart) = PeelWords(rest, optionStart, Bar3OptionNames);
            Bar3DStyle? style = null;
            Color? face = null;
            foreach (string word in words)
            {
                if (Bar3StyleWords.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    style = ParseBar3Style(verb, word, line, col);
                }
                else if (IsColorWord(word))
                {
                    face = OptionColor(JgsValue.Str(word), line, col, verb);
                }
                else
                {
                    // A colour is read letter by letter, so a misspelled option name with an 'r' in
                    // it would come out red. Saying what the call cannot mean beats guessing.
                    throw new JgsRuntimeException(line, col,
                        $"{verb} has no option '{word}'. It takes {string.Join(", ", Bar3OptionNames)}, "
                            + $"and the layout words {string.Join(", ", Bar3StyleWords)}.");
                }
            }

            Bar3DPlot plot = JG.Bar3(heights, rows);
            plot.Horizontal = horizontal;
            plot.FaceColor = face;
            if (style is { } laid)
            {
                plot.Style = laid;
            }

            if (width is { } fraction)
            {
                plot.BarWidth = fraction;
            }

            Bar3Options(verb, plot, rest, optionStart, line, col);
            return Handle(plot);
        });
    }

    private static Bar3DStyle ParseBar3Style(string verb, string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "detached" => Bar3DStyle.Detached,
            "grouped" => Bar3DStyle.Grouped,
            "stacked" => Bar3DStyle.Stacked,
            _ => throw new JgsRuntimeException(line, col,
                $"{verb}: '{word}' is not a layout. It takes {string.Join(", ", Bar3StyleWords)}."),
        };

    private static void Bar3Options(
        string verb, Bar3DPlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf(verb, args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "facecolor":
                    plot.FaceColor = OptionColor(value, line, col, verb);
                    break;
                case "edgecolor":
                    plot.EdgeColor = OptionColor(value, line, col, verb);
                    break;
                case "facealpha":
                    plot.FaceAlpha = NumOf($"{verb}: FaceAlpha", value, line, col);
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf($"{verb}: LineWidth", value, line, col);
                    break;
                case "barwidth":
                    plot.BarWidth = NumOf($"{verb}: BarWidth", value, line, col);
                    break;
                case "basevalue":
                    plot.Baseline = NumOf($"{verb}: BaseValue", value, line, col);
                    break;
                case "style":
                    plot.Style = ParseBar3Style(verb, StrOf($"{verb}: Style", value, line, col), line, col);
                    break;
                case "horizontal":
                    plot.Horizontal = JgsGraphicsProperties.ToOnOff($"{verb}: Horizontal", value, line, col);
                    break;
                case "colormap":
                    plot.Colormap = OptionColormap(verb, value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf($"{verb}: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{verb} has no option '{name}'. It takes {string.Join(", ", Bar3OptionNames)}.");
            }
        }
    }

    // --- pie3 -------------------------------------------------------------------------------------

    /// <summary>The properties <c>pie3</c> accepts after its data.</summary>
    private static readonly string[] Pie3OptionNames =
    [
        "EdgeColor", "LineWidth", "FaceAlpha", "StartAngle", "Clockwise", "Height",
        "ShowLabels", "LabelRadius", "Colormap", "DisplayName",
    ];

    /// <summary>
    /// <c>pie3(X)</c>, <c>pie3(X, explode)</c>, <c>pie3(X, labels)</c>,
    /// <c>pie3(X, explode, labels)</c>, any of them on a named axes and followed by name/value pairs.
    /// The two optional positions tell themselves apart by type rather than by counting, exactly as
    /// the flat <c>pie</c> does.
    /// </summary>
    private static JgsValue Pie3(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "pie3 expects the values to divide: pie3(x).");
            }

            double[] values = ToDoubles("pie3", rest[0], line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "pie3 needs at least one value.");
            }

            foreach (double value in values)
            {
                if (value < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "pie3: a wedge cannot have a negative share, so every value must be zero or more.");
                }
            }

            double[]? explode = null;
            string[]? labels = null;
            int optionStart = 1;
            while (optionStart < rest.Count && rest[optionStart].Type != JgsType.String)
            {
                JgsValue next = rest[optionStart];
                if (IsTextList(next))
                {
                    labels = LabelWords("pie3", next, values.Length, line, col);
                }
                else if (explode is null && labels is null)
                {
                    explode = Exploded("pie3", ToDoubles("pie3", next, line, col), values.Length, line, col);
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        "pie3 takes pie3(x), pie3(x, explode), pie3(x, labels), or pie3(x, explode, labels).");
                }

                optionStart++;
            }

            Pie3DPlot plot = JG.Pie3(values);
            plot.Explode = explode;
            plot.Labels = labels;
            Pie3Options(plot, rest, optionStart, line, col);
            return Handle(plot);
        });
    }

    private static void Pie3Options(
        Pie3DPlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "pie3: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf("pie3", args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "edgecolor":
                    plot.EdgeColor = OptionColor(value, line, col, "pie3");
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf("pie3: LineWidth", value, line, col);
                    break;
                case "facealpha":
                    plot.FaceAlpha = NumOf("pie3: FaceAlpha", value, line, col);
                    break;
                case "startangle":
                    plot.StartAngle = NumOf("pie3: StartAngle", value, line, col);
                    break;
                case "clockwise":
                    plot.Clockwise = JgsGraphicsProperties.ToOnOff("pie3: Clockwise", value, line, col);
                    break;
                case "height":
                    plot.Height = NumOf("pie3: Height", value, line, col);
                    break;
                case "showlabels":
                    plot.ShowLabels = JgsGraphicsProperties.ToOnOff("pie3: ShowLabels", value, line, col);
                    break;
                case "labelradius":
                    plot.LabelRadius = NumOf("pie3: LabelRadius", value, line, col);
                    break;
                case "colormap":
                    plot.Colormap = OptionColormap("pie3", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("pie3: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"pie3 has no option '{name}'. It takes {string.Join(", ", Pie3OptionNames)}.");
            }
        }
    }

    // --- shared reading ---------------------------------------------------------------------------

    /// <summary>
    /// The lone words between the data and the <c>'Name', value</c> pairs — a layout, a colour, a
    /// line spec, <c>'filled'</c> — and where the pairs begin.
    /// <para>
    /// They are told apart by name rather than by counting what is left: a call may carry two lone
    /// words and an even number of arguments after them, which no parity rule can read. The first
    /// documented option name ends the run, which is exactly where a reader would say the pairs
    /// start.
    /// </para>
    /// </summary>
    private static (IReadOnlyList<string> Words, int OptionStart) PeelWords(
        IReadOnlyList<JgsValue> args, int start, string[] optionNames)
    {
        var words = new List<string>();
        int at = start;
        while (at < args.Count
            && args[at].Type == JgsType.String
            && !optionNames.Contains(args[at].AsString, StringComparer.OrdinalIgnoreCase))
        {
            words.Add(args[at].AsString);
            at++;
        }

        return (words, at);
    }

    /// <summary>
    /// Whether a lone word is a colour at all: a spec, a name spelled out, or a hex string. Without
    /// this check <c>OptionColor</c> would find the 'r' inside a misspelled option name and paint
    /// the chart red.
    /// </summary>
    private static bool IsColorWord(string word) =>
        IsLineSpecWord(word) || word.StartsWith('#');

    /// <summary>How far into the arguments the data runs: up to the first word.</summary>
    private static int FirstWord(IReadOnlyList<JgsValue> args)
    {
        int at = 0;
        while (at < args.Count && args[at].Type != JgsType.String)
        {
            at++;
        }

        return at;
    }

    /// <summary>
    /// A matrix of heights, from a matrix or from a vector. A vector is one column of bars, which is
    /// MATLAB's reading — <c>bar3([1 2 3])</c> is three bars in a row, not one row of three.
    /// </summary>
    private static double[,] HeightGrid(string verb, JgsValue value, int line, int col)
    {
        if (JgsMatrix.RowCount(value) > 1 && JgsMatrix.ColCount(value) > 1)
        {
            return Matrix(verb, [value], 0, line, col);
        }

        double[] flat = ToDoubles(verb, value, line, col);
        var column = new double[flat.Length, 1];
        for (int r = 0; r < flat.Length; r++)
        {
            column[r, 0] = flat[r];
        }

        return column;
    }

    /// <summary>
    /// The grid a matrix of heights is indexed by, flattened into three parallel arrays: the column
    /// number along X, the row number along Y, and the value itself.
    /// </summary>
    private static (double[] X, double[] Y, double[] Z) GridOf(
        string verb, JgsValue value, int line, int col)
    {
        if (JgsMatrix.RowCount(value) <= 1 || JgsMatrix.ColCount(value) <= 1)
        {
            // A vector is one row of the grid, so its stems stand along X at the first row's depth.
            double[] flat = ToDoubles(verb, value, line, col);
            var depth = new double[flat.Length];
            Array.Fill(depth, 1);
            return (Counting(flat.Length), depth, flat);
        }

        double[,] grid = Matrix(verb, [value], 0, line, col);
        int rows = grid.GetLength(0);
        int columns = grid.GetLength(1);
        var x = new double[rows * columns];
        var y = new double[rows * columns];
        var z = new double[rows * columns];
        int at = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                x[at] = c + 1;
                y[at] = r + 1;
                z[at] = grid[r, c];
                at++;
            }
        }

        return (x, y, z);
    }
}
