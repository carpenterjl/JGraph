using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>An RGB colour held the way MATLAB holds a component's: three doubles in [0, 1], never
/// rounded to bytes, so <c>[0 0.45 0.74]</c> reads back as it was written.</summary>
public readonly record struct UiColor(double R, double G, double B)
{
    /// <summary>The nearest 8-bit colour, for drawing.</summary>
    public Drawing.Color ToColor() => Drawing.Color.FromScRgb(R, G, B);
}

/// <summary>Whether a component answers the user: MATLAB's <c>Enable</c> words.</summary>
public enum UiEnable
{
    /// <summary>Usable.</summary>
    On,

    /// <summary>Greyed and unusable.</summary>
    Off,

    /// <summary>Looks usable, takes no input; a click runs its <c>ButtonDownFcn</c> instead.</summary>
    Inactive,
}

/// <summary>Which shape a component's text was given in, so it reads back in that shape.</summary>
public enum UiTextForm
{
    /// <summary>A character row (or a string scalar, which MATLAB stores as one).</summary>
    CharRow,

    /// <summary>A character matrix, one line per row, padded to one width.</summary>
    CharMatrix,

    /// <summary>A cell of lines, which MATLAB always hands back as a column.</summary>
    Cell,
}

/// <summary>
/// A component's text — a <c>uicontrol</c>'s <c>String</c>, a <c>Tooltip</c> — as its lines and the
/// shape it reads back in. Immutable: every write is a new one.
/// </summary>
public sealed class UiText
{
    /// <summary>No text, as a character row.</summary>
    public static readonly UiText Empty = new(UiTextForm.CharRow, []);

    public UiText(UiTextForm form, IReadOnlyList<string> lines)
    {
        Form = form;
        Lines = lines;
    }

    /// <summary>One line of text, as a character row.</summary>
    public static UiText Of(string text) => text.Length == 0 ? Empty : new UiText(UiTextForm.CharRow, [text]);

    public UiTextForm Form { get; }

    /// <summary>The lines, top to bottom. A character row holds at most one.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The lines joined by newlines, for a control that shows one block of text.</summary>
    public string Joined => string.Join("\n", Lines);
}

/// <summary>
/// The base of every app-building component (app-building plan, U1): what a <c>uicontrol</c> and,
/// later, a panel or a <c>uibutton</c> share. It draws nothing itself. The window realises it as a
/// WPF control from a frame snapshot (<see cref="UiFrame"/>), and nothing on the interface thread
/// ever writes it: a user's typing reaches it through the script queue.
/// <para>
/// Every property here is curated in the script surface rather than reflected, which is why none is
/// browsable: MATLAB's names and words are not the model's, and a component answers to MATLAB's alone.
/// </para>
/// </summary>
public abstract class UiObject : GraphObject
{
    private Rect2D _position = new(20, 20, 60, 20);
    private UiUnits _units = UiUnits.Pixels;
    private UiEnable _enable = UiEnable.On;
    private UiText _tooltip = UiText.Empty;

    /// <summary>
    /// MATLAB's <c>Position</c>, in <see cref="Units"/>: the left edge, the bottom edge, the width and
    /// the height, measured from the bottom-left corner of the parent's inner area. In pixels the
    /// edges are 1-based (see <see cref="UiUnitConverter"/>).
    /// </summary>
    [Browsable(false)]
    public Rect2D Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Ui);
    }

    /// <summary>
    /// MATLAB's <c>Units</c>. Writing it here changes what <see cref="Position"/> is counted in and
    /// leaves the numbers alone; <see cref="ChangeUnits"/> is the write a script's <c>Units</c> makes,
    /// which keeps the component where it is.
    /// </summary>
    [Browsable(false)]
    public UiUnits Units
    {
        get => _units;
        set => SetProperty(ref _units, value, InvalidationKind.Ui);
    }

    /// <summary>The figure or panel holding this component, or null while it is detached.</summary>
    [Browsable(false)]
    public IUiContainer? Container => Parent as IUiContainer;

    /// <summary>
    /// The size, in pixels, of the area this component is placed in: its parent's inner area. A
    /// detached component measures against a default figure's 560 by 420.
    /// </summary>
    public Size2D ReferenceSize() => Container?.InnerPixelSize ?? new Size2D(560, 420);

    /// <summary><see cref="Position"/> as MATLAB's pixel rectangle within the parent.</summary>
    public Rect2D PixelPosition() => UiUnitConverter.ToPixels(_position, _units, ReferenceSize());

    /// <summary>Places the component by a pixel rectangle, keeping its <see cref="Units"/>.</summary>
    public void SetPixelPosition(Rect2D pixels) =>
        Position = UiUnitConverter.FromPixels(pixels, _units, ReferenceSize());

    /// <summary>
    /// Changes <see cref="Units"/> the way a script does: the component stays where it is and
    /// <see cref="Position"/> is re-expressed.
    /// </summary>
    public void ChangeUnits(UiUnits units)
    {
        if (units == _units)
        {
            return;
        }

        Rect2D pixels = PixelPosition();
        Size2D reference = ReferenceSize();
        _units = units;
        _position = UiUnitConverter.FromPixels(pixels, units, reference);
        OnPropertyChanged(nameof(Units));
        OnPropertyChanged(nameof(Position));
        Invalidate(InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Enable</c>.</summary>
    [Browsable(false)]
    public UiEnable Enable
    {
        get => _enable;
        set => SetProperty(ref _enable, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Tooltip</c> (the older spelling is <c>TooltipString</c>).</summary>
    [Browsable(false)]
    public UiText Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value ?? UiText.Empty, InvalidationKind.Ui);
    }

    /// <summary>The figure this component sits in, or null while it is detached.</summary>
    [Browsable(false)]
    public FigureModel? Figure
    {
        get
        {
            for (GraphObject? up = Parent; up is not null; up = up.Parent)
            {
                if (up is FigureModel figure)
                {
                    return figure;
                }
            }

            return null;
        }
    }
}
