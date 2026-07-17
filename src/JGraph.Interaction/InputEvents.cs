using JGraph.Core.Primitives;

namespace JGraph.Interaction;

/// <summary>A pointer (mouse) button.</summary>
public enum PointerButton
{
    None,
    Left,
    Middle,
    Right,
}

/// <summary>Keyboard modifier keys held during an input event.</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>Keys the interaction system reacts to. Kept minimal and UI-framework independent.</summary>
public enum InteractionKey
{
    None,
    Escape,
    Delete,
}

/// <summary>A cursor hint an interaction mode requests; the UI layer maps it to a real cursor.</summary>
public enum InteractionCursor
{
    Arrow,
    Hand,
    Cross,
    SizeAll,
}

/// <summary>A UI-independent pointer event in device/pixel space.</summary>
public readonly struct PointerEventArgs
{
    public PointerEventArgs(Point2D position, PointerButton button, ModifierKeys modifiers, int clickCount = 1)
    {
        Position = position;
        Button = button;
        Modifiers = modifiers;
        ClickCount = clickCount;
    }

    public Point2D Position { get; }

    public PointerButton Button { get; }

    public ModifierKeys Modifiers { get; }

    public int ClickCount { get; }
}

/// <summary>A UI-independent mouse-wheel event. Positive <see cref="Delta"/> is a scroll-up notch.</summary>
public readonly struct WheelEventArgs
{
    public WheelEventArgs(Point2D position, double delta, ModifierKeys modifiers)
    {
        Position = position;
        Delta = delta;
        Modifiers = modifiers;
    }

    public Point2D Position { get; }

    public double Delta { get; }

    public ModifierKeys Modifiers { get; }
}

/// <summary>A UI-independent key event.</summary>
public readonly struct KeyEventArgs
{
    public KeyEventArgs(InteractionKey key, ModifierKeys modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public InteractionKey Key { get; }

    public ModifierKeys Modifiers { get; }
}
