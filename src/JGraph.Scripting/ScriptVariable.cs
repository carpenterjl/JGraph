namespace JGraph.Scripting;

/// <summary>
/// A UI-friendly projection of one variable a script defined: its name, a short type name, a display
/// string (truncated for huge values), and — when the value is data a viewer can open — the raw value
/// (<c>double[]</c> for numeric arrays, a <see cref="JGraph.Data.Table"/> for tables, boxed scalars for
/// numbers/booleans/strings; null for functions and other opaque values).
/// </summary>
/// <param name="Name">The variable's name as the script declared it.</param>
/// <param name="Type">A short user-facing type name (e.g. "number", "array", "table").</param>
/// <param name="DisplayValue">The value formatted for display, truncated to a sane length.</param>
/// <param name="RawValue">The underlying value when it is inspectable data, otherwise null.</param>
public sealed record ScriptVariable(string Name, string Type, string DisplayValue, object? RawValue)
{
    /// <summary>The maximum length of <see cref="DisplayValue"/>; longer values end with an ellipsis.</summary>
    public const int MaxDisplayLength = 256;

    /// <summary>Truncates <paramref name="text"/> to <see cref="MaxDisplayLength"/> characters.</summary>
    public static string Truncate(string text) =>
        text.Length <= MaxDisplayLength ? text : text[..(MaxDisplayLength - 1)] + "…";
}
