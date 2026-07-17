using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Application.Services;

/// <summary>
/// UI-facing export operations for the figure window. Implementations own the file dialog and the
/// clipboard so the view model stays free of WPF types and is testable.
/// </summary>
public interface IFigureExportService
{
    /// <summary>
    /// Prompts for a destination file (PNG/JPEG/BMP/TIFF/SVG/PDF) and exports the figure at the
    /// given size and theme. Returns the exported path, or null when the user cancelled.
    /// </summary>
    string? ExportInteractive(FigureModel figure, Size2D size, ITheme theme);

    /// <summary>Copies the figure to the clipboard as an image. Returns false when the clipboard was busy.</summary>
    bool CopyImage(FigureModel figure, Size2D size, ITheme theme);
}
