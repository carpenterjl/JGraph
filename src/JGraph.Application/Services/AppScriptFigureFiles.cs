using JGraph.Core.Model;
using JGraph.Export;
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
    public void Export(FigureModel figure, string path) =>
        FigureExporter.Export(figure, path, new ExportOptions());
}
