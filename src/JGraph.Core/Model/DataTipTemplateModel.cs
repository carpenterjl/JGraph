using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>
/// One line of a data tip: a label, where its number comes from, and how to print it. The value is
/// either the name of one of the owning plot's own data properties (<c>'XData'</c>, <c>'SizeData'</c>)
/// or an array given outright, which is how MATLAB lets a tip show a column that is not plotted.
/// </summary>
public sealed class DataTipRowModel : GraphObject
{
    private string _label = string.Empty;
    private string _valueSource = string.Empty;
    private double[]? _valueData;
    private string _format = string.Empty;

    public DataTipRowModel() => Name = "DataTipTextRow";

    public DataTipRowModel(string label, string valueSource)
        : this()
    {
        _label = label;
        _valueSource = valueSource;
    }

    /// <summary>What is written to the left of the number.</summary>
    [Category("General")]
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <summary>The owning plot's data property this row reads, empty when the row carries its own.</summary>
    [Browsable(false)]
    public string ValueSource
    {
        get => _valueSource;
        set => SetProperty(ref _valueSource, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <summary>The numbers this row shows when it does not read one of the plot's own channels.</summary>
    [Browsable(false)]
    public double[]? ValueData
    {
        get => _valueData;
        set => SetProperty(ref _valueData, value, InvalidationKind.Render);
    }

    /// <summary>A format for the number — MATLAB's own words ('auto', 'usd') or a printf spec.</summary>
    [Category("General")]
    public string Format
    {
        get => _format;
        set => SetProperty(ref _format, value ?? string.Empty, InvalidationKind.Render);
    }
}

/// <summary>
/// What a data tip on this series says. The rows are consulted when a tip is placed, so a script that
/// renames a row or adds one changes every tip taken afterwards — which is the whole point of a
/// template rather than a property on each placed tip.
/// </summary>
public sealed class DataTipTemplateModel : GraphObject
{
    private readonly List<DataTipRowModel> _rows = new();
    private string _interpreter = "tex";

    public DataTipTemplateModel() => Name = "DataTipTemplate";

    /// <summary>The rows, in the order they are written.</summary>
    [Browsable(false)]
    public IReadOnlyList<DataTipRowModel> DataTipRows => _rows;

    /// <summary>How the labels are read: 'tex' (the default), 'latex', or 'none'.</summary>
    [Category("General")]
    public string Interpreter
    {
        get => _interpreter;
        set => SetProperty(ref _interpreter, value ?? "tex", InvalidationKind.Render);
    }

    /// <summary>Replaces every row. The template owns its rows, so each one is re-parented here.</summary>
    public void SetRows(IEnumerable<DataTipRowModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (DataTipRowModel old in _rows)
        {
            old.SetParent(null);
        }

        _rows.Clear();
        foreach (DataTipRowModel row in rows)
        {
            row.SetParent(this);
            _rows.Add(row);
        }

        Invalidate(InvalidationKind.Render);
    }

    /// <summary>Adds one row at the end.</summary>
    public void AddRow(DataTipRowModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.SetParent(this);
        _rows.Add(row);
        Invalidate(InvalidationKind.Render);
    }
}
