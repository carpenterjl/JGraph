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

    /// <summary>Closes every script figure window.</summary>
    void CloseAll();
}
