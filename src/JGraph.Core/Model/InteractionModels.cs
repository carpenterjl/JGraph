using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>Which of an axes' directions a gesture is allowed to move (MATLAB <c>Dimensions</c>).</summary>
public enum InteractionDimensions
{
    /// <summary>Both, which is what every one of these gestures did before M80.</summary>
    XY,

    /// <summary>Along X only: the other direction holds still.</summary>
    X,

    /// <summary>Along Y only.</summary>
    Y,
}

/// <summary>
/// One of the gestures an axes answers to without a tool being chosen first — MATLAB's
/// <c>ax.Interactions</c>, the list a script hands an axes to say which of them it wants.
/// <para>
/// Every one of these was already happening: dragging pans, the wheel zooms, a click pins a data tip.
/// What was missing was the ability to say so, or to say no. That is the whole of what M80 adds here,
/// and it is why these are small: an interaction object <em>is</em> a switch with a setting on it,
/// and inventing more of it than MATLAB documents would be inventing behaviour.
/// </para>
/// </summary>
public abstract class InteractionModel : GraphObject
{
    /// <summary>The word <c>findobj</c> and <c>get(h, 'Type')</c> answer with.</summary>
    [Browsable(false)]
    public abstract string Kind { get; }
}

/// <summary>An interaction that moves one or both directions (pan, zoom, ruler pan, region zoom).</summary>
public abstract class DirectionalInteractionModel : InteractionModel
{
    private InteractionDimensions _dimensions = InteractionDimensions.XY;

    /// <summary>Which directions the gesture may move.</summary>
    [Category("Interaction")]
    public InteractionDimensions Dimensions
    {
        get => _dimensions;
        set => SetProperty(ref _dimensions, value, InvalidationKind.None);
    }
}

/// <summary>Dragging inside the axes moves the view (MATLAB <c>panInteraction</c>).</summary>
public sealed class PanInteractionModel : DirectionalInteractionModel
{
    public PanInteractionModel() => Name = "Pan";

    public override string Kind => "pan";
}

/// <summary>The wheel zooms about the pointer (MATLAB <c>zoomInteraction</c>).</summary>
public sealed class ZoomInteractionModel : DirectionalInteractionModel
{
    public ZoomInteractionModel() => Name = "Zoom";

    public override string Kind => "zoom";
}

/// <summary>Dragging a ruler moves that direction alone (MATLAB <c>rulerPanInteraction</c>).</summary>
public sealed class RulerPanInteractionModel : DirectionalInteractionModel
{
    public RulerPanInteractionModel() => Name = "Ruler pan";

    public override string Kind => "rulerpan";
}

/// <summary>Dragging a rectangle zooms to it (MATLAB <c>regionZoomInteraction</c>).</summary>
public sealed class RegionZoomInteractionModel : DirectionalInteractionModel
{
    public RegionZoomInteractionModel() => Name = "Region zoom";

    public override string Kind => "regionzoom";
}

/// <summary>Dragging turns a three-dimensional view (MATLAB <c>rotateInteraction</c>).</summary>
public sealed class RotateInteractionModel : InteractionModel
{
    public RotateInteractionModel() => Name = "Rotate";

    public override string Kind => "rotate";
}

/// <summary>A click pins a data tip (MATLAB <c>dataTipInteraction</c>).</summary>
public sealed class DataTipInteractionModel : InteractionModel
{
    private bool _snapToDataVertex = true;

    public DataTipInteractionModel() => Name = "Data tip";

    public override string Kind => "datatip";

    /// <summary>
    /// Whether a tip lands on the nearest data point or where the pointer was. This build only ever
    /// pins to a point, so turning it off is refused rather than accepted and ignored.
    /// </summary>
    [Category("Interaction"), DisplayName("Snap to data vertex")]
    public bool SnapToDataVertex
    {
        get => _snapToDataVertex;
        set => SetProperty(ref _snapToDataVertex, value, InvalidationKind.None);
    }
}
