using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>Whether a toolbar button acts once or holds a state (MATLAB <c>axtoolbarbtn</c>).</summary>
public enum ToolbarButtonStyle
{
    /// <summary>Acts on the press and comes back up.</summary>
    Push,

    /// <summary>Stays down until it is pressed again, or another state button is.</summary>
    State,
}

/// <summary>
/// One button of an axes' hovering toolbar. The icon is a word rather than a picture: MATLAB's
/// built-in buttons are named, and a picture given as a matrix is refused rather than accepted and
/// drawn as nothing.
/// </summary>
public sealed class AxesToolbarButtonModel : GraphObject
{
    private string _icon = string.Empty;
    private ToolbarButtonStyle _style = ToolbarButtonStyle.Push;
    private string _tooltip = string.Empty;
    private bool _value;

    public AxesToolbarButtonModel(string icon)
    {
        _icon = icon ?? string.Empty;
        Name = _icon.Length > 0 ? _icon : "Button";
    }

    /// <summary>Which of the named buttons this is: <c>export</c>, <c>pan</c>, <c>zoomin</c>, …</summary>
    [Category("Appearance")]
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value ?? string.Empty, InvalidationKind.None);
    }

    [Category("Appearance")]
    public ToolbarButtonStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.None);
    }

    [Category("Appearance")]
    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value ?? string.Empty, InvalidationKind.None);
    }

    /// <summary>Whether a state button is down. A push button is never down between presses.</summary>
    [Category("Appearance")]
    public bool Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.None);
    }
}

/// <summary>
/// The small toolbar that appears over an axes when the pointer is inside it (MATLAB
/// <c>axtoolbar</c>): restore the view, zoom, pan, take a data tip, export.
/// <para>
/// It is window chrome rather than part of the drawing, so the figure renderer never draws it and an
/// export never shows it — the control that hosts the figure paints it over the top. What
/// lives here is which buttons it has and whether it is shown, which is what a script can say.
/// </para>
/// </summary>
public sealed class AxesToolbarModel : GraphObject
{
    /// <summary>
    /// The buttons an axes has when nothing else was asked for. MATLAB's own default set begins with
    /// a brush button, and this one does not: there is no data-brushing mode here, and a button that
    /// did nothing when it was pressed would be the failure this whole wave exists to avoid.
    /// </summary>
    public static readonly string[] DefaultButtons =
        ["export", "datacursor", "pan", "zoomin", "zoomout", "restoreview"];

    /// <summary>Every icon word this build draws a button for, and can act on when it is pressed.</summary>
    public static readonly string[] KnownButtons =
        ["export", "datacursor", "pan", "zoomin", "zoomout", "restoreview", "rotate"];

    private readonly List<AxesToolbarButtonModel> _buttons = [];

    public AxesToolbarModel()
    {
        Name = "Axes toolbar";
        Restore();
    }

    /// <summary>The buttons, left to right.</summary>
    [Browsable(false)]
    public IReadOnlyList<AxesToolbarButtonModel> Buttons => _buttons;

    /// <summary>Puts back the default buttons, which is what <c>axtoolbar('default')</c> asks for.</summary>
    public void Restore() => Replace(DefaultButtons);

    /// <summary>
    /// Replaces the buttons with the named ones, in the order they are named — left to right, which
    /// is the order a script writing the list means and the order they are drawn in.
    /// </summary>
    public void Replace(IEnumerable<string> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        _buttons.Clear();
        foreach (string icon in icons)
        {
            var button = new AxesToolbarButtonModel(icon);
            _buttons.Add(button);
            Adopt(button);
        }

        Invalidate(InvalidationKind.None);
    }

    /// <summary>Adds one button at the left end, which is where <c>axtoolbarbtn</c> puts one.</summary>
    public AxesToolbarButtonModel Add(AxesToolbarButtonModel button)
    {
        ArgumentNullException.ThrowIfNull(button);
        _buttons.Insert(0, button);
        Adopt(button);
        Invalidate(InvalidationKind.None);
        return button;
    }
}
