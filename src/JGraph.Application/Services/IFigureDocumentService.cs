using JGraph.Core.Model;

namespace JGraph.Application.Services;

/// <summary>
/// UI-facing persistence for the figure window: saving and opening ".graph" documents and copying or
/// pasting a figure through the clipboard. Implementations own the file dialogs and clipboard so the
/// view model stays free of WPF types.
/// </summary>
public interface IFigureDocumentService
{
    /// <summary>Prompts for a destination and saves the figure as a ".graph" document. Returns the path, or null if cancelled.</summary>
    string? Save(FigureModel figure);

    /// <summary>Prompts for a ".graph" document and loads it. Returns the loaded figure, or null if cancelled or invalid.</summary>
    FigureModel? Open();

    /// <summary>Copies the figure to the clipboard as an editable object graph. Returns false when the clipboard was busy.</summary>
    bool CopyFigure(FigureModel figure);

    /// <summary>Pastes a figure previously copied to the clipboard. Returns false when none is present.</summary>
    bool TryPasteFigure(out FigureModel? figure);
}
