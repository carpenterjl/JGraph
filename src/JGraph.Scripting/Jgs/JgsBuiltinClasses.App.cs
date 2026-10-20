using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting.Designer;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.apps.App</c>, the class an app in App Designer's plain-text format is written under
/// (designer plan DS7a). R2025b's class (<c>runtime/+matlab/+apps/App.m</c>) is a constructor that
/// hands the object to <c>AppManagementService.initializeMApp</c>: it reads the app's file, cuts the
/// <c>%[app:layout]</c> appendix out of it, generates a function that makes the components from the
/// XML - each component by its informal interface with its properties, a callback wired to the
/// method it names - runs it, registers the app with its figure, and runs the startup function the
/// run configuration names with the constructor's arguments; an app whose run configuration says
/// <c>SingleRunningInstance</c> answers the instance already running instead, its figure focused.
/// Here the appendix is read by <see cref="DesignPlainText"/> into the design, the components are
/// made by the same generated lines an <c>.mlapp</c>'s <c>createComponents</c> holds
/// (<see cref="DesignCodeWriter.Method"/>), and the registration, the callbacks and the startup
/// function are <c>matlab.apps.AppBase</c>'s (U7).
/// </summary>
/// <remarks>
/// R2025b refuses to run such an app unless its feature <c>AppDesignerPlainTextFileFormat</c> is on
/// (<c>MATLABApp:unknownFileType</c>, "Unknown FileType"); this build runs it as it is, which is
/// recorded as a divergence.
/// </remarks>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The class a plain-text app inherits from.</summary>
    public const string AppClass = "matlab.apps.App";

    private static ClassdefStmt AppDeclaration(Interpreter interpreter) =>
        new(AppClass, isHandle: true, [],
        [
            new ClassMethod(new FnStmt(AppClass, ["varargin"], [], ["app"]) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction(AppClass, (args, line, col) => ConstructApp(interpreter, args, line, col))
                {
                    KeepsStringArguments = true,
                },
            },
            Method("delete", MemberAccess.Public, isSealed: false, (args, line, col) => DeleteApp(interpreter, args, line, col), bindsAns: false),
            Method("saveobj", MemberAccess.Public, isSealed: false, (args, line, col) => SaveObj(interpreter, args, line, col)),
            new ClassMethod(new FnStmt("loadobj", ["s"], [], []) { Dialect = JgsDialect.Matlab }, Static: true)
            {
                Native = new BuiltinFunction("loadobj", static (_, line, col) => throw new JgsRuntimeException(line, col,
                    "MATLAB:appdesigner:appdesigner:LoadObjWarning",
                    $"Unable to load App Designer app object. Load not supported for {AppBase} objects.")),
            },
        ])
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = ["handle"],
            Abstract = true,
        };

    /// <summary>
    /// The constructor: the app's components from its file's appendix, the registration, the
    /// startup function with the arguments; or the running instance of a single-instance app.
    /// </summary>
    private static JgsValue ConstructApp(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsValue self = args[0];
        JgsObject instance = self.AsObject;

        // The class's file; the batch file's when the class is the file being run (its source id is empty).
        string file = interpreter.FileOfCode(interpreter.FileOfClass(instance.Class));
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:AppAppendixMissing",
                $"The app file '{file}' could not be read: {ex.Message}");
        }

        // Refused as R2025b refuses it (plaintext-matlab spec, section 5): layout XML that is not
        // well-formed with the parser's message, a missing layout or figure with its fixed message;
        // and, going past it, an appendix the reader could not take whole (an element with no
        // name, a name used twice, a value it does not read), rather than an app with parts missing.
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        if (read.Read.Problems.FirstOrDefault(static p => p.StartsWith(DesignPlainText.MalformedXmlProblem, StringComparison.Ordinal)) is { } malformed)
        {
            throw new JgsRuntimeException(line, col, "MATLABApp:appendixError", malformed[DesignPlainText.MalformedXmlProblem.Length..]);
        }

        if (read.Read.Design is not { } design)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:AppAppendixException", "Unable to process app layout due to incorrect XML.");
        }

        if (read.Read.Problems.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:AppAppendixException",
                $"Unable to process app layout due to incorrect XML. {string.Join(" ", read.Read.Problems)}");
        }

        string folder = Path.GetDirectoryName(file) ?? string.Empty;
        foreach (DesignComponent component in design.Components)
        {
            // An element of a class the catalog does not know is a custom component, made by a call
            // of its class; a name that is no class is refused as R2025b refuses it.
            if (component.Class is null && !interpreter.KnowsClass(component.Type))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:AppAppendixUnknownComponent",
                    $"Unrecognized component: '{component.Type}'.");
            }

            // An image named in quotes that the internal data does not list is found beside the app
            // when it is there, as the generator takes it (exist on the path, else fullfile with the
            // app's folder); a preset icon or a file on the path stays the name.
            foreach (string key in (string[])["ImageSource", "Icon"])
            {
                if (component.Get(key) is CharValue { Text: { Length: > 0 } named } && !Path.IsPathRooted(named)
                    && named.Contains('.', StringComparison.Ordinal) && File.Exists(Path.Combine(folder, named)))
                {
                    component.Set(key, new ImageFileValue(named.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)));
                }
            }
        }

        (string? startup, bool singleton) = DesignPlainText.RunConfiguration(text);
        if (singleton && GetRunningApp([self], line, col) is { Type: JgsType.Object } running)
        {
            // R2025b focuses the running instance's figure and answers it.
            if (FigureOf(running.AsObject) is { Target: FigureModel model } && JG.GetFigureNumber(model) is > 0 and var number)
            {
                JG.Figure(number);
            }

            return running;
        }

        // The components as createComponents makes them, then the registration and the startup
        // function, in a function closed over a scope that carries AppBase's methods and the class's
        // access, so that a callback's @Method and the startup call reach the class's private methods.
        var lines = new List<string> { "function ad_init(app, ad_args)" };
        IReadOnlyList<string> method = DesignCodeWriter.Method(design, read.Read.Style);
        foreach (string l in method.Skip(2).SkipLast(1))
        {
            lines.Add(l.Trim() == DesignCodeReader.PathToAppLine
                ? $"{DesignValueText.PathToApp} = '{folder.Replace("'", "''", StringComparison.Ordinal)}';"
                : l);
        }

        lines.Add($"registerApp(app, app.{design.Figure.CodeName})");
        if (startup is not null)
        {
            lines.Add($"runStartupFcn(app, @(app) {startup}(app, ad_args{{:}}))");
        }

        lines.Add("end");
        // Parsed under a file of its own beside the app, as R2025b runs it from a generated
        // ad_<hash>.m in its cache: its line numbers are not the class file's, so a breakpoint or an
        // error location in the app file never lands on a generated line.
        string source = string.Join('\n', lines);
        string generated = Path.Combine(folder, "ad_" + instance.Class.Name + ".m");
        FnStmt init;
        try
        {
            init = Parser.Parse(source, generated, JgsDialect.Matlab).OfType<FnStmt>().First();
        }
        catch (JgsException ex)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:appdesigner:appdesigner:AppAppendixComponentException",
                $"The components of '{Path.GetFileName(file)}' could not be made: {ex.Message}");
        }

        JgsEnvironment scope = interpreter.NewFileScope();
        scope.OwnClass = instance.Class;
        scope.DeclareFunction("createCallbackFcn", JgsValue.Function(new BuiltinFunction("createCallbackFcn", (a, l, c) => CreateCallbackFcn(interpreter, a, l, c)) { KeepsStringArguments = true }));
        scope.DeclareFunction("registerApp", JgsValue.Function(new BuiltinFunction("registerApp", (a, l, c) => RegisterApp(interpreter, a, l, c)) { BindsAnsAsStatement = false }));
        scope.DeclareFunction("runStartupFcn", JgsValue.Function(new BuiltinFunction("runStartupFcn", (a, l, c) => RunStartupFcn(interpreter, a, l, c)) { BindsAnsAsStatement = false }));
        var function = new UserFunction(init, scope, interpreter) { Owner = instance.Class, OwnerClass = instance.Class.Name };
        function.Call([self, JgsValue.Cell(args.Skip(1).ToArray())], line, col);
        return self;
    }

    /// <summary><c>delete(app)</c>: the app's figure is deleted, which takes the app with it (<c>AppManagementService.unregisterMApp</c>).</summary>
    private static JgsValue DeleteApp(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject app = Receiver("delete", args, line, col);
        if (FigureOf(app) is { Target: { BeingDeleted: false } target } && interpreter.Host is { } host)
        {
            JgsBuiltins.TryDeleteGraphics(JgsHandleRegistry.For(target), host);
        }

        return JgsValue.Null;
    }
}
