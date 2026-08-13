using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M57 wave D: <c>binscatter</c>, the chart for the sample size a scatter cannot draw. Past a few
/// thousand markers every new one lands on one already there, and the picture stops saying how many
/// readings are underneath; counting them into bins and colouring the bins says it again.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterDensityBuiltins(JgsEnvironment env) =>
        env.Declare("binscatter", JgsValue.Function(
            new BuiltinFunction("binscatter", (args, line, col) => BinScatter(args, line, col))
            { BindsAnsAsStatement = false }));

    /// <summary>The properties <c>binscatter</c> accepts after its data.</summary>
    private static readonly string[] BinScatterOptionNames =
    [
        "NumBins", "XLimits", "YLimits", "ShowEmptyBins", "UseParallel", "Colormap", "DisplayName",
    ];

    /// <summary>
    /// <c>binscatter(x, y)</c>, <c>binscatter(x, y, N)</c> and <c>binscatter(x, y, [nx ny])</c>, any
    /// of them on a named axes and followed by name/value pairs.
    /// <para>
    /// The bin count is the one thing that can be said either positionally or by name, which is
    /// MATLAB's grammar; saying it both ways is not an error, and the later word wins, since that is
    /// what the reader of the call would expect.
    /// </para>
    /// </summary>
    private static JgsValue BinScatter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count < 2)
            {
                throw new JgsRuntimeException(line, col,
                    "binscatter expects the readings to count: binscatter(x, y).");
            }

            double[] xs = ToDoubles("binscatter", rest[0], line, col);
            double[] ys = ToDoubles("binscatter", rest[1], line, col);
            if (xs.Length != ys.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"binscatter: x has {xs.Length} readings but y has {ys.Length}.");
            }

            if (xs.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "binscatter: there are no readings to count.");
            }

            // The bin count is the only thing that can stand between the data and the pairs, so a
            // third argument that is not a name is it — and that is the whole of the positional part.
            int optionStart = 2;
            (int Across, int Up)? bins = null;
            if (rest.Count > 2 && rest[2].Type != JgsType.String)
            {
                bins = BinCounts("binscatter: the bin count", rest[2], line, col);
                optionStart = 3;
            }

            BinScatterPlot plot = JG.BinScatter(xs, ys);
            if (bins is { } counted)
            {
                plot.NumBinsX = counted.Across;
                plot.NumBinsY = counted.Up;
            }

            BinScatterOptions(plot, rest, optionStart, line, col);
            return Handle(plot);
        });
    }

    private static void BinScatterOptions(
        BinScatterPlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "binscatter: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf("binscatter", args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "numbins":
                    (int across, int up) = BinCounts("binscatter: NumBins", value, line, col);
                    plot.NumBinsX = across;
                    plot.NumBinsY = up;
                    break;
                case "xlimits":
                    plot.XLimits = SpanOption("binscatter: XLimits", value, line, col);
                    break;
                case "ylimits":
                    plot.YLimits = SpanOption("binscatter: YLimits", value, line, col);
                    break;
                case "showemptybins":
                    plot.ShowEmptyBins =
                        JgsGraphicsProperties.ToOnOff("binscatter: ShowEmptyBins", value, line, col);
                    break;

                // There is no parallel pool to spread the counting over, so the option is read and
                // then ignored — a script written against one still runs, and gets the same answer.
                case "useparallel":
                    JgsGraphicsProperties.ToOnOff("binscatter: UseParallel", value, line, col);
                    break;
                case "colormap":
                    plot.Colormap = OptionColormap("binscatter", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("binscatter: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"binscatter has no option '{name}'. "
                        + $"It takes {string.Join(", ", BinScatterOptionNames)}.");
            }
        }
    }

    /// <summary>
    /// A bin count said as one number for both directions or as <c>[across up]</c> for each, which is
    /// how MATLAB says it in both the positional slot and the <c>NumBins</c> option.
    /// </summary>
    internal static (int Across, int Up) BinCounts(string what, JgsValue value, int line, int col)
    {
        double[] counts = ToDoubles(what, value, line, col);
        if (counts.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is one number for both directions or two, such as [40 20].");
        }

        int across = OneCount(what, counts[0], line, col);
        return (across, counts.Length == 2 ? OneCount(what, counts[1], line, col) : across);
    }

    private static int OneCount(string what, double count, int line, int col)
    {
        if (!double.IsFinite(count) || count < 1 || count != System.Math.Floor(count))
        {
            throw new JgsRuntimeException(line, col, $"{what} is a whole number of bins, at least one.");
        }

        if (count > BinScatterPlot.MaxBinsPerSide)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is at most {BinScatterPlot.MaxBinsPerSide} in each direction.");
        }

        return (int)count;
    }

    /// <summary>The two increasing numbers a limits option is, as the span the bins fill.</summary>
    internal static DataRange SpanOption(string what, JgsValue value, int line, int col)
    {
        double[] ends = ToDoubles(what, value, line, col);
        if (ends.Length != 2 || !(ends[0] < ends[1]) || !double.IsFinite(ends[0]) || !double.IsFinite(ends[1]))
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is two increasing numbers, such as [0 10].");
        }

        return new DataRange(ends[0], ends[1]);
    }
}
