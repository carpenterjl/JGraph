using System.Text.Json;
using System.Text.RegularExpressions;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Designer;

/// <summary>What <see cref="DesignTreeWriter.Update"/> did to an app's saved tree.</summary>
/// <param name="Bytes">The MAT-file to write in the old one's place; null when the tree was left as it was.</param>
/// <param name="Kept">The components whose saved objects were kept as they were.</param>
/// <param name="Changed">The components whose saved objects were kept with the changed properties written over them, each with those properties in brackets.</param>
/// <param name="Added">The components made anew from their class's base.</param>
/// <param name="Rebuilt">The components whose change could not be written over their saved object, made anew instead.</param>
/// <param name="Removed">The saved components the design no longer has.</param>
/// <param name="Problems">Each component or property the tree could not hold, one line each, and why a tree was left.</param>
public sealed record DesignTreeUpdate(
    byte[]? Bytes,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Rebuilt,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Problems)
{
    /// <summary>Whether the tree was brought to the design.</summary>
    public bool IsUpdated => Bytes is not null;
}

/// <content>
/// Bringing a saved tree to a design while keeping what App Designer saved (designer plan DS6,
/// ADR 0245; the user's decision of 2026-10-18: keep the old default values).
/// </content>
public static partial class DesignTreeWriter
{
    private const string CodeNameField = "CodeName";
    private const string ComponentCodeField = "ComponentCode";
    private const string ImageField = "ImageRelativePath";
    private const string StoredTarget = "app.ad_CODENAME_ad";

    /// <summary>
    /// Brings the component tree of the App Designer model <paramref name="model"/> (the bytes of an
    /// <c>appModel.mat</c>) to <paramref name="design"/>, keeping everything App Designer saved that
    /// the design did not change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A component is matched to its saved object by code name and class. Its lines as App Designer
    /// stores them (<see cref="DesignCodeWriter.StoredComponentCode"/>) are held against the lines
    /// App Designer stored with the object: when they are the same the object is kept as it is, with
    /// every value only the tree holds (an older release's defaults among them); when they differ,
    /// the object is kept and only the properties whose lines changed are written over it, last, so
    /// that <c>load</c> assigns them after what it held, and a property whose line went is set back
    /// to its default (its mode to <c>'auto'</c>). A change that cannot be written over the object -
    /// a component moved out of a grid, its axes toolbar or colormap set back - makes it anew from
    /// its class's base, as a new component is. A saved component the design no longer has is left
    /// out, and so is whatever refers to it. Children, a context menu and a tab group's selection
    /// follow the design. Component groups and every other variable stay as they were.
    /// </para>
    /// <para>
    /// The tree is left as it was (and <see cref="DesignTreeUpdate.Bytes"/> is null) when the model
    /// is not a level-5 MAT-file with the subsystem this reads - a version 7.3 model is one.
    /// </para>
    /// </remarks>
    public static DesignTreeUpdate Update(byte[] model, AppDesign design, DesignCodeStyle? style = null, string? appFolder = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        var problems = new List<string>();
        if (MatMcosFile.Read(model) is not { } file)
        {
            return Left("The model is not a level-5 MAT-file with a subsystem this can rewrite (a version 7.3 model is not).");
        }

        if (file.Variable(Variable) is not { } components
            || components.References.SelectMany(static r => r.Targets).FirstOrDefault(static t => t?.ClassName == "matlab.ui.Figure") is null)
        {
            return Left("The model holds no component tree.");
        }

        // The saved components, by code name, with what App Designer stored about each.
        var saved = new Dictionary<string, Saved>(StringComparer.Ordinal);
        foreach (McosObject o in file.Objects)
        {
            if (DesignTime(file, o) is { } held && !saved.ContainsKey(held.CodeName))
            {
                saved[held.CodeName] = held;
            }
        }

        var allSaved = new HashSet<McosObject>(saved.Values.Select(static s => s.Object), ReferenceEqualityComparer.Instance);
        var objects = new Dictionary<DesignComponent, McosObject>(ReferenceEqualityComparer.Instance);
        var plans = new Dictionary<DesignComponent, Plan>(ReferenceEqualityComparer.Instance);
        List<string> kept = [], changed = [], added = [], rebuilt = [];
        foreach (DesignComponent component in design.Components)
        {
            bool hasBase = BaseOf(component) is not null;
            if (saved.TryGetValue(component.CodeName, out Saved? held) && held.Object.ClassName == component.Type
                && !objects.ContainsValue(held.Object))
            {
                IReadOnlyList<string> lines = DesignCodeWriter.StoredComponentCode(component, style, appFolder);
                // The same lines in another order (an older release's generator) are no change.
                Plan plan = lines.SequenceEqual(held.Code, StringComparer.Ordinal)
                    ? new Plan(held, lines, null, null)
                    : PlanChange(component, held, lines, style.Theme, appFolder);
                if (plan is { Rebuild: false, Changed.Count: 0, Reset.Count: 0 })
                {
                    plan = plan with { Changed = null, Reset = null };
                }

                if (plan.Changed is not null && !hasBase)
                {
                    problems.Add($"{component.CodeName}: App Designer's tree has no {component.Type} this designer can write; its saved object was kept as it was.");
                    plan = plan with { Changed = null, Reset = null };
                }

                if (plan.Rebuild && hasBase)
                {
                    objects[component] = Build(BaseOf(component)!.Value, component.Type);
                    plans[component] = plan;
                    rebuilt.Add(component.CodeName);
                    continue;
                }

                objects[component] = held.Object;
                plans[component] = plan with { Rebuild = false };
                (plan.Changed is null ? kept : changed).Add(plan.Changed is null ? component.CodeName
                    : $"{component.CodeName} ({string.Join(", ", plan.Changed.Concat(plan.Reset!.Select(static r => r.Name + " set back")))})");
            }
            else if (hasBase)
            {
                objects[component] = Build(BaseOf(component)!.Value, component.Type);
                added.Add(component.CodeName);
            }
            else
            {
                problems.Add($"{component.CodeName}: App Designer's tree has no {component.Type} this designer can write.");
            }
        }

        if (!objects.TryGetValue(design.Figure, out McosObject? figure) || figure.ClassName != "matlab.ui.Figure")
        {
            return Left("The design's figure cannot be written.");
        }

        // A design that is not responsive keeps no responsive container among its saved components:
        // App Designer's own conversion to a standard app takes the flag off (AppDataCopyUtil, DS4b).
        bool unflagged = false;
        if (!design.IsResponsive)
        {
            foreach (Plan plan in plans.Values)
            {
                if (plan.Saved.Fields.RemoveAll(static f => f.Name == "AppTypeProperties") == 0)
                {
                    continue;
                }

                unflagged = true;
                if (!plan.Rebuild && plan.Changed is null)
                {
                    plan.Saved.DesignTime.Set("DynamicValue_", new McosValue.Value(new MatData.Structs(
                        [1, 1], plan.Saved.Fields.Select(static f => f.Name).ToArray(), [plan.Saved.Fields.Select(static f => f.Value).ToArray()])));
                }
            }
        }

        foreach ((DesignComponent component, McosObject o) in objects)
        {
            if (!plans.TryGetValue(component, out Plan? plan) || plan.Rebuild)
            {
                Fill(design, component, o, objects, style, appFolder, problems);
                if (plan is not null)
                {
                    KeepGroup(o, plan.Saved);
                }

                ListRenamed(o, file);
            }
            else if (plan.Changed is not null)
            {
                WriteOver(design, component, o, plan, objects, style, appFolder, problems);
                ListRenamed(o, file);
            }
        }

        var live = new HashSet<McosObject>(objects.Values, ReferenceEqualityComparer.Instance);
        var gone = new HashSet<McosObject>(allSaved.Where(o => !live.Contains(o)), ReferenceEqualityComparer.Instance);

        // A component made anew takes its saved object's place among its container's children.
        var replaced = new Dictionary<McosObject, McosObject>(ReferenceEqualityComparer.Instance);
        foreach ((DesignComponent component, Plan plan) in plans)
        {
            if (plan.Rebuild)
            {
                replaced[plan.Saved.Object] = objects[component];
            }
        }

        bool touched = changed.Count + added.Count + rebuilt.Count + gone.Count > 0 || unflagged;
        foreach ((DesignComponent component, McosObject o) in objects)
        {
            if (plans.TryGetValue(component, out Plan? plan) && !plan.Rebuild)
            {
                touched |= FollowDesign(design, component, o, objects, allSaved, gone, replaced, plans);
            }
        }

        // The variable names the figure itself; a figure made anew takes the old one's place there.
        if (!allSaved.Contains(figure))
        {
            int at = file.Variables.IndexOf(components);
            file.Variables[at] = components with
            {
                References = components.References.Select(r => r with
                {
                    Targets = r.Targets.Select(t => t?.ClassName == "matlab.ui.Figure" ? figure : t).ToArray(),
                }).ToArray(),
            };
        }

        // A tree the design did not change is the model as it was, to the byte.
        List<string> removed = saved.Values.Where(s => gone.Contains(s.Object) && design.Find(s.CodeName) is null).Select(static s => s.CodeName).ToList();
        return new DesignTreeUpdate(touched ? file.Write() : model, kept, changed, added, rebuilt, removed, problems);

        DesignTreeUpdate Left(string why) => new(null, [], [], [], [], [], [why]);
    }

    /// <summary>A saved component: its object, its <c>DesignTimeProperties</c> object and fields, its code name, and the lines App Designer stored for it.</summary>
    private sealed record Saved(McosObject Object, McosObject DesignTime, List<(string Name, MatData Value)> Fields, string CodeName, IReadOnlyList<string> Code);

    /// <summary>
    /// What a save does to a saved component: nothing (no changes), write the changed properties and
    /// set back the ones whose lines went, or make it anew.
    /// </summary>
    private sealed record Plan(Saved Saved, IReadOnlyList<string> Lines, IReadOnlyList<string>? Changed, IReadOnlyList<(string Name, DesignValue Default)>? Reset, bool Rebuild = false);

    /// <summary>The design-time record App Designer keeps on <paramref name="o"/>, or null when it has none.</summary>
    private static Saved? DesignTime(MatMcosFile file, McosObject o)
    {
        foreach (McosObject d in o.Dynamic)
        {
            if (TextOf(d.Get("DynamicName_")) != "DesignTimeProperties"
                || d.Get("DynamicValue_") is not McosValue.Value { Data: MatData.Kept value }
                || MatRaw.Plain(value.Element, file.Keep) is not MatData.Structs { Elements: [var values] } record)
            {
                continue;
            }

            var fields = record.Fields.Zip(values).ToList();
            string? codeName = fields.Find(static f => f.First == CodeNameField).Second is MatData.Chars name ? name.Characters : null;
            MatData? code = fields.Find(static f => f.First == ComponentCodeField).Second;
            if (codeName is null)
            {
                continue;
            }

            List<string> lines = code is MatData.Cells cells
                ? cells.Items.Select(static i => i is MatData.Chars c ? c.Characters : "\u0000").ToList()
                : code is MatData.Chars one ? [one.Characters] : [];
            return new Saved(o, d, fields.Select(static f => (f.First, f.Second)).ToList(), codeName, lines);
        }

        return null;
    }

    private static string? TextOf(McosValue? value) => value switch
    {
        McosValue.Value { Data: MatData.Chars c } => c.Characters,
        McosValue.Value { Data: MatData.Kept k } => MatRaw.Text(k.Element),
        McosValue.Word w => w.Text,
        _ => null,
    };

    /// <summary>
    /// Which properties a changed component writes, from its lines against the saved ones: each
    /// property whose line is new, and each property whose saved line went, set back to its
    /// default. A line this cannot name, or a property that cannot be set back over the old object,
    /// makes the component anew.
    /// </summary>
    private static Plan PlanChange(DesignComponent component, Saved held, IReadOnlyList<string> lines, DesignTheme theme, string? appFolder)
    {
        IReadOnlyList<(string Name, DesignValue Value)> written = DesignCodeWriter.WrittenProperties(component, theme);
        var savedLines = new HashSet<string>(held.Code.Skip(1), StringComparer.Ordinal);
        var newLines = new HashSet<string>(lines.Skip(1), StringComparer.Ordinal);
        var changes = new List<string>();
        for (int i = 0; i < written.Count; i++)
        {
            if (!savedLines.Contains(lines[i + 1]))
            {
                changes.Add(written[i].Name);
            }
        }

        var resets = new List<(string, DesignValue)>();
        foreach (string line in held.Code.Skip(1).Where(l => !newLines.Contains(l)))
        {
            if (PropertyOfLine(component, line) is not { } name)
            {
                return new Plan(held, lines, changes, [], Rebuild: true);
            }

            DesignValue? value = LabelParts.ContainsKey(name) ? new CharValue(string.Empty)
                : name is "Layout.Row" or "Layout.Column" or "ToolbarVisible" or "AD_ColormapString" ? null
                : component.Class?.Property(name)?.DefaultFor(theme);

            // No change, though the generator here leaves the line out: a grid child's Position,
            // which the grid sets and App Designer stored before R2025a, or a line that sets the
            // value the design holds or the default (an older release wrote some defaults out).
            if (written.Any(w => w.Name == name) || resets.Exists(r => r.Item1 == name)
                || (name == "Position" && component.IsInGrid)
                || (component.Get(name) is { } held2 && DesignCodeWriter.StoredPropertyLine(component, name, held2, appFolder) == line)
                || (value is not null && DesignCodeWriter.StoredPropertyLine(component, name, value, appFolder) == line))
            {
                continue;
            }
            if (value is null)
            {
                return new Plan(held, lines, changes, [], Rebuild: true);
            }

            resets.Add((name, value));
        }

        // The creation line holds the parent as a placeholder, so it changes only with how the
        // component is made (a button's style is its class, which the match already holds).
        return new Plan(held, lines, changes, resets);
    }

    /// <summary>The property a stored line sets, by the name the design holds it under; null when the line is not one this knows.</summary>
    private static string? PropertyOfLine(DesignComponent component, string line)
    {
        if (line.StartsWith(StoredTarget + ".Toolbar.Visible", StringComparison.Ordinal))
        {
            return "ToolbarVisible";
        }

        if (StoredAssignment().Match(line) is { Success: true } assignment)
        {
            string name = assignment.Groups["name"].Value;
            return name == "ThemeChangedFcn" && component.Class?.Property("AD_AliasedThemeChangedFcn") is not null
                ? "AD_AliasedThemeChangedFcn"
                : name;
        }

        if (StoredCall().Match(line) is { Success: true } call)
        {
            string function = call.Groups["fn"].Value;
            return function == "colormap" ? "AD_ColormapString"
                : DesignCodeReader.LabelAliases.TryGetValue(function, out string? alias) ? alias
                : null;
        }

        return null;
    }

    [GeneratedRegex(@"^app\.ad_CODENAME_ad\.(?<name>[A-Za-z]\w*(?:\.[A-Za-z]\w*)*)\s*=")]
    private static partial Regex StoredAssignment();

    [GeneratedRegex(@"^(?<fn>\w+)\(app\.ad_CODENAME_ad\s*,")]
    private static partial Regex StoredCall();

    /// <summary>
    /// Writes a changed component's properties over its saved object: each as a new object of its
    /// class would store it, moved last; each property whose line went set back to its default.
    /// </summary>
    private static void WriteOver(
        AppDesign design, DesignComponent component, McosObject o, Plan plan, Dictionary<DesignComponent, McosObject> objects,
        DesignCodeStyle style, string? appFolder, List<string> problems)
    {
        JsonElement recorded = BaseOf(component)!.Value;
        var fresh = Build(recorded, component.Type);
        var setBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        Fill(design, component, fresh, objects, style, appFolder, problems, setBy);
        foreach (string property in plan.Changed!)
        {
            foreach (string name in setBy.GetValueOrDefault(property) ?? [])
            {
                o.SetLast(name, fresh.Get(name)!);
            }

            if (property == "Position" && component.Type == Axes)
            {
                DropAxesPlace(o);
            }
        }

        foreach ((string property, DesignValue value) in plan.Reset!)
        {
            var back = Build(recorded);
            var backBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var why = new List<string>();
            Fill(design, component, back, objects, style, appFolder, why, backBy, [(property, value)]);
            if (why.Count > 0 || !backBy.TryGetValue(property, out List<string>? names))
            {
                problems.Add($"{component.CodeName}.{property}: it could not be set back to its default in App Designer's tree.");
                continue;
            }

            foreach (string name in names)
            {
                o.SetLast(name, back.Get(name)!);
            }

            if (component.Class?.Property(property) is { HasMode: true })
            {
                o.SetLast(property + "Mode", new McosValue.Word("auto"));
            }
        }

        // App Designer's record of the component: the new lines, and the image it shows.
        string? image = DesignCodeWriter.WrittenProperties(component, style.Theme).Select(static w => w.Value)
            .OfType<ImageFileValue>().FirstOrDefault()?.RelativePath;
        List<(string Name, MatData Value)> fields = plan.Saved.Fields.ToList();
        SetField(fields, ComponentCodeField, Column(plan.Lines));
        if (image is not null || ImageClasses.Contains(component.Type) || fields.Exists(static f => f.Name == ImageField))
        {
            SetField(fields, ImageField, MatData.Text(image ?? string.Empty));
        }

        plan.Saved.DesignTime.Set("DynamicValue_", new McosValue.Value(
            new MatData.Structs([1, 1], fields.Select(static f => f.Name).ToArray(), [fields.Select(static f => f.Value).ToArray()])));
    }

    private static void SetField(List<(string Name, MatData Value)> fields, string name, MatData value)
    {
        int at = fields.FindIndex(f => f.Name == name);
        if (at >= 0)
        {
            fields[at] = (name, value);
        }
        else
        {
            fields.Add((name, value));
        }
    }

    /// <summary>
    /// In a file MATLAB saved, every class keeps its values at the time of saving, which <c>load</c>
    /// puts beside what an object stored; an object whose hook stored a public name for its
    /// <c>_I</c> storage lists that storage as renamed, so that the two are not both handed to the
    /// hook (R2025b warns that the name "already exists in the serialization content"). An object
    /// written here that stores a public name whose <c>_I</c> storage the class's values hold lists
    /// it so too.
    /// </summary>
    private static void ListRenamed(McosObject o, MatMcosFile file)
    {
        IReadOnlySet<string> defaults = file.DefaultNames(o.ClassName);
        foreach ((string name, _) in o.Entries)
        {
            string storage = name + "_I";
            if (defaults.Contains(storage) && !o.Renamed.Contains(storage))
            {
                o.Renamed.Add(storage);
            }
        }
    }

    /// <summary>A component made anew in a saved one's place keeps the component group the saved one was in.</summary>
    private static void KeepGroup(McosObject o, Saved saved)
    {
        if (saved.Fields.Find(static f => f.Name == "GroupId").Value is not MatData.Chars { Characters.Length: > 0 } group
            || o.Dynamic.Find(static d => d.Get("DynamicName_") is McosValue.Value { Data: MatData.Chars { Characters: "DesignTimeProperties" } }) is not { } designTime
            || designTime.Get("DynamicValue_") is not McosValue.Value { Data: MatData.Structs { Elements: [var values] } record })
        {
            return;
        }

        int at = Array.IndexOf(record.Fields, "GroupId");
        if (at >= 0)
        {
            MatData[] copy = [.. values];
            copy[at] = group;
            designTime.Set("DynamicValue_", new McosValue.Value(record with { Elements = [copy] }));
        }
    }

    /// <summary>
    /// What a kept object refers to follows the design: its children, its
    /// context menu, a tab group's selection, a tab's size; and nothing it holds names a component
    /// the design no longer has.
    /// </summary>
    private static bool FollowDesign(
        AppDesign design, DesignComponent component, McosObject o, Dictionary<DesignComponent, McosObject> objects,
        HashSet<McosObject> allSaved, HashSet<McosObject> gone, Dictionary<McosObject, McosObject> replaced,
        Dictionary<DesignComponent, Plan> plans)
    {
        bool touched = false;
        foreach ((string name, McosValue value) in o.Entries.ToArray())
        {
            if (name != Children && Targets(value).Any(t => t is not null && gone.Contains(t)))
            {
                o.Remove(name);
                touched = true;
            }
        }

        // Children: those the object held stay in the order it held them (an older release saved
        // some containers' in the order made), a component the design no longer has there leaves,
        // and one new there goes in front, the last made first, where MATLAB puts a new child.
        List<McosObject> stored = o.Get(Children) is { } heldChildren ? Targets(heldChildren).OfType<McosObject>().ToList() : [];
        List<McosObject> held = stored.Select(h => replaced.GetValueOrDefault(h, h)).ToList();
        var wanted = new HashSet<McosObject>(component.Children.Where(objects.ContainsKey).Select(c => objects[c]), ReferenceEqualityComparer.Instance);
        List<McosObject> children = component.Children.Where(objects.ContainsKey).Select(c => objects[c])
            .Where(c => !held.Contains(c)).Reverse()
            .Concat(held.Where(h => wanted.Contains(h) || (!allSaved.Contains(h) && !objects.ContainsValue(h))))
            .ToList();
        if (!children.SequenceEqual(stored, ReferenceEqualityComparer.Instance))
        {
            touched = true;
            o.Set(Children, children.Count > 0
                ? new McosValue.Refs(children)
                : new McosValue.Value(new MatData.Numbers("double", [0, 0], [])));
        }

        McosObject? menu = component.Get("ContextMenu") is ComponentsValue { CodeNames: [string menuName] }
            && design.Find(menuName) is { } contextMenu && objects.TryGetValue(contextMenu, out McosObject? menuObject)
            ? menuObject
            : null;
        string[] menuEntries = o.Entries
            .Where(e => e.Name != Children && Targets(e.Value).Any(static t => t?.ClassName == "matlab.ui.container.ContextMenu"))
            .Select(static e => e.Name).ToArray();
        if ((menu is null && menuEntries.Length > 0) || (menu is not null && !menuEntries.Any(n => Targets(o.Get(n)!).Contains(menu))))
        {
            touched = true;
            foreach (string entry in menuEntries)
            {
                o.Remove(entry);
            }

            if (menu is not null)
            {
                o.SetLast("ContextMenu", new McosValue.Refs([menu]));
            }
        }

        if (component.Type == "matlab.ui.container.TabGroup" && component.Children.FirstOrDefault(objects.ContainsKey) is { } firstTab)
        {
            var tabs = new HashSet<McosObject>(component.Children.Where(objects.ContainsKey).Select(c => objects[c]), ReferenceEqualityComparer.Instance);
            if (!o.Entries.Any(e => e.Name != Children && Targets(e.Value).Any(t => t is not null && tabs.Contains(t))))
            {
                touched = true;
                o.SetLast("SelectedTab", new McosValue.Refs([objects[firstTab]]));
            }
        }

        if (component.Type == "matlab.ui.container.Tab" && component.Parent is { } group
            && plans.TryGetValue(group, out Plan? groupPlan) && groupPlan.Changed?.Contains("Position") == true)
        {
            touched = true;
            TabSize(component, o);
        }

        return touched;
    }

    /// <summary>The objects a stored value refers to.</summary>
    private static IEnumerable<McosObject?> Targets(McosValue value) => value switch
    {
        McosValue.Refs refs => refs.Objects,
        McosValue.Value { Data: MatData.Kept kept } => kept.References.SelectMany(static r => r.Targets),
        _ => [],
    };
}
