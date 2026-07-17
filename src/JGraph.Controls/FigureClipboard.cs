using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using JGraph.Core.Model;
using JGraph.Export;
using JGraph.Serialization;

namespace JGraph.Controls;

/// <summary>
/// Moves figures to and from the Windows clipboard. A figure can be copied as an image (PNG, matching
/// file exports) for pasting into other applications, or as an editable object graph (the ".graph"
/// JSON format) for pasting back into JGraph.
/// </summary>
public static class FigureClipboard
{
    /// <summary>The private clipboard format under which a figure's ".graph" JSON is stored.</summary>
    public const string FigureDataFormat = "JGraph.Figure";

    /// <summary>
    /// Renders the figure and places it on the clipboard as an image.
    /// Returns false when the clipboard was unavailable (held open by another process).
    /// </summary>
    public static bool CopyImage(FigureModel figure, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(figure);

        byte[] png = FigureExporter.ExportBytes(figure, ExportFormat.Png, options);
        using var stream = new MemoryStream(png);
        BitmapSource image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        try
        {
            Clipboard.SetImage(image);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another process holds the clipboard open; not a JGraph failure.
            return false;
        }
    }

    /// <summary>
    /// Places the figure on the clipboard as an editable ".graph" object graph (and as plain text, for
    /// inspection). Returns false when the clipboard was unavailable.
    /// </summary>
    public static bool CopyFigure(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        string json = GraphFormat.Serialize(figure);
        var data = new DataObject();
        data.SetData(FigureDataFormat, json);
        data.SetText(json);

        try
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a figure previously copied with <see cref="CopyFigure"/> from the clipboard. Returns false
    /// when the clipboard holds no JGraph figure or its content could not be parsed.
    /// </summary>
    public static bool TryPasteFigure(out FigureModel? figure)
    {
        figure = null;
        try
        {
            if (Clipboard.GetData(FigureDataFormat) is not string json)
            {
                return false;
            }

            figure = GraphFormat.Deserialize(json);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
        catch (GraphFormatException)
        {
            return false;
        }
    }
}
