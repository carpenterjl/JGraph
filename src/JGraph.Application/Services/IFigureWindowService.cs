using JGraph.Core.Model;

namespace JGraph.Application.Services;

/// <summary>
/// Manages the figure windows scripts open: one full <see cref="FigureWindow"/> per MATLAB-style
/// figure number, created on first show and reused (content swapped in place) when the same figure
/// number is shown again — so re-running a script updates its windows instead of spawning more.
/// </summary>
public interface IFigureWindowService
{
    /// <summary>Shows <paramref name="figure"/> in the window for figure <paramref name="number"/>,
    /// creating that window on first use. Must be called on the UI thread.</summary>
    void ShowScriptFigure(int number, FigureModel figure);

    /// <summary>
    /// Opens an empty figure window under the next unused figure number and returns that number.
    /// It is numbered like any other figure, so a script or the console can address it with
    /// <c>figure(n)</c> and draw into it. Must be called on the UI thread.
    /// </summary>
    int OpenBlankFigure();

    /// <summary>
    /// Closes the window showing figure <paramref name="number"/>, if one is open — the host half of
    /// the script's <c>close</c>. Must be called on the UI thread.
    /// </summary>
    void CloseScriptFigure(int number);

    /// <summary>Closes every script figure window.</summary>
    void CloseAll();
}
