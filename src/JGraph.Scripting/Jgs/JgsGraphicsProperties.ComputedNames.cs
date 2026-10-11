using System.Runtime.CompilerServices;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Maths.Ticks;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The R2025b names item 88 left out because their values are not the class's constants (open item
/// 95, probes <c>probe_95</c>, <c>probe_95b</c>, <c>probe_95c</c>): objects (a figure's <c>Theme</c>,
/// an axes' <c>InteractionOptions</c>, a group's <c>Annotation</c>), a table (<c>SourceTable</c>),
/// and values worked out from the data or the layout (a ruler's <c>Exponent</c> and
/// <c>MinorTickValues</c>, a title's <c>Position</c> and <c>Extent</c>, a quiver's
/// <c>ScaleFactor</c>). An object is the classed struct this build uses for objects; a value written
/// is kept and read back, and turns its mode manual.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private const string GraphicsThemeClass = "matlab.graphics.theme.GraphicsTheme";

    /// <summary>What a script wrote to the names below that are not the model's own, by object and name.</summary>
    private static readonly ConditionalWeakTable<GraphObject, Dictionary<string, JgsValue>> Written = new();

    private static JgsValue? WrittenValue(GraphObject target, string name) =>
        Written.TryGetValue(target, out var held) && held.TryGetValue(name, out JgsValue? value) ? value : null;

    private static void Write(GraphObject target, string name, JgsValue? value)
    {
        Dictionary<string, JgsValue> held = Written.GetOrCreateValue(target);
        if (value is null)
        {
            held.Remove(name);
        }
        else
        {
            JgsLifetime.Pin(value);
            held[name] = JgsValue.Share(value);
        }
    }

    private static void AddComputedNames(Type type, IDictionary<string, GraphicsProperty> table)
    {
        if (type == typeof(JgsGraphicsGroup))
        {
            Put(table, "Annotation", entry => JgsHandleRegistry.For(((JgsGraphicsGroup)entry.Target).Annotation));
        }

        if (typeof(QuiverPlot).IsAssignableFrom(type))
        {
            // MATLAB's auto-scale applied, or 1 with it off (probe_95: six to six digits).
            Put(table, "ScaleFactor", entry => JgsValue.Number(((QuiverPlot)entry.Target).EffectiveScale));
        }

        if ((typeof(LinePlot).IsAssignableFrom(type) || typeof(StemPlot).IsAssignableFrom(type)
            || typeof(BoxChartPlot).IsAssignableFrom(type)) && !table.ContainsKey("SourceTable"))
        {
            AddSourceTable(table);
        }

        if (type == typeof(FigureModel))
        {
            AddTheme(table);
        }

        if (type == typeof(AxesModel))
        {
            AddInteractionOptions(table);
        }

        if (type == typeof(AxisModel))
        {
            AddExponent(table);
            AddMinorTickValues(table);
        }

        if (type == typeof(JgsTextLabel))
        {
            AddLabelPlace(table);
        }
    }

    // --- SourceTable -----------------------------------------------------------------------------

    /// <summary>The table a chart was made from: R2025b's 0-by-0 one for a chart made from arrays.</summary>
    private static void AddSourceTable(IDictionary<string, GraphicsProperty> table) =>
        Put(table, "SourceTable",
            entry => WrittenValue(entry.Target, "SourceTable") ?? JgsValue.Table(new Table([])),
            (entry, value, line, col) => Write(entry.Target, "SourceTable", value.Type == JgsType.Table
                ? value
                : throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidTable", "SourceTable must be a table.")));

    // --- Theme -----------------------------------------------------------------------------------

    /// <summary>
    /// A figure's <c>Theme</c>: R2025b's <c>GraphicsTheme</c>, its <c>Name</c> and <c>BaseColorStyle</c>.
    /// Writing <c>'light'</c>, <c>'dark'</c> or a theme sets it and turns <c>ThemeMode</c> manual;
    /// it is kept and read back, and this build draws every figure in the application's theme.
    /// </summary>
    private static void AddTheme(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Theme",
            entry => ThemeValue(WrittenValue(entry.Target, "Theme") is { } style ? JgsBuiltins.TextOf(style) : "light"),
            (entry, value, line, col) =>
            {
                string style = value.Type == JgsType.Struct && value.AsStruct.TryGetValue("BaseColorStyle", out JgsValue? held)
                    ? JgsBuiltins.TextOf(held)
                    : JgsBuiltins.IsTextScalar(value) ? JgsBuiltins.TextOf(value).ToLowerInvariant() : "";
                if (style is not ("light" or "dark"))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:graphics:theme:InvalidTheme",
                        "Theme must be 'light', 'dark', or a GraphicsTheme object.");
                }

                Write(entry.Target, "Theme", JgsValue.Str(style));
                Write(entry.Target, "ThemeMode", JgsValue.Str("manual"));
            });
        Put(table, "ThemeMode",
            entry => WrittenValue(entry.Target, "ThemeMode") ?? JgsValue.Str("auto"),
            (entry, value, line, col) =>
            {
                string mode = JgsBuiltins.StrOf("ThemeMode", value, line, col).ToLowerInvariant();
                if (mode is not ("auto" or "manual"))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:datatypes:onoffboolean:IncorrectValue", "ThemeMode must be 'auto' or 'manual'.");
                }

                Write(entry.Target, "ThemeMode", JgsValue.Str(mode));
                if (mode == "auto")
                {
                    Write(entry.Target, "Theme", null);
                }
            });
    }

    private static JgsValue ThemeValue(string style)
    {
        JgsValue theme = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(style == "dark" ? "Dark Theme" : "Light Theme"),
            ["BaseColorStyle"] = JgsValue.Str(style),
        });
        theme.SetClassName(GraphicsThemeClass);
        return theme;
    }

    // --- InteractionOptions ----------------------------------------------------------------------

    /// <summary>
    /// An axes' <c>InteractionOptions</c>, R2025b's fields and their values (probe_95): a Cartesian
    /// axes' seventeen, a polar axes' two. A struct written back (<c>ax.InteractionOptions.X = v</c>)
    /// is kept, each field checked against the class's; the window's interactions do not read them.
    /// </summary>
    private static void AddInteractionOptions(IDictionary<string, GraphicsProperty> table) =>
        Put(table, "InteractionOptions",
            entry => WrittenValue(entry.Target, "InteractionOptions") ?? InteractionOptionsFor((AxesModel)entry.Target),
            (entry, value, line, col) =>
            {
                JgsValue fresh = InteractionOptionsFor((AxesModel)entry.Target);
                if (value.Type != JgsType.Struct)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:shaped_arrays:ObjectType",
                        $"Value must be a {fresh.ClassName} object.");
                }

                var fields = new Dictionary<string, JgsValue>(fresh.AsStruct, StringComparer.Ordinal);
                foreach ((string name, JgsValue given) in value.AsStruct)
                {
                    if (!fields.ContainsKey(name))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
                            $"Unrecognized property '{name}' for class '{fresh.ClassName}'.");
                    }

                    fields[name] = name == "LimitsDimensions" && JgsBuiltins.IsTextScalar(given) && !given.IsStringArray
                        ? JgsValue.StringScalar(JgsBuiltins.TextOf(given))
                        : JgsValue.Share(given);
                }

                JgsValue kept = JgsValue.Struct(fields);
                kept.SetClassName(fresh.ClassName!);
                Write(entry.Target, "InteractionOptions", kept);
            });

    private static JgsValue InteractionOptionsFor(AxesModel axes)
    {
        const string Root = "matlab.graphics.interaction.interactionoptions.";
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        string cls;
        if (axes.IsPolar)
        {
            cls = Root + "PolarAxesInteractionOptions";
        }
        else
        {
            cls = Root + "CartesianAxesInteractionOptions";
            fields["LimitsDimensions"] = JgsValue.StringScalar("xyz"); // a string, as R2025b's is
            fields["OuterXLimits"] = Row(0, 0);
            fields["OuterYLimits"] = Row(0, 0);
            fields["OuterZLimits"] = Row(0, 0);
            fields["PanSupported"] = JgsValue.Str("on");
            fields["ZoomSupported"] = JgsValue.Str("on");
            fields["RotateSupported"] = JgsValue.Str("on");
            fields["BrushSupported"] = JgsValue.Str("on");
            fields["RestoredXLimits"] = Row(0, 0);
            fields["RestoredYLimits"] = Row(0, 0);
            fields["RestoredZLimits"] = Row(0, 0);
            fields["RestoredView"] = Row(0, 0);
            fields["PanLimitsBounded"] = JgsValue.Str("off");
            fields["ZoomLimitsBounded"] = JgsValue.Str("on");
            fields["RotateIndicator"] = JgsValue.Str("none");
        }

        fields["DatatipsSupported"] = JgsValue.Str("on");
        fields["DatatipsPlacementMethod"] = JgsValue.Str("vertex");
        JgsValue options = JgsValue.Struct(fields);
        options.SetClassName(cls);
        return options;
    }

    // --- A ruler's exponent and minor ticks --------------------------------------------------------

    /// <summary>
    /// A ruler's <c>Exponent</c>, an int32: what a script wrote, or the power of ten R2025b factors out
    /// of the tick labels (<see cref="ExponentTickGenerator.Auto"/>, a fitted rule); none on a log
    /// ruler or one whose labels were given. The labels and the ×10ⁿ mark follow it (open item 97).
    /// </summary>
    private static void AddExponent(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Exponent",
            entry => JgsNumericClasses.Stamp(JgsValue.Number(((AxisModel)entry.Target).Exponent ?? AutoExponent(entry)), JgsNumericClass.Int32),
            (entry, value, line, col) =>
            {
                if (value.Type is not (JgsType.Number or JgsType.Bool or JgsType.Array) || value.IsCharMatrix || value.IsStringArray)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:class:RequireNumeric",
                        "Error setting property 'Exponent' of class 'NumericRuler':\nValue must be numeric or logical.");
                }

                ((AxisModel)entry.Target).Exponent = (int)System.Math.Round(JgsBuiltins.NumOf("Exponent", value, line, col));
            });
        Put(table, "ExponentMode",
            entry => JgsValue.Str(((AxisModel)entry.Target).Exponent is null ? "auto" : "manual"),
            (entry, value, line, col) =>
            {
                string mode = JgsBuiltins.StrOf("ExponentMode", value, line, col).ToLowerInvariant();
                if (mode == "auto")
                {
                    ((AxisModel)entry.Target).Exponent = null;
                }
                else if (mode == "manual")
                {
                    ((AxisModel)entry.Target).Exponent = AutoExponent(entry);
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidEnumValue", "ExponentMode must be 'auto' or 'manual'.");
                }
            });
    }

    private static int AutoExponent(JgsHandleEntry entry)
    {
        var ruler = (AxisModel)entry.Target;
        return ruler.Scale != AxisScaleType.Linear || ruler.TickLabelOverrides is not null
            ? 0
            : ExponentTickGenerator.Auto(RulerTicks(entry));
    }

    private static double[] RulerTicks(JgsHandleEntry entry) =>
        JgsBuiltins.ToDoubles("TickValues", Get(entry, "TickValues", 0, 0), 0, 0);

    /// <summary>
    /// A ruler's <c>MinorTickValues</c>, R2025b's rule (probes probe_95b, probe_95c): each step
    /// between its ticks in five, or four when the step's leading digit is 2, inside the limits and
    /// leaving out the ticks; on a log ruler whose ticks are decades, 1 to 9 times each power of ten
    /// inside the limits. Written values are kept and turn <c>MinorTickValuesMode</c> manual.
    /// </summary>
    private static void AddMinorTickValues(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "MinorTickValues",
            entry => WrittenValue(entry.Target, "MinorTickValues") ?? Row([.. AutoMinorTicks(entry)]),
            (entry, value, line, col) =>
            {
                double[] given = JgsBuiltins.ToDoubles("MinorTickValues", value, line, col);
                Write(entry.Target, "MinorTickValues", Row([.. given.Order()]));
            });
        Put(table, "MinorTickValuesMode",
            entry => JgsValue.Str(WrittenValue(entry.Target, "MinorTickValues") is null ? "auto" : "manual"),
            (entry, value, line, col) =>
            {
                string mode = JgsBuiltins.StrOf("MinorTickValuesMode", value, line, col).ToLowerInvariant();
                if (mode == "auto")
                {
                    Write(entry.Target, "MinorTickValues", null);
                }
                else if (mode == "manual")
                {
                    Write(entry.Target, "MinorTickValues", Row([.. AutoMinorTicks(entry)]));
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidEnumValue", "MinorTickValuesMode must be 'auto' or 'manual'.");
                }
            });
    }

    private static List<double> AutoMinorTicks(JgsHandleEntry entry)
    {
        var minors = new List<double>();
        double[] ticks = [.. RulerTicks(entry).Where(double.IsFinite).Order()];
        double[] limits = JgsBuiltins.ToDoubles("Limits", Get(entry, "Limits", 0, 0), 0, 0);
        if (limits.Length < 2 || !(limits[1] > limits[0]))
        {
            return minors;
        }

        (double low, double high) = (limits[0], limits[1]);
        double room = (high - low) * 1e-9;
        bool decades = ((AxisModel)entry.Target).Scale == AxisScaleType.Logarithmic && ticks.Length > 0
            && ticks.All(static t => t > 0 && System.Math.Abs(System.Math.Log10(t) - System.Math.Round(System.Math.Log10(t))) < 1e-9);
        if (decades)
        {
            for (int power = (int)System.Math.Floor(System.Math.Log10(low)); power <= (int)System.Math.Ceiling(System.Math.Log10(high)); power++)
            {
                for (int k = 1; k <= 9; k++)
                {
                    double value = Tidy(k * System.Math.Pow(10, power));
                    if (value >= low * (1 - 1e-12) && value <= high * (1 + 1e-12))
                    {
                        minors.Add(value);
                    }
                }
            }

            return minors;
        }

        if (ticks.Length < 2)
        {
            return minors;
        }

        double step = ticks[1] - ticks[0];
        double leading = step / System.Math.Pow(10, System.Math.Floor(System.Math.Log10(step)));
        int parts = System.Math.Abs(leading - 2) < 1e-9 ? 4 : 5;
        double minor = step / parts;
        long first = (long)System.Math.Ceiling((low - ticks[0]) / minor - 1e-9);
        long last = (long)System.Math.Floor((high - ticks[0]) / minor + 1e-9);
        for (long i = first; i <= last; i++)
        {
            if (i % parts == 0)
            {
                continue;
            }

            double value = ticks[0] + (i * minor); // unrounded, as R2025b's are
            if (value >= low - room && value <= high + room)
            {
                minors.Add(value);
            }
        }

        return minors;
    }

    /// <summary>A computed tick rounded to fifteen significant digits, so 0.1 * 3 reads 0.3.</summary>
    private static double Tidy(double value) =>
        value == 0 ? 0 : System.Math.Round(value, System.Math.Clamp(14 - (int)System.Math.Floor(System.Math.Log10(System.Math.Abs(value))), 0, 15));

    // --- A title's and a label's place ------------------------------------------------------------

    /// <summary>
    /// A title's or a ruler label's <c>Position</c> and <c>Extent</c>, worked out from this build's
    /// layout as R2025b places them (probe_95b): a title a quarter of its font size in pixels above the
    /// plot box (below it when the y direction is reversed), centred; an x label below the tick labels
    /// and a y label left of them. In data units, or normalized ones once <c>Units</c> says so. A
    /// position written is kept, read back and turns <c>PositionMode</c> manual; it does not move the
    /// drawn text.
    /// </summary>
    private static void AddLabelPlace(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Position",
            entry => WrittenValue(entry.Target, "Position") ?? LabelPlace((JgsTextLabel)entry.Target, entry).Position,
            (entry, value, line, col) =>
            {
                double[] given = JgsBuiltins.ToDoubles("Position", value, line, col);
                if (given.Length is not (2 or 3))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:shaped_arrays:VectorShapeException",
                        "Value must be a vector with 2 or 3 elements.");
                }

                Write(entry.Target, "Position", Row(given[0], given[1], given.Length == 3 ? given[2] : 0));
            });
        Put(table, "PositionMode",
            entry => JgsValue.Str(WrittenValue(entry.Target, "Position") is null ? "auto" : "manual"),
            (entry, value, line, col) =>
            {
                string mode = JgsBuiltins.StrOf("PositionMode", value, line, col).ToLowerInvariant();
                if (mode == "auto")
                {
                    Write(entry.Target, "Position", null);
                }
                else if (mode != "manual")
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidEnumValue", "PositionMode must be 'auto' or 'manual'.");
                }
            });
        Put(table, "Extent", entry => LabelPlace((JgsTextLabel)entry.Target, entry).Extent);
    }

    private static (JgsValue Position, JgsValue Extent) LabelPlace(JgsTextLabel label, JgsHandleEntry entry)
    {
        AxesModel? axes = label.Owner switch
        {
            AxesModel owner => owner,
            AxisModel ruler => ruler.Parent as AxesModel,
            _ => null,
        };
        if (axes is null)
        {
            return (Row(0, 0, 0), Row(0, 0, 0, 0));
        }

        TextStyle style = label.Style;
        Size2D box = PlotBoxPixels(axes);
        double fontPixels = style.FontSize * 96.0 / 72;
        (double textWidth, double textHeight) = label.Text.Length == 0
            ? (0.0, 0.0)
            : UiFonts.Measure(label.Text, style.FontFamily, fontPixels, style.Bold, style.Italic);
        bool normalized = TryFind(label, "Units", out GraphicsProperty units)
            && JgsBuiltins.TextOf(units.Read(entry)).Equals("normalized", StringComparison.OrdinalIgnoreCase);

        // Where it stands as a fraction of the plot box, x right and y up, and how far out in pixels.
        (double fx, double fy, double outX, double outY) = label.Owner switch
        {
            AxesModel => (0.5, 1.0, 0.0, 0.25 * style.FontSize),
            AxisModel { Orientation: AxisOrientation.Vertical } => (0.0, 0.5, -1.04 * fontPixels, 0.0),
            _ => (0.5, 0.0, 0.0, -0.86 * fontPixels),
        };
        fx += box.Width > 0 ? outX / box.Width : 0;
        fy += box.Height > 0 ? outY / box.Height : 0;
        double w = box.Width > 0 ? textWidth / box.Width : 0;
        double h = box.Height > 0 ? textHeight / box.Height : 0;
        if (normalized)
        {
            return (Row(fx, fy, 0), Row(fx - (w / 2), fy, w, h));
        }

        double x = DataAt(axes, "XLim", axes.PrimaryXAxis, fx);
        double y = DataAt(axes, "YLim", axes.ActiveYAxis, fy);
        double left = DataAt(axes, "XLim", axes.PrimaryXAxis, fx - (w / 2));
        double right = DataAt(axes, "XLim", axes.PrimaryXAxis, fx + (w / 2));
        double top = DataAt(axes, "YLim", axes.ActiveYAxis, fy + h);
        return (Row(x, y, label.Owner is AxesModel ? 0.5 : 0), Row(System.Math.Min(left, right), System.Math.Min(y, top), System.Math.Abs(right - left), System.Math.Abs(top - y)));
    }
}
