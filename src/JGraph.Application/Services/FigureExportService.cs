using System.IO;
using System.Windows;
using JGraph.Controls;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using Microsoft.Win32;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IFigureExportService"/>: a save dialog over
/// <see cref="FigureExporter"/> (format inferred from the chosen extension) and clipboard copy via
/// <see cref="FigureClipboard"/>. Raster formats are supersampled 2× for print-quality output while
/// keeping the on-screen layout.
/// </summary>
public sealed class FigureExportService : IFigureExportService
{
    private const double RasterScale = 2.0;

    private const string Filter =
        "PNG image (*.png)|*.png|" +
        "JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
        "Bitmap (*.bmp)|*.bmp|" +
        "TIFF image (*.tif;*.tiff)|*.tif;*.tiff|" +
        "SVG vector image (*.svg)|*.svg|" +
        "PDF document (*.pdf)|*.pdf";

    /// <inheritdoc />
    public string? ExportInteractive(FigureModel figure, Size2D size, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(figure);

        var dialog = new SaveFileDialog
        {
            Title = "Export figure",
            Filter = Filter,
            DefaultExt = ".png",
            AddExtension = true,
            FileName = SuggestFileName(figure),
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            FigureExporter.Export(figure, dialog.FileName, new ExportOptions
            {
                Size = size,
                Scale = RasterScale,
                Theme = theme,
            });
            return dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(
                $"The figure could not be exported:\n{ex.Message}",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return null;
        }
    }

    /// <inheritdoc />
    public bool CopyImage(FigureModel figure, Size2D size, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return FigureClipboard.CopyImage(figure, new ExportOptions
        {
            Size = size,
            Scale = RasterScale,
            Theme = theme,
        });
    }

    private static string SuggestFileName(FigureModel figure)
    {
        string name = string.IsNullOrWhiteSpace(figure.Title) ? "figure" : figure.Title;
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name + ".png";
    }
}
