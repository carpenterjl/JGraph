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
    /// The recorded members of the class a deleted object's handle still names (open item 91): R2025b's
    /// <c>methods(h)</c>, <c>properties(h)</c> and <c>events(h)</c> answer them after <c>delete(h)</c>.
    /// </summary>
    internal static MatlabMembers? RetiredMembers(JgsValue value) =>
        JgsHandleRegistry.RetiredClassOf(value) is { } name && R2025bMembers.ByClass.TryGetValue(name, out MatlabMembers? members) ? members : null;

    /// <summary>
    /// A dot on a deleted object's handle, in the MATLAB dialect (probe <c>probe_91</c>): a name the
    /// class has is the dead handle's refusal, any other the dot's own, naming the class. Null for a
    /// value that is no deleted handle.
    /// </summary>
    internal static JgsRuntimeException? DotOnRetired(JgsValue value, string name, bool reading, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || RetiredMembers(value) is not { } members)
        {
            return null;
        }

        bool known = members.Properties.Contains(name, StringComparer.OrdinalIgnoreCase) || Array.IndexOf(members.Methods, name) >= 0;
        return known
            ? new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.")
            : reading
                ? new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
                    $"Unrecognized method, property, or field '{name}' for class '{members.ClassName}'.")
                : new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
                    $"Unrecognized property '{name}' for class '{members.ClassName}'.");
    }

    /// <summary>
    /// Whether <paramref name="name"/> is one of the object's methods, a property not being one (the
    /// caller asks the properties first). Method names are case-sensitive, as R2025b's are.
    /// </summary>
    internal static bool IsMethodOf(GraphObject target, string name) =>
        MembersOf(target) is { } members && Array.IndexOf(members.Methods, name) >= 0;

    /// <summary>The class a graphics object's metaproperty wears, and its metaclass's (probe <c>probe_91b</c>).</summary>
    internal const string GraphicsMetaPropertyClass = "matlab.graphics.internal.GraphicsMetaProperty";

    private const string GraphicsMetaClassClass = "matlab.graphics.internal.GraphicsMetaClass";

    /// <summary>
    /// <c>findprop(h, name)</c> on a graphics object (open item 91): the property of exactly that name
    /// as R2025b's metaproperty, its fields in R2025b's order, or null when the object has none — the
    /// name is matched with its case, as R2025b's is. A component's is a plain
    /// <c>matlab.metadata.Property</c>, without the four graphics fields. The name, the access, whether
    /// it is hidden and whether it is observable are R2025b's; the other flags vary by property in
    /// R2025b and are this build's reading (a recorded divergence).
    /// </summary>
    internal static JgsValue? MetaPropertyOf(JgsHandleEntry entry, string name)
    {
        GraphObject target = entry.Target;
        if (!TryFind(target, name, out GraphicsProperty property) || !property.Name.Equals(name, StringComparison.Ordinal))
        {
            return null;
        }

        MatlabMembers? members = MembersOf(target);
        bool component = SpeaksAsComponent(target);
        bool writable = property.Write is not null;
        bool observable = members?.Observable.Contains(name) ?? false;
        string owner = MatlabClassOf(target) ?? FullClassOf(target);
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (!component)
        {
            fields["AffectsObject"] = JgsValue.Bool(false);
            fields["AffectsDataLimits"] = JgsValue.Bool(false);
            fields["AffectsLegend"] = JgsValue.Bool(false);
            fields["InternalComponent"] = JgsValue.Bool(false);
        }

        JgsValue definer = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Name"] = JgsValue.Str(owner) });
        definer.SetClassName(component ? "matlab.metadata.Class" : GraphicsMetaClassClass);
        fields["Name"] = JgsValue.Str(property.Name);
        fields["Description"] = JgsValue.Str(component ? "" : property.Name + " PropInfo");
        fields["DetailedDescription"] = JgsValue.Str("");
        fields["GetAccess"] = JgsValue.Str("public");
        fields["SetAccess"] = JgsValue.Str(writable ? "public" : target is JgsGraphicsRoot ? "private" : "protected");
        fields["Dependent"] = JgsValue.Bool(writable);
        fields["Constant"] = JgsValue.Bool(false);
        fields["Abstract"] = JgsValue.Bool(false);
        fields["Transient"] = JgsValue.Bool(false);
        fields["Hidden"] = JgsValue.Bool(members is not null && Array.IndexOf(members.Properties, property.Name) < 0);
        fields["GetObservable"] = JgsValue.Bool(observable);
        fields["SetObservable"] = JgsValue.Bool(observable);
        fields["AbortSet"] = JgsValue.Bool(true);
        fields["NonCopyable"] = JgsValue.Bool(false);
        fields["WeakHandle"] = JgsValue.Bool(false);
        fields["PartialMatchPriority"] = JgsValue.Number(1);
        fields["HasDefault"] = JgsValue.Bool(false);
        fields["DefaultValue"] = JgsEmpty.Zero();
        fields["GetMethod"] = JgsEmpty.Zero();
        fields["SetMethod"] = JgsEmpty.Zero();
        fields["Validation"] = JgsEmpty.Zero();
        fields["DefiningClass"] = definer;
        JgsValue meta = JgsValue.Struct(fields);
        meta.SetClassName(component ? JgsBuiltins.MetaPropertyClassName : GraphicsMetaPropertyClass);
        return meta;
    }

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
