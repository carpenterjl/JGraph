namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// What <c>methods</c> prints for a .NET type when its answer is not asked for (interop plan, stage 2,
/// ADR 0175): the names in columns, or with <c>-full</c> one signature per line. Measured in R2025b
/// under <c>-batch</c> (net_display, probe_methods).
/// </summary>
/// <remarks>
/// <para>
/// <b>The columns.</b> Every name is padded to the longest name listed — the instance names and the
/// static ones together — plus two, and as many columns as fit a 180-character line are filled top
/// to bottom. 180 is the command window width R2025b reports under <c>-batch</c>; JGraph has no
/// window width of its own. Instance names come first (the type's methods, its indexer, its
/// constructor, the MATLAB names of its operators, and <c>matlab.mixin.Scalar</c>'s), then
/// <c>Static methods:</c> with the static names no instance method shares, then, for a reference
/// type, the line naming <c>handle</c>'s. A static class lists no instance methods of its own.
/// </para>
/// <para>
/// <b>The order of <c>-full</c>.</b> R2025b lists overloads of one name in an order it does not keep
/// from run to run (stage 1 finding 2). JGraph lists the type's own lines by name, then the lines
/// <c>handle</c> and <c>matlab.mixin.Scalar</c> contribute, in R2025b's order.
/// </para>
/// <para>
/// One R2025b listing does not follow the rule: <c>methods('System.Math')</c> puts its seven
/// instance names in two rows where the width allows one. No other type probed does so; it is
/// recorded in ADR 0175 and not imitated.
/// </para>
/// </remarks>
internal static class NetMethodsListing
{
    private const int WindowWidth = 180;

    /// <summary>The methods <c>matlab.mixin.Scalar</c> gives every .NET object.</summary>
    public static readonly string[] ScalarMethods = ["end", "isempty", "isscalar", "length", "ndims", "numel", "size"];

    /// <summary>The methods <c>handle</c> gives a .NET object of a reference type.</summary>
    public static readonly string[] HandleMethods =
        ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify"];

    /// <summary>The operators <c>methods</c> lists under a MATLAB name as well (measured on JGTest.Vector2).</summary>
    private static readonly (string Net, string Matlab)[] Operators =
    [
        ("op_Addition", "plus"), ("op_Subtraction", "minus"), ("op_Multiply", "mtimes"),
        ("op_Equality", "eq"), ("op_Inequality", "ne"),
    ];

    /// <summary>The inherited lines of <c>-full</c>, in R2025b's order, each with whether <c>handle</c> gives it.</summary>
    private static readonly (string Text, bool Handle)[] Inherited =
    [
        ("event.proplistener L addlistener(handle sources, cell propertynames, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.listener L addlistener(handle sources, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L addlistener(handle sources, matlab.metadata.Property properties, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L addlistener(handle sources, asciiString propertyname, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L addlistener(handle sources, string propertyname, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("delete(handle obj)  % Inherited from handle", true),
        ("ind end(~, ~, ~)  % Inherited from matlab.mixin.Scalar", false),
        ("logical TF eq(A, B)  % Inherited from handle", true),
        ("handle HM findobj(handle H, varargin)  % Inherited from handle", true),
        ("matlab.metadata.Property prop findprop(handle scalar object, asciiString propname)  % Inherited from handle", true),
        ("logical TF ge(A, B)  % Inherited from handle", true),
        ("logical TF gt(A, B)  % Inherited from handle", true),
        ("TF isempty(~)  % Inherited from matlab.mixin.Scalar", false),
        ("TF isscalar(~)  % Inherited from matlab.mixin.Scalar", false),
        ("logical validity isvalid(handle obj)  % Inherited from handle", true),
        ("logical TF le(A, B)  % Inherited from handle", true),
        ("L length(~)  % Inherited from matlab.mixin.Scalar", false),
        ("event.listener L listener(handle sources, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L listener(handle sources, matlab.metadata.Property properties, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L listener(handle sources, asciiString propertyname, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L listener(handle sources, cell propertynames, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("event.proplistener L listener(handle sources, string propertyname, char vector eventname, function_handle scalar callback)  % Inherited from handle", true),
        ("logical TF lt(A, B)  % Inherited from handle", true),
        ("N ndims(~)  % Inherited from matlab.mixin.Scalar", false),
        ("logical TF ne(A, B)  % Inherited from handle", true),
        ("notify(handle sources, asciiString eventname, event.EventData scalar eventdata)  % Inherited from handle", true),
        ("notify(handle sources, asciiString eventname)  % Inherited from handle", true),
        ("n numel(~)  % Inherited from matlab.mixin.Scalar", false),
        ("varargout size(~, varargin)  % Inherited from matlab.mixin.Scalar", false),
    ];

    /// <summary>The MATLAB names of the operators <paramref name="type"/> declares.</summary>
    public static IEnumerable<string> OperatorNames(Type type) =>
        Operators.Where(o => NetSignature.Group(type, o.Net, instance: false).Length > 0).Select(static o => o.Matlab);

    private static bool IsStaticClass(Type type) => type.IsAbstract && type.IsSealed;

    /// <summary>
    /// The listing <c>methods(x)</c> prints, headed with <paramref name="className"/> — the type's name,
    /// or an interface view's <c>NET.view.</c> name (probe2).
    /// </summary>
    public static string Names(Type type, string className)
    {
        var instance = new SortedSet<string>(StringComparer.Ordinal);
        var statics = new SortedSet<string>(StringComparer.Ordinal);
        foreach (NetSignature signature in NetSignature.All(type, instance: !IsStaticClass(type)))
        {
            (signature.IsStatic ? statics : instance).Add(signature.Name);
        }

        if (NetInvoke.HasConstructor(type))
        {
            instance.Add(NetNames.ShortName(type));
        }

        instance.UnionWith(OperatorNames(type));
        instance.UnionWith(ScalarMethods);
        if (type.IsInterface)
        {
            instance.Add("eq"); // an interface view lists eq among its own (probe2)
        }

        statics.ExceptWith(instance);

        int width = instance.Concat(statics).Max(static n => n.Length) + 2;
        var lines = new List<string> { "", $"Methods for class {className}:", "" };
        lines.AddRange(Columns([.. instance], width));
        if (statics.Count > 0)
        {
            lines.AddRange(["", "Static methods:", ""]);
            lines.AddRange(Columns([.. statics], width));
        }

        if (!type.IsValueType)
        {
            lines.AddRange(["", $"Methods of {className} inherited from handle."]);
        }

        lines.Add("");
        return string.Join("\n", lines);
    }

    /// <summary>The listing <c>methods(x, '-full')</c> prints.</summary>
    public static string Full(Type type, string className) =>
        string.Join("\n", new[] { "", $"Methods for class {className}:", "" }.Concat(FullLines(type)).Append(""));

    /// <summary>The signature lines of <c>methods(x, '-full')</c>, which it answers as a cell when asked.</summary>
    public static IEnumerable<string> FullLines(Type type)
    {
        var own = new List<(string Name, string Text)>();
        foreach (NetSignature signature in NetSignature.All(type, instance: !IsStaticClass(type)))
        {
            own.Add((signature.Name, signature.FullText(type)));
        }

        if (!type.IsAbstract)
        {
            string shortName = NetNames.ShortName(type);
            foreach (NetSignature constructor in NetSignature.Constructors(type))
            {
                own.Add((shortName, constructor.FullText(type)));
            }

            if (type.IsValueType && !NetSignature.Constructors(type).Any(static c => c.Inputs.Length == 0))
            {
                own.Add((shortName, $"{NetNames.ClassName(type)} lhs1 {shortName}"));
            }
        }

        var aliases = OperatorNames(type).ToHashSet(StringComparer.Ordinal);
        foreach (string alias in aliases)
        {
            own.Add((alias, $"lhs1 {alias}(A, B)"));
        }

        IEnumerable<string> inherited = Inherited
            .Where(i => !i.Handle || !type.IsValueType)
            .Where(i => !(i.Text.Contains(" eq(", StringComparison.Ordinal) && aliases.Contains("eq"))
                && !(i.Text.Contains(" ne(", StringComparison.Ordinal) && aliases.Contains("ne")))
            .Select(static i => i.Text);
        return own.OrderBy(static o => o.Name, StringComparer.Ordinal).Select(static o => o.Text).Concat(inherited);
    }

    /// <summary>Names in columns filled top to bottom, each padded to <paramref name="width"/>.</summary>
    internal static IEnumerable<string> Columns(string[] names, int width)
    {
        int columns = Math.Max(1, WindowWidth / width);
        int rows = (names.Length + columns - 1) / columns;
        for (int r = 0; r < rows; r++)
        {
            var line = new System.Text.StringBuilder();
            for (int i = r; i < names.Length; i += rows)
            {
                line.Append(names[i].PadRight(width));
            }

            yield return line.ToString();
        }
    }
}
