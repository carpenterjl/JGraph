using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The M73 axes property families: fonts, grid appearance, ruler colors and tick geometry, limit
/// and tick modes, series cycling, axes-level color mapping, and aspect ratios. Everything here is
/// a MATLAB spelling over state the model already carries — the mode properties in particular are
/// derived words over the nullable-means-auto idiom, never a second copy of anything.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>The <c>XLimitMethod</c> words, in <see cref="LimitMethod"/>'s order.</summary>
    private static readonly string[] LimitMethodWords = ["padded", "tight", "tickaligned"];

    private static void AddAxesWave(IDictionary<string, GraphicsProperty> table)
    {
        AddFontBlock(table);
        AddGridBlock(table);
        AddRulerBlock(table);
        AddPolarRulerBlock(table);
        AddSeriesBlock(table);
        AddColorMappingBlock(table);
        AddAspectBlock(table);
    }

    /// <summary>Every ruler an axes owns, whichever mode it is in — a font speaks to all of them.</summary>
    private static IEnumerable<AxisModel> AllRulers(AxesModel axes)
    {
        foreach (AxisModel ruler in axes.XAxes)
        {
            yield return ruler;
        }

        foreach (AxisModel ruler in axes.YAxes)
        {
            yield return ruler;
        }

        yield return axes.ZAxis;
        yield return axes.RAxis;
        yield return axes.ThetaAxis;
    }

    private static JgsValue AutoManual(bool manual) => JgsValue.Str(manual ? "manual" : "auto");

    private static bool ToAutoManual(string name, JgsValue value, int line, int col) =>
        JgsBuiltins.StrOf(name, value, line, col).ToLowerInvariant() switch
        {
            "manual" => true,
            "auto" => false,
            var word => throw new JgsRuntimeException(
                line, col, $"{name} is 'auto' or 'manual', but got '{word}'."),
        };

    // --- Fonts ----------------------------------------------------------------------------------

    private static void AddFontBlock(IDictionary<string, GraphicsProperty> table)
    {
        // The ruler font is the axes font in MATLAB, so reads answer off the primary X ruler's tick
        // style and writes fan out: every ruler's tick and label styles, and the title pair through
        // their multipliers. A title() call with its own FontSize still overrides afterward.
        Put(table, "FontSize",
            entry =>
            {
                Axes(entry).ApplyAutoFont(); // the size follows the plot box (open item 118)
                return JgsValue.Number(Axes(entry).PrimaryXAxis.TickLabelStyle.FontSize);
            },
            (entry, value, line, col) =>
            {
                double size = Numbers("FontSize", value, 1, line, col)[0];
                if (size <= 0)
                {
                    throw new JgsRuntimeException(line, col, $"FontSize must be positive, but got {size}.");
                }

                ApplyFontSize(Axes(entry), size);
                Axes(entry).FontSizeManual = true;
            });

        Put(table, "FontSizeMode",
            entry => AutoManual(Axes(entry).FontSizeManual),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                bool manual = ToAutoManual("FontSizeMode", value, line, col);
                if (!manual && axes.FontSizeManual)
                {
                    // Automatic restores the built-in sizes: tick 11, label 13, title 15, subtitle 12.
                    foreach (AxisModel ruler in AllRulers(axes))
                    {
                        ruler.TickLabelStyle = ruler.TickLabelStyle.WithSize(11);
                        ruler.LabelStyle = ruler.LabelStyle.WithSize(13);
                    }

                    axes.TitleStyle = axes.TitleStyle.WithSize(15);
                    axes.SubtitleStyle = axes.SubtitleStyle.WithSize(12);
                }

                axes.FontSizeManual = manual;
                if (!manual)
                {
                    axes.ApplyAutoFont(); // and shrink with the plot box again (open item 118)
                }
            });

        Put(table, "FontName",
            entry => JgsValue.Str(Axes(entry).PrimaryXAxis.TickLabelStyle.FontFamily),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                string family = JgsBuiltins.StrOf("FontName", value, line, col);
                foreach (AxisModel ruler in AllRulers(axes))
                {
                    ruler.TickLabelStyle = ruler.TickLabelStyle.WithFamily(family);
                    ruler.LabelStyle = ruler.LabelStyle.WithFamily(family);
                }

                axes.TitleStyle = axes.TitleStyle.WithFamily(family);
                axes.TitleStyleManual = true; // open item 40
                axes.SubtitleStyle = axes.SubtitleStyle.WithFamily(family);
            });

        Put(table, "FontAngle",
            entry => JgsValue.Str(Axes(entry).PrimaryXAxis.TickLabelStyle.Italic ? "italic" : "normal"),
            (entry, value, line, col) =>
            {
                bool italic = JgsBuiltins.StrOf("FontAngle", value, line, col).ToLowerInvariant() switch
                {
                    "normal" => false,
                    "italic" or "oblique" => true,
                    var word => throw new JgsRuntimeException(
                        line, col, $"FontAngle is 'normal' or 'italic', but got '{word}'."),
                };
                AxesModel axes = Axes(entry);
                foreach (AxisModel ruler in AllRulers(axes))
                {
                    ruler.TickLabelStyle = ruler.TickLabelStyle.WithItalic(italic);
                    ruler.LabelStyle = ruler.LabelStyle.WithItalic(italic);
                }
            });

        // FontWeight speaks for the ruler text; the title pair carries its own weights below.
        Put(table, "FontWeight",
            entry => JgsValue.Str(Axes(entry).PrimaryXAxis.TickLabelStyle.Bold ? "bold" : "normal"),
            (entry, value, line, col) =>
            {
                bool bold = ToWeight("FontWeight", value, line, col);
                foreach (AxisModel ruler in AllRulers(Axes(entry)))
                {
                    ruler.TickLabelStyle = ruler.TickLabelStyle.WithBold(bold);
                    ruler.LabelStyle = ruler.LabelStyle.WithBold(bold);
                }
            });

        // Points are the only unit the renderer speaks; anything else is refused rather than
        // silently converted, and the refusal is a recorded divergence.
        Put(table, "FontUnits",
            static entry => JgsValue.Str("points"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("FontUnits", value, line, col);
                if (!word.Equals("points", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(
                        line, col, $"FontUnits other than 'points' are not supported, but got '{word}'.");
                }
            });

        Put(table, "FontSmoothing",
            entry => OnOff(Axes(entry).PrimaryXAxis.TickLabelStyle.Antialias),
            (entry, value, line, col) =>
            {
                bool smooth = ToOnOff("FontSmoothing", value, line, col);
                AxesModel axes = Axes(entry);
                foreach (AxisModel ruler in AllRulers(axes))
                {
                    ruler.TickLabelStyle = ruler.TickLabelStyle.WithAntialias(smooth);
                    ruler.LabelStyle = ruler.LabelStyle.WithAntialias(smooth);
                }

                axes.TitleStyle = axes.TitleStyle.WithAntialias(smooth);
                axes.SubtitleStyle = axes.SubtitleStyle.WithAntialias(smooth);
            });

        // The multipliers are stored on the model (and reflection would have answered them), but
        // the write must also re-derive the style it scales, or the number changes and the pixels
        // do not.
        Put(table, "TitleFontSizeMultiplier",
            entry => JgsValue.Number(Axes(entry).TitleFontSizeMultiplier),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                axes.TitleFontSizeMultiplier = Numbers("TitleFontSizeMultiplier", value, 1, line, col)[0];
                axes.TitleStyle = axes.TitleStyle.WithSize(
                    axes.PrimaryXAxis.TickLabelStyle.FontSize * axes.TitleFontSizeMultiplier);
                axes.TitleStyleManual = true; // open item 40
            });

        Put(table, "LabelFontSizeMultiplier",
            entry => JgsValue.Number(Axes(entry).LabelFontSizeMultiplier),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                axes.LabelFontSizeMultiplier = Numbers("LabelFontSizeMultiplier", value, 1, line, col)[0];
                foreach (AxisModel ruler in AllRulers(axes))
                {
                    ruler.LabelStyle = ruler.LabelStyle.WithSize(
                        ruler.TickLabelStyle.FontSize * axes.LabelFontSizeMultiplier);
                }
            });

        Put(table, "TitleFontWeight",
            entry => JgsValue.Str(Axes(entry).TitleStyle.Bold ? "bold" : "normal"),
            (entry, value, line, col) =>
            {
                Axes(entry).TitleStyle = Axes(entry).TitleStyle.WithBold(ToWeight("TitleFontWeight", value, line, col));
                Axes(entry).TitleStyleManual = true; // open item 40
            });

        Put(table, "SubtitleFontWeight",
            entry => JgsValue.Str(Axes(entry).SubtitleStyle.Bold ? "bold" : "normal"),
            (entry, value, line, col) => Axes(entry).SubtitleStyle =
                Axes(entry).SubtitleStyle.WithBold(ToWeight("SubtitleFontWeight", value, line, col)));

        Put(table, "TickLabelInterpreter",
            entry => JgsValue.Str(
                JgsBuiltins.InterpreterWord(Axes(entry).PrimaryXAxis.TickLabelStyle.Interpreter)),
            (entry, value, line, col) =>
            {
                TextInterpreter interpreter = JgsBuiltins.ParseInterpreter(
                    "TickLabelInterpreter",
                    JgsBuiltins.StrOf("TickLabelInterpreter", value, line, col),
                    line,
                    col);
                foreach (AxisModel ruler in AllRulers(Axes(entry)))
                {
                    ruler.TickLabelStyle = ruler.TickLabelStyle.WithInterpreter(interpreter);
                }
            });
    }

    private static void ApplyFontSize(AxesModel axes, double size)
    {
        foreach (AxisModel ruler in AllRulers(axes))
        {
            ruler.TickLabelStyle = ruler.TickLabelStyle.WithSize(size);
            ruler.LabelStyle = ruler.LabelStyle.WithSize(size * axes.LabelFontSizeMultiplier);
        }

        axes.TitleStyle = axes.TitleStyle.WithSize(size * axes.TitleFontSizeMultiplier);
        axes.TitleStyleManual = true; // the theme leaves a size a script chose alone (open item 40)
        axes.SubtitleStyle = axes.SubtitleStyle.WithSize(size);
        axes.SyncFurnitureFonts(); // the legend and colorbar follow (open item 118)
    }

    private static bool ToWeight(string name, JgsValue value, int line, int col) =>
        JgsBuiltins.StrOf(name, value, line, col).ToLowerInvariant() switch
        {
            "normal" => false,
            "bold" => true,
            var word => throw new JgsRuntimeException(
                line, col, $"{name} is 'normal' or 'bold', but got '{word}'."),
        };

    // --- Grid appearance ------------------------------------------------------------------------

    private static void AddGridBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddGridStyle(
            table, "GridColor", "GridAlpha", "GridLineStyle",
            static grid => grid.MajorLineStyle,
            static (grid, style) => grid.MajorLineStyle = style,
            static grid => grid.MajorColorManual,
            static (grid, manual) => grid.MajorColorManual = manual,
            static grid => grid.MajorAlphaManual,
            static (grid, manual) => grid.MajorAlphaManual = manual);
        AddGridStyle(
            table, "MinorGridColor", "MinorGridAlpha", "MinorGridLineStyle",
            static grid => grid.MinorLineStyle,
            static (grid, style) => grid.MinorLineStyle = style,
            static grid => grid.MinorColorManual,
            static (grid, manual) => grid.MinorColorManual = manual,
            static grid => grid.MinorAlphaManual,
            static (grid, manual) => grid.MinorAlphaManual = manual);

        Put(table, "XMinorGrid",
            entry => OnOff(Axes(entry).Grid.ShowMinorX),
            (entry, value, line, col) => Axes(entry).Grid.ShowMinorX = ToOnOff("XMinorGrid", value, line, col));
        Put(table, "YMinorGrid",
            entry => OnOff(Axes(entry).Grid.ShowMinorY),
            (entry, value, line, col) => Axes(entry).Grid.ShowMinorY = ToOnOff("YMinorGrid", value, line, col));
        Put(table, "ZMinorGrid",
            entry => OnOff(Axes(entry).Grid.ShowMinorZ),
            (entry, value, line, col) => Axes(entry).Grid.ShowMinorZ = ToOnOff("ZMinorGrid", value, line, col));
    }

    private static void AddGridStyle(
        IDictionary<string, GraphicsProperty> table,
        string colorName,
        string alphaName,
        string dashName,
        Func<GridModel, LineStyle> read,
        Action<GridModel, LineStyle> write,
        Func<GridModel, bool> colorManual,
        Action<GridModel, bool> setColorManual,
        Func<GridModel, bool> alphaManual,
        Action<GridModel, bool> setAlphaManual)
    {
        Put(table, colorName,
            entry => ColorRow(read(Axes(entry).Grid).Color),
            (entry, value, line, col) =>
            {
                GridModel grid = Axes(entry).Grid;
                Color chosen = JgsBuiltins.OptionColor(value, line, col, colorName);
                LineStyle style = read(grid);
                write(grid, style.WithColor(JgsBuiltins.WithAlpha(chosen, style.Color.A / 255.0)));
                setColorManual(grid, true);
            });

        Put(table, colorName + "Mode",
            entry => AutoManual(colorManual(Axes(entry).Grid)),
            (entry, value, line, col) =>
                setColorManual(Axes(entry).Grid, ToAutoManual(colorName + "Mode", value, line, col)));

        Put(table, alphaName,
            entry => JgsValue.Number(read(Axes(entry).Grid).Color.A / 255.0),
            (entry, value, line, col) =>
            {
                double alpha = Numbers(alphaName, value, 1, line, col)[0];
                if (alpha is < 0 or > 1 || !double.IsFinite(alpha))
                {
                    throw new JgsRuntimeException(line, col, $"{alphaName} is in [0, 1], but got {alpha}.");
                }

                GridModel grid = Axes(entry).Grid;
                LineStyle style = read(grid);
                write(grid, style.WithColor(JgsBuiltins.WithAlpha(style.Color, alpha)));
                setAlphaManual(grid, true);
            });

        Put(table, alphaName + "Mode",
            entry => AutoManual(alphaManual(Axes(entry).Grid)),
            (entry, value, line, col) =>
                setAlphaManual(Axes(entry).Grid, ToAutoManual(alphaName + "Mode", value, line, col)));

        Put(table, dashName,
            entry => JgsValue.Str(DashWord(read(Axes(entry).Grid).Dash)),
            (entry, value, line, col) =>
            {
                GridModel grid = Axes(entry).Grid;
                write(grid, read(grid).WithDash(
                    ToDash(dashName, JgsBuiltins.StrOf(dashName, value, line, col), line, col)));
            });
    }

    private static string DashWord(DashStyle dash) => dash switch
    {
        DashStyle.Dash => "--",
        DashStyle.Dot => ":",
        DashStyle.DashDot or DashStyle.DashDotDot => "-.",
        DashStyle.None => "none",
        _ => "-",
    };

    private static DashStyle ToDash(string name, string word, int line, int col) => word switch
    {
        "-" => DashStyle.Solid,
        "--" => DashStyle.Dash,
        ":" => DashStyle.Dot,
        "-." => DashStyle.DashDot,
        "none" => DashStyle.None,
        _ => throw new JgsRuntimeException(
            line, col, $"{name} is '-', '--', ':', '-.', or 'none', but got '{word}'."),
    };

    // --- Rulers: colors, modes, tick geometry, locations ----------------------------------------

    private static void AddRulerBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddRulerWave(table, "X", static axes => axes.PrimaryXAxis);
        AddRulerWave(table, "Y", static axes => axes.ActiveYAxis);
        AddRulerWave(table, "Z", static axes => axes.ZAxis);

        // TickDir, TickDirMode and TickLength are axes-wide in MATLAB: one word covers every ruler.
        Put(table, "TickDir",
            entry => JgsValue.Str(
                (Axes(entry).PrimaryXAxis.TickDirection ?? TickDirection.Out).ToString().ToLowerInvariant()),
            (entry, value, line, col) =>
            {
                TickDirection direction = JgsBuiltins.StrOf("TickDir", value, line, col).ToLowerInvariant() switch
                {
                    "in" => TickDirection.In,
                    "out" => TickDirection.Out,
                    "both" => TickDirection.Both,
                    var word => throw new JgsRuntimeException(
                        line, col, $"TickDir is 'in', 'out', or 'both', but got '{word}'."),
                };
                foreach (AxisModel ruler in AllRulers(Axes(entry)))
                {
                    ruler.TickDirection = direction;
                }
            });

        Put(table, "TickDirMode",
            entry => AutoManual(Axes(entry).PrimaryXAxis.TickDirection is not null),
            (entry, value, line, col) =>
            {
                bool manual = ToAutoManual("TickDirMode", value, line, col);
                foreach (AxisModel ruler in AllRulers(Axes(entry)))
                {
                    ruler.TickDirection = manual ? ruler.TickDirection ?? TickDirection.Out : null;
                }
            });

        // The automatic answer is MATLAB's default fractions; the automatic drawing is the fixed
        // five pixels every JGraph figure has always had — recorded as a divergence.
        Put(table, "TickLength",
            entry => Axes(entry).PrimaryXAxis.TickLength is { } chosen
                ? Row(chosen.X, chosen.Y)
                : Row(0.01, 0.025),
            (entry, value, line, col) =>
            {
                double[] pair = Numbers("TickLength", value, 2, line, col);
                if (pair[0] < 0 || pair[1] < 0)
                {
                    throw new JgsRuntimeException(line, col, "TickLength fractions cannot be negative.");
                }

                var length = new Vector2D(pair[0], pair[1]);
                foreach (AxisModel ruler in AllRulers(Axes(entry)))
                {
                    ruler.TickLength = length;
                }
            });

        Put(table, "XAxisLocation",
            entry => JgsValue.Str(
                Axes(entry).PrimaryXAxis.Position == AxisPosition.Top ? "top" : "bottom"),
            (entry, value, line, col) => Axes(entry).PrimaryXAxis.Position =
                JgsBuiltins.StrOf("XAxisLocation", value, line, col).ToLowerInvariant() switch
                {
                    "bottom" => AxisPosition.Bottom,
                    "top" => AxisPosition.Top,
                    var word => throw new JgsRuntimeException(
                        line, col, $"XAxisLocation is 'bottom' or 'top', but got '{word}'."),
                });

        // Under yyaxis it names the side that is active, as R2025b's does (open item 106).
        Put(table, "YAxisLocation",
            entry => JgsValue.Str(
                Axes(entry).ActiveYAxis.Position == AxisPosition.Right ? "right" : "left"),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                if (axes.YAxes.Count > 1)
                {
                    // MATLAB refuses this on a yyaxis axes too: each side already has its ruler.
                    throw new JgsRuntimeException(
                        line, col, "YAxisLocation cannot be set on an axes with two y-rulers.");
                }

                axes.PrimaryYAxis.Position =
                    JgsBuiltins.StrOf("YAxisLocation", value, line, col).ToLowerInvariant() switch
                    {
                        "left" => AxisPosition.Left,
                        "right" => AxisPosition.Right,
                        var word => throw new JgsRuntimeException(
                            line, col, $"YAxisLocation is 'left' or 'right', but got '{word}'."),
                    };
            });
    }

    /// <summary>A ruler's colour mode: 'manual' freezes the ink showing, 'auto' hands back the built-in inks.</summary>
    internal static void SetRulerColorMode(AxisModel ruler, string name, JgsValue value, int line, int col)
    {
        if (ToAutoManual(name, value, line, col))
        {
            ruler.RulerColor ??= ruler.TickLabelStyle.Color;
            ruler.InkIsAutomatic = false;
        }
        else if (ruler.RulerColor is not null && !ruler.InkIsAutomatic)
        {
            Unink(ruler);
        }
    }

    /// <summary>
    /// A ruler's colour mode: 'manual' once a script inked it; a ruler yyaxis inked in its side's
    /// colour is still 'auto', as R2025b's is (open item 111).
    /// </summary>
    internal static JgsValue RulerColorMode(AxisModel ruler) =>
        AutoManual(ruler.RulerColor is not null && !ruler.InkIsAutomatic);

    /// <summary>Hands a ruler back the theme's line and its built-in text inks.</summary>
    internal static void Unink(AxisModel ruler)
    {
        ruler.RulerColor = null;
        ruler.InkIsAutomatic = false;
        ruler.TickLabelStyle = ruler.TickLabelStyle.WithColor(Colors.DarkGray);
        ruler.LabelStyle = ruler.LabelStyle.WithColor(Colors.Black);
    }

    private static void AddRulerWave(
        IDictionary<string, GraphicsProperty> table, string letter, Func<AxesModel, AxisModel> chosen)
    {
        // {X}Color inks the whole ruler: axis line, ticks, and both text runs. Automatic hands the
        // text back its built-in inks and the line back to the theme's.
        Put(table, letter + "Color",
            entry =>
            {
                AxisModel ruler = chosen(Axes(entry));
                return ColorRow(ruler.RulerColor ?? ruler.TickLabelStyle.Color);
            },
            (entry, value, line, col) =>
            {
                chosen(Axes(entry)).Ink(JgsBuiltins.OptionColor(value, line, col, letter + "Color"));
            });

        Put(table, letter + "ColorMode",
            entry => RulerColorMode(chosen(Axes(entry))),
            (entry, value, line, col) => SetRulerColorMode(chosen(Axes(entry)), letter + "ColorMode", value, line, col));

        // The mode spellings are derived words over the nullable-means-auto state the rulers have
        // always carried; 'manual' freezes what is showing, exactly as xlim('manual') does.
        Put(table, letter + "LimMode",
            entry => AutoManual(!chosen(Axes(entry)).AutoScale),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                AxisModel ruler = chosen(axes);
                if (ToAutoManual(letter + "LimMode", value, line, col))
                {
                    if (ruler.AutoScale)
                    {
                        axes.RecomputeDataBounds();
                        ruler.AutoScale = false;
                    }
                }
                else
                {
                    ruler.AutoScale = true;
                    axes.RecomputeDataBounds();
                }
            });

        Put(table, letter + "LimitMethod",
            entry => JgsValue.Str(LimitMethodWords[(int)chosen(Axes(entry)).LimitMethod]),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                AxisModel ruler = chosen(axes);

                // The word in any case, or a start of it that only one word has; anything else is
                // refused in R2025b's words (open item 87, probe_87). The limits themselves stay
                // JGraph's: the default method reads 'padded' where R2025b's is 'tickaligned'.
                string typed = JgsBuiltins.StrOf(letter + "LimitMethod", value, line, col);
                ruler.LimitMethod = Matching(typed.Trim(), LimitMethodWords) switch
                {
                    "padded" => LimitMethod.Padded,
                    "tight" => LimitMethod.Tight,
                    "tickaligned" => LimitMethod.Tickaligned,
                    _ => throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidEnumValue",
                        $"Error setting property '{letter}LimitMethod' of class 'Axes':\n'{typed}' is not a valid value. Use one of these values: 'tickaligned' | 'tight' | 'padded'."),
                };
                if (ruler.AutoScale)
                {
                    axes.RecomputeDataBounds();
                }
            });

        Put(table, letter + "TickMode",
            entry => AutoManual(chosen(Axes(entry)).TickPositions is not null),
            (entry, value, line, col) =>
            {
                AxisModel ruler = chosen(Axes(entry));
                if (ToAutoManual(letter + "TickMode", value, line, col))
                {
                    // Freezing what is showing is writing the current ticks back as chosen ones.
                    JgsRulerTicks.WriteValues(
                        letter + "TickMode", ruler, JgsRulerTicks.ReadValues(ruler), line, col);
                }
                else
                {
                    ruler.TickPositions = null;
                }
            });

        Put(table, letter + "TickLabelMode",
            entry => AutoManual(chosen(Axes(entry)).TickLabelOverrides is not null),
            (entry, value, line, col) =>
            {
                AxisModel ruler = chosen(Axes(entry));
                if (ToAutoManual(letter + "TickLabelMode", value, line, col))
                {
                    JgsRulerTicks.WriteLabels(
                        letter + "TickLabelMode", ruler, JgsRulerTicks.ReadLabels(ruler), line, col);
                }
                else
                {
                    ruler.TickLabelOverrides = null;
                }
            });

        Put(table, letter + "MinorTick",
            entry => OnOff(chosen(Axes(entry)).ShowMinorTicks),
            (entry, value, line, col) => chosen(Axes(entry)).ShowMinorTicks =
                ToOnOff(letter + "MinorTick", value, line, col));
    }

    // --- Series cycling -------------------------------------------------------------------------

    private static void AddSeriesBlock(IDictionary<string, GraphicsProperty> table)
    {
        // Under yyaxis a MATLAB script reads and writes the active side's own order (open item 111).
        Put(table, "ColorOrder",
            entry => ColorTable(JgsBuiltins.SideColorOrderOf(Axes(entry)) ?? Axes(entry).ColorOrder ?? Colors.DefaultSeriesOrder),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                IReadOnlyList<Color>? colors = ParseColorList("ColorOrder", value, line, col);
                if (JgsBuiltins.SideColorOrderOf(axes) is not null && colors is not null)
                {
                    axes.SetSideColorOrder(axes.ActiveYAxisIndex, colors);
                    return;
                }

                axes.ColorOrder = colors;
            });

        // Both index spellings are the same 1-based counter; NextSeriesIndex is the R2023b name.
        foreach (string name in new[] { "ColorOrderIndex", "NextSeriesIndex" })
        {
            string spelling = name;
            // Under yyaxis a MATLAB script reads the active side's own place in its colours (item 111).
            Put(table, spelling,
                entry => JgsBuiltins.SideColorOrderOf(Axes(entry)) is { } own
                    ? JgsValue.Number((Axes(entry).SideSeriesCount(Axes(entry).ActiveYAxisIndex) % own.Count) + 1)
                    : JgsValue.Number(Axes(entry).NextSeriesIndex + 1),
                (entry, value, line, col) =>
                {
                    double seat = Numbers(spelling, value, 1, line, col)[0];
                    if (seat < 1 || !double.IsFinite(seat))
                    {
                        throw new JgsRuntimeException(
                            line, col, $"{spelling} is a positive whole number, but got {seat}.");
                    }

                    Axes(entry).NextSeriesIndex = (int)System.Math.Round(seat) - 1;
                });
        }

        // A MATLAB script reads the order back as it was written: text as a char matrix, a list as a
        // cell, and R2025b's own orders - '-', and yyaxis's - as text (open item 111). JGS reads a cell.
        Put(table, "LineStyleOrder",
            entry =>
            {
                AxesModel axes = Axes(entry);
                IReadOnlyList<string> words = axes.LineStyleOrderWords
                    ?? (axes.LineStyleOrder ?? new[] { SeriesLineStyle.Solid }).Select(LineStyleWord).ToArray();
                if (JgsRunningDialect.ThreadIsMatlab && !axes.LineStyleOrderIsList)
                {
                    return words.Count == 1 ? JgsValue.Str(words[0]) : JgsValue.CharMatrix([.. words]);
                }

                // R2025b keeps a list as a column, however it was written.
                JgsValue list = JgsValue.Cell([.. words.Select(JgsValue.Str)]);
                if (JgsRunningDialect.ThreadIsMatlab)
                {
                    list.Reshape(words.Count, 1);
                }

                return list;
            },
            (entry, value, line, col) =>
            {
                (IReadOnlyList<SeriesLineStyle>? styles, string[] words, bool asList) = ParseLineStyleOrder(value, line, col);
                Axes(entry).SetLineStyleOrder(styles, words, asList);
            });

        Put(table, "LineStyleOrderIndex",
            entry =>
            {
                AxesModel axes = Axes(entry);
                int colors = (axes.ColorOrder ?? Colors.DefaultSeriesOrder).Count;
                int styles = axes.LineStyleOrder?.Count ?? 1;

                // Under yyaxis the active side's own count, stepping a style each time the side's
                // colours come round (open items 106 and 111).
                if (axes.YAxes.Count > 1)
                {
                    int laps = axes.SideColorOrder(axes.ActiveYAxisIndex)?.Count ?? 1;
                    return JgsValue.Number((axes.SideSeriesCount(axes.ActiveYAxisIndex) / laps % styles) + 1);
                }

                return JgsValue.Number((axes.NextSeriesIndex / System.Math.Max(1, colors) % styles) + 1);
            });
    }

    private static JgsValue ColorTable(IReadOnlyList<Color> colors)
    {
        var data = new double[colors.Count * 3];
        for (int i = 0; i < colors.Count; i++)
        {
            data[i] = colors[i].R / 255.0;
            data[colors.Count + i] = colors[i].G / 255.0;
            data[(2 * colors.Count) + i] = colors[i].B / 255.0;
        }

        return JgsMatrix.FromColumnMajor(data, colors.Count, 3);
    }

    private static IReadOnlyList<Color>? ParseColorList(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return new[] { JgsBuiltins.OptionColor(value, line, col, name) };
        }

        if (value.Type == JgsType.Cell)
        {
            return value.AsCell.Select(spec => JgsBuiltins.OptionColor(spec, line, col, name)).ToArray();
        }

        double[][] rows = JgsMatrix.ToRows(name, value, line, col);
        if (rows.Length == 0)
        {
            return null;
        }

        var colors = new Color[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Length != 3)
            {
                throw new JgsRuntimeException(
                    line, col, $"{name} is an n-by-3 matrix of red, green and blue in [0, 1].");
            }

            colors[i] = Color.FromScRgb(
                System.Math.Clamp(rows[i][0], 0, 1),
                System.Math.Clamp(rows[i][1], 0, 1),
                System.Math.Clamp(rows[i][2], 0, 1));
        }

        return colors;
    }

    private static string LineStyleWord(SeriesLineStyle style)
    {
        string dash = DashWord(style.Dash);
        string marker = style.Marker switch
        {
            MarkerType.Circle => "o",
            MarkerType.Square => "s",
            MarkerType.Diamond => "d",
            MarkerType.TriangleUp => "^",
            MarkerType.TriangleDown => "v",
            MarkerType.Plus => "+",
            MarkerType.Cross => "x",
            MarkerType.Star => "*",
            MarkerType.Point => ".",
            _ => string.Empty,
        };
        return dash == "none" ? marker : dash + marker;
    }

    /// <summary>
    /// A line-style order as written: a cell or a string array is a list, one word or a char matrix of
    /// them (a row each, the padding dropped) text (open item 111). Answers the styles, the words, and
    /// whether they came as a list.
    /// </summary>
    private static (IReadOnlyList<SeriesLineStyle>? Styles, string[] Words, bool AsList) ParseLineStyleOrder(
        JgsValue value, int line, int col)
    {
        bool asList = value.Type == JgsType.Cell || (value.IsStringArray && !JgsBuiltins.IsTextScalar(value));
        string[] texts = value.Type == JgsType.Cell
            ? [.. value.AsCell.Select(spec => JgsBuiltins.StrOf("LineStyleOrder", spec, line, col))]
            : value.IsCharMatrix
                ? [.. value.CharMatrixRows().Select(static row => row.TrimEnd())]
                : value.IsStringArray && JgsBuiltins.TextElementsOf(value) is { } strings
                    ? strings
                    : [JgsBuiltins.StrOf("LineStyleOrder", value, line, col)];
        if (texts.Length == 0)
        {
            return (null, [], false);
        }

        var order = new SeriesLineStyle[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            string text = texts[i];
            LineSpec spec = LineSpec.Parse(text);
            if (spec.Color is not null)
            {
                throw new JgsRuntimeException(line, col,
                    $"LineStyleOrder entries carry line styles and markers, not colors: '{text}'.");
            }

            // A bare marker means marker-only (no connecting line), which is MATLAB's reading too.
            DashStyle dash = spec.Dash
                ?? (spec.MarkerSpecified && !spec.LineSpecified ? DashStyle.None : DashStyle.Solid);
            order[i] = new SeriesLineStyle(dash, spec.Marker ?? MarkerType.None);
        }

        return (order, texts, asList);
    }

    // --- Axes-level color mapping ----------------------------------------------------------------

    private static void AddColorMappingBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "CLim",
            entry =>
            {
                AxesModel axes = Axes(entry);
                if (axes.ColorLimits is { } pinned)
                {
                    return Row(pinned.Min, pinned.Max);
                }

                foreach (PlotObject plot in axes.Plots)
                {
                    if (plot is Rendering.IColorMapped mapped && !IsTrueColour(plot))
                    {
                        (double min, double max) = mapped.ColorRange;
                        return Row(min, max);
                    }
                }

                return Row(0, 1);
            },
            (entry, value, line, col) =>
            {
                double[] pair = Numbers("CLim", value, 2, line, col);
                if (!double.IsFinite(pair[0]) || !double.IsFinite(pair[1]) || pair[0] >= pair[1])
                {
                    throw new JgsRuntimeException(line, col,
                        $"CLim must be finite and increasing, but got [{pair[0]}, {pair[1]}].");
                }

                AxesModel axes = Axes(entry);
                axes.ColorLimits = new DataRange(pair[0], pair[1]);
                foreach (PlotObject plot in axes.Plots)
                {
                    plot.AdoptAxesDefaults(axes);
                }
            });

        Put(table, "CLimMode",
            entry => AutoManual(Axes(entry).ColorLimits is not null),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                if (ToAutoManual("CLimMode", value, line, col))
                {
                    if (axes.ColorLimits is null)
                    {
                        // Freeze what is showing, the way xlim('manual') freezes a fitted range.
                        (double min, double max) = CurrentColorRange(axes);
                        axes.ColorLimits = new DataRange(min, max);
                        foreach (PlotObject plot in axes.Plots)
                        {
                            plot.AdoptAxesDefaults(axes);
                        }
                    }
                }
                else if (axes.ColorLimits is not null)
                {
                    axes.ColorLimits = null;
                    ReleaseColorLimits(axes);
                }
            });

        Put(table, "Colormap",
            entry =>
            {
                AxesModel axes = Axes(entry);
                Colormap map = axes.ResolveColormap() ?? FirstMappedColormap(axes) ?? Colormap.Parula;
                return ColorTable(map.Stops);
            },
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                Colormap map;
                if (value.Type == JgsType.String)
                {
                    string name = JgsBuiltins.StrOf("Colormap", value, line, col);
                    if (!Colormap.TryGetByName(name, out map))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"Unknown colormap '{name}'. Known colormaps: {string.Join(", ", Colormap.KnownNames)}.");
                    }
                }
                else
                {
                    map = (Colormap)ValueBridge.FromValue(typeof(Colormap), "Colormap", value, line, col)!;
                }

                axes.Colormap = map;
                foreach (PlotObject plot in axes.Plots)
                {
                    plot.AdoptAxesDefaults(axes);
                }
            });
    }

    private static (double Min, double Max) CurrentColorRange(AxesModel axes)
    {
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot is Rendering.IColorMapped mapped && !IsTrueColour(plot))
            {
                return mapped.ColorRange;
            }
        }

        return (0, 1);
    }

    /// <summary>
    /// A true-colour image maps no colours, so the automatic colour limits pass it by: R2025b's axes
    /// under <c>imshow(rgb)</c> reads [0 1] (open item 142, oi142_imshow B8).
    /// </summary>
    private static bool IsTrueColour(PlotObject plot) => plot is JGraph.Objects.ImagePlot { TrueColors: not null };

    private static Colormap? FirstMappedColormap(AxesModel axes)
    {
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot is Rendering.IColorMapped mapped)
            {
                return mapped.Colormap;
            }
        }

        return null;
    }

    /// <summary>Hands every color-mapped plot back its own data range (CLimMode 'auto').</summary>
    private static void ReleaseColorLimits(AxesModel axes)
    {
        foreach (PlotObject plot in axes.Plots)
        {
            switch (plot)
            {
                case Objects.ImagePlot image:
                    image.AutoScaleColor = true;
                    break;
                case Objects.SurfacePlot surface:
                    surface.AutoScaleColor = true;
                    break;
                case Objects.ContourPlot contour:
                    contour.AutoScaleColor = true;
                    break;
                case Objects.PatchPlot patch:
                    patch.AutoScaleColor = true;
                    break;
                case Objects.ScatterPlot scatter:
                    scatter.AutoScaleColor = true;
                    break;
                case Objects.Scatter3DPlot scatter3D:
                    scatter3D.AutoScaleColor = true;
                    break;
            }
        }
    }

    // --- Aspect ratios ---------------------------------------------------------------------------

    private static void AddAspectBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "DataAspectRatio",
            entry =>
            {
                AxesModel axes = Axes(entry);
                Vector3D ratio = axes.DataAspectRatio ?? DerivedDataAspect(axes);
                return Row(ratio.X, ratio.Y, ratio.Z);
            },
            (entry, value, line, col) =>
            {
                double[] xyz = Numbers("DataAspectRatio", value, 3, line, col);
                if (xyz.Any(static v => v <= 0 || !double.IsFinite(v)))
                {
                    throw new JgsRuntimeException(
                        line, col, "DataAspectRatio takes three positive finite numbers.");
                }

                Axes(entry).DataAspectRatio = new Vector3D(xyz[0], xyz[1], xyz[2]);
            });

        Put(table, "DataAspectRatioMode",
            entry => AutoManual(Axes(entry).DataAspectRatio is not null),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                if (ToAutoManual("DataAspectRatioMode", value, line, col))
                {
                    axes.DataAspectRatio ??= DerivedDataAspect(axes);
                }
                else
                {
                    axes.DataAspectRatio = null;
                }
            });

        Put(table, "PlotBoxAspectRatio",
            entry => Row(
                Axes(entry).PlotBoxAspect.X, Axes(entry).PlotBoxAspect.Y, Axes(entry).PlotBoxAspect.Z),
            (entry, value, line, col) =>
            {
                double[] xyz = Numbers("PlotBoxAspectRatio", value, 3, line, col);
                if (xyz.Any(static v => v <= 0 || !double.IsFinite(v)))
                {
                    throw new JgsRuntimeException(
                        line, col, "PlotBoxAspectRatio takes three positive finite numbers.");
                }

                AxesModel axes = Axes(entry);
                axes.PlotBoxAspect = new Vector3D(xyz[0], xyz[1], xyz[2]);
                axes.PlotBoxAspectManual = true;
            });

        Put(table, "PlotBoxAspectRatioMode",
            entry => AutoManual(Axes(entry).PlotBoxAspectManual),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                if (ToAutoManual("PlotBoxAspectRatioMode", value, line, col))
                {
                    axes.PlotBoxAspectManual = true;
                }
                else
                {
                    // Back to the default cube, without disturbing a stored data aspect: the box
                    // write clears it deliberately for scripts, so it is saved and put back here.
                    Vector3D? kept = axes.DataAspectRatio;
                    axes.PlotBoxAspect = new Vector3D(1, 1, 1);
                    axes.DataAspectRatio = kept;
                    axes.PlotBoxAspectManual = false;
                }
            });
    }

    /// <summary>
    /// The data aspect an auto axes is showing: the rulers' spans, told with the smallest as one.
    /// </summary>
    private static Vector3D DerivedDataAspect(AxesModel axes)
    {
        double x = System.Math.Abs(axes.PrimaryXAxis.Range.Max - axes.PrimaryXAxis.Range.Min);
        double y = System.Math.Abs(axes.ActiveYAxis.Range.Max - axes.ActiveYAxis.Range.Min);
        double z = System.Math.Abs(axes.ZAxis.Range.Max - axes.ZAxis.Range.Min);
        double smallest = new[] { x, y, z }.Where(static v => v > 0).DefaultIfEmpty(1).Min();
        return new Vector3D(
            x > 0 ? x / smallest : 1,
            y > 0 ? y / smallest : 1,
            z > 0 ? z / smallest : 1);
    }
}
