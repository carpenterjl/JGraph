using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>
/// MATLAB's <c>uitoolbar</c> (<c>matlab.ui.container.Toolbar</c>; app-building plan, U8): a row of
/// tools under a figure's menu bar. It belongs to a figure and holds push tools and toggle tools;
/// the window builds it from the figure's frame.
/// </summary>
public sealed class UiToolbarModel : GraphObject
{
    private UiColor _background = UiPanelModel.DefaultBackground;

    public UiToolbarModel()
    {
        Name = "Toolbar";
        Tools = new GraphObjectCollection<UiToolModel>(this);
    }

    /// <summary>The tools, left to right.</summary>
    [Browsable(false)]
    public GraphObjectCollection<UiToolModel> Tools { get; }

    [Browsable(false)]
    public UiColor BackgroundColor
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Ui);
    }
}

/// <summary>
/// One tool of a toolbar: MATLAB's <c>uipushtool</c> (<c>matlab.ui.container.toolbar.PushTool</c>),
/// and the base of its <c>uitoggletool</c>, which stays down when pressed.
/// </summary>
public class UiToolModel : GraphObject
{
    private bool _state;
    private bool _enable = true;
    private bool _separator;
    private UiText _tooltip = UiText.Empty;
    private UiImage? _picture;
    private long _userWriteSeq;

    public UiToolModel()
    {
        Name = "PushTool";
    }

    /// <summary>Whether this is a toggle tool.</summary>
    [Browsable(false)]
    public virtual bool IsToggle => false;

    /// <summary>A toggle tool's <c>State</c>: down or up.</summary>
    [Browsable(false)]
    public bool State
    {
        get => _state;
        set => SetProperty(ref _state, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Enable
    {
        get => _enable;
        set => SetProperty(ref _enable, value, InvalidationKind.Ui);
    }

    /// <summary>Whether a dividing line stands to the left of this tool.</summary>
    [Browsable(false)]
    public bool Separator
    {
        get => _separator;
        set => SetProperty(ref _separator, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiText Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value ?? UiText.Empty, InvalidationKind.Ui);
    }

    /// <summary>The picture on the tool's face — its <c>Icon</c> when it has one, else its <c>CData</c>.</summary>
    [Browsable(false)]
    public UiImage? Picture
    {
        get => _picture;
        set => SetProperty(ref _picture, value, InvalidationKind.Ui);
    }

    /// <summary>The sequence number of the last press written here (see <see cref="UiControlModel.UserWriteSeq"/>).</summary>
    [Browsable(false)]
    public long UserWriteSeq
    {
        get => _userWriteSeq;
        set => SetProperty(ref _userWriteSeq, value, InvalidationKind.Ui);
    }
}

/// <summary>MATLAB's <c>uitoggletool</c> (<c>matlab.ui.container.toolbar.ToggleTool</c>).</summary>
public sealed class UiToggleToolModel : UiToolModel
{
    public UiToggleToolModel()
    {
        Name = "ToggleTool";
    }

    /// <inheritdoc />
    public override bool IsToggle => true;
}

/// <summary>One tool as a frame holds it.</summary>
public sealed record UiToolFrame(
    UiToolModel Source,
    bool IsToggle,
    bool State,
    bool Enabled,
    bool Separator,
    string Tooltip,
    UiImage? Picture,
    string Tag,
    long UserWriteSeq);

/// <summary>One toolbar that shows, as a frame holds it, with the tools of it that show.</summary>
public sealed record UiToolbarFrame(UiToolbarModel Source, UiColor Background, string Tag, IReadOnlyList<UiToolFrame> Tools);

/// <summary>
/// One entry of a figure's menu bar as a frame holds it, with the entries under it. An entry that
/// is not <c>Visible</c> is left out.
/// </summary>
public sealed record UiMenuFrame(
    MenuItemModel Source,
    string Text,
    bool Checked,
    bool Enabled,
    bool Separator,
    string Accelerator,
    string Tooltip,
    Drawing.Color Foreground,
    string Tag,
    IReadOnlyList<UiMenuFrame> Items);
