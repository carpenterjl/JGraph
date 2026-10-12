namespace JGraph.Objects;

/// <summary>
/// A marker chart's colour data as a script wrote it — the numbers to the last bit and the shape
/// they came in, a row or a column, one colour or one per point — which the drawn channels cannot
/// say back (a colour is kept in eight bits a channel, and a value per point as a list). MATLAB's
/// <c>CData</c> reads it back while the chart is still drawn from it (open item 107).
/// </summary>
/// <param name="Values">The numbers, column by column.</param>
/// <param name="Rows">How many rows they came in.</param>
/// <param name="Columns">How many columns they came in.</param>
public sealed record WrittenColorData(double[] Values, int Rows, int Columns);
