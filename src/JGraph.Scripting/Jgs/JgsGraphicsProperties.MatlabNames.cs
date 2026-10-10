using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Which of an object's names a MATLAB-dialect script sees (open item 82). Reflection reaches every
/// browsable model property, and some of them are this build's own: the plot browser's <c>Name</c>,
/// <c>ZOrder</c>, <c>Opacity</c>; and some hand-written spellings were added to every object of a
/// model when R2025b gives them to one kind (a Cartesian axes answered the polar settings). R2025b
/// lists none of them. So in the MATLAB dialect a name the object's R2025b classes define as hidden
/// (<c>UIContextMenu</c>, a figure's <c>HitTest</c>), or do not define at all, answers when named and is
/// left out of <c>get(h)</c> and <c>set(h)</c>; the plot browser's <c>Name</c> on anything but a figure is
/// refused outright (<c>isprop</c> 0, <c>MATLAB:hg:InvalidProperty</c>). JGS scripts list every name,
/// and so does an object whose classes were not recorded.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>How a MATLAB-dialect script sees a property: listed, answered only when named, or not at all.</summary>
    private enum MatlabSight
    {
        Shown,
        Hidden,
        Absent,
    }

    private const string Primitive = "matlab.graphics.primitive.";
    private const string Chart = "matlab.graphics.chart.primitive.";
    private const string InPolarAxes = "@polaraxes";

    /// <summary>
    /// The R2025b class an object is refused under when a name means nothing to it, in full or by its
    /// last word, or null for an object whose classes were not recorded.
    /// </summary>
    private static string? MatlabClassWord(GraphObject target, bool full) =>
        JgsRunningDialect.ThreadIsMatlab && R2025bClassesOf(target) is [var first, ..]
            ? full ? first : first[(first.LastIndexOf('.') + 1)..]
            : null;

    /// <summary>The two axes names R2025b's <c>get</c> and <c>set</c> refuse as not public rather than unknown.</summary>
    private static bool IsProhibitedBubbleName(string name) =>
        name.Equals("BubbleSizeLimits", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BubbleSizeRange", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// R2025b's refusal of a bubble size name through <c>get</c> or <c>set</c> (measured): its internal
    /// <c>_IS</c> twin is what the sentence names, with the class in doubled quotes. Null for anything else.
    /// </summary>
    private static JgsRuntimeException? ProhibitedBubbleName(GraphObject target, string name, bool reading, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || target is not AxesModel { IsUiAxes: false, IsPolar: false } || !IsProhibitedBubbleName(name))
        {
            return null;
        }

        string inner = (name.Equals("BubbleSizeLimits", StringComparison.OrdinalIgnoreCase) ? "BubbleSizeLimits" : "BubbleSizeRange") + "_IS";
        return reading
            ? new JgsRuntimeException(line, col, "MATLAB:class:GetProhibited", $"No public property '{inner}' for class ''Axes''.")
            : new JgsRuntimeException(line, col, "MATLAB:class:SetProhibited", $"Setting the '{inner}' property of class ''Axes'' is not supported.");
    }

    /// <summary>Whether a MATLAB-dialect script must not reach this property on this object.</summary>
    private static bool NotMatlabs(GraphObject target, GraphicsProperty property) =>
        SightOf(target, property) == MatlabSight.Absent;

    /// <summary>Whether <c>get(h)</c> and <c>set(h)</c> leave this property out for this script.</summary>
    private static bool UnlistedForMatlab(GraphObject target, GraphicsProperty property) =>
        SightOf(target, property) != MatlabSight.Shown;

    private static MatlabSight SightOf(GraphObject target, GraphicsProperty property)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || R2025bClassesOf(target) is not { } classes)
        {
            return MatlabSight.Shown;
        }

        // R2025b keeps an axes' bubble size limits behind bubblelim and refuses the name (open item 88).
        if (target is AxesModel && IsProhibitedBubbleName(property.Name))
        {
            return MatlabSight.Absent;
        }

        MatlabSight sight = MatlabSight.Shown;
        bool recorded = false;
        foreach (string name in classes)
        {
            if (!R2025bNames.ByClass.TryGetValue(name, out Dictionary<string, bool>? names))
            {
                continue;
            }

            recorded = true;
            if (names.TryGetValue(property.Name, out bool hidden))
            {
                if (!hidden)
                {
                    return MatlabSight.Shown;
                }

                sight = MatlabSight.Hidden;
            }
            else if (sight != MatlabSight.Hidden)
            {
                sight = MatlabSight.Absent;
            }
        }

        // A name none of the classes defines is this build's own. It answers when named, as R2025b's
        // hidden names do, and is left out of the lists, so a MATLAB script keeps every JGraph extra
        // (ThetaDirection, Opacity, a plain axes' BackgroundColor) — the user's decision, 2026-10-09.
        // The plot browser's Name is the one refused: it is no extra but the model's label, and
        // isprop(h, 'Name') is how GUI code tells a figure from anything else.
        if (recorded && sight == MatlabSight.Absent)
        {
            return property.Name == "Name" && target is not FigureModel ? MatlabSight.Absent : MatlabSight.Hidden;
        }

        return recorded ? sight : MatlabSight.Shown;
    }

    /// <summary>
    /// The recorded rows as name-to-hidden maps, built on first use: the rows sit in another part of
    /// this class, and the order static fields of a partial class start in is not the order of its files.
    /// </summary>
    private static class R2025bNames
    {
        public static readonly Dictionary<string, Dictionary<string, bool>> ByClass = R2025bNameRows.ToDictionary(
            static row => row.Class,
            static row => row.Names.Split(' ').ToDictionary(
                static word => word.TrimStart('~'),
                static word => word.StartsWith('~'),
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether <c>set(h)</c> and <c>set(h, name)</c> answer R2025b's words for this object (open item
    /// 41): a MATLAB-dialect script, and an object whose classes were recorded.
    /// </summary>
    internal static bool AnswersMatlabOptions(GraphObject target) =>
        JgsRunningDialect.ThreadIsMatlab && R2025bClassesOf(target) is not null;

    /// <summary>
    /// The words R2025b's <c>set(h)</c> gives a property of this object, from the first of its
    /// classes that records any, or null.
    /// </summary>
    private static IReadOnlyList<string>? MatlabWordsOf(GraphObject target, string property)
    {
        foreach (string name in R2025bClassesOf(target) ?? [])
        {
            if (R2025bWords.ByClass.TryGetValue(name, out Dictionary<string, string[]>? words)
                && words.TryGetValue(property, out string[]? found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The recorded words as a map per class, built on first use (see <see cref="R2025bNames"/>).</summary>
    private static class R2025bWords
    {
        public static readonly Dictionary<string, Dictionary<string, string[]>> ByClass = R2025bSetWordRows
            .GroupBy(static row => row.Class)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToDictionary(
                    static row => row.Property,
                    static row => row.Words.Length == 0 ? [] : row.Words.Split('\t'),
                    StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A property's words as <c>set</c> answers them: its own when it has some, R2025b's recorded ones,
    /// or none. A recorded property without words (a colour) is R2025b's 0-by-1 cell; one never
    /// recorded is the 0-by-0 cell every other property without words is.
    /// </summary>
    private static JgsValue WordsFor(GraphObject target, GraphicsProperty property)
    {
        if (property.Words is not null || MatlabWordsOf(target, property.Name) is not { } recorded)
        {
            return WordsCell(property.Words);
        }

        if (recorded.Count > 0)
        {
            return WordsCell(recorded);
        }

        JgsValue column = JgsValue.Cell([]);
        column.Reshape(0, 1);
        return column;
    }

    /// <summary>
    /// The R2025b classes an object stands for, among those <c>graphics_class_names.m</c> recorded, or
    /// null. The object is read as <c>TypeNameOf</c> reads it. A model that makes more than one class
    /// answers with all of them, and a name any of them defines is the object's: a line is also what
    /// <c>fplot</c> makes, and a line or scatter in polar axes gains the polar names. What <c>rectangle</c>
    /// and <c>animatedline</c> make is a patch and a line marked as theirs, and answers as their class
    /// alone (open item 88). A shape whose class is ambiguous answers null.
    /// </summary>
    private static string[]? R2025bClassesOf(GraphObject target) => target switch
    {
        FigureModel => ["matlab.ui.Figure"],
        AxesModel { IsUiAxes: true } => ["matlab.ui.control.UIAxes"],
        AxesModel { IsPolar: true } => ["matlab.graphics.axis.PolarAxes"],
        AxesModel => ["matlab.graphics.axis.Axes"],
        AxisModel => ["matlab.graphics.axis.decorator.NumericRuler"],
        LegendModel => ["matlab.graphics.illustration.Legend"],
        ColorbarModel => ["matlab.graphics.illustration.ColorBar"],
        LightModel => [Primitive + "Light"],
        JgsGraphicsGroup { Transforms: true } => [Primitive + "Transform"],
        JgsGraphicsGroup => [Primitive + "Group"],
        JgsTextLabel => [Primitive + "Text"],
        LinePlot { Steps: not StepMode.None } => [Chart + "Stair"],
        PlotObject animated when JgsBuiltins.AnimatedCapOf(animated) is not null => ["matlab.graphics.animation.AnimatedLine"],
        LinePlot or Line3DPlot => InPolar(target)
            ? [Chart + "Line", Chart + "Line" + InPolarAxes]
            : [Chart + "Line", "matlab.graphics.function.FunctionLine",
                "matlab.graphics.function.ImplicitFunctionLine", "matlab.graphics.function.ParameterizedFunctionLine"],
        ScatterPlot { BubbleSizing: true } => [Chart + "BubbleChart"],
        ScatterPlot or Scatter3DPlot => InPolar(target)
            ? [Chart + "Scatter", Chart + "Scatter" + InPolarAxes]
            : [Chart + "Scatter"],
        BarPlot => [Chart + "Bar"],
        AreaPlot => [Chart + "Area"],
        StemPlot or Stem3DPlot => [Chart + "Stem"],
        HistogramPlot or PolarHistogramPlot => [Chart + "Histogram"],
        Histogram2Plot => [Chart + "Histogram2"],
        ErrorBarPlot => [Chart + "ErrorBar"],
        SurfacePlot => [Primitive + "Surface"],
        ContourPlot => [Chart + "Contour"],
        PatchPlot patch when JgsBuiltins.RectangleShapeOf(patch) is not null => [Primitive + "Rectangle"],
        PatchPlot => [Primitive + "Patch"],
        QuiverPlot => [Chart + "Quiver"],
        ImagePlot or RgbImagePlot => [Primitive + "Image"],
        HeatmapPlot => ["matlab.graphics.chart.HeatmapChart"],
        BoxChartPlot => [Chart + "BoxChart"],
        ConstantLinePlot => ["matlab.graphics.chart.decoration.ConstantLine"],
        TextAnnotation { Box: not null } => ["matlab.graphics.shape.TextBox"],
        TextAnnotation => [Primitive + "Text"],
        ArrowAnnotation { Text.Length: > 0 } => ["matlab.graphics.shape.TextArrow"],
        ArrowAnnotation { ShowTailHead: true } => ["matlab.graphics.shape.DoubleEndArrow"],
        ArrowAnnotation { ShowHead: false } => ["matlab.graphics.shape.Line"],
        ArrowAnnotation => ["matlab.graphics.shape.Arrow"],
        EllipseAnnotation => ["matlab.graphics.shape.Ellipse"],
        _ => null,
    };

    /// <summary>Whether the object is drawn in polar axes, however many groups lie between.</summary>
    private static bool InPolar(GraphObject target)
    {
        for (GraphObject? at = target.Parent; at is not null; at = at.Parent)
        {
            if (at is AxesModel axes)
            {
                return axes.IsPolar;
            }
        }

        return false;
    }
}
