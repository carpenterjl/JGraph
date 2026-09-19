using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A table column of times that stays what it was (V6, ADR 0167): a <c>duration</c> variable, and a
/// timetable's row times whether <c>duration</c> or <c>datetime</c>. The count of milliseconds and the
/// tag are kept as the script value had them, so <c>TT.Time</c> and <c>TT.Properties.RowTimes</c> read
/// back the class, the format and the exact values that went in, through any number of rebuilds.
/// </summary>
/// <remarks>
/// The drawing side still sees numbers: a duration counts seconds and a datetime counts days, which
/// are the readings a row-times column had before it kept its type.
/// </remarks>
internal sealed class JgsTimeColumn : TableColumn
{
    private readonly double[] _ms;

    public JgsTimeColumn(string name, double[] milliseconds, JgsTimeTag tag)
        : base(name, (milliseconds ?? throw new ArgumentNullException(nameof(milliseconds))).Length)
    {
        _ms = milliseconds;
        Tag = tag ?? throw new ArgumentNullException(nameof(tag));
    }

    /// <summary>What the times are, and how they display.</summary>
    public JgsTimeTag Tag { get; }

    /// <summary>The times in milliseconds, NaN for a missing one.</summary>
    public ReadOnlySpan<double> Milliseconds => _ms;

    public override ColumnType Type => Tag.Kind == JgsTimeKind.Datetime ? ColumnType.DateTime : ColumnType.Number;

    public override bool IsMissing(int row) => double.IsNaN(_ms[row]);

    public override double GetNumber(int row) =>
        _ms[row] / (Tag.Kind == JgsTimeKind.Datetime ? JgsTime.MsPerDay : JgsTime.MsPerSecond);

    public override string GetText(int row) =>
        double.IsNaN(_ms[row]) ? string.Empty : GetNumber(row).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public override TableColumn TakeRows(IReadOnlyList<int> rows)
    {
        var taken = new double[rows.Count];
        for (int r = 0; r < taken.Length; r++)
        {
            taken[r] = _ms[rows[r]];
        }

        return new JgsTimeColumn(Name, taken, Tag);
    }

    /// <summary>The column as the script value it holds: an n-by-1 array of its kind.</summary>
    public JgsValue ToValue() =>
        JgsMatrix.FromColumnMajorDims((double[])_ms.Clone(), [_ms.Length, 1]).MarkTime(Tag);

    /// <summary>This column under another name.</summary>
    public JgsTimeColumn Renamed(string name) => new(name, _ms, Tag);

    /// <summary>This column grown to <paramref name="rows"/> rows, the new ones missing (measured: NaN).</summary>
    public JgsTimeColumn Grown(int rows)
    {
        var grown = new double[rows];
        Array.Fill(grown, double.NaN);
        _ms.CopyTo(grown, 0);
        return new JgsTimeColumn(Name, grown, Tag);
    }
}
