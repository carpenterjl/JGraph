using System.Text;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>One call of a device method: the object, the arguments after it, and where it was written.</summary>
internal sealed class DeviceCall
{
    public required DeviceObject Target { get; init; }

    public required IReadOnlyList<JgsValue> Args { get; init; }

    /// <summary>How many outputs the caller asked for.</summary>
    public int Wanted { get; init; }

    public int Line { get; init; }

    public int Column { get; init; }

    public JGraphScriptGlobals Host => Target.Session.Host;

    public Interpreter Interpreter => Target.Interpreter;

    public JgsRuntimeException Error(string identifier, string message) => new(Line, Column, identifier, message);
}

/// <summary>A method body: answers its outputs (none for a method that returns nothing).</summary>
internal delegate JgsValue[] DeviceMethodBody(DeviceCall call);

/// <summary>One property a device class declares.</summary>
/// <param name="Name">The name as the class spells it.</param>
/// <param name="Get">Its getter.</param>
/// <param name="Set">Its setter, or null for a property that is read-only (MATLAB's SetProhibited).</param>
/// <param name="Hidden">Whether <c>properties</c>, <c>get</c> and the display leave it out (a legacy property).</param>
internal sealed record DeviceProperty(
    string Name,
    Func<DeviceObject, DeviceCall, JgsValue> Get,
    Action<DeviceObject, JgsValue, DeviceCall>? Set = null,
    bool Hidden = false);

/// <summary>
/// The declaration of a device class (device classes plan, architecture A): its name, the bases
/// <c>isa</c> and <c>superclasses</c> answer, its properties in the order <c>properties</c> lists them,
/// its methods, and the properties its short display shows. Everything the language asks of a device
/// object — a dot, a call, <c>get</c>, <c>set</c>, <c>properties</c>, <c>methods</c> — is answered
/// from this, once, by <see cref="DeviceObject"/>.
/// </summary>
internal sealed class DeviceClass
{
    private readonly Dictionary<string, DeviceProperty> _byName;
    private readonly Dictionary<string, DeviceProperty> _byNameIgnoringCase;

    public DeviceClass(
        string name,
        string shortName,
        IReadOnlyList<string> superclasses,
        IReadOnlyList<DeviceProperty> properties,
        IReadOnlyDictionary<string, DeviceMethodBody> methods,
        IReadOnlyList<string> methodListing,
        IReadOnlyList<string> displayProperties)
    {
        Name = name;
        ShortName = shortName;
        Superclasses = superclasses;
        Properties = properties;
        Methods = methods;
        MethodListing = methodListing;
        DisplayProperties = displayProperties;
        _byName = properties.ToDictionary(static p => p.Name, StringComparer.Ordinal);
        foreach (string method in methods.Keys)
        {
            AllMethodNames.TryAdd(method, 0);
        }

        _byNameIgnoringCase = new Dictionary<string, DeviceProperty>(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceProperty property in properties)
        {
            _byNameIgnoringCase.TryAdd(property.Name, property);
        }
    }

    /// <summary>Every method name any device class declares, filled as each class is declared.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> AllMethodNames = new(StringComparer.Ordinal);

    private static int s_allDeclared;

    /// <summary>
    /// Whether some device class has a method <paramref name="name"/> (serialport's serialbreak, a GPIB
    /// visadev's visatrigger): a call of it on an object whose class has none is R2025b's
    /// "Undefined function … for input arguments of type …", not an unknown name.
    /// </summary>
    public static bool IsSomeClassMethod(string name)
    {
        if (Interlocked.Exchange(ref s_allDeclared, 1) == 0)
        {
            foreach (Type type in typeof(DeviceClass).Assembly.GetTypes().Where(static t => t.IsSubclassOf(typeof(DeviceObject)) && !t.IsAbstract))
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
        }

        return AllMethodNames.ContainsKey(name);
    }

    /// <summary>What <c>class</c> answers: <c>internal.Serialport</c>.</summary>
    public string Name { get; }

    /// <summary>The name the display and R2025b's sentences use: <c>Serialport</c>.</summary>
    public string ShortName { get; }

    /// <summary>Every base, in the order <c>superclasses</c> lists them.</summary>
    public IReadOnlyList<string> Superclasses { get; }

    /// <summary>Every property, visible ones in <c>properties</c> order.</summary>
    public IReadOnlyList<DeviceProperty> Properties { get; }

    /// <summary>Every method a call reaches, hidden ones included.</summary>
    public IReadOnlyDictionary<string, DeviceMethodBody> Methods { get; }

    /// <summary>What <c>methods</c> lists.</summary>
    public IReadOnlyList<string> MethodListing { get; }

    /// <summary>The properties the short display shows.</summary>
    public IReadOnlyList<string> DisplayProperties { get; }

    /// <summary>The visible properties' names, in order.</summary>
    public IEnumerable<string> VisibleNames => Properties.Where(static p => !p.Hidden).Select(static p => p.Name);

    /// <summary>A property by its exact name (a dot), or by a case-blind one (<c>get</c> and <c>set</c>).</summary>
    public DeviceProperty? Find(string name, bool ignoreCase = false) =>
        (ignoreCase ? _byNameIgnoringCase : _byName).TryGetValue(name, out DeviceProperty? found) ? found : null;

    /// <summary>
    /// Whether the class reads property names as a classdef with <c>CaseInsensitiveProperties</c> and
    /// <c>TruncatedProperties</c> does (audioplayer, audiorecorder): any case, and any prefix that names
    /// one property, hidden ones counted, on a dot, <c>get</c> and <c>set</c> alike.
    /// </summary>
    public bool LooseNames { get; init; }

    /// <summary>What <c>[a b]</c> of this class's objects throws, when it is not R2025b's general refusal.</summary>
    public (string Identifier, string Message)? ConcatenationRefusal { get; init; }

    /// <summary>
    /// Whether the class is a plain handle rather than a matlab.mixin.SetGet, so that <c>get</c> and
    /// <c>set</c> find no method for it (mididevice, midicontrols).
    /// </summary>
    public bool NoGetSet { get; init; }

    /// <summary>
    /// A property by a loose name (<see cref="LooseNames"/>): the case-blind name, else the one property
    /// it is a prefix of; <paramref name="ambiguous"/> says a prefix named several.
    /// </summary>
    public DeviceProperty? FindLoose(string name, out bool ambiguous)
    {
        ambiguous = false;
        if (Find(name, ignoreCase: true) is { } exact)
        {
            return exact;
        }

        if (name.Length == 0)
        {
            return null;
        }

        DeviceProperty[] prefixed = Properties.Where(p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        ambiguous = prefixed.Length > 1;
        return prefixed.Length == 1 ? prefixed[0] : null;
    }
}

/// <summary>
/// A device object held by a script (device classes plan, architecture A): a handle that answers the
/// language's questions from its <see cref="DeviceClass"/>, whose resources (a port, a socket, a
/// reader thread) close when <c>delete</c> runs or when its last holder lets go — counted exactly, as
/// a handle object with a destructor is (ADR 0171), so <c>clear s</c> frees the port before the next
/// statement.
/// </summary>
internal abstract class DeviceObject : IJgsExternal
{
    protected DeviceObject(DeviceSession session, Interpreter interpreter)
    {
        Session = session;
        Interpreter = interpreter;

        // A call of a name nothing else answers, read(s, …), must now look among its arguments for an
        // object with that method, as it does once .NET values are about.
        interpreter.NoteNet();
    }

    /// <summary>The session's devices this object is one of.</summary>
    public DeviceSession Session { get; }

    /// <summary>The interpreter that made it: whose callbacks it runs and whose lifetimes count it.</summary>
    public Interpreter Interpreter { get; }

    /// <summary>The class declaration.</summary>
    public abstract DeviceClass Class { get; }

    public string ClassName => Class.Name;

    public bool IsHandle => true;

    public bool IsA(string className) =>
        className == Class.Name || className == "handle" || Class.Superclasses.Contains(className);

    public IJgsExternal CopyForBinding() => this;

    public string Kind => "device object";

    /// <summary>Whether <c>delete</c> has run.</summary>
    public bool Deleted { get; private set; }

    // --- exact lifetime (ADR 0171) ---------------------------------------------------------------

    /// <summary>How many holders the lifetime count sees.</summary>
    internal int Exact { get; set; }

    /// <summary>Whether the count has seen this object bound at least once.</summary>
    internal bool Counted { get; set; }

    /// <summary>Runs <c>delete</c>: once, releasing what the object holds.</summary>
    public void Delete()
    {
        if (Deleted)
        {
            return;
        }

        Deleted = true;
        try
        {
            OnDelete();
        }
        finally
        {
            Session.Forget(this);
        }
    }

    /// <summary>What <c>delete</c> releases.</summary>
    protected abstract void OnDelete();

    // --- display -----------------------------------------------------------------------------------

    public string Display() => Deleted ? "handle to deleted " + Class.ShortName : ShortDisplay();

    /// <summary>
    /// The short display, in JGraph's layout (ADR 0174): the class's display properties, then R2025b's
    /// pointer to the rest, as text.
    /// </summary>
    protected virtual string ShortDisplay()
    {
        var sb = new StringBuilder(Class.ShortName).Append(" with properties:\n");
        foreach (string name in Class.DisplayProperties)
        {
            sb.Append("\n    ").Append(name).Append(": ").Append(Shown(ReadForDisplay(name)));
        }

        sb.Append("\n\n  Show all properties, functions");
        return sb.ToString();
    }

    /// <summary>Every visible property, as <c>get(obj)</c> displays them.</summary>
    public string LongDisplay()
    {
        var sb = new StringBuilder();
        foreach (string name in Class.VisibleNames)
        {
            sb.Append("    ").Append(name).Append(": ").Append(Shown(ReadForDisplay(name))).Append('\n');
        }

        return sb.ToString();
    }

    private JgsValue ReadForDisplay(string name)
    {
        try
        {
            return GetProperty(name, new DeviceCall { Target = this, Args = [] });
        }
        catch (JgsException)
        {
            return JgsValue.Str("?");
        }
    }

    /// <summary>How a property value shows in a display line: text quoted as MATLAB shows it, the rest in JGraph's layout.</summary>
    internal static string Shown(JgsValue value)
    {
        if (value.IsStringArray && value.ArrayLength == 1 && value.ElementAt(0).Type == JgsType.String)
        {
            return $"\"{value.ElementAt(0).AsString}\"";
        }

        if (value.Type == JgsType.Function)
        {
            return JgsBuiltins.SourceTextOf("disp", value, 0, 0);
        }

        if (value.AsExternalOrNull() is DeviceEnumValue member)
        {
            return member.Choice;
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
        {
            return "[]";
        }

        return Net.NetDisplay.Shown(value);
    }

    public virtual string? Summary() => null;

    // --- members -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>obj.Name</c>: a property's value, or a method bound to the object (called at once when the
    /// mention is bare). An exact name, as a dot is in MATLAB.
    /// </summary>
    public JgsValue Member(string name, bool autoCall, int wanted, int line, int col)
    {
        var call = new DeviceCall { Target = this, Args = [], Line = line, Column = col, Wanted = wanted };
        if (Class.Find(name) is { } property)
        {
            LiveOrThrow(line, col);
            return property.Get(this, call);
        }

        if (Class.Methods.TryGetValue(name, out DeviceMethodBody? body))
        {
            var bound = new DeviceBoundMethod(this, name, body);
            return autoCall ? bound.CallMultiple([], wanted, line, col) is [var first, ..] ? first : JgsValue.Null : JgsValue.Function(bound);
        }

        LiveOrThrow(line, col);
        if (Class.LooseNames && Loose(name, "MATLAB:class:AmbiguousProperty", line, col) is { } loose)
        {
            return loose.Get(this, call);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{name}' for class '{Class.Name}'.");
    }

    /// <summary>A loosely named property (<see cref="DeviceClass.LooseNames"/>), or null; a prefix naming several throws <paramref name="ambiguousId"/>.</summary>
    private DeviceProperty? Loose(string name, string ambiguousId, int line, int col)
    {
        DeviceProperty? found = Class.FindLoose(name, out bool ambiguous);
        return ambiguous
            ? throw new JgsRuntimeException(line, col, ambiguousId, $"Ambiguous {Class.Name} property: '{name}'.")
            : found;
    }

    /// <summary>A property's value by name for <c>get</c> (case-blind).</summary>
    public JgsValue GetProperty(string name, DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        DeviceProperty property = (Class.LooseNames ? Loose(name, "MATLAB:class:AmbiguousProperty", call.Line, call.Column) : Class.Find(name, ignoreCase: true))
            ?? throw call.Error("MATLAB:class:setgetPropertyNotFound",
                $"Property {name} not found in class {Class.Name}, or is not present in all elements of the array of class {Class.Name}.");
        return property.Get(this, call);
    }

    /// <summary><c>obj.Name = value</c> (exact name) or <c>set(obj, name, value)</c> (case-blind).</summary>
    public void SetProperty(string name, JgsValue value, DeviceCall call, bool ignoreCase)
    {
        LiveOrThrow(call.Line, call.Column);
        DeviceProperty? property = Class.LooseNames
            ? Loose(name, ignoreCase ? "MATLAB:class:InvalidProperty" : "MATLAB:class:AmbiguousProperty", call.Line, call.Column)
            : Class.Find(name, ignoreCase);
        if (property is null)
        {
            throw ignoreCase
                ? call.Error("MATLAB:class:InvalidProperty",
                    $"The name '{name}' is not an accessible property for an instance of class '{Class.Name}'.")
                : call.Error("MATLAB:noPublicFieldForClass", $"Unrecognized property '{name}' for class '{Class.Name}'.");
        }

        if (property.Set is null)
        {
            throw call.Error("MATLAB:class:SetProhibited",
                $"Unable to set the '{property.Name}' property of class ''{Class.ShortName}'' because it is read-only.");
        }

        property.Set(this, value, call);
    }

    /// <summary>Throws R2025b's refusal for a deleted handle.</summary>
    public void LiveOrThrow(int line, int col)
    {
        if (Deleted)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");
        }
    }

    /// <summary>Whether a call <c>name(…, obj, …)</c> reaches one of this class's methods.</summary>
    public bool HasMethod(string name) => Class.Methods.ContainsKey(name);

    /// <summary>Runs a method with the object first among <paramref name="args"/>, as function syntax writes it.</summary>
    public JgsValue[] CallMethod(string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        var rest = new List<JgsValue>(args.Count);
        bool dropped = false;
        foreach (JgsValue argument in args)
        {
            if (!dropped && ReferenceEquals(argument.AsExternalOrNull(), this))
            {
                dropped = true;
                continue;
            }

            rest.Add(argument);
        }

        return Class.Methods[name](new DeviceCall { Target = this, Args = rest, Wanted = wanted, Line = line, Column = col });
    }

    /// <summary>A method bound to its object: what <c>obj.read</c> answers, and what <c>read(obj, …)</c> resolves to.</summary>
    internal sealed class DeviceBoundMethod(DeviceObject target, string name, DeviceMethodBody body) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            body(new DeviceCall { Target = target, Args = arguments, Wanted = wanted, Line = line, Column = column });
    }

    /// <summary>A method reached by function syntax, <c>read(obj, …)</c>: the object leaves the arguments.</summary>
    internal sealed class DeviceFunctionSyntax(DeviceObject target, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            target.CallMethod(name, arguments, wanted, line, column);
    }
}
