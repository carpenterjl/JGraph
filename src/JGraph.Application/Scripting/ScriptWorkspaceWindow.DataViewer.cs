using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using JGraph.Controls.Scripting;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Data.Import;
using JGraph.Scripting;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Data Viewer pane and the figure bridge: drilling a variable from the Variables pane into a
/// grid that follows the variable — refreshed after every statement, step and edit — and whose
/// cells write back into the workspace that owns the value; opening a data file straight from the
/// tree; and marshalling a script's figure onto the UI thread so it lands in a numbered figure window.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>The variable the Data Viewer is showing, or null when it shows a file or nothing.</summary>
    private string? _viewedVariable;

    /// <summary>The language whose workspace the Variables pane currently shows, or null when empty.</summary>
    private string? _variablesLanguage;

    private void OnVariablesDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedVariable();

    /// <summary>
    /// Fills the Workspace pane and keeps the Data Viewer on the variable it was showing — every
    /// path that changes the workspace ends here, which is what makes the viewer a live view rather
    /// than a snapshot of the moment it was opened.
    /// </summary>
    /// <param name="variables">The workspace as it now stands, or null to empty the pane.</param>
    /// <param name="language">The language whose workspace it is.</param>
    private void ShowVariables(IReadOnlyList<ScriptVariable>? variables, string? language)
    {
        VariablesList.ItemsSource = variables;
        _variablesLanguage = variables is null ? null : language;
        RefreshViewedVariable();
    }

    private void RefreshViewedVariable()
    {
        if (_viewedVariable is null)
        {
            return;
        }

        ScriptVariable? variable = ViewedVariable();
        TableGridAdapter? adapter = variable is null ? null : AdapterFor(variable);
        if (adapter is null)
        {
            // Cleared, or no longer something a grid can show: the viewer must not keep showing a
            // value the workspace no longer holds.
            _viewedVariable = null;
            DataViewer.CanEdit = false;
            DataViewer.Show(null);
            return;
        }

        DataViewer.Refresh(adapter);
        DataViewer.CanEdit = CanEdit(variable!);
    }

    /// <summary>The Workspace pane's entry for the viewed variable, or null when it has gone.</summary>
    private ScriptVariable? ViewedVariable() =>
        _viewedVariable is null
            ? null
            : (VariablesList.ItemsSource as IEnumerable<ScriptVariable>)?
                .FirstOrDefault(v => string.Equals(v.Name, _viewedVariable, StringComparison.Ordinal));

    /// <summary>
    /// Whether the viewed variable's cells can be written: its workspace can compose a write for it
    /// — the paused frame's debugger, or a session with the <see cref="IWorkspaceCellEditor"/>
    /// capability — asked about the value's last column, which is the value column of every grid
    /// shape (an index/value vector, a struct's Field/Type/Value, a table, a matrix).
    /// </summary>
    private bool CanEdit(ScriptVariable variable)
    {
        int lastColumn = variable.RawValue switch
        {
            double[] => 1,
            Table table => table.ColumnCount - 1,
            ScriptValueGrid grid => grid.ColumnNames.Count - 1,
            _ => -1,
        };
        return lastColumn >= 0 && ComposeCellAssignment(variable, 0, lastColumn, "0") is not null;
    }

    /// <summary>
    /// The statement that writes <paramref name="text"/> into one cell of <paramref name="variable"/>,
    /// from whichever workspace owns it right now, or null when nothing can write it.
    /// </summary>
    private string? ComposeCellAssignment(ScriptVariable variable, int row, int column, string text)
    {
        if (_debugSession is { IsPaused: true } debug)
        {
            return debug.ComposeCellAssignment(variable, row, column, text);
        }

        return _variablesLanguage is { } language && _sessions.TryGetValue(language, out IScriptSession? session)
            && session is IWorkspaceCellEditor editor
            ? editor.ComposeCellAssignment(variable, row, column, text)
            : null;
    }

    /// <summary>
    /// A cell was edited in the Data Viewer. The edit runs as the statement the owning workspace
    /// composes for it — in the paused frame while debugging, at the prompt otherwise — so it is
    /// interrupted, reported and reflected exactly as a typed one would be, and the viewer then
    /// re-reads the value, which is also what reverts the cell when the write fails.
    /// </summary>
    private async void OnDataViewerCellEdited(object sender, DataGridCellEditedEventArgs e)
    {
        ScriptVariable? variable = ViewedVariable();
        if (variable is null)
        {
            RefreshViewedVariable();
            return;
        }

        string? statement = ComposeCellAssignment(variable, e.Row, e.Column, e.Text);
        if (statement is null)
        {
            SetStatus($"That cell of '{variable.Name}' cannot be edited here.");
            RefreshViewedVariable();
            return;
        }

        if (_debugSession is { IsPaused: true })
        {
            if (IsEvaluating)
            {
                SetStatus("Busy — the previous K>> statement is still running.");
                RefreshViewedVariable();
                return;
            }

            await EvaluateAtPausedPromptAsync(statement).ConfigureAwait(true);
        }
        else if (_variablesLanguage is { } language)
        {
            await RunWorkspaceStatementAsync(language, statement).ConfigureAwait(true);
        }

        // A failed write leaves the value as it was; re-reading it is what takes the typed text
        // back out of the cell. A successful one has already refreshed through ShowVariables, but
        // a run that could not start (busy) has not.
        RefreshViewedVariable();
    }

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
        if (VariablesList.SelectedItem is ScriptVariable variable)
        {
            OpenVariable(variable);
        }
    }

    /// <summary>Shows <paramref name="variable"/> in the Data Viewer — the pane's double-click, and <c>open x</c> at the prompt.</summary>
    private void OpenVariable(ScriptVariable variable)
    {
        if (AdapterFor(variable) is { } adapter)
        {
            _viewedVariable = variable.Name;
            ShowInDataViewer(adapter, variable.Name);
            DataViewer.CanEdit = CanEdit(variable);
            return;
        }

        if (variable.RawValue is null && variable.Type is "array" or "cell" or "struct")
        {
            // Oversize values carry no raw copy (JgsRunner.MaxRawValueElements and
            // ScriptValueGrid.MaxCells) — the grid would freeze on millions of rows anyway.
            SetStatus($"'{variable.Name}' is too large for the data viewer — index a smaller slice to inspect it.");
        }
        else
        {
            SetStatus($"'{variable.Name}' has no tabular view — only arrays and tables do.");
        }
    }

    /// <summary>The grid projection of a variable's value, or null when it has no tabular view.</summary>
    private static TableGridAdapter? AdapterFor(ScriptVariable variable) => variable.RawValue switch
    {
        Table table => TableGridAdapter.ForTable(table),
        double[] array => TableGridAdapter.ForArray(array),
        ScriptValueGrid grid => TableGridAdapter.ForGrid(
            $"{grid.Kind} {grid.Rows.Count}×{grid.ColumnNames.Count}", grid.ColumnNames, grid.Rows),
        _ => null,
    };

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
            _viewedVariable = null; // a file has no workspace to write back into
            DataViewer.CanEdit = false;
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
