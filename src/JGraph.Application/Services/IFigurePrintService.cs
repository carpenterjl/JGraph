using JGraph.Core.Model;

namespace JGraph.Application.Services;

/// <summary>
/// The print and page dialogs a figure can be shown through (M84).
/// </summary>
/// <remarks>
/// WPF-free, like <see cref="IFigureExportService"/> and <see cref="IDataImportService"/> beside it,
/// so the view model and the script host stay clear of window types. The implementation owns the
/// dialogs; every method answers false when the person cancelled or when there is no window to show
/// one in, and the script verbs turn that false into a refusal that names the non-interactive verb.
/// </remarks>
public interface IFigurePrintService
{
    /// <summary>Shows the print dialog and prints the figure on the page it describes.</summary>
    bool Print(FigureModel figure);

    /// <summary>Shows the page a figure would print on, with a button to print it.</summary>
    bool Preview(FigureModel figure);

    /// <summary>Shows the page-setup dialog, writing the figure's <c>Paper*</c> properties.</summary>
    bool PageSetup(FigureModel figure);

    /// <summary>Shows the export-setup dialog, writing the figure's export preset.</summary>
    bool ExportSetup(FigureModel figure);
}
