using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// A function of an app's callbacks block (designer plan DS5): a component callback, the startup
/// function, or the layout callback App Designer writes for a responsive app. Its body is the
/// user's, kept line for line as the text holds it, indentation and all, as App Designer's
/// <c>CallbackModel</c> keeps its <c>Code</c>; everything around the body - the comment, the
/// signature, the <c>end</c> - is written again from the design on every render.
/// </summary>
public sealed class DesignCallback
{
    /// <summary>
    /// A callback named <paramref name="name"/> whose body is <paramref name="body"/>; a body of no
    /// lines is one empty line, which is how App Designer shows a callback loaded with no code.
    /// </summary>
    public DesignCallback(string name, IEnumerable<string>? body = null, bool isGenerated = false, bool isStartup = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Body = body?.ToList() ?? [];
        if (Body.Count == 0)
        {
            Body.Add(string.Empty);
        }

        IsGenerated = isGenerated;
        IsStartup = isStartup;
        DefaultBody = [string.Empty];
        GeneratedName = name;
    }

    /// <summary>The function's name.</summary>
    public string Name { get; set; }

    /// <summary>
    /// The name App Designer gave the callback (<c>CallbackModel.GeneratedName</c>): the name it was
    /// made or read with, following a rename of its component but not one the user makes. A callback
    /// is renamed with its component only while its name is still this one.
    /// </summary>
    public string GeneratedName { get; set; }

    /// <summary>
    /// The callback's bindings in the order App Designer's <c>CallbackBindingCollection</c> holds them:
    /// as its reader finds them on load, then as they are made. The first decides whose comment a
    /// callback bound to one kind of property takes, and gives a callback made with no code its
    /// default code. A binding gone from the design no longer counts; one the list does not hold yet
    /// comes after those it does.
    /// </summary>
    public List<DesignCallbackBinding> Bindings { get; } = [];

    /// <summary>The lines between the signature and the <c>end</c>, as the text holds them.</summary>
    public List<string> Body { get; }

    /// <summary>
    /// Whether this is the app's startup function, which runs after the components are made with
    /// the app's input parameters: it keeps its place in the block, as App Designer keeps the order
    /// it finds in the file, and a new one goes first.
    /// </summary>
    public bool IsStartup { get; }

    /// <summary>
    /// Whether App Designer writes the function itself (a responsive app's <c>updateAppLayout</c>): its
    /// body is not the user's, and it is kept last in the block.
    /// </summary>
    public bool IsGenerated { get; }

    /// <summary>
    /// The code the callback was made with (<c>CallbackModel.DefaultCode</c>), unindented: one empty
    /// line for most, a line or two for a few callback properties. A callback read from a file takes
    /// its first binding's default once the design is joined to the code, as App Designer's does when
    /// its loader adds the binding (<c>_onGenerateDefaultCode</c>); its body is not changed by it.
    /// </summary>
    public IReadOnlyList<string> DefaultBody { get; set; }

    /// <summary>
    /// Whether a binding was added to or taken from the callback since it was read or made: App
    /// Designer generates the comment again then, and a callback left with no binding reads "not
    /// associated with a component" where one read with none reads "Callback function" alone.
    /// </summary>
    public bool BindingsChanged { get; set; }

    /// <summary>
    /// Whether the body is still what the callback was made with, or blank
    /// (<c>CallbackModel.isDefaultOrEmptyCode</c>): App Designer then takes the startup function
    /// away with its parameters, and asks nothing before deleting a callback.
    /// </summary>
    public bool IsDefaultOrEmpty
    {
        get
        {
            string[] code = Body.Select(static l => l.Trim()).Where(static l => l.Length > 0).ToArray();
            if (code.Length == 0)
            {
                return true;
            }

            string[] defaults = DefaultBody.Select(static l => l.Trim()).Where(static l => l.Length > 0).ToArray();
            return code.SequenceEqual(defaults, StringComparer.Ordinal);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// A generated properties block after the component properties, as a class text had it, and how
/// many empty lines App Designer left before it (one, but a Simulink app's blocks have two).
/// </summary>
public sealed record DesignGeneratedBlock(IReadOnlyList<string> Lines, int BlankLinesBefore);

/// <summary>
/// An app's code around its components (designer plan DS5): what App Designer keeps in its code model
/// and the designer must carry through a render of the class - the class name, the help comment
/// under <c>classdef</c>, the editable section, each callback and the startup function with their
/// bodies, the app's input parameters and whether it runs as a single instance. Read from a class
/// text by <see cref="JgsMlappLayout"/>'s cut, and written back around the components by
/// <see cref="DesignClassWriter"/>.
/// </summary>
public sealed partial class DesignCode
{
    /// <summary>The startup function's default name (<c>InspectableAppNodeModel.getDefaultCallbackName</c>).</summary>
    public const string StartupName = "startupFcn";

    /// <summary>A code model for a class <paramref name="className"/> with nothing in it but its components.</summary>
    public DesignCode(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ClassName = className;
    }

    /// <summary>The class's name, which is the file's.</summary>
    public string ClassName { get; set; }

    /// <summary>
    /// The comment lines between <c>classdef</c> and the component properties block, as the text
    /// holds them: App Designer writes the app's summary and description there from its App
    /// Details, which the designer does not edit, so they are kept as they are.
    /// </summary>
    public List<string> HeadComments { get; } = [];

    /// <summary>
    /// The component properties block as the text had it, or null for a new app. It is written
    /// again only when it no longer declares the design's components (<see cref="DesignCodeWriter.Splice"/>'s
    /// rule): older files hold an older release's order, which a save of an unchanged app keeps.
    /// </summary>
    public IReadOnlyList<string>? PropertiesBlockAsRead { get; set; }

    /// <summary>
    /// Any further generated properties blocks after the component properties - a responsive
    /// app's widths, a Simulink app's model properties - each as the text had it, with the empty
    /// lines before it. A responsive template's widths block is written again from the design;
    /// any other is kept.
    /// </summary>
    public List<DesignGeneratedBlock> OtherPropertiesBlocks { get; } = [];

    /// <summary>The editable section's lines, without the one empty line App Designer puts on each side.</summary>
    public List<string> EditableSection { get; } = [];

    /// <summary>The startup function; null when the app has none.</summary>
    public DesignCallback? Startup => Callbacks.FirstOrDefault(static c => c.IsStartup);

    /// <summary>
    /// The startup function a plain-text app's run configuration names when no function of the
    /// callbacks block is it (the function stands in the user's code, designer plan DS7a): the name
    /// is kept and written again, since the app runs it from there. Null otherwise.
    /// </summary>
    public string? UnplacedStartup { get; set; }

    /// <summary>
    /// The app's input parameters as the startup function's parameters after <c>app</c>, written
    /// <c>a, b</c> (App Designer's <c>InputParameters</c>); empty when it takes none.
    /// </summary>
    public string InputParameters { get; set; } = string.Empty;

    /// <summary>
    /// Every function of the callbacks block in its order: the component callbacks, the startup
    /// function where it stands among them, a generated one last.
    /// </summary>
    public List<DesignCallback> Callbacks { get; } = [];

    /// <summary>Whether the app allows one running instance (App Designer's <c>SingletonMode</c> FOCUS).</summary>
    public bool SingleInstance { get; set; }

    /// <summary>The input parameters' names, in order.</summary>
    public IReadOnlyList<string> InputParameterNames =>
        InputParameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The function named <paramref name="name"/>, the startup function included, or null.</summary>
    public DesignCallback? Find(string name) => Callbacks.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Reads the code model of a class <paramref name="text"/> laid out as App Designer lays out an
    /// app; null when it is not, or a function of its callbacks block is left open, so that the
    /// user's code cannot be told from the generated code.
    /// </summary>
    public static DesignCode? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        JgsMlappLayout layout = JgsMlappLayout.Of(text);
        if (!layout.IsAppLayout || !layout.IsComplete || layout.ClassName.Length == 0)
        {
            return null;
        }

        var code = new DesignCode(layout.ClassName)
        {
            InputParameters = layout.InputParameters,
            SingleInstance = layout.SingleInstance,
            UnplacedStartup = layout.UnplacedStartup,
        };

        IReadOnlyList<string> lines = layout.Lines;
        int classLine = Index(lines, 0, static l => ClassLine().IsMatch(l));
        int properties = Index(lines, classLine + 1, static l => l.Trim() == DesignCodeReader.ComponentPropertiesComment);
        if (classLine >= 0 && properties > classLine)
        {
            code.HeadComments.AddRange(lines.Skip(classLine + 1).Take(properties - classLine - 1).Where(static l => l.TrimStart().StartsWith('%')));
        }

        // The component properties block, and the generated blocks after it, each to its end.
        int head = properties;
        int blanks = 0;
        bool first = true;
        while (head >= 0 && head + 1 < lines.Count && lines[head + 1].Trim().StartsWith("properties", StringComparison.Ordinal))
        {
            int end = Index(lines, head + 2, static l => l.Trim() == "end");
            if (end < 0)
            {
                break;
            }

            IReadOnlyList<string> block = [.. lines.Skip(head).Take(end - head + 1)];
            if (first)
            {
                code.PropertiesBlockAsRead = block;
                first = false;
            }
            else
            {
                code.OtherPropertiesBlocks.Add(new DesignGeneratedBlock(block, blanks));
            }

            int next = end + 1;
            while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next]))
            {
                next++;
            }

            blanks = next - end - 1;
            head = next < lines.Count && GeneratedPropertiesComment().IsMatch(lines[next]) ? next : -1;
        }

        code.EditableSection.AddRange(layout.EditableSectionCode);
        foreach (MlappRegion region in layout.Regions.Where(static r => r.Kind != MlappRegionKind.EditableSection))
        {
            bool startup = region.Kind == MlappRegionKind.Startup && code.Startup is null;
            code.Callbacks.Add(new DesignCallback(region.Name, layout.CodeOf(region), region.Kind == MlappRegionKind.GeneratedCallback, startup));
        }

        return code;
    }

    private static int Index(IReadOnlyList<string> lines, int from, Func<string, bool> test)
    {
        for (int i = Math.Max(0, from); i < lines.Count; i++)
        {
            if (test(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"^\s*classdef\s")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^\s*%.*[Pp]roperties that correspond to ")]
    private static partial Regex GeneratedPropertiesComment();
}
