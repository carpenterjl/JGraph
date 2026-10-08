using System.IO;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Imaging;
using JGraph.Scripting;
using JGraph.Serialization;

namespace JGraph.Application.Services;

/// <summary>
/// The app's <see cref="IScriptFigureFiles"/>: <c>savefigure</c>/<c>loadfigure</c> ride the versioned
/// <see cref="GraphFormat"/> document format and <c>exportfigure</c> the UI-free
/// <see cref="FigureExporter"/> (format by extension). IO and format exceptions propagate — the
/// script builtins turn them into diagnostics with the script's line/column.
/// </summary>
public sealed class AppScriptFigureFiles : IScriptFigureFiles
{
    /// <inheritdoc />
    public void Save(FigureModel figure, string path) => GraphFormat.Save(figure, path);

    /// <inheritdoc />
    public FigureModel Load(string path) => GraphFormat.Load(path);

    /// <inheritdoc />
    public void Export(FigureModel figure, string path, double scale = 1.0, Size2D? size = null) =>
        FigureExporter.Export(figure, path, new ExportOptions { Scale = scale, Size = size });

    /// <inheritdoc />
    public ImageBuffer Capture(FigureModel figure, double scale)
    {
        (int width, int height, byte[] rgba) =
            FigureExporter.RenderRgba(figure, new ExportOptions { Scale = scale });

        // A figure with no page drew a cut-out, and its coverage is the only thing that says where
        // the cut is -- so that capture keeps four channels where an ordinary one keeps three.
        return ImageBuffer.FromRgba(rgba, width, height, figure.Background.IsTransparent);
    }

    /// <inheritdoc />
    public bool CopyToClipboard(FigureModel figure, double scale) =>
        JGraph.Controls.FigureClipboard.CopyImage(figure, new ExportOptions { Scale = scale });

    // --- The dialogs and the window capture (M84) --------------------------------------------------
    //
    // The default implementations on the interface answer false, which is what a batch run wants and
    // what the verbs turn into a refusal. These are the overrides for a host that does have a window.

    private readonly Printing.FigurePrintService _printing = new();

    /// <inheritdoc />
    public bool PrintInteractive(FigureModel figure) => _printing.Print(figure);

    /// <inheritdoc />
    public bool PreviewPage(FigureModel figure) => _printing.Preview(figure);

    /// <inheritdoc />
    public bool PageSetup(FigureModel figure) => _printing.PageSetup(figure);

    /// <inheritdoc />
    public bool ExportSetup(FigureModel figure) => _printing.ExportSetup(figure);

    /// <inheritdoc />
    /// <remarks>
    /// The one export that goes through the control rather than the renderer. M80 put the axes toolbar
    /// in JGraph.Controls precisely so that no export could carry it; <c>exportapp</c> is the verb
    /// whose whole point is a picture of the application, so it is the one that has to.
    /// </remarks>
    public bool CaptureWindow(FigureModel figure, string path)
    {
        if (System.Windows.Application.Current is not { } app)
        {
            return false;
        }

        // The figure's own window, asked for on the thread that owns it and only once that thread
        // has nothing left to lay out or draw (U4): a script calls from its own thread, and before
        // this fix the picture was of whichever window happened to be active.
        System.Windows.Media.Imaging.BitmapSource? picture = app.Dispatcher.Invoke(
            () => app.Windows.OfType<FigureWindow>().FirstOrDefault(window => window.IsVisible && window.Shows(figure))?.CaptureSurface(),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (picture is null)
        {
            return false;
        }

        // A PDF is the picture on one page of the window's size (open item 45).
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture));
            using var encoded = new MemoryStream();
            png.Save(encoded);
            using FileStream pdf = File.Create(path);
            JGraph.Export.FigureExporter.WritePicturePdf(encoded.ToArray(), picture.DpiX, figure.Name, pdf);
            return true;
        }

        System.Windows.Media.Imaging.BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 95 },
            ".tif" or ".tiff" => new System.Windows.Media.Imaging.TiffBitmapEncoder(),
            ".bmp" => new System.Windows.Media.Imaging.BmpBitmapEncoder(),
            _ => new System.Windows.Media.Imaging.PngBitmapEncoder(),
        };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture));
        using FileStream file = File.Create(path);
        encoder.Save(file);
        return true;
    }
}
