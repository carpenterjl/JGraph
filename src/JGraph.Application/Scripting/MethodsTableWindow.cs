using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace JGraph.Application.Scripting;

/// <summary>
/// The window <c>methodsview</c> and <c>libfunctionsview</c> open (interop plan, stage 10, ADR 0183):
/// R2025b's table of a class's methods or a library's functions — name in bold, then return type,
/// arguments, qualifiers and the class a method is inherited from, whichever of them the rows fill —
/// sortable by any column, read-only, one window per call as R2025b opens one figure per call.
/// </summary>
internal sealed class MethodsTableWindow : Window
{
    /// <summary>R2025b sizes its table to at most this many rows before it scrolls (methodsview.m).</summary>
    private const int VisibleRows = 26;

    public MethodsTableWindow(string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        Title = title;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // A DataTable rather than the string arrays themselves: its view sorts by column natively.
        var table = new DataTable();
        for (int j = 0; j < headers.Count; j++)
        {
            table.Columns.Add("c" + j, typeof(string));
        }

        foreach (string[] row in rows)
        {
            table.Rows.Add([.. row]);
        }

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Extended,
            ItemsSource = table.DefaultView,
        };

        var bold = new Style(typeof(TextBlock));
        bold.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
        for (int j = 0; j < headers.Count; j++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[j],
                Binding = new Binding("c" + j),
                SortMemberPath = "c" + j,
                ElementStyle = j == 0 ? bold : null,
                Width = j == headers.Count - 1
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : DataGridLength.Auto,
            });
        }

        Content = grid;
        Width = Math.Clamp(160 + (headers.Count * 170), 480, 1400);
        Height = Math.Clamp(90 + (Math.Min(rows.Count, VisibleRows) * 22), 200, 720);
    }
}
