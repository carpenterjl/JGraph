using JGraph.Core.Model;
using JGraph.Data.Import;

namespace JGraph.Objects;

/// <summary>
/// Builds the plots described by a <see cref="TablePlotSpec"/> onto an <see cref="AxesModel"/> — one
/// plot per Y column — and enables the legend when the spec produces more than one. This is the shared
/// bridge used by the import wizard (and later by scripts) so callers can stay free of the concrete plot
/// types.
/// </summary>
public static class TablePlotBuilder
{
    public static IReadOnlyList<PlotObject> Build(AxesModel axes, TablePlotSpec spec)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(spec);

        var plots = new List<PlotObject>();

        switch (spec.Kind)
        {
            case ImportPlotKind.Line:
                foreach (string y in spec.YColumns)
                {
                    plots.Add(axes.AddLine(spec.Table, spec.XColumn, y));
                }

                break;

            case ImportPlotKind.Scatter:
                foreach (string y in spec.YColumns)
                {
                    plots.Add(axes.AddScatter(spec.Table, spec.XColumn, y));
                }

                break;

            case ImportPlotKind.Stem:
                foreach (string y in spec.YColumns)
                {
                    plots.Add(axes.AddStem(spec.Table, spec.XColumn, y));
                }

                break;

            case ImportPlotKind.Bar:
                foreach (string y in spec.YColumns)
                {
                    plots.Add(axes.AddBar(spec.Table, spec.XColumn, y));
                }

                break;

            case ImportPlotKind.Histogram:
                foreach (string y in spec.YColumns)
                {
                    plots.Add(axes.AddHistogram(spec.Table, y, spec.HistogramBins));
                }

                break;

            case ImportPlotKind.ErrorBar:
                foreach (string y in spec.YColumns)
                {
                    plots.Add(axes.AddErrorBar(spec.Table, spec.XColumn, y, spec.ErrorColumn
                        ?? throw new ArgumentException("An error-bar plot requires an error column.", nameof(spec))));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "Unsupported plot kind.");
        }

        if (plots.Count > 1)
        {
            axes.Legend.Visible = true;
        }

        return plots;
    }
}
