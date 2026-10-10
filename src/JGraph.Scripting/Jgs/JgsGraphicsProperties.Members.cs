using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a graphics handle answers to besides its properties (open item 68): <c>methods(h)</c>,
/// <c>properties(h)</c> and the listing <c>methods(h)</c> prints, all R2025b's as
/// <c>graphics_class_members.m</c> recorded them, and the methods a dot reaches (<c>t.expand()</c>,
/// <c>h.sendEventToHTMLSource(...)</c>), which call the builtin of the same name with the handle first.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>One class's recorded members.</summary>
    internal sealed record MatlabMembers(
        string ClassName, string[] Methods, string[] Properties, string[] Listed, string[] Static,
        string[] Events, HashSet<string> Listens, HashSet<string> Observable);

    /// <summary>
    /// The R2025b class an object is, in full: a component's own name, or the first of the classes its
    /// names were recorded under; null for an object neither covers.
    /// </summary>
    internal static string? MatlabClassOf(GraphObject target) =>
        R2025bClassesOf(target) is [var first, ..] ? first.Replace(InPolarAxes, "", StringComparison.Ordinal)
        : SpeaksAsComponent(target) || target is FigureModel ? FullClassOf(target)
        : null;

    /// <summary>Whether a property, or one a script added, answers to <paramref name="name"/> on this object.</summary>
    internal static bool Answers(JgsHandleEntry entry, string name) =>
        TryFind(entry.Target, name, out _) || (entry.AddedProperties?.ContainsKey(name) ?? false);

    /// <summary>The recorded members of the object's class, or null when R2025b's were not recorded.</summary>
    internal static MatlabMembers? MembersOf(GraphObject target) =>
        MatlabClassOf(target) is { } name && R2025bMembers.ByClass.TryGetValue(name, out MatlabMembers? members) ? members : null;

    /// <summary>
    /// Whether <paramref name="name"/> is one of the object's methods, a property not being one (the
    /// caller asks the properties first). Method names are case-sensitive, as R2025b's are.
    /// </summary>
    internal static bool IsMethodOf(GraphObject target, string name) =>
        MembersOf(target) is { } members && Array.IndexOf(members.Methods, name) >= 0;

    /// <summary>The listing <c>methods(h)</c> prints: R2025b's instance names, its static ones, and handle's line.</summary>
    internal static string MethodsListing(MatlabMembers members)
    {
        string[] instance = [.. members.Listed.Order(StringComparer.Ordinal)];
        string[] statics = [.. members.Static.Order(StringComparer.Ordinal)];
        var lines = new List<string> { "", $"Methods for class {members.ClassName}:", "" };
        int width = instance.Concat(statics).Max(static n => n.Length) + 2;
        lines.AddRange(Net.NetMethodsListing.Columns(instance, width));
        if (statics.Length > 0)
        {
            lines.AddRange(["", "Static methods:", ""]);
            lines.AddRange(Net.NetMethodsListing.Columns(statics, width));
        }

        lines.AddRange(["", $"Methods of {members.ClassName} inherited from handle.", "", ""]);
        return string.Join("\n", lines);
    }

    /// <summary>The listing <c>properties(h)</c> prints (measured): a heading and each name indented four.</summary>
    internal static string PropertiesListing(MatlabMembers members) =>
        $"\nProperties for class {members.ClassName}:\n\n{string.Concat(members.Properties.Select(static n => $"    {n}\n"))}\n";

    /// <summary>The recorded rows by class, built on first use (see <see cref="R2025bNames"/>).</summary>
    private static class R2025bMembers
    {
        public static readonly Dictionary<string, MatlabMembers> ByClass = R2025bMemberRows.ToDictionary(
            static row => row.Class,
            static row => new MatlabMembers(
                row.Class, Words(row.Methods), Words(row.Properties), Words(row.Listed), Words(row.Static), Words(row.Events),
                new HashSet<string>(Words(row.Listens), StringComparer.Ordinal),
                new HashSet<string>(Words(row.Observable), StringComparer.OrdinalIgnoreCase)),
            StringComparer.Ordinal);

        private static string[] Words(string text) => text.Length == 0 ? [] : text.Split(' ');
    }
}
