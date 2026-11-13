using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>What one of an app's user-written stretches of code is.</summary>
public enum MlappRegionKind
{
    /// <summary>The user's own properties and helper methods, between the component properties and the callbacks.</summary>
    EditableSection,

    /// <summary>The body of the startup function.</summary>
    Startup,

    /// <summary>The body of a component callback.</summary>
    Callback,

    /// <summary>
    /// The body of a callback App Designer writes itself (a responsive app's layout function). It
    /// is listed with the callbacks and is not the user's code.
    /// </summary>
    GeneratedCallback,

    /// <summary>The body of a custom UI component's <c>update</c> method (App Designer window plan W15), which the user writes.</summary>
    Update,
}

/// <summary>
/// One stretch of an App Designer class file that App Designer keeps apart from the code it
/// generates: where it is in the text, by line (zero-based), and the function it is the body of.
/// </summary>
/// <param name="Kind">What the stretch is.</param>
/// <param name="Name">The function whose body it is; empty for the editable section.</param>
/// <param name="FirstLine">The zero-based index of its first line.</param>
/// <param name="LineCount">How many lines it has, which may be none.</param>
public sealed record MlappRegion(MlappRegionKind Kind, string Name, int FirstLine, int LineCount)
{
    /// <summary>Whether the user owns these lines: App Designer keeps them as written when it saves.</summary>
    public bool IsUserCode => Kind != MlappRegionKind.GeneratedCallback;
}

/// <summary>
/// The layout of an App Designer class file (app-building plan U7b, ADR 0205): which lines are the
/// user's and which App Designer generates from its component tree. This is the piece the editor
/// shades by, the save-back derives App Designer's copy of the code from, and a visual designer
/// would regenerate around.
/// </summary>
/// <remarks>
/// <para>
/// App Designer writes a class in a fixed order and marks each part with a comment, and its own
/// record of the user's code (<c>appModel.mat</c>'s <c>code</c>) is those parts cut out of the
/// text. Measured over the 44 apps R2025b ships (probe <c>u7b_shape</c>): the component properties
/// block comes first, under <c>% Properties that correspond to app components</c>; one empty line
/// follows it; the editable section runs from there to the empty line before
/// <c>% Callbacks that handle component events</c> (or before <c>% Component initialization</c>
/// in an app with no callbacks); each function in the callbacks block is a callback, its record
/// the lines between its signature and its <c>end</c>; the one under
/// <c>% Code that executes after component creation</c> is the startup function, and what follows
/// <c>app</c> in its signature the app's input parameters. A responsive app and a Simulink app
/// have a second generated properties block ahead of the editable section, under a comment of the
/// same wording, and a responsive app a generated layout function among the callbacks.
/// </para>
/// <para>
/// A function's <c>end</c> is found by its block structure, not by its indentation, so user code
/// indented any way is cut correctly. Text that is not laid out this way - a plain class, or an app
/// whose marker comments were removed - has no regions, and nothing of it is shaded or derived.
/// </para>
/// </remarks>
public sealed partial class JgsMlappLayout
{
    private const string ComponentProperties = "% Properties that correspond to app components";
    private const string UnderlyingComponents = JGraph.Scripting.Designer.DesignComponentCode.PropertiesComment;
    private const string EventsComment = JGraph.Scripting.Designer.DesignComponentCode.EventsComment;
    private const string EventsBlockLine = JGraph.Scripting.Designer.DesignComponentCode.EventsBlockLine;
    private const string LifecycleBlockLine = "methods (Access = protected)";
    private const string SetupSignature = "function setup(comp)";
    private const string UpdateSignature = "function update(comp)";
    private const string CallbacksBlock = "% Callbacks that handle component events";
    private const string ComponentInitialization = "% Component initialization";
    private const string StartupComment = "% Code that executes after component creation";
    private const string LayoutCallbackComment = "% Changes arrangement of the app based on UIFigure width";

    /// <summary>The line a plain-text app's appendix begins with (<c>SectionInfo.appendixStartGrammar</c>).</summary>
    public const string AppendixMarker = "%[appendix]";

    /// <summary>The grammar line of the run configuration section (<c>SectionInfo.runConfig.grammar</c>).</summary>
    public const string RunConfigurationMarker = "%[app:runConfiguration]";

    private readonly List<MlappRegion> _regions = [];
    private readonly List<(int FirstLine, int LastLine)> _managedBlocks = [];
    private int _componentPropertiesEnd;
    private int _editableFirst;
    private int _editableCount;

    private JgsMlappLayout(string[] lines)
    {
        Lines = lines;
    }

    /// <summary>The text's lines, without their line endings.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Whether the text is laid out as App Designer lays out an app (a custom UI component's class included).</summary>
    public bool IsAppLayout { get; private set; }

    /// <summary>
    /// Whether the class is a custom UI component's as App Designer lays one out (App Designer
    /// window plan W15): its properties block under <c>% Properties that correspond to underlying
    /// components</c>, then the events block and the managed properties' blocks it generates, the
    /// editable section, the callbacks block, and the <c>methods (Access = protected)</c> block
    /// holding <c>update</c>, whose body is the user's, and <c>setup</c>, which is generated.
    /// </summary>
    public bool IsComponentLayout { get; private set; }

    /// <summary>A component's events block, as the zero-based lines of its comment and its <c>end</c>; null when the class has none.</summary>
    public (int FirstLine, int LastLine)? EventsBlock { get; private set; }

    /// <summary>A component's managed properties' blocks, each as the zero-based lines of its <c>properties</c> line and its <c>end</c>, in text order.</summary>
    public IReadOnlyList<(int FirstLine, int LastLine)> ManagedBlocks => _managedBlocks;

    /// <summary>The <c>update</c> method's body of a component; null for an app, or a component whose <c>update</c> was not found.</summary>
    public MlappRegion? Update => _regions.FirstOrDefault(static r => r.Kind == MlappRegionKind.Update);

    /// <summary>
    /// The editable section's lines as App Designer records them, as (first line, count): the
    /// region without the one whitespace line the generated code puts before it (empty for an app;
    /// a component's last managed block leaves an indented one) and the empty line after it.
    /// </summary>
    public (int FirstLine, int LineCount) EditableSectionBounds => (_editableFirst, _editableCount);

    /// <summary>
    /// Whether the app is in App Designer's plain-text format (designer plan DS7a): a class under
    /// <c>matlab.apps.App</c> with no <c>createComponents</c>, constructor or <c>delete</c>, its
    /// components in the <c>%[appendix]</c> after the class. Everything from the class's <c>end</c>
    /// on is generated.
    /// </summary>
    public bool IsPlainText { get; private set; }

    /// <summary>
    /// Whether every function in the callbacks block is closed. Text caught mid-edit may not be,
    /// and then the callbacks cannot be told apart: there are no callback regions.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>The class the file defines; empty when the text does not begin a class.</summary>
    public string ClassName { get; private set; } = string.Empty;

    /// <summary>The user's stretches of the text and the generated callbacks' bodies, in text order.</summary>
    public IReadOnlyList<MlappRegion> Regions => _regions;

    /// <summary>The startup function's parameters after <c>app</c>, as written; empty when it has none.</summary>
    public string InputParameters { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the app runs as a single instance (App Designer's <c>SingletonMode</c> FOCUS, designer
    /// plan DS5): its constructor asks <c>getRunningApp</c> for an instance already running before it
    /// makes its components. Read from the constructor, which App Designer generates.
    /// </summary>
    public bool SingleInstance { get; private set; }

    /// <summary>The startup function's body, or null when the app has no startup function.</summary>
    public MlappRegion? Startup => _regions.FirstOrDefault(static r => r.Kind == MlappRegionKind.Startup);

    /// <summary>
    /// The startup function a plain-text app's run configuration names when no function of the
    /// callbacks block carries that name (it stands in the user's code); null otherwise.
    /// </summary>
    public string? UnplacedStartup { get; private set; }

    /// <summary>The callbacks, generated ones included, in text order.</summary>
    public IEnumerable<MlappRegion> Callbacks =>
        _regions.Where(static r => r.Kind is MlappRegionKind.Callback or MlappRegionKind.GeneratedCallback);

    /// <summary>Reads the layout of a class file's <paramref name="text"/>.</summary>
    public static JgsMlappLayout Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        var layout = new JgsMlappLayout(lines);
        layout.Read(text);
        return layout;
    }

    /// <summary>The lines of a region.</summary>
    public IReadOnlyList<string> CodeOf(MlappRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new ArraySegment<string>((string[])Lines, region.FirstLine, region.LineCount);
    }

    /// <summary>
    /// The editable section as App Designer records it: the lines between the generated
    /// properties and the callbacks, without the one empty line App Designer puts on each side.
    /// Empty when there is none.
    /// </summary>
    public IReadOnlyList<string> EditableSectionCode =>
        _regions.Any(static r => r.Kind == MlappRegionKind.EditableSection)
            ? new ArraySegment<string>((string[])Lines, _editableFirst, _editableCount)
            : [];

    /// <summary>
    /// The editable section counted from the component properties block, any further generated
    /// properties block included. One shipped Simulink app's record of its section is cut this
    /// way, so the save-back holds a record against both cuts and keeps the one it was made with.
    /// </summary>
    internal IReadOnlyList<string> EditableSectionCodeWithGeneratedBlocks =>
        _regions.FirstOrDefault(static r => r.Kind == MlappRegionKind.EditableSection) is { } section
            ? Between(_componentPropertiesEnd + 1, section.FirstLine + section.LineCount - _componentPropertiesEnd - 1)
            : [];

    /// <summary>A run of lines without the one empty line App Designer puts on each side of it.</summary>
    private IReadOnlyList<string> Between(int first, int count)
    {
        (first, count) = Strip(first, count, leadingBlank: false);
        return new ArraySegment<string>((string[])Lines, first, count);
    }

    /// <summary>
    /// The user's lines of a region: without the whitespace line the generated code leaves before
    /// it - an empty one, or for a component any blank one when <paramref name="leadingBlank"/> -
    /// and without the empty line after it.
    /// </summary>
    private (int First, int Count) Strip(int first, int count, bool leadingBlank)
    {
        if (count > 0 && (Lines[first].Length == 0 || (leadingBlank && string.IsNullOrWhiteSpace(Lines[first]))))
        {
            first++;
            count--;
        }

        if (count > 0 && Lines[first + count - 1].Length == 0)
        {
            count--;
        }

        return (first, count);
    }

    /// <summary>
    /// The runs of lines App Designer generates, as (first line, count): everything but the user's
    /// regions. Empty when the text is not an app's.
    /// </summary>
    public IReadOnlyList<(int FirstLine, int LineCount)> GeneratedSpans
    {
        get
        {
            var spans = new List<(int, int)>();
            if (!IsAppLayout)
            {
                return spans;
            }

            int at = 0;
            foreach (MlappRegion region in _regions.Where(static r => r.IsUserCode))
            {
                if (region.FirstLine > at)
                {
                    spans.Add((at, region.FirstLine - at));
                }

                at = region.FirstLine + region.LineCount;
            }

            if (at < Lines.Count)
            {
                spans.Add((at, Lines.Count - at));
            }

            return spans;
        }
    }

    /// <summary>
    /// Whether the code App Designer generates is the same in <paramref name="other"/>: only the
    /// user's regions differ, if anything does. An edit that leaves this true is one App Designer
    /// keeps; one that does not is overwritten the next time App Designer saves the app.
    /// </summary>
    public bool SameGeneratedCode(JgsMlappLayout other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return IsAppLayout == other.IsAppLayout && string.Equals(Skeleton(), other.Skeleton(), StringComparison.Ordinal);
    }

    /// <summary>The generated lines, with a mark where each user region is.</summary>
    private string Skeleton()
    {
        if (!IsAppLayout)
        {
            return string.Join('\n', Lines);
        }

        var skeleton = new StringBuilder();
        foreach ((int first, int count) in GeneratedSpans)
        {
            for (int i = first; i < first + count; i++)
            {
                skeleton.Append(Lines[i]).Append('\n');
            }

            skeleton.Append('\0');
        }

        return skeleton.ToString();
    }

    private void Read(string text)
    {
        if (Lines.Select(static l => ClassLine().Match(l)).FirstOrDefault(static m => m.Success) is { } classLine)
        {
            ClassName = classLine.Groups[1].Value;
        }

        int properties = IndexOfLine(ComponentProperties, 0);
        if (properties < 0 && IndexOfLine(UnderlyingComponents, 0) is var underlying && underlying >= 0)
        {
            properties = underlying;
            IsComponentLayout = true;
        }

        int head = properties < 0 ? -1 : IndexOfLine("end", properties + 1);
        if (head < 0)
        {
            return;
        }

        _componentPropertiesEnd = head;
        if (IsComponentLayout)
        {
            ReadComponent(head);
            return;
        }

        // Further generated properties blocks - a responsive app's, a Simulink app's - each after
        // any number of empty lines and under a comment of the same wording.
        while (true)
        {
            int next = head + 1;
            while (next < Lines.Count && string.IsNullOrWhiteSpace(Lines[next]))
            {
                next++;
            }

            if (next + 1 >= Lines.Count || !GeneratedPropertiesComment().IsMatch(Lines[next])
                || !Lines[next + 1].TrimStart().StartsWith("properties", StringComparison.Ordinal))
            {
                break;
            }

            head = IndexOfLine("end", next + 1);
            if (head < 0)
            {
                return;
            }
        }

        // A plain-text app (designer plan DS7a, App Designer's PlainTextClassBehavior) is told by
        // its class line: it has no createComponents, constructor or delete, and ends at its own
        // `end` before the appendix; which function is the startup one, and whether the app runs
        // as a single instance, the appendix's run configuration says. Its user's code may hold
        // the .mlapp cutter's marker comment without being cut by it.
        bool plain = PlainTextClassLine().IsMatch(Lines.FirstOrDefault(static l => ClassLine().IsMatch(l)) ?? string.Empty);
        int initialization = plain ? -1 : IndexOfLine(ComponentInitialization, head + 1);
        string? plainStartup = null;
        if (initialization < 0)
        {
            if (!plain)
            {
                return;
            }

            int appendix = AppendixLine(Lines);
            if (appendix <= head)
            {
                appendix = -1;
            }

            int classEnd = LastLineAt(string.Empty, head + 1, appendix < 0 ? Lines.Count : appendix);
            if (classEnd < 0)
            {
                return;
            }

            IsPlainText = true;
            initialization = classEnd;
            (plainStartup, SingleInstance) = ReadRunConfiguration(appendix);
        }

        int callbacks = IndexOfLine(CallbacksBlock, head + 1);
        if (callbacks > initialization)
        {
            callbacks = -1;
        }

        int anchor = callbacks < 0 ? initialization : callbacks;
        IsAppLayout = true;
        _regions.Add(new MlappRegion(MlappRegionKind.EditableSection, string.Empty, head + 1, anchor - head - 1));
        (_editableFirst, _editableCount) = Strip(head + 1, anchor - head - 1, leadingBlank: false);
        IsComplete = callbacks < 0 || ReadCallbacks(callbacks, initialization);
        if (!IsPlainText)
        {
            SingleInstance = ReadSingleInstance(initialization);
        }
        else if (plainStartup is not null && Startup?.Name != plainStartup)
        {
            // The run configuration's word outranks the comment over a function: the app runs the
            // function it names (initializeMApp, the loader). The commented one is a callback like
            // any other; the named one is the startup function when the callbacks block has it,
            // else it stands in the user's code and its name alone is kept.
            int commented = _regions.FindIndex(static r => r.Kind == MlappRegionKind.Startup);
            if (commented >= 0)
            {
                _regions[commented] = _regions[commented] with { Kind = MlappRegionKind.Callback };
                InputParameters = string.Empty;
            }

            int at = _regions.FindIndex(r => r.Kind == MlappRegionKind.Callback && r.Name == plainStartup);
            if (at >= 0)
            {
                MlappRegion named = _regions[at];
                _regions[at] = named with { Kind = MlappRegionKind.Startup };
                InputParameters = StartupParameters(named.FirstLine - 1);
            }
            else
            {
                UnplacedStartup = plainStartup;
            }
        }
    }

    /// <summary>
    /// The cut of a custom UI component's class (App Designer window plan W15), after its underlying
    /// components' block ending at <paramref name="head"/>: the generated events block and managed
    /// properties' blocks, each followed by its whitespace line; the editable section up to the
    /// callbacks block or the lifecycle block; the callbacks; and the lifecycle block's <c>update</c>,
    /// whose body is the user's, before the generated <c>setup</c>.
    /// </summary>
    private void ReadComponent(int head)
    {
        int at = head + 1;

        // The events block, under its comment, after the properties block's empty line.
        int look = SkipBlank(at);
        if (look + 1 < Lines.Count && Lines[look].Trim() == EventsComment && Lines[look + 1].Trim() == EventsBlockLine)
        {
            int end = IndexOfLine("end", look + 2);
            if (end < 0)
            {
                return;
            }

            EventsBlock = (look, end);
            at = end + 1;
        }

        // The managed properties' blocks: each a properties block of the attributes the dialog
        // writes whose every declaration is one the dialog makes, after its predecessor's one
        // whitespace line, the blocks in the order of their attributes as the group writes them; the
        // first block that is anything else - a user's own, typed into the editable section - begins
        // the editable section. (A user's block that looks like the dialog's and stands where the
        // next managed block would is read as managed; the .mlapp's own record would tell, open item.)
        string lastKey = string.Empty;
        while (true)
        {
            look = SkipBlank(at);
            if (look >= Lines.Count || look > at + 1)
            {
                break;
            }

            IReadOnlyList<string>? attributes = JGraph.Scripting.Designer.DesignComponentCode.AttributesOf(Lines[look]);
            if (attributes is null || !JGraph.Scripting.Designer.DesignComponentCode.IsManagedBlock(attributes))
            {
                break;
            }

            string key = string.Concat(attributes.Order(StringComparer.Ordinal));
            if (string.CompareOrdinal(key, lastKey) <= 0)
            {
                break;
            }

            int end = IndexOfLine("end", look + 1);
            if (end < 0)
            {
                return;
            }

            bool managed = true;
            for (int i = look + 1; i < end && managed; i++)
            {
                managed = string.IsNullOrWhiteSpace(Lines[i]) || JGraph.Scripting.Designer.DesignManagedProperty.Parse(Lines[i], attributes) is not null;
            }

            if (!managed)
            {
                break;
            }

            _managedBlocks.Add((look, end));
            lastKey = key;
            at = end + 1;
        }

        // The lifecycle block: the methods (Access = protected) line over setup.
        int setup = IndexOfLine(SetupSignature, at);
        if (setup < 0)
        {
            return;
        }

        int lifecycle = -1;
        for (int i = setup - 1; i >= at; i--)
        {
            if (Lines[i].Trim() == LifecycleBlockLine)
            {
                lifecycle = i;
                break;
            }
        }

        if (lifecycle < 0)
        {
            return;
        }

        int callbacks = IndexOfLine(CallbacksBlock, at);
        if (callbacks > lifecycle)
        {
            callbacks = -1;
        }

        int anchor = callbacks < 0 ? lifecycle : callbacks;
        IsAppLayout = true;
        _regions.Add(new MlappRegion(MlappRegionKind.EditableSection, string.Empty, at, Math.Max(0, anchor - at)));
        (_editableFirst, _editableCount) = Strip(at, Math.Max(0, anchor - at), leadingBlank: true);
        bool complete = callbacks < 0 || ReadCallbacks(callbacks, lifecycle);

        // update's body: between its signature and the last end at its indentation before setup.
        int update = IndexOfLine(UpdateSignature, lifecycle + 1);
        if (update < 0 || update > setup)
        {
            IsComplete = false;
            return;
        }

        string indent = IndentOf(Lines[update]);
        int close = -1;
        for (int i = update + 1; i < setup; i++)
        {
            if (IsEndAt(Lines[i], indent))
            {
                close = i;
            }
        }

        if (close < 0)
        {
            IsComplete = false;
            return;
        }

        _regions.Add(new MlappRegion(MlappRegionKind.Update, "update", update + 1, Math.Max(0, close - update - 1)));
        SortRegions();
        IsComplete = complete;
    }

    /// <summary>The first line at or after <paramref name="from"/> that is not blank; the count when there is none.</summary>
    private int SkipBlank(int from)
    {
        int i = from;
        while (i < Lines.Count && string.IsNullOrWhiteSpace(Lines[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// The startup function's name and the single-instance setting from a plain-text app's run
    /// configuration section (<c>%[app:runConfiguration]</c>: <c>StartupFcn</c> and
    /// <c>SingleRunningInstance</c> elements, as <c>AppAppendixParser</c> reads them); the app has
    /// neither when the section is absent.
    /// </summary>
    private (string? Startup, bool SingleInstance) ReadRunConfiguration(int appendix)
    {
        if (appendix < 0)
        {
            return (null, false);
        }

        int section = IndexOfLine(RunConfigurationMarker, appendix + 1);
        if (section < 0)
        {
            return (null, false);
        }

        string? startup = null;
        bool singleton = false;
        for (int i = section + 1; i < Lines.Count && Lines[i].Trim() != "%}"; i++)
        {
            Match m = RunConfigurationElement().Match(Lines[i]);
            if (!m.Success)
            {
                continue;
            }

            string value = m.Groups[2].Value.Trim();
            if (m.Groups[1].Value == "StartupFcn" && value.Length > 0)
            {
                startup = value;
            }
            else if (m.Groups[1].Value == "SingleRunningInstance")
            {
                singleton = value == "true";
            }
        }

        return (startup, singleton);
    }

    /// <summary>
    /// The zero-based line a plain-text app's appendix begins on (open item 161): the first
    /// <c>%[appendix]</c> line after the class's last line of code, which in a class that parses is
    /// its closing <c>end</c> - after it every line is blank or a comment (a <c>%</c> line, or a line
    /// of a <c>%{</c> ... <c>%}</c> block, where the sections' XML stands). That is where App
    /// Designer's writer puts it (<c>PlainTextFileContentGroupBehavior</c>). A marker line typed into
    /// the user's code, a callback's comment, is not the appendix: code follows it. -1 when there is
    /// none.
    /// </summary>
    /// <remarks>
    /// R2025b's MATLAB side never cuts by this line (its loader and its run take each section by its
    /// grammar line, <c>getAppendixByGrammarName</c>), so a marker line in the user's code is
    /// harmless there: measured, such an app loads and runs as one without it.
    /// </remarks>
    public static int AppendixLine(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int lastCode = -1;
        int depth = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line == "%{")
            {
                depth++;
            }
            else if (depth > 0)
            {
                if (line == "%}")
                {
                    depth--;
                }
            }
            else if (line.Length > 0 && line[0] != '%')
            {
                lastCode = i;
            }
        }

        for (int i = lastCode + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim() == AppendixMarker)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The index in <paramref name="text"/> of the first character of its appendix's line (<see cref="AppendixLine"/>), or -1.</summary>
    public static int AppendixOffset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains(AppendixMarker, StringComparison.Ordinal))
        {
            return -1;
        }

        string[] lines = text.Split('\n');
        int line = AppendixLine(lines);
        if (line < 0)
        {
            return -1;
        }

        int offset = 0;
        for (int i = 0; i < line; i++)
        {
            offset += lines[i].Length + 1;
        }

        return offset;
    }

    /// <summary>The last line in [<paramref name="from"/>, <paramref name="limit"/>) that is an <c>end</c> standing at exactly <paramref name="indent"/>, or -1.</summary>
    private int LastLineAt(string indent, int from, int limit)
    {
        for (int i = Math.Min(limit, Lines.Count) - 1; i >= from; i--)
        {
            if (IsEndAt(Lines[i], indent))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether the constructor - the function after <c>createComponents</c> that returns the class's
    /// name - calls <c>getRunningApp</c> before <c>delete</c>'s function begins.
    /// </summary>
    private bool ReadSingleInstance(int initialization)
    {
        var constructor = new Regex(@"^\s*function\s+\w+\s*=\s*" + Regex.Escape(ClassName) + @"\s*(\(|$)");
        int at = -1;
        for (int i = initialization + 1; i < Lines.Count; i++)
        {
            if (constructor.IsMatch(Lines[i]))
            {
                at = i;
                break;
            }
        }

        for (int i = at + 1; at >= 0 && i < Lines.Count; i++)
        {
            if (DeleteFunction().IsMatch(Lines[i]))
            {
                break;
            }

            if (GetRunningApp().IsMatch(Lines[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first line at or after <paramref name="from"/> that is <paramref name="text"/> once trimmed, or -1.</summary>
    private int IndexOfLine(string text, int from)
    {
        for (int i = from; i < Lines.Count; i++)
        {
            if (Lines[i].Trim() == text)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds each function of the callbacks block and the lines between its signature and its
    /// <c>end</c>, by the lines App Designer generates rather than by the balance of the blocks in
    /// between, as App Designer's editor tells its generated lines apart from the user's (its
    /// <c>RTCUserEditManager</c> cuts by the lines it marked read-only, and never parses a body): the
    /// block's <c>end</c> stands at its <c>methods</c> line's indentation; a function begins at a
    /// <c>function</c> line at the first function's indentation and ends at the last <c>end</c> at that
    /// indentation before the next function or the block's <c>end</c>. A body the user is typing an
    /// <c>if</c> or a <c>for</c> into is still a body, and a function nested in a body is part of it.
    /// False when the block is not closed or a function has no <c>end</c> of its own.
    /// </summary>
    private bool ReadCallbacks(int callbacks, int initialization)
    {
        // The block's own `methods` line follows the comment; its functions start below that.
        int methods = callbacks + 1;
        if (methods >= initialization)
        {
            return false;
        }

        string blockIndent = IndentOf(Lines[methods]);
        string? functionIndent = null;
        int blockEnd = -1;
        var signatures = new List<int>();
        for (int i = methods + 1; i < initialization; i++)
        {
            string line = Lines[i];
            if (IsEndAt(line, blockIndent))
            {
                blockEnd = i;
                break;
            }

            if (FunctionStart().IsMatch(line))
            {
                string indent = IndentOf(line);
                if (indent.Length > blockIndent.Length && (functionIndent ??= indent) == indent)
                {
                    signatures.Add(i);
                }
            }
        }

        if (blockEnd < 0)
        {
            return false;
        }

        var found = new List<MlappRegion>();
        for (int s = 0; s < signatures.Count; s++)
        {
            int signature = signatures[s];
            int limit = s + 1 < signatures.Count ? signatures[s + 1] : blockEnd;
            int close = -1;
            for (int i = signature + 1; i < limit; i++)
            {
                if (IsEndAt(Lines[i], functionIndent!))
                {
                    close = i;
                }
            }

            if (close < 0)
            {
                return false;
            }

            found.Add(RegionOf(signature, close));
        }

        _regions.AddRange(found);
        return true;
    }

    /// <summary>The regions in text order, which the update region added after the callbacks may not be in.</summary>
    private void SortRegions() => _regions.Sort(static (a, b) => a.FirstLine.CompareTo(b.FirstLine));

    /// <summary>The whitespace a line begins with.</summary>
    private static string IndentOf(string line) => line[..(line.Length - line.TrimStart().Length)];

    /// <summary>Whether <paramref name="line"/> is an <c>end</c> standing at exactly <paramref name="indent"/>, with nothing after it but spaces, a semicolon or a comment.</summary>
    private static bool IsEndAt(string line, string indent) =>
        line.StartsWith(indent, StringComparison.Ordinal) && EndLine().IsMatch(line[indent.Length..]);

    /// <summary>The body between a function's signature line and its closing line, and what the function is.</summary>
    private MlappRegion RegionOf(int signature, int close)
    {
        Match match = FunctionLine().Match(Lines[signature]);
        string name = match.Success ? match.Groups[1].Value : string.Empty;
        string above = signature > 0 ? Lines[signature - 1].Trim() : string.Empty;
        MlappRegionKind kind = above switch
        {
            StartupComment => MlappRegionKind.Startup,
            LayoutCallbackComment => MlappRegionKind.GeneratedCallback,
            _ => MlappRegionKind.Callback,
        };

        if (kind == MlappRegionKind.Startup && match.Success)
        {
            InputParameters = StartupParameters(signature);
        }

        return new MlappRegion(kind, name, signature + 1, Math.Max(0, close - signature - 1));
    }

    /// <summary>What follows <c>app</c> in the startup function's signature at line <paramref name="signature"/>: the app's input parameters as written.</summary>
    private string StartupParameters(int signature)
    {
        Match match = FunctionLine().Match(Lines[signature]);
        string parameters = match.Success ? match.Groups[2].Value : string.Empty;
        int comma = parameters.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? string.Empty : parameters[(comma + 1)..].Trim();
    }

    [GeneratedRegex(@"^\s*%.*[Pp]roperties that correspond to ")]
    private static partial Regex GeneratedPropertiesComment();

    [GeneratedRegex(@"^end\b\s*;?\s*(?:%.*)?$")]
    private static partial Regex EndLine();

    [GeneratedRegex(@"^\s*function\b")]
    private static partial Regex FunctionStart();

    [GeneratedRegex(@"^\s*classdef\s+(?:\(.*?\)\s*)?(\w+)")]
    private static partial Regex ClassLine();

    /// <summary>A class line naming <c>matlab.apps.App</c>, the superclass of a plain-text app (<c>PlainTextStrategy.getSuperClassNames</c>), as its only superclass.</summary>
    [GeneratedRegex(@"^\s*classdef\s+(?:\(.*?\)\s*)?\w+\s*<\s*matlab\.apps\.App\s*(?:%.*)?$")]
    private static partial Regex PlainTextClassLine();

    /// <summary>One element of the run configuration section: <c>&lt;StartupFcn&gt;name&lt;/StartupFcn&gt;</c>.</summary>
    [GeneratedRegex(@"^\s*<(StartupFcn|SingleRunningInstance)>(.*?)</\1>\s*$")]
    private static partial Regex RunConfigurationElement();

    [GeneratedRegex(@"^\s*function\s+(?:(?:\[[^\]]*\]|\w+)\s*=\s*)?(\w+)\s*(?:\((.*)\))?")]
    private static partial Regex FunctionLine();

    [GeneratedRegex(@"^\s*function\s+delete\s*\(")]
    private static partial Regex DeleteFunction();

    [GeneratedRegex(@"\bgetRunningApp\s*\(")]
    private static partial Regex GetRunningApp();
}
