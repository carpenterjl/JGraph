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
/// <c>[Browsable(false)]</c>, and either of a type one of the editors supports or of a composite
/// type listed in <see cref="Composites"/>; everything else (collections, computed state) is simply
/// not shown. <c>[Category]</c> and <c>[DisplayName]</c> attributes drive grouping and captions;
/// names without a <c>[DisplayName]</c> are humanized ("LineWidth" → "Line width").
/// <para>
/// A composite property expands into a <see cref="PropertyEditorKind.Header"/> row followed by one
/// row per member, all carrying the root property's category and name so they sort together and undo
/// as one struct.
/// </para>
/// </summary>
public static class EditablePropertyFactory
{
    private static readonly string[] CategoryOrder = { "General", "Appearance", "Ticks", "Behavior" };

    /// <summary>
    /// One member of a composite (struct-valued) property: how to show it, how to read it out of the
    /// struct, and how to rebuild the struct with a new value for it. The structs are immutable with
    /// positional constructors, so a rebuilder is one expression — and listing members explicitly
    /// means an omitted member is visibly omitted rather than silently mis-bound.
    /// </summary>
    private sealed record CompositeMember(
        string DisplayName,
        PropertyEditorKind Editor,
        Type ValueType,
        Func<object, object?> Read,
        Func<object, object?, object> Rebuild);

    private static readonly Dictionary<Type, CompositeMember[]> Composites = new()
    {
        [typeof(TextStyle)] = new[]
        {
            new CompositeMember("Font", PropertyEditorKind.FontFamily, typeof(string),
                s => ((TextStyle)s).FontFamily,
                (s, v) =>
                {
                    TextStyle t = (TextStyle)s;
                    return new TextStyle(t.Color, t.FontSize, AsString(v), t.Bold, t.Italic);
                }),
            new CompositeMember("Font size", PropertyEditorKind.Number, typeof(double),
                s => ((TextStyle)s).FontSize,
                (s, v) =>
                {
                    TextStyle t = (TextStyle)s;
                    return new TextStyle(t.Color, AsDouble(v, t.FontSize), t.FontFamily, t.Bold, t.Italic);
                }),
            new CompositeMember("Bold", PropertyEditorKind.Toggle, typeof(bool),
                s => ((TextStyle)s).Bold,
                (s, v) =>
                {
                    TextStyle t = (TextStyle)s;
                    return new TextStyle(t.Color, t.FontSize, t.FontFamily, AsBool(v, t.Bold), t.Italic);
                }),
            new CompositeMember("Italic", PropertyEditorKind.Toggle, typeof(bool),
                s => ((TextStyle)s).Italic,
                (s, v) =>
                {
                    TextStyle t = (TextStyle)s;
                    return new TextStyle(t.Color, t.FontSize, t.FontFamily, t.Bold, AsBool(v, t.Italic));
                }),
            new CompositeMember("Color", PropertyEditorKind.Color, typeof(Color),
                s => ((TextStyle)s).Color,
                (s, v) =>
                {
                    TextStyle t = (TextStyle)s;
                    return new TextStyle(AsColor(v, t.Color), t.FontSize, t.FontFamily, t.Bold, t.Italic);
                }),
        },
        [typeof(LineStyle)] = new[]
        {
            new CompositeMember("Color", PropertyEditorKind.Color, typeof(Color),
                s => ((LineStyle)s).Color,
                (s, v) =>
                {
                    LineStyle l = (LineStyle)s;
                    return new LineStyle(AsColor(v, l.Color), l.Width, l.Dash, l.Cap, l.Join);
                }),
            new CompositeMember("Width", PropertyEditorKind.Number, typeof(double),
                s => ((LineStyle)s).Width,
                (s, v) =>
                {
                    LineStyle l = (LineStyle)s;
                    return new LineStyle(l.Color, AsDouble(v, l.Width), l.Dash, l.Cap, l.Join);
                }),
            new CompositeMember("Dash", PropertyEditorKind.Enum, typeof(DashStyle),
                s => ((LineStyle)s).Dash,
                (s, v) =>
                {
                    LineStyle l = (LineStyle)s;
                    return new LineStyle(l.Color, l.Width, AsEnum(v, l.Dash), l.Cap, l.Join);
                }),
            new CompositeMember("Cap", PropertyEditorKind.Enum, typeof(LineCap),
                s => ((LineStyle)s).Cap,
                (s, v) =>
                {
                    LineStyle l = (LineStyle)s;
                    return new LineStyle(l.Color, l.Width, l.Dash, AsEnum(v, l.Cap), l.Join);
                }),
            new CompositeMember("Join", PropertyEditorKind.Enum, typeof(LineJoin),
                s => ((LineStyle)s).Join,
                (s, v) =>
                {
                    LineStyle l = (LineStyle)s;
                    return new LineStyle(l.Color, l.Width, l.Dash, l.Cap, AsEnum(v, l.Join));
                }),
        },
        [typeof(Rect2D)] = new[]
        {
            new CompositeMember("X", PropertyEditorKind.Number, typeof(double),
                s => ((Rect2D)s).X,
                (s, v) =>
                {
                    Rect2D r = (Rect2D)s;
                    return new Rect2D(AsDouble(v, r.X), r.Y, r.Width, r.Height);
                }),
            new CompositeMember("Y", PropertyEditorKind.Number, typeof(double),
                s => ((Rect2D)s).Y,
                (s, v) =>
                {
                    Rect2D r = (Rect2D)s;
                    return new Rect2D(r.X, AsDouble(v, r.Y), r.Width, r.Height);
                }),
            new CompositeMember("Width", PropertyEditorKind.Number, typeof(double),
                s => ((Rect2D)s).Width,
                (s, v) =>
                {
                    Rect2D r = (Rect2D)s;
                    return new Rect2D(r.X, r.Y, AsDouble(v, r.Width), r.Height);
                }),
            new CompositeMember("Height", PropertyEditorKind.Number, typeof(double),
                s => ((Rect2D)s).Height,
                (s, v) =>
                {
                    Rect2D r = (Rect2D)s;
                    return new Rect2D(r.X, r.Y, r.Width, AsDouble(v, r.Height));
                }),
        },
        [typeof(Size2D)] = new[]
        {
            new CompositeMember("Width", PropertyEditorKind.Number, typeof(double),
                s => ((Size2D)s).Width,
                (s, v) => new Size2D((double)(v ?? 0d), ((Size2D)s).Height)),
            new CompositeMember("Height", PropertyEditorKind.Number, typeof(double),
                s => ((Size2D)s).Height,
                (s, v) => new Size2D(((Size2D)s).Width, (double)(v ?? 0d))),
        },
        [typeof(Point2D)] = new[]
        {
            new CompositeMember("X", PropertyEditorKind.Number, typeof(double),
                s => ((Point2D)s).X,
                (s, v) => new Point2D((double)(v ?? 0d), ((Point2D)s).Y)),
            new CompositeMember("Y", PropertyEditorKind.Number, typeof(double),
                s => ((Point2D)s).Y,
                (s, v) => new Point2D(((Point2D)s).X, (double)(v ?? 0d))),
        },
    };

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

        // Each entry is one property's descriptors: a single row, or a header plus its children. They
        // sort as a unit so a composite's children stay adjacent to their header.
        var result = new List<(EditableProperty[] Descriptors, string Category, int Depth, int Token)>();
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

            string displayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? Humanize(property.Name);
            string category = property.GetCustomAttribute<CategoryAttribute>()?.Category ?? "Other";

            EditableProperty[]? descriptors = Expand(property, displayName, category);
            if (descriptors is null)
            {
                continue;
            }

            result.Add((descriptors, category, InheritanceDepth(property.DeclaringType), property.MetadataToken));
        }

        return result
            .OrderBy(p => CategoryRank(p.Category))
            .ThenBy(p => p.Category, StringComparer.Ordinal)
            .ThenBy(p => p.Depth)
            .ThenBy(p => p.Token)
            .SelectMany(p => p.Descriptors)
            .ToArray();
    }

    /// <summary>
    /// Returns the descriptors for one property: one row for a directly editable type, a header plus
    /// a child row per member for a composite, or null when neither applies (the property is hidden).
    /// </summary>
    private static EditableProperty[]? Expand(PropertyInfo property, string displayName, string category)
    {
        if (EditorFor(property.PropertyType) is { } editor)
        {
            return new[]
            {
                new EditableProperty(
                    property,
                    displayName,
                    category,
                    editor,
                    property.PropertyType,
                    group: null,
                    getter: property.GetValue,
                    setter: property.SetValue),
            };
        }

        if (!Composites.TryGetValue(property.PropertyType, out CompositeMember[]? members))
        {
            return null;
        }

        var descriptors = new EditableProperty[members.Length + 1];
        descriptors[0] = new EditableProperty(
            property,
            displayName,
            category,
            PropertyEditorKind.Header,
            property.PropertyType,
            group: null,
            getter: property.GetValue,
            setter: null);

        for (int i = 0; i < members.Length; i++)
        {
            CompositeMember member = members[i];
            descriptors[i + 1] = new EditableProperty(
                property,
                member.DisplayName,
                category,
                member.Editor,
                member.ValueType,
                group: displayName,
                getter: target => member.Read(property.GetValue(target)!),
                setter: (target, value) => property.SetValue(target, member.Rebuild(property.GetValue(target)!, value)));
        }

        return descriptors;
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

            case PropertyEditorKind.FontFamily:
                // Any non-empty name is accepted: a family that is not installed here falls back at
                // render time, which is what a figure authored for another machine needs.
                if (text.Length == 0)
                {
                    return false;
                }

                value = text;
                return true;

            case PropertyEditorKind.Number:
                if (!TryParseDouble(text, out double number))
                {
                    return false;
                }

                Type numeric = Nullable.GetUnderlyingType(property.ValueType) ?? property.ValueType;
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
                Type enumType = Nullable.GetUnderlyingType(property.ValueType) ?? property.ValueType;
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

    // Coercions used by the composite rebuilders. A rebuilder is handed whatever the editor produced;
    // anything unexpected leaves that member at its current value rather than throwing mid-edit.
    private static string AsString(object? value) => value as string ?? string.Empty;

    private static double AsDouble(object? value, double fallback) =>
        value switch { double d => d, int i => i, _ => fallback };

    private static bool AsBool(object? value, bool fallback) => value is bool b ? b : fallback;

    private static Color AsColor(object? value, Color fallback) => value is Color c ? c : fallback;

    private static T AsEnum<T>(object? value, T fallback)
        where T : struct, Enum => value is T t ? t : fallback;

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
