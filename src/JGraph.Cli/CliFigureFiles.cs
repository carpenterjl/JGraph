using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Imaging;
using JGraph.Scripting;
using JGraph.Serialization;

namespace JGraph.Cli;

/// <summary>
/// The launcher's <c>savefigure</c>/<c>loadfigure</c>/<c>exportfigure</c> services. Identical in
/// behaviour to the application's, and deliberately duplicated rather than shared: a common home
/// would mean <c>JGraph.Scripting</c> referencing the serialization and export projects for three
/// one-line methods.
/// </summary>
internal sealed class CliFigureFiles : IScriptFigureFiles
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

    /// <summary>A launcher run has no clipboard, which is the whole point of running it headless.</summary>
    public bool CopyToClipboard(FigureModel figure, double scale) => false;
}
