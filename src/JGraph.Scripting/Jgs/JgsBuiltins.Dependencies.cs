using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The dependency builtins (compiler and SDK plan, stage K2): <c>matlab.codetools.requiredFilesAndProducts</c>,
/// the build's dependency analysis (<see cref="JgsDependencyAnalyzer"/>) asked from a session, and
/// <c>compiler.codetools.deployableSupportPackages</c>, which answers no support package because
/// JGraph has none. Their answers and refusals are R2025b's, recorded by the fixture
/// <c>k2_required_files</c> and probe_k2_depend.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterDependencyBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> DependencyBuiltinNames { get; } =
    [
        "matlab.codetools.requiredFilesAndProducts", "compiler.codetools.deployableSupportPackages",
    ];

    private const string RequiredFilesName = "matlab.codetools.requiredFilesAndProducts";

    /// <summary>Declares the dependency builtins; they read the session's folders and path when called.</summary>
    internal static void RegisterDependencyBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register(RequiredFilesName, JgsValue.Function(new BuiltinFunction(RequiredFilesName,
            (args, line, col) => RequiredFiles(env, interpreter, host, args, 1, line, col)[0])
        {
            KeepsStringArguments = true,
            MultiOutput = (args, wanted, line, col) => RequiredFiles(env, interpreter, host, args, wanted, line, col),
        }));

        env.Builtins.Register("compiler.codetools.deployableSupportPackages", JgsValue.Function(new BuiltinFunction(
            "compiler.codetools.deployableSupportPackages", (args, line, col) =>
            {
                ArityRange("compiler.codetools.deployableSupportPackages", args, 1, 1, line, col);
                foreach (string name in DependencyNames(args[0], line, col))
                {
                    if (DependencyRoot(name, interpreter, host) is null && !IsBuiltinName(env, name))
                    {
                        throw new JgsRuntimeException(line, col, "Compiler:utils:shared:cannotFindFile", $"Cannot find file:\n\n{name}");
                    }
                }

                return JgsValue.StringArray([], 0, 0);
            })
        {
            KeepsStringArguments = true,
        }));
    }

    /// <summary>
    /// <c>[fList, pList] = matlab.codetools.requiredFilesAndProducts(files, 'toponly')</c>: the user
    /// files the named files need, ordered by full path as a 1-by-n cell (0-by-0 when none), and the
    /// products as a 1-by-m struct of Name, Version, ProductNumber and Certain, MATLAB first.
    /// </summary>
    private static JgsValue[] RequiredFiles(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        if (wanted > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        IReadOnlyList<string> names = DependencyNames(args[0], line, col);
        bool topOnly = false;
        if (args.Count == 2)
        {
            string flag = IsTextScalar(args[1]) ? TextOf(args[1]) : "";
            if (!flag.Equals("toponly", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:requiredFilesAndProducts:BadStringFlag",
                    $"Flag \"{flag}\" is not recognized. Use a valid flag.");
            }

            topOnly = true;
        }

        var roots = new List<string>();
        var builtinProducts = new List<int>();
        foreach (string name in names)
        {
            if (DependencyRoot(name, interpreter, host) is { } root)
            {
                roots.Add(root);
            }
            else if (Path.GetExtension(name).Length == 0 && IsBuiltinName(env, name))
            {
                builtinProducts.AddRange(DeploymentRules.ProductsOf(name));
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:depfun:req:NameNotFound",
                    $"File, function or class \"{name}\" may not exist. Neither WHICH nor EXIST could find an exact, "
                    + "case-sensitive match. Please check the spelling of the name, and that any required directories "
                    + "are on the MATLAB path.");
            }
        }

        DependencyReport report = AnalyzeDependencies(env, interpreter, host, roots, topOnly);
        JgsValue[] files = report.Files.Select(f => JgsValue.Str(f.Path)).ToArray();
        JgsValue fileList = JgsValue.Cell(files);
        fileList.Reshape(files.Length == 0 ? 0 : 1, files.Length);
        if (wanted < 2)
        {
            return [fileList];
        }

        var numbers = report.Products.Select(p => p.Number).ToList();
        foreach (int number in builtinProducts)
        {
            if (!numbers.Contains(number)) numbers.Add(number);
        }

        Dictionary<string, JgsValue>[] products = numbers
            .Select(DeploymentRules.Product).OfType<RuntimeProduct>()
            .Select(p => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(p.Name),
                ["Version"] = JgsValue.Str(p.Version),
                ["ProductNumber"] = JgsValue.Number(p.Number),
                ["Certain"] = JgsValue.Bool(true),
            })
            .ToArray();
        return [fileList, JgsValue.StructArray(products)];
    }

    /// <summary>
    /// Runs the dependency analysis over <paramref name="roots"/> from the session: its current
    /// folder, then its path, and its built-ins as what the runtime ships.
    /// </summary>
    internal static DependencyReport AnalyzeDependencies(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<string> roots, bool topOnly)
    {
        var options = new DependencyOptions
        {
            CurrentFolder = host.CurrentDirectory,
            SearchPath = interpreter.FunctionPath?.Folders ?? [],
            TopOnly = topOnly,
        };
        return new JgsDependencyAnalyzer(options, name => IsBuiltinName(env, name)).Analyze(roots);
    }

    private static bool IsBuiltinName(JgsEnvironment env, string name) =>
        env.Builtins.TryGet(name, out JgsValue value) && value.Type == JgsType.Function;

    /// <summary>
    /// The names <c>requiredFilesAndProducts</c> is given: a char row, a string array or a cell of
    /// char rows and string scalars, in R2025b's refusals otherwise.
    /// </summary>
    private static IReadOnlyList<string> DependencyNames(JgsValue files, int line, int col)
    {
        if (files.Type == JgsType.Cell)
        {
            var names = new List<string>();
            var bad = new List<int>();
            JgsValue[] cells = files.AsCell;
            for (int i = 0; i < cells.Length; i++)
            {
                if (IsTextScalar(cells[i]))
                {
                    names.Add(TextOf(cells[i]));
                }
                else
                {
                    bad.Add(i + 1);
                }
            }

            if (bad.Count > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:requiredFilesAndProducts:NameMustBeChar",
                    $"Invalid element(s) found in the first input argument: {string.Join(", ", bad.Select(b => "#" + b))}. "
                    + "Use strings, a row character vector or a cell array of row character vectors for file names.");
            }

            return names;
        }

        if (TextElementsOf(files) is { } texts && (files.IsStringArray || IsTextScalar(files)))
        {
            return texts;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:matlab:codetools:requiredFilesAndProducts:invalidType",
            $"Expected input number 1, files, to be one of these types:\n\nchar, cell, string\n\nInstead its type was {ClassOf(files, JgsDialect.Matlab)}.");
    }

    /// <summary>
    /// The file or folder a name given to <c>requiredFilesAndProducts</c> means, as <c>which</c> and
    /// <c>exist</c> would find it: a folder or file as written (relative to the current folder), a
    /// code file named with or without its extension on the path, or a file of that name on the path.
    /// </summary>
    private static string? DependencyRoot(string name, Interpreter interpreter, JGraphScriptGlobals host)
    {
        if (name.Length == 0)
        {
            return null;
        }

        string asWritten = Path.GetFullPath(Path.IsPathRooted(name) ? name : Path.Combine(host.CurrentDirectory, name));
        if (Directory.Exists(asWritten) || File.Exists(asWritten))
        {
            return asWritten;
        }

        if (interpreter.FunctionPath is { } path && name.IndexOfAny(['/', '\\']) < 0)
        {
            if (path.FindFile(name) is { } code)
            {
                return Path.GetFullPath(code);
            }

            foreach (string folder in path.Folders)
            {
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }
}
