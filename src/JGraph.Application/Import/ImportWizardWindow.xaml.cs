using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using JGraph.Data;
using JGraph.Data.Import;
using Microsoft.Win32;

namespace JGraph.Application.Import;

/// <summary>
/// The data-import wizard dialog. All decisions live in <see cref="ImportWizardViewModel"/> (over the
/// UI-free <see cref="ImportWizardModel"/>); this code-behind owns only the parts that must touch WPF:
/// the file dialog, the clipboard, and building the dynamic preview grid.
/// </summary>
public partial class ImportWizardWindow : Window
{
    private const string FileFilter =
        "Data files (*.csv;*.tsv;*.txt;*.xlsx)|*.csv;*.tsv;*.txt;*.xlsx|All files (*.*)|*.*";

    private const int PreviewRowLimit = 100;

    private readonly ImportWizardViewModel _viewModel;

    public ImportWizardWindow(ImportWizardModel model)
    {
        _viewModel = new ImportWizardViewModel(model);
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.PreviewChanged += (_, _) => RefreshPreview();
    }

    private void OnCloseRequested(bool result)
    {
        DialogResult = result;
        Close();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a data file", Filter = FileFilter };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.LoadFile(dialog.FileName);
        }
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        string text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        if (ClipboardTableParser.LooksLikeTable(text))
        {
            _viewModel.LoadClipboardText(text);
        }
        else
        {
            MessageBox.Show(
                this,
                "The clipboard does not contain tabular text. Copy a range from a spreadsheet or a delimited block first.",
                "Paste from Clipboard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void RefreshPreview()
    {
        var gridView = new GridView();
        PreviewList.View = gridView;

        ImportResult? result = _viewModel.Model.Result;
        if (result is null)
        {
            PreviewList.ItemsSource = null;
            return;
        }

        Table table = result.Table;
        for (int c = 0; c < table.ColumnCount; c++)
        {
            TableColumn column = table.Columns[c];
            gridView.Columns.Add(new GridViewColumn
            {
                Header = $"{column.Name} ({column.Type})",
                DisplayMemberBinding = new Binding($"[{c}]"),
                Width = 120,
            });
        }

        int rowCount = System.Math.Min(table.RowCount, PreviewRowLimit);
        var rows = new List<string[]>(rowCount);
        for (int r = 0; r < rowCount; r++)
        {
            var cells = new string[table.ColumnCount];
            for (int c = 0; c < table.ColumnCount; c++)
            {
                cells[c] = table.Columns[c].GetText(r);
            }

            rows.Add(cells);
        }

        PreviewList.ItemsSource = rows;
    }
}
