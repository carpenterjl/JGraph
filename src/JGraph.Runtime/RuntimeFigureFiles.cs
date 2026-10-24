using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Imaging;
using JGraph.Scripting;
using JGraph.Serialization;

namespace JGraph.Runtime;

/// <summary>
/// A component's <c>savefig</c>, <c>openfig</c>, <c>exportgraphics</c>, <c>print</c> and
/// <c>getframe</c> (stage K5): the headless half is the launcher's (<c>CliFigureFiles</c>, duplicated
/// for the reason given there), and what needs a window (the print and page dialogs, the clipboard,
/// <c>exportapp</c>'s picture of a window) is the window host's once a component has shown a figure,
/// and refused as a batch run refuses it before then.
/// </summary>
internal sealed class RuntimeFigureFiles : IScriptFigureFiles
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

    private static IScriptFigureFiles? Windowed => JgRuntime.LoadedDesktop?.FigureFiles;

    /// <inheritdoc />
    public bool CopyToClipboard(FigureModel figure, double scale) => Windowed?.CopyToClipboard(figure, scale) ?? false;

    /// <inheritdoc />
    public bool PrintInteractive(FigureModel figure) => Windowed?.PrintInteractive(figure) ?? false;

    /// <inheritdoc />
    public bool PreviewPage(FigureModel figure) => Windowed?.PreviewPage(figure) ?? false;

    /// <inheritdoc />
    public bool PageSetup(FigureModel figure) => Windowed?.PageSetup(figure) ?? false;

    /// <inheritdoc />
    public bool ExportSetup(FigureModel figure) => Windowed?.ExportSetup(figure) ?? false;

    /// <inheritdoc />
    public bool CaptureWindow(FigureModel figure, string path) => Windowed?.CaptureWindow(figure, path) ?? false;
}
