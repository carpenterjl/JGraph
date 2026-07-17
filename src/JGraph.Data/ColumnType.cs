namespace JGraph.Data;

/// <summary>
/// The inferred kind of a <see cref="TableColumn"/>. This drives how a column's values are stored,
/// how they are exposed numerically for plotting, and how an axis is configured when the column is
/// used for the X coordinate (date/time or category scale).
/// </summary>
public enum ColumnType
{
    /// <summary>Real numbers, stored as <see cref="double"/>; missing values are NaN.</summary>
    Number,

    /// <summary>Dates/times, stored as OLE automation dates (see <c>DateTimeAxis</c>); missing values are NaN.</summary>
    DateTime,

    /// <summary>Free text, stored as strings; the distinct values form an ordered category set.</summary>
    Text,
}
