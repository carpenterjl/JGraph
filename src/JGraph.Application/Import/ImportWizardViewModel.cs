using System.Collections.ObjectModel;
using System.Globalization;
using JGraph.Application.Mvvm;
using JGraph.Data.Import;

namespace JGraph.Application.Import;

/// <summary>
/// A thin <see cref="ObservableObject"/> adapter over the UI-free <see cref="ImportWizardModel"/>. It
/// exposes bindable choices for the wizard's two pages and forwards every decision to the model, which
/// owns the real logic. Property changes are re-raised whenever the model re-parses.
/// </summary>
public sealed class ImportWizardViewModel : ObservableObject
{
    private static readonly CultureInfo CommaCulture = CreateCommaCulture();

    private readonly ImportWizardModel _model;
    private int _page = 1;

    public ImportWizardViewModel(ImportWizardModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _model.Parsed += (_, _) => OnParsed();

        YColumns = new ObservableCollection<YColumnItem>();

        BackCommand = new RelayCommand(() => Page = 1, () => Page == 2);
        NextCommand = new RelayCommand(() => Page = 2, () => Page == 1 && _model.Result is not null);
        ImportCommand = new RelayCommand(() => CloseRequested?.Invoke(true), () => _model.CanBuild);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
    }

    /// <summary>Raised when the wizard should close; the argument is the dialog result.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Raised after a re-parse so the view can rebuild its (dynamic) preview grid.</summary>
    public event EventHandler? PreviewChanged;

    public ImportWizardModel Model => _model;

    public IReadOnlyList<string> DelimiterChoices { get; } = new[] { "Auto", "Comma", "Semicolon", "Tab", "Pipe" };

    public IReadOnlyList<string> HeaderChoices { get; } = new[] { "Auto", "Yes", "No" };

    public IReadOnlyList<string> DecimalChoices { get; } = new[] { "Auto", "Point (.)", "Comma (,)" };

    public ObservableCollection<YColumnItem> YColumns { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand NextCommand { get; }

    public RelayCommand ImportCommand { get; }

    public RelayCommand CancelCommand { get; }

    public int Page
    {
        get => _page;
        set
        {
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(IsPage1));
                OnPropertyChanged(nameof(IsPage2));
                BackCommand.RaiseCanExecuteChanged();
                NextCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsPage1 => _page == 1;

    public bool IsPage2 => _page == 2;

    public string FilePathDisplay => _model.SourceKind switch
    {
        ImportSourceKind.ClipboardText => "(pasted from clipboard)",
        ImportSourceKind.None => "(no data loaded)",
        _ => _model.FilePath ?? string.Empty,
    };

    public bool ShowSheetSelector => _model.SourceKind == ImportSourceKind.XlsxFile && _model.SheetNames.Count > 0;

    public IReadOnlyList<string> SheetNames => _model.SheetNames;

    public string? SelectedSheet
    {
        get => _model.SheetName;
        set => _model.SheetName = value;
    }

    public string SelectedDelimiter
    {
        get => _model.Delimiter switch
        {
            ',' => "Comma",
            ';' => "Semicolon",
            '\t' => "Tab",
            '|' => "Pipe",
            _ => "Auto",
        };
        set => _model.Delimiter = value switch
        {
            "Comma" => ',',
            "Semicolon" => ';',
            "Tab" => '\t',
            "Pipe" => '|',
            _ => null,
        };
    }

    public string SelectedHeader
    {
        get => _model.HasHeader switch { true => "Yes", false => "No", _ => "Auto" };
        set => _model.HasHeader = value switch { "Yes" => true, "No" => false, _ => (bool?)null };
    }

    public string SelectedDecimal
    {
        get => _model.Culture is null ? "Auto"
            : Equals(_model.Culture, CultureInfo.InvariantCulture) ? "Point (.)" : "Comma (,)";
        set => _model.Culture = value switch
        {
            "Point (.)" => CultureInfo.InvariantCulture,
            "Comma (,)" => CommaCulture,
            _ => null,
        };
    }

    public int SkipRows
    {
        get => _model.SkipRows;
        set => _model.SkipRows = value;
    }

    public string? Error => _model.Error;

    public string Warnings => _model.Result is { Warnings.Count: > 0 }
        ? string.Join(Environment.NewLine, _model.Result.Warnings)
        : string.Empty;

    public bool HasError => _model.Error is not null;

    public bool HasWarnings => _model.Result is { Warnings.Count: > 0 };

    // ----- Page 2: mapping -----

    public IReadOnlyList<string> XChoices
    {
        get
        {
            var choices = new List<string> { RowIndexLabel };
            choices.AddRange(_model.XColumnChoices);
            return choices;
        }
    }

    public string SelectedX
    {
        get => _model.XColumn ?? RowIndexLabel;
        set
        {
            _model.XColumn = value == RowIndexLabel ? null : value;
            RefreshMapping();
        }
    }

    public IReadOnlyList<string> ErrorChoices
    {
        get
        {
            var choices = new List<string> { NoneLabel };
            choices.AddRange(_model.NumericColumnNames);
            return choices;
        }
    }

    public string SelectedError
    {
        get => _model.ErrorColumn ?? NoneLabel;
        set
        {
            _model.ErrorColumn = value == NoneLabel ? null : value;
            RefreshMapping();
        }
    }

    public IReadOnlyList<ImportPlotKind> PlotKinds => _model.AllowedPlotKinds;

    public ImportPlotKind SelectedPlotKind
    {
        get => _model.PlotKind;
        set
        {
            _model.PlotKind = value;
            OnPropertyChanged(nameof(ShowHistogramBins));
            OnPropertyChanged(nameof(ShowErrorColumn));
            ImportCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ShowHistogramBins => _model.PlotKind == ImportPlotKind.Histogram;

    public bool ShowErrorColumn => _model.PlotKind == ImportPlotKind.ErrorBar;

    public int HistogramBins
    {
        get => _model.HistogramBins;
        set => _model.HistogramBins = value;
    }

    public bool IsNewFigure
    {
        get => _model.Target == ImportTarget.NewFigure;
        set { if (value) { _model.Target = ImportTarget.NewFigure; } }
    }

    public bool IsCurrentAxes
    {
        get => _model.Target == ImportTarget.CurrentAxes;
        set { if (value) { _model.Target = ImportTarget.CurrentAxes; } }
    }

    /// <summary>Loads a file into the model (called by the view after a file dialog).</summary>
    public void LoadFile(string path)
    {
        _model.LoadFile(path);
        OnPropertyChanged(nameof(SelectedSheet));
    }

    /// <summary>Loads pasted text into the model (called by the view after reading the clipboard).</summary>
    public void LoadClipboardText(string text) => _model.LoadClipboardText(text);

    private const string RowIndexLabel = "(row index)";
    private const string NoneLabel = "(none)";

    private void OnParsed()
    {
        RebuildYColumns();
        OnPropertyChanged(nameof(FilePathDisplay));
        OnPropertyChanged(nameof(ShowSheetSelector));
        OnPropertyChanged(nameof(SheetNames));
        OnPropertyChanged(nameof(SelectedSheet));
        OnPropertyChanged(nameof(SelectedDelimiter));
        OnPropertyChanged(nameof(SelectedHeader));
        OnPropertyChanged(nameof(SelectedDecimal));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(XChoices));
        OnPropertyChanged(nameof(ErrorChoices));
        RefreshMapping();
        NextCommand.RaiseCanExecuteChanged();
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshMapping()
    {
        OnPropertyChanged(nameof(SelectedX));
        OnPropertyChanged(nameof(SelectedError));
        OnPropertyChanged(nameof(PlotKinds));
        OnPropertyChanged(nameof(SelectedPlotKind));
        OnPropertyChanged(nameof(ShowHistogramBins));
        OnPropertyChanged(nameof(ShowErrorColumn));
        ImportCommand.RaiseCanExecuteChanged();
    }

    private void RebuildYColumns()
    {
        YColumns.Clear();
        foreach (string name in _model.NumericColumnNames)
        {
            YColumns.Add(new YColumnItem(name, _model, this));
        }
    }

    private void NotifyYColumnChanged() => RefreshMapping();

    private static CultureInfo CreateCommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>A selectable Y column (a checkbox row on page 2).</summary>
    public sealed class YColumnItem : ObservableObject
    {
        private readonly ImportWizardModel _model;
        private readonly ImportWizardViewModel _owner;

        public YColumnItem(string name, ImportWizardModel model, ImportWizardViewModel owner)
        {
            Name = name;
            _model = model;
            _owner = owner;
        }

        public string Name { get; }

        public bool IsSelected
        {
            get => _model.IsYColumnSelected(Name);
            set
            {
                _model.SetYColumnSelected(Name, value);
                OnPropertyChanged();
                _owner.NotifyYColumnChanged();
            }
        }
    }
}
