using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a legend answers a MATLAB-dialect script beyond its drawing (open item 109, probes
/// <c>probe_109b</c> and <c>probe_109c</c>): <c>Visible</c> is whether a legend that is there is drawn,
/// <c>PlotChildren</c> the series it shows, and its <c>String</c> can be written.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static void AddMatlabLegendBlock(IDictionary<string, GraphicsProperty> table)
    {
        // A legend is its axes' part for life, so its model Visible says whether there is one; to a
        // MATLAB script, Visible is whether that one is drawn, which legend hide and show set.
        Put(table, "Visible",
            entry =>
            {
                var legend = (LegendModel)entry.Target;
                return OnOff(JgsRunningDialect.ThreadIsMatlab ? !legend.Hidden : legend.Visible);
            },
            (entry, value, line, col) =>
            {
                var legend = (LegendModel)entry.Target;
                bool on = ToOnOff("Visible", value, line, col);
                if (JgsRunningDialect.ThreadIsMatlab)
                {
                    legend.Hidden = !on;
                }
                else
                {
                    legend.Visible = on;
                }
            });

        Put(table, "PlotChildren", entry =>
        {
            var legend = (LegendModel)entry.Target;
            JgsBuiltins.RefreshLegend(legend);
            return HandleColumn([.. legend.Entries.Where(static e => e.Visible).Select(static e => e.Plot).OfType<GraphObject>()]);
        });

        // The legend's title is a text object of its own, as R2025b's is (open item 141). Written
        // words still title it: R2025b refuses the write as read-only, and this build keeps the
        // assignment it has always taken (a JGraph extra, recorded in ADR 0242).
        Put(table, "Title",
            entry => JgsHandleRegistry.For(JgsLegendTitle.For((LegendModel)entry.Target)),
            (entry, value, line, col) => JgsLegendTitle.For((LegendModel)entry.Target).WriteString(value, line, col));
    }

    /// <summary>
    /// <c>l.String = …</c>: each name names the series of its row, as R2025b's does. Fewer names than
    /// rows take the rest out of the legend, keeping the names they had; more are passed over.
    /// </summary>
    private static void WriteLegendString(LegendModel legend, JgsValue value, int line, int col)
    {
        string[] names = JgsBuiltins.IsTextScalar(value)
            ? [JgsBuiltins.StrOf("String", value, line, col)]
            : JgsBuiltins.TextElementsOf(value)
                ?? (value.Type == JgsType.Cell && value.AsCell.Length == 0
                    ? []
                    : throw new JgsRuntimeException(line, col,
                        "A legend's String is text: a character vector, a string array, or a cell array of them."));

        JgsBuiltins.RefreshLegend(legend);
        int row = 0;
        foreach (LegendEntryModel entry in legend.Entries)
        {
            if (!entry.Visible || entry.Plot is not { } plot)
            {
                continue;
            }

            if (row < names.Length)
            {
                plot.DisplayName = names[row];
            }
            else
            {
                entry.Visible = false;
            }

            row++;
        }

        legend.NameUnnamedRows();
    }
}
