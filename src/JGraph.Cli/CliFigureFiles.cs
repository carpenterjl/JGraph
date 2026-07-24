using JGraph.Core.Model;
using JGraph.Export;
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
    public void Export(FigureModel figure, string path) =>
        FigureExporter.Export(figure, path, new ExportOptions());
}
