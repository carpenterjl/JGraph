using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.ui.componentcontainer.ComponentContainer</c>, the class a custom component is written
/// under (app-building plan, U10, ADR 0209), declared here as <c>AppBase</c> is. It is abstract,
/// with the abstract protected <c>setup</c> and <c>update</c>; its 22 public properties are R2025b's
/// (research C's metaclass dump, in the order <c>properties</c> lists them, probe <c>u10_matrix</c>),
/// each Dependent and read and written through the area the object stands for in its figure. Its
/// constructor takes R2025b's arguments - an optional parent, then name-value pairs or a struct -
/// and its <c>delete</c> takes the area out of the figure.
/// </summary>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The class a custom component inherits from.</summary>
    public const string ComponentContainer = "matlab.ui.componentcontainer.ComponentContainer";

    /// <summary>The classes a custom component also <c>isa</c> (R2025b, probe <c>u10_matrix</c>, and the metaclass's superclasses).</summary>
    public static readonly string[] ComponentContainerAncestors =
    [
        "matlab.ui.container.internal.ComponentContainerProxy",
        "matlab.graphics.chartcontainer.mixin.internal.GeneratedCallbackSaveLoadMixin",
        "matlab.graphics.mixin.CustomThemeable",
        "matlab.graphics.Graphics",
        "matlab.ui.control.Component",
    ];

    /// <summary>The public properties, in the order R2025b lists them after a subclass's own.</summary>
    public static readonly string[] ComponentContainerProperties =
    [
        "Visible", "BackgroundColor", "SizeChangedFcn", "Children", "Parent", "HandleVisibility", "ButtonDownFcn", "ContextMenu",
        "BusyAction", "BeingDeleted", "Interruptible", "CreateFcn", "DeleteFcn", "Type", "Tag", "UserData", "Clipping", "Units",
        "Position", "InnerPosition", "OuterPosition", "Layout",
    ];

    /// <summary>The ones only the class may write (the metaclass's <c>SetAccess</c>).</summary>
    private static readonly HashSet<string> ComponentContainerReadOnly = new(StringComparer.Ordinal) { "Type", "BeingDeleted", "InnerPosition" };

    private static ClassdefStmt ComponentContainerDeclaration(Interpreter interpreter)
    {
        var properties = new List<ClassProperty>(ComponentContainerProperties.Length);
        var methods = new List<ClassMethod>();
        foreach (string name in ComponentContainerProperties)
        {
            properties.Add(new ClassProperty(new ArgumentSpec(name, null, null, [], null), Constant: false, Dependent: true)
            {
                SetAccess = ComponentContainerReadOnly.Contains(name) ? Protected : MemberAccess.Public,
            });
            string captured = name;
            methods.Add(Accessor("get." + name, ["obj"], (args, line, col) => GetContainerProperty(captured, args, line, col)));
            methods.Add(Accessor("set." + name, ["obj", "value"], (args, line, col) => SetContainerProperty(captured, args, line, col)));
        }

        methods.Add(new ClassMethod(new FnStmt("setup", ["comp"], [], []) { Dialect = JgsDialect.Matlab }, Static: false) { Access = Protected, Abstract = true });
        methods.Add(new ClassMethod(new FnStmt("update", ["comp"], [], []) { Dialect = JgsDialect.Matlab }, Static: false) { Access = Protected, Abstract = true });
        methods.Add(new ClassMethod(new FnStmt(ComponentContainer, ["varargin"], [], ["obj"]) { Dialect = JgsDialect.Matlab }, Static: false)
        {
            Native = new BuiltinFunction(ComponentContainer, (args, line, col) => ConstructComponentContainer(interpreter, args, line, col))
            {
                KeepsStringArguments = true,
            },
        });
        methods.Add(Method("delete", MemberAccess.Public, isSealed: false, (args, _, _) => DeleteComponentContainer(interpreter, args), bindsAns: false));

        return new ClassdefStmt(ComponentContainer, isHandle: true, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = ["handle"],
            Abstract = true,
        };
    }

    private static ClassMethod Accessor(string name, string[] parameters, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
        new(new FnStmt(name, parameters, [], name.StartsWith("get.", StringComparison.Ordinal) ? ["value"] : ["obj"]) { Dialect = JgsDialect.Matlab }, Static: false)
        {
            Native = new BuiltinFunction(name, body) { KeepsStringArguments = true },
        };

    /// <summary>
    /// The <c>get</c> or <c>set</c> method of a <c>HasCallbackProperty</c> event's <c>NameFcn</c>
    /// (U10): the callback, held beside the object, taken in the forms a graphics callback property
    /// takes and refused in its words; empty is <c>''</c>. A write owes no <c>update</c> (measured).
    /// </summary>
    public static ClassMethod CallbackAccessor(JgsClass owner, string name, string eventName, bool write)
    {
        string property = name[4..];
        ClassMethod method = write
            ? Accessor(name, ["obj", "value"], (args, line, col) =>
            {
                JgsComponentContainerState state = StateOf(args, line, col);
                JgsValue? stored = JgsGraphicsCallbackValues.Normalize(state.Model, property, args[1], line, col);
                if (stored is null)
                {
                    state.Callbacks.Remove(eventName);
                }
                else
                {
                    JgsLifetime.Pin(stored); // the component holds its callback for as long as it lives
                    state.Callbacks[eventName] = stored;
                }

                return args[0];
            })
            : Accessor(name, ["obj"], (args, line, col) =>
                StateOf(args, line, col).Callbacks.TryGetValue(eventName, out JgsValue? held) ? held : JgsValue.Str(string.Empty));
        method.Owner = owner;
        return method;
    }

    private static JgsComponentContainerState StateOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        return JgsComponentContainers.TryState(args[0].AsObject, out JgsComponentContainerState? state)
            ? state
            : throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle",
                $"The {args[0].AsObject.Class.Name} has no area in a figure yet: its ComponentContainer constructor has not run.");
    }

    /// <summary>A property read through the area: <c>Type</c> is the class's name in lower case, and <c>Children</c> is always empty (measured).</summary>
    private static JgsValue GetContainerProperty(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsComponentContainerState state = StateOf(args, line, col);
        return name switch
        {
            "Type" => JgsValue.Str(state.Model.TypeName),
            "Children" => JgsMatrix.FromColumnMajor([], 0, 0),
            _ => JgsGraphicsProperties.Get(state.Entry, name, line, col),
        };
    }

    /// <summary>A property written through the area, after which <c>update</c> is owed. A write to <c>Children</c> is taken and does nothing (measured).</summary>
    private static JgsValue SetContainerProperty(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsComponentContainerState state = StateOf(args, line, col);
        if (name != "Children")
        {
            JgsGraphicsProperties.Set(state.Entry, name, args[1], line, col);
        }

        JgsComponentContainers.MarkOwed(state.Owner);
        return args[0];
    }

    /// <summary>
    /// The constructor (R2025b, probes <c>u10_matrix</c>, <c>u10_more</c>): the parent - the first
    /// argument when it is a graphics object, or a <c>'Parent'</c> pair, or else a new
    /// <c>uifigure</c> - then the area, then the class's <c>setup</c>, then the other pairs, each
    /// name matched without regard to case, then an <c>update</c> owed. An odd count of pairs
    /// refuses before <c>setup</c> and a pair that cannot be set after it, each deleting the
    /// object; a failing <c>setup</c> refuses and leaves the area in its figure.
    /// </summary>
    private static JgsValue ConstructComponentContainer(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsValue self = args[0];
        JgsObject instance = self.AsObject;
        string className = instance.Class.Name;
        var given = args.Skip(1).ToList();

        JgsValue? parentValue = null;
        int start = 0;
        if (given.Count > 0 && !JgsBuiltins.IsTextScalar(given[0]) && given[0].Type != JgsType.Struct && JgsHandleRegistry.TryGet(given[0], out _))
        {
            parentValue = given[0];
            start = 1;
        }

        var pairs = new List<(string? Name, JgsValue Value)>();
        if (given.Count - start == 1 && given[start].Type == JgsType.Struct && !given[start].IsStructArray)
        {
            foreach ((string name, JgsValue value) in given[start].AsStruct)
            {
                pairs.Add((name, value));
            }
        }
        else
        {
            if ((given.Count - start) % 2 != 0)
            {
                interpreter.RunDestructor(instance);
                throw new JgsRuntimeException(line, col, "MATLAB:ui:componentcontainer:UnmatchedNameValuePairs",
                    "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
            }

            for (int i = start; i < given.Count; i += 2)
            {
                pairs.Add((JgsBuiltins.IsTextScalar(given[i]) ? JgsBuiltins.TextOf(given[i]) : null, given[i + 1]));
            }
        }

        foreach ((string? name, JgsValue value) in pairs)
        {
            if (name is not null && name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                parentValue = value;
            }
        }

        pairs.RemoveAll(static pair => pair.Name is not null && pair.Name.Equals("Parent", StringComparison.OrdinalIgnoreCase));

        // A custom component holds another only while its setup runs.
        if (parentValue is not null && JgsHandleRegistry.TryGet(parentValue, out JgsHandleEntry? named)
            && named.Target is UiComponentContainerModel { InSetup: false } closed)
        {
            interpreter.RunDestructor(instance);
            throw new JgsRuntimeException(line, col, "MATLAB:ui:componentcontainer:invalidParent", $"{closed.ClassName} cannot be a parent of {className}.");
        }

        IUiContainer parent;
        try
        {
            parent = parentValue is null ? JgsBuiltins.NewUiFigureForComponent(line, col) : JgsBuiltins.ContainerForComponent(parentValue, line, col);
        }
        catch (JgsRuntimeException)
        {
            interpreter.RunDestructor(instance);
            throw;
        }

        var model = new UiComponentContainerModel(className);
        JgsBuiltins.AddComponentArea(model, parent, line, col);
        var state = new JgsComponentContainerState
        {
            Self = self,
            Model = model,
            Entry = JgsHandleRegistry.EntryFor(model),
            Interpreter = interpreter,
        };
        JgsComponentContainers.Join(state);
        JgsLifetime.Pin(self); // the figure holds the component for as long as it has it
        if (interpreter.Host is { } host)
        {
            JgsBuiltins.AddGraphicsDestroyedHook(state.Entry, host, () => interpreter.RunDestructor(instance));
        }

        if (instance.Class.TryMethod("setup", out ClassMethod? setup) && !setup.Abstract)
        {
            model.InSetup = true;
            try
            {
                instance.Class.Callable(setup).Call([self], line, col);
            }
            catch (JgsRuntimeException failure)
            {
                // R2025b's words, with the class's own error as the exception's cause.
                const string id = "MATLAB:ui:componentcontainer:ErrorWhileExecutingSetup";
                const string text = "Unable to execute 'setup' method.";
                JgsValue cause = failure.Carried as JgsValue ?? JgsBuiltins.MakeException(failure.Identifier ?? string.Empty, failure.Message);
                JgsValue thrown = JgsBuiltins.MakeException(id, text);
                thrown.WritableStruct()["cause"] = JgsValue.Cell([JgsValue.Share(cause)]);
                throw new JgsRuntimeException(line, col, id, text) { Carried = thrown };
            }
            finally
            {
                model.InSetup = false;
            }
        }

        foreach ((string? name, JgsValue value) in pairs)
        {
            ClassProperty? property = name is null ? null
                : instance.Class.ListedProperties.FirstOrDefault(p => p.Spec.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property is { Spec.Name: "Type" })
            {
                continue; // taken, and nothing done (measured)
            }

            bool written = false;
            if (property is { Constant: false } && property.SetAccess.IsPublic)
            {
                try
                {
                    interpreter.WriteProperty(self, property.Spec.Name, value, line, col);
                    written = true;
                }
                catch (JgsRuntimeException)
                {
                }
            }

            if (!written)
            {
                interpreter.RunDestructor(instance);
                throw new JgsRuntimeException(line, col, "MATLAB:ui:componentcontainer:ErrorWhileSettingNameValuePairs", "Unable to set name-value arguments.");
            }
        }

        JgsComponentContainers.MarkOwed(instance);
        return self;
    }

    /// <summary>The class's <c>delete</c>, run after a subclass's own: the area leaves its figure.</summary>
    private static JgsValue DeleteComponentContainer(Interpreter interpreter, IReadOnlyList<JgsValue> args)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Object && JgsComponentContainers.TryState(args[0].AsObject, out JgsComponentContainerState? state)
            && !state.Model.BeingDeleted && interpreter.Host is { } host)
        {
            JgsBuiltins.TryDeleteGraphics(JgsHandleRegistry.For(state.Model), host);
        }

        return JgsValue.Null;
    }
}
