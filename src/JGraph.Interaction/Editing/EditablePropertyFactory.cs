using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Editing;

/// <summary>
/// Builds the list of <see cref="EditableProperty"/> descriptors for an object via reflection.
/// A property is included when it is public, readable and writable, not an indexer, not marked
/// <c>[Browsable(false)]</c>, and of a type one of the editors supports; everything else (complex
/// styles, collections, computed state) is simply not shown. <c>[Category]</c> and
/// <c>[DisplayName]</c> attributes drive grouping and captions; names without a
/// <c>[DisplayName]</c> are humanized ("LineWidth" → "Line width").
/// </summary>
public static class EditablePropertyFactory
{
    private static readonly string[] CategoryOrder = { "General", "Appearance", "Ticks", "Behavior" };

    /// <summary>Describes the editable properties of <paramref name="target"/>, grouped and ordered for display.</summary>
    public static IReadOnlyList<EditableProperty> Describe(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Describe(target.GetType());
    }

    /// <summary>Describes the editable properties of a type, grouped and ordered for display.</summary>
    public static IReadOnlyList<EditableProperty> Describe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var result = new List<(EditableProperty Descriptor, int Depth, int Token)>();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.GetCustomAttribute<BrowsableAttribute>() is { Browsable: false })
            {
                continue;
            }

            PropertyEditorKind? editor = EditorFor(property.PropertyType);
            if (editor is null)
            {
                continue;
            }

            string displayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? Humanize(property.Name);
            string category = property.GetCustomAttribute<CategoryAttribute>()?.Category ?? "Other";

            result.Add((
                new EditableProperty(property, displayName, category, editor.Value),
                InheritanceDepth(property.DeclaringType),
                property.MetadataToken));
        }

        return result
            .OrderBy(p => CategoryRank(p.Descriptor.Category))
            .ThenBy(p => p.Descriptor.Category, StringComparer.Ordinal)
            .ThenBy(p => p.Depth)
            .ThenBy(p => p.Token)
            .Select(p => p.Descriptor)
            .ToArray();
    }

    /// <summary>
    /// Parses editor text into a value assignable to the property, honoring the current culture with
    /// an invariant-culture fallback. Returns false when the text is not valid for the editor.
    /// </summary>
    public static bool TryParse(EditableProperty property, string? text, out object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        value = null;
        text = text?.Trim() ?? string.Empty;

        switch (property.Editor)
        {
            case PropertyEditorKind.Text:
                value = text;
                return true;

            case PropertyEditorKind.Number:
                if (!TryParseDouble(text, out double number))
                {
                    return false;
                }

                Type numeric = Nullable.GetUnderlyingType(property.Property.PropertyType) ?? property.Property.PropertyType;
                if (numeric == typeof(int))
                {
                    if (!double.IsInteger(number) || number is < int.MinValue or > int.MaxValue)
                    {
                        return false;
                    }

                    value = (int)number;
                }
                else
                {
                    value = number;
                }

                return true;

            case PropertyEditorKind.Toggle:
                if (!bool.TryParse(text, out bool flag))
                {
                    return false;
                }

                value = flag;
                return true;

            case PropertyEditorKind.Enum:
                Type enumType = Nullable.GetUnderlyingType(property.Property.PropertyType) ?? property.Property.PropertyType;
                if (!Enum.TryParse(enumType, text, ignoreCase: true, out object? parsed)
                    || !Enum.IsDefined(enumType, parsed!))
                {
                    return false;
                }

                value = parsed;
                return true;

            case PropertyEditorKind.Color:
                if (!Color.TryParse(text, out Color color))
                {
                    return false;
                }

                value = color;
                return true;

            case PropertyEditorKind.OptionalColor:
                if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                    return true;
                }

                if (!Color.TryParse(text, out Color optional))
                {
                    return false;
                }

                value = (Color?)optional;
                return true;

            case PropertyEditorKind.Range:
                string[] parts = text.Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2
                    || !TryParseDouble(parts[0], out double min)
                    || !TryParseDouble(parts[1], out double max))
                {
                    return false;
                }

                value = new DataRange(min, max);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Formats a property value the way the matching editor expects to display and re-parse it.</summary>
    public static string Format(EditableProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        return value switch
        {
            null => string.Empty,
            double d => d.ToString("G6", CultureInfo.CurrentCulture),
            Color c => FormatColor(c),
            DataRange r =>
                $"{r.Min.ToString("G6", CultureInfo.CurrentCulture)}, {r.Max.ToString("G6", CultureInfo.CurrentCulture)}",
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
        };
    }

    /// <summary>Formats a color as "#RRGGBB" (or "#AARRGGBB" when not fully opaque).</summary>
    public static string FormatColor(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static PropertyEditorKind? EditorFor(Type type)
    {
        if (type == typeof(string))
        {
            return PropertyEditorKind.Text;
        }

        if (type == typeof(double) || type == typeof(int))
        {
            return PropertyEditorKind.Number;
        }

        if (type == typeof(bool))
        {
            return PropertyEditorKind.Toggle;
        }

        if (type.IsEnum)
        {
            return PropertyEditorKind.Enum;
        }

        if (type == typeof(Color))
        {
            return PropertyEditorKind.Color;
        }

        if (Nullable.GetUnderlyingType(type) == typeof(Color))
        {
            return PropertyEditorKind.OptionalColor;
        }

        if (type == typeof(DataRange))
        {
            return PropertyEditorKind.Range;
        }

        return null;
    }

    private static int CategoryRank(string category)
    {
        int index = Array.IndexOf(CategoryOrder, category);
        return index >= 0 ? index : CategoryOrder.Length;
    }

    private static int InheritanceDepth(Type? type)
    {
        int depth = 0;
        while (type?.BaseType is not null)
        {
            depth++;
            type = type.BaseType;
        }

        return depth;
    }

    /// <summary>"LineWidth" → "Line width"; runs of capitals ("XY") stay together.</summary>
    private static string Humanize(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 4);
        sb.Append(name[0]);
        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
