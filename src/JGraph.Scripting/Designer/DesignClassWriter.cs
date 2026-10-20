using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Writes an app's whole class as R2025b's App Designer writes it (designer plan DS5; its
/// <c>CodeEngine</c> with <c>AppCodeEngineConfig</c>): the component properties block and
/// <c>createComponents</c> from the design (<see cref="DesignCodeWriter"/>), a responsive app's
/// widths and layout callback (<see cref="DesignResponsive"/>), and around the user's code the
/// callbacks block - each callback's comment generated from what binds it, its signature, the
/// startup function with the app's input parameters - and the lifecycle block: the constructor,
/// with or without <c>varargin</c>, as a single instance or not, and <c>delete</c>. The pieces come
/// in the order <c>MlappClassBehavior</c>'s groups put them, one empty line between blocks as its
/// whitespace builders leave it.
/// </summary>
public static class DesignClassWriter
{
    /// <summary>The class every app derives from.</summary>
    public const string AppBase = "matlab.apps.AppBase";

    /// <summary>The comment over the callbacks block (<c>callbackBlockComment</c>).</summary>
    public const string CallbacksComment = "% Callbacks that handle component events";

    /// <summary>The comment over the createComponents block (<c>componentConstructionSectionComment</c>).</summary>
    public const string InitializationComment = "% Component initialization";

    /// <summary>The comment over the lifecycle block (<c>appLifeCycleComment</c>).</summary>
    public const string LifecycleComment = "% App creation and deletion";

    private const string App = DesignValueText.AppObjectName;

    /// <summary>
    /// The class's text: <see cref="Lines"/> joined by <paramref name="newline"/>, with a final line
    /// ending when <paramref name="trailingNewline"/>.
    /// </summary>
    public static string Render(AppDesign design, DesignCode code, DesignCodeStyle? style = null, Func<DesignComponent, Size2D?>? sizeOf = null, string newline = "\n", bool trailingNewline = true)
    {
        string text = string.Join(newline, Lines(design, code, style, sizeOf));
        return trailingNewline ? text + newline : text;
    }

    /// <summary>The class's lines, without line endings.</summary>
    /// <param name="design">The app's components.</param>
    /// <param name="code">The app's code around them.</param>
    /// <param name="style">The code's indentation and theme; App Designer's own when null.</param>
    /// <param name="sizeOf">A responsive region's size as the canvas lays it out, for its layout callback; null when there is no canvas.</param>
    public static IReadOnlyList<string> Lines(AppDesign design, DesignCode code, DesignCodeStyle? style = null, Func<DesignComponent, Size2D?>? sizeOf = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(code);
        style ??= DesignCodeStyle.Default;
        string i1 = style.Indent(1);
        string i2 = style.Indent(2);
        var lines = new List<string> { $"classdef {code.ClassName} < {AppBase}" };
        lines.AddRange(code.HeadComments);
        lines.Add(string.Empty);

        // The component properties, as the text had them when they still declare the design's components.
        IReadOnlyList<string> block = DesignCodeWriter.PropertiesBlock(design, style);
        lines.AddRange(code.PropertiesBlockAsRead is { } held && SameDeclarations(held, block) ? held : block);

        // A responsive template's widths are written again; any other generated block is kept, with
        // the empty lines App Designer left before it.
        int panels = design.IsResponsive ? DesignResponsive.Panels(design) : 0;
        bool template = panels > 0 && DesignResponsive.IsTemplate(design);
        var others = code.OtherPropertiesBlocks.ToList();
        if (template)
        {
            var widths = new DesignGeneratedBlock(DesignResponsive.PropertiesBlock(panels, style), 1);
            int at = others.FindIndex(static b => b.Lines.Count > 0 && b.Lines[0].Trim() == DesignResponsive.PropertiesComment);
            if (at >= 0)
            {
                others[at] = widths;
            }
            else
            {
                others.Insert(0, widths);
            }
        }

        foreach (DesignGeneratedBlock other in others)
        {
            lines.AddRange(Enumerable.Repeat(string.Empty, Math.Max(1, other.BlankLinesBefore)));
            lines.AddRange(other.Lines);
        }

        lines.Add(string.Empty);

        if (code.EditableSection.Count > 0)
        {
            lines.AddRange(code.EditableSection);
            lines.Add(string.Empty);
        }

        if (code.Callbacks.Count > 0)
        {
            lines.Add(i1 + CallbacksComment);
            lines.Add(i1 + "methods (Access = private)");
            foreach (DesignCallback function in code.Callbacks)
            {
                lines.Add(string.Empty);
                lines.AddRange(Function(design, code, function, style, template ? sizeOf : null, template));
            }

            lines.Add(i1 + "end");
            lines.Add(string.Empty);
        }

        lines.Add(i1 + InitializationComment);
        lines.Add(i1 + "methods (Access = private)");
        lines.Add(string.Empty);
        lines.AddRange(DesignCodeWriter.Method(design, style));
        lines.Add(i1 + "end");
        lines.Add(string.Empty);

        lines.Add(i1 + LifecycleComment);
        lines.Add(i1 + "methods (Access = public)");
        lines.Add(string.Empty);
        lines.AddRange(Constructor(code, design.Figure.CodeName, style));
        lines.Add(string.Empty);
        lines.AddRange(Delete(design.Figure.CodeName, style));
        lines.Add(i1 + "end");
        lines.Add("end");
        return lines;
    }

    /// <summary>Whether <paramref name="held"/>, a component properties block, declares what <paramref name="written"/> declares, in any order.</summary>
    public static bool SameDeclarations(IReadOnlyList<string> held, IReadOnlyList<string> written)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(written);
        return Declarations(held).SequenceEqual(Declarations(written), StringComparer.Ordinal);

        static IEnumerable<string> Declarations(IReadOnlyList<string> block) =>
            block.Skip(2).SkipLast(1)
                .Select(static l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Where(static l => l.Length > 0)
                .Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// One function of the callbacks block, from its comment to its <c>end</c> (<c>CallbackBehavior</c>
    /// over <c>MethodBuilder</c>): the comment lines wrapped at the right-hand limit less the method's
    /// indentation, the signature, the body as the model holds it - a responsive template's layout
    /// callback written again from the regions - and the <c>end</c>.
    /// </summary>
    public static IReadOnlyList<string> Function(AppDesign design, DesignCode code, DesignCallback function, DesignCodeStyle? style = null, Func<DesignComponent, Size2D?>? sizeOf = null, bool regenerateLayout = true)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(function);
        style ??= DesignCodeStyle.Default;
        string i2 = style.Indent(2);
        IEnumerable<string> comment = function.IsGenerated
            ? [DesignResponsive.CallbackComment]
            : function.IsStartup ? [DesignCallbacks.Format(DesignCallbacks.StartupComment)]
            : DesignCallbacks.R2025b.Comment(design, function);
        int width = DesignCallbacks.RightHandLimit - style.IndentSize * 2;
        var lines = new List<string>();
        foreach (string c in comment)
        {
            lines.AddRange(DesignCallbacks.Wrap(c, width).Select(l => i2 + DesignCallbacks.Format(l)));
        }

        lines.Add(i2 + Signature(code, function));
        IReadOnlyList<string>? generated = function.IsGenerated && regenerateLayout ? DesignResponsive.Body(design, sizeOf) : null;
        lines.AddRange(generated is not null ? generated.Select(l => style.Indent(3) + l) : function.Body);
        lines.Add(i2 + "end");
        return lines;
    }

    /// <summary>A function's signature: the startup function takes <c>app</c> and the input parameters, any other <c>app, event</c>.</summary>
    public static string Signature(DesignCode code, DesignCallback function)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(function);
        IEnumerable<string> arguments = function.IsStartup
            ? [App, .. code.InputParameterNames]
            : [App, "event"];
        return $"function {function.Name}({string.Join(", ", arguments)})";
    }

    /// <summary>
    /// The constructor, from its comment to its <c>end</c> (<c>ConstructorBehavior</c> with
    /// <c>ConstructorCodeBehavior</c>, or <c>ConstructorSingletonCodeBehavior</c> for a single
    /// instance): <c>varargin</c> when the app takes input parameters, the startup function run
    /// when there is one, and <c>app</c> cleared when nothing takes it.
    /// </summary>
    public static IReadOnlyList<string> Constructor(DesignCode code, string figure, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(figure);
        style ??= DesignCodeStyle.Default;
        string i2 = style.Indent(2);
        string i3 = style.Indent(3);
        string i4 = style.Indent(4);
        bool parameters = code.InputParameters.Length > 0;
        string? startup = code.Startup is { } s
            ? code.InputParameterNames.Count > 0 ? $"runStartupFcn({App}, @({App}){s.Name}({App}, varargin{{:}}))" : $"runStartupFcn({App}, @{s.Name})"
            : null;
        var lines = new List<string>
        {
            i2 + "% Construct app",
            i2 + $"function {App} = {code.ClassName}" + (parameters ? "(varargin)" : string.Empty),
            string.Empty,
        };

        if (!code.SingleInstance)
        {
            lines.AddRange(
            [
                i3 + "% Create UIFigure and components",
                i3 + $"createComponents({App})",
                string.Empty,
                i3 + "% Register the app with App Designer",
                i3 + $"registerApp({App}, {App}.{figure})",
                string.Empty,
            ]);
            if (startup is not null)
            {
                lines.Add(i3 + "% Execute the startup function");
                lines.Add(i3 + startup);
                lines.Add(string.Empty);
            }
        }
        else
        {
            lines.AddRange(
            [
                i3 + $"runningApp = getRunningApp({App});",
                string.Empty,
                i3 + "% Check for running singleton app",
                i3 + "if isempty(runningApp)",
                string.Empty,
                i4 + "% Create UIFigure and components",
                i4 + $"createComponents({App})",
                string.Empty,
                i4 + "% Register the app with App Designer",
                i4 + $"registerApp({App}, {App}.{figure})",
            ]);
            if (startup is not null)
            {
                lines.Add(string.Empty);
                lines.Add(i4 + "% Execute the startup function");
                lines.Add(i4 + startup);
            }

            lines.AddRange(
            [
                i3 + "else",
                string.Empty,
                i4 + "% Focus the running singleton app",
                i4 + $"figure(runningApp.{figure})",
                string.Empty,
                i4 + $"{App} = runningApp;",
                i3 + "end",
                string.Empty,
            ]);
        }

        lines.AddRange(
        [
            i3 + "if nargout == 0",
            i4 + $"clear {App}",
            i3 + "end",
            i2 + "end",
        ]);
        return lines;
    }

    /// <summary><c>delete</c>, from its comment to its <c>end</c> (<c>DeleteBehavior</c> with <c>DeleteCodeBehavior</c>).</summary>
    public static IReadOnlyList<string> Delete(string figure, DesignCodeStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(figure);
        style ??= DesignCodeStyle.Default;
        string i2 = style.Indent(2);
        string i3 = style.Indent(3);
        return
        [
            i2 + "% Code that executes before app deletion",
            i2 + $"function delete({App})",
            string.Empty,
            i3 + "% Delete UIFigure when app is deleted",
            i3 + $"delete({App}.{figure})",
            i2 + "end",
        ];
    }
}
