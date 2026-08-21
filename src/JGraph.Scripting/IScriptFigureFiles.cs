using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Imaging;

namespace JGraph.Scripting;

/// <summary>
/// Host-provided figure file services behind the <c>savefigure</c>/<c>loadfigure</c>/<c>exportfigure</c>
/// builtins. A host callback rather than direct project references keeps JGraph.Scripting free of
/// JGraph.Serialization/JGraph.Export and lets the app supply its current theme for image export —
/// the same seam pattern as <see cref="ScriptContext.ShowFigure"/>. Implementations may throw
/// IO/format exceptions; the builtins surface them as script diagnostics.
/// </summary>
public interface IScriptFigureFiles
{
    /// <summary>Saves a figure as a <c>.graph</c> document, overwriting silently.</summary>
    void Save(FigureModel figure, string path);

    /// <summary>Loads a figure from a <c>.graph</c> document.</summary>
    FigureModel Load(string path);

    /// <summary>Exports a figure as an image; the format follows the extension (png/jpg/bmp/tiff/svg/pdf).</summary>
    /// <param name="figure">The figure to write.</param>
    /// <param name="path">Where to write it; the extension chooses the format.</param>
    /// <param name="scale">
    /// Pixels per device-independent unit, which is what a resolution in dots per inch comes to
    /// once it is divided by the ninety-six a device-independent unit is worth. One is the screen.
    /// </param>
    /// <param name="size">
    /// The size to draw at, in device-independent units, or null for the figure's own. This is how
    /// a page size reaches the exporter: printing at a stated paper position is drawing the figure
    /// at the size that position asks for.
    /// </param>
    void Export(FigureModel figure, string path, double scale = 1.0, Size2D? size = null);

    /// <summary>
    /// Renders a figure into pixels without a window and without a file — what <c>getframe</c>
    /// answers with, and the only way a headless script can look at what it drew. The caller owns
    /// the returned buffer. <paramref name="scale"/> is pixels per device-independent unit.
    /// </summary>
    ImageBuffer Capture(FigureModel figure, double scale);

    /// <summary>
    /// Puts a figure on the system clipboard as an image, answering false when this host has no
    /// clipboard to put it on — which is what a batch run is, and why <c>copygraphics</c> is an
    /// accepted no-op there rather than an error.
    /// </summary>
    bool CopyToClipboard(FigureModel figure, double scale);
}
