using System.Windows;
using JGraph.Application.Import;
using JGraph.Core.Model;
using JGraph.Data.Import;
using JGraph.Objects;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IDataImportService"/>: shows the import wizard, then builds the
/// chosen plots either into a new figure or onto the current axes.
/// </summary>
public sealed class DataImportService : IDataImportService
{
    /// <inheritdoc />
    public FigureModel? Import(FigureModel current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var model = new ImportWizardModel();
        var window = new ImportWizardWindow(model)
        {
            Owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };

        if (window.ShowDialog() != true || !model.CanBuild)
        {
            return null;
        }

        TablePlotSpec spec = model.BuildSpec();

        if (model.Target == ImportTarget.CurrentAxes)
        {
            AxesModel axes = current.Axes.Count > 0 ? current.Axes[^1] : current.AddAxes();
            TablePlotBuilder.Build(axes, spec);
            return current;
        }

        var figure = new FigureModel();
        AxesModel newAxes = figure.AddAxes();
        TablePlotBuilder.Build(newAxes, spec);
        return figure;
    }
}
