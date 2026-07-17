using JGraph.Core.Model;

namespace JGraph.Application.Services;

/// <summary>
/// UI-facing data import for the figure window: runs the import wizard and returns the figure to show.
/// Implementations own the wizard window and file dialogs so the view model stays free of WPF types.
/// </summary>
public interface IDataImportService
{
    /// <summary>
    /// Runs the import wizard against the current figure. Returns the same figure instance when plots
    /// were appended to its current axes, a new figure when the user chose "New figure", or null when
    /// the wizard was cancelled.
    /// </summary>
    FigureModel? Import(FigureModel current);
}
