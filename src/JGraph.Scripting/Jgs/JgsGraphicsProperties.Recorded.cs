using System.Runtime.CompilerServices;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The names R2025b's <c>get(h)</c> lists that this build's model lacked (open item 88): in the MATLAB
/// dialect each answers R2025b's own value until a script writes one, and then the value written; a
/// name with a <c>…Mode</c> partner turns it <c>'manual'</c>, as R2025b's do. Most change nothing that
/// is drawn, a recorded divergence; a rectangle's <c>Position</c> and <c>Curvature</c> reshape it and an
/// animated line's <c>MaximumNumPoints</c> trims it.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>What a script wrote to a recorded name, per object; the object's lifetime is the table's.</summary>
    private static readonly ConditionalWeakTable<GraphObject, Dictionary<string, JgsValue>> RecordedWrites = new();

    /// <summary>The recorded name and its default, for this object, or false when none of its classes records it.</summary>
    private static bool TryRecorded(GraphObject target, string name, out GraphicsProperty property)
    {
        property = null!;
        if (!JgsRunningDialect.ThreadIsMatlab || R2025bClassesOf(target) is not { } classes)
        {
            return false;
        }

        foreach (string cls in classes)
        {
            if (R2025bDefaults.ByClass.TryGetValue(cls, out Dictionary<string, (string Name, Func<JgsValue> Value)>? names)
                && names.TryGetValue(name, out (string Name, Func<JgsValue> Value) found))
            {
                property = RecordedProperty(cls, found.Name, found.Value);
                return true;
            }
        }

        return false;
    }

    /// <summary>Every recorded name the object's classes give it, for <c>get(h)</c>'s list.</summary>
    private static IEnumerable<string> RecordedNames(GraphObject target) =>
        JgsRunningDialect.ThreadIsMatlab && R2025bClassesOf(target) is { } classes
            ? classes.SelectMany(static cls => R2025bDefaults.ByClass.TryGetValue(cls, out var names)
                    ? names.Values.Select(static n => n.Name)
                    : []).Distinct(StringComparer.OrdinalIgnoreCase)
            : [];

    private static GraphicsProperty RecordedProperty(string cls, string name, Func<JgsValue> fallback) => new(
        name,
        entry => ShapeRead(entry.Target, name)
            ?? (RecordedWrites.TryGetValue(entry.Target, out Dictionary<string, JgsValue>? held) && held.TryGetValue(name, out JgsValue? value)
                ? value
                : fallback()),
        (entry, value, line, col) =>
        {
            if (ShapeWrite(entry.Target, name, value, line, col))
            {
                return;
            }

            Dictionary<string, JgsValue> held = RecordedWrites.GetOrCreateValue(entry.Target);
            JgsLifetime.Pin(value);
            held[name] = JgsValue.Share(value);
            if (R2025bDefaults.ByClass[cls].TryGetValue(name + "Mode", out (string Name, Func<JgsValue> Value) mode))
            {
                held[mode.Name] = JgsValue.Str("manual");
            }
        });

    /// <summary>A recorded name the model does keep: a rectangle's outline, an animated line's cap.</summary>
    private static JgsValue? ShapeRead(GraphObject target, string name) => (target, name) switch
    {
        (PatchPlot patch, "Position") when JgsBuiltins.RectangleShapeOf(patch) is { } shape => Row(shape.Box),
        (PatchPlot patch, "Curvature") when JgsBuiltins.RectangleShapeOf(patch) is { } shape => Row(shape.Curvature),
        (PlotObject line, "MaximumNumPoints") when JgsBuiltins.AnimatedCapOf(line) is { } cap => JgsValue.Number(cap),
        _ => null,
    };

    private static bool ShapeWrite(GraphObject target, string name, JgsValue value, int line, int col)
    {
        switch (target, name)
        {
            case (PatchPlot patch, "Position" or "Curvature") when JgsBuiltins.RectangleShapeOf(patch) is { } shape:
                double[] given = JgsBuiltins.ToDoubles("rectangle: " + name, value, line, col);
                JgsBuiltins.ReshapeRectangle(
                    patch, name == "Position" ? given : shape.Box, name == "Curvature" ? given : shape.Curvature, line, col);
                return true;
            case (PlotObject animated, "MaximumNumPoints") when JgsBuiltins.AnimatedCapOf(animated) is not null:
                JgsBuiltins.SetAnimatedCap(animated, value, line, col);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A 1-by-0 string array: a bar's <c>Labels</c> before any are given.</summary>
    private static JgsValue EmptyStringRow() => JgsValue.StringArray([], 1, 0);

    /// <summary>The recorded rows as name maps per class, built on first use (see <see cref="R2025bNames"/>).</summary>
    private static class R2025bDefaults
    {
        public static readonly Dictionary<string, Dictionary<string, (string Name, Func<JgsValue> Value)>> ByClass = R2025bDefaultRows
            .GroupBy(static row => row.Class)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToDictionary(
                    static row => row.Name,
                    static row => (row.Name, row.Value),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);
    }
}
