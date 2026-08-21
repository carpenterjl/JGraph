namespace JGraph.Core.Model;

/// <summary>
/// What plotting into a figure that already holds something does (MATLAB <c>NextPlot</c>).
/// </summary>
public enum FigureNextPlot
{
    /// <summary>Keep what is there and add to it.</summary>
    Add,

    /// <summary>Clear the figure, including the properties a script set on it.</summary>
    Replace,

    /// <summary>Clear the contents but keep the figure's own properties.</summary>
    ReplaceChildren,

    /// <summary>Leave this figure alone and draw into a fresh one.</summary>
    New,
}

/// <summary>Whether a figure's toolbar is shown (MATLAB <c>ToolBar</c>).</summary>
public enum FigureToolBarMode
{
    /// <summary>Shown, unless the figure's menu bar was taken away.</summary>
    Auto,

    /// <summary>Always shown.</summary>
    Figure,

    /// <summary>Never shown.</summary>
    None,
}

/// <summary>How a figure's window is displayed (MATLAB <c>WindowState</c>).</summary>
public enum FigureWindowState
{
    Normal,
    Minimized,
    Maximized,
    Fullscreen,
}

/// <summary>
/// The pointer a figure asks for while the mouse is over it (MATLAB <c>Pointer</c>). The words are
/// MATLAB's; the shapes are whatever the host toolkit has that means the same thing.
/// </summary>
public enum PointerShape
{
    Arrow,
    Ibeam,
    Crosshair,
    Watch,
    TopL,
    TopR,
    BotL,
    BotR,
    Circle,
    Cross,
    Fleur,
    Left,
    Right,
    Top,
    Bottom,
    Hand,
    Custom,
}

/// <summary>Which mouse gesture last selected something in a figure (MATLAB <c>SelectionType</c>).</summary>
public enum SelectionKind
{
    /// <summary>A plain left click.</summary>
    Normal,

    /// <summary>Shift-click, or both buttons at once.</summary>
    Extend,

    /// <summary>A right click, or ctrl-click.</summary>
    Alt,

    /// <summary>A double click.</summary>
    Open,
}

/// <summary>The units <c>PaperSize</c> and <c>PaperPosition</c> are measured in.</summary>
public enum PaperUnitType
{
    Inches,
    Centimeters,
    Normalized,
    Points,
}

/// <summary>Which way round the page is (MATLAB <c>PaperOrientation</c>).</summary>
public enum PaperOrientationType
{
    Portrait,
    Landscape,
}
