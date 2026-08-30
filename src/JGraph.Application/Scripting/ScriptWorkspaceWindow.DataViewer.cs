using System.IO;
using System.Windows.Input;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Data.Import;
using JGraph.Scripting;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Data Viewer pane and the figure bridge: drilling a variable from the Variables pane into a
/// grid, opening a data file straight from the tree, and marshalling a script's figure onto the UI
/// thread so it lands in a numbered figure window.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private void OnVariablesDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedVariable();

    /// <summary>
    /// Selects the row the pointer is over before its context menu opens. A ListViewItem takes
    /// selection on the left button only, and both menu commands read SelectedItem — so without
    /// this, right-clicking a variable plotted whichever one was last left-clicked, under the other
    /// one's name, and right-clicking with nothing selected said there was nothing to plot.
    /// </summary>
    private void OnVariablesRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source
            && System.Windows.Controls.ItemsControl.ContainerFromElement(VariablesList, source)
                is System.Windows.Controls.ListViewItem row)
        {
            row.IsSelected = true;
        }
    }

    private void OnPlotVariableClick(object sender, System.Windows.RoutedEventArgs e) => PlotSelectedVariable();

    private void OnOpenVariableClick(object sender, System.Windows.RoutedEventArgs e) => OpenSelectedVariable();

    private void OpenSelectedVariable()
    {
        if (VariablesList.SelectedItem is not ScriptVariable variable)
        {
            return;
        }

        switch (variable.RawValue)
        {
            case Table table:
                ShowInDataViewer(TableGridAdapter.ForTable(table), variable.Name);
                break;
            case double[] array:
                ShowInDataViewer(TableGridAdapter.ForArray(array), variable.Name);
                break;
            case ScriptValueGrid grid:
                ShowInDataViewer(
                    TableGridAdapter.ForGrid(
                        $"{grid.Kind} {grid.Rows.Count}×{grid.ColumnNames.Count}", grid.ColumnNames, grid.Rows),
                    variable.Name);
                break;
            case null when variable.Type is "array" or "cell" or "struct":
                // Oversize values carry no raw copy (JgsRunner.MaxRawValueElements and
                // ScriptValueGrid.MaxCells) — the grid would freeze on millions of rows anyway.
                SetStatus($"'{variable.Name}' is too large for the data viewer — index a smaller slice to inspect it.");
                break;
            default:
                SetStatus($"'{variable.Name}' has no tabular view — only arrays and tables do.");
                break;
        }
    }

    /// <summary>
    /// Plots the selected numeric variable against its index — the quickest possible look at a vector,
    /// and the console-driven figure the Workspace pane is otherwise one <c>plot(x)</c> away from. It
    /// goes through <see cref="JG"/>, so the result joins the same numbered figures scripts use.
    /// </summary>
    private void PlotSelectedVariable()
    {
        if (VariablesList.SelectedItem is not ScriptVariable { RawValue: double[] values } variable)
        {
            SetStatus("Select a numeric array to plot.");
            return;
        }

        if (values.Length == 0)
        {
            SetStatus($"'{variable.Name}' is empty.");
            return;
        }

        JG.Figure();
        JG.Plot(values);
        JG.Title(variable.Name);
        ShowFigureOnUi(JG.CurrentFigureNumber, JG.CurrentFigure);
        SetStatus($"Plotted {variable.Name} ({values.Length} points).");
    }

    private void OpenDataFile(string path)
    {
        try
        {
            Table table = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? Table.ReadXlsx(path)
                : Table.ReadCsv(path);
            ShowInDataViewer(TableGridAdapter.ForTable(table), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImportException)
        {
            SetStatus($"Could not read '{path}': {ex.Message}");
        }
    }

    private void ShowInDataViewer(TableGridAdapter adapter, string name)
    {
        DataViewer.Show(adapter);
        ShowPane("dataviewer");
        SetStatus($"Data Viewer: {name} ({adapter.Title}).");
    }

    private void ShowFigureOnUi(int number, FigureModel figure)
    {
        if (Dispatcher.CheckAccess())
        {
            _figureWindows.ShowScriptFigure(number, figure);
        }
        else
        {
            Dispatcher.Invoke(() => _figureWindows.ShowScriptFigure(number, figure));
        }
    }

    private void CloseFigureOnUi(int number)
    {
        if (Dispatcher.CheckAccess())
        {
            _figureWindows.CloseScriptFigure(number);
        }
        else
        {
            Dispatcher.Invoke(() => _figureWindows.CloseScriptFigure(number));
        }
    }
}
