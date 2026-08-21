using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Controls;

/// <summary>What the window as a whole saw the pointer do, whatever it landed on.</summary>
/// <remarks>
/// This is the raw account, beside — not instead of — <see cref="ObjectClickedEventArgs"/>, which
/// says what was hit. MATLAB draws the same distinction between its <c>WindowButton…Fcn</c> family,
/// which hears every press anywhere in the figure, and an object's own <c>ButtonDownFcn</c>.
/// </remarks>
public sealed class FigurePointerEventArgs : EventArgs
{
    public FigurePointerEventArgs(FigureModel figure, Point2D pixel, PointerAction action, SelectionKind selection)
    {
        Figure = figure;
        Pixel = pixel;
        Action = action;
        Selection = selection;
    }

    /// <summary>The figure the pointer was over.</summary>
    public FigureModel Figure { get; }

    /// <summary>Where, in pixels from the top-left of the drawable area.</summary>
    public Point2D Pixel { get; }

    /// <summary>Whether a button went down, came up, or the pointer merely moved.</summary>
    public PointerAction Action { get; }

    /// <summary>Which gesture this was, in MATLAB's four words. Only meaningful on a press.</summary>
    public SelectionKind Selection { get; }
}

/// <summary>The three things a pointer can do that a figure hears about as a whole.</summary>
public enum PointerAction
{
    Pressed,
    Released,
    Moved,
}

/// <summary>What the wheel did over a figure.</summary>
public sealed class FigureWheelEventArgs : EventArgs
{
    public FigureWheelEventArgs(FigureModel figure, int notches)
    {
        Figure = figure;
        Notches = notches;
    }

    /// <summary>The figure the pointer was over.</summary>
    public FigureModel Figure { get; }

    /// <summary>How far it turned, positive toward the user — MATLAB's sign for a scroll down.</summary>
    public int Notches { get; }
}
