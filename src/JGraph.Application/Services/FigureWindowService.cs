using JGraph.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IFigureWindowService"/>: a number-keyed registry of
/// DI-minted <see cref="FigureWindow"/>/<see cref="Mvvm.FigureViewModel"/> pairs (both transient).
/// The script figure replaces the view model's sample figure before the window first shows, so
/// there is no flash; closing a window just evicts it — the next show of that number recreates it.
/// </summary>
public sealed class FigureWindowService : IFigureWindowService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<int, FigureWindow> _windows = new();

    /// <summary>Creates the service over the DI container that mints figure windows.</summary>
    public FigureWindowService(IServiceProvider services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public void ShowScriptFigure(int number, FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        string status = $"Figure {number} — from script";

        if (_windows.TryGetValue(number, out FigureWindow? window))
        {
            window.ViewModel.DisplayFigure(figure, status);
            window.Show(); // Restores a minimized window; no-op when already visible.
            return;
        }

        window = _services.GetRequiredService<FigureWindow>();
        window.Title = $"Figure {number}";
        window.ViewModel.DisplayFigure(figure, status);
        window.Closed += (_, _) => _windows.Remove(number);
        _windows[number] = window;
        window.Show();
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        foreach (FigureWindow window in _windows.Values.ToArray())
        {
            window.Close(); // The Closed handler evicts it from the map.
        }
    }
}
