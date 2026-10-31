using System.Globalization;
using System.Text.RegularExpressions;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// What R2025b's sheet says about a property's editor (the App Designer window plan's stage W7c): the widgets that show and
/// edit it in place, its rich editor, and of <c>richEditorProperties</c> what holds for every instance.
/// </summary>
public sealed record DesignEditorInfo
{
    /// <summary>No editor named.</summary>
    public static DesignEditorInfo None { get; } = new();

    /// <summary>The widget that shows the value (<c>renderer</c>'s last part).</summary>
    public string Renderer { get; init; } = string.Empty;

    /// <summary>The widget that edits it in place (<c>inPlaceEditor</c>'s last part).</summary>
    public string InPlace { get; init; } = string.Empty;

    /// <summary>The rich editor behind the row's kebab (<c>richEditor</c>'s last part); empty for none.</summary>
    public string Rich { get; init; } = string.Empty;

    /// <summary>A choice's words (<c>categories</c>).</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Whether a choice takes only its words (<c>isProtected</c>).</summary>
    public bool IsProtected { get; init; } = true;

    /// <summary>A toggle button's or button group's words (<c>enumeratedValues</c>).</summary>
    public IReadOnlyList<string> Enumerated { get; init; } = [];

    /// <summary>A button group's icons (<c>icons</c>), or one toggle button's (<c>icon</c>).</summary>
    public IReadOnlyList<string> Icons { get; init; } = [];

    /// <summary>The fields of a value edited as fields (<c>fields</c>).</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

    /// <summary>What the fields must satisfy (<c>validation</c>).</summary>
    public IReadOnlyList<string> Validation { get; init; } = [];

    /// <summary>The fewest items (<c>minNumber</c>).</summary>
    public double? MinNumber { get; init; }

    /// <summary>The most items (<c>maxNumber</c>).</summary>
    public double? MaxNumber { get; init; }

    /// <summary>Which word a new item is named from (<c>defaultNameKey</c>).</summary>
    public string DefaultNameKey { get; init; } = string.Empty;

    /// <summary>The form a text area commits (<c>outputType</c>).</summary>
    public string OutputType { get; init; } = string.Empty;

    /// <summary>The colour words offered beside a colour (<c>styleOptions</c>).</summary>
    public IReadOnlyList<string> StyleOptions { get; init; } = [];

    /// <summary>A spinner's least value (<c>MinValue</c>).</summary>
    public double? Minimum { get; init; }

    /// <summary>A spinner's greatest value (<c>MaxValue</c>).</summary>
    public double? Maximum { get; init; }

    /// <summary>A spinner's step (<c>Step</c>).</summary>
    public double Step { get; init; } = 1;
}

/// <summary>
/// The names <c>UniqueNameGenerator</c> gives a new row of an items table (<c>new UniqueNameGenerator(true, " ")</c>): the word of
/// <c>Item %1</c>, a space and one more than the highest number already following it.
/// </summary>
public static class DesignItemNames
{
    private static readonly Regex Trailing = new(@"\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>A name for a new row among <paramref name="items"/>, from <paramref name="defaultName"/> (<c>generateVariableName</c>).</summary>
    public static string Next(IReadOnlyList<string> items, string defaultName)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(defaultName);
        string word = defaultName.Split(' ')[0];
        bool huge = false;
        long highest = 0;
        foreach (string item in items)
        {
            if (string.IsNullOrEmpty(item) || !item.Contains(word, StringComparison.Ordinal))
            {
                continue;
            }

            string squeezed = Space.Replace(item, string.Empty);
            if (word.Length < item.Length && !IsDigitOrSpaceOrBar(item[word.Length]))
            {
                continue;
            }

            long number;
            string stem;
            Match digits = Trailing.Match(squeezed);
            if (digits.Success)
            {
                stem = squeezed[..digits.Index];
                if (digits.Length > 15)
                {
                    huge = true;
                    continue;
                }

                number = long.Parse(digits.Value, CultureInfo.InvariantCulture);
                highest = System.Math.Max(highest, number);
            }
            else
            {
                stem = squeezed;
                number = 1;
            }

            if (stem == word && number > highest)
            {
                highest = number;
            }
        }

        if (huge)
        {
            return Free(word, items, 0);
        }

        if (highest == 0)
        {
            return word + " 1";
        }

        Match tail = Trailing.Match(word);
        return Free(tail.Success ? word[..tail.Index] : word, items, highest);
    }

    private static bool IsDigitOrSpaceOrBar(char c) => c is '|' || (c >= '0' && c <= '9') || char.IsWhiteSpace(c);

    private static string Free(string word, IReadOnlyList<string> items, long from)
    {
        long n = from;
        string name;
        do
        {
            n++;
            name = word + " " + n.ToString(CultureInfo.InvariantCulture);
        }
        while (items.Contains(name));
        return name;
    }

    /// <summary>The word a new row is named from (<c>defaultItemName</c>, <c>defaultStateName</c>, <c>defaultOptionName</c>).</summary>
    public static string DefaultName(string key) => key switch
    {
        "defaultItemName" => "Item %1",
        "defaultStateName" => "State %1",
        "defaultOptionName" => "Option %1",
        _ => string.Empty,
    };
}

/// <summary>A change an editor's table asks for: the property, what it held and what it is to hold.</summary>
public sealed record DesignEditorCommit(string Property, object? Before, object? After);

/// <summary>The table behind Items (<c>ItemsEditor</c>, <c>ItemsModel</c>, <c>_PSTableController</c>, <c>_PSTable</c>): a radio button and an item on each row.</summary>
public sealed class DesignItemsModel
{
    private readonly double _minimum;
    private readonly double _maximum;
    private readonly string _defaultName;

    /// <summary>A table over <paramref name="items"/> with <paramref name="selectedIndex"/> selected, counted from 1.</summary>
    public DesignItemsModel(IReadOnlyList<string> items, int selectedIndex, DesignEditorInfo info)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(info);
        Items = [.. items];
        SelectedIndex = selectedIndex;
        _minimum = info.MinNumber ?? double.NegativeInfinity;
        _maximum = info.MaxNumber ?? double.PositiveInfinity;
        _defaultName = DesignItemNames.DefaultName(info.DefaultNameKey);
    }

    /// <summary>The items.</summary>
    public List<string> Items { get; private set; }

    /// <summary>Which is selected, from 1; -1 for none.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Whether the plus and minus buttons show at all: not for a fixed number of items (<c>_updateAddButtonState</c>).</summary>
    public bool ButtonsShown => _minimum != _maximum;

    /// <summary>Whether another row may be added.</summary>
    public bool CanAdd => Items.Count < _maximum;

    /// <summary>Whether the selected row may go.</summary>
    public bool CanDelete(int selectedRow) => Items.Count > _minimum && selectedRow >= 0;

    /// <summary>Adds a row after <paramref name="selectedRow"/>, or last (<c>addRow</c>); returns the row then selected, the last.</summary>
    public IReadOnlyList<DesignEditorCommit> AddRow(int selectedRow, out int rowToSelect)
    {
        var items = new List<string>(Items);
        rowToSelect = -1;
        IReadOnlyList<DesignEditorCommit> commits = [];
        if (items.Count < _maximum)
        {
            int at = selectedRow >= 0 && selectedRow < items.Count - 1 ? selectedRow + 1 : items.Count;
            string name = DesignItemNames.Next(items, _defaultName);
            int selected = SelectedIndex;
            if (items.Count == 0)
            {
                selected = 1;
            }

            if (at + 1 <= selected)
            {
                selected = SelectedIndex + 1;
            }

            items.Insert(at, name);
            commits = Apply(items, selected);
        }

        rowToSelect = Items.Count - 1;
        return commits;
    }

    /// <summary>Deletes <paramref name="row"/> (<c>deleteRow</c>); returns the row then selected, or -1 for none.</summary>
    public IReadOnlyList<DesignEditorCommit> DeleteRow(int row, out int rowToSelect)
    {
        IReadOnlyList<DesignEditorCommit> commits = [];
        if (Items.Count > _minimum && row >= 0 && row < Items.Count)
        {
            var items = new List<string>(Items);
            int was = SelectedIndex;
            int selected = was;
            if (row + 1 == was)
            {
                selected = items.Count == 1 ? -1 : was > 1 ? was - 1 : was;
            }
            else if (row + 1 < was)
            {
                selected = was - 1;
            }

            items.RemoveAt(row);
            commits = Apply(items, selected);
        }

        rowToSelect = Items.Count == 0 ? -1 : row > 0 ? row - 1 : 0;
        return commits;
    }

    /// <summary>Row <paramref name="row"/>'s words changed (<c>updateItems</c>).</summary>
    public IReadOnlyList<DesignEditorCommit> SetItem(int row, string text)
    {
        var items = new List<string>(Items)
        {
            [row] = text,
        };
        return Apply(items, SelectedIndex);
    }

    /// <summary>Row <paramref name="row"/>'s radio button pressed (<c>updateSelectedIndex</c>).</summary>
    public IReadOnlyList<DesignEditorCommit> Select(int row) => Apply(Items, row + 1);

    // _handleModelUpdate: a selected item gone says its new index first, then the items; else the index alone, if it moved.
    private List<DesignEditorCommit> Apply(List<string> items, int selected)
    {
        List<string> oldItems = Items;
        int oldSelected = SelectedIndex;
        Items = items;
        SelectedIndex = selected;
        var commits = new List<DesignEditorCommit>();
        if (!oldItems.SequenceEqual(items))
        {
            string? wasSelected = oldSelected >= 1 && oldSelected <= oldItems.Count ? oldItems[oldSelected - 1] : null;
            if (wasSelected is null || !items.Contains(wasSelected))
            {
                commits.Add(new DesignEditorCommit("SelectedIndex", oldSelected, selected));
            }

            commits.Add(new DesignEditorCommit("Items", oldItems, items));
        }
        else if (selected != oldSelected)
        {
            commits.Add(new DesignEditorCommit("SelectedIndex", oldSelected, selected));
        }

        return commits;
    }
}

/// <summary>
/// The table behind ticks and their labels (<c>TicksDialogEditor</c>, <c>TickDialogTableModel</c>): a number or text in the first column,
/// the label in the second. Commits go out only while the ticks are in ascending order; while they are not, the first column's bad rows
/// are marked and <see cref="Error"/> says so.
/// </summary>
public sealed class DesignTicksModel
{
    /// <summary>What the editor says for ticks out of order (<c>ticksEditorErrorMsg</c>).</summary>
    public const string OutOfOrder = "Specify tick values in ascending order";

    /// <summary>What it says for too small a step (<c>ticksEditorStebByError</c>).</summary>
    public const string StepTooSmall = "Step value is too small";

    private const string Manual = "manual";
    private readonly string _ticksName;
    private readonly string _labelsName;
    private readonly List<DesignEditorCommit> _commits = [];
    private double[] _limits;
    private string _tickMode;
    private string _labelMode;
    private bool _labelsFewer;
    private bool _forced;
    private bool _bad;
    private List<(object Tick, string Label)> _old = [];

    /// <summary>A table over the ticks and labels a component holds.</summary>
    public DesignTicksModel(
        string ticksName,
        string labelsName,
        IReadOnlyList<double> ticks,
        IReadOnlyList<string> labels,
        double[] limits,
        string tickMode,
        string labelMode)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(labels);
        _ticksName = ticksName;
        _labelsName = labelsName;
        _limits = limits;
        _tickMode = tickMode;
        _labelMode = labelMode;
        SetTable(ticks.Cast<object>().ToList(), labels);
    }

    /// <summary>Each row: a number, or text that is not one, and its label.</summary>
    public List<(object Tick, string Label)> Rows { get; private set; } = [];

    /// <summary>The words over the table, if any.</summary>
    public string Error { get; private set; } = string.Empty;

    /// <summary>Which rows' ticks are marked as out of order.</summary>
    public HashSet<int> Marked { get; } = [];

    /// <summary>The row selected, by this editor's own rule, once a change is done; -1 to leave it.</summary>
    public int RowToSelect { get; private set; } = -1;

    /// <summary>What the last change committed, in order.</summary>
    public IReadOnlyList<DesignEditorCommit> Commits => _commits;

    /// <summary>The ticks' mode.</summary>
    public string TickMode => _tickMode;

    /// <summary>The labels' mode.</summary>
    public string LabelMode => _labelMode;

    /// <summary>The Step by shown over the ticks: their interval if they share just one (<c>getCurrentInterval</c>), else null.</summary>
    public double? CurrentInterval
    {
        get
        {
            var seen = new List<double>();
            for (int i = 0; i < Rows.Count - 1; i++)
            {
                double step = System.Math.Abs(((AsNumber(Rows[i + 1].Tick) * 10) - (AsNumber(Rows[i].Tick) * 10)) / 10);
                if (seen.Count == 0 || seen[0] != step)
                {
                    seen.Add(step); // as the editor counts (r.indexOf(l)&&r.push(l)): a repeat of the first alone is not counted
                }
            }

            return seen.Count == 1 ? seen[0] : null;
        }
    }

    /// <summary>The limits changed (<c>setLimits</c>).</summary>
    public void SetLimits(double[] limits) => _limits = limits;

    /// <summary>Adds a row after <paramref name="selectedRow"/>, or at the end (<c>_addRow</c>, <c>_addNumericRow</c>).</summary>
    public void AddRow(int selectedRow)
    {
        Begin();
        int end = Rows.Count - 1;
        int at = selectedRow >= 0 && selectedRow < end ? selectedRow : end;
        if (at >= 0 && at < Rows.Count && Marked.Contains(at))
        {
            return;
        }

        at++;
        object tick = _limits[0];
        if (at > 0)
        {
            object before = Rows[at - 1].Tick;
            if (at >= Rows.Count)
            {
                double step = CurrentInterval ?? 1;
                tick = before is double b ? b + step : before;
            }
            else
            {
                object after = Rows[at].Tick;
                tick = before is double l && after is double r ? (r + l) / 2 : "NaN";
            }
        }

        if (tick is not double number)
        {
            return;
        }

        number = System.Math.Round(number, 5, MidpointRounding.AwayFromZero);
        string label = DesignDocument.NumberText(number);
        var rows = new List<(object Tick, string Label)>(Rows);
        rows.Insert(at, (number, label));
        var ticks = rows.Select(static r => r.Tick).ToList();
        var labels = rows.Select(static r => r.Label).ToList();
        List<object> oldTicks = _old.Select(static r => r.Tick).ToList();
        List<string> oldLabels = _old.Select(static r => r.Label).ToList();
        Updated(_ticksName, ticks, oldTicks);
        if (_labelMode == Manual || _forced || _labelsFewer)
        {
            labels[at] = label;
            Updated(_labelsName, labels, oldLabels);
        }

        SetTable(ticks, labels);
        RowToSelect = at;
    }

    /// <summary>Deletes <paramref name="row"/> (<c>deleteRow</c>), selecting the row before it.</summary>
    public void DeleteRow(int row)
    {
        Begin();
        Delete(row);
    }

    private void Delete(int row)
    {
        var rows = new List<(object Tick, string Label)>(Rows);
        rows.RemoveAt(row);
        var ticks = rows.Select(static r => r.Tick).ToList();
        var labels = rows.Select(static r => r.Label).ToList();
        SetTable(ticks, labels);
        RowToSelect = row == 0 ? 0 : row - 1;
        if (row == 0)
        {
            RowToSelect = 0;
        }

        List<object> oldTicks = _old.Select(static r => r.Tick).ToList();
        List<string> oldLabels = _old.Select(static r => r.Label).ToList();
        Updated(_ticksName, ticks, oldTicks);
        if (_labelMode == Manual || _forced)
        {
            Updated(_labelsName, labels, oldLabels);
        }
    }

    /// <summary>A tick cell typed into (<c>_resolve</c>); empty text deletes the row.</summary>
    public void EditTick(int row, string text)
    {
        Begin();
        if (text.Length == 0)
        {
            Rows[row] = (string.Empty, Rows[row].Label);
            Delete(row);
            return;
        }

        bool labelsToo = false;
        if (!TryJsonNumber(text, out double number))
        {
            Rows[row] = (text, Rows[row].Label); // _evalInMATLAB: what it cannot make a number of stays as it is, and is marked
            UpdateAndCommit(row, 0, text, ticks: true, labels: false);
            return;
        }

        if (_old[row].Tick is double was && was.Equals(number))
        {
            Rows[row] = (number, Rows[row].Label);
            return;
        }

        if (number < _limits[0] || number > _limits[1])
        {
            if (row == 0 || row == Rows.Count - 1)
            {
                var copied = new List<(object Tick, string Label)>(_old)
                {
                    [row] = (number, DesignDocument.NumberText(number)),
                };
                labelsToo = true;
                SetTable(copied.Select(static r => r.Tick).ToList(), copied.Select(static r => r.Label).ToList());
            }
        }

        UpdateAndCommit(row, 0, number, ticks: true, labels: labelsToo);
    }

    /// <summary>A label cell typed into (<c>_resolve</c>).</summary>
    public void EditLabel(int row, string text)
    {
        Begin();
        UpdateAndCommit(row, 1, text, ticks: false, labels: true);
    }

    /// <summary>Step by a number typed in the ticks' header menu (<c>updateInterval</c>).</summary>
    public void SetInterval(double step)
    {
        Begin();
        if (System.Math.Abs((_limits[1] - _limits[0]) / step) > 500)
        {
            Error = StepTooSmall;
            return;
        }

        var ticks = new List<object>();
        List<object> oldTicks = _old.Select(static r => r.Tick).ToList();
        List<string> oldLabels = _old.Select(static r => r.Label).ToList();
        for (double t = _limits[0]; t <= _limits[1]; t = ((10 * t) + (10 * step)) / 10)
        {
            if (t < _limits[0])
            {
                return;
            }

            ticks.Add(t);
        }

        var labels = new List<string>();
        if (_labelMode != Manual)
        {
            labels.AddRange(ticks.Select(static t => DesignDocument.NumberText((double)t)));
        }
        else
        {
            foreach (object t in ticks)
            {
                int at = oldTicks.FindIndex(o => o.Equals(t));
                labels.Add(at != -1 ? oldLabels[at] : string.Empty);
            }
        }

        SetTable(ticks, labels);
        Updated(_ticksName, ticks, oldTicks);
        if (_labelMode == Manual)
        {
            Updated(_labelsName, labels, oldLabels);
        }
    }

    /// <summary>The labels' Auto in their header menu (<c>setLabelsToAuto</c>).</summary>
    public void LabelsToAuto()
    {
        Begin();
        if (_labelMode == Manual)
        {
            SetTable(Rows.Select(static r => r.Tick).ToList(), Rows.Select(static r => r.Tick is double d ? DesignDocument.NumberText(d) : (string)r.Tick).ToList());
        }

        ClearStep();
        _labelMode = "auto";
        _commits.Add(new DesignEditorCommit(_labelsName + "Mode", Manual, "auto"));
    }

    /// <summary>The ticks' Auto in their header menu.</summary>
    public void TicksToAuto()
    {
        Begin();
        ClearStep();
        _tickMode = "auto";
        _commits.Add(new DesignEditorCommit(_ticksName + "Mode", Manual, "auto"));
    }

    /// <summary>What the component holds now, read back (<c>_updateEditor</c>).</summary>
    public void Refresh(IReadOnlyList<double> ticks, IReadOnlyList<string> labels, string tickMode, string labelMode)
    {
        _tickMode = tickMode;
        _labelMode = labelMode;
        SetTable(ticks.Cast<object>().ToList(), labels);
        Error = string.Empty;
        _bad = false;
        Marked.Clear();
    }

    private void Begin()
    {
        _commits.Clear();
        RowToSelect = -1;
    }

    // _clearStebyError: only the step's words go.
    private void ClearStep()
    {
        if (Error == StepTooSmall)
        {
            Error = string.Empty;
        }
    }

    private void UpdateAndCommit(int row, int column, object value, bool ticks, bool labels)
    {
        (object Tick, string Label) cell = Rows[row];
        Rows[row] = column == 0 ? (value, cell.Label) : (cell.Tick, (string)value);
        var newTicks = Rows.Select(static r => r.Tick).ToList();
        var newLabels = Rows.Select(static r => r.Label).ToList();
        List<object> oldTicks = _old.Select(static r => r.Tick).ToList();
        List<string> oldLabels = _old.Select(static r => r.Label).ToList();
        SetTable(newTicks, newLabels);
        if (ticks)
        {
            Updated(_ticksName, newTicks, oldTicks);
        }

        if (labels || _forced)
        {
            Updated(_labelsName, newLabels, oldLabels);
        }
    }

    // _generateTableData: labels fewer than ticks are repeated round them.
    private void SetTable(List<object> ticks, IReadOnlyList<string> labels)
    {
        var rows = new List<(object Tick, string Label)>();
        if (ticks.Count > 0 && labels.Count > 0 && labels.Count < ticks.Count)
        {
            _labelsFewer = true;
            for (int i = 0; i < ticks.Count; i++)
            {
                rows.Add((ticks[i], labels[i % labels.Count]));
            }
        }
        else
        {
            _labelsFewer = false;
            for (int i = 0; i < ticks.Count; i++)
            {
                rows.Add((ticks[i], i < labels.Count ? labels[i] : string.Empty));
            }
        }

        Rows = rows;
        _old = [.. rows];
    }

    // _handleModelUpdate: ticks are checked; while bad nothing goes out, and labels wait to go with the next good ticks.
    private void Updated(string property, object value, object old)
    {
        if (property == _ticksName)
        {
            _bad = FlagInvalid((List<object>)value);
            Error = _bad ? OutOfOrder : string.Empty;
        }

        if (_bad)
        {
            if (property == _labelsName)
            {
                _forced = true;
            }

            return;
        }

        if (property == _labelsName)
        {
            _forced = false;
            _labelMode = Manual;
        }
        else
        {
            _tickMode = Manual;
        }

        _commits.Add(new DesignEditorCommit(property, old, value));
    }

    private bool FlagInvalid(List<object> ticks)
    {
        bool bad = false;
        Marked.Clear();
        for (int i = 0; i < ticks.Count; i++)
        {
            if (ticks[i] is string)
            {
                Marked.Add(i);
                bad = true;
                continue;
            }

            if (i + 1 < ticks.Count && AsNumber(ticks[i]) >= AsNumber(ticks[i + 1]))
            {
                bad = true;
                if (i + 1 == ticks.Count - 1)
                {
                    Marked.Add(i + 1);
                    break;
                }

                Marked.Add(i);
            }
        }

        return bad;
    }

    private static double AsNumber(object tick) => tick switch
    {
        double d => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        string { Length: 0 } => 0,
        _ => double.NaN,
    };

    // JSON.parse giving a number.
    private static bool TryJsonNumber(string text, out double number)
    {
        number = 0;
        string t = text.Trim();
        return Regex.IsMatch(t, @"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$", RegexOptions.CultureInvariant)
            && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}

/// <summary>The table behind ScaleColors and ScaleColorLimits (<c>ScaleColorsEditor</c>, <c>ScaleColorsModel</c>): a colour, Start and End on each row.</summary>
public sealed class DesignScaleColorsModel
{
    private static readonly double[] DefaultColor = [1, 0, 0];
    private static readonly Regex Letters = new("[a-d|f-z]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string _colorsName;
    private readonly string _limitsName;

    /// <summary>A table over the colours and limits a component holds; each is a row of a matrix.</summary>
    public DesignScaleColorsModel(IReadOnlyList<double[]> colors, IReadOnlyList<double[]> limits, double[] range, string colorsName = "ScaleColors", string limitsName = "ScaleColorLimits")
    {
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(limits);
        Colors = [.. colors];
        Limits = [.. limits];
        Range = range;
        _colorsName = colorsName;
        _limitsName = limitsName;
        Table = Generate();
    }

    /// <summary>The colours, as the component holds them.</summary>
    public List<double[]> Colors { get; private set; }

    /// <summary>The limits, as the component holds them.</summary>
    public List<double[]> Limits { get; private set; }

    /// <summary>The component's limits.</summary>
    public double[] Range { get; set; }

    /// <summary>The rows shown.</summary>
    public List<(double[] Color, double Start, double End)> Table { get; private set; }

    /// <summary>A row's colour chosen, rounded to 4 places (<c>_onTableEdit</c>, <c>updateScaleColor</c>).</summary>
    public IReadOnlyList<DesignEditorCommit> SetColor(int row, double[] color)
    {
        Table[row] = (color.Select(static c => System.Math.Round(c * 1e4, MidpointRounding.AwayFromZero) / 1e4).ToArray(), Table[row].Start, Table[row].End);
        return Apply(Table.Select(static r => r.Color).ToList(), Limits);
    }

    /// <summary>A row's Start typed in (<c>updateStartValue</c>).</summary>
    /// <returns>What goes out; null with <paramref name="refused"/> saying why, and the table goes back.</returns>
    public IReadOnlyList<DesignEditorCommit>? SetStart(int row, string text, out string? refused)
    {
        refused = null;
        if (!IsNumber(text))
        {
            refused = "Value must be numeric.";
            Table = Generate();
            return null;
        }

        Table[row] = (Table[row].Color, Parse(text), Table[row].End);
        return Apply(Colors, Table.Select(static r => new[] { r.Start, r.End }).ToList());
    }

    /// <summary>A row's End typed in (<c>updateEndValue</c>).</summary>
    /// <returns>What goes out; null with <paramref name="refused"/> saying why, and the table goes back.</returns>
    public IReadOnlyList<DesignEditorCommit>? SetEnd(int row, string text, out string? refused)
    {
        refused = null;
        if (!IsNumber(text))
        {
            refused = "Value must be numeric.";
            Table = Generate();
            return null;
        }

        Table[row] = (Table[row].Color, Table[row].Start, Parse(text));
        return Apply(Colors, Table.Select(static r => new[] { r.Start, r.End }).ToList());
    }

    /// <summary>A row added after <paramref name="selectedRow"/>, or last (<c>_onAddBtnClick</c>, <c>addRow</c>): red, over the top tenth of the range.</summary>
    public IReadOnlyList<DesignEditorCommit> AddRow(int selectedRow, out int rowToSelect)
    {
        int at = selectedRow == -1 ? Table.Count : selectedRow + 1;
        double start = 0;
        double end = 10;
        if (double.IsNegativeInfinity(Range[0]))
        {
            if (Limits.Count > at)
            {
                start = Limits[at][0] + 10;
            }
        }
        else
        {
            start = Range[1] - (System.Math.Abs(Range[1] - Range[0]) * .1);
        }

        if (double.IsPositiveInfinity(Range[1]))
        {
            if (Limits.Count > at)
            {
                start = Limits[at][1] + 10;
            }
        }
        else
        {
            end = Range[1];
        }

        Table.Insert(at, (DefaultColor, start, end));
        IReadOnlyList<DesignEditorCommit> commits = Apply(Table.Select(static r => r.Color).ToList(), Table.Select(static r => new[] { r.Start, r.End }).ToList());
        rowToSelect = Table.Count - 1;
        return commits;
    }

    /// <summary>A row deleted (<c>deleteRow</c>).</summary>
    public IReadOnlyList<DesignEditorCommit> DeleteRow(int row, out int rowToSelect)
    {
        Table.RemoveAt(row);
        IReadOnlyList<DesignEditorCommit> commits = Apply(Table.Select(static r => r.Color).ToList(), Table.Select(static r => new[] { r.Start, r.End }).ToList());
        rowToSelect = Table.Count == 0 ? -1 : row > 0 ? row - 1 : 0;
        return commits;
    }

    private List<(double[] Color, double Start, double End)> Generate()
    {
        var table = new List<(double[] Color, double Start, double End)>();
        int rows = System.Math.Min(Limits.Count, Colors.Count);
        for (int i = 0; i < rows; i++)
        {
            table.Add((Colors[i], Limits[i][0], Limits[i][1]));
        }

        return table;
    }

    private List<DesignEditorCommit> Apply(List<double[]> colors, List<double[]> limits)
    {
        var commits = new List<DesignEditorCommit>();
        if (!Same(colors, Colors))
        {
            commits.Add(new DesignEditorCommit(_colorsName, Colors, colors));
        }

        if (!Same(limits, Limits))
        {
            commits.Add(new DesignEditorCommit(_limitsName, Limits, limits));
        }

        Colors = colors;
        Limits = limits;
        Table = Generate();
        return commits;
    }

    private static bool Same(List<double[]> a, List<double[]> b) =>
        a.Count == b.Count && a.Zip(b).All(static p => p.First.SequenceEqual(p.Second));

    // d(o): parseFloat takes it, it is finite, and it has no letter but an e.
    private static bool IsNumber(string text) =>
        !Letters.IsMatch(text) && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v);

    private static double Parse(string text) => double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>The modes of View Format's editor (<c>DisplayFormatMode.MODES</c>).</summary>
public enum DesignFormatMode
{
    /// <summary>Default: <c>%11.4g</c>.</summary>
    Default,

    /// <summary>Integer: <c>%.0f</c>.</summary>
    Integer,

    /// <summary>A number of decimals: <c>%.Nf</c>.</summary>
    Decimal,

    /// <summary>Scientific: <c>%e</c>.</summary>
    Scientific,

    /// <summary>Whatever else is typed.</summary>
    Custom,
}

/// <summary>View Format (<c>DisplayFormatEditor</c>, <c>DisplayFormatMode</c>, the <c>DisplayFormat</c> datatype).</summary>
public static class DesignDisplayFormat
{
    /// <summary>What Decimal's field says when it is not a whole number (<c>viewFormatErrorText</c>).</summary>
    public const string NotWhole = "Only positive whole numbers are allowed.";

    private static readonly Regex DecimalForm = new("%.[0-9]+f", RegexOptions.CultureInvariant);
    private static readonly Regex Valid = new(
        @"%(\d+\$)?[-+\s0#]?(\d+|\*)?(\.\d+)?[bt]?[diuoxfegcs]|%\*?\d*l?([diuoxfegcs]|\[\S*\])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The mode a format is shown in, and what its field says (<c>refresh</c>).</summary>
    public static (DesignFormatMode Mode, string? Value) Read(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format == "%11.4g")
        {
            return (DesignFormatMode.Default, null);
        }

        if (format == "%.0f")
        {
            return (DesignFormatMode.Integer, null);
        }

        if (DecimalForm.IsMatch(format))
        {
            int dot = format.IndexOf('.', StringComparison.Ordinal);
            int f = format.IndexOf('f', StringComparison.Ordinal);
            // substring takes the two ends in order, and an absent one as 0.
            int from = dot + 1;
            int to = System.Math.Max(f, 0);
            return (DesignFormatMode.Decimal, from <= to ? format[from..to] : format[to..from]);
        }

        return format == "%e" ? (DesignFormatMode.Scientific, null) : (DesignFormatMode.Custom, format);
    }

    /// <summary>The format a mode and its field come to (<c>getFormatString</c>); null when Decimal's field is not a whole number.</summary>
    public static string? Write(DesignFormatMode mode, string? value)
    {
        switch (mode)
        {
            case DesignFormatMode.Default:
                return "%11.4g";
            case DesignFormatMode.Integer:
                return "%.0f";
            case DesignFormatMode.Scientific:
                return "%e";
            case DesignFormatMode.Decimal:
                return IsWhole(value ?? string.Empty) ? "%." + value + "f" : null;
            default:
                return value;
        }
    }

    /// <summary>Whether R2025b's datatype takes a format (<c>DisplayFormat</c>): text with a conversion in it, or <c>1</c>.</summary>
    public static bool IsValid(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Valid.IsMatch(format) || format == "1";
    }

    // Number(i)%1===0 && i!=="" && Number(i)>-1
    private static bool IsWhole(string text) =>
        text.Length > 0 && (string.IsNullOrWhiteSpace(text) ? 0d : double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : double.NaN) is var v && v % 1 == 0 && v > -1;
}

/// <summary>The table behind a table's columns (<c>UITableColumnEditor</c>, its model and table model): Name, Width(px), Auto Width, Editable and Sortable.</summary>
public sealed class DesignColumnsModel
{
    private const string Auto = "auto";

    /// <summary>A table over a component's columns. A width is text or a number, and an editable or sortable a bool or text.</summary>
    public DesignColumnsModel(IReadOnlyList<string> names, IReadOnlyList<object> widths, IReadOnlyList<bool> editable, IReadOnlyList<bool>? sortable)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(widths);
        ArgumentNullException.ThrowIfNull(editable);
        int count = names.Count;
        Names = [.. names];
        Widths = [.. widths];
        while (Widths.Count < count)
        {
            Widths.Add(Auto);
        }

        Editable = Fill(editable, count);
        Sortable = sortable is null ? null : Fill(sortable, count);
    }

    /// <summary>Each column's name.</summary>
    public List<string> Names { get; private set; }

    /// <summary>Each column's width: <c>auto</c> or another word, or a number.</summary>
    public List<object> Widths { get; private set; }

    /// <summary>Whether each is editable.</summary>
    public List<bool> Editable { get; private set; }

    /// <summary>Whether each is sortable; null where the component has no such property.</summary>
    public List<bool>? Sortable { get; private set; }

    /// <summary>What the Width(px) cell shows for a column (<c>_generateColumnWidthForTable</c>).</summary>
    public string WidthCell(int row) => Widths[row] is string s ? (s == Auto ? string.Empty : s) : DesignDocument.NumberText((double)Widths[row]);

    /// <summary>Whether its Auto Width is ticked.</summary>
    public bool IsAuto(int row) => Widths[row] is Auto;

    /// <summary>A column added after <paramref name="selectedRow"/>, or last (<c>_plusButtonClickHandler</c>, <c>addRow</c>).</summary>
    public IReadOnlyList<DesignEditorCommit> AddRow(int selectedRow, out int rowToSelect)
    {
        int end = Names.Count - 1;
        int at = (selectedRow >= 0 && selectedRow < end ? selectedRow : end) + 1;
        string Named(string n) => "Column " + n;
        string name = Named((at + 1).ToString(CultureInfo.InvariantCulture));
        int g = 1;
        while (Names.Contains(name))
        {
            name = Named((at + 1).ToString(CultureInfo.InvariantCulture) + "_" + g.ToString(CultureInfo.InvariantCulture));
            g++;
        }

        var names = new List<string>(Names);
        var widths = new List<object>(Widths);
        var editable = new List<bool>(Editable);
        List<bool>? sortable = Sortable is null ? null : new List<bool>(Sortable);
        names.Insert(at, name);
        widths.Insert(at, Auto);
        editable.Insert(at, false);
        sortable?.Insert(at, false);
        rowToSelect = at;
        return Apply(names, widths, editable, sortable);
    }

    /// <summary>A column deleted (<c>_minusButtonClickHandler</c>, <c>deleteRow</c>): the row before it selected, if any.</summary>
    public IReadOnlyList<DesignEditorCommit> DeleteRow(int row, out int rowToSelect)
    {
        var names = new List<string>(Names);
        var widths = new List<object>(Widths);
        var editable = new List<bool>(Editable);
        List<bool>? sortable = Sortable is null ? null : new List<bool>(Sortable);
        names.RemoveAt(row);
        widths.RemoveAt(row);
        editable.RemoveAt(row);
        sortable?.RemoveAt(row);
        rowToSelect = row - 1;
        return Apply(names, widths, editable, sortable);
    }

    /// <summary>A name typed in.</summary>
    public IReadOnlyList<DesignEditorCommit> SetName(int row, string name)
    {
        if (Names[row] == name)
        {
            return [];
        }

        var names = new List<string>(Names)
        {
            [row] = name,
        };
        return Apply(names, Widths, Editable, Sortable);
    }

    /// <summary>A width typed in; as it is, and made a number by what is sent (<c>_generateColumnWidthFromTable</c>, <c>_convertToValidWidth</c>).</summary>
    public IReadOnlyList<DesignEditorCommit> SetWidth(int row, string width)
    {
        if ((Widths[row] is string held ? (held == Auto ? string.Empty : held) : null) == width)
        {
            return [];
        }

        var widths = new List<object>(Widths)
        {
            [row] = width,
        };
        return Apply(Names, widths, Editable, Sortable);
    }

    /// <summary>Auto Width ticked or not: unticked, 75 pixels.</summary>
    public IReadOnlyList<DesignEditorCommit> SetAuto(int row, bool auto)
    {
        if (IsAuto(row) == auto)
        {
            return [];
        }

        var widths = new List<object>(Widths)
        {
            [row] = auto ? Auto : 75d,
        };
        return Apply(Names, widths, Editable, Sortable);
    }

    /// <summary>Editable ticked or not.</summary>
    public IReadOnlyList<DesignEditorCommit> SetEditable(int row, bool on)
    {
        if (Editable[row] == on)
        {
            return [];
        }

        var editable = new List<bool>(Editable)
        {
            [row] = on,
        };
        return Apply(Names, Widths, editable, Sortable);
    }

    /// <summary>Sortable ticked or not.</summary>
    public IReadOnlyList<DesignEditorCommit> SetSortable(int row, bool on)
    {
        if (Sortable![row] == on)
        {
            return [];
        }

        var sortable = new List<bool>(Sortable!)
        {
            [row] = on,
        };
        return Apply(Names, Widths, Editable, sortable);
    }

    private static List<bool> Fill(IReadOnlyList<bool> given, int count)
    {
        var filled = new List<bool>(given);
        bool one = given.Count == 1 && given[0];
        while (filled.Count < count)
        {
            filled.Add(one);
        }

        return filled;
    }

    // _handleEditorValueChange with _updateBeforeSendToServer: each that changed, as it is sent.
    private List<DesignEditorCommit> Apply(List<string> names, List<object> widths, List<bool> editable, List<bool>? sortable)
    {
        var commits = new List<DesignEditorCommit>();
        List<string> sentNames = names.Select(static n => n.Trim()).ToList();
        if (!sentNames.SequenceEqual(Names.Select(static n => n.Trim())))
        {
            commits.Add(new DesignEditorCommit("ColumnName", Names, sentNames));
        }

        List<object> sentWidths = SendWidths(widths);
        List<object> oldWidths = SendWidths(Widths);
        if (!SameObjects(sentWidths, oldWidths))
        {
            commits.Add(new DesignEditorCommit("ColumnWidth", oldWidths, sentWidths));
        }

        List<bool> sentEditable = SendFlags(editable);
        if (!sentEditable.SequenceEqual(SendFlags(Editable)))
        {
            commits.Add(new DesignEditorCommit("ColumnEditable", SendFlags(Editable), sentEditable));
        }

        if (sortable is not null && Sortable is not null)
        {
            List<bool> sentSortable = SendFlags(sortable);
            if (!sentSortable.SequenceEqual(SendFlags(Sortable)))
            {
                commits.Add(new DesignEditorCommit("ColumnSortable", SendFlags(Sortable), sentSortable));
            }
        }

        Names = names;
        Widths = widths;
        Editable = editable;
        Sortable = sortable;
        return commits;
    }

    private static bool SameObjects(List<object> a, List<object> b) => a.Count == b.Count && a.Zip(b).All(static p => p.First.Equals(p.Second));

    // Numeric text a number; none, auto; all the same word, one.
    private static List<object> SendWidths(List<object> widths)
    {
        if (widths.Count == 0)
        {
            return [Auto];
        }

        var sent = new List<object>();
        foreach (object w in widths)
        {
            if (w is string text)
            {
                string t = text.Trim();
                if (t is not Auto and not "fit" && !t.EndsWith('x') && LeadingNumber(t) is { } n)
                {
                    sent.Add(n);
                }
                else
                {
                    sent.Add(t);
                }
            }
            else
            {
                sent.Add(w);
            }
        }

        return sent.Count > 1 && sent[0] is string && sent.All(w => w.Equals(sent[0])) ? [sent[0]] : sent;
    }

    private static List<bool> SendFlags(List<bool> flags) =>
        flags.Count > 1 && flags.All(f => f == flags[0]) ? [flags[0]] : flags;

    // parseFloat: the number the text starts with.
    private static double? LeadingNumber(string text)
    {
        Match m = Regex.Match(text, @"^[+-]?(\d+\.?\d*([eE][+-]?\d+)?|\.\d+([eE][+-]?\d+)?|Infinity)", RegexOptions.CultureInvariant);
        return !m.Success ? null
            : m.Value.EndsWith("Infinity", StringComparison.Ordinal) ? (m.Value[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity)
            : double.Parse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

/// <summary>The table behind a date picker's DisabledDates (<c>DateTableEditor</c>, <c>DateModel</c>).</summary>
public sealed class DesignDatesModel
{
    /// <summary>A table over the dates a component holds.</summary>
    public DesignDatesModel(IReadOnlyList<DesignDate> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        Dates = [.. dates];
    }

    /// <summary>The dates.</summary>
    public List<DesignDate> Dates { get; }

    /// <summary>A date added after <paramref name="selectedRow"/>, or last (<c>addRow</c>): today in an empty table, else the next free day after the one before it.</summary>
    public int AddRow(int selectedRow, DateTime today)
    {
        int count = Dates.Count;
        int at = selectedRow >= 0 && selectedRow < count - 1 ? selectedRow + 1 : count;
        DesignDate date;
        int row;
        if (count == 0)
        {
            date = new DesignDate(today.Year, today.Month, today.Day);
            row = 0;
        }
        else
        {
            (date, row) = NextOf(Dates[at - 1], at - 1);
            while (Dates.Contains(date))
            {
                (date, row) = NextOf(date, row);
            }
        }

        Dates.Insert(at, date);
        return row;
    }

    /// <summary>A date deleted (<c>deleteRow</c>); returns the row then selected.</summary>
    public int DeleteRow(int row)
    {
        Dates.RemoveAt(row);
        return Dates.Count == 0 ? -1 : row > 0 ? row - 1 : 0;
    }

    private static (DesignDate Date, int Row) NextOf(DesignDate date, int row)
    {
        DateTime next = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(date.Day);
        return (new DesignDate(next.Year, next.Month, next.Day), row + 1);
    }
}

/// <summary>A value edited as fields (<c>PropertyFieldsEditor</c>): a position's x, y, width and height, limits' min and max.</summary>
public static class DesignFieldsEditor
{
    /// <summary>What it says when max is not above min (<c>maxGreaterThanMin</c> of <c>inspector_client</c>'s words).</summary>
    public const string MaxNotAboveMin = "max must be greater than min";

    /// <summary>The value's numbers, field by field, a missing one 0 (<c>_getObjForEdit</c>).</summary>
    public static double[] Read(IReadOnlyList<double> value, int fields)
    {
        ArgumentNullException.ThrowIfNull(value);
        var read = new double[fields];
        for (int i = 0; i < fields && i < value.Count; i++)
        {
            read[i] = value[i];
        }

        return read;
    }

    /// <summary>A field's text as a number (<c>_getNewObjFromEvent</c>, <c>filterFloat</c>); null when it is not one.</summary>
    public static double? Number(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string t = text.Trim();
        if (t.Equals("-inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.NegativeInfinity;
        }

        if (t.Equals("inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.PositiveInfinity;
        }

        if (t.Equals("nan", StringComparison.OrdinalIgnoreCase))
        {
            return double.NaN;
        }

        return Regex.IsMatch(t, @"^(-|\+)?([0-9]+(\.[0-9]+)?|Infinity)$", RegexOptions.CultureInvariant)
            ? t.EndsWith("Infinity", StringComparison.Ordinal)
                ? (t[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity)
                : double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Whether fields fail what the editor checks (<c>_isValidationError</c>): min not below max.</summary>
    public static bool Refused(DesignEditorInfo info, double[] fields)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(fields);
        if (!info.Validation.Contains("maxGreaterThanMin"))
        {
            return false;
        }

        int min = IndexOf(info.Fields, "min");
        int max = IndexOf(info.Fields, "max");
        return min >= 0 && max >= 0 && fields[min] >= fields[max];
    }

    private static int IndexOf(IReadOnlyList<string> fields, string name)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (fields[i] == name)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>A text area's lines (<c>TextAreaEditor</c>): what it shows while edited, and the lines it commits.</summary>
public static class DesignTextArea
{
    /// <summary>The lines of what is typed, as <c>outputType: "array"</c> commits them: nothing for no text, a trailing line end dropped.</summary>
    public static string[] Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return [];
        }

        string t = text.EndsWith('\n') ? text[..^1] : text;
        return Regex.Split(t, "\r\n|\r|\n", RegexOptions.CultureInvariant);
    }

    /// <summary>A row's text while it is shown and not edited: items run together with commas (<c>String(array)</c>).</summary>
    public static string Shown(IEnumerable<string> lines) => string.Join(",", lines);

    /// <summary>Its text while edited (<c>_getMultiLineEditValue</c>): one line each.</summary>
    public static string Edited(IEnumerable<string> lines) => string.Join("\n", lines);
}

/// <summary>A colour as its text box and picker hold it (<c>ColorTextBoxEditor</c>, <c>_ColorUtils</c>, <c>MColor</c>).</summary>
public static class DesignColors
{
    /// <summary>The standard colours, by row (<c>MColor.getStandardColors</c>).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Standard { get; } =
    [
        ["#1171be", "#dd5400", "#edb120", "#8516d1", "#3baa32", "#2fbeef", "#d1048b"],
        ["#268cdd", "#f57729", "#ffe864", "#c05cfb", "#49db40", "#6cf4ff", "#f267c5"],
        ["#ffffff", "#f0f0f0", "#e6e6e6", "#cccccc", "#a6a6a6", "#808080", "#262626"],
        ["#ff0000", "#ff00ff", "#ffff00", "#00ff00", "#00ffff", "#0000ff", "#000000"],
    ];

    /// <summary>The recent colours a new picker starts with (<c>recentlyUsedColors</c>).</summary>
    public static IReadOnlyList<string> Recent { get; } = ["#ff0000", "#ff00ff", "#ffff00", "#00ff00", "#ffffff", "#00ffff", "#0000ff"];

    private static readonly Dictionary<string, double[]> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = [1, 0, 0], ["r"] = [1, 0, 0],
        ["green"] = [0, 1, 0], ["g"] = [0, 1, 0],
        ["blue"] = [0, 0, 1], ["b"] = [0, 0, 1],
        ["cyan"] = [0, 1, 1], ["c"] = [0, 1, 1],
        ["magenta"] = [1, 0, 1], ["m"] = [1, 0, 1],
        ["yellow"] = [1, 1, 0], ["y"] = [1, 1, 0],
        ["black"] = [0, 0, 0], ["k"] = [0, 0, 0],
        ["white"] = [1, 1, 1], ["w"] = [1, 1, 1],
    };

    /// <summary>A colour as the box shows it: two places, with commas (<c>_setForegroundValues</c>).</summary>
    public static string Text(IReadOnlyList<double> rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        return string.Join(",", rgb.Take(3).Select(static c => (System.Math.Round(System.Math.Round(c * 255) / 255 * 100, MidpointRounding.AwayFromZero) / 100).ToString("0.00", CultureInfo.InvariantCulture)));
    }

    /// <summary>What is typed as a colour, 0 to 1: three numbers with commas, spaces or brackets, a MATLAB colour's name or letter, or <c>#rrggbb</c>; null when it is none.</summary>
    public static double[]? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string t = text.Trim().Trim('\'', '"');
        if (Named.TryGetValue(t, out double[]? named))
        {
            return named;
        }

        if (t.Length == 7 && t[0] == '#' && int.TryParse(t.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            return [((hex >> 16) & 255) / 255d, ((hex >> 8) & 255) / 255d, (hex & 255) / 255d];
        }

        string[] parts = t.Trim('[', ']').Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return null;
        }

        var rgb = new double[3];
        for (int i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out rgb[i]) || rgb[i] < 0 || rgb[i] > 1)
            {
                return null;
            }
        }

        return rgb;
    }

    /// <summary><c>#rrggbb</c> as 0 to 1.</summary>
    public static double[] FromHex(string hex) => Parse(hex) ?? throw new ArgumentException("Not a colour.", nameof(hex));

    /// <summary>A colour as <c>#rrggbb</c>.</summary>
    public static string Hex(IReadOnlyList<double> rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        return "#" + string.Concat(rgb.Take(3).Select(static c => ((int)System.Math.Round(System.Math.Clamp(c, 0, 1) * 255)).ToString("x2", CultureInfo.InvariantCulture)));
    }
}

/// <summary>What a property's <c>...Mode</c> becomes beside it (<c>PropertyCommandFactory.executePropertyEditorCommand</c>).</summary>
public static class DesignPropertyMode
{
    /// <summary><c>auto</c> when <paramref name="value"/> says what the class's default says, numbers to 2 places; else <c>manual</c>.</summary>
    public static string For(DesignValue? defaultValue, DesignValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return defaultValue is not null && DesignValueText.Format(Round(defaultValue)) == DesignValueText.Format(Round(value)) ? "auto" : "manual";
    }

    private static DesignValue Round(DesignValue value) => value is NumericValue { IsLogical: false } n
        ? new NumericValue(n.Rows, n.Cols, n.Data.Select(static d => System.Math.Round(d, 2, MidpointRounding.AwayFromZero)).ToArray())
        : value;
}

/// <summary>How a class is sized to its text (<c>PreferredSizing</c> of each model).</summary>
/// <param name="PreserveWidth">Its width stays, as a field's does (<c>isPreserveWidth</c>).</param>
/// <param name="PreserveMinimumDefault">It is no narrower than it is made (<c>isPreserveMinimumDefaultWidth</c>).</param>
public readonly record struct DesignPreferredRule(bool PreserveWidth, bool PreserveMinimumDefault);

/// <summary>A component sized to its text after an edit (<c>AbstractDesignTimeController.setComponentToPreferredSize</c>).</summary>
public static class DesignPreferredSize
{
    private static readonly string[] Common = ["Text", "FontName", "FontSize", "FontAngle", "FontWeight"];

    private static readonly Dictionary<string, (DesignPreferredRule Rule, string[] Properties)> Rules = new(StringComparer.Ordinal)
    {
        ["matlab.ui.control.CheckBox"] = (new(false, false), Common),
        ["matlab.ui.control.DatePicker"] = (new(true, false), Common),
        ["matlab.ui.control.DropDown"] = (new(true, false), Common),
        ["matlab.ui.control.Hyperlink"] = (new(false, false), Common),
        ["matlab.ui.control.Label"] = (new(false, false), [.. Common, "Interpreter"]),
        ["matlab.ui.control.NumericEditField"] = (new(true, false), Common),
        ["matlab.ui.control.Button"] = (new(false, true), Common),
        ["matlab.ui.control.RadioButton"] = (new(false, false), Common),
        ["matlab.ui.control.Spinner"] = (new(true, false), Common),
        ["matlab.ui.control.StateButton"] = (new(false, true), Common),
        ["matlab.ui.control.EditField"] = (new(true, false), Common),
        ["matlab.ui.control.ToggleButton"] = (new(false, true), Common),
    };

    /// <summary>Whether editing <paramref name="property"/> sizes a <paramref name="type"/> to its text (<c>PreferredSizeProperties</c>).</summary>
    public static bool Applies(string type, string property) =>
        Rules.TryGetValue(type, out (DesignPreferredRule Rule, string[] Properties) r) && r.Properties.Contains(property);

    /// <summary>A class's rule; false when it has none.</summary>
    public static bool TryRule(string type, out DesignPreferredRule rule)
    {
        bool found = Rules.TryGetValue(type, out (DesignPreferredRule Rule, string[] Properties) r);
        rule = r.Rule;
        return found;
    }

    /// <summary>The least a class can be (<c>MinimumSize</c>).</summary>
    public static Size2D Minimum(string type) => type switch
    {
        "matlab.ui.control.Label" or "matlab.ui.control.Hyperlink" => new Size2D(15, 22),
        "matlab.ui.control.CheckBox" or "matlab.ui.control.RadioButton" => new Size2D(20, 22),
        _ => new Size2D(10, 22),
    };

    /// <summary>
    /// What a class's widget asks for, its <c>preferredSize</c>: its text in its font, as the shared measure has it, with its chrome
    /// (R2025b measures its widget in its browser); a field's height alone matters, its width staying.
    /// </summary>
    public static Size2D Widget(string type, IReadOnlyList<string> lines, string font, double size, bool bold, bool italic)
    {
        ArgumentNullException.ThrowIfNull(lines);
        double text = System.Math.Ceiling(UiFit.TextWidth(lines, font, size, bold, italic) - 1e-9);
        double line = System.Math.Ceiling((1.23 * size) - 1e-9);
        int count = System.Math.Max(1, lines.Count);
        return type switch
        {
            "matlab.ui.control.Label" or "matlab.ui.control.Hyperlink" => new Size2D(System.Math.Max(text, 20), (count * line) + 2),
            "matlab.ui.control.RadioButton" => new Size2D(text + 18, (count * line) + 2),
            "matlab.ui.control.CheckBox" => new Size2D(text + 21, (count * line) + 2),
            "matlab.ui.control.Button" or "matlab.ui.control.StateButton" or "matlab.ui.control.ToggleButton" => new Size2D(text + 16, (count * line) + 7),
            _ => new Size2D(text, line + 7),
        };
    }

    /// <summary>The widest margin a text is given (<c>_getWidthBuffer</c>).</summary>
    public static double Buffer(double width) => System.Math.Round(System.Math.Max(5, 0.03 * width), MidpointRounding.AwayFromZero);

    /// <summary>
    /// Where and how large a component goes (<c>setComponentToPreferredSize</c>, <c>_computeSizeToPreserve</c>,
    /// <c>_getLocationBasedOnNewSize</c>): <paramref name="preferred"/> its widget's size for what it now says, with the margin, the
    /// least it can be, and what it was made and last sized to; a size set by hand is not shrunk.
    /// </summary>
    public static (Size2D Size, Point2D Location) Fit(
        DesignPreferredRule rule,
        Size2D minimum,
        Size2D made,
        Size2D preferred,
        Size2D? previous,
        Size2D shown,
        Size2D inner,
        Point2D location,
        string alignment,
        bool wordWrap)
    {
        double width = System.Math.Max(preferred.Width + Buffer(preferred.Width), minimum.Width);
        double height = System.Math.Max(preferred.Height, minimum.Height);
        double? previousHeight = null;
        if (previous is { } was)
        {
            double wasWidth = System.Math.Max(was.Width + Buffer(was.Width), minimum.Width);
            double wasHeight = System.Math.Max(was.Height, minimum.Height);
            if (rule.PreserveMinimumDefault)
            {
                wasWidth = System.Math.Max(wasWidth, made.Width);
            }

            previousHeight = wasHeight;
            if (!rule.PreserveWidth && !wordWrap)
            {
                if (shown.Width != wasWidth || shown.Height != wasHeight)
                {
                    width = System.Math.Max(width, shown.Width);
                    height = System.Math.Max(height, shown.Height);
                }
                else if (rule.PreserveMinimumDefault)
                {
                    width = System.Math.Max(width, made.Width);
                }
            }
        }
        else if (!rule.PreserveWidth && !wordWrap && rule.PreserveMinimumDefault)
        {
            width = System.Math.Max(width, made.Width);
        }

        if (rule.PreserveWidth || wordWrap)
        {
            width = inner.Width;
            if (previousHeight is { } h && shown.Height > h)
            {
                height = System.Math.Max(shown.Height, height);
            }
        }

        double x = alignment switch
        {
            "right" => location.X - (width - inner.Width),
            "center" => location.X - (0.5 * (width - inner.Width)),
            _ => location.X,
        };
        return (new Size2D(width, height), new Point2D(x, location.Y + inner.Height - height));
    }
}

/// <summary>What the inspector's editors read from a component's values, and the values their changes come to.</summary>
public static class DesignEditorValues
{
    /// <summary>A value's lines: a cell's items, or text by its line ends.</summary>
    public static List<string> Lines(DesignValue? value) => value switch
    {
        CharValue c => [.. c.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')],
        StringValue s => [.. s.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')],
        CellValue cell => cell.Items.Select(static i => i switch
        {
            CharValue c => c.Text,
            StringValue s => s.Text,
            NumericValue { Data: [var d] } => DesignDocument.NumberText(d),
            _ => DesignValueText.Format(i),
        }).ToList(),
        null => [],
        NumericValue n => n.Data.Select(DesignDocument.NumberText).ToList(),
        _ => [DesignValueText.Format(value)],
    };

    /// <summary>Lines as a component's text holds them: one as text, several as a column (<c>getServerValue</c>).</summary>
    public static DesignValue Text(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Count switch
        {
            0 => new CharValue(string.Empty),
            1 => new CharValue(lines[0]),
            _ => CellValue.Column(lines.Select(static l => (DesignValue)new CharValue(l)).ToArray()),
        };
    }

    /// <summary>Lines as items hold them: a row, or an empty cell for none.</summary>
    public static DesignValue Items(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Count == 0 ? new CellValue(0, 0, []) : CellValue.Row(lines.Select(static l => (DesignValue)new CharValue(l)).ToArray());
    }

    /// <summary>A row typed for <c>ItemsData</c> or a value (<c>ItemsValueEditor</c>): as MATLAB writes it when bracketed, else a row of its lines.</summary>
    public static DesignValue ItemsData(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        string t = typed.Trim();
        return (t.StartsWith('{') && t.EndsWith('}')) || (t.StartsWith('[') && t.EndsWith(']'))
            ? DesignValueText.Parse(t)
            : Items(DesignTextArea.Lines(typed));
    }

    /// <summary>Which item <paramref name="value"/> picks, from 1 (<c>SelectedIndex</c>); -1 for none.</summary>
    public static int SelectedIndex(DesignValue? items, DesignValue? itemsData, DesignValue? value)
    {
        List<string> data = Lines(itemsData);
        List<string> names = data.Count > 0 ? data : Lines(items);
        List<string> picked = Lines(value);
        return picked.Count == 0 ? -1 : names.IndexOf(picked[0]) is var at && at >= 0 ? at + 1 : -1;
    }

    /// <summary>The value row <paramref name="selected"/> comes to, the data's where there is data, past its end its last (<c>convertValueFromInspector</c>).</summary>
    public static DesignValue ValueAt(int selected, IReadOnlyList<string> items, DesignValue? itemsData, bool several)
    {
        ArgumentNullException.ThrowIfNull(items);
        DesignValue one;
        if (selected < 1)
        {
            return several ? new CellValue(0, 0, []) : new CharValue(string.Empty);
        }

        if (itemsData is CellValue { Items.Count: > 0 } cell)
        {
            one = cell.Items[System.Math.Min(selected, cell.Items.Count) - 1];
        }
        else if (itemsData is NumericValue { Data.Count: > 0 } numbers)
        {
            one = NumericValue.Scalar(numbers.Data[System.Math.Min(selected, numbers.Data.Count) - 1]);
        }
        else
        {
            one = new CharValue(selected <= items.Count ? items[selected - 1] : string.Empty);
        }

        return several ? CellValue.Row(one) : one;
    }

    /// <summary>A matrix's rows; empty for what is not one. A cell of colours' names or letters by their numbers.</summary>
    public static List<double[]> Rows(DesignValue? value)
    {
        var rows = new List<double[]>();
        if (value is NumericValue { Data.Count: > 0 } n)
        {
            for (int r = 0; r < n.Rows; r++)
            {
                var row = new double[n.Cols];
                for (int c = 0; c < n.Cols; c++)
                {
                    row[c] = n.Data[r + (c * n.Rows)];
                }

                rows.Add(row);
            }
        }
        else if (value is CellValue cell)
        {
            foreach (DesignValue item in cell.Items)
            {
                rows.Add(item is NumericValue { Data.Count: 3 } one ? [.. one.Data] : DesignColors.Parse(Lines(item).FirstOrDefault() ?? string.Empty) ?? [1, 1, 1]);
            }
        }

        return rows;
    }

    /// <summary>Rows as a matrix of <paramref name="columns"/> columns.</summary>
    public static NumericValue Matrix(IReadOnlyList<double[]> rows, int columns)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return new NumericValue(0, 0, []);
        }

        var data = new double[rows.Count * columns];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                data[r + (c * rows.Count)] = rows[r][c];
            }
        }

        return new NumericValue(rows.Count, columns, data);
    }

    /// <summary>A row of numbers typed, with or without its brackets (<c>ArrayOfValuesTextBoxEditor</c>, <c>TicksEditorConverter</c>).</summary>
    public static DesignValue Numbers(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        string t = typed.Trim();
        return DesignValueText.Parse(t.StartsWith('[') || t.StartsWith('{') || t.StartsWith('\'') ? t : "[" + t.Replace(',', ' ') + "]");
    }

    /// <summary>The ticks, labels and limits a ticks row goes with (<c>richEditorDependencies</c>).</summary>
    public static (string Ticks, string Labels, string Limits) TickNames(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name is "MajorTicks" or "MajorTickLabels")
        {
            return ("MajorTicks", "MajorTickLabels", "Limits");
        }

        if (name.Length > 0 && name[0] is 'X' or 'Y' or 'Z' or 'R')
        {
            string axis = name[..1];
            return (axis + "Tick", axis + "TickLabel", axis + "Lim");
        }

        return ("Ticks", "TickLabels", "Limits");
    }

    /// <summary>A table's column names (<c>_getUItableColumnInfo</c>).</summary>
    public static List<string> ColumnNames(DesignValue? value) => value switch
    {
        null => [],
        NumericValue { IsEmpty: true } => [],
        _ => Lines(value),
    };

    /// <summary>A table's column widths: words and numbers.</summary>
    public static List<object> ColumnWidths(DesignValue? value) => value switch
    {
        CellValue cell => cell.Items.Select(static i => i is NumericValue { Data: [var d] } ? (object)d : Lines(i).FirstOrDefault() ?? "auto").ToList(),
        NumericValue n => n.Data.Select(static d => (object)d).ToList(),
        CharValue c => [c.Text],
        StringValue s => [s.Text],
        _ => [],
    };

    /// <summary>A table's Editable or Sortable: a logical row, a scalar standing for all.</summary>
    public static List<bool> Flags(DesignValue? value) => value switch
    {
        NumericValue n => n.Data.Select(static d => d != 0).ToList(),
        CellValue cell => cell.Items.Select(static i => i is NumericValue { Data: [not 0] }).ToList(),
        _ => [],
    };

    /// <summary>The value a commit of a table's columns comes to, as it goes to the component.</summary>
    public static DesignValue? ColumnValue(string property, object? after) => (property, after) switch
    {
        ("ColumnName", List<string> names) => names.Count == 0 ? new CellValue(0, 0, []) : CellValue.Column(names.Select(static n => (DesignValue)new CharValue(n)).ToArray()),
        ("ColumnWidth", List<object> widths) => CellValue.Row(widths.Select(static w => w is double d ? NumericValue.Scalar(d) : (DesignValue)new CharValue((string)w)).ToArray()),
        (_, List<bool> flags) => new NumericValue(flags.Count == 0 ? 0 : 1, flags.Count, flags.Select(static f => f ? 1d : 0d).ToArray(), isLogical: true),
        _ => null,
    };

    /// <summary>A value's dates (a <c>DatetimeValue</c>'s), <c>NaT</c> left out.</summary>
    public static List<DesignDate> Dates(DesignValue? value) =>
        value is DatetimeValue d ? d.Items.Where(static i => i is not null).Select(static i => i!.Value).ToList() : [];

    /// <summary>Dates as a row.</summary>
    public static DatetimeValue DatesValue(IReadOnlyList<DesignDate?> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        return new DatetimeValue(dates.Count == 0 ? 0 : 1, dates.Count, dates);
    }

    /// <summary>A date as a row shows it.</summary>
    public static string DateText(DesignDate? date) =>
        date is { } d ? new DateTime(d.Year == 0 ? 1 : d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Unspecified).ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : "NaT";
}
