using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// App Designer's Run (designer plan DS8): the saved app run in the session's base workspace as a
/// user's call of its name would run it, as R2025b's <c>AppModel.runApp</c>, <c>runAppHelper</c> and
/// <c>AppManagementService.runApp</c> do it - the file must be there; an app of the same name running
/// from another file refuses (the designer's own instance aside); the class is cleared and the app's
/// folder put at the front of the path; the name must then resolve to this file (the current folder
/// and anything of higher precedence refuse, in App Designer's words); the instance the designer
/// launched before is deleted first - or, for a single-instance app it has none of, one started
/// elsewhere; and the constructor is evaluated in the base workspace, the instance answered and not
/// bound. A startup function that wanted arguments Run did not give has the app this run made
/// deleted and App Designer's error raised over the cause.
/// </summary>
internal static class DesignRun
{
    /// <summary>
    /// Runs the app saved at <paramref name="fullFileName"/> in <paramref name="interpreter"/>'s base
    /// workspace. <paramref name="launched"/> is the instance the designer's last Run of this file
    /// made, if any (App Designer's <c>RunningApp</c>); <paramref name="remember"/> is told the
    /// instance this run made, or null, before anything is thrown.
    /// </summary>
    /// <returns>The running app.</returns>
    /// <exception cref="JgsRuntimeException">What App Designer refuses with, under its identifiers.</exception>
    public static JgsValue Run(Interpreter interpreter, string fullFileName, bool singleInstance, JgsValue? launched, Action<JgsValue?> remember) =>
        Run(interpreter, fullFileName, singleInstance, launched, string.Empty, false, remember);

    /// <summary>
    /// <see cref="Run(Interpreter, string, bool, JgsValue?, Action{JgsValue?})"/> with the app's input <paramref name="arguments"/> as typed (<c>AppModel._sendRunAppEvent</c>: blanks alone are none), which the base workspace evaluates,
    /// and <paramref name="changeFolder"/>, which makes the app's folder current first (<c>AppController</c>). An app in a <c>+package</c> folder is run by its whole name, from the folder above the packages.
    /// </summary>
    /// <exception cref="JgsRuntimeException">What App Designer refuses with, under its identifiers.</exception>
    public static JgsValue Run(Interpreter interpreter, string fullFileName, bool singleInstance, JgsValue? launched, string arguments, bool changeFolder, Action<JgsValue?> remember)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(fullFileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(remember);
        fullFileName = Path.GetFullPath(fullFileName); // as the session names the file (a command line may say it with forward slashes)
        (string name, string folder) = CommandOf(fullFileName);
        string fileName = Path.GetFileName(fullFileName);
        if (changeFolder)
        {
            string here = Path.GetDirectoryName(fullFileName)!.Replace("'", "''", StringComparison.Ordinal);
            interpreter.EvaluateSourceIn($"cd('{here}')", interpreter.Globals, 0, 0);
        }

        if (!File.Exists(fullFileName))
        {
            throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:RunFailedFileNotFound",
                $"Error running '{fileName}'.\n{fullFileName} not found.");
        }

        if (IsComponentFile(fullFileName))
        {
            return RunComponent(interpreter, fullFileName, launched, remember); // App Designer's UserComponentRunner (W15)
        }

        JgsObject? own = launched is { Type: JgsType.Object } l && !l.AsObject.Deleted ? l.AsObject : null;

        // isSameNameAppRunning: an app of this class running from another file (case blind on
        // Windows), the designer's own instance aside.
        foreach ((JgsValue running, string file) in RunningApps())
        {
            if (!ReferenceEquals(running.AsObject, own) && string.Equals(running.AsObject.Class.Name, name, StringComparison.Ordinal)
                && !SameFile(file, fullFileName))
            {
                throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:SameNameAppRunning",
                    $"An app named '{name}' is already running. Please close it before running a different app with that name.");
            }
        }

        // clear(command); addpath(appPath) - so that which() finds this file first unless something outranks it.
        interpreter.FunctionPath?.Unload(name);
        interpreter.FunctionPath?.Add(folder, atEnd: false);

        // isAppNameShadowed: what the name resolves to, from the base workspace, must be this file.
        Resolution found = interpreter.Resolver.Query(name, interpreter.Globals).FirstOrDefault(Resolution.None);
        if (found.Found && !SameFile(found.File ?? string.Empty, fullFileName))
        {
            string shadower = found.File is { Length: > 0 } file ? file
                : found.Layer == ResolutionLayer.Builtin ? $"the built-in function {name}"
                : $"the function {name}";
            string? shadowerFolder = found.File is { Length: > 0 } ? Path.GetDirectoryName(found.File) : null;
            string current = (interpreter.Host as JGraphScriptGlobals)?.CurrentDirectory ?? Environment.CurrentDirectory;
            if (shadowerFolder is not null && SameFile(shadowerFolder, current))
            {
                throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:AppNameShadowedByCWD",
                    $"Unable to run the app because \n{shadower} shadows it in the current working folder.\n\nTo run the app, change the MATLAB current folder.");
            }

            throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:AppNameShadowedByPrecedence",
                $"Unable to run the app because {shadower} has a higher precedence.\n\nTo run the app, delete or rename {Path.GetFileName(found.File) ?? name}.");
        }

        // runAppHelper: the instance this designer launched is closed first (AppInstanceCloser:
        // delete); with none, a single-instance app started elsewhere is closed
        // (closeExternallyExecutedSingletonApp), so that the design environment takes over.
        if (own is not null)
        {
            interpreter.RunDestructor(own);
        }
        else if (singleInstance)
        {
            foreach ((JgsValue running, string file) in RunningApps().ToList())
            {
                if (SameFile(file, fullFileName))
                {
                    interpreter.RunDestructor(running.AsObject);
                }
            }
        }

        remember(null);
        var before = new HashSet<JgsObject>(RunningApps().Select(static r => r.App.AsObject), ReferenceEqualityComparer.Instance);

        // AppManagementService.runApp: evalin('base', 'Name(arguments);') - the instance answered, nothing bound.
        bool none = DesignRunArguments.IsNoArguments(arguments);
        try
        {
            JgsValue app = interpreter.EvaluateSourceIn($"{name}({(none ? string.Empty : arguments)})", interpreter.Globals, 0, 0);
            remember(app.Type == JgsType.Object ? app : null);
            return app;
        }
        catch (JgsRuntimeException ex)
        {
            // What this run made is the designer's running app whether or not the constructor got to
            // the end (getRunningApp: the app registered to a figure of this file).
            JgsValue? made = RunningApps().FirstOrDefault(r => SameFile(r.File, fullFileName) && !before.Contains(r.App.AsObject)).App;
            remember(made);

            // DesktopAppRunner.fireAppError, isBadNumberOfArguments: a startup function that wanted
            // arguments Run did not give - the error raised by the startup function itself - leaves a
            // half-made app, which App Designer deletes, and the error is its own, the cause under it
            // (TooFewAppArgumentsError). An error from deeper, or before the app was registered, is
            // reported as it is.
            if (made is not null && ex.Identifier == "MATLAB:minrhs" && RaisedByStartup(ex, made.AsObject))
            {
                interpreter.RunDestructor(made.AsObject);
                remember(null);
                throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:TooFewAppArgumentsError",
                    "Cannot run with provided input arguments.\nCaused by: " + ex.Message);
            }

            // fireAppError: too many for the startup function, or an error evaluating an argument -
            // not in the startup function, nor naming the app's file, which is a syntax error in it -
            // are the arguments' fault, and Run's drop-down says so on its text field.
            if (made is not null && ex.Identifier is "MATLAB:TooManyInputs" or "MATLAB:maxrhs" && RaisedByStartup(ex, made.AsObject))
            {
                interpreter.RunDestructor(made.AsObject);
                remember(null);
                throw new JgsRuntimeException(0, 0, DesignRunOutcome.ArgumentError, ex.Message);
            }

            if (!none && !ex.Message.Contains(fileName, StringComparison.Ordinal)
                && !ex.Frames.Any(static f => f.Name.Contains("startupFcn", StringComparison.Ordinal)))
            {
                throw new JgsRuntimeException(0, 0, DesignRunOutcome.ArgumentError, ex.Message);
            }

            throw;
        }
    }

    /// <summary>Whether the file's class derives from <c>matlab.ui.componentcontainer.ComponentContainer</c> directly (App Designer runs such a file as a component).</summary>
    private static bool IsComponentFile(string fullFileName)
    {
        try
        {
            return DesignComponentMetadata.Superclasses(JgsMlapp.ReadSource(fullFileName), out _).Contains(DesignComponentMetadata.Superclass, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// App Designer's Run of a custom UI component (App Designer window plan W15; R2025b's
    /// <c>UserComponentRunner.run</c> and <c>UserComponentInstantiator.run</c>, measured by the probe
    /// <c>w15_component</c>): the one it launched before is closed by deleting its parent figure
    /// (<c>UserComponentInstanceCloser</c>); the class is cleared and the component's folder put at the
    /// front of the path; the class is called with no arguments in the base workspace, which makes a
    /// figure for it; that figure is marked <c>IsRunningInAppDesigner</c> (1) and
    /// <c>RunningInstanceFullFileName</c> and sized to the component's far corner; the instance is
    /// answered and not bound.
    /// </summary>
    /// <exception cref="JgsRuntimeException">What the component's class raises, or the missing file's refusal.</exception>
    public static JgsValue RunComponent(Interpreter interpreter, string fullFileName, JgsValue? launched, Action<JgsValue?> remember)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(fullFileName);
        ArgumentNullException.ThrowIfNull(remember);
        fullFileName = Path.GetFullPath(fullFileName);
        (string name, string folder) = CommandOf(fullFileName);
        if (!File.Exists(fullFileName))
        {
            throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:RunFailedFileNotFound",
                $"Error running '{Path.GetFileName(fullFileName)}'.\n{fullFileName} not found.");
        }

        if (launched is { Type: JgsType.Object } before && !before.AsObject.Deleted
            && JgsComponentContainers.TryState(before.AsObject, out JgsComponentContainerState? state) && state.Model.Figure is { BeingDeleted: false } old
            && interpreter.Host is JGraphScriptGlobals closer)
        {
            JgsBuiltins.TryDeleteGraphics(JgsHandleRegistry.For(old), closer);
        }

        remember(null);
        interpreter.FunctionPath?.Unload(name);
        interpreter.FunctionPath?.Add(folder, atEnd: false);
        JgsValue component = interpreter.EvaluateSourceIn(name, interpreter.Globals, 0, 0);
        if (component.Type == JgsType.Object && JgsComponentContainers.TryState(component.AsObject, out JgsComponentContainerState? made) && made.Model.Figure is { } figure
            && JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry))
        {
            entry.AddedProperties ??= new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            entry.AddedProperties["IsRunningInAppDesigner"] = JgsValue.Number(1);
            entry.AddedProperties[JgsBuiltinClasses.RunningInstanceFullFileName] = JgsValue.Str(fullFileName);
            double[]? at = NumbersOf(JgsGraphicsProperties.Get(entry, "Position", 0, 0));
            double[]? own = NumbersOf(JgsGraphicsProperties.Get(made.Entry, "Position", 0, 0));
            if (at is [var x, var y, _, _] && own is [var cx, var cy, var cw, var ch])
            {
                JgsGraphicsProperties.Set(entry, "Position", JgsMatrix.FromColumnMajor([x, y, cx + cw, cy + ch], 1, 4), 0, 0);
            }

            remember(component);
        }

        return component;
    }

    /// <summary>
    /// What the "App Designer Custom UI Component Metadata" dialog learns by making the component once
    /// (<c>MetadataUIViewModel.getAvatar</c> and <c>validateComponent</c>, App Designer window plan W15): the class
    /// at <paramref name="fullFileName"/> constructed with an invisible figure as its parent, from its folder (the
    /// one above its packages) put first on the path; then its <c>Units</c> must be pixels, the figure must hold no
    /// other child than it and its context menus, and none of its public callbacks may hold a value. The figure is
    /// deleted afterwards. An abstract class and a failing construction are the check's error, in R2025b's words.
    /// R2025b also photographs the component for the library's drag preview; JGraph leaves the avatars empty.
    /// </summary>
    public static DesignComponentCheck CheckComponent(Interpreter interpreter, string fullFileName)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(fullFileName);
        (string directory, string className, string componentName) = DesignComponentMetadata.PackageParts(fullFileName);
        interpreter.FunctionPath?.Unload(componentName);
        interpreter.FunctionPath?.Add(directory, atEnd: false);
        var scratch = new JgsEnvironment(interpreter.Globals);
        JgsValue figure = JgsValue.Null;
        try
        {
            interpreter.EvaluateSourceIn("jgMetadataFigure__ = uifigure('Visible', 'off');", scratch, 0, 0);
            scratch.TryGet("jgMetadataFigure__", out figure);
            interpreter.EvaluateSourceIn($"jgMetadataComponent__ = {className}('Parent', jgMetadataFigure__);", scratch, 0, 0);
            if (!scratch.TryGet("jgMetadataComponent__", out JgsValue component) || component.Type != JgsType.Object
                || !JgsComponentContainers.TryState(component.AsObject, out JgsComponentContainerState? state))
            {
                return new DesignComponentCheck(null, $"{className} did not make a custom UI component.");
            }

            if (JgsGraphicsProperties.Get(state.Entry, "Units", 0, 0) is { Type: JgsType.String } units && units.AsString != "pixels")
            {
                return new DesignComponentCheck(null, DesignComponentMetadata.InvalidUnits);
            }

            if (JgsHandleRegistry.TryGet(figure, out JgsHandleEntry? figureEntry))
            {
                foreach (JgsValue child in Elements(JgsGraphicsProperties.Get(figureEntry, "Children", 0, 0)))
                {
                    if (JgsHandleRegistry.TryGet(child, out JgsHandleEntry? childEntry) && !ReferenceEquals(childEntry, state.Entry)
                        && !ReferenceEquals(childEntry.Target, state.Entry.Target)
                        && !(JgsGraphicsProperties.Get(childEntry, "Type", 0, 0) is { Type: JgsType.String } type && type.AsString == "uicontextmenu"))
                    {
                        return new DesignComponentCheck(null, DesignComponentMetadata.RogueComponent);
                    }
                }
            }

            string[] assigned = state.Callbacks.Where(static kv => !IsEmptyCallback(kv.Value)).Select(static kv => kv.Key).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
            if (assigned.Length > 0)
            {
                return new DesignComponentCheck(null, DesignComponentMetadata.CallbackHasValue(assigned.Select(static n => n + "Fcn"), assigned));
            }

            double[]? position = NumbersOf(JgsGraphicsProperties.Get(state.Entry, "Position", 0, 0));
            return position is { Length: 4 } ? new DesignComponentCheck(position, null) : new DesignComponentCheck(null, $"{className} has no Position.");
        }
        catch (JgsRuntimeException ex)
        {
            return new DesignComponentCheck(null, ex.Identifier == "MATLAB:class:abstract" ? DesignComponentMetadata.AbstractComponent(className) : ex.Message);
        }
        finally
        {
            if (figure.Type != JgsType.Null && interpreter.Host is JGraphScriptGlobals host)
            {
                JgsBuiltins.TryDeleteGraphics(figure, host);
            }
        }
    }

    private static IEnumerable<JgsValue> Elements(JgsValue value)
    {
        if (value.Type is JgsType.Number or JgsType.Object)
        {
            yield return value;
        }
        else if (value.Type is JgsType.Array or JgsType.Cell)
        {
            for (int i = 0; i < value.ArrayLength; i++)
            {
                yield return value.ElementAt(i);
            }
        }
    }

    private static bool IsEmptyCallback(JgsValue value) => value.Type switch
    {
        JgsType.Null => true,
        JgsType.Array => value.ArrayLength == 0,
        JgsType.Cell => value.AsCell.Length == 0,
        JgsType.String => value.AsString.Length == 0,
        _ => false,
    };

    /// <summary>A value's numbers as a flat array; null when it is not numbers.</summary>
    private static double[]? NumbersOf(JgsValue value)
    {
        if (value.Type == JgsType.Number)
        {
            return [value.AsNumber];
        }

        if (value.Type != JgsType.Array || value.IsStringArray || value.IsCharMatrix)
        {
            return null;
        }

        var numbers = new double[value.ArrayLength];
        for (int i = 0; i < numbers.Length; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type != JgsType.Number)
            {
                return null;
            }

            numbers[i] = element.AsNumber;
        }

        return numbers;
    }

    /// <summary>
    /// What Run calls an app at <paramref name="fullFileName"/>, and the folder that goes on the path (<c>PathUtil.getAppRunCommandFromFileName</c>, <c>getPathToApp</c>):
    /// its name, with the packages its folders name before it, and the folder above them.
    /// </summary>
    public static (string Command, string Folder) CommandOf(string fullFileName)
    {
        ArgumentNullException.ThrowIfNull(fullFileName);
        string command = Path.GetFileNameWithoutExtension(fullFileName);
        string folder = Path.GetDirectoryName(fullFileName)!;
        while (Path.GetFileName(folder) is { Length: > 1 } part && part[0] == '+' && Path.GetDirectoryName(folder) is { } above)
        {
            command = part[1..] + "." + command;
            folder = above;
        }

        return (command, folder);
    }

    /// <summary>
    /// A picture of the window of the app last run from <paramref name="fullFileName"/> (<c>captureScreenshot</c>: <c>drawnow</c> then <c>getframe</c>), through the same barrier <c>exportapp</c> goes through;
    /// null with no window or no host to take it. A capture that fails says nothing, as it should go unnoticed.
    /// </summary>
    public static byte[]? Capture(JGraphScriptGlobals host, string fullFileName)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fullFileName);
        if (!ScriptEventQueue.PumpInstalled || host.FigureFiles is not { } files)
        {
            return null;
        }

        foreach (GraphObject figure in JgsGraphicsProperties.RootFigures(hidden: true).Reverse())
        {
            if (figure is FigureModel model && !figure.BeingDeleted && JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry)
                && entry.AddedProperties is { } added
                && added.TryGetValue(JgsBuiltinClasses.RunningInstanceFullFileName, out JgsValue? file) && file.Type == JgsType.String
                && SameFile(file.AsString, fullFileName))
            {
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    JG.TouchFigure(model);
                    host.ShowTouchedFigures();
                    ScriptComponentFrames.Flush(force: true);
                    ScriptRenderPump.Flush();
                    return files.CaptureWindow(model, path) && File.Exists(path) ? File.ReadAllBytes(path) : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    return null;
                }
                finally
                {
                    File.Delete(path);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="ex"/> was raised by the app's startup function reading an input Run did
    /// not give, as R2025b's <c>isBadNumberOfArguments</c> reads the stack (the startup function's
    /// frame right under the constructor's, depth 1 for <c>MATLAB:minrhs</c>): the innermost frame a
    /// method of the app's class that declares inputs beyond <c>app</c>, entered from the constructor.
    /// An anonymous function pushes no frame here, so one the startup calls short of arguments is told
    /// apart by the startup's own parameters, which an error of its own would need.
    /// </summary>
    private static bool RaisedByStartup(JgsRuntimeException ex, JgsObject app)
    {
        if (ex.Frames.Count < 2)
        {
            return false;
        }

        string className = app.Class.Name;
        string Method(string frame) => frame.StartsWith(className + ".", StringComparison.Ordinal) ? frame[(className.Length + 1)..] : frame;
        string inner = Method(ex.Frames[0].Name);
        return inner != className && Method(ex.Frames[1].Name) == className
            && app.Class.TryMethod(inner, out ClassMethod? method) && !method.Static && method.Function.Parameters.Count > 1;
    }

    /// <summary>Every running app, newest figure first, with the file its class came from.</summary>
    private static IEnumerable<(JgsValue App, string File)> RunningApps()
    {
        foreach (GraphObject figure in JgsGraphicsProperties.RootFigures(hidden: true).Reverse())
        {
            if (!figure.BeingDeleted && JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry)
                && entry.AddedProperties is { } added
                && added.TryGetValue(JgsBuiltinClasses.RunningAppInstance, out JgsValue? running)
                && running.Type == JgsType.Object && !running.AsObject.Deleted
                && added.TryGetValue(JgsBuiltinClasses.RunningInstanceFullFileName, out JgsValue? file) && file.Type == JgsType.String)
            {
                yield return (running, file.AsString);
            }
        }
    }

    /// <summary>Whether two file names name one file, case blind as Windows is, whatever their slashes.</summary>
    private static bool SameFile(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
