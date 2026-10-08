namespace JGraph.Scripting.Jgs;

/// <summary>
/// What <c>methods</c> prints for a user class, and answers with <c>-full</c> (open item 14), measured
/// in R2025b under <c>-batch</c> (probe_b6d, probe_b6e in the open-items scratch).
/// </summary>
/// <remarks>
/// <para>
/// <b>The names.</b> The instance methods, the constructor among them and those inherited from a
/// superclass other than <c>handle</c> (<c>copy</c> of <c>matlab.mixin.Copyable</c> is one), by
/// character code; then <c>Static methods:</c>; then, for a handle class, the line naming
/// <c>handle</c>'s, which R2025b writes with two hyperlinks and JGraph as their text, as the .NET
/// listing does (ADR 0175). The columns are the .NET listing's.
/// </para>
/// <para>
/// <b>The signatures.</b> The outputs, one bare or several in brackets, then the name, then the inputs
/// in parentheses when there are any: <c>obj PB6Shape(r)</c>, <c>obj PB6Plain</c>, <c>[a, b]
/// two_out(obj, x, varargin)</c>, <c>skip_arg(~, y)</c>, <c>Static c unit</c>. An inherited one ends
/// with <c>  % Inherited from</c> its class. <c>handle</c>'s lines are untyped here, where a .NET
/// type's are typed. Every line is ordered by its method's name.
/// </para>
/// </remarks>
internal static class ClassMethodsListing
{
    /// <summary><c>handle</c>'s lines under <c>-full</c> for a user class, in R2025b's order.</summary>
    private static readonly (string Name, string Text)[] HandleLines =
    [
        ("addlistener", "L addlistener(sources, eventname, callback)"),
        ("addlistener", "L addlistener(sources, properties, eventname, callback)"),
        ("addlistener", "L addlistener(sources, propertyname, eventname, callback)"),
        ("addlistener", "L addlistener(sources, propertyname, eventname, callback)"),
        ("addlistener", "L addlistener(sources, propertynames, eventname, callback)"),
        ("delete", "delete(obj)"),
        ("eq", "TF eq(A, B)"),
        ("findobj", "HM findobj(H, varargin)"),
        ("findprop", "prop findprop(object, propname)"),
        ("ge", "TF ge(A, B)"),
        ("gt", "TF gt(A, B)"),
        ("isvalid", "validity isvalid(obj)"),
        ("le", "TF le(A, B)"),
        ("listener", "L listener(sources, eventname, callback)"),
        ("listener", "L listener(sources, properties, eventname, callback)"),
        ("listener", "L listener(sources, propertyname, eventname, callback)"),
        ("listener", "L listener(sources, propertyname, eventname, callback)"),
        ("listener", "L listener(sources, propertynames, eventname, callback)"),
        ("lt", "TF lt(A, B)"),
        ("ne", "TF ne(A, B)"),
        ("notify", "notify(sources, eventname)"),
        ("notify", "notify(sources, eventname, eventdata)"),
    ];

    /// <summary>The listing <c>methods(x)</c> prints for <paramref name="cls"/>.</summary>
    public static string Names(JgsClass cls)
    {
        var instance = new SortedSet<string>(StringComparer.Ordinal);
        var statics = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ClassMethod method in cls.ListedMethods)
        {
            (method.Static ? statics : instance).Add(method.Function.Name);
        }

        if (cls.HasImplicitConstructor)
        {
            instance.Add(cls.ConstructorName);
        }

        var lines = new List<string> { "", $"Methods for class {cls.Name}:", "" };
        if (instance.Count + statics.Count > 0)
        {
            int width = instance.Concat(statics).Max(static n => n.Length) + 2;
            lines.AddRange(Net.NetMethodsListing.Columns([.. instance], width));
            if (statics.Count > 0)
            {
                lines.AddRange(["", "Static methods:", ""]);
                lines.AddRange(Net.NetMethodsListing.Columns([.. statics], width));
            }
        }

        if (cls.IsHandle)
        {
            lines.AddRange(["", $"Methods of {cls.Name} inherited from handle."]);
        }

        lines.Add("");
        lines.Add("");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The methods R2025b lists for a struct and a function handle (measured: 11 and 7). Other
    /// built-in classes list hundreds, toolbox overloads among them, which JGraph does not imitate.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> BuiltinClassMethods = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["struct"] =
        [
            "ctranspose", "display", "iscolumn", "ismatrix", "isrow", "isscalar", "isvector", "permute", "reshape",
            "struct2cell", "transpose",
        ],
        ["function_handle"] = ["display", "edit", "func2str", "functions", "nargin", "nargout", "open"],
    };

    /// <summary>The listing <c>methods</c> prints for a built-in class from its names.</summary>
    public static string BuiltinNames(string className, string[] names)
    {
        var lines = new List<string> { "", $"Methods for class {className}:", "" };
        lines.AddRange(Net.NetMethodsListing.Columns(names, names.Max(static n => n.Length) + 2));
        lines.Add("");
        lines.Add("");
        return string.Join("\n", lines);
    }

    /// <summary>What <c>methods</c> prints for a name that is no class (measured).</summary>
    public static string NoClass(string name) => $"\nNo class '{name}'.\n\n";

    /// <summary>The listing <c>methods(x, '-full')</c> prints for <paramref name="cls"/>.</summary>
    public static string Full(JgsClass cls) =>
        string.Join("\n", new[] { "", $"Methods for class {cls.Name}:", "" }.Concat(FullLines(cls, notes: true)).Append("").Append(""));

    /// <summary>
    /// The signature lines of <c>methods(x, '-full')</c>. Answered as a cell they carry no
    /// <c>% Inherited from</c> notes (measured, as a library's do not, ADR 0183); printed, they do.
    /// </summary>
    public static IEnumerable<string> FullLines(JgsClass cls, bool notes)
    {
        var lines = new List<(string Name, string Text)>();
        foreach (ClassMethod method in cls.ListedMethods)
        {
            string text = method.Native is not null
                ? $"lhs1 {method.Function.Name}(rhs1)" // measured on matlab.mixin.Copyable's copy
                : Signature(method.Function);
            if (method.Static)
            {
                text = "Static " + text;
            }

            if (notes && method.Owner is { } owner && !ReferenceEquals(owner, cls))
            {
                text += $"  % Inherited from {owner.Name}";
            }

            lines.Add((method.Function.Name, text));
        }

        if (cls.HasImplicitConstructor)
        {
            lines.Add((cls.ConstructorName, $"obj {cls.ConstructorName}"));
        }

        if (cls.IsHandle)
        {
            var own = lines.Select(static l => l.Name).ToHashSet(StringComparer.Ordinal);
            lines.AddRange(HandleLines.Where(h => !own.Contains(h.Name))
                .Select(h => (h.Name, notes ? h.Text + "  % Inherited from handle" : h.Text)));
        }

        // A stable sort, so a name's several lines keep the order they were added in.
        return lines.OrderBy(static l => l.Name, StringComparer.Ordinal).Select(static l => l.Text);
    }

    private static string Signature(FnStmt function)
    {
        string outputs = function.Outputs.Count switch
        {
            0 => "",
            1 => function.Outputs[0] + " ",
            _ => $"[{string.Join(", ", function.Outputs)}] ",
        };
        string inputs = function.Parameters.Count == 0 ? "" : $"({string.Join(", ", function.Parameters)})";
        return outputs + function.Name + inputs;
    }
}
