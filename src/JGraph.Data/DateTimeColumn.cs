using System.Globalization;
using JGraph.Core.Model;

namespace JGraph.Data;

/// <summary>
/// A <see cref="TableColumn"/> of dates/times stored as OLE automation dates (the
/// <see cref="DateTimeAxis"/> convention), so a date column plots directly onto a date/time axis with
/// no conversion. NaN marks a missing value. The backing array is used directly (not copied).
/// </summary>
public sealed class DateTimeColumn : TableColumn
{
    private const string DisplayFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly double[] _oaDates;

    /// <summary>Creates a date column from OLE automation dates (used directly, not copied).</summary>
    public DateTimeColumn(string name, double[] oaDates)
        : base(name, (oaDates ?? throw new ArgumentNullException(nameof(oaDates))).Length)
    {
        _oaDates = oaDates;
    }

    /// <summary>Creates a date column from <see cref="DateTime"/> values.</summary>
    public DateTimeColumn(string name, DateTime[] values)
        : base(name, (values ?? throw new ArgumentNullException(nameof(values))).Length)
    {
        _oaDates = DateTimeAxis.ToValues(values);
    }

    public override ColumnType Type => ColumnType.DateTime;

    /// <summary>The OLE automation date values, including NaN for missing entries.</summary>
    public ReadOnlySpan<double> Values => _oaDates;

    internal double[] Storage => _oaDates;

    public override bool IsMissing(int row) => double.IsNaN(_oaDates[row]);

    public override double GetNumber(int row) => _oaDates[row];

    /// <summary>The <see cref="DateTime"/> at <paramref name="row"/> (undefined for a missing value).</summary>
    public DateTime GetDateTime(int row) => DateTimeAxis.FromValue(_oaDates[row]);

    public override string GetText(int row)
    {
        double value = _oaDates[row];
        return double.IsNaN(value)
            ? string.Empty
            : DateTimeAxis.FromValue(value).ToString(DisplayFormat, CultureInfo.InvariantCulture);
    }
}
