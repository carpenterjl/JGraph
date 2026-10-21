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
    public static JgsValue Run(Interpreter interpreter, string fullFileName, bool singleInstance, JgsValue? launched, Action<JgsValue?> remember)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(fullFileName);
        ArgumentNullException.ThrowIfNull(remember);
        fullFileName = Path.GetFullPath(fullFileName); // as the session names the file (a command line may say it with forward slashes)
        string name = Path.GetFileNameWithoutExtension(fullFileName);
        string fileName = Path.GetFileName(fullFileName);
        if (!File.Exists(fullFileName))
        {
            throw new JgsRuntimeException(0, 0, "MATLAB:appdesigner:appdesigner:RunFailedFileNotFound",
                $"Error running '{fileName}'.\n{fullFileName} not found.");
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
        string folder = Path.GetDirectoryName(fullFileName)!;
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

        // AppManagementService.runApp: evalin('base', 'Name();') - the instance answered, nothing bound.
        try
        {
            JgsValue app = interpreter.EvaluateSourceIn($"{name}()", interpreter.Globals, 0, 0);
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

            throw;
        }
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
