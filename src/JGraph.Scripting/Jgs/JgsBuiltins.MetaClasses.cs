namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.metadata.Class</c> as R2025b answers it for <c>metaclass(x)</c>, <c>?C</c> and
/// <c>meta.class.fromName</c> (open item 196, ADR 0353): R2025b's eighteen properties, with
/// <c>MethodList</c>, <c>PropertyList</c>, <c>EventList</c>, <c>EnumerationMemberList</c> and
/// <c>SuperclassList</c> as columns of <c>matlab.metadata.Method</c>, <c>Property</c>, <c>Event</c>,
/// <c>EnumerationMember</c> and <c>Class</c> - so <c>{mc.MethodList.Name}</c> and
/// <c>arrayfun(@(m) m.DefiningClass.Name, mc.MethodList')</c> work.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order</b> is the one stage K9's <c>k9_build</c> recorded and probe <c>p196</c> confirmed: each
/// <c>methods</c> block's methods last first, the blocks in file order (a property's accessors are
/// not methods), the constructor R2025b supplies when the class writes none, <c>empty</c>, an
/// enumeration's seventeen conversions, then each superclass's list in header order less its
/// constructor, its <c>empty</c>, its private methods and those the class overrides. <c>handle</c>'s
/// list is 33 long, <c>notify</c> twice and <c>addlistener</c> and <c>listener</c> ten times each
/// (five of the pairs hidden). Properties and events are the class's own in file order, then each
/// superclass's that are not private.
/// </para>
/// <para>
/// <b>The objects</b> are structs carrying the class name, as <c>findprop</c>'s metaproperty already
/// was: their fields are R2025b's properties in its order, and a write to one is R2025b's
/// SetProhibited (Interpreter's member write). A member's <c>DefiningClass</c> and an entry of
/// <c>SuperclassList</c> are the defining class's own metaclass, whose members' <c>DefiningClass</c>
/// is in turn the class's metaclass without its five member lists: the value has to end somewhere,
/// where R2025b's handles point back at each other.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>R2025b's class of a metamethod.</summary>
    internal const string MetaMethodClassName = "matlab.metadata.Method";

    private const string MetaEventClassName = "matlab.metadata.Event";
    private const string MetaEnumerationMemberClassName = "matlab.metadata.EnumerationMember";
    private const string MetaValidationClassName = "matlab.metadata.Validation";
    private const string MetaNamespaceClassName = "matlab.metadata.Namespace";

    /// <summary>The depth of the metaclass a call answers: its members' defining classes have their lists, theirs do not.</summary>
    private const int MetaDepth = 2;

    private static readonly string[] MetaClassFieldNames =
    [
        "Name", "Description", "DetailedDescription", "Hidden", "Sealed", "Abstract", "Enumeration", "ConstructOnLoad",
        "HandleCompatible", "InferiorClasses", "Namespace", "Aliases", "RestrictsSubclassing", "PropertyList", "MethodList",
        "EventList", "EnumerationMemberList", "SuperclassList",
    ];

    private static readonly string[] MetaMethodFieldNames =
    [
        "Name", "Description", "DetailedDescription", "Access", "Static", "Abstract", "Sealed", "Hidden", "InputNames",
        "OutputNames", "DefiningClass",
    ];

    /// <summary>A metaproperty's fields in R2025b's order (probe p196: <c>properties(mc.PropertyList(1))</c>).</summary>
    private static readonly string[] MetaPropertyFieldNames =
    [
        "Name", "Description", "DetailedDescription", "GetAccess", "SetAccess", "Dependent", "Constant",
        "Abstract", "Transient", "Hidden", "GetObservable", "SetObservable", "AbortSet", "NonCopyable",
        "WeakHandle", "PartialMatchPriority", "HasDefault", "DefaultValue", "GetMethod", "SetMethod",
        "Validation", "DefiningClass",
    ];

    private static readonly string[] MetaEventFieldNames =
        ["Name", "Description", "DetailedDescription", "Hidden", "NotifyAccess", "ListenAccess", "DefiningClass"];

    private static readonly string[] MetaEnumerationFieldNames = ["Name", "Description", "DetailedDescription", "Hidden", "DefiningClass"];

    private static readonly string[] MetaValidationFieldNames = ["Class", "Size", "ValidatorFunctions"];

    /// <summary>A namespace's fields, as <see cref="PackageMeta"/> writes them, which an empty one has too.</summary>
    private static readonly string[] MetaNamespaceFieldNames =
    [
        "Name", "Description", "DetailedDescription", "ClassList", "FunctionList", "InnerNamespaces", "PackageList", "OuterNamespace",
        "ContainingPackage",
    ];

    private const string EmptyMethodDescription = "Returns an empty object array of the given size";

    /// <summary>The methods every enumeration lists after <c>empty</c> (probe p196 and k9_build): name, inputs, outputs, hidden, description.</summary>
    private static readonly (string Name, string[] Inputs, string[] Outputs, bool Hidden, string Description)[] MetaEnumerationMethods =
    [
        ("char", ["rhs1"], ["lhs1"], false, "Char conversion for enumeration classes"),
        ("eq", ["A", "B"], ["TF"], false, ""),
        ("ne", ["A", "B"], ["TF"], false, ""),
        ("colon", ["varargin"], ["varargout"], true, "colon method for enumeration derived from built-in classes"),
        ("cellstr", ["rhs1"], ["lhs1"], false, "Cellstr conversion for enumeration classes"),
        ("string", ["rhs1"], ["lhs1"], false, "string conversion for enumeration classes"),
        ("strcmp", ["rhs1", "rhs2"], ["lhs1"], false, "strcmp for enumeration classes"),
        ("strncmp", ["rhs1", "rhs2", "rhs3"], ["lhs1"], false, "strncmp for enumeration classes"),
        ("strcmpi", ["rhs1", "rhs2"], ["lhs1"], false, "strcmpi for enumeration classes"),
        ("strncmpi", ["rhs1", "rhs2", "rhs3"], ["lhs1"], false, "strncmpi for enumeration classes"),
        ("setdiff", ["varargin"], ["varargout"], false, "setdiff for enumeration classes"),
        ("setxor", ["varargin"], ["varargout"], false, "setxor for enumeration classes"),
        ("union", ["varargin"], ["varargout"], false, "union for enumeration classes"),
        ("intersect", ["varargin"], ["varargout"], false, "intersect for enumeration classes"),
        ("ismember", ["varargin"], ["varargout"], false, "ismember for enumeration classes"),
        ("isequal", ["rhs1"], ["lhs1"], false, "isequal for enumerations, including support for string/char vector/cellstr comparisons"),
        ("isequaln", ["rhs1"], ["lhs1"], false, "isequaln for enumerations, including support for string/char vector/cellstr comparisons"),
    ];

    /// <summary>Whether a value is one of the metadata objects this file makes, whose properties a script may read and not write.</summary>
    internal static bool IsMetadataValue(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName is { } name
        && (name.StartsWith("matlab.metadata.", StringComparison.Ordinal) || name == MetaClassName);

    /// <summary>The <c>matlab.metadata.Class</c> of a class: <c>?C</c>, <c>metaclass(obj)</c>, <c>meta.class.fromName('C')</c>.</summary>
    internal static JgsValue MetaClassOf(JgsClass definition)
    {
        var context = new MetaContext(definition.Interpreter);
        return context.Render(context.Model(definition.Name, definition), MetaDepth);
    }

    /// <summary><c>?handle</c> and <c>meta.class.fromName('handle')</c>: handle's metaclass (probe p196).</summary>
    internal static JgsValue MetaClassOfHandle(Interpreter interpreter)
    {
        var context = new MetaContext(interpreter);
        return context.Render(context.Model("handle"), MetaDepth);
    }

    /// <summary>The 0-by-0 metaclass <c>meta.class.fromName</c> answers for a name that is no class (probe p196).</summary>
    internal static JgsValue EmptyMetaClass() =>
        EmptyMetaArray(JgsRunningDialect.ThreadIsMatlab ? "matlab.metadata.Class" : MetaClassName, MetaClassFieldNames, 0, 0);

    /// <summary>
    /// The metaclass of a value of a class that is no class here - a double, a cell, a table: its name, and its
    /// method and property names as <c>methods</c> and <c>properties</c> list them, each a metamethod or a
    /// metaproperty of the class with nothing else known about it.
    /// </summary>
    private static JgsValue MetaClassOfValue(string className, IEnumerable<string> properties, IEnumerable<string> methods, Interpreter interpreter)
    {
        var model = new MetaModel(className);
        model.Properties.AddRange(properties.Select(p => new MetaPropertyModel(p, className)));
        model.Methods.AddRange(methods.Select(m => new MetaMethodModel(m, className, MemberAccess.Public, [], [])));
        var context = new MetaContext(interpreter);
        context.Seed(model);
        return context.Render(model, MetaDepth);
    }

    /// <summary>A device class's metaclass (stage K20): its properties with their access, its superclasses, its methods.</summary>
    private static JgsValue MetaClassOfDevice(Devices.DeviceObject device, Interpreter interpreter)
    {
        string className = device.ClassName;
        var model = new MetaModel(className) { Superclasses = [.. device.Class.Superclasses] };
        model.Properties.AddRange(device.Class.Properties.Select(p => new MetaPropertyModel(p.Name, className)
        {
            Set = p.Set is null ? new MemberAccess(MemberAccessKind.Private) : MemberAccess.Public,
            SetWord = p.Set is null ? "none" : null,
            Hidden = p.Hidden,
        }));
        model.Methods.AddRange(device.Class.MethodListing.Select(m => new MetaMethodModel(m, className, MemberAccess.Public, [], [])));
        var context = new MetaContext(interpreter);
        context.Seed(model);
        return context.Render(model, MetaDepth);
    }

    /// <summary>A user class's property as <c>findprop</c> answers it: the metaproperty its class's <c>PropertyList</c> holds.</summary>
    private static JgsValue UserMetaProperty(ClassProperty property)
    {
        JgsClass? owner = property.Owner;
        if (owner is null)
        {
            var bare = new MetaContext(null);
            return bare.RenderProperty(new MetaPropertyModel(property.Spec.Name, string.Empty) { Property = property }, 1);
        }

        var context = new MetaContext(owner.Interpreter);
        MetaModel model = context.Model(owner.Name, owner);
        MetaPropertyModel found = model.Properties.FirstOrDefault(p => ReferenceEquals(p.Property, property))
            ?? new MetaPropertyModel(property.Spec.Name, owner.Name) { Property = property, Owner = owner };
        return context.RenderProperty(found, MetaDepth);
    }

    /// <summary>A 0-by-0 of a metadata class with no fields: an empty <c>Namespace</c>, an empty <c>Validation.Class</c>.</summary>
    private static JgsValue EmptyMetaArray(string className, string[] fields, int rows, int cols)
    {
        JgsValue empty = JgsValue.StructArray(new JgsStructArray([], fields), rows, cols);
        empty.SetClassName(className);
        return empty;
    }

    /// <summary>Elements as a column of a metadata class, a 0-by-1 when there are none (R2025b's shape for every member list).</summary>
    private static JgsValue MetaColumn(List<Dictionary<string, JgsValue>> elements, string[] fields, string className)
    {
        JgsValue column = JgsValue.StructArray(new JgsStructArray([.. elements], fields), elements.Count, 1);
        column.SetClassName(className);
        return column;
    }

    /// <summary>Texts as a cell column, a 0-by-1 when there are none (a metamethod's <c>InputNames</c>).</summary>
    private static JgsValue TextColumn(IReadOnlyList<string> texts)
    {
        JgsValue column = JgsValue.Cell([.. texts.Select(JgsValue.Str)]);
        column.Reshape(texts.Count, 1);
        return column;
    }

    /// <summary>
    /// A member's <c>Description</c> and <c>DetailedDescription</c> from its help block (probe p196b): the
    /// first line without its spaces and without a first word that is the member's own name in any case
    /// (<c>% GO goes somewhere.</c> is "goes somewhere."), and the other lines as help reads them, joined by
    /// newlines.
    /// </summary>
    private static (string Description, string Detailed) MetaDescriptionOf(string block, string name)
    {
        if (block.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        string[] lines = block.TrimEnd('\n').Split('\n');
        string first = lines[0].Trim();
        int space = first.IndexOf(' ', StringComparison.Ordinal);
        string word = space < 0 ? first : first[..space];
        if (string.Equals(word, name, StringComparison.OrdinalIgnoreCase))
        {
            first = space < 0 ? string.Empty : first[(space + 1)..].TrimStart();
        }

        return (first, string.Join('\n', lines.Skip(1)));
    }

    /// <summary>A file's lines for reading members' help, or null when there is no file to read.</summary>
    private static string[]? MetaSourceLines(string? file)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return null;
        }

        try
        {
            return JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>One class's members as R2025b's metaclass lists them, before they become values.</summary>
    private sealed class MetaModel(string name)
    {
        public string Name { get; } = name;

        public string Description { get; set; } = string.Empty;

        public string Detailed { get; set; } = string.Empty;

        public bool Sealed { get; set; }

        public bool Abstract { get; set; }

        public bool Enumeration { get; set; }

        public bool ConstructOnLoad { get; set; }

        public bool HandleCompatible { get; set; }

        public bool RestrictsSubclassing { get; set; }

        public string[] Inferior { get; set; } = [];

        /// <summary>The package the class is in, or null.</summary>
        public string? Namespace { get; set; }

        public string[] Superclasses { get; set; } = [];

        public List<MetaMethodModel> Methods { get; } = [];

        public List<MetaPropertyModel> Properties { get; } = [];

        public List<MetaEventModel> Events { get; } = [];

        public List<string> Members { get; } = [];
    }

    private sealed record MetaMethodModel(string Name, string Definer, MemberAccess Access, string[] Inputs, string[] Outputs)
    {
        public string Description { get; init; } = string.Empty;

        public string Detailed { get; init; } = string.Empty;

        public bool Static { get; init; }

        public bool Abstract { get; init; }

        public bool Sealed { get; init; }

        public bool Hidden { get; init; }
    }

    private sealed record MetaPropertyModel(string Name, string Definer)
    {
        /// <summary>The declared property, or null for a property this build describes by name alone.</summary>
        public ClassProperty? Property { get; init; }

        /// <summary>The class that declares <see cref="Property"/>, whose file its default is evaluated in.</summary>
        public JgsClass? Owner { get; init; }

        public string Description { get; init; } = string.Empty;

        public string Detailed { get; init; } = string.Empty;

        public MemberAccess Set { get; init; } = MemberAccess.Public;

        /// <summary>A <c>SetAccess</c> word that is no access kind here (<c>none</c>), or null.</summary>
        public string? SetWord { get; init; }

        public bool Hidden { get; init; }
    }

    private sealed record MetaEventModel(string Name, string Definer, MemberAccess Notify, MemberAccess Listen, bool Hidden)
    {
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>The models one call reads, by class name, and the values it has made of them.</summary>
    private sealed class MetaContext(Interpreter? interpreter)
    {
        private readonly Dictionary<string, MetaModel> _models = new(StringComparer.Ordinal);
        private readonly Dictionary<(string, int), JgsValue> _rendered = [];

        private static string ClassValueName => JgsRunningDialect.ThreadIsMatlab ? "matlab.metadata.Class" : MetaClassName;

        /// <summary>Makes <paramref name="model"/> the one its name answers in this call.</summary>
        public void Seed(MetaModel model) => _models[model.Name] = model;

        /// <summary>The model of the class <paramref name="name"/>: a class here, <c>handle</c>, or a name with nothing known about it.</summary>
        public MetaModel Model(string name, JgsClass? known = null)
        {
            if (_models.TryGetValue(name, out MetaModel? held))
            {
                return held;
            }

            JgsClass? definition = known;
            if (definition is null && name is not ("handle" or "event.EventData") && interpreter is not null)
            {
                try
                {
                    definition = interpreter.ClassForLoad(name) ?? interpreter.BuiltinClass(name);
                }
                catch (JgsException)
                {
                    definition = null;
                }
            }

            MetaModel made = definition is not null ? FromClass(definition)
                : name is "handle" or "event.EventData" ? HandleModel(name)
                : new MetaModel(name);
            _models[name] = made;
            return made;
        }

        /// <summary>
        /// <c>handle</c>'s metaclass (probe p196, <c>meta.class.fromName('handle')</c>): abstract, constructed on load,
        /// its 33 methods and <c>ObjectBeingDestroyed</c>. <c>event.EventData</c> is read as it, since this build
        /// gives it nothing more.
        /// </summary>
        private static MetaModel HandleModel(string name)
        {
            var model = new MetaModel(name)
            {
                Description = name == "handle" ? "Base class for handle classes" : string.Empty,
                Abstract = true,
                ConstructOnLoad = true,
                HandleCompatible = true,
            };

            foreach (string compare in (string[])["eq", "ne", "lt", "gt", "le", "ge"])
            {
                model.Methods.Add(new MetaMethodModel(compare, name, MemberAccess.Public, ["A", "B"], ["TF"]));
            }

            model.Methods.Add(new MetaMethodModel("delete", name, MemberAccess.Public, ["obj"], []));
            model.Methods.Add(new MetaMethodModel("isvalid", name, MemberAccess.Public, ["obj"], ["validity"]) { Sealed = true });
            model.Methods.Add(new MetaMethodModel("findprop", name, MemberAccess.Public, ["object", "propname"], ["prop"]));
            model.Methods.Add(new MetaMethodModel("notify", name, MemberAccess.Public, ["sources", "eventname"], []));
            model.Methods.Add(new MetaMethodModel("notify", name, MemberAccess.Public, ["sources", "eventname", "eventdata"], []));
            string[][] listeners =
            [
                ["sources", "eventname", "callback"],
                ["sources", "properties", "eventname", "callback"],
                ["sources", "propertyname", "eventname", "callback"],
                ["sources", "propertyname", "eventname", "callback"],
                ["sources", "propertynames", "eventname", "callback"],
                ["sources", "eventname", "callback", "rhs4", "rhs5"],
                ["sources", "properties", "eventname", "callback", "rhs5", "rhs6"],
                ["sources", "propertyname", "eventname", "callback", "rhs5", "rhs6"],
                ["sources", "propertyname", "eventname", "callback", "rhs5", "rhs6"],
                ["sources", "propertynames", "eventname", "callback", "rhs5", "rhs6"],
            ];
            for (int i = 0; i < listeners.Length; i++)
            {
                model.Methods.Add(new MetaMethodModel("addlistener", name, MemberAccess.Public, listeners[i], ["L"]) { Hidden = i >= 5 });
                model.Methods.Add(new MetaMethodModel("listener", name, MemberAccess.Public, listeners[i], ["L"]) { Hidden = i >= 5 });
            }

            model.Methods.Add(new MetaMethodModel("findobj", name, MemberAccess.Public, ["H", "varargin"], ["HM"]));
            model.Methods.Add(EmptyMethod(name));
            model.Events.Add(new MetaEventModel(JgsClass.ObjectBeingDestroyed, name, new MemberAccess(MemberAccessKind.Private), MemberAccess.Public, false)
            {
                Description = "Notifies listeners that a particular object has been destroyed.",
            });
            return model;
        }

        private static MetaMethodModel EmptyMethod(string definer) =>
            new("empty", definer, MemberAccess.Public, ["varargin"], ["E"]) { Static = true, Hidden = true, Description = EmptyMethodDescription };

        /// <summary>The model of a class here, read from its declaration and its file's comments.</summary>
        private MetaModel FromClass(JgsClass definition)
        {
            ClassdefStmt declaration = definition.Declaration;
            string[]? lines = MetaSourceLines(declaration.SourceId);
            string shortName = definition.ConstructorName;
            var model = new MetaModel(definition.Name)
            {
                Sealed = declaration.Sealed,
                Abstract = definition.IsAbstract,
                Enumeration = definition.IsEnumeration,
                HandleCompatible = definition.IsHandle || declaration.HandleCompatible,
                RestrictsSubclassing = declaration.AllowedSubclasses is not null,
                Namespace = definition.IsPackaged ? definition.Name[..definition.Name.LastIndexOf('.')] : null,
            };
            (model.Description, model.Detailed) = lines is null ? (string.Empty, string.Empty) : MetaDescriptionOf(ClassBlock(lines), shortName);
            model.Inferior = definition.IsEnumeration && !declaration.InferiorClasses.Contains("string", StringComparer.Ordinal)
                ? [.. declaration.InferiorClasses, "string"]
                : [.. declaration.InferiorClasses];

            // The header's names, each the class it resolved to (handle and event.EventData are no class here).
            var supers = new List<string>();
            int next = 0;
            foreach (string named in declaration.Superclasses)
            {
                supers.Add(named is "handle" or "event.EventData" || next >= definition.Supers.Count ? named : definition.Supers[next++].Name);
            }

            model.Superclasses = [.. supers];
            _models[model.Name] = model; // before the superclasses are read, so a member's definer finds it

            // Methods: the blocks in file order, each last first; the supplied constructor; empty; an
            // enumeration's conversions; then each superclass's, less what the class has of its own.
            var own = definition.OwnMethods.ToList();
            var ownNames = new HashSet<string>(own.Select(static m => m.Function.Name), StringComparer.Ordinal);
            foreach (IGrouping<int, ClassMethod> block in own.GroupBy(static m => m.Block).OrderBy(static g => g.Key))
            {
                foreach (ClassMethod method in block.Reverse())
                {
                    model.Methods.Add(MethodModel(method, definition, lines, model));
                }
            }

            if (!ownNames.Contains(shortName))
            {
                model.Methods.Add(new MetaMethodModel(shortName, model.Name, MemberAccess.Public, SuperTakesArguments(definition) ? ["varargin"] : [], ["obj"])
                {
                    Description = model.Description,
                    Detailed = model.Detailed,
                });
                ownNames.Add(shortName);
            }

            model.Methods.Add(EmptyMethod(model.Name));
            ownNames.Add("empty");
            if (definition.IsEnumeration)
            {
                foreach ((string name, string[] inputs, string[] outputs, bool hidden, string description) in MetaEnumerationMethods)
                {
                    model.Methods.Add(new MetaMethodModel(name, model.Name, MemberAccess.Public, inputs, outputs) { Hidden = hidden, Description = description });
                    ownNames.Add(name);
                }
            }

            var seenMethods = new HashSet<MetaMethodModel>(ReferenceEqualityComparer.Instance);
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ClassProperty property in definition.Properties.Where(p => ReferenceEquals(p.Owner, definition)))
            {
                model.Properties.Add(PropertyModel(property, definition, lines, model.Name));
                propertyNames.Add(property.Spec.Name);
            }

            foreach (string eventName in definition.Events)
            {
                if (definition.Event(eventName) is { } declared && ReferenceEquals(declared.Owner, definition))
                {
                    model.Events.Add(new MetaEventModel(declared.Name, model.Name, declared.NotifyAccess, declared.ListenAccess, declared.Hidden));
                }
            }

            model.Members.AddRange(definition.EnumerationNames);

            foreach (string superName in supers)
            {
                MetaModel super = Model(superName);
                string superShort = superName[(superName.LastIndexOf('.') + 1)..];
                foreach (MetaMethodModel inherited in super.Methods)
                {
                    bool constructor = inherited.Name == superShort && inherited.Definer == super.Name;
                    if (constructor || inherited.Name == "empty" || inherited.Access.Kind == MemberAccessKind.Private
                        || ownNames.Contains(inherited.Name) || !seenMethods.Add(inherited))
                    {
                        continue;
                    }

                    model.Methods.Add(inherited);
                }

                foreach (MetaPropertyModel inherited in super.Properties)
                {
                    if (inherited.Property?.GetAccess.Kind != MemberAccessKind.Private && propertyNames.Add(inherited.Name))
                    {
                        model.Properties.Add(inherited);
                    }
                }

                foreach (MetaEventModel inherited in super.Events)
                {
                    if (!model.Events.Exists(e => e.Name == inherited.Name))
                    {
                        model.Events.Add(inherited);
                    }
                }
            }

            return model;
        }

        /// <summary>Whether a superclass's constructor takes an argument, which gives a supplied constructor <c>varargin</c> (probe p196).</summary>
        private static bool SuperTakesArguments(JgsClass definition) =>
            definition.Supers.Any(static super => super.TryOwnMethod(super.ConstructorName, out ClassMethod? constructor)
                ? constructor.Function.Parameters.Count > 0
                : SuperTakesArguments(super));

        /// <summary>A class file's help block: the comments at its top, else those under the classdef line.</summary>
        private static string ClassBlock(string[] lines)
        {
            int first = SkipBlankLines(lines, 0);
            if (first >= lines.Length)
            {
                return string.Empty;
            }

            if (lines[first].TrimStart().StartsWith('%'))
            {
                return HelpBlock(lines, first);
            }

            int declaration = FirstCodeLine(lines, first);
            return declaration < lines.Length && StartsWithWord(lines[declaration].TrimStart(), "classdef")
                ? BlockAfterDeclaration(lines, declaration)
                : string.Empty;
        }

        private static MetaMethodModel MethodModel(ClassMethod method, JgsClass definition, string[]? classLines, MetaModel model)
        {
            FnStmt function = method.Function;
            string[]? lines = method.File is { } file ? MetaSourceLines(file) : classLines;
            string block = lines is not null && function.Line >= 1 && function.Line <= lines.Length && method.Native is null
                ? BlockAfterDeclaration(lines, function.Line - 1)
                : string.Empty;
            (string description, string detailed) = MetaDescriptionOf(block, function.Name);
            if (function.Name == definition.ConstructorName && description.Length == 0 && detailed.Length == 0)
            {
                (description, detailed) = (model.Description, model.Detailed); // a constructor without help reads the class's
            }

            return new MetaMethodModel(
                function.Name, model.Name, method.Access,
                method.Undeclared ? ["varargin"] : [.. function.Parameters],
                method.Undeclared ? ["varargout"] : [.. function.Outputs])
            {
                Description = description,
                Detailed = detailed,
                Static = method.Static,
                Abstract = method.Abstract,
                Sealed = method.Sealed,
                Hidden = method.Hidden,
            };
        }

        /// <summary>A property's model, its description the block above its line or else the comment at its end.</summary>
        private static MetaPropertyModel PropertyModel(ClassProperty property, JgsClass owner, string[]? lines, string definer)
        {
            string description = string.Empty, detailed = string.Empty;
            int at = property.Spec.Line - 1;
            if (lines is not null && at >= 0 && at < lines.Length)
            {
                int above = at;
                while (above > 0 && lines[above - 1].TrimStart().StartsWith('%'))
                {
                    above--;
                }

                if (above < at)
                {
                    (description, detailed) = MetaDescriptionOf(HelpBlock(lines, above), property.Spec.Name);
                }
                else
                {
                    string code = CodePart(lines[at]);
                    if (code.Length < lines[at].Length)
                    {
                        string comment = lines[at][code.Length..];
                        (description, _) = MetaDescriptionOf(' ' + (comment.StartsWith("%%", StringComparison.Ordinal) ? comment[2..] : comment[1..]), property.Spec.Name);
                    }
                }
            }

            return new MetaPropertyModel(property.Spec.Name, definer)
            {
                Property = property,
                Owner = owner,
                Description = description,
                Detailed = detailed,
                Set = property.SetAccess,
                SetWord = property.Constant ? "none" : null,
                Hidden = property.Hidden,
            };
        }

        /// <summary>The metaclass value of <paramref name="model"/>, its member lists down to <paramref name="depth"/> levels.</summary>
        public JgsValue Render(MetaModel model, int depth)
        {
            if (_rendered.TryGetValue((model.Name, depth), out JgsValue? made))
            {
                return JgsValue.Share(made);
            }

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(model.Name),
                ["Description"] = JgsValue.Str(model.Description),
                ["DetailedDescription"] = JgsValue.Str(model.Detailed),
                ["Hidden"] = JgsValue.Bool(false),
                ["Sealed"] = JgsValue.Bool(model.Sealed),
                ["Abstract"] = JgsValue.Bool(model.Abstract),
                ["Enumeration"] = JgsValue.Bool(model.Enumeration),
                ["ConstructOnLoad"] = JgsValue.Bool(model.ConstructOnLoad),
                ["HandleCompatible"] = JgsValue.Bool(model.HandleCompatible),
            };

            if (depth > 0)
            {
                fields["InferiorClasses"] = ClassList(model.Inferior, depth - 1);
            }

            fields["Namespace"] = model.Namespace is { } package && PackageMeta(interpreter?.FunctionPath, package) is { Type: JgsType.Struct } namespaceValue
                ? Classed(namespaceValue, MetaNamespaceClassName)
                : EmptyMetaArray(MetaNamespaceClassName, MetaNamespaceFieldNames, 0, 0);
            fields["Aliases"] = JgsValue.StringArray([], 0, 1);
            fields["RestrictsSubclassing"] = JgsValue.Bool(model.RestrictsSubclassing);

            if (depth > 0)
            {
                fields["PropertyList"] = MetaColumn([.. model.Properties.Select(p => PropertyFields(p, depth))], MetaPropertyFieldNames, MetaPropertyClassName);
                fields["MethodList"] = MetaColumn([.. model.Methods.Select(m => MethodFields(m, depth))], MetaMethodFieldNames, MetaMethodClassName);
                fields["EventList"] = MetaColumn([.. model.Events.Select(e => EventFields(e, depth))], MetaEventFieldNames, MetaEventClassName);
                fields["EnumerationMemberList"] = MetaColumn([.. model.Members.Select(m => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Name"] = JgsValue.Str(m),
                    ["Description"] = JgsValue.Str(string.Empty),
                    ["DetailedDescription"] = JgsValue.Str(string.Empty),
                    ["Hidden"] = JgsValue.Bool(false),
                    ["DefiningClass"] = Definer(model.Name, depth),
                })], MetaEnumerationFieldNames, MetaEnumerationMemberClassName);
                fields["SuperclassList"] = ClassList(model.Superclasses, depth - 1);
            }

            JgsValue value = JgsValue.Struct(fields);
            value.SetClassName(ClassValueName);
            _rendered[(model.Name, depth)] = value;
            return JgsValue.Share(value);
        }

        /// <summary>One metaproperty as a value, as <c>findprop</c> answers it.</summary>
        public JgsValue RenderProperty(MetaPropertyModel property, int depth)
        {
            JgsValue value = JgsValue.Struct(PropertyFields(property, depth));
            value.SetClassName(MetaPropertyClassName);
            return value;
        }

        private static JgsValue Classed(JgsValue value, string className)
        {
            value.SetClassName(className);
            return value;
        }

        /// <summary>A column of the metaclasses of <paramref name="names"/>.</summary>
        private JgsValue ClassList(IReadOnlyList<string> names, int depth)
        {
            var elements = new List<Dictionary<string, JgsValue>>();
            string[] fields = [];
            foreach (string name in names)
            {
                JgsValue rendered = Render(Model(name), depth);
                fields = rendered.AsStructArray.FieldNames;
                var element = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach ((string field, JgsValue held) in rendered.AsStruct)
                {
                    element[field] = JgsValue.Share(held); // the element is one more holder of each field (M2)
                }

                elements.Add(element);
            }

            return MetaColumn(elements, elements.Count == 0 ? (depth > 0 ? MetaClassFieldNames : []) : fields, ClassValueName);
        }

        /// <summary>A member's <c>DefiningClass</c>: the defining class's metaclass one level down.</summary>
        private JgsValue Definer(string name, int depth) => Render(Model(name), depth - 1);

        /// <summary>An access as R2025b writes it: a word, or a row cell of the classes a list names.</summary>
        private JgsValue Access(MemberAccess access, int depth)
        {
            if (access.Kind != MemberAccessKind.List)
            {
                return JgsValue.Str(access.Kind.ToString().ToLowerInvariant());
            }

            JgsValue[] classes = [.. (access.Classes ?? []).Select(c => Render(Model(c), Math.Max(depth - 1, 0)))];
            JgsValue row = JgsValue.Cell(classes);
            row.Reshape(1, classes.Length);
            return row;
        }

        private Dictionary<string, JgsValue> MethodFields(MetaMethodModel method, int depth) => new(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(method.Name),
            ["Description"] = JgsValue.Str(method.Description),
            ["DetailedDescription"] = JgsValue.Str(method.Detailed),
            ["Access"] = Access(method.Access, depth),
            ["Static"] = JgsValue.Bool(method.Static),
            ["Abstract"] = JgsValue.Bool(method.Abstract),
            ["Sealed"] = JgsValue.Bool(method.Sealed),
            ["Hidden"] = JgsValue.Bool(method.Hidden),
            ["InputNames"] = TextColumn(method.Inputs),
            ["OutputNames"] = TextColumn(method.Outputs),
            ["DefiningClass"] = Definer(method.Definer, depth),
        };

        private Dictionary<string, JgsValue> EventFields(MetaEventModel declared, int depth) => new(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(declared.Name),
            ["Description"] = JgsValue.Str(declared.Description),
            ["DetailedDescription"] = JgsValue.Str(string.Empty),
            ["Hidden"] = JgsValue.Bool(declared.Hidden),
            ["NotifyAccess"] = Access(declared.Notify, depth),
            ["ListenAccess"] = Access(declared.Listen, depth),
            ["DefiningClass"] = Definer(declared.Definer, depth),
        };

        /// <summary>
        /// A metaproperty's fields (probe p196): a constant's <c>SetAccess</c> is 'none', and a constant and a
        /// dependent property are NonCopyable; <c>DefaultValue</c> is the default evaluated in the declaring
        /// class's file, and 0-by-0 where R2025b raises NoDefaultDefined; <c>GetMethod</c> is the getter's
        /// handle, a 0-by-0 for none.
        /// </summary>
        private Dictionary<string, JgsValue> PropertyFields(MetaPropertyModel model, int depth)
        {
            ClassProperty? property = model.Property;
            bool constant = property?.Constant ?? false;
            bool dependent = property?.Dependent ?? false;
            bool isAbstract = property?.Abstract ?? false;
            bool hasDefault = property is { Spec.Default: not null } && !dependent && !isAbstract;
            JgsClass? owner = model.Owner;
            return new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(model.Name),
                ["Description"] = JgsValue.Str(model.Description),
                ["DetailedDescription"] = JgsValue.Str(model.Detailed),
                ["GetAccess"] = Access(property?.GetAccess ?? MemberAccess.Public, depth),
                ["SetAccess"] = model.SetWord is { } word ? JgsValue.Str(word) : Access(model.Set, depth),
                ["Dependent"] = JgsValue.Bool(dependent),
                ["Constant"] = JgsValue.Bool(constant),
                ["Abstract"] = JgsValue.Bool(isAbstract),
                ["Transient"] = JgsValue.Bool(property?.Transient ?? false),
                ["Hidden"] = JgsValue.Bool(model.Hidden),
                ["GetObservable"] = JgsValue.Bool(false),
                ["SetObservable"] = JgsValue.Bool(property?.Observable ?? false),
                ["AbortSet"] = JgsValue.Bool(property?.AbortSet ?? false),
                ["NonCopyable"] = JgsValue.Bool((property?.NonCopyable ?? false) || constant || dependent),
                ["WeakHandle"] = JgsValue.Bool(false),
                ["PartialMatchPriority"] = JgsValue.Number(1),
                ["HasDefault"] = JgsValue.Bool(hasDefault),
                ["DefaultValue"] = hasDefault && owner is not null ? DefaultValueOf(owner, property!) : JgsEmpty.Zero(),
                ["GetMethod"] = owner is not null && property is not null && owner.TryGetter(property.Spec.Name, out ClassMethod? getter)
                    ? JgsValue.Function(owner.Callable(getter))
                    : JgsEmpty.Zero(),
                ["SetMethod"] = owner is not null && property is not null && owner.TrySetter(property.Spec.Name, out ClassMethod? setter)
                    ? JgsValue.Function(owner.Callable(setter))
                    : JgsEmpty.Zero(),
                ["Validation"] = property is not null && owner is not null ? ValidationOf(property.Spec, owner, depth) : EmptyMetaArray(MetaValidationClassName, MetaValidationFieldNames, 0, 0),
                ["DefiningClass"] = model.Definer.Length == 0 ? EmptyMetaArray(ClassValueName, [], 0, 0) : Definer(model.Definer, depth),
            };
        }

        /// <summary>A property's default as its class evaluates it; a constant's value; a 0-by-0 when it does not evaluate.</summary>
        private static JgsValue DefaultValueOf(JgsClass owner, ClassProperty property)
        {
            try
            {
                if (property.Constant && owner.TryConstant(property.Spec.Name, out JgsValue constant))
                {
                    return JgsValue.Share(constant);
                }

                return owner.DefaultOf(property, property.Spec.Line, 0);
            }
            catch (JgsException)
            {
                return JgsEmpty.Zero();
            }
        }

        /// <summary>
        /// A property's <c>Validation</c> (probe p196b): empty when its line declares no class, size or validator;
        /// else its class's metaclass (a 0-by-0 for none), its size as dimensions - a FixedDimension with its
        /// uint64 <c>Length</c>, an UnrestrictedDimension for <c>:</c>, the array an ArrayDimension when they mix, a
        /// 1-by-0 for no size - and its validators as a row of handles.
        /// </summary>
        private JgsValue ValidationOf(ArgumentSpec spec, JgsClass owner, int depth)
        {
            if (spec.ClassName is null && spec.Dims is null && spec.Validators.Count == 0)
            {
                return EmptyMetaArray(MetaValidationClassName, MetaValidationFieldNames, 0, 0);
            }

            JgsValue classValue = spec.ClassName is { } className
                ? Render(Model(className), Math.Max(depth - 1, 0))
                : EmptyMetaArray(ClassValueName, [], 0, 0);

            JgsValue size;
            if (spec.Dims is not { Count: > 0 } dims)
            {
                size = EmptyMetaArray("matlab.metadata.ArrayDimension", [], 1, 0);
            }
            else
            {
                bool anyFixed = dims.Any(static d => d is not null);
                bool allFixed = dims.All(static d => d is not null);
                Dictionary<string, JgsValue>[] lengths =
                [
                    .. dims.Select(d =>
                    {
                        var entry = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                        if (anyFixed)
                        {
                            entry["Length"] = d is NumberLiteral literal
                                ? JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajorDims([Math.Round(literal.Value)], [1, 1]), JgsNumericClass.UInt64)
                                : JgsEmpty.Zero();
                        }

                        return entry;
                    }),
                ];
                size = JgsValue.StructArray(new JgsStructArray(lengths), 1, lengths.Length);
                size.SetClassName(allFixed ? "matlab.metadata.FixedDimension" : anyFixed ? "matlab.metadata.ArrayDimension" : "matlab.metadata.UnrestrictedDimension");
            }

            var functions = new List<JgsValue>();
            foreach (Expr validator in spec.Validators)
            {
                Expr handle = validator switch
                {
                    VariableExpr named => new FunctionHandleExpr(named.Name) { Line = validator.Line, Column = validator.Column },
                    _ => new AnonymousFnExpr([spec.Name], validator) { Dialect = JgsDialect.Matlab, Line = validator.Line, Column = validator.Column },
                };
                try
                {
                    functions.Add(owner.EvaluateForMeta(handle, $"{owner.Name}.{spec.Name} validator"));
                }
                catch (JgsException)
                {
                    // A validator that names nothing here is left out rather than failing the metaclass.
                }
            }

            JgsValue row = JgsValue.Cell([.. functions]);
            row.Reshape(1, functions.Count);
            JgsValue validation = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Class"] = classValue,
                ["Size"] = size,
                ["ValidatorFunctions"] = row,
            });
            validation.SetClassName(MetaValidationClassName);
            return validation;
        }
    }
}
