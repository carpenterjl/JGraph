namespace JGraph.Scripting.Jgs;

/// <summary>
/// compiler.build's classes (compiler and SDK plan, stage K6): <c>compiler.build.StandaloneApplicationOptions</c>,
/// <c>compiler.build.Results</c> and <c>compiler.runtime.Dependencies</c>, value classes declared here
/// as <c>uistyle</c>'s Style is, their properties in R2025b's order and each checked when set as
/// R2025b's class files check it (toolbox/compiler/cli/build and toolbox/compiler/utils, read in
/// place; the messages from resources/Compiler/en). The checks themselves are
/// <see cref="JgsBuiltins"/>' compiler options.
/// </summary>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The standalone options class.</summary>
    public const string StandaloneOptions = "compiler.build.StandaloneApplicationOptions";

    /// <summary>The build results class.</summary>
    public const string BuildResults = "compiler.build.Results";

    /// <summary>The runtime dependencies class.</summary>
    public const string RuntimeDependencies = "compiler.runtime.Dependencies";

    /// <summary>The .NET assembly options class (stage K8).</summary>
    public const string DotNetOptions = "compiler.build.DotNETAssemblyOptions";

    /// <summary>The .NET options' public properties, in the order R2025b lists them (k8_build: main.options).</summary>
    public static readonly string[] DotNetOptionNames =
    [
        "ClassMap", "DebugBuild", "EnableRemoting", "FrameworkVersion", "SampleGenerationFiles", "StrongNameKeyFile", "AssemblyName",
        "AssemblyVersion", "EmbedArchive", "Interface", "AdditionalFiles", "AutoDetectDataFiles", "ExternalEncryptionKey", "ObfuscateArchive",
        "SecretsManifest", "SupportPackages", "Verbose", "OutputDir",
    ];

    /// <summary>Where a name given for <c>AssemblyName</c> is kept (R2025b's UserProvidedAssemblyName).</summary>
    public const string GivenAssemblyName = "UserProvidedAssemblyName";

    /// <summary>R2025b's InterfaceManager: the interface, its version and EmbedArchive, kept together because setting one warns about the others.</summary>
    public const string InterfaceValue = "InterfaceManagerInterface";

    /// <summary>The interface manager's version: <c>AssemblyVersion</c>.</summary>
    public const string InterfaceVersion = "InterfaceManagerVersion";

    /// <summary>The interface manager's <c>EmbedArchive</c>.</summary>
    public const string InterfaceEmbed = "InterfaceManagerEmbedArchive";

    /// <summary>The options' public properties, in the order R2025b lists them (k6_build, api_m.options).</summary>
    public static readonly string[] StandaloneOptionNames =
    [
        "ExecutableName", "CustomHelpTextFile", "EmbedArchive", "ExecutableIcon", "ExecutableSplashScreen", "ExecutableVersion",
        "AppFile", "TreatInputsAsNumeric", "AdditionalFiles", "AutoDetectDataFiles", "ExternalEncryptionKey", "ObfuscateArchive",
        "SecretsManifest", "SupportPackages", "Verbose", "OutputDir",
    ];

    /// <summary>JGraph's own option, hidden so the display stays R2025b's: where the program's runtime comes from.</summary>
    public const string RuntimeDeliveryOption = "RuntimeDelivery";

    /// <summary>Where a name given for <c>ExecutableName</c> is kept; the property itself is derived (R2025b's UserProvidedExecutableName).</summary>
    public const string GivenExecutableName = "UserProvidedExecutableName";

    /// <summary>Where a folder given for <c>OutputDir</c> is kept (R2025b's OutputDirManager).</summary>
    public const string GivenOutputDir = "UserProvidedOutputDir";

    private static ClassdefStmt StandaloneOptionsDeclaration(Interpreter interpreter)
    {
        var properties = new List<ClassProperty>();
        var methods = new List<ClassMethod>();
        foreach (string name in StandaloneOptionNames)
        {
            bool derived = name is "ExecutableName" or "OutputDir";
            properties.Add(new ClassProperty(new ArgumentSpec(name, null, null, [], new PreEvaluated(JgsBuiltins.StandaloneOptionDefault(name))),
                Constant: false, Dependent: derived));
            methods.Add(Setter(name, interpreter));
            if (derived)
            {
                string captured = name;
                methods.Add(new ClassMethod(new FnStmt("get." + name, ["obj"], [], ["value"]) { Dialect = JgsDialect.Matlab }, Static: false)
                {
                    Native = new BuiltinFunction("get." + name, (args, line, col) => JgsBuiltins.StandaloneOptionDerived(captured, args[0].AsObject)),
                });
            }
        }

        var hidden = new MemberAccess(MemberAccessKind.Protected);
        properties.Add(new ClassProperty(new ArgumentSpec(GivenExecutableName, null, null, [], new PreEvaluated(JgsValue.Str(""))), Constant: false)
        {
            GetAccess = hidden,
            SetAccess = hidden,
            Hidden = true,
        });
        properties.Add(new ClassProperty(new ArgumentSpec(GivenOutputDir, null, null, [], new PreEvaluated(JgsValue.Str(""))), Constant: false)
        {
            GetAccess = hidden,
            SetAccess = hidden,
            Hidden = true,
        });
        properties.Add(new ClassProperty(new ArgumentSpec(RuntimeDeliveryOption, null, null, [], new PreEvaluated(JgsValue.Str("installed"))), Constant: false)
        {
            Hidden = true,
        });
        methods.Add(Setter(RuntimeDeliveryOption, interpreter));
        return new ClassdefStmt(StandaloneOptions, isHandle: false, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
        };
    }

    /// <summary><c>obj.Name = value</c>: the value checked, in a copy of the object, as a value class's set method answers.</summary>
    private static ClassMethod Setter(string name, Interpreter interpreter) =>
        Setter(name, (copy, value, line, col) => JgsBuiltins.SetStandaloneOption(interpreter, copy, name, value, line, col));

    /// <summary>A set method that checks and stores through <paramref name="set"/>, in a copy of the object.</summary>
    private static ClassMethod Setter(string name, Action<JgsObject, JgsValue, int, int> set) =>
        new(new FnStmt("set." + name, ["obj", "value"], [], ["obj"]) { Dialect = JgsDialect.Matlab }, Static: false)
        {
            Native = new BuiltinFunction("set." + name, (args, line, col) =>
            {
                if (args.Count < 2 || args[0].Type != JgsType.Object)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                JgsObject copy = Copied(args[0].AsObject);
                set(copy, args[1], line, col);
                return JgsValue.Object(copy);
            })
            {
                KeepsStringArguments = true,
            },
        };

    /// <summary>
    /// <c>compiler.build.DotNETAssemblyOptions</c> (stage K8): R2025b's properties in its order, the
    /// assembly name, version, EmbedArchive, interface and output folder dependent on what was given,
    /// each set checked by <see cref="JgsBuiltins.SetDotNetOption"/>.
    /// </summary>
    private static ClassdefStmt DotNetOptionsDeclaration(Interpreter interpreter)
    {
        var properties = new List<ClassProperty>();
        var methods = new List<ClassMethod>();
        foreach (string name in DotNetOptionNames)
        {
            bool derived = name is "AssemblyName" or "AssemblyVersion" or "EmbedArchive" or "Interface" or "OutputDir";
            properties.Add(new ClassProperty(new ArgumentSpec(name, null, null, [], new PreEvaluated(JgsBuiltins.DotNetOptionDefault(name))),
                Constant: false, Dependent: derived));
            string captured = name;
            methods.Add(Setter(name, (copy, value, line, col) =>
                JgsBuiltins.SetDotNetOption(interpreter, interpreter.Host as JGraphScriptGlobals, copy, captured, value, line, col)));
            if (derived)
            {
                methods.Add(new ClassMethod(new FnStmt("get." + name, ["obj"], [], ["value"]) { Dialect = JgsDialect.Matlab }, Static: false)
                {
                    Native = new BuiltinFunction("get." + name, (args, line, col) => JgsBuiltins.DotNetOptionDerived(captured, args[0].AsObject)),
                });
            }
        }

        // R2025b's hidden getName: the assembly name's last part, which names the dll and the output folder.
        methods.Add(new ClassMethod(new FnStmt("getName", ["obj"], [], ["name"]) { Dialect = JgsDialect.Matlab }, Static: false)
        {
            Hidden = true,
            Native = new BuiltinFunction("getName", (args, line, col) => JgsValue.Str(JgsBuiltins.DotNetShortName(args[0].AsObject))),
        });

        var hidden = new MemberAccess(MemberAccessKind.Protected);
        foreach ((string field, JgsValue initial) in new[]
        {
            (GivenAssemblyName, JgsValue.Str("")), (GivenOutputDir, JgsValue.Str("")), (InterfaceValue, JgsValue.Str("mwarray")),
            (InterfaceVersion, JgsValue.Str("")), (InterfaceEmbed, OnOffSwitchStateValue.Of(true)),
        })
        {
            properties.Add(new ClassProperty(new ArgumentSpec(field, null, null, [], new PreEvaluated(initial)), Constant: false)
            {
                GetAccess = hidden,
                SetAccess = hidden,
                Hidden = true,
            });
        }

        return new ClassdefStmt(DotNetOptions, isHandle: false, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
        };
    }

    /// <summary>A value object's copy, every field shared, for a set method to change.</summary>
    internal static JgsObject Copied(JgsObject source)
    {
        var copy = new JgsObject(source.Class);
        foreach ((string field, JgsValue held) in source.Fields)
        {
            copy.Fields[field] = held;
        }

        return copy;
    }

    /// <summary>
    /// <c>compiler.build.Results</c>: what a build made, read-only. <c>BuildID</c> and
    /// <c>BuildDuration</c> are hidden, as R2025b's are.
    /// </summary>
    private static ClassdefStmt BuildResultsDeclaration()
    {
        var readOnly = new MemberAccess(MemberAccessKind.Private);
        ClassProperty Stored(string name, bool hidden = false) =>
            new(new ArgumentSpec(name, null, null, [], new PreEvaluated(JgsMatrix.FromColumnMajor([], 0, 0))), Constant: false)
            {
                SetAccess = readOnly,
                Hidden = hidden,
            };

        return new ClassdefStmt(BuildResults, isHandle: false,
            [Stored("BuildType"), Stored("Files"), Stored("IncludedSupportPackages"), Stored("Options"), Stored("RuntimeDependencies"),
                Stored("BuildID", hidden: true), Stored("BuildDuration", hidden: true)],
            [])
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
        };
    }

    /// <summary><c>compiler.runtime.Dependencies</c>: the runtime products a build needs, as two tables.</summary>
    private static ClassdefStmt RuntimeDependenciesDeclaration()
    {
        var readOnly = new MemberAccess(MemberAccessKind.Private);
        ClassProperty Stored(string name) =>
            new(new ArgumentSpec(name, null, null, [], new PreEvaluated(JgsMatrix.FromColumnMajor([], 0, 0))), Constant: false)
            {
                SetAccess = readOnly,
            };

        return new ClassdefStmt(RuntimeDependencies, isHandle: false, [Stored("Required"), Stored("Optional")], [])
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
        };
    }
}
