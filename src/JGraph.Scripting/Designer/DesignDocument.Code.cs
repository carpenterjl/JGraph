using System.Text.RegularExpressions;
using JGraph.Core.Primitives;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// The app's code around its components (designer plan DS5): the class as the design and the user's
/// code now stand, which the code view shows and a save writes; the callbacks, added, bound, renamed
/// and deleted as App Designer's commands do it; the startup function and the app's input
/// parameters; the single-instance setting; and the user's code taken back from the code view.
/// </summary>
public sealed partial class DesignDocument
{
    private string? _renderedText;
    private bool _textDirty;

    /// <summary>
    /// The app's code around its components; null when the class is not laid out as App Designer
    /// lays out an app, or a function of its callbacks block is left open, so that the user's code
    /// cannot be told from the generated code: the design is then view only.
    /// </summary>
    public DesignCode? Code { get; }

    /// <summary>Why the code cannot be worked on, when <see cref="Code"/> is null.</summary>
    public string? CodeProblem => Code is null
        ? "The app's class is not laid out as App Designer lays out an app, or a function in its callbacks block is not closed, so the designer cannot tell the user's code from the code App Designer generates."
        : null;

    /// <summary>
    /// The class as the design and its code now stand: every generated part written again from the
    /// design, the user's code as it is. What the code view shows and a save writes. The text as read
    /// when the code cannot be worked on.
    /// </summary>
    public string CurrentText
    {
        get
        {
            if (Code is null)
            {
                return Text;
            }

            // Rendered once per state: every edit, undo and redo (Settled) and every text taken back clears it.
            return _renderedText ??= DesignClassWriter.Render(Design, Code, Style, SizeOfRegion, _newline, _trailingNewline);
        }
    }

    /// <summary>The functions of the callbacks block in order; empty when the code cannot be worked on.</summary>
    public IReadOnlyList<DesignCallback> Callbacks => Code?.Callbacks ?? [];

    /// <summary>The app's input parameters as written, <c>a, b</c>; empty when it takes none.</summary>
    public string InputParameters => Code?.InputParameters ?? string.Empty;

    /// <summary>Whether the app runs as a single instance.</summary>
    public bool SingleInstance => Code?.SingleInstance ?? false;

    /// <summary>
    /// The callback properties of <paramref name="component"/>, as App Designer's inspector lists
    /// them; the catalog's callback properties for a class the table does not know.
    /// </summary>
    public IReadOnlyList<string> CallbackPropertiesOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        IReadOnlyList<string> listed = DesignCallbacks.R2025b.PropertiesOf(component.Type);
        if (listed.Count > 0 || component.Class is null)
        {
            return listed;
        }

        return component.Class.Properties.Select(static p => DesignCallbacks.PropertyName(p.Name))
            .Where(static n => n.EndsWith("Fcn", StringComparison.Ordinal) || n.EndsWith("Callback", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>The callback properties of <paramref name="component"/> in their groups, as App Designer's inspector lists them (<see cref="CallbackPropertiesOf"/>).</summary>
    public IReadOnlyList<DesignCallbackGroup> CallbackGroupsOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        IReadOnlyList<DesignCallbackGroup> groups = DesignCallbacks.R2025b.GroupsOf(component.Type);
        if (groups.Count > 0)
        {
            return groups;
        }

        IReadOnlyList<string> properties = CallbackPropertiesOf(component);
        return properties.Count == 0 ? [] : [new DesignCallbackGroup(null, properties)];
    }

    /// <summary>The callback bound to <paramref name="property"/> of <paramref name="component"/>, or null.</summary>
    public string? CallbackOf(DesignComponent component, string property)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        return component.Get(DesignCallbacks.DesignPropertyName(component, property)) is CallbackValue bound ? bound.Method : null;
    }

    /// <summary>The name App Designer offers a new callback for <paramref name="property"/> of <paramref name="component"/>, free among the app's names.</summary>
    public string DefaultCallbackName(DesignComponent component, string property)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        return DesignCallbacks.Unique(DesignCallbacks.DefaultName(component, property), NamesInUse());
    }

    /// <summary>Why <paramref name="name"/> cannot be a function's name - <paramref name="current"/> being the function's own, which is free; null when it can.</summary>
    public string? WhyNotFunctionName(string name, string? current = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return DesignNames.WhyNotName(name, current, NamesInUse());
    }

    /// <summary>
    /// The code model of <paramref name="text"/> joined to <paramref name="design"/>, as App Designer's
    /// loader joins them: each callback's bindings in the order its reader finds them, the first of
    /// which gives a callback its default code (<c>CallbackModel._onGenerateDefaultCode</c>); and a
    /// responsive template's layout callback, which App Designer makes again from the figure's
    /// binding (<c>ResponsiveAppTemplateData</c>, <c>ReadonlyCallbackCodeBehavior</c>): put back last
    /// when the class has lost it, and App Designer's own again when the comment over it no longer
    /// says so. Null when the text is not laid out as an app.
    /// </summary>
    private static DesignCode? ReadCode(string text, AppDesign design)
    {
        if (DesignCode.Read(text) is not { } code)
        {
            return null;
        }

        foreach (DesignCallback callback in code.Callbacks.Where(static c => !c.IsStartup && !c.IsGenerated))
        {
            IReadOnlyList<DesignCallbackBinding> bindings = DesignCallbacks.BindingsAsLoaded(design, callback.Name);
            callback.Bindings.AddRange(bindings);
            if (bindings.Count > 0)
            {
                callback.DefaultBody = DesignCallbacks.R2025b.DefaultBody(bindings[0].Component, bindings[0].Property);
            }
        }

        if (design.IsResponsive && DesignResponsive.IsTemplate(design) && design.Figure.Get("SizeChangedFcn") is CallbackValue bound)
        {
            DesignCallback? layout = code.Find(bound.Method);
            if (layout is null)
            {
                code.Callbacks.Add(new DesignCallback(bound.Method, isGenerated: true));
            }
            else if (!layout.IsGenerated)
            {
                code.Callbacks[code.Callbacks.IndexOf(layout)] = new DesignCallback(layout.Name, layout.Body, isGenerated: true);
            }
        }

        return code;
    }

    /// <summary>
    /// A component moved to another container (<c>ComponentPropertiesBlockBehavior._reOrderProperties</c>):
    /// its declaration and its descendants' are taken out of the component properties block as it was
    /// read and put back after the component before it in the browser's order - each child at the line
    /// after its parent, so a moved container's children come out last first, as App Designer's builder
    /// inserts them - and every name is padded again. A block that no longer declares the design's
    /// components is written again whole anyway, and left alone here.
    /// </summary>
    private void NoteMoved(DesignComponent component)
    {
        if (Code?.PropertiesBlockAsRead is not { } held || !DesignClassWriter.SameDeclarations(held, DesignCodeWriter.PropertiesBlock(Design, Style)))
        {
            return;
        }

        var byName = Design.Components.ToDictionary(static c => c.CodeName, StringComparer.Ordinal);
        List<string> names = held.Skip(2).SkipLast(1).Select(static l => l.Trim().Split(' ')[0]).Where(n => n.Length > 0 && byName.ContainsKey(n)).ToList();
        var moved = new HashSet<string>(Walk(component).Select(static c => c.CodeName), StringComparer.Ordinal);
        names.RemoveAll(moved.Contains);
        List<DesignComponent> order = DesignCodeWriter.ViewOrder(Design).ToList();
        int at = order.FindIndex(c => ReferenceEquals(c, component));
        int previous = at > 0 ? names.IndexOf(order[at - 1].CodeName) : -1;
        Insert(component, previous < 0 ? names.Count : previous + 1);
        Code.PropertiesBlockAsRead = DesignCodeWriter.PropertiesBlock(names.Select(n => byName[n]), Style);

        void Insert(DesignComponent c, int index)
        {
            names.Insert(System.Math.Min(index, names.Count), c.CodeName);
            index++;
            foreach (DesignComponent child in DesignCodeWriter.BrowserOrder(Design, c))
            {
                Insert(child, index);
            }
        }
    }

    /// <summary>
    /// Adds a callback for <paramref name="property"/> of <paramref name="component"/>, named
    /// <paramref name="name"/> - App Designer's default name when null - and binds the property to
    /// it, as one edit (<c>AddCallbackCommand</c>): the function goes last in the block, before a
    /// generated one; its code is what App Designer starts the callback with. A property already
    /// bound is bound to the new callback instead, the old one kept.
    /// </summary>
    /// <returns>Null when added; else why not, and nothing changed.</returns>
    public string? AddCallback(DesignComponent component, string property, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        if (Code is null)
        {
            return CodeProblem;
        }

        if (!CallbackPropertiesOf(component).Contains(property, StringComparer.Ordinal))
        {
            return $"{component.CodeName} has no callback property {property}.";
        }

        name ??= DefaultCallbackName(component, property);
        if (WhyNotFunctionName(name) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        IReadOnlyList<string> defaults = DesignCallbacks.R2025b.DefaultBody(component, property);
        var callback = new DesignCallback(name, defaults.Select(l => Style.Indent(3) + l)) { DefaultBody = defaults };
        string designProperty = DesignCallbacks.DesignPropertyName(component, property);
        Do(
        [
            new CallbackListEdit(Design.Figure, callback, NewCallbackIndex(), Add: true),
            new PropertyEdit(component, designProperty, component.Get(designProperty), new CallbackValue(name), RuntimeBefore: null, RuntimeAfter: null),
        ]);
        return null;
    }

    /// <summary>
    /// Adds the startup function, named <paramref name="name"/> (<c>startupFcn</c> when null), first
    /// in the callbacks block, as App Designer adds the app's <c>StartupFcn</c>.
    /// </summary>
    /// <returns>Null when added; else why not, and nothing changed.</returns>
    public string? AddStartup(string? name = null)
    {
        if (Code is null)
        {
            return CodeProblem;
        }

        if (Code.Startup is { } have)
        {
            return $"The app already has a startup function, {have.Name}.";
        }

        name ??= DesignCallbacks.Unique(DesignCode.StartupName, NamesInUse());
        if (WhyNotFunctionName(name) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        Do([new CallbackListEdit(Design.Figure, new DesignCallback(name, [Style.Indent(3)], isStartup: true), 0, Add: true)]);
        return null;
    }

    /// <summary>
    /// Binds <paramref name="property"/> of <paramref name="component"/> to the callback
    /// <paramref name="name"/>, one the app has (<c>AttachCallbackCommand</c>, <c>ReassignCallbackCommand</c>);
    /// a null name unbinds it (<c>DetachAppCallbackFunctionCommand</c>), the callback kept.
    /// </summary>
    /// <returns>Null when bound; else why not, and nothing changed.</returns>
    public string? BindCallback(DesignComponent component, string property, string? name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(property);
        if (Code is null)
        {
            return CodeProblem;
        }

        if (!CallbackPropertiesOf(component).Contains(property, StringComparer.Ordinal))
        {
            return $"{component.CodeName} has no callback property {property}.";
        }

        DesignCallback? callback = name is null ? null : Code.Find(name);
        if (name is not null && (callback is null || callback.IsStartup || callback.IsGenerated))
        {
            return $"The app has no callback {name} a component can be bound to.";
        }

        string designProperty = DesignCallbacks.DesignPropertyName(component, property);
        DesignValue? before = component.Get(designProperty);
        if ((before as CallbackValue)?.Method == name)
        {
            return null;
        }

        EndGesture(commit: false);
        Do([new PropertyEdit(component, designProperty, before, name is null ? null : new CallbackValue(name), RuntimeBefore: null, RuntimeAfter: null)]);
        return null;
    }

    /// <summary>
    /// Renames <paramref name="callback"/> to <paramref name="name"/> (<c>RenameAppCallbackFunctionCommand</c>):
    /// the properties bound to it follow, and so does the user's code that calls it.
    /// </summary>
    /// <returns>Null when renamed; else why not, and nothing changed.</returns>
    public string? RenameCallback(DesignCallback callback, string name)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(name);
        if (Code is null)
        {
            return CodeProblem;
        }

        if (callback.IsGenerated)
        {
            return $"App Designer writes {callback.Name} itself; it cannot be renamed.";
        }

        if (name == callback.Name)
        {
            return null;
        }

        if (WhyNotFunctionName(name, callback.Name) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        Do([new RenameCallbackEdit(Design.Figure, callback, callback.Name, name)]);
        return null;
    }

    /// <summary>
    /// Deletes <paramref name="callback"/> with its code (<c>DeleteAppCallbackFunctionCommand</c>):
    /// every property bound to it is unbound; the startup function deleted takes the app's input
    /// parameters with it.
    /// </summary>
    /// <returns>Null when deleted; else why not, and nothing changed.</returns>
    public string? DeleteCallback(DesignCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (Code is null)
        {
            return CodeProblem;
        }

        int index = Code.Callbacks.IndexOf(callback);
        if (index < 0)
        {
            return $"The app has no callback {callback.Name}.";
        }

        if (callback.IsGenerated)
        {
            return $"App Designer writes {callback.Name} itself; it cannot be deleted.";
        }

        EndGesture(commit: false);
        var edits = new List<Edit>();
        foreach (DesignCallbackBinding binding in DesignCallbacks.BindingsOf(Design, callback.Name))
        {
            string designProperty = DesignCallbacks.DesignPropertyName(binding.Component, binding.Property);
            edits.Add(new PropertyEdit(binding.Component, designProperty, binding.Component.Get(designProperty), null, RuntimeBefore: null, RuntimeAfter: null) { Selects = false });
        }

        if (callback.IsStartup && Code.InputParameters.Length > 0)
        {
            edits.Add(new ParametersEdit(Design.Figure, Code.InputParameters, string.Empty));
        }

        edits.Add(new CallbackListEdit(Design.Figure, callback, index, Add: false));
        Do([.. edits]);
        return null;
    }

    /// <summary>
    /// Sets the app's input parameters to the names in <paramref name="text"/>, written any way
    /// (<c>InputArgumentsCommand</c>, <c>CodeController.updateAppParams</c>): the constructor takes
    /// <c>varargin</c> and the startup function the names; a startup function is added when the app
    /// has none, and one whose code is still blank is taken away when the parameters are.
    /// </summary>
    /// <returns>Null when set; else why not, and nothing changed.</returns>
    public string? SetInputParameters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Code is null)
        {
            return CodeProblem;
        }

        if (WhyNotInputParameters(text) is { } why)
        {
            return why;
        }

        string parameters = SanitizeInputParameters(text);
        string[] names = Code.InputParameterNames.ToArray();
        names = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parameters == Code.InputParameters)
        {
            return null;
        }

        EndGesture(commit: false);
        var edits = new List<Edit> { new ParametersEdit(Design.Figure, Code.InputParameters, parameters) };
        if (Code.Startup is { } startup)
        {
            if (names.Length == 0 && startup.IsDefaultOrEmpty)
            {
                edits.Add(new CallbackListEdit(Design.Figure, startup, Code.Callbacks.IndexOf(startup), Add: false));
            }
        }
        else if (names.Length > 0)
        {
            string name = DesignCallbacks.Unique(DesignCode.StartupName, NamesInUse());
            edits.Add(new CallbackListEdit(Design.Figure, new DesignCallback(name, [Style.Indent(3)], isStartup: true), 0, Add: true));
        }

        Do([.. edits]);
        return null;
    }

    /// <summary>Input parameters as App Designer's dialog tidies them (<c>CodeEditUtil.sanitizeInputArgumentText</c>): the names, split at spaces and commas, joined by <c>, </c>.</summary>
    public static string SanitizeInputParameters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Join(", ", text.Trim().Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Why <paramref name="text"/> cannot be the app's input parameters (<c>CodeEditUtil.validateInputArguments</c> over the tidied text): a name that is no MATLAB name, or one named twice. Null when it can.</summary>
    public static string? WhyNotInputParameters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] names = SanitizeInputParameters(text).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string name in names)
        {
            if (DesignNames.WhyNotName(name, null, []) is { } why)
            {
                return why;
            }
        }

        return names.Distinct(StringComparer.Ordinal).Count() != names.Length ? "Names must be unique." : null;
    }

    /// <summary>Sets whether the app runs as a single instance (<c>SingletonModeCommand</c>).</summary>
    public void SetSingleInstance(bool on)
    {
        if (Code is null || Code.SingleInstance == on)
        {
            return;
        }

        EndGesture(commit: false);
        Do([new SingleInstanceEdit(Design.Figure, Code.SingleInstance, on)]);
    }

    /// <summary>
    /// Takes the user's code back from <paramref name="text"/>, the class as the code view has it
    /// after an edit: the editable section and each callback's body, matched to the model by the
    /// class's structure, which the view keeps (its generated lines are read-only). Not an edit of
    /// the design, so not undone with one, as App Designer's editor keeps its own undo; the app is
    /// dirty until saved.
    /// </summary>
    /// <returns>False when the text's structure no longer matches the model's, and nothing was taken.</returns>
    public bool ApplyEditedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Code is null || DesignCode.Read(text) is not { } edited || edited.ClassName != Code.ClassName
            || edited.Callbacks.Count != Code.Callbacks.Count
            || !edited.Callbacks.Zip(Code.Callbacks).All(static p => p.First.Name == p.Second.Name && p.First.IsStartup == p.Second.IsStartup && p.First.IsGenerated == p.Second.IsGenerated))
        {
            return false;
        }

        bool changed = false;
        if (!edited.EditableSection.SequenceEqual(Code.EditableSection, StringComparer.Ordinal))
        {
            Code.EditableSection.Clear();
            Code.EditableSection.AddRange(edited.EditableSection);
            changed = true;
        }

        foreach ((DesignCallback from, DesignCallback to) in edited.Callbacks.Zip(Code.Callbacks))
        {
            if (!to.IsGenerated && !from.Body.SequenceEqual(to.Body, StringComparer.Ordinal))
            {
                to.Body.Clear();
                to.Body.AddRange(from.Body);
                changed = true;
            }
        }

        if (changed)
        {
            _textDirty = true;
            _renderedText = null;
            OnChanged();
        }

        return true;
    }

    // --- the editable section (App Designer's Add Property and Add Function) ------------------------

    /// <summary>The names of the properties the user declares in the editable section, in order.</summary>
    public IReadOnlyList<string> UserProperties()
    {
        var names = new List<string>();
        if (Code is null)
        {
            return names;
        }

        List<string> code = Code.EditableSection;
        for (int i = 0; i < code.Count; i++)
        {
            if (!BlockStart().IsMatch(code[i]) || !code[i].TrimStart().StartsWith("properties", StringComparison.Ordinal))
            {
                continue;
            }

            int end = SectionEndIndex(code, i);
            for (int j = i + 1; j < (end < 0 ? code.Count : end); j++)
            {
                string line = RemoveComment(code[j]).Trim();
                if (line.Length > 0 && !line.StartsWith('%') && PropertyName().Match(line) is { Success: true } m)
                {
                    names.Add(m.Groups[1].Value);
                }
            }

            if (end > i)
            {
                i = end;
            }
        }

        return names;
    }

    /// <summary>The user's functions in the editable section: each name and its parameters as written, null for a declaration with no parentheses.</summary>
    public IReadOnlyList<(string Name, string? Parameters)> UserFunctions()
    {
        var functions = new List<(string, string?)>();
        if (Code is null)
        {
            return functions;
        }

        foreach (string line in Code.EditableSection)
        {
            if (UserFunction().Match(line) is { Success: true } m)
            {
                functions.Add((m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value.Trim() : null));
            }
        }

        return functions;
    }

    /// <summary>
    /// Deletes the user's function <paramref name="name"/> from the editable section, its
    /// <c>function</c> line through its <c>end</c> (App Designer's code browser Delete); the block
    /// it was in stays, as do any comment lines above it.
    /// </summary>
    /// <returns>Null when deleted; else why not.</returns>
    public string? DeleteUserFunction(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Code is null)
        {
            return CodeProblem;
        }

        List<string> code = Code.EditableSection;
        int start = code.FindIndex(l => UserFunction().Match(l) is { Success: true } m && m.Groups[1].Value == name);
        if (start < 0)
        {
            return $"The editable section has no function {name}.";
        }

        int end = SectionEndIndex(code, start);
        if (end < 0)
        {
            return $"The function {name} is not closed.";
        }

        var after = new List<string>(code);
        after.RemoveRange(start, end - start + 1);
        EndGesture(commit: false);
        Do([new SectionEdit(Design.Figure, [.. code], after)]);
        return null;
    }

    /// <summary>Deletes the user's property <paramref name="name"/>: its declaration line, comment and all; the block stays.</summary>
    /// <returns>Null when deleted; else why not.</returns>
    public string? DeleteUserProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Code is null)
        {
            return CodeProblem;
        }

        List<string> code = Code.EditableSection;
        int line = DeclarationLine(code, name);
        if (line < 0)
        {
            return $"The editable section declares no property {name}.";
        }

        var after = new List<string>(code);
        after.RemoveAt(line);
        EndGesture(commit: false);
        Do([new SectionEdit(Design.Figure, [.. code], after)]);
        return null;
    }

    /// <summary>
    /// Renames the user's function <paramref name="from"/> to <paramref name="to"/> wherever the
    /// user's code declares, calls or names it (<c>_Behavior._renameUserCode</c> for a method:
    /// <c>app.from</c> and <c>from(app</c>), as App Designer's code browser rename does.
    /// </summary>
    /// <returns>Null when renamed; else why not.</returns>
    public string? RenameUserFunction(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (Code is null)
        {
            return CodeProblem;
        }

        if (!UserFunctions().Any(f => f.Name == from))
        {
            return $"The editable section has no function {from}.";
        }

        if (from == to)
        {
            return null;
        }

        if (WhyNotFunctionName(to, from) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        Do([new UserCodeEdit(Design.Figure, this, CallbackRewrite(from, to))]);
        return null;
    }

    /// <summary>
    /// Renames the user's property <paramref name="from"/> to <paramref name="to"/>
    /// (<c>_Behavior._renameUserCode</c> for a property): every <c>app.from</c> in the user's code, and
    /// the bare word on the line that is its declaration alone. A use of the bare name elsewhere - a
    /// local variable of the same name - is left as it is, as App Designer leaves it.
    /// </summary>
    /// <returns>Null when renamed; else why not.</returns>
    public string? RenameUserProperty(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (Code is null)
        {
            return CodeProblem;
        }

        if (!UserProperties().Contains(from, StringComparer.Ordinal))
        {
            return $"The editable section declares no property {from}.";
        }

        if (from == to)
        {
            return null;
        }

        if (WhyNotFunctionName(to, from) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        Do([new UserCodeEdit(Design.Figure, this, PropertyRewrite(from, to))]);
        return null;
    }

    /// <summary>
    /// The lines of the user's code a rename of <paramref name="component"/> to <paramref name="to"/>
    /// would change - <c>app.Name</c>, and the calls of the callbacks renamed with it - each as it is
    /// and as it would read, one-based in <see cref="CurrentText"/>: what the rename's preview shows.
    /// </summary>
    public IReadOnlyList<(int Line, string Before, string After)> RenamePreview(DesignComponent component, string to)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(to);
        var rewrites = new List<Func<string, string>> { ComponentRewrite(component.CodeName, to) };
        foreach (Edit edit in CallbackRenames(component, component.CodeName, to))
        {
            if (edit is RenameCallbackEdit rename)
            {
                rewrites.Add(CallbackRewrite(rename.From, rename.To));
            }
        }

        return Changes(line => rewrites.Aggregate(line, static (text, rewrite) => rewrite(text)));
    }

    /// <summary>
    /// The lines of the user's code a rename of the callback or function <paramref name="from"/> (or,
    /// when <paramref name="property"/>, the property) to <paramref name="to"/> would change, each
    /// as it is and as it would read, one-based in <see cref="CurrentText"/>.
    /// </summary>
    public IReadOnlyList<(int Line, string Before, string After)> RenamePreview(string from, string to, bool property)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return Changes(property ? PropertyRewrite(from, to) : CallbackRewrite(from, to));
    }

    private IReadOnlyList<(int Line, string Before, string After)> Changes(Func<string, string> rewrite) =>
        UserLines().Select(l => (l.Line, l.Text, rewrite(l.Text))).Where(static c => c.Item2 != c.Item3).ToArray();

    /// <summary><c>app.from</c> renamed on a line (<c>_Behavior._renameUserCode</c> for a component).</summary>
    private static Func<string, string> ComponentRewrite(string from, string to)
    {
        Regex reference = ComponentReference(from);
        return line => reference.Replace(line, m => m.Groups[1].Value + to);
    }

    /// <summary><c>app.from</c> and <c>from(app</c> renamed on a line (<c>_renameUserCode</c> for a callback or method).</summary>
    private static Func<string, string> CallbackRewrite(string from, string to)
    {
        Regex reference = CallbackReference(from);
        return line => reference.Replace(line, m => m.Groups[1].Success ? m.Groups[1].Value + to : to + m.Groups[2].Value);
    }

    /// <summary><c>app.from</c> renamed on a line, and the bare name on the line that is its declaration alone (<c>_renameUserCode</c> for a property).</summary>
    private static Func<string, string> PropertyRewrite(string from, string to)
    {
        Regex reference = ComponentReference(from);
        return line =>
        {
            string renamed = reference.Replace(line, m => m.Groups[1].Value + to);
            string code = RemoveComment(renamed);
            if (code.Trim() == from)
            {
                int at = code.IndexOf(from, StringComparison.Ordinal);
                renamed = renamed.Remove(at, from.Length).Insert(at, to);
            }

            return renamed;
        };
    }

    /// <summary>The index in <paramref name="code"/> of the line declaring the property <paramref name="name"/> inside a <c>properties</c> block, or -1.</summary>
    private static int DeclarationLine(IReadOnlyList<string> code, string name)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (!BlockStart().IsMatch(code[i]) || !code[i].TrimStart().StartsWith("properties", StringComparison.Ordinal))
            {
                continue;
            }

            int end = SectionEndIndex(code, i);
            for (int j = i + 1; j < (end < 0 ? code.Count : end); j++)
            {
                string line = RemoveComment(code[j]).Trim();
                if (line.Length > 0 && !line.StartsWith('%') && PropertyName().Match(line) is { Success: true } m && m.Groups[1].Value == name)
                {
                    return j;
                }
            }

            if (end > i)
            {
                i = end;
            }
        }

        return -1;
    }

    /// <summary>
    /// Adds a property to the editable section as App Designer's Add Property does
    /// (<c>UserEditableBlockBehavior._onAddProperty</c>): <c>Name % Description</c> into the
    /// <c>properties</c> block of that <paramref name="access"/> (<c>private</c> or <c>public</c>),
    /// before its <c>end</c>; a new block when there is none, after the other access's block or at
    /// the top. Named <c>Property</c>, <c>Property2</c>... free among the app's names.
    /// </summary>
    /// <returns>The property's name, or null when nothing was added.</returns>
    public string? InsertUserProperty(string access = "private")
    {
        ArgumentNullException.ThrowIfNull(access);
        if (Code is null)
        {
            return null;
        }

        List<string> code = Code.EditableSection;
        int count = UserProperties().Count + 1;
        string name = DesignCallbacks.Unique(count == 1 ? "Property" : "Property" + count, NamesInUse());
        string declaration = Style.Indent(2) + name + " % Description";
        var after = new List<string>(code);
        int section = SectionIndex(after, "properties", access);
        if (section >= 0)
        {
            int end = SectionEndIndex(after, section);
            if (end < 0)
            {
                return null;
            }

            after.Insert(end, declaration);
        }
        else
        {
            var block = new List<string> { Style.Indent(1), Style.Indent(1) + $"properties (Access = {access})", declaration, Style.Indent(1) + "end" };
            if (code.Count == 0)
            {
                block.Add(Style.Indent(1));
            }

            int at = 0;
            int other = SectionIndex(after, "properties", access == "private" ? "public" : "private");
            if (other >= 0 && SectionEndIndex(after, other) is var otherEnd && otherEnd >= 0)
            {
                at = otherEnd + 1;
            }

            after.InsertRange(at, block);
        }

        EndGesture(commit: false);
        Do([new SectionEdit(Design.Figure, [.. code], after)]);
        return name;
    }

    /// <summary>
    /// Adds a function to the editable section as App Designer's Add Function does
    /// (<c>UserEditableBlockBehavior._onAddMethod</c>): <c>function results = name(app)</c> with an
    /// empty body into the <c>methods</c> block of that <paramref name="access"/>, before its
    /// <c>end</c>; a new block at the end of the section when there is none. Named <c>func</c>,
    /// <c>func2</c>... free among the app's names.
    /// </summary>
    /// <returns>The function's name, or null when nothing was added.</returns>
    public string? InsertUserFunction(string access = "private")
    {
        ArgumentNullException.ThrowIfNull(access);
        if (Code is null)
        {
            return null;
        }

        List<string> code = Code.EditableSection;
        int count = UserFunctions().Count + 1;
        string name = DesignCallbacks.Unique(count == 1 ? "func" : "func" + count, NamesInUse());
        string[] method = [Style.Indent(2), Style.Indent(2) + $"function results = {name}({DesignValueText.AppObjectName})", Style.Indent(3), Style.Indent(2) + "end"];
        var after = new List<string>(code);
        int section = SectionIndex(after, "methods", access);
        if (section >= 0)
        {
            int end = SectionEndIndex(after, section);
            if (end < 0)
            {
                return null;
            }

            after.InsertRange(end, method);
        }
        else
        {
            var block = new List<string> { Style.Indent(1), Style.Indent(1) + $"methods (Access = {access})" };
            block.AddRange(method);
            block.Add(Style.Indent(1) + "end");
            if (code.Count == 0)
            {
                block.Add(Style.Indent(1));
            }

            int at = after.Count > 0 && after[^1].Trim().Length == 0 ? after.Count - 1 : after.Count;
            after.InsertRange(at, block);
        }

        EndGesture(commit: false);
        Do([new SectionEdit(Design.Figure, [.. code], after)]);
        return name;
    }

    /// <summary>
    /// The first line of a <paramref name="kind"/> block (<c>properties</c>, <c>methods</c>) of
    /// <paramref name="access"/> (<c>UserEditableBlockBehavior._getSectionIndex</c>): the line, its
    /// spaces taken out, holds <c>kind(Access=access)</c>; a bare <c>kind</c> line is public. -1 when none.
    /// </summary>
    internal static int SectionIndex(IReadOnlyList<string> code, string kind, string access)
    {
        string target = $"{kind}(Access={access})";
        for (int i = 0; i < code.Count; i++)
        {
            string squeezed = code[i].Replace(" ", string.Empty, StringComparison.Ordinal);
            if ((access == "public" && squeezed == kind) || squeezed.Contains(target, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The line of the <c>end</c> that closes the block opened at <paramref name="start"/>
    /// (<c>UserEditableBlockBehavior._getSectionEndIndex</c>): blocks are counted by their first word,
    /// block comments skipped, comments and strings taken out before an <c>end</c> is looked for. -1 when the block is not closed.
    /// </summary>
    internal static int SectionEndIndex(IReadOnlyList<string> code, int start)
    {
        int depth = 1;
        bool inBlockComment = false;
        for (int i = start + 1; i < code.Count; i++)
        {
            string line = code[i];
            if (inBlockComment)
            {
                if (line.Trim().StartsWith("%}", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }

                continue;
            }

            if (line.Trim().StartsWith("%{", StringComparison.Ordinal))
            {
                inBlockComment = true;
                continue;
            }

            string b = RemoveComment(line);
            if (b.Trim().Length == 0)
            {
                continue;
            }

            string first = b.Trim().Split(' ')[0].Split('(')[0];
            if (BlockKeywords.Contains(first))
            {
                depth++;
                if (HasEndToken(b))
                {
                    depth--;
                }

                continue;
            }

            if (HasEndToken(b) && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly HashSet<string> BlockKeywords = new(StringComparer.Ordinal) { "function", "if", "for", "while", "try", "switch", "arguments", "parfor" };

    /// <summary>A line without its comment (<c>CodeFormatter.removeComment</c>): a comment sign inside a string is not one.</summary>
    private static string RemoveComment(string line)
    {
        if (!line.Contains('%', StringComparison.Ordinal))
        {
            return line;
        }

        if (line.TrimStart().StartsWith('%'))
        {
            return line.Split('%')[0];
        }

        (string text, List<Match> strings) = RemoveStrings(line);
        text = text.Split('%')[0];
        foreach (Match s in Enumerable.Reverse(strings))
        {
            if (s.Index < text.Length)
            {
                text = text.Insert(s.Index, s.Value);
            }
        }

        return text;
    }

    /// <summary>The quoted strings taken out of a line, each remembered with where it was (<c>CodeFormatter.removeUserStrings</c>).</summary>
    private static (string Text, List<Match> Strings) RemoveStrings(string line)
    {
        var strings = new List<Match>();
        string text = line;
        for (Match m = QuotedString().Match(text); m.Success; m = QuotedString().Match(text))
        {
            strings.Add(m);
            text = text.Remove(m.Index, m.Length);
        }

        return (text, strings);
    }

    /// <summary>Whether a line, its strings taken out, ends in <c>end</c> (<c>CodeFormatter.hasEndToken</c>).</summary>
    private static bool HasEndToken(string line) => EndToken().IsMatch(RemoveStrings(line).Text);

    [GeneratedRegex(@"('|"")(.*?)('|"")")]
    private static partial Regex QuotedString();

    [GeneratedRegex(@"( *|\t*)(\bend\b)( *|\t*)(;*)( *|\t*)$")]
    private static partial Regex EndToken();

    [GeneratedRegex(@"^\s*(properties|methods)\b")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^([A-Za-z]\w*)")]
    private static partial Regex PropertyName();

    [GeneratedRegex(@"^\s*function\s+(?:(?:\[[^\]]*\]|\w+)\s*=\s*)?(\w+)\s*(?:\((.*)\))?")]
    private static partial Regex UserFunction();

    /// <summary>
    /// Every line of the user's code rewritten by <paramref name="Rewrite"/> (a rename). Undone line
    /// by line: each line the rewrite changed is put back as it was where it still reads as the
    /// rewrite left it, so that code typed in the code view since stays. (App Designer leaves these to
    /// its editor's own undo; see ADR 0250.)
    /// </summary>
    private sealed record UserCodeEdit(DesignComponent Component, DesignDocument Document, Func<string, string> Rewrite) : CodeEdit(Component)
    {
        private readonly List<(List<string> Lines, int Index, string Before, string After)> _changes = [];

        public override void Do(DesignDocument document)
        {
            _changes.Clear();
            foreach (List<string> lines in document.UserLineLists())
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    string after = Rewrite(lines[i]);
                    if (after != lines[i])
                    {
                        _changes.Add((lines, i, lines[i], after));
                        lines[i] = after;
                    }
                }
            }
        }

        public override void Undo(DesignDocument document)
        {
            foreach ((List<string> lines, int index, string before, string after) in _changes)
            {
                int at = index < lines.Count && lines[index] == after ? index : lines.IndexOf(after);
                if (at >= 0)
                {
                    lines[at] = before;
                }
            }
        }
    }

    /// <summary>
    /// The editable section with a run of lines put in or taken out: App Designer's Add Property and
    /// Add Function, and the code browser's Delete of either. Undone and redone by that run alone -
    /// found where it was put, or wherever it still stands - so that code typed in the code view since
    /// stays; a run the user has changed is left as it is.
    /// </summary>
    private sealed record SectionEdit(DesignComponent Component, IReadOnlyList<string> Before, IReadOnlyList<string> After) : CodeEdit(Component)
    {
        private readonly int _at = CommonPrefix(Before, After);
        private readonly string[] _removed = Middle(Before, CommonPrefix(Before, After), CommonSuffix(Before, After));
        private readonly string[] _added = Middle(After, CommonPrefix(Before, After), CommonSuffix(Before, After));

        public override void Do(DesignDocument document) => Swap(document.Code!.EditableSection, _removed, _added);

        public override void Undo(DesignDocument document) => Swap(document.Code!.EditableSection, _added, _removed);

        private void Swap(List<string> section, string[] gone, string[] put)
        {
            int at = gone.Length == 0 ? System.Math.Min(_at, section.Count) : IndexOfRun(section, gone, _at);
            if (at < 0)
            {
                return;
            }

            section.RemoveRange(at, gone.Length);
            section.InsertRange(at, put);
        }

        /// <summary>Where <paramref name="run"/> stands in <paramref name="lines"/>: at <paramref name="expected"/> when it does, else wherever it first does, else -1.</summary>
        private static int IndexOfRun(List<string> lines, string[] run, int expected)
        {
            if (RunAt(lines, run, expected))
            {
                return expected;
            }

            for (int i = 0; i + run.Length <= lines.Count; i++)
            {
                if (RunAt(lines, run, i))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool RunAt(List<string> lines, string[] run, int at) =>
            at >= 0 && at + run.Length <= lines.Count && run.Select((l, i) => lines[at + i] == l).All(static same => same);

        private static int CommonPrefix(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            int n = 0;
            while (n < a.Count && n < b.Count && a[n] == b[n])
            {
                n++;
            }

            return n;
        }

        private static int CommonSuffix(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            int prefix = CommonPrefix(a, b);
            int n = 0;
            while (n < a.Count - prefix && n < b.Count - prefix && a[a.Count - 1 - n] == b[b.Count - 1 - n])
            {
                n++;
            }

            return n;
        }

        private static string[] Middle(IReadOnlyList<string> lines, int prefix, int suffix) =>
            lines.Skip(prefix).Take(lines.Count - prefix - suffix).ToArray();
    }

    /// <summary>
    /// The lines of the user's code that name the component <paramref name="codeName"/> as
    /// <c>app.Name</c>, one-based in <see cref="CurrentText"/>: what a rename would change.
    /// </summary>
    public IReadOnlyList<(int Line, string Text)> UserCodeReferences(string codeName)
    {
        ArgumentNullException.ThrowIfNull(codeName);
        Regex reference = ComponentReference(codeName);
        return UserLines().Where(l => reference.IsMatch(l.Text)).ToArray();
    }

    /// <summary>The lines of the user's code that call or name the callback <paramref name="name"/>, one-based in <see cref="CurrentText"/>.</summary>
    public IReadOnlyList<(int Line, string Text)> CallbackReferences(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Regex reference = CallbackReference(name);
        return UserLines().Where(l => reference.IsMatch(l.Text)).ToArray();
    }

    /// <summary>The one-based line of <paramref name="callback"/>'s signature in <see cref="CurrentText"/>, or 0.</summary>
    public int LineOf(DesignCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        string[] lines = CurrentText.Split('\n');
        JgsMlappLayout layout = JgsMlappLayout.Of(CurrentText);
        MlappRegion? region = layout.Regions.FirstOrDefault(r => r.Kind != MlappRegionKind.EditableSection && r.Name == callback.Name);
        return region is null || region.FirstLine == 0 ? 0 : region.FirstLine;
    }

    /// <summary>The user's lines of <see cref="CurrentText"/>: the editable section and the callbacks' bodies, numbered from one.</summary>
    private IEnumerable<(int Line, string Text)> UserLines()
    {
        JgsMlappLayout layout = JgsMlappLayout.Of(CurrentText);
        foreach (MlappRegion region in layout.Regions.Where(static r => r.IsUserCode))
        {
            for (int i = region.FirstLine; i < region.FirstLine + region.LineCount; i++)
            {
                yield return (i + 1, layout.Lines[i]);
            }
        }
    }

    /// <summary>Where a new callback goes: last, but before a generated one App Designer keeps last (<c>CallbackCollection._updateAddedCallbackOrder</c>).</summary>
    private int NewCallbackIndex()
    {
        int count = Code!.Callbacks.Count;
        return count > 0 && Code.Callbacks[^1].IsGenerated ? count - 1 : count;
    }

    private Size2D? SizeOfRegion(DesignComponent component) => BoxOf(component) is { } box ? box.Box.Size : null;

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/> wherever the user's code names a component so (<c>_Behavior._renameUserCode</c>).</summary>
    private void RenameComponentInUserCode(string from, string to)
    {
        if (Code is not null)
        {
            ReplaceUserLines(ComponentRewrite(from, to));
        }
    }

    /// <summary>Renames the callback <paramref name="from"/> to <paramref name="to"/> wherever the user's code calls or names it.</summary>
    private void RenameCallbackInUserCode(string from, string to)
    {
        if (Code is not null)
        {
            ReplaceUserLines(CallbackRewrite(from, to));
        }
    }

    private void ReplaceUserLines(Func<string, string> replace)
    {
        foreach (List<string> lines in UserLineLists())
        {
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = replace(lines[i]);
            }
        }
    }

    /// <summary>The lists the user's code is held in: the editable section, then each callback's body but a generated one's.</summary>
    private IEnumerable<List<string>> UserLineLists()
    {
        if (Code is null)
        {
            yield break;
        }

        yield return Code.EditableSection;
        foreach (DesignCallback callback in Code.Callbacks.Where(static c => !c.IsGenerated))
        {
            yield return callback.Body;
        }
    }

    /// <summary><c>app.Name</c>, the dot taken as a dot (App Designer's pattern takes it as any character; see ADR 0250).</summary>
    private static Regex ComponentReference(string codeName) =>
        new(@"(\b" + DesignValueText.AppObjectName + @"\.\s*)" + Regex.Escape(codeName) + @"\b");

    /// <summary><c>app.Name</c> or <c>Name(app</c>, the two forms App Designer renames a function by.</summary>
    private static Regex CallbackReference(string name) =>
        new(@"(\b" + DesignValueText.AppObjectName + @"\.\s*)" + Regex.Escape(name) + @"\b|\b" + Regex.Escape(name) + @"(\s*\(\s*" + DesignValueText.AppObjectName + @"\b)");

    /// <summary>A callback put into or taken out of the block at <paramref name="Index"/>.</summary>
    private sealed record CallbackListEdit(DesignComponent Component, DesignCallback Callback, int Index, bool Add) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => Apply(document, Add);

        public override void Undo(DesignDocument document) => Apply(document, !Add);

        private void Apply(DesignDocument document, bool add)
        {
            List<DesignCallback> callbacks = document.Code!.Callbacks;
            if (add)
            {
                callbacks.Insert(System.Math.Min(Index, callbacks.Count), Callback);
            }
            else
            {
                callbacks.Remove(Callback);
            }
        }
    }

    /// <summary>
    /// A callback renamed: its name, the properties bound to it, and the user's code that calls it.
    /// When <paramref name="Generated"/> (a component's rename), the name App Designer gave it follows;
    /// a rename the user makes leaves that, and the callback no longer follows its component.
    /// </summary>
    private sealed record RenameCallbackEdit(DesignComponent Component, DesignCallback Callback, string From, string To, bool Generated = false) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => Apply(document, From, To);

        public override void Undo(DesignDocument document) => Apply(document, To, From);

        private void Apply(DesignDocument document, string from, string to)
        {
            Callback.Name = to;
            if (Generated)
            {
                Callback.GeneratedName = to;
            }

            foreach (DesignCallbackBinding binding in DesignCallbacks.BindingsOf(document.Design, from))
            {
                binding.Component.Set(DesignCallbacks.DesignPropertyName(binding.Component, binding.Property), new CallbackValue(to));
            }

            document.RenameCallbackInUserCode(from, to);
        }
    }

    /// <summary>The app's input parameters changed.</summary>
    private sealed record ParametersEdit(DesignComponent Component, string Before, string After) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => document.Code!.InputParameters = After;

        public override void Undo(DesignDocument document) => document.Code!.InputParameters = Before;
    }

    /// <summary>The single-instance setting changed.</summary>
    private sealed record SingleInstanceEdit(DesignComponent Component, bool Before, bool After) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => document.Code!.SingleInstance = After;

        public override void Undo(DesignDocument document) => document.Code!.SingleInstance = Before;
    }

    /// <summary>An edit of the code around the components: it selects nothing, and leaves the selection as it is.</summary>
    private abstract record CodeEdit(DesignComponent Component) : Edit(Component)
    {
        public override bool KeepsSelection => true;
    }
}
