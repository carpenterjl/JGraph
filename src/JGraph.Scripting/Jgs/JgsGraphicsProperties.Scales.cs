using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The properties of the second batch of <c>uifigure</c> components (app-building plan, U9):
/// knobs, switches, gauges, the lamp, the date picker and the colour picker. Names, defaults,
/// coercions and refusals are R2025b's, recorded headless (probes <c>u9_matrix</c>,
/// <c>u9_behave</c>, <c>u9_forms</c>).
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static readonly string[] ScaleDirectionWords = ["clockwise", "counterclockwise"];

    private static readonly string[] DayNames = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    private static UiScaleModel Scale(JgsHandleEntry entry) => (UiScaleModel)entry.Target;

    private static UiGaugeStyle GaugeStyleOf(Type type) =>
        type == typeof(UiLinearGaugeModel) ? UiGaugeStyle.Linear
        : type == typeof(UiNinetyDegreeGaugeModel) ? UiGaugeStyle.NinetyDegree
        : type == typeof(UiSemicircularGaugeModel) ? UiGaugeStyle.Semicircular
        : UiGaugeStyle.Circular;

    // --- ticks of every scale --------------------------------------------------------------------

    /// <summary>
    /// The ticks a scale leaves to itself. A slider's follow the pixels its track has (U5); a knob's
    /// and a gauge's follow a rule of round steps that agrees with R2025b's view on the common
    /// ranges and not on all (probe <c>u9_behave</c>, <c>TICKS</c>; the ADR has the count).
    /// </summary>
    internal static void RefreshTicks(UiScaleModel scale)
    {
        if (scale is UiSliderModel slider)
        {
            RefreshSliderTicks(slider);
            return;
        }

        double low = scale.Lower;
        double high = scale.Upper;
        double span = high - low;
        if (!(span > 0) || !double.IsFinite(span))
        {
            return;
        }

        // The counts are what R2025b reads before its view has settled, which is what a script
        // reads at once and what the fixtures hold; the view re-ticks a knob to ten intervals and
        // a ninety-degree gauge to four some time later (probe u9_behave, TICKS).
        (int most, int fallback, int minorPer) = scale switch
        {
            UiKnobModel => (8, 7, 2),
            UiGaugeModel { Style: UiGaugeStyle.NinetyDegree } => (2, 2, 5),
            UiGaugeModel { Style: UiGaugeStyle.Linear } => (8, 5, 5),
            UiGaugeModel { Style: UiGaugeStyle.Semicircular } => (8, 5, 5),
            _ => (8, 5, 10),
        };

        if (!scale.MajorTicksManual)
        {
            // The most intervals, up to the kind's most, whose step is a round number; failing
            // that, the kind's own count, with the ticks rounded to four significant digits.
            double[]? best = null;
            for (int intervals = most; intervals >= 2 && best is null; intervals--)
            {
                double step = span / intervals;
                if (IsRoundStep(step))
                {
                    best = Even(low, high, intervals, span);
                }
            }

            best ??= [.. Even(low, high, fallback, span).Select(static t => RoundSignificant(t, 4))];
            best[^1] = high;
            if (!scale.MajorTicks.SequenceEqual(best))
            {
                scale.MajorTicks = best;
            }
        }

        if (!scale.MajorTickLabelsManual)
        {
            string[] labels = [.. scale.MajorTicks.Select(TickLabel)];
            if (!scale.MajorTickLabels.SequenceEqual(labels))
            {
                scale.MajorTickLabels = labels;
            }
        }

        if (!scale.MinorTicksManual)
        {
            IReadOnlyList<double> major = scale.MajorTicks;
            var minor = new List<double>();
            for (int i = 0; i + 1 < major.Count; i++)
            {
                for (int k = 0; k < minorPer; k++)
                {
                    minor.Add(RoundTick(major[i] + ((major[i + 1] - major[i]) * k / minorPer), span));
                }
            }

            if (major.Count > 1)
            {
                minor.Add(major[^1]);
            }

            if (!scale.MinorTicks.SequenceEqual(minor))
            {
                scale.MinorTicks = minor;
            }
        }
    }

    private static double[] Even(double low, double high, int intervals, double span)
    {
        var ticks = new double[intervals + 1];
        for (int i = 0; i <= intervals; i++)
        {
            ticks[i] = i == intervals ? high : RoundTick(low + (span * i / intervals), span);
        }

        return ticks;
    }

    /// <summary>Whether a step is one of the round sizes a scale is marked in: 1, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 times a power of ten.</summary>
    private static bool IsRoundStep(double step)
    {
        if (!(step > 0) || !double.IsFinite(step))
        {
            return false;
        }

        double mantissa = step / System.Math.Pow(10, System.Math.Floor(System.Math.Log10(step)));
        foreach (double round in new[] { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
        {
            if (System.Math.Abs(mantissa - round) < 1e-9 * round)
            {
                return true;
            }
        }

        return false;
    }

    private static double RoundSignificant(double value, int digits)
    {
        if (value == 0 || !double.IsFinite(value))
        {
            return value;
        }

        double scale = System.Math.Pow(10, digits - 1 - System.Math.Floor(System.Math.Log10(System.Math.Abs(value))));
        return System.Math.Round(value * scale) / scale;
    }

    /// <summary>
    /// The tick properties every scale has (U5 for a slider, U9 for a knob and a gauge):
    /// <c>MajorTicks</c>, <c>MinorTicks</c>, <c>MajorTickLabels</c> and their three modes.
    /// </summary>
    private static void AddScaleTicksBlock(IDictionary<string, GraphicsProperty> table)
    {
        foreach (bool major in new[] { true, false })
        {
            string name = major ? "MajorTicks" : "MinorTicks";
            Put(table, name,
                entry =>
                {
                    RefreshTicks(Scale(entry));
                    IReadOnlyList<double> ticks = major ? Scale(entry).MajorTicks : Scale(entry).MinorTicks;
                    return ticks.Count == 0 ? JgsMatrix.FromColumnMajor([], 0, 0) : Row([.. ticks]);
                },
                (entry, value, line, col) =>
                {
                    UiScaleModel scale = Scale(entry);
                    if (value.Type == JgsType.Complex
                        || (value.Type == JgsType.Array && !value.IsStringArray && Enumerable.Range(0, value.ArrayLength).Any(i => value.ElementAt(i).Type == JgsType.Complex)))
                    {
                        throw UiError(entry, $"invalid{name}", $"'{name}' array cannot contain complex values.", line, col);
                    }

                    double[]? ticks = value.IsTime || (value.Type == JgsType.Array && value.Rows > 1 && value.Cols > 1) ? null : RealNumbers(value);
                    if (ticks is null)
                    {
                        throw UiError(entry, $"invalid{name}", $"'{name}' must be a 1-by-n numeric array.", line, col);
                    }

                    if (!ticks.All(double.IsFinite))
                    {
                        throw UiError(entry, $"invalid{name}", $"'{name}' array cannot contain NaN or Inf.", line, col);
                    }

                    double[] sorted = [.. ticks.Distinct().OrderBy(static t => t)];
                    if (major)
                    {
                        scale.MajorTicks = sorted;
                        scale.MajorTicksManual = true;
                    }
                    else
                    {
                        scale.MinorTicks = sorted;
                        scale.MinorTicksManual = true;
                    }

                    RefreshTicks(scale);
                });
            string mode = name + "Mode";
            Put(table, mode,
                entry => JgsValue.Str((major ? Scale(entry).MajorTicksManual : Scale(entry).MinorTicksManual) ? "manual" : "auto"),
                (entry, value, line, col) =>
                {
                    bool manual = UiWord(entry, value, AutoManualWords, $"invalid{mode}", $"'{mode}' value must be 'auto' or 'manual'.", line, col) == "manual";
                    if (major)
                    {
                        Scale(entry).MajorTicksManual = manual;
                    }
                    else
                    {
                        Scale(entry).MinorTicksManual = manual;
                    }

                    RefreshTicks(Scale(entry));
                });
        }

        Put(table, "MajorTickLabels",
            entry =>
            {
                RefreshTicks(Scale(entry));
                return RowCell(Scale(entry).MajorTickLabels);
            },
            (entry, value, line, col) =>
            {
                string[]? labels = value.Type == JgsType.Cell && value.AsCell.Length == 0 ? [] : TextVector(value);
                Scale(entry).MajorTickLabels = labels ?? throw UiError(entry, "invalidMajorTickLabels",
                    "'MajorTickLabels' must be a 1-by-N array of the following type: cell array of character vectors, string, or categorical.", line, col);
                Scale(entry).MajorTickLabelsManual = true;
            });
        Put(table, "MajorTickLabelsMode",
            entry => JgsValue.Str(Scale(entry).MajorTickLabelsManual ? "manual" : "auto"),
            (entry, value, line, col) =>
            {
                Scale(entry).MajorTickLabelsManual = UiWord(entry, value, AutoManualWords, "invalidMajorTickLabelsMode",
                    "'MajorTickLabelsMode' value must be 'auto' or 'manual'.", line, col) == "manual";
                RefreshTicks(Scale(entry));
            });
    }

    /// <summary>A scale's <c>Limits</c>: two finite, increasing doubles; the value is clamped into them when <paramref name="clamp"/>.</summary>
    private static void AddScaleLimits(IDictionary<string, GraphicsProperty> table, bool clamp)
    {
        Put(table, "Limits",
            entry => Row(Scale(entry).Lower, Scale(entry).Upper),
            (entry, value, line, col) =>
            {
                UiScaleModel scale = Scale(entry);
                double[]? limits = value.Type == JgsType.Array && !value.IsTime && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double" && (value.Rows == 1 || value.Cols == 1)
                    ? RealNumbers(value)
                    : null;
                if (limits is not { Length: 2 } || !limits.All(double.IsFinite) || !(limits[1] > limits[0]))
                {
                    throw UiError(entry, "invalidFiniteScaleLimits", "'Limits' must be a 1-by-2 array of finite, increasing double values.", line, col);
                }

                scale.Lower = limits[0];
                scale.Upper = limits[1];
                if (clamp)
                {
                    scale.Value = System.Math.Clamp(scale.Value, limits[0], limits[1]);
                    if (scale is UiRangeSliderModel two)
                    {
                        two.High = System.Math.Clamp(two.High, limits[0], limits[1]);
                    }
                }

                if (scale is UiGaugeModel gauge && !gauge.ScaleColorLimitsManual)
                {
                    gauge.ScaleColorLimits = EvenBands(gauge);
                }

                RefreshTicks(scale);
            });
    }

    // --- knob ------------------------------------------------------------------------------------

    private static void AddKnobBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddNamedSlot(table, "ValueChangedFcn");
        AddNamedSlot(table, "ValueChangingFcn");
        Put(table, "Value",
            entry => JgsValue.Number(Scale(entry).Value),
            (entry, value, line, col) =>
            {
                // A logical is refused; false lands only where the knob is already at 0, through the
                // equality every write is checked for first (SkipsEqualWrite).
                UiScaleModel knob = Scale(entry);
                double? number = DoubleScalar(value);
                knob.Value = number is { } given && given >= knob.Lower && given <= knob.Upper
                    ? given
                    : throw UiError(entry, "invalidValue", "'Value' must be a double scalar within the range of 'Limits'.", line, col);
            });
        AddScaleLimits(table, clamp: true);
        AddScaleTicksBlock(table);
    }

    // --- gauges ----------------------------------------------------------------------------------

    private static UiGaugeModel Gauge(JgsHandleEntry entry) => (UiGaugeModel)entry.Target;

    /// <summary>The bands R2025b gives a gauge whose colours it was told and whose limits it was not: equal shares of the scale.</summary>
    private static double[] EvenBands(UiGaugeModel gauge)
    {
        int count = gauge.ScaleColors.Count;
        var bands = new double[2 * count];
        double span = gauge.Upper - gauge.Lower;
        for (int i = 0; i < count; i++)
        {
            bands[2 * i] = gauge.Lower + (span * i / count);
            bands[(2 * i) + 1] = gauge.Lower + (span * (i + 1) / count);
        }

        return bands;
    }

    private static JgsValue BandsValue(IReadOnlyList<double> bands)
    {
        int rows = bands.Count / 2;
        if (rows == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        var data = new double[2 * rows];
        for (int i = 0; i < rows; i++)
        {
            data[i] = bands[2 * i];
            data[rows + i] = bands[(2 * i) + 1];
        }

        return JgsMatrix.FromColumnMajor(data, rows, 2);
    }

    private static JgsValue ColorsValue(IReadOnlyList<UiColor> colors)
    {
        int rows = colors.Count;
        if (rows == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        var data = new double[3 * rows];
        for (int i = 0; i < rows; i++)
        {
            data[i] = colors[i].R;
            data[rows + i] = colors[i].G;
            data[(2 * rows) + i] = colors[i].B;
        }

        return JgsMatrix.FromColumnMajor(data, rows, 3);
    }

    private static void AddGaugeBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Value",
            entry => JgsValue.Number(Scale(entry).Value),
            (entry, value, line, col) =>
            {
                string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                double? number = value.Type == JgsType.Number && kind != "logical" ? value.AsNumber : null;
                Scale(entry).Value = number is { } given && double.IsFinite(given)
                    ? given
                    : throw UiError(entry, "invalidValue", "'Value' must be a finite numeric, such as 10.", line, col);
            });
        AddScaleLimits(table, clamp: false);
        AddScaleTicksBlock(table);
        Put(table, "ScaleColors",
            entry => ColorsValue(Gauge(entry).ScaleColors),
            (entry, value, line, col) =>
            {
                UiGaugeModel gauge = Gauge(entry);
                gauge.ScaleColors = ScaleColorsOf(entry, value, line, col);
                if (!gauge.ScaleColorLimitsManual)
                {
                    gauge.ScaleColorLimits = EvenBands(gauge);
                }
            });
        Put(table, "ScaleColorLimits",
            entry => BandsValue(Gauge(entry).ScaleColorLimits),
            (entry, value, line, col) =>
            {
                // [] and '' take the bands away; an empty cell or string is refused (probe u9_props).
                UiGaugeModel gauge = Gauge(entry);
                if ((value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
                    || (value.Type == JgsType.String && !value.IsCharMatrix && value.AsString.Length == 0))
                {
                    gauge.ScaleColorLimits = [];
                    gauge.ScaleColorLimitsManual = false;
                    return;
                }

                string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                if (value.Type is not (JgsType.Number or JgsType.Array) || value.IsStringArray || kind == "logical" || !IsNumericKind(kind)
                    || value.Type == JgsType.Number || value.Cols != 2 || value.Dims.Length != 2)
                {
                    throw UiError(entry, "invalidScaleColorLimits", "'ScaleColorLimits' must be a n-by-2 array, such as [0 10; 80 90; 90 100];", line, col);
                }

                double[] data = JgsBuiltins.ToDoubles("ScaleColorLimits", value, line, col);
                int rows = value.Rows;
                var bands = new double[2 * rows];
                for (int i = 0; i < rows; i++)
                {
                    bands[2 * i] = data[i];
                    bands[(2 * i) + 1] = data[rows + i];
                    if (!(bands[2 * i] < bands[(2 * i) + 1]))
                    {
                        throw UiError(entry, "nonIncreasingScaleColorLimits",
                            "'ScaleColorLimits' must be a n-by-2 array, where each element in the first column is less than the corresponding element in the second column.", line, col);
                    }
                }

                gauge.ScaleColorLimits = bands;
                gauge.ScaleColorLimitsManual = true;
            });
        if (table.ContainsKey("ScaleDirection"))
        {
            table.Remove("ScaleDirection");
        }

        Put(table, "BackgroundColor",
            entry => UiColorValue(Leaf(entry).BackgroundColor),
            (entry, value, line, col) => Leaf(entry).BackgroundColor = SolidColor(entry, "BackgroundColor", value, line, col));
    }

    /// <summary>The style-specific words of a gauge: <c>ScaleDirection</c> for the round ones, <c>Orientation</c> for the others.</summary>
    private static void AddGaugeWords(UiGaugeStyle style, IDictionary<string, GraphicsProperty> table)
    {
        if (style != UiGaugeStyle.Linear)
        {
            Put(table, "ScaleDirection",
                entry => JgsValue.Str(Gauge(entry).Clockwise ? "clockwise" : "counterclockwise"),
                (entry, value, line, col) => Gauge(entry).Clockwise = UiWord(entry, value, ScaleDirectionWords, "invalidScaleDirection",
                    "'ScaleDirection' value must be 'clockwise' or 'counterclockwise'.", line, col) == "clockwise");
            Options(table, "ScaleDirection", ScaleDirectionWords);
        }

        if (style != UiGaugeStyle.Circular)
        {
            string[] words = [.. UiGaugeModel.OrientationWords(style)];
            string sentence = style == UiGaugeStyle.Linear
                ? "'Orientation' value must be 'horizontal' or 'vertical'."
                : $"'Orientation' value must be {string.Join(", ", words.Take(words.Length - 1).Select(static w => $"'{w}'"))}, or '{words[^1]}'.";
            Put(table, "Orientation",
                entry => JgsValue.Str(Gauge(entry).Orientation),
                (entry, value, line, col) =>
                {
                    UiGaugeModel gauge = Gauge(entry);
                    string word = UiWord(entry, value, words, "invalidOrientation", sentence, line, col);
                    if (style == UiGaugeStyle.Linear && (word == "vertical") != (gauge.Orientation == "vertical"))
                    {
                        // A linear gauge turns about its corner, as a slider does.
                        Rect2D box = gauge.Position;
                        gauge.Position = new Rect2D(box.X, box.Y, box.Height, box.Width);
                    }

                    gauge.Orientation = word;
                });
            Options(table, "Orientation", words);
        }
    }

    /// <summary>
    /// <c>ScaleColors</c> as R2025b takes it: an n-by-3 array of RGB triplets within [0, 1], a cell
    /// or string array of colour names or hexadecimal codes, or an empty for none. A single name is
    /// refused, as there.
    /// </summary>
    private static IReadOnlyList<UiColor> ScaleColorsOf(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        JgsRuntimeException Refused() => UiError(entry, "invalidScaleColors",
            "'ScaleColors' value must be an n-by-3 array of RGB triplets, or a 1-D array of color names or hexadecimal color codes.", line, col);
        // [], '' and {} take the colours away; an empty string is refused (probe u9_props).
        if ((value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 0 && !value.IsStringArray)
            || (value.Type == JgsType.Cell && value.AsCell.Length == 0)
            || (value.Type == JgsType.String && !value.IsCharMatrix && value.AsString.Length == 0))
        {
            return [];
        }

        if (value.Type == JgsType.Cell || value.IsStringArray)
        {
            var named = new List<UiColor>();
            int count = value.Type == JgsType.Cell ? value.AsCell.Length : value.ArrayLength;
            if (value.Type == JgsType.Cell && value.Rows > 1 && value.Cols > 1)
            {
                throw Refused();
            }

            for (int i = 0; i < count; i++)
            {
                JgsValue one = value.Type == JgsType.Cell ? value.AsCell[i] : value.ElementAt(i);
                if (!JgsBuiltins.IsTextScalar(one) || NamedColor(JgsBuiltins.TextOf(one).Trim()) is not { } colour)
                {
                    throw Refused();
                }

                named.Add(colour);
            }

            return named;
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (value.Type != JgsType.Array || kind == "logical" || !IsNumericKind(kind) || value.Cols != 3 || value.Dims.Length != 2)
        {
            throw Refused();
        }

        double[] data = JgsBuiltins.ToDoubles("ScaleColors", value, line, col);
        int rows = value.Rows;
        var colors = new List<UiColor>(rows);
        for (int i = 0; i < rows; i++)
        {
            double r = data[i];
            double g = data[rows + i];
            double b = data[(2 * rows) + i];
            if (!(r >= 0 && r <= 1 && g >= 0 && g <= 1 && b >= 0 && b <= 1))
            {
                throw Refused();
            }

            colors.Add(new UiColor(r, g, b));
        }

        return colors;
    }

    // --- switch --------------------------------------------------------------------------------------

    private static void AddSwitchOrientation(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Orientation",
            entry => JgsValue.Str(((UiSwitchModel)entry.Target).Vertical ? "vertical" : "horizontal"),
            (entry, value, line, col) =>
            {
                var toggle = (UiSwitchModel)entry.Target;
                bool vertical = UiWord(entry, value, OrientationWords, "invalidOrientation",
                    "'Orientation' value must be 'horizontal' or 'vertical'.", line, col) == "vertical";
                if (vertical == toggle.Vertical)
                {
                    return;
                }

                Rect2D box = toggle.Position;
                toggle.Vertical = vertical;
                toggle.Position = new Rect2D(box.X, box.Y, box.Height, box.Width);
            });
        Options(table, "Orientation", OrientationWords);
    }

    // --- lamp ------------------------------------------------------------------------------------

    private static void AddLampBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Color",
            entry => UiColorValue(((UiLampModel)entry.Target).Color),
            (entry, value, line, col) => ((UiLampModel)entry.Target).Color = SolidColor(entry, "Color", value, line, col));
    }

    // --- colour picker ---------------------------------------------------------------------------

    private static UiColorPickerModel Picker(JgsHandleEntry entry) => (UiColorPickerModel)entry.Target;

    private static void AddColorPickerBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddNamedSlot(table, "ValueChangedFcn");
        Put(table, "Value",
            entry => UiColorValue(Picker(entry).Value),
            (entry, value, line, col) =>
            {
                // Three logicals are a colour here, as a component's colour never is elsewhere (probe u9_behave).
                if (value.Type == JgsType.Array && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "logical" && value.ArrayLength == 3)
                {
                    value = Row(JgsBuiltins.ToDoubles("Value", value, line, col));
                }

                Picker(entry).Value = SolidColor(entry, "Value", value, line, col);
            });
        Put(table, "Icon",
            entry => entry.UiCData ?? JgsValue.Str(Picker(entry).IconSource),
            (entry, value, line, col) =>
            {
                (UiImage? image, string source, JgsValue? kept) = IconOf(entry, value, line, col);
                Picker(entry).Icon = image;
                Picker(entry).IconSource = source;
                entry.UiCData = kept;
            });
        Put(table, "BackgroundColor",
            entry => UiColorValue(Leaf(entry).BackgroundColor),
            (entry, value, line, col) => Leaf(entry).BackgroundColor = SolidColor(entry, "BackgroundColor", value, line, col));
    }

    /// <summary>
    /// An <c>Icon</c> of a colour picker or a tree node (U9): a file that is an image, or an
    /// m-by-n-by-3 array; a name with an extension that is no file warns and leaves none
    /// (R2025b, probe <c>u9_behave</c>), a name with no extension is refused outright.
    /// </summary>
    private static (UiImage? Image, string Source, JgsValue? Kept) IconOf(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        JgsRuntimeException NotAFile() => UiError(entry, "invalidIconFile",
            "You have specified a file that cannot be found or is not an image. Specify a file name that is on the MATLAB path, or use a full or relative path.", line, col);
        if (value.IsCharMatrix)
        {
            throw UiError(entry, "MustBeChar", "Input must be a row vector of characters, or a string scalar, or a cellstr, or a string matrix.", line, col);
        }

        if (value.IsTime)
        {
            throw NotAFile();
        }

        if (value.Type == JgsType.String)
        {
            string text = MissingAsEmpty(value.AsString);
            if (text.Length == 0)
            {
                return (null, string.Empty, null);
            }

            // A colour picker has two words of its own (probe u9_extra): 'default' is no icon, and
            // 'text' shows the colour as text; either by any unambiguous beginning.
            if (entry.Target is UiColorPickerModel && Matching(text, ["default", "text"]) is { } word)
            {
                return (null, word == "text" ? "text" : string.Empty, null);
            }

            string path = JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host ? host.Resolve(text) : text;
            if (File.Exists(path) && JgsBuiltins.TryReadPicture(path) is { } read)
            {
                return (read, text, null);
            }

            if (Path.GetExtension(text).Length > 0)
            {
                PropertyWarning($"MATLAB:ui:{Cls(entry)}:invalidIconNotInPath", $"File '{text}' is not on the MATLAB or specified path.");
                return (null, string.Empty, null);
            }

            throw NotAFile();
        }

        if (IsEmptyDouble(value))
        {
            return (null, string.Empty, null);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (value.Type is JgsType.Cell or JgsType.Struct or JgsType.Function || value.IsStringArray || kind == "logical")
        {
            throw NotAFile();
        }

        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        if (kind is not ("double" or "single" or "uint8" or "uint16") || dims.Length != 3 || dims[2] != 3 || value.Type == JgsType.Complex)
        {
            throw UiError(entry, "invalidIconCData",
                "You have specified an invalid CData. Specify an RGB image as an m-by-n-by-3 array of type double, single, uint8, or uint16.", line, col);
        }

        double[] data = JgsBuiltins.ToDoubles("Icon", value, line, col);
        double high = kind switch
        {
            "uint8" => 255,
            "uint16" => 65535,
            _ => 1,
        };
        int rows = dims[0];
        int cols = dims[1];
        var pixels = new byte[rows * cols * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                byte Channel(int plane) => (byte)System.Math.Round(255 * System.Math.Clamp(data[r + (c * rows) + (plane * rows * cols)] / high, 0, 1));
                pixels[at] = Channel(2);
                pixels[at + 1] = Channel(1);
                pixels[at + 2] = Channel(0);
                pixels[at + 3] = 255;
            }
        }

        return (rows > 0 && cols > 0 ? new UiImage(cols, rows, pixels) : null, string.Empty, JgsValue.Share(value));
    }

    // --- date picker -----------------------------------------------------------------------------

    private static UiDatePickerModel Dates(JgsHandleEntry entry) => (UiDatePickerModel)entry.Target;

    /// <summary>A datetime value of whole days from the epoch, in the picker's display format: what every date property reads as.</summary>
    private static JgsValue DatesValue(UiDatePickerModel picker, IReadOnlyList<double?> days, int rows, int cols, string? format = null)
    {
        double[] ms = [.. days.Select(d => d is { } day ? day * JgsTime.MsPerDay : double.NaN)];
        JgsValue value = JgsMatrix.FromColumnMajor(ms, rows, cols);
        return value.MarkTime(new JgsTimeTag(JgsTimeKind.Datetime, format ?? picker.DisplayFormat));
    }

    /// <summary>
    /// A picker's <c>Value</c> as a script reads it. A <c>NaT</c> that <c>Limits</c> or
    /// <c>DisabledDaysOfWeek</c> left behind shows datetime's own default format until the value or
    /// the display format is written again (probe <c>u9_extra</c>).
    /// </summary>
    private static JgsValue DateValue(UiDatePickerModel picker) =>
        DatesValue(picker, [picker.ValueDays], 1, 1, picker.ValueDays is null && picker.NatInDefaultFormat ? JgsTime.DefaultDatetimeFormat : null);

    /// <summary>Takes a picker's value away, as a limit or a disabled weekday does.</summary>
    private static void UnsetDate(UiDatePickerModel picker, bool defaultFormat)
    {
        picker.ValueDays = null;
        picker.DisplayText = string.Empty;
        picker.NatInDefaultFormat = defaultFormat;
    }

    /// <summary>The whole days of a datetime value, time of day and zone dropped; null when the value is not a datetime.</summary>
    private static double[]? DaysOf(JgsValue value)
    {
        if (!value.IsDatetime)
        {
            return null;
        }

        double[] ms = JgsBuiltins.TimeMs(value);
        return [.. ms.Select(m => double.IsNaN(m) ? double.NaN : System.Math.Floor(m / JgsTime.MsPerDay))];
    }

    private static void AddDatePickerBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddNamedSlot(table, "ValueChangedFcn");
        Put(table, "DisplayFormat",
            entry => JgsValue.Str(Dates(entry).DisplayFormat),
            (entry, value, line, col) =>
            {
                // An empty character vector is refused as empty; an empty string goes on to be read
                // as a format and is refused as one that names no date (probe u9_props).
                UiDatePickerModel picker = Dates(entry);
                bool isString = value.IsStringArray && value.ArrayLength == 1;
                string text = isString ? MissingAsEmpty(JgsBuiltins.TextOf(value.ElementAt(0)))
                    : value.Type == JgsType.String && !value.IsCharMatrix ? MissingAsEmpty(value.AsString)
                    : throw UiError(entry, "displayFormatInvalid", "'DisplayFormat' value must be a nonempty character vector or string scalar.", line, col);
                if (text.Length == 0 && !isString)
                {
                    throw UiError(entry, "displayFormatInvalid", "'DisplayFormat' value must be a nonempty character vector or string scalar.", line, col);
                }

                picker.DisplayFormat = DateFormatOf(entry, text, line, col);
                picker.DisplayText = DateText(picker, picker.ValueDays);
                picker.NatInDefaultFormat = false;
            });
        Put(table, "Value",
            entry => DateValue(Dates(entry)),
            (entry, value, line, col) =>
            {
                UiDatePickerModel picker = Dates(entry);
                WarnTextAgainstDatetime(value);
                double[]? days = DaysOf(value);
                if (days is not { Length: 1 })
                {
                    throw UiError(entry, "valueNotValid", "'Value' must be a finite datetime object within the range of 'Limits' or NaT.", line, col);
                }

                if (double.IsNaN(days[0]))
                {
                    picker.ValueDays = null;
                }
                else if (days[0] < picker.LowerDays || days[0] > picker.UpperDays || days[0] < UiDatePickerModel.FirstDay || days[0] > UiDatePickerModel.LastDay)
                {
                    throw UiError(entry, "valueNotValid", "'Value' must be a finite datetime object within the range of 'Limits' or NaT.", line, col);
                }
                else if (picker.DisabledWeekdays.Contains(WeekdayOf(days[0])))
                {
                    throw UiError(entry, "valueConflictsWithDisabledDaysOfWeek", "'Value' must not be a day in 'DisabledDaysOfWeek'.", line, col);
                }
                else
                {
                    picker.ValueDays = days[0];
                }

                picker.DisplayText = DateText(picker, picker.ValueDays);
                picker.NatInDefaultFormat = false;
            });
        Put(table, "Limits",
            entry => DatesValue(Dates(entry), [Dates(entry).LowerDays, Dates(entry).UpperDays], 1, 2),
            (entry, value, line, col) =>
            {
                UiDatePickerModel picker = Dates(entry);
                WarnTextAgainstDatetime(value);
                double[]? days = DaysOf(value);
                if (days is not { Length: 2 } || days.Any(double.IsNaN) || !(days[0] <= days[1])
                    || days[0] < UiDatePickerModel.FirstDay || days[1] > UiDatePickerModel.LastDay)
                {
                    throw UiError(entry, "dateLimitsInvalid",
                        "'Limits' value must be a 1-by-2 datetime array that is increasing and within the years 0000 to 9999.", line, col);
                }

                picker.LowerDays = days[0];
                picker.UpperDays = days[1];
                if (picker.ValueDays is { } held && (held < days[0] || held > days[1]))
                {
                    UnsetDate(picker, defaultFormat: true);
                }
            });
        Put(table, "DisabledDates",
            entry =>
            {
                UiDatePickerModel picker = Dates(entry);
                return picker.DisabledDays.Count == 0
                    ? DatesValue(picker, [], picker.DisabledEmptyRows, picker.DisabledEmptyCols)
                    : DatesValue(picker, [.. picker.DisabledDays.Select(static d => (double?)d)], picker.DisabledDays.Count, 1);
            },
            (entry, value, line, col) =>
            {
                // Only a datetime, and only a vector of them; an empty one keeps its shape (probe
                // u9_extra). [] lands only where there are no disabled dates (SkipsEqualWrite).
                UiDatePickerModel picker = Dates(entry);
                WarnTextAgainstDatetime(value);
                double[]? days = value.Type == JgsType.Array && value.Rows > 1 && value.Cols > 1 ? null : DaysOf(value);
                if (days is null)
                {
                    throw UiError(entry, "disabledDatesInvalid", "'DisabledDates' value must be an array of datetime objects or datetime.empty().", line, col);
                }

                if (days.Length == 0)
                {
                    picker.DisabledDays = [];
                    picker.DisabledEmptyRows = value.Rows;
                    picker.DisabledEmptyCols = value.Cols;
                    return;
                }

                if (days.Any(double.IsNaN))
                {
                    throw UiError(entry, "disabledDatesContainsNaT", "'DisabledDaysOfWeek' value must not contain NaT.", line, col);
                }

                picker.DisabledDays = [.. days.Distinct().OrderBy(static d => d)];
                if (picker.ValueDays is { } held && picker.DisabledDays.Contains(held))
                {
                    UnsetDate(picker, defaultFormat: false);
                }
            });
        Put(table, "DisabledDaysOfWeek",
            entry =>
            {
                UiDatePickerModel picker = Dates(entry);
                return picker.DisabledWeekdays.Count == 0
                    ? JgsMatrix.FromColumnMajor([], 0, 0)
                    : JgsBuiltins.RowOfClass([.. picker.DisabledWeekdays.Select(static d => (double)d)], entry.DisabledDaysClass);
            },
            (entry, value, line, col) =>
            {
                UiDatePickerModel picker = Dates(entry);
                const string sentence = "'DisabledDaysOfWeek' value must be an array of numbers from 1 to 7, or an array of day names.";
                List<int> days = [];
                string kind = "double";
                if (IsEmptyValue(value) && !(value.IsStringArray && value.ArrayLength > 0))
                {
                    days = [];
                }
                else if (value.Type == JgsType.Cell || value.IsStringArray || value.Type == JgsType.String || value.IsCharMatrix)
                {
                    if (value.Type == JgsType.Cell && !Array.TrueForAll(value.AsCell, JgsBuiltins.IsTextScalar))
                    {
                        throw UiError(entry, "disabledDaysOfWeekInvalid", sentence, line, col);
                    }

                    int count = value.Type == JgsType.Cell ? value.AsCell.Length : value.IsStringArray ? value.ArrayLength : 1;
                    for (int i = 0; i < count; i++)
                    {
                        JgsValue one = value.Type == JgsType.Cell ? value.AsCell[i] : value.IsStringArray ? value.ElementAt(i) : value;
                        string name = one.IsCharMatrix ? ColumnMajorText(one) : JgsBuiltins.IsTextScalar(one) ? JgsBuiltins.TextOf(one) : string.Empty;
                        int at = Array.IndexOf(DayNames, name.Trim().ToLowerInvariant());
                        if (at < 0)
                        {
                            throw UiError(entry, "disabledDaysOfWeekInvalid", $"Unrecognized day name '{name}'.", line, col);
                        }

                        days.Add(at + 1);
                    }
                }
                else
                {
                    kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                    double[]? numbers = value.Type is JgsType.Number or JgsType.Array && kind != "logical" && IsNumericKind(kind)
                        && !(value.Rows > 1 && value.Cols > 1) ? RealNumbers(value) : null;
                    if (numbers is null || numbers.Any(static d => d < 1 || d > 7 || d != System.Math.Floor(d)))
                    {
                        throw UiError(entry, "disabledDaysOfWeekInvalid", sentence, line, col);
                    }

                    days = [.. numbers.Select(static d => (int)d)];
                }

                // A value on a weekday now disabled is taken away (probe u9_extra); it is writing
                // such a value that is refused.
                days = [.. days.Distinct().OrderBy(static d => d)];
                if (picker.ValueDays is { } held && days.Contains(WeekdayOf(held)))
                {
                    UnsetDate(picker, defaultFormat: true);
                }

                picker.DisabledWeekdays = days;
                entry.DisabledDaysClass = kind;
            });
        Put(table, "Editable",
            entry => OnOff(Dates(entry).Editable),
            (entry, value, line, col) => Dates(entry).Editable = OnOffState(entry, "Editable", value, line, col));
        Options(table, "Editable", OnOffWords);
        Put(table, "Placeholder",
            entry => JgsValue.Str(Dates(entry).Placeholder),
            (entry, value, line, col) => Dates(entry).Placeholder = OneLine(entry, value, Dates(entry).Placeholder, "invalidPlaceholder",
                "'Placeholder' must be a character vector or a string scalar.", line, col));
        Put(table, "BackgroundColor",
            entry => UiColorValue(Leaf(entry).BackgroundColor),
            (entry, value, line, col) => Leaf(entry).BackgroundColor = SolidColor(entry, "BackgroundColor", value, line, col));
    }

    /// <summary>MATLAB's weekday of a day from the epoch: 1 for Sunday through 7 for Saturday (the epoch was a Saturday).</summary>
    private static int WeekdayOf(double days) => (int)(((days % 7) + 7 + 6) % 7) + 1;

    /// <summary>The date a picker shows, in its display format; empty for <c>NaT</c>.</summary>
    internal static string DateText(UiDatePickerModel picker, double? days) =>
        days is { } day ? JgsTime.Format(day * JgsTime.MsPerDay, new JgsTimeTag(JgsTimeKind.Datetime, picker.DisplayFormat)) : string.Empty;

    /// <summary>The days a person's typing names, parsed in the picker's own format; null when it names none.</summary>
    internal static double? DateTyped(UiDatePickerModel picker, string text)
    {
        if (!JgsTime.TryParse(text, picker.DisplayFormat, out double ms) && !JgsTime.TryParse(text, null, out ms))
        {
            return null;
        }

        return double.IsNaN(ms) ? null : System.Math.Floor(ms / JgsTime.MsPerDay);
    }

    /// <summary>
    /// A <c>DisplayFormat</c> as R2025b takes it (probe <c>u9_matrix</c>): the letters of a date,
    /// with anything in quotes kept; a format that goes on to a time of day keeps the date alone;
    /// one that is a time alone, or empty, is refused; one with a letter that is neither is refused
    /// as unsupported.
    /// </summary>
    private static string DateFormatOf(JgsHandleEntry entry, string format, int line, int col)
    {
        const string dateLetters = "yuMdDeEQwWGL";
        const string timeLetters = "HhmsSakKzZxXOv";
        bool quoted = false;
        int cut = -1;
        bool anyDate = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '\'')
            {
                quoted = !quoted;
                continue;
            }

            if (quoted || !char.IsAsciiLetter(c))
            {
                continue;
            }

            if (dateLetters.Contains(c))
            {
                if (cut < 0)
                {
                    anyDate = true;
                }
            }
            else if (timeLetters.Contains(c))
            {
                if (cut < 0)
                {
                    cut = i;
                }
            }
            else
            {
                throw UiError(entry, "displayFormatInvalid", "'DisplayFormat' contains an unsupported format symbol.", line, col);
            }
        }

        if (!anyDate)
        {
            throw UiError(entry, "displayFormatInvalid",
                "'DisplayFormat' value specifies a time instead of a date. For more information on valid letter identifiers, see the documentation for DatePicker.DisplayFormat.", line, col);
        }

        return cut < 0 ? format : format[..cut].TrimEnd(' ', ':', '-', '/', '.', ',', 'T');
    }
}
