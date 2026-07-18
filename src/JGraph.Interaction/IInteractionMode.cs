namespace JGraph.Interaction;

/// <summary>Identifies the built-in interaction modes.</summary>
public enum InteractionModeKind
{
    /// <summary>The default (M21): drag pans/rotates, click places persistent data tips.</summary>
    Pointer,
    Pan,
    RectangleZoom,

    /// <summary>The roving readout tool: each click replaces the tip it placed last.</summary>
    DataTips,
    Edit,
}

/// <summary>
/// A modal interaction behavior (pan, rectangle-zoom, data cursor). The controller forwards pointer
/// and key events to the active mode; modes mutate the model through the controller's helpers and the
/// <see cref="Navigation"/> math. Modes are UI-framework independent.
/// </summary>
public interface IInteractionMode
{
    InteractionModeKind Kind { get; }

    /// <summary>The cursor hint to show while this mode is active and idle.</summary>
    InteractionCursor Cursor { get; }

    void OnPointerDown(InteractionController controller, PointerEventArgs e);

    void OnPointerMove(InteractionController controller, PointerEventArgs e);

    void OnPointerUp(InteractionController controller, PointerEventArgs e);

    void OnKey(InteractionController controller, KeyEventArgs e);

    /// <summary>Aborts any in-progress gesture (for example on Escape or mode switch).</summary>
    void Cancel(InteractionController controller);
}

/// <summary>Base class providing no-op implementations so modes override only what they need.</summary>
public abstract class InteractionModeBase : IInteractionMode
{
    public abstract InteractionModeKind Kind { get; }

    public virtual InteractionCursor Cursor => InteractionCursor.Arrow;

    public virtual void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
    }

    public virtual void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
    }

    public virtual void OnPointerUp(InteractionController controller, PointerEventArgs e)
    {
    }

    public virtual void OnKey(InteractionController controller, KeyEventArgs e)
    {
    }

    public virtual void Cancel(InteractionController controller)
    {
    }
}
