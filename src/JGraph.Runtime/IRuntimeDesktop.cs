using JGraph.Core.Model;

namespace JGraph.Runtime;

/// <summary>
/// The window host a component's figures are shown in (compiler and SDK plan, stage K5):
/// <c>JGraph.Runtime.Desktop</c>'s, found beside this assembly the first time a component shows a
/// figure. This assembly stays free of WPF, so where the host is missing (no window library shipped,
/// or a system without one) a component's figures are made, counted, exported and printed as before,
/// and none is shown.
/// </summary>
internal interface IRuntimeDesktop
{
    /// <summary>
    /// Shows <paramref name="figure"/>, numbered <paramref name="number"/> among
    /// <paramref name="owner"/>'s figures, in that component's window for the number. Called on the
    /// engine thread; returns once the window is up.
    /// </summary>
    void Show(object owner, int number, FigureModel figure);

    /// <summary>Closes <paramref name="owner"/>'s window for figure <paramref name="number"/>, if one is open. Engine thread.</summary>
    void Close(object owner, int number);

    /// <summary>Closes every window of <paramref name="owner"/>, whose session has ended. Engine thread.</summary>
    void Retire(object owner);

    /// <summary>Whether any of <paramref name="owner"/>'s windows is on screen. Any thread but the window thread.</summary>
    bool AnyVisible(object owner);

    /// <summary>Whether the caller is the thread the windows run on, where waiting for them could never end.</summary>
    bool IsWindowThread { get; }

    /// <summary>The figure files of a program with windows: the print and page dialogs, the clipboard, a window's picture.</summary>
    JGraph.Scripting.IScriptFigureFiles FigureFiles { get; }
}
