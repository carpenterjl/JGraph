using JGraph.Core.Model;

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
    void Export(FigureModel figure, string path);
}
