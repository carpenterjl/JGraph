using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JGraph.Application.Services;
using JGraph.Core.Model;
using JGraph.Export;

namespace JGraph.Application.Printing;

/// <summary>
/// The WPF implementation of <see cref="IFigurePrintService"/> (M84): a real print job, a real
/// preview, and the two setup dialogs behind them.
/// </summary>
/// <remarks>
/// <para>
/// The job reuses <see cref="FigureExporter"/>. The figure is rendered at the printer's own
/// resolution and placed on the page its <c>Paper*</c> properties describe, which means the printed
/// page is the same picture <c>print -dpng</c> writes — a property a test can check without owning a
/// printer, and the reason no new rendering path was written for this.
/// </para>
/// <para>
/// M75 made every <c>Paper*</c> property real and said in its own header that they had waited for
/// something that printed rather than describing a page nothing ever printed on. This is that thing.
/// </para>
/// </remarks>
public sealed class FigurePrintService : IFigurePrintService
{
    /// <inheritdoc />
    public bool Print(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        // The printer's own resolution, so the figure is rendered for the page rather than scaled up
        // from a screen-sized picture.
        double dpi = dialog.PrintTicket?.PageResolution?.X ?? 300;
        Visual page = FigurePageDialogs.ComposePage(figure, dpi);
        dialog.PrintVisual(page, figure.Name is { Length: > 0 } name ? name : "JGraph figure");
        return true;
    }

    /// <inheritdoc />
    public bool Preview(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return FigurePageDialogs.Preview(figure, Print);
    }

    /// <inheritdoc />
    public bool PageSetup(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return FigurePageDialogs.PageSetup(figure);
    }

    /// <inheritdoc />
    public bool ExportSetup(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return FigurePageDialogs.ExportSetup(figure);
    }
}
