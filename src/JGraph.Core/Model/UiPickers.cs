using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// MATLAB's <c>uidatepicker</c> (<c>matlab.ui.control.DatePicker</c>; U9): a field showing one
/// date in a format, with a calendar to pick it from. Dates are kept as days since 1899-12-30,
/// the epoch the script side's <c>datetime</c> counts from, so that year 0 — R2025b's lower limit —
/// has a place; <c>NaT</c>, the value a picker starts with, is null. The text shown is worked out
/// on the script side, which owns the date formats, and kept here as <see cref="DisplayText"/>.
/// </summary>
public sealed class UiDatePickerModel : UiComponentModel
{
    /// <summary>R2025b's default <c>DisplayFormat</c>.</summary>
    public const string DefaultFormat = "dd-MMM-uuuu";

    /// <summary>31 December 9999, in days from the epoch (the upper limit a picker starts with).</summary>
    public static readonly double LastDay = System.Math.Floor((new DateTime(9999, 12, 31) - new DateTime(1899, 12, 30)).TotalDays);

    /// <summary>
    /// 1 January of year 0, in days from the epoch (the lower limit a picker starts with): the ten
    /// thousand years 0 to 9999 hold 3,652,425 days in the proleptic Gregorian calendar.
    /// </summary>
    public static readonly double FirstDay = LastDay - 3652424;

    private double? _valueDays;
    private string _displayFormat = DefaultFormat;
    private string _displayText = string.Empty;
    private double _lowerDays = FirstDay;
    private double _upperDays = LastDay;
    private IReadOnlyList<double> _disabledDays = [];
    private IReadOnlyList<int> _disabledWeekdays = [];
    private bool _editable = true;
    private string _placeholder = string.Empty;

    /// <summary>
    /// Whether a <c>NaT</c> value reads in datetime's own default format rather than the display
    /// format: so after <c>Limits</c> or <c>DisabledDaysOfWeek</c> took the value away (R2025b).
    /// </summary>
    [Browsable(false)]
    public bool NatInDefaultFormat { get; set; }

    /// <summary>The shape an empty <c>DisabledDates</c> was written with, which it reads back in (R2025b).</summary>
    [Browsable(false)]
    public int DisabledEmptyRows { get; set; }

    /// <inheritdoc cref="DisabledEmptyRows"/>
    [Browsable(false)]
    public int DisabledEmptyCols { get; set; }

    public UiDatePickerModel()
        : base("DatePicker", new Rect2D(100, 100, 150, 22))
    {
        BackgroundColor = White;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.DatePicker;

    /// <summary>The date, in whole days from the epoch; null for <c>NaT</c>.</summary>
    [Browsable(false)]
    public double? ValueDays
    {
        get => _valueDays;
        set => SetProperty(ref _valueDays, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>DisplayFormat</c>, in <c>datetime</c>'s letters.</summary>
    [Browsable(false)]
    public string DisplayFormat
    {
        get => _displayFormat;
        set => SetProperty(ref _displayFormat, string.IsNullOrEmpty(value) ? DefaultFormat : value, InvalidationKind.Ui);
    }

    /// <summary>The date as the field shows it, in the display format; empty for <c>NaT</c>.</summary>
    [Browsable(false)]
    public string DisplayText
    {
        get => _displayText;
        set => SetProperty(ref _displayText, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The first day that may be picked, in days from the epoch.</summary>
    [Browsable(false)]
    public double LowerDays
    {
        get => _lowerDays;
        set => SetProperty(ref _lowerDays, value, InvalidationKind.Ui);
    }

    /// <summary>The last day that may be picked.</summary>
    [Browsable(false)]
    public double UpperDays
    {
        get => _upperDays;
        set => SetProperty(ref _upperDays, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>DisabledDates</c>: days that cannot be picked, sorted, each once.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> DisabledDays
    {
        get => _disabledDays;
        set => SetProperty(ref _disabledDays, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>DisabledDaysOfWeek</c>: 1 for Sunday to 7 for Saturday, sorted, each once.</summary>
    [Browsable(false)]
    public IReadOnlyList<int> DisabledWeekdays
    {
        get => _disabledWeekdays;
        set => SetProperty(ref _disabledWeekdays, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>Whether a date may be typed into the field, or only picked from the calendar.</summary>
    [Browsable(false)]
    public bool Editable
    {
        get => _editable;
        set => SetProperty(ref _editable, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string Placeholder
    {
        get => _placeholder;
        set => SetProperty(ref _placeholder, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The epoch days are counted from.</summary>
    public static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>A day count as a <see cref="DateTime"/>, or null when it lies outside what one can hold (before year 1).</summary>
    public static DateTime? ToDateTime(double? days)
    {
        if (days is not { } count || double.IsNaN(count))
        {
            return null;
        }

        double ticks = (count * TimeSpan.TicksPerDay) + Epoch.Ticks;
        return ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ? null : new DateTime((long)ticks, DateTimeKind.Unspecified);
    }

    /// <summary>A date as a whole day count from the epoch.</summary>
    public static double FromDateTime(DateTime date) => System.Math.Floor((date.Date - Epoch).TotalDays);

    /// <summary>Whether a day may be picked: within the limits, not a disabled date, not on a disabled weekday.</summary>
    public bool Allows(double days)
    {
        if (days < _lowerDays || days > _upperDays || _disabledDays.Contains(days))
        {
            return false;
        }

        // Day 0 of the epoch, 30 December 1899, was a Saturday: weekday 7.
        int weekday = (int)(((days % 7) + 7 + 6) % 7) + 1;
        return !_disabledWeekdays.Contains(weekday);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Text = _displayText,
        Placeholder = _placeholder,
        Editable = _editable,
        HasNumber = _valueDays is not null,
        Number = _valueDays ?? 0,
        Min = _lowerDays,
        Max = _upperDays,
        Date = new UiDateFrame(_disabledDays, _disabledWeekdays),
    };
}

/// <summary>What a date picker's calendar greys out (U9).</summary>
public sealed record UiDateFrame(IReadOnlyList<double> DisabledDays, IReadOnlyList<int> DisabledWeekdays);

/// <summary>
/// MATLAB's <c>uicolorpicker</c> (<c>matlab.ui.control.ColorPicker</c>; U9): a button showing a
/// colour that opens a palette. Its <c>Value</c> is always a colour; <c>Icon</c> is a picture drawn
/// beside the swatch, or none.
/// </summary>
public sealed class UiColorPickerModel : UiComponentModel
{
    private UiColor _value = new(1, 0, 0);
    private UiImage? _icon;
    private string _iconSource = string.Empty;

    public UiColorPickerModel()
        : base("ColorPicker", new Rect2D(100, 100, 38, 22))
    {
        BackgroundColor = Grey;
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.ColorPicker;

    [Browsable(false)]
    public UiColor Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value, InvalidationKind.Ui);
    }

    /// <summary>The file <c>Icon</c> named, when it named one.</summary>
    [Browsable(false)]
    public string IconSource
    {
        get => _iconSource;
        set => SetProperty(ref _iconSource, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Color = _value,
        Image = _icon,
    };
}
