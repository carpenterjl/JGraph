using System.Linq;
using System.Windows;
using JGraph.Application.Scripting;
using JGraph.Core.Model;
using JGraph.Scripting;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IScriptingService"/>: hosts a single modeless
/// <see cref="ScriptWorkspaceWindow"/> over the available script engines. Re-opening while it is
/// already up just brings it to the front.
/// </summary>
public sealed class ScriptingService : IScriptingService
{
    private readonly IReadOnlyList<IScriptEngine> _engines;
    private readonly IWorkspaceStateService _stateService;
    private readonly IFigureWindowService _figureWindows;
    private ScriptWorkspaceWindow? _window;

    /// <summary>Creates the service over the registered script engines.</summary>
    public ScriptingService(
        IEnumerable<IScriptEngine> engines,
        IWorkspaceStateService stateService,
        IFigureWindowService figureWindows)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(figureWindows);
        _engines = engines.ToList();
        _stateService = stateService;
        _figureWindows = figureWindows;
    }

    /// <inheritdoc />
    public void OpenEditor()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new ScriptWorkspaceWindow(_engines, _stateService, _figureWindows)
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive),
        };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
