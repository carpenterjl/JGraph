namespace JGraph.Data;

/// <summary>
/// A <see cref="TableColumn"/> of free text backed by a <see cref="string"/> array (null marks a
/// missing value). The distinct values, in first-appearance order, form the <see cref="Categories"/>
/// set; <see cref="GetNumber"/> returns a row's index into that set so a text column can drive a
/// category axis (positions 0, 1, 2, …). The backing array is used directly (not copied).
/// </summary>
public sealed class TextColumn : TableColumn
{
    private readonly string?[] _values;
    private readonly int[] _categoryIndices;
    private readonly List<string> _categories;

    /// <summary>Creates a text column over <paramref name="values"/> (used directly, not copied).</summary>
    public TextColumn(string name, string?[] values)
        : base(name, (values ?? throw new ArgumentNullException(nameof(values))).Length)
    {
        _values = values;
        _categories = new List<string>();
        _categoryIndices = new int[values.Length];

        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < values.Length; i++)
        {
            string? value = values[i];
            if (value is null)
            {
                _categoryIndices[i] = -1;
                continue;
            }

            if (!lookup.TryGetValue(value, out int index))
            {
                index = _categories.Count;
                _categories.Add(value);
                lookup[value] = index;
            }

            _categoryIndices[i] = index;
        }
    }

    public override ColumnType Type => ColumnType.Text;

    /// <summary>The distinct values, in first-appearance order — the category labels for an axis.</summary>
    public IReadOnlyList<string> Categories => _categories;

    /// <summary>The string at <paramref name="row"/>, or null when missing.</summary>
    public string? GetString(int row) => _values[row];

    public override bool IsMissing(int row) => _values[row] is null;

    public override double GetNumber(int row)
    {
        int index = _categoryIndices[row];
        return index < 0 ? double.NaN : index;
    }

    public override string GetText(int row) => _values[row] ?? string.Empty;
}
