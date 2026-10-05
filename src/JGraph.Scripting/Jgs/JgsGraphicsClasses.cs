using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What class a graphics handle is (U7 of the app-building plan, ADR 0204; decision Q2). A handle
/// is a number here (ADR 0051), so <c>class(h)</c> goes on answering <c>'double'</c>; what a number
/// that names a live object can also answer is R2025b's class of that object and each class it
/// derives from. <c>isa</c> asks, and so does a property typed with a graphics class, which is how
/// App Designer declares every component it creates (<c>UIFigure matlab.ui.Figure</c>).
/// </summary>
/// <remarks>
/// The table is generated from a recording of R2025b (<c>u7_classes</c>): <c>superclasses</c> of one
/// object of each kind, and <c>isa</c> against the bases MATLAB builds in and
/// <c>superclasses</c> does not list (<c>matlab.graphics.axis.AbstractAxes</c> is one). Names in an
/// <c>.internal.</c> package are left out.
/// </remarks>
internal static partial class JgsGraphicsClasses
{
    private static readonly Dictionary<string, string[]> Chains;
    private static readonly HashSet<string> Known;

    /// <summary>What an object the table has no row for still is: everything a placeholder is, but a placeholder.</summary>
    private static readonly string[] Common;

    // In a constructor, not in initialisers: the table is in the other half of this class, and
    // initialisers in two files run in no promised order.
    static JgsGraphicsClasses()
    {
        Chains = BuildChains();
        Known = [.. Chains.Values.SelectMany(static chain => chain)];
        Common = [.. Chains["gobjects"].Skip(1)];
    }

    private static Dictionary<string, string[]> BuildChains()
    {
        var chains = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach ((string key, int[] chain) in Rows)
        {
            chains[key] = [.. chain.Select(static index => Names[index])];
        }

        return chains;
    }

    /// <summary>Whether a name is a class some graphics object is: the question that makes <c>isa</c> look at a number as a handle.</summary>
    public static bool IsKnown(string name) => Known.Contains(name);

    /// <summary>Whether a property typed with this class holds graphics handles.</summary>
    public static bool IsGraphicsType(string className) =>
        className.StartsWith("matlab.ui.", StringComparison.Ordinal)
        || className.StartsWith("matlab.graphics.", StringComparison.Ordinal);

    /// <summary>R2025b's class of an object, then each class it derives from.</summary>
    public static IReadOnlyList<string> ChainOf(GraphObject target)
    {
        if (Chains.TryGetValue(KeyOf(target), out string[]? chain))
        {
            return chain;
        }

        return target is UiOverlayModel
            ? ["matlab.ui.dialog.ProgressDialog", "handle"]
            : [JgsGraphicsProperties.FullClassOf(target), .. Common];
    }

    /// <summary>R2025b's class of an object, in full.</summary>
    public static string ClassOf(GraphObject target) => ChainOf(target)[0];

    /// <summary>Whether an object is of a class, or of one derived from it.</summary>
    public static bool IsA(GraphObject target, string className)
    {
        IReadOnlyList<string> chain = ChainOf(target);
        for (int i = 0; i < chain.Count; i++)
        {
            if (string.Equals(chain[i], className, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The table's row for an object: its type name, except where one type name covers two of R2025b's classes.</summary>
    private static string KeyOf(GraphObject target) => target switch
    {
        AxesModel { IsUiAxes: true, IsPolar: false } => "uiaxes",
        ArrowAnnotation { Text.Length: 0, ShowTailHead: false, ShowHead: false } => "annotationline",
        PiePlot => "piechart",
        _ => JgsGraphicsProperties.TypeNameOf(target),
    };

    /// <summary>
    /// <c>isa(value, className)</c> where the value is one live handle or an array of them and the
    /// class is one a graphics object can be. False when the question is not this one, and the
    /// ordinary answer stands: a number that names nothing is only a number.
    /// </summary>
    public static bool TryIsA(JgsValue value, string className, out bool answer)
    {
        answer = false;
        if (!IsKnown(className) || !TryTargets(value, out List<GraphObject>? targets, out _))
        {
            return false;
        }

        answer = targets.TrueForAll(target => IsA(target, className));
        return true;
    }

    /// <summary>
    /// The objects a value names, when it is a handle or a non-empty array of nothing but handles.
    /// <paramref name="stray"/> is the first number that names nothing.
    /// </summary>
    public static bool TryTargets(JgsValue value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<GraphObject>? targets, out double stray)
    {
        targets = null;
        stray = double.NaN;
        if (value.Type == JgsType.Number)
        {
            if (!JgsHandleRegistry.TryGet(value, out JgsHandleEntry? entry))
            {
                stray = value.AsNumber;
                return false;
            }

            targets = [entry.Target];
            return true;
        }

        if (value.Type != JgsType.Array || value.IsStringArray || value.IsCharMatrix || value.ArrayLength == 0
            || value.NumericClass != JgsNumericClass.Double)
        {
            return false;
        }

        var found = new List<GraphObject>(value.ArrayLength);
        for (int i = 0; i < value.ArrayLength; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type != JgsType.Number || !JgsHandleRegistry.TryGet(element, out JgsHandleEntry? entry))
            {
                stray = element.Type == JgsType.Number ? element.AsNumber : double.NaN;
                return false;
            }

            found.Add(entry.Target);
        }

        targets = found;
        return true;
    }

    /// <summary>What a value is to a property typed with a graphics class.</summary>
    public enum Fit
    {
        /// <summary>Handles of the class, or nothing at all.</summary>
        Fits,

        /// <summary>A number that names no object.</summary>
        NotAHandle,

        /// <summary>Something else: another class of object, or not a handle in kind.</summary>
        WrongClass,
    }

    /// <summary>
    /// Whether a value may be held by a property typed <paramref name="className"/>: handles of
    /// that class or of one derived from it, or an empty, which is what such a property starts
    /// as. A graphics class this build makes no object of takes any handle.
    /// </summary>
    public static Fit Fitting(JgsValue value, string className, out double stray)
    {
        stray = double.NaN;
        if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray && !value.IsCharMatrix)
        {
            return Fit.Fits;
        }

        if (!TryTargets(value, out List<GraphObject>? targets, out stray))
        {
            bool number = value.Type == JgsType.Number
                || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix
                    && value.NumericClass == JgsNumericClass.Double && !double.IsNaN(stray));
            return number ? Fit.NotAHandle : Fit.WrongClass;
        }

        return !IsKnown(className) || targets.TrueForAll(target => IsA(target, className)) ? Fit.Fits : Fit.WrongClass;
    }
}
