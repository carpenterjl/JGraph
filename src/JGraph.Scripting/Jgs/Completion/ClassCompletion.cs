using System.Text.RegularExpressions;
using JGraph.Scripting.Completion;

namespace JGraph.Scripting.Jgs.Completion;

/// <summary>
/// What follows <c>app.</c> in a class file (U7 of the app-building plan, ADR 0204): the
/// properties and methods the buffer's <c>classdef</c> declares, and the ones it inherits from a
/// class this build supplies. The buffer is read as text, line by line, because a buffer being
/// typed in rarely parses; nothing is run and no file is read.
/// </summary>
/// <remarks>
/// The name before the dot is the object when it is what the class's own methods call it: the
/// first parameter of a method, or the output of the constructor. Every other name is left to
/// the completions that were there before.
/// </remarks>
internal static partial class ClassCompletion
{
    private static readonly string[] HandleMethods = ["addlistener", "delete", "isvalid", "listener", "notify"];

    private static readonly (string Name, string Signature, string Summary)[] AppBaseMethods =
    [
        ("createCallbackFcn", "createCallbackFcn(app, callback, requiresEventData)",
            "The handle a component's callback property is given: called with the component and its event, it calls the method with the app."),
        ("getRunningApp", "getRunningApp(app)", "The app of this class that is already running, or an empty."),
        ("registerApp", "registerApp(app, uiFigure)", "Ties the app to its figure: deleting either deletes the other."),
        ("runStartupFcn", "runStartupFcn(app, startupFcn)", "Calls the startup function with the app."),
        ("setAutoResize", "setAutoResize(app, uiFigure, value)", "Sets the figure's AutoResizeChildren."),
    ];

    [GeneratedRegex(@"^\s*classdef\b(?:\s*\([^)]*\))?\s+(?<name>\w+)\s*(?:<\s*(?<supers>[^%\r\n]*))?", RegexOptions.Multiline)]
    private static partial Regex ClassHeader();

    [GeneratedRegex(@"^\s*function\s+(?:(?<out>\w+)\s*=\s*|\[[^\]]*\]\s*=\s*)?(?<name>[\w.]+)\s*(?:\((?<args>[^)]*)\))?", RegexOptions.Multiline)]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"^\s*properties\b[^\r\n]*\r?\n(?<body>.*?)^\s*end\b", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex PropertiesBlock();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z]\w*)", RegexOptions.Multiline)]
    private static partial Regex PropertyLine();

    /// <summary>
    /// The members to offer after <paramref name="qualifier"/><c>.</c>, or null when the buffer
    /// is not a class file or the name is not what its methods call the object.
    /// </summary>
    public static IReadOnlyList<CompletionItem>? Members(string code, string qualifier)
    {
        if (qualifier.Contains('.', StringComparison.Ordinal) || ClassHeader().Match(code) is not { Success: true } header)
        {
            return null;
        }

        string className = header.Groups["name"].Value;
        var selves = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match block in PropertiesBlock().Matches(code, header.Index))
        {
            foreach (Match line in PropertyLine().Matches(block.Groups["body"].Value))
            {
                string name = line.Groups["name"].Value;
                if (seen.Add(name))
                {
                    items.Add(new CompletionItem(name, CompletionItemKind.Variable, Signature: null, $"property of {className}"));
                }
            }
        }

        foreach (Match function in FunctionHeader().Matches(code, header.Index))
        {
            string name = function.Groups["name"].Value;
            string[] arguments = [.. function.Groups["args"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
            bool constructor = name == className;
            if (constructor && function.Groups["out"].Success)
            {
                selves.Add(function.Groups["out"].Value);
            }
            else if (!constructor && arguments.Length > 0 && arguments[0] != "~")
            {
                selves.Add(arguments[0]);
            }

            // An accessor (get.Name) is how a property is read, not a method to call by name.
            if (!constructor && !name.Contains('.', StringComparison.Ordinal) && seen.Add(name))
            {
                items.Add(new CompletionItem(name, CompletionItemKind.Function, $"{name}({string.Join(", ", arguments)})", $"method of {className}"));
            }
        }

        if (!selves.Contains(qualifier))
        {
            return null;
        }

        string supers = header.Groups["supers"].Value;
        bool app = supers.Contains(JgsBuiltinClasses.AppBase, StringComparison.Ordinal);
        if (app)
        {
            foreach ((string name, string signature, string summary) in AppBaseMethods)
            {
                if (seen.Add(name))
                {
                    items.Add(new CompletionItem(name, CompletionItemKind.Builtin, signature, summary));
                }
            }
        }

        if (app || supers.Contains("handle", StringComparison.Ordinal) || supers.Contains("matlab.mixin.", StringComparison.Ordinal))
        {
            foreach (string name in HandleMethods)
            {
                if (seen.Add(name))
                {
                    items.Add(new CompletionItem(name, CompletionItemKind.Builtin, Signature: null, "inherited from handle"));
                }
            }
        }

        return items;
    }
}
