using System.Globalization;
using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a person did to a <c>uifigure</c> component, turned into the model's new state and the
/// callback that is owed (app-building plan, U5). It runs on the script thread as the event leaves
/// the queue. The value is written first and always — the person did it, and a busy or cancelled
/// callback does not undo it — and only then is the callback's event data made, with the value and
/// the one before it.
/// <para>
/// R2025b cannot be driven headless, so the event data here follow its class definitions (research
/// B's metaclass dumps: <c>ValueChangedData</c> has <c>Value</c> and <c>PreviousValue</c>,
/// <c>ValueChangingData</c> has <c>Value</c>, the rest have only <c>Source</c> and
/// <c>EventName</c>) and MathWorks' documentation for the event names.
/// </para>
/// </summary>
internal static partial class JgsUiComponentEvents
{
    /// <summary>The actions a window reports, and the callback property each is owed.</summary>
    public const string Value = "value";
    public const string Changing = "changing";
    public const string Pushed = "pushed";
    public const string Clicked = "clicked";
    public const string DoubleClicked = "doubleclicked";
    public const string Opening = "opening";
    public const string ImageClicked = "image";
    public const string LinkClicked = "link";

    private const string Prefix = "matlab.ui.eventdata.";

    /// <summary>
    /// Applies a component event to the model and answers the event to deliver, or null when no
    /// callback is owed. The answer's <c>Action</c> is the callback property's name and its
    /// <c>Interim</c> the event data.
    /// </summary>
    public static GraphicsEvent? Prepare(GraphicsEvent raised)
    {
        // A callback whose event data is made already is owed as it stands (U8).
        if (IsOwedAsItStands(raised))
        {
            return raised.Target.BeingDeleted ? null : raised;
        }

        if (raised.Target is UiTabGroupModel or UiToolModel)
        {
            return PrepareBars(raised);
        }

        if (raised.Target is not UiComponentModel { BeingDeleted: false } component
            || !JgsHandleRegistry.TryGetEntry(component, out JgsHandleEntry? entry))
        {
            return null;
        }

        JgsValue source = JgsHandleRegistry.For(component);
        if (component is UiTableModel table)
        {
            return PrepareTable(raised, table, entry, source);
        }

        if (component is UiTreeModel tree)
        {
            return PrepareTree(raised, tree, entry, source);
        }

        switch (raised.Action)
        {
            case Value:
                return ApplyValue(raised, component, entry, source);

            case Changing:
                return raised with
                {
                    Action = "ValueChangingFcn",
                    Interim = JgsUiEventData.Make(Prefix + "ValueChangingData", source, "ValueChanging", new()
                    {
                        ["Value"] = InterimValue(component, raised.Interim),
                    }),
                };

            case Pushed:
                return raised with { Action = "ButtonPushedFcn", Interim = JgsUiEventData.Make(Prefix + "ButtonPushedData", source, "ButtonPushed") };

            case ImageClicked:
                return raised with { Action = "ImageClickedFcn", Interim = JgsUiEventData.Make(Prefix + "ImageClickedData", source, "ImageClicked") };

            case LinkClicked:
                if (component is UiHyperlinkModel link)
                {
                    link.WasVisited = true;
                }

                return raised with { Action = "HyperlinkClickedFcn", Interim = JgsUiEventData.Make(Prefix + "HyperlinkClickedData", source, "HyperlinkClicked") };

            case Opening:
                return raised with { Action = "DropDownOpeningFcn", Interim = JgsUiEventData.Make(Prefix + "DropDownOpeningData", source, "DropDownOpening") };

            case Clicked or DoubleClicked:
            {
                bool twice = raised.Action == DoubleClicked;
                JgsValue information = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Item"] = raised.Interim is int item && item >= 0 ? JgsValue.Number(item + 1) : JgsMatrix.FromColumnMajor([], 0, 0),
                    ["ScreenLocation"] = JgsMatrix.FromColumnMajor([], 0, 0),
                    ["Location"] = JgsMatrix.FromColumnMajor([], 0, 0),
                });
                return raised with
                {
                    Action = twice ? "DoubleClickedFcn" : "ClickedFcn",
                    Interim = JgsUiEventData.Make(Prefix + (twice ? "DoubleClickedData" : "ClickedData"), source,
                        twice ? "DoubleClicked" : "Clicked", new() { ["InteractionInformation"] = information }),
                };
            }

            default:
                return null;
        }
    }

    /// <summary>A value on its way, as the callback sees it: a number, two for a range, or the text typed so far.</summary>
    private static JgsValue InterimValue(UiComponentModel component, object? interim) => interim switch
    {
        double number => JgsValue.Number(number),
        double[] pair => JgsGraphicsProperties.Row(pair),
        string text when component is UiTextAreaModel => LinesCell(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')),
        string text => JgsValue.Str(text),
        _ => JgsMatrix.FromColumnMajor([], 0, 0),
    };

    private static JgsValue LinesCell(IReadOnlyList<string> lines)
    {
        JgsValue cell = JgsValue.Cell([.. lines.Select(static l => JgsValue.Str(l))]);
        cell.Reshape(lines.Count, 1);
        return cell;
    }

    /// <summary>What <c>Value</c> reads as now, through the component's own property.</summary>
    private static JgsValue Read(JgsHandleEntry entry) => JgsGraphicsProperties.Get(entry, "Value", 0, 0);

    /// <summary>
    /// Writes the value a person gave into the model. What the model will not hold — text that is
    /// not a number, a number outside the limits — is not written, and the window is told to show
    /// the model's value again. Answers the <c>ValueChangedFcn</c> event when the value changed.
    /// </summary>
    private static GraphicsEvent? ApplyValue(GraphicsEvent raised, UiComponentModel component, JgsHandleEntry entry, JgsValue source)
    {
        // A radio or toggle button has no callback of its own: its group hears of the new selection.
        if (component is UiRadioButtonModel or UiToggleButtonModel)
        {
            GraphicsEvent? selection = null;
            if (component.Parent is UiButtonGroupModel group && raised.UserValue is true && component is UiCaptionModel pressed && !pressed.Value)
            {
                UiCaptionModel? old = JgsBuiltins.SelectedButton(group);
                foreach (UiCaptionModel other in group.Components.OfType<UiCaptionModel>())
                {
                    if (other is UiRadioButtonModel or UiToggleButtonModel)
                    {
                        other.Value = ReferenceEquals(other, pressed);
                    }
                }

                selection = new GraphicsEvent(GraphicsEventKind.GroupSelectionChanged, group, Clicked: pressed, ContextObject: old);
            }

            component.UserWriteSeq = raised.UserSeq;
            return selection;
        }

        JgsValue before = Read(entry);
        switch (component)
        {
            case UiCaptionModel caption when raised.UserValue is bool on:
                caption.Value = on;
                break;

            case UiEditFieldModel field when raised.UserValue is string text:
                if (field.Accepts(text))
                {
                    field.Value = text;
                }

                break;

            case UiNumericModel numeric:
                ApplyNumber(numeric, raised.UserValue);
                break;

            case UiTextAreaModel area when raised.UserValue is string[] lines:
                area.Lines = lines;
                break;

            case UiDropDownModel drop when raised.UserValue is int index:
                if (index >= 0 && index < drop.Items.Count)
                {
                    drop.Typed = null;
                    drop.Selected = [index];
                }

                break;

            case UiDropDownModel drop when raised.UserValue is string typed:
            {
                int at = drop.Items.ToList().IndexOf(typed);
                if (at >= 0)
                {
                    drop.Typed = null;
                    drop.Selected = [at];
                }
                else if (drop.Editable)
                {
                    drop.Typed = typed;
                }

                break;
            }

            case UiListBoxModel list when raised.UserValue is int[] picked:
                list.Selected = [.. picked.Where(i => i >= 0 && i < list.Items.Count).Take(list.Multiselect ? int.MaxValue : 1)];
                break;

            // U9: a knob turned, a switch or a discrete knob set, a date picked or typed, a colour chosen.
            case UiKnobModel knob when raised.UserValue is double turned:
                knob.Value = System.Math.Clamp(turned, knob.Lower, knob.Upper);
                break;

            case UiDiscreteKnobModel or UiSwitchModel when raised.UserValue is int chosen && component is UiItemsModel picks:
                if (chosen >= 0 && chosen < picks.Items.Count)
                {
                    picks.Selected = [chosen];
                }

                break;

            case UiDatePickerModel picker:
                ApplyDate(picker, raised.UserValue);
                break;

            case UiColorPickerModel colours when raised.UserValue is UiColor chosenColour:
                colours.Value = chosenColour;
                break;

            case UiRangeSliderModel range when raised.UserValue is double[] { Length: 2 } pair:
                range.Value = System.Math.Clamp(System.Math.Min(pair[0], pair[1]), range.Lower, range.Upper);
                range.High = System.Math.Clamp(System.Math.Max(pair[0], pair[1]), range.Lower, range.Upper);
                break;

            case UiSliderModel slider when raised.UserValue is double moved:
                slider.Value = System.Math.Clamp(moved, slider.Lower, slider.Upper);
                break;
        }

        component.UserWriteSeq = raised.UserSeq;
        JgsValue after = Read(entry);
        if (JgsStdlib.DeepEquals(before, after, nanEqual: true))
        {
            return null;
        }

        return raised with
        {
            UserValue = null,
            Action = "ValueChangedFcn",
            Interim = JgsUiEventData.Make(Prefix + "ValueChangedData", source, "ValueChanged", new()
            {
                ["Value"] = after,
                ["PreviousValue"] = before,
            }),
        };
    }

    /// <summary>A numeric field's new value from what was typed, or from its arrows.</summary>
    private static void ApplyNumber(UiNumericModel field, object? given)
    {
        double? number;
        switch (given)
        {
            case double direct:
                number = direct;
                break;

            case string text when text.Trim().Length == 0:
                if (field.AllowEmpty)
                {
                    field.Value = null;
                    field.DisplayText = string.Empty;
                }

                return;

            case string text:
                number = ParseTyped(text);
                break;

            default:
                return;
        }

        if (number is not { } typed || double.IsNaN(typed))
        {
            return;
        }

        // The arrows stop at a limit; a number typed outside the limits is not taken.
        double stored = field.Rounded(given is double ? field.Clamped(typed) : typed);
        if (!field.InRange(stored))
        {
            return;
        }

        field.Value = stored;
        field.DisplayText = JgsGraphicsProperties.DisplayOf(field);
    }

    /// <summary>
    /// A date picker's new date (U9): a day from its calendar, or text typed in its own format;
    /// nothing typed is <c>NaT</c>. A day the picker does not allow, or text that is no date, is not
    /// taken, and the field shows the date it had.
    /// </summary>
    private static void ApplyDate(UiDatePickerModel picker, object? given)
    {
        double? days;
        bool readable = true;
        switch (given)
        {
            case double direct:
                days = direct;
                break;
            case string text when text.Trim().Length == 0:
                days = null;
                break;
            case string text:
                days = JgsGraphicsProperties.DateTyped(picker, text);
                readable = days is not null;
                break;
            default:
                return;
        }

        if (!readable || (days is { } day && !picker.Allows(day)))
        {
            picker.DisplayText = JgsGraphicsProperties.DateText(picker, picker.ValueDays);
            return;
        }

        picker.ValueDays = days;
        picker.DisplayText = JgsGraphicsProperties.DateText(picker, days);
    }

    /// <summary>The number a person typed: digits, a sign, a decimal point, an exponent, <c>Inf</c>.</summary>
    private static double? ParseTyped(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Equals("inf", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("+inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.PositiveInfinity;
        }

        if (trimmed.Equals("-inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.NegativeInfinity;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
    }

    /// <summary>
    /// A button of a dialog over a figure was pressed. A progress dialog's Cancel only raises its
    /// flag; an alert or a confirmation is taken away, its answer kept, and its <c>CloseFcn</c>
    /// answered — the callback and its event data, or null for none.
    /// </summary>
    public static (JgsValue Callback, JgsValue EventData, FigureModel Figure)? Answer(GraphicsEvent raised)
    {
        if (raised.Target is not UiOverlayModel overlay || overlay.Parent is not FigureModel figure
            || !JgsHandleRegistry.TryGetEntry(overlay, out JgsHandleEntry? entry))
        {
            return null;
        }

        if (overlay.Kind == UiOverlayKind.Progress)
        {
            overlay.CancelRequested = true;
            return null;
        }

        int option = raised.UserValue is int pressed && pressed >= 0 && pressed < overlay.Options.Count ? pressed : overlay.CancelOption;
        entry.OverlayAnswer = option;
        JgsValue? callback = entry.OverlayCloseFcn;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Source"] = JgsHandleRegistry.For(figure),
            ["EventName"] = JgsValue.Str(overlay.Kind == UiOverlayKind.Alert ? "AlertDialogClosed" : "ConfirmDialogClosed"),
            ["DialogTitle"] = JgsValue.Str(overlay.Title),
        };
        if (overlay.Kind == UiOverlayKind.Confirm)
        {
            fields["SelectedOptionIndex"] = JgsValue.Number(option + 1);
            fields["SelectedOption"] = JgsValue.Str(overlay.Options[option]);
        }

        JgsBuiltins.RemoveOverlay(overlay);
        return callback is null ? null : (callback, JgsValue.Struct(fields), figure);
    }
}
