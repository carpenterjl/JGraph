using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering.Layout;

/// <summary>
/// The rectangles of the components that keep their shape (U9, ADR 0207): a knob's dial sits
/// inside a ring of ticks and labels, a switch between its two captions, and a lamp and a round
/// gauge keep their proportions. From a frame alone, so the layout and the window agree without
/// touching the model: <see cref="OuterOf"/> grows a component's own rectangle to what it draws
/// in, and <see cref="InCell"/> is the component's own rectangle within a grid's cell, which is
/// the largest of its shape whose outer rectangle still fits (<see cref="UiFit.ShapeInCell"/>).
/// </summary>
public static class UiShapedCells
{
    /// <summary>The rectangle a component draws in: its own, grown by what lies outside it.</summary>
    public static Rect2D OuterOf(UiComponentFrame frame, Rect2D box)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double size = frame.FontSize;
        double Width(string text) => UiFit.TextWidth([text], frame.FontName, size, frame.Bold, frame.Italic);
        double line = UiFit.LineHeight(size);
        switch (frame.Kind)
        {
            case UiComponentKind.Knob:
            case UiComponentKind.DiscreteKnob:
            {
                IReadOnlyList<string> labels = frame.Kind == UiComponentKind.Knob ? frame.TickLabels : frame.Items;
                double ring = UiKnobModel.TickGap + UiKnobModel.MajorTickLength + UiKnobModel.TickGap;
                double across = (labels.Count == 0 ? 0 : labels.Max(Width)) + ring;
                double down = line + ring;
                return new Rect2D(box.X - across, box.Y - down, box.Width + (2 * across), box.Height + (2 * down));
            }

            case UiComponentKind.Switch:
            case UiComponentKind.RockerSwitch:
            case UiComponentKind.ToggleSwitch:
            {
                double first = frame.Items.Count > 0 ? Width(frame.Items[0]) : 0;
                double second = frame.Items.Count > 1 ? Width(frame.Items[1]) : 0;
                return frame.Upright
                    ? new Rect2D(box.X, box.Y - line - UiSwitchModel.LabelGap, box.Width, box.Height + (2 * (line + UiSwitchModel.LabelGap)))
                    : new Rect2D(box.X - first - UiSwitchModel.LabelGap, box.Y, box.Width + first + second + (2 * UiSwitchModel.LabelGap), box.Height);
            }

            default:
                return box;
        }
    }

    /// <summary>The width over the height a kind keeps, or null for one that fills what it is given.</summary>
    public static double? AspectRatioOf(UiComponentFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Kind switch
        {
            UiComponentKind.Knob or UiComponentKind.DiscreteKnob or UiComponentKind.Lamp
                or UiComponentKind.Gauge or UiComponentKind.NinetyDegreeGauge => 1,
            UiComponentKind.SemicircularGauge => 120.0 / 65,
            UiComponentKind.Switch or UiComponentKind.RockerSwitch or UiComponentKind.ToggleSwitch => frame.Upright ? 20.0 / 45 : 45.0 / 20,
            _ => null,
        };
    }

    /// <summary>
    /// A component's own rectangle within a grid's cell: a slider's track, the largest of a shaped
    /// kind's shape whose outer rectangle fits, and the whole cell for everything else.
    /// </summary>
    public static Rect2D InCell(UiComponentFrame frame, Rect2D cell)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Kind is UiComponentKind.Slider or UiComponentKind.RangeSlider)
        {
            return UiSliderModel.TrackInCell(cell, frame.Upright);
        }

        return AspectRatioOf(frame) is { } ratio
            ? UiFit.ShapeInCell(cell, ratio, box => OuterOf(frame, box))
            : cell;
    }
}
