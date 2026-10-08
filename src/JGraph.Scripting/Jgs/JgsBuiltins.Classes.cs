namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs that ask a value what class it is and what that class can do (M68): <c>isobject</c>,
/// <c>properties</c>, <c>methods</c>, <c>metaclass</c>, and <c>addCause</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>properties</c> and <c>methods</c> are ordinary builtin names rather than keywords, which is what
/// the <c>classdef</c> parser was careful to preserve: the two words are recognised as block openers
/// only inside a class definition, so they stay available as the names of the two verbs that ask an
/// object what it has.
/// </para>
/// <para>
/// <b>MException stays a tagged struct.</b> The plan for this milestone called for turning it into a
/// real <see cref="JgsType.Object"/>, and re-checking that before doing it showed there was nothing
/// left to gain: <c>class(ME)</c>, <c>isa(ME, 'MException')</c>, every field read, <c>throw</c> and
/// <c>rethrow</c> already answer as an object, and <c>ME.stack</c> became a true struct array the day
/// M65 made struct arrays real. The one thing that answered wrongly was <c>isstruct(ME)</c>, and that
/// is fixed here as a rule about tagged values rather than about MException — which fixes
/// <c>containers.Map</c>, <c>dictionary</c> and the spatial-reference types in the same line.
/// Converting it would have routed the error path — the one that runs when something has already gone
/// wrong — through brand-new machinery to win a single predicate.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name <c>metaclass</c> answers to.</summary>
    private const string MetaClassName = "meta.class";

    /// <summary>Declares the class-introspection builtins into <paramref name="env"/>.</summary>
    internal static void RegisterClassBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

        Define("isobject", (args, line, col) =>
        {
            Arity("isobject", args, 1, line, col);
            return JgsValue.Bool(IsObjectValue(args[0]));
        });

        Define("properties", (args, line, col) =>
        {
            Arity("properties", args, 1, line, col);
            JgsValue names = CellColumn(PropertyNames("properties", args[0], interpreter, line, col));
            if (names.AsCell.Length == 0 && IsDeviceValue(args[0].AsExternalOrNull()))
            {
                names.Reshape(0, 1); // midicontrols: a 0-by-1 cell (probe_midi_controls)
            }

            return names;
        });

        // methods(x) answers the names as a cell column; methods(x, '-full') a .NET type's signatures.
        // Asked for nothing, a .NET type's listing is printed as R2025b prints it (stage 2, ADR 0175);
        // anything else still answers its cell, which the statement binds to ans.
        JgsValue Methods(IReadOnlyList<JgsValue> args, int line, int col)
        {
            ArityRange("methods", args, 1, 2, line, col);
            bool full = MethodsFull(args, line, col);

            // A library, a lib.pointer or a libstruct (ADR 0183): its names, or its -full lines
            // without their inheritance notes; a lib. name that names nothing answers [].
            if (LibListingOf(args[0], interpreter, out bool unknownLib) is { } lib)
            {
                return CellColumn(full ? lib.Lines.Select(static l => l.Text) : lib.Names);
            }

            if (unknownLib)
            {
                return JgsEmpty.Zero();
            }

            // A user class's -full lines, a struct's or a function handle's names, and [] for a
            // name that is no class (open item 14, measured).
            switch (MethodsSubject(args[0]))
            {
                case { Class: { } cls } when full:
                    return CellColumn(ClassMethodsListing.FullLines(cls, notes: false));
                case { Builtin: { } names }:
                    return CellColumn(names);
                case { Unknown: not null }:
                    return JgsEmpty.Zero();
            }

            return full && NetTypeNamed(args[0], interpreter) is { } type
                ? CellColumn(Net.NetMethodsListing.FullLines(type))
                : CellColumn(MethodNames("methods", args[0], interpreter, line, col));
        }

        // What a methods call is about, when it is one the listing of open item 14 covers: a user
        // class (an instance or its name), a built-in class whose names were measured (a struct, a
        // function handle, or either named), or in the MATLAB dialect a name that is nothing at all —
        // not a class, a .NET type, a library or a builtin, so methods('double') keeps its refusal
        // rather than claiming there is no such class.
        (JgsClass? Class, string[]? Builtin, string? BuiltinName, string? Unknown) MethodsSubject(JgsValue value)
        {
            if (value.Type == JgsType.Object)
            {
                return (value.AsObject.Class, null, null, null);
            }

            if (NamedClass(value, interpreter) is { } named)
            {
                return (named, null, null, null);
            }

            string? builtinName = IsTextScalar(value) ? TextOf(value)
                : value.Type is JgsType.Struct or JgsType.Function ? ClassOf(value, JgsDialect.Matlab)
                : null;
            if (builtinName is not null && ClassMethodsListing.BuiltinClassMethods.TryGetValue(builtinName, out string[]? names))
            {
                return (null, names, builtinName, null);
            }

            if (interpreter.Dialect.IsMatlab && IsTextScalar(value) && TextOf(value) is { Length: > 0 } text
                && !text.Contains('.', StringComparison.Ordinal) && interpreter.BuiltinClass(text) is null
                && !env.TryGet(text, out _) && LibListingOf(value, interpreter, out _) is null)
            {
                return (null, null, null, text);
            }

            return (null, null, null, null);
        }

        env.Builtins.Register("methods", JgsValue.Function(new BuiltinFunction("methods", Methods)
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) =>
            {
                ArityRange("methods", args, 1, 2, line, col);
                if (wanted == 0 && interpreter.Host is { } libHost)
                {
                    bool full = MethodsFull(args, line, col);
                    if (LibListingOf(args[0], interpreter, out bool unknownLib) is { } lib)
                    {
                        libHost.WriteOut(full
                            ? Native.LibMethodsListing.Full(lib.ClassName, lib.Lines)
                            : Native.LibMethodsListing.Names(lib.ClassName, lib.Own, lib.IsLibrary));
                        return [];
                    }

                    if (unknownLib)
                    {
                        libHost.WriteOut($"\nNo class '{TextOf(args[0])}'.\n\n");
                        return [];
                    }
                }

                if (wanted == 0 && interpreter.Host is { } listingHost)
                {
                    string? listing = MethodsSubject(args[0]) switch
                    {
                        { Class: { } cls } => MethodsFull(args, line, col) ? ClassMethodsListing.Full(cls) : ClassMethodsListing.Names(cls),
                        { Builtin: { } names, BuiltinName: { } className } => ClassMethodsListing.BuiltinNames(className, names),
                        { Unknown: { } name } => ClassMethodsListing.NoClass(name),
                        _ => null,
                    };
                    if (listing is not null)
                    {
                        listingHost.WriteOut(listing);
                        return [];
                    }
                }

                if (wanted == 0 && NetTypeNamed(args[0], interpreter) is { } type && interpreter.Host is { } host)
                {
                    string className = args[0].AsExternalOrNull()?.ClassName ?? Net.NetNames.ClassName(type);
                    host.print(MethodsFull(args, line, col)
                        ? Net.NetMethodsListing.Full(type, className)
                        : Net.NetMethodsListing.Names(type, className));
                    return [];
                }

                return [Methods(args, line, col)];
            },
        }));

        // isprop(obj, name): whether the object declares the property (V6, #28: a Dependent one
        // counts), whether a class-named value or a graphics handle answers to it; false otherwise.
        Define("isprop", (args, line, col) =>
        {
            Arity("isprop", args, 2, line, col);
            string name = TextOf(args[1]);
            JgsValue target = args[0];
            if (target.Type == JgsType.Object)
            {
                // Any property, whoever may read it (U6, measured: isprop of a private one is true).
                return JgsValue.Bool(target.AsObject.Class.Property(name) is not null);
            }

            if (target.Type == JgsType.External && IsDeviceValue(target.AsExternal))
            {
                return JgsValue.Bool(DeviceHasProperty(target.AsExternal, name)); // hidden legacy ones too (probe_sp_object)
            }

            if (target.Type == JgsType.External)
            {
                return JgsValue.Bool(PropertyNames("isprop", target, interpreter, line, col).Contains(name, StringComparer.Ordinal));
            }

            if (target.Type == JgsType.Struct && target.ClassName is not null)
            {
                return JgsValue.Bool(!target.IsStructArray && target.AsStruct.ContainsKey(name));
            }

            if (JgsHandleRegistry.TryGet(target, out JgsHandleEntry? handle))
            {
                try
                {
                    _ = GetHandleProperty(handle, name, line, col);
                    return JgsValue.Bool(true);
                }
                catch (JgsRuntimeException)
                {
                    return JgsValue.Bool(false);
                }
            }

            return JgsValue.Bool(false);
        });

        // ismethod(obj, name): whether the object's class has a method of that name — a classdef
        // method, or a .NET method, static ones included (R2025b, net_basics; ADR 0174).
        Define("ismethod", (args, line, col) =>
        {
            Arity("ismethod", args, 2, line, col);
            string name = TextOf(args[1]);
            return JgsValue.Bool(args[0].Type switch
            {
                // The methods a listing shows (U6, measured: a private, protected or hidden one is not one).
                JgsType.Object => args[0].AsObject.Class.MethodNames.Contains(name, StringComparer.Ordinal),
                JgsType.External when args[0].AsExternal is NetObject net =>
                    Net.NetInvoke.HasMethod(net.Type, name, instance: true),
                JgsType.External when args[0].AsExternal is Devices.DeviceObject device =>
                    device.Class.MethodListing.Contains(name, StringComparer.Ordinal),
                _ => false,
            });
        });

        // superclasses(obj) or superclasses('Name'): each superclass followed by its own, a column
        // (U6, measured: {'U6Mid'; 'U6Base'; 'handle'}); a value of a built-in class has none.
        // Asked for nothing it prints R2025b's listing instead (open item 27, measured): a blank line,
        // "Superclasses for class X:", a blank line, each name indented four, and a blank line; or
        // "No superclasses for class X." between blank lines.
        env.Builtins.Register("superclasses", JgsValue.Function(new BuiltinFunction("superclasses", (args, line, col) =>
            Superclasses(args, line, col).Column)
        {
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) =>
            {
                (JgsValue column, string[] names, string className) = Superclasses(args, line, col);
                if (wanted == 0 && interpreter.Host is { } host)
                {
                    host.WriteOut(names.Length == 0
                        ? $"\nNo superclasses for class {className}.\n\n"
                        : $"\nSuperclasses for class {className}:\n\n{string.Concat(names.Select(static n => $"    {n}\n"))}\n");
                    return [];
                }

                return [column];
            },
        }));

        (JgsValue Column, string[] Names, string ClassName) Superclasses(IReadOnlyList<JgsValue> args, int line, int col)
        {
            Arity("superclasses", args, 1, line, col);
            JgsClass? asked = args[0].Type == JgsType.Object ? args[0].AsObject.Class
                : IsTextScalar(args[0]) ? interpreter.ClassForLoad(TextOf(args[0])) ?? interpreter.BuiltinClass(TextOf(args[0]))
                : null;
            string className = asked?.Name ?? (IsTextScalar(args[0]) ? TextOf(args[0]) : ClassOf(args[0], JgsDialect.Matlab));
            string[] names = asked is null ? [] : [.. asked.SuperclassNames];
            JgsValue column = JgsValue.Cell([.. names.Select(JgsValue.Str)]);
            column.Reshape(names.Length, 1);
            return (column, names, className);
        }

        Define("metaclass", (args, line, col) =>
        {
            Arity("metaclass", args, 1, line, col);
            if (args[0].Type == JgsType.Object)
            {
                return MetaClassOf(args[0].AsObject.Class);
            }

            JgsValue described = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(ClassOf(args[0], JgsDialect.Matlab)),
                ["PropertyList"] = CellColumn(PropertyNames("metaclass", args[0], interpreter, line, col)),
                ["MethodList"] = CellColumn(MethodNames("metaclass", args[0], interpreter, line, col)),
            });

            described.SetClassName(MetaClassName);
            return described;
        });

        Define("addCause", (args, line, col) =>
        {
            Arity("addCause", args, 2, line, col);
            (string identifier, string message) = ReadErrorValue("addCause", args[0], line, col);
            if (args[1].ClassName != ExceptionClass)
            {
                throw new JgsRuntimeException(line, col,
                    "Invalid input for argument 2 (rhs2): Value must be 'MException scalar'.");
            }

            // A new exception rather than a write into the old one: MException is a value here, and a
            // script that writes `ME = addCause(ME, cause)` should not also have changed whatever else
            // was holding the original.
            // The causes are entries of the new exception (M2), in the column R2025b keeps them in.
            JgsValue[] causes = [.. ExistingCauses(args[0]).Select(JgsValue.Share), JgsValue.Share(args[1])];
            JgsValue built = MakeException(
                identifier, message, Field(args[0], "stack") is { } stack ? JgsValue.Share(stack) : StackValue([]));
            JgsValue column = JgsValue.Cell(causes);
            column.Reshape(causes.Length, 1);
            built.AsStruct["cause"] = column;
            return built;
        });
    }

    /// <summary>The <c>meta.class</c> of a user class named rather than instantiated: <c>?Circle</c>.</summary>
    internal static JgsValue MetaClassOf(JgsClass definition)
    {
        JgsValue described = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(definition.Name),
            ["PropertyList"] = CellColumn(definition.Properties.Select(static p => p.Spec.Name)),
            ["MethodList"] = CellColumn(definition.MethodNames),
            ["Abstract"] = JgsValue.Bool(definition.IsAbstract),
            ["Sealed"] = JgsValue.Bool(definition.Declaration.Sealed),
            ["HandleCompatible"] = JgsValue.Bool(definition.IsHandle || definition.Declaration.HandleCompatible),
            ["SuperclassList"] = SuperclassList(definition),
        });

        described.SetClassName(MetaClassName);
        return described;
    }

    /// <summary>
    /// A metaclass's <c>SuperclassList</c>: the classes its header names, each by its name, as a
    /// column a script indexes and counts (U6). <c>handle</c> has no entry here, as it has no file.
    /// </summary>
    private static JgsValue SuperclassList(JgsClass definition)
    {
        Dictionary<string, JgsValue>[] direct =
        [
            .. definition.Supers.Select(static super => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(super.Name),
            }),
        ];
        return JgsValue.StructArray(new JgsStructArray(direct), direct.Length, direct.Length == 0 ? 0 : 1);
    }

    /// <summary>The causes an exception already carries, or none.</summary>
    private static JgsValue[] ExistingCauses(JgsValue exception) =>
        Field(exception, "cause") is { Type: JgsType.Cell } held ? held.AsCell : [];

    /// <summary>One field of a struct-shaped value, or null when it has no such field.</summary>
    private static JgsValue? Field(JgsValue value, string name) =>
        value.Type == JgsType.Struct && value.AsStruct.TryGetValue(name, out JgsValue? held) ? held : null;

    /// <summary>
    /// Whether a value is an object: an instance of a user class, or one of the values that carry a
    /// class name because they stand for a MATLAB object (MException, containers.Map, the spatial
    /// reference types). It is the same question <see cref="IsStructValue"/> answers the other way
    /// round, which is why the two read one property between them.
    /// </summary>
    internal static bool IsObjectValue(JgsValue value) =>
        value.Type is JgsType.Object or JgsType.External
        || (value.Type == JgsType.Struct && value.ClassName is not null);

    /// <summary>
    /// Whether a value is a plain struct. A struct carrying a class name is not one: it is the
    /// representation an object is kept in, and <c>isstruct</c> saying otherwise is what let
    /// <c>isstruct(MException('a:b', 'x'))</c> answer true (M68). A struct carrying a time tag is not
    /// one either, for the same reason: a <c>calendarDuration</c> keeps its three components in a
    /// struct array because that storage already knows how to be an array, and the tag is what says
    /// the storage is not the type (M82).
    /// </summary>
    internal static bool IsStructValue(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName is null && value.TimeTag is null;

    /// <summary>The property names of whatever <paramref name="value"/> is, in declaration order.</summary>
    private static IEnumerable<string> PropertyNames(
        string builtin, JgsValue value, Interpreter interpreter, int line, int col)
    {
        if (value.Type == JgsType.Object)
        {
            return value.AsObject.Class.ListedProperties.Select(static p => p.Spec.Name); // public and not hidden (U6)
        }

        // A .NET object's readable properties and fields (ADR 0174); an assembly's seven lists; a
        // NET.NetException's ExceptionObject ahead of MException's own (measured, net_exceptions).
        switch (value.AsExternalOrNull())
        {
            case NetObject net:
                return Net.NetDisplay.MemberNames(net);
            case NetAssemblyValue:
                return NetAssemblyValue.PropertyNames;
            case NetMetaClass:
                return ["Name"];
            case NetGenericClass:
                return []; // "GenericClass with no properties." (probe4)
            case var lib when IsLibValue(lib):
                return LibPropertyNames(lib!); // Value, DataType; a libstruct's fields (ADR 0182)
            case var device when IsDeviceValue(device):
                return DevicePropertyNames(device!); // a serialport's visible properties (device classes plan)
        }

        if (value.ClassName == Net.NetInvoke.NetExceptionClass)
        {
            return ["ExceptionObject", "identifier", "message", "cause", "stack", "Correction"];
        }

        if (NamedClass(value, interpreter) is { } definition)
        {
            return definition.ListedProperties.Select(static p => p.Spec.Name);
        }

        if (IsMatFile(value))
        {
            return MatFilePropertyNames(value, line, col); // Properties, then the file's variables (V6, #112)
        }

        if (value.Type == JgsType.Struct)
        {
            return value.AsStructArray.FieldNames;
        }

        throw new JgsRuntimeException(line, col,
            $"{builtin}: a {value.TypeName} has no properties to list.");
    }

    /// <summary>The method names of whatever <paramref name="value"/> is, in declaration order.</summary>
    private static IEnumerable<string> MethodNames(
        string builtin, JgsValue value, Interpreter interpreter, int line, int col)
    {
        if (value.Type == JgsType.Object)
        {
            return value.AsObject.Class.MethodNames;
        }

        // A .NET object's, or a .NET type's named as text: methods('JGTest.Members') (ADR 0174).
        if (value.AsExternalOrNull() is NetObject net)
        {
            return Net.NetInvoke.MethodNames(net.Type);
        }

        if (IsLibValue(value.AsExternalOrNull()))
        {
            return LibMethodNames(value.AsExternal); // ADR 0182
        }

        if (IsDeviceValue(value.AsExternalOrNull()))
        {
            return DeviceMethodNames(value.AsExternal); // device classes plan
        }

        if (value.Type == JgsType.String && value.AsString.Contains('.', StringComparison.Ordinal)
            && interpreter.TryNetName(value.AsString, interpreter.CurrentFrame, out Type? type, out string? member) && member is null)
        {
            return Net.NetInvoke.MethodNames(type!);
        }

        if (NamedClass(value, interpreter) is { } definition)
        {
            return definition.MethodNames;
        }

        // A plain struct and a function handle answer R2025b's names before this (open item 14); a
        // tagged struct answers none, which lets a script ask about a value it has not looked at yet.
        return value.Type is JgsType.Function or JgsType.Struct
            ? []
            : throw new JgsRuntimeException(line, col,
                $"{builtin}: a {value.TypeName} has no methods to list.");
    }

    /// <summary>Whether a <c>methods</c> call asked for <c>-full</c>; any other option is refused.</summary>
    private static bool MethodsFull(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 2 && TextOf(args[1]) != "-full")
        {
            throw new JgsRuntimeException(line, col, $"methods: '{TextOf(args[1])}' is not an option; only '-full' is.");
        }

        return args.Count == 2;
    }

    /// <summary>The .NET type a value is, or names by text (<c>'JGTest.Members'</c>); null for anything else.</summary>
    private static Type? NetTypeNamed(JgsValue value, Interpreter interpreter)
    {
        if (value.AsExternalOrNull() is NetObject net)
        {
            return net.NullableOf is null ? net.Type : null;
        }

        // A command's word arrives as a string scalar (methods JGTest.Access, enumeration JGTest.Color).
        string? text = value.Type == JgsType.String ? value.AsString
            : value.IsStringArray && value.ArrayLength == 1 ? TextOf(value) : null;
        return text is not null && text.Contains('.', StringComparison.Ordinal)
            && interpreter.TryNetName(text, interpreter.CurrentFrame, out Type? type, out string? member) && member is null
            ? type
            : null;
    }

    /// <summary>
    /// The class a value <em>names</em>: <c>properties('Circle')</c> asks about the class rather than
    /// about the char row. Null when the value is not the name of a loaded class.
    /// </summary>
    private static JgsClass? NamedClass(JgsValue value, Interpreter interpreter) =>
        value.Type != JgsType.String ? null
        : interpreter.Classes.TryGetValue(value.AsString, out JgsClass? definition) ? definition
        : interpreter.ClassForLoad(value.AsString); // the first mention of the class loads its file (U6)

    /// <summary>A cell column of names — the shape MATLAB's <c>properties</c> and <c>methods</c> answer.</summary>
    private static JgsValue CellColumn(IEnumerable<string> names)
    {
        JgsValue[] cells = [.. names.Select(JgsValue.Str)];
        JgsValue column = JgsValue.Cell(cells);
        column.Reshape(cells.Length, cells.Length == 0 ? 0 : 1);
        return column;
    }
}
