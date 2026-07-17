using System.IO;
using System.Windows;
using JGraph.Controls;
using JGraph.Core.Model;
using JGraph.Serialization;
using Microsoft.Win32;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IFigureDocumentService"/>: save/open dialogs over
/// <see cref="GraphFormat"/> and figure clipboard interchange via <see cref="FigureClipboard"/>.
/// </summary>
public sealed class FigureDocumentService : IFigureDocumentService
{
    private const string Filter = "JGraph figure (*.graph)|*.graph|All files (*.*)|*.*";

    /// <inheritdoc />
    public string? Save(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        var dialog = new SaveFileDialog
        {
            Title = "Save figure",
            Filter = Filter,
            DefaultExt = GraphFormat.FileExtension,
            AddExtension = true,
            FileName = SuggestFileName(figure),
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            GraphFormat.Save(figure, dialog.FileName);
            return dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"The figure could not be saved:\n{ex.Message}", "Save failed");
            return null;
        }
    }

    /// <inheritdoc />
    public FigureModel? Open()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open figure",
            Filter = Filter,
            DefaultExt = GraphFormat.FileExtension,
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            return GraphFormat.Load(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GraphFormatException)
        {
            ShowError($"The figure could not be opened:\n{ex.Message}", "Open failed");
            return null;
        }
    }

    /// <inheritdoc />
    public bool CopyFigure(FigureModel figure) => FigureClipboard.CopyFigure(figure);

    /// <inheritdoc />
    public bool TryPasteFigure(out FigureModel? figure) => FigureClipboard.TryPasteFigure(out figure);

    private static string SuggestFileName(FigureModel figure)
    {
        string name = string.IsNullOrWhiteSpace(figure.Title) ? "figure" : figure.Title;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name + GraphFormat.FileExtension;
    }

    private static void ShowError(string message, string caption) =>
        MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
}
