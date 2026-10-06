using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// A component with a numeric scale (app-building plan, U5 and U9): a slider, a knob, a gauge.
/// Each has a <c>Value</c>, <c>Limits</c>, and major and minor ticks with labels that are worked
/// out for it unless a script writes them. What differs — a slider's step and orientation, a
/// gauge's bands of colour — is the subclass's.
/// </summary>
public abstract class UiScaleModel : UiComponentModel
{
    private double _value;
    private double _lower;
    private double _upper = 100;
    private IReadOnlyList<double> _majorTicks = [0, 20, 40, 60, 80, 100];
    private IReadOnlyList<double> _minorTicks = [];
    private IReadOnlyList<string> _labels = ["0", "20", "40", "60", "80", "100"];
    private bool _majorManual;
    private bool _minorManual;
    private bool _labelsManual;

    protected UiScaleModel(string name, Rect2D position)
        : base(name, position)
    {
    }

    [Browsable(false)]
    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Lower
    {
        get => _lower;
        set => SetProperty(ref _lower, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public double Upper
    {
        get => _upper;
        set => SetProperty(ref _upper, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public IReadOnlyList<double> MajorTicks
    {
        get => _majorTicks;
        set => SetProperty(ref _majorTicks, value ?? [], InvalidationKind.Ui);
    }

    [Browsable(false)]
    public IReadOnlyList<double> MinorTicks
    {
        get => _minorTicks;
        set => SetProperty(ref _minorTicks, value ?? [], InvalidationKind.Ui);
    }

    [Browsable(false)]
    public IReadOnlyList<string> MajorTickLabels
    {
        get => _labels;
        set => SetProperty(ref _labels, value ?? [], InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool MajorTicksManual
    {
        get => _majorManual;
        set => SetProperty(ref _majorManual, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool MinorTicksManual
    {
        get => _minorManual;
        set => SetProperty(ref _minorManual, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool MajorTickLabelsManual
    {
        get => _labelsManual;
        set => SetProperty(ref _labelsManual, value, InvalidationKind.Ui);
    }

    /// <summary>The part of a snapshot every scale shares.</summary>
    protected UiComponentFrame ScaleFrame() => Common() with
    {
        Number = _value,
        Min = _lower,
        Max = _upper,
        MajorTicks = _majorTicks,
        MinorTicks = _minorTicks,
        TickLabels = _labels,
    };

    /// <summary>The widest of the tick labels in the component's font, in pixels.</summary>
    public double WidestLabel() => _labels.Count == 0
        ? 0
        : _labels.Max(label => UiFit.TextWidth([label], FontName, FontSize, Bold, Italic));
}

/// <summary>
/// MATLAB's <c>uiknob</c> (<c>matlab.ui.control.Knob</c>; U9): a dial turned through three
/// quarters of a circle, its ticks and labels round the outside. Its <c>Position</c> is the dial;
/// what the labels take is its <c>OuterPosition</c>.
/// </summary>
public sealed class UiKnobModel : UiScaleModel
{
    /// <summary>The gap between the dial and its tick ring, and between the ring and the labels.</summary>
    public const double TickGap = 4;

    /// <summary>The length of a major tick mark.</summary>
    public const double MajorTickLength = 7;

    public UiKnobModel()
        : base("Knob", new Rect2D(100, 100, 60, 60))
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Knob;

    /// <summary>
    /// The rectangle the dial, its ticks and its labels take together: the dial grown by the ring
    /// and by the widest label on each side, and by a line of text above and below. R2025b's own
    /// rule was not measured (its labels are placed by its view); this is where this build puts them.
    /// </summary>
    public Rect2D OuterOf(Rect2D dial)
    {
        double ring = TickGap + MajorTickLength + TickGap;
        double across = WidestLabel() + ring;
        double down = UiFit.LineHeight(FontSize) + ring;
        return new Rect2D(dial.X - across, dial.Y - down, dial.Width + (2 * across), dial.Height + (2 * down));
    }

    /// <inheritdoc />
    public override Rect2D InCell(Rect2D cell) => UiFit.ShapeInCell(cell, 1, OuterOf);

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => ScaleFrame();
}

/// <summary>The four looks of a <c>uigauge</c>, each a class of its own in MATLAB.</summary>
public enum UiGaugeStyle
{
    Circular,
    Linear,
    NinetyDegree,
    Semicircular,
}

/// <summary>
/// MATLAB's <c>uigauge</c> (U9): a scale with a needle and no input, in one of four shapes
/// (<see cref="UiGaugeStyle"/>). Bands of colour along the scale are <c>ScaleColors</c> and
/// <c>ScaleColorLimits</c>; the round ones have a <c>ScaleDirection</c>, the others an
/// <c>Orientation</c>. Defaults are R2025b's (probe <c>u9_matrix</c>).
/// </summary>
public class UiGaugeModel : UiScaleModel
{
    private IReadOnlyList<UiColor> _scaleColors = [];
    private IReadOnlyList<double> _scaleColorLimits = [];
    private bool _scaleColorLimitsManual;
    private bool _clockwise = true;
    private string _orientation;

    public UiGaugeModel()
        : this(UiGaugeStyle.Circular)
    {
    }

    protected UiGaugeModel(UiGaugeStyle style)
        : base(NameOf(style), DefaultRect(style))
    {
        Style = style;
        _orientation = DefaultOrientation(style);
        BackgroundColor = White;
        if (style == UiGaugeStyle.NinetyDegree)
        {
            MajorTicks = [0, 50, 100];
            MajorTickLabels = ["0", "50", "100"];
        }
    }

    /// <summary>Which of the four shapes this is; fixed when the gauge is made.</summary>
    [Browsable(false)]
    public UiGaugeStyle Style { get; init; }

    /// <inheritdoc />
    public override UiComponentKind Kind => Style switch
    {
        UiGaugeStyle.Linear => UiComponentKind.LinearGauge,
        UiGaugeStyle.NinetyDegree => UiComponentKind.NinetyDegreeGauge,
        UiGaugeStyle.Semicircular => UiComponentKind.SemicircularGauge,
        _ => UiComponentKind.Gauge,
    };

    /// <inheritdoc />
    public override Rect2D InCell(Rect2D cell) => Style switch
    {
        UiGaugeStyle.Linear => cell,
        UiGaugeStyle.Semicircular => UiFit.ShapeInCell(cell, 120.0 / 65, static box => box),
        _ => UiFit.ShapeInCell(cell, 1, static box => box),
    };

    /// <summary>The colours of the bands along the scale, in order.</summary>
    [Browsable(false)]
    public IReadOnlyList<UiColor> ScaleColors
    {
        get => _scaleColors;
        set => SetProperty(ref _scaleColors, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>Where each band lies, as pairs of scale values laid end to end: low, high, low, high, ….</summary>
    [Browsable(false)]
    public IReadOnlyList<double> ScaleColorLimits
    {
        get => _scaleColorLimits;
        set => SetProperty(ref _scaleColorLimits, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>
    /// Whether a script wrote <c>ScaleColorLimits</c>; until it does, the bands are equal shares of
    /// the scale, worked out again whenever the colours or the limits change (probe <c>u9_behave</c>).
    /// </summary>
    [Browsable(false)]
    public bool ScaleColorLimitsManual
    {
        get => _scaleColorLimitsManual;
        set => SetProperty(ref _scaleColorLimitsManual, value, InvalidationKind.None);
    }

    /// <summary>MATLAB's <c>ScaleDirection</c>: whether the scale runs clockwise (a round gauge's).</summary>
    [Browsable(false)]
    public bool Clockwise
    {
        get => _clockwise;
        set => SetProperty(ref _clockwise, value, InvalidationKind.Ui);
    }

    /// <summary>
    /// MATLAB's <c>Orientation</c> word: a linear gauge's <c>horizontal</c> or <c>vertical</c>, a
    /// ninety-degree gauge's corner (<c>northwest</c>, …), a semicircular gauge's side (<c>north</c>, …).
    /// </summary>
    [Browsable(false)]
    public string Orientation
    {
        get => _orientation;
        set => SetProperty(ref _orientation, value ?? DefaultOrientation(Style), InvalidationKind.Ui);
    }

    /// <summary>The words <c>Orientation</c> takes for a style; none for the circular gauge.</summary>
    public static IReadOnlyList<string> OrientationWords(UiGaugeStyle style) => style switch
    {
        UiGaugeStyle.Linear => ["horizontal", "vertical"],
        UiGaugeStyle.NinetyDegree => ["northwest", "northeast", "southwest", "southeast"],
        UiGaugeStyle.Semicircular => ["north", "south", "east", "west"],
        _ => [],
    };

    private static string DefaultOrientation(UiGaugeStyle style) => style switch
    {
        UiGaugeStyle.Linear => "horizontal",
        UiGaugeStyle.NinetyDegree => "northwest",
        UiGaugeStyle.Semicircular => "north",
        _ => string.Empty,
    };

    private static string NameOf(UiGaugeStyle style) => style switch
    {
        UiGaugeStyle.Linear => "LinearGauge",
        UiGaugeStyle.NinetyDegree => "NinetyDegreeGauge",
        UiGaugeStyle.Semicircular => "SemicircularGauge",
        _ => "Gauge",
    };

    private static Rect2D DefaultRect(UiGaugeStyle style) => style switch
    {
        UiGaugeStyle.Linear => new Rect2D(100, 100, 120, 40),
        UiGaugeStyle.NinetyDegree => new Rect2D(100, 100, 90, 90),
        UiGaugeStyle.Semicircular => new Rect2D(100, 100, 120, 65),
        _ => new Rect2D(100, 100, 120, 120),
    };

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => ScaleFrame() with
    {
        Colors = _scaleColors,
        ColorLimits = _scaleColorLimits,
        Checked = _clockwise,
        Word = _orientation,
    };
}

/// <summary>MATLAB's <c>uigauge(…, 'linear')</c> (<c>matlab.ui.control.LinearGauge</c>).</summary>
public sealed class UiLinearGaugeModel : UiGaugeModel
{
    public UiLinearGaugeModel()
        : base(UiGaugeStyle.Linear)
    {
    }
}

/// <summary>MATLAB's <c>uigauge(…, 'ninetydegree')</c> (<c>matlab.ui.control.NinetyDegreeGauge</c>).</summary>
public sealed class UiNinetyDegreeGaugeModel : UiGaugeModel
{
    public UiNinetyDegreeGaugeModel()
        : base(UiGaugeStyle.NinetyDegree)
    {
    }
}

/// <summary>MATLAB's <c>uigauge(…, 'semicircular')</c> (<c>matlab.ui.control.SemicircularGauge</c>).</summary>
public sealed class UiSemicircularGaugeModel : UiGaugeModel
{
    public UiSemicircularGaugeModel()
        : base(UiGaugeStyle.Semicircular)
    {
    }
}

/// <summary>
/// MATLAB's <c>uiknob(…, 'discrete')</c> (<c>matlab.ui.control.DiscreteKnob</c>; U9): a dial
/// with a position for each of its <c>Items</c>, which it shares with a drop-down's rules for
/// <c>Items</c>, <c>ItemsData</c>, <c>Value</c> and <c>ValueIndex</c>.
/// </summary>
public sealed class UiDiscreteKnobModel : UiItemsModel
{
    public UiDiscreteKnobModel()
        : base("DiscreteKnob", new Rect2D(100, 100, 60, 60), ["Off", "Low", "Medium", "High"])
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.DiscreteKnob;

    /// <summary>The dial grown by the widest item on each side and a line of text above and below (see <see cref="UiKnobModel.OuterOf"/>).</summary>
    public Rect2D OuterOf(Rect2D dial)
    {
        double ring = UiKnobModel.TickGap + UiKnobModel.MajorTickLength + UiKnobModel.TickGap;
        double widest = Items.Count == 0 ? 0 : Items.Max(item => UiFit.TextWidth([item], FontName, FontSize, Bold, Italic));
        double across = widest + ring;
        double down = UiFit.LineHeight(FontSize) + ring;
        return new Rect2D(dial.X - across, dial.Y - down, dial.Width + (2 * across), dial.Height + (2 * down));
    }

    /// <inheritdoc />
    public override Rect2D InCell(Rect2D cell) => UiFit.ShapeInCell(cell, 1, OuterOf);

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Items = Items,
        Selected = Selected,
    };
}

/// <summary>The three looks of a <c>uiswitch</c>, each a class of its own in MATLAB.</summary>
public enum UiSwitchStyle
{
    Slider,
    Rocker,
    Toggle,
}

/// <summary>
/// MATLAB's <c>uiswitch</c> (U9): a two-position control whose <c>Items</c> are exactly two
/// words, shown at its ends. A slider switch lies down and the other two stand up unless
/// <c>Orientation</c> says otherwise, and turning one about exchanges its width and height, as a
/// slider's does.
/// </summary>
public sealed class UiSwitchModel : UiItemsModel
{
    /// <summary>The gap between the switch and each of its labels.</summary>
    public const double LabelGap = 5;

    private bool _vertical;

    public UiSwitchModel()
        : this(UiSwitchStyle.Slider)
    {
    }

    public UiSwitchModel(UiSwitchStyle style)
        : base(NameOf(style), style == UiSwitchStyle.Slider ? new Rect2D(100, 100, 45, 20) : new Rect2D(100, 100, 20, 45), ["Off", "On"])
    {
        Style = style;
        _vertical = style != UiSwitchStyle.Slider;
    }

    /// <summary>Which of the three looks this is; fixed when the switch is made.</summary>
    [Browsable(false)]
    public UiSwitchStyle Style { get; init; }

    /// <inheritdoc />
    public override UiComponentKind Kind => Style switch
    {
        UiSwitchStyle.Rocker => UiComponentKind.RockerSwitch,
        UiSwitchStyle.Toggle => UiComponentKind.ToggleSwitch,
        _ => UiComponentKind.Switch,
    };

    /// <summary>MATLAB's <c>Orientation</c>: whether the switch stands up, its labels above and below.</summary>
    [Browsable(false)]
    public bool Vertical
    {
        get => _vertical;
        set => SetProperty(ref _vertical, value, InvalidationKind.Ui);
    }

    /// <summary>Whether the switch is in its second position.</summary>
    [Browsable(false)]
    public bool IsOn => Selected.Count > 0 && Selected[0] == 1;

    /// <summary>The switch grown by its two labels: beside it lying down, above and below it standing up.</summary>
    public Rect2D OuterOf(Rect2D box)
    {
        double first = Items.Count > 0 ? UiFit.TextWidth([Items[0]], FontName, FontSize, Bold, Italic) : 0;
        double second = Items.Count > 1 ? UiFit.TextWidth([Items[1]], FontName, FontSize, Bold, Italic) : 0;
        double line = UiFit.LineHeight(FontSize);
        return _vertical
            ? new Rect2D(box.X, box.Y - line - LabelGap, box.Width, box.Height + (2 * (line + LabelGap)))
            : new Rect2D(box.X - first - LabelGap, box.Y, box.Width + first + second + (2 * LabelGap), box.Height);
    }

    /// <inheritdoc />
    public override Rect2D InCell(Rect2D cell) => UiFit.ShapeInCell(cell, _vertical ? 20.0 / 45 : 45.0 / 20, OuterOf);

    private static string NameOf(UiSwitchStyle style) => style switch
    {
        UiSwitchStyle.Rocker => "RockerSwitch",
        UiSwitchStyle.Toggle => "ToggleSwitch",
        _ => "Switch",
    };

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Items = Items,
        Selected = Selected,
        Upright = _vertical,
        Checked = IsOn,
    };
}

/// <summary>MATLAB's <c>uilamp</c> (U9): a circle of one colour, with no font and no input.</summary>
public sealed class UiLampModel : UiComponentModel
{
    private UiColor _color = new(0, 1, 0);

    public UiLampModel()
        : base("Lamp", new Rect2D(100, 100, 20, 20))
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Lamp;

    /// <inheritdoc />
    public override Rect2D InCell(Rect2D cell) => UiFit.ShapeInCell(cell, 1, static box => box);

    [Browsable(false)]
    public UiColor Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with { Color = _color };
}
