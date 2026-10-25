using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The functions and classes of a MATLAB Data API build as R2025b's strongly typed generator reads
/// them (stage K9, ADR 0265): each function's inputs and outputs from its header and
/// <c>arguments</c> blocks (MATLABInfo, ArgumentTpl), each class's public properties, constructor
/// and methods in <c>meta.class</c>'s order, which k9_build's meta rows measured: each
/// <c>methods</c> block's methods last first, the blocks in file order, then a handle class's
/// <c>eq ne lt gt le ge delete isvalid</c> where the class defines none of its own.
/// </summary>
internal static class TypedInterfaceReader
{
    /// <summary>handle's methods that R2025b's generator wraps, with their arguments as handle declares them.</summary>
    private static readonly (string Name, string[] Inputs, string[] Outputs)[] HandleMethods =
    [
        ("eq", ["A", "B"], ["TF"]), ("ne", ["A", "B"], ["TF"]), ("lt", ["A", "B"], ["TF"]), ("gt", ["A", "B"], ["TF"]),
        ("le", ["A", "B"], ["TF"]), ("ge", ["A", "B"], ["TF"]), ("delete", ["obj"], []), ("isvalid", ["obj"], ["validity"]),
    ];

    /// <summary>Reads <paramref name="files"/>; a file that does not parse is left out, as it would fail when called.</summary>
    public static (List<TypedFunction> Functions, List<TypedClass> Classes) Read(IEnumerable<string> files)
    {
        var functions = new List<TypedFunction>();
        var classes = new List<TypedClass>();
        foreach (string file in files)
        {
            IReadOnlyList<Stmt> statements;
            try
            {
                statements = Parser.Parse(File.ReadAllText(file), file, JgsDialect.Matlab);
            }
            catch (JgsException)
            {
                continue;
            }

            string prefix = PackagePrefix(file);
            if (statements.OfType<ClassdefStmt>().FirstOrDefault() is { } cls)
            {
                classes.Add(Class(cls, prefix));
            }
            else if (statements.OfType<FnStmt>().FirstOrDefault() is { } function)
            {
                (List<TypedArgument> inputs, List<TypedArgument> outputs) = Arguments(function);
                functions.Add(new TypedFunction(prefix + Path.GetFileNameWithoutExtension(file), inputs, outputs));
            }
        }

        return (functions, classes);
    }

    /// <summary>A file's function or class by the name a caller gives it: <c>pkg.name</c> in a <c>+pkg</c> folder.</summary>
    public static string FullName(string file) => PackagePrefix(file) + Path.GetFileNameWithoutExtension(file);

    /// <summary>The package a file's <c>+folders</c> put it in, as <c>pkg.</c>, or empty.</summary>
    private static string PackagePrefix(string file)
    {
        var parts = new List<string>();
        for (DirectoryInfo? folder = new FileInfo(file).Directory; folder is not null && folder.Name.StartsWith('+'); folder = folder.Parent)
        {
            parts.Insert(0, folder.Name[1..]);
        }

        return parts.Count == 0 ? string.Empty : string.Join(".", parts) + ".";
    }

    /// <summary>A function's or method's inputs and outputs, every one typed by its <c>arguments</c> line when it has one.</summary>
    private static (List<TypedArgument> Inputs, List<TypedArgument> Outputs) Arguments(FnStmt function)
    {
        ArgumentsSignature? signature = function.Signature;
        var inputs = new List<TypedArgument>();
        foreach (string parameter in function.Parameters)
        {
            if (parameter == "varargin" || signature?.Repeating.Contains(parameter, StringComparer.Ordinal) == true)
            {
                inputs.Add(new TypedArgument(parameter) { Kind = TypedArgumentKind.Repeating });
                continue;
            }

            List<ArgumentSpec> named = signature?.NameValue.Where(s => s.Structure == parameter).ToList() ?? [];
            if (named.Count > 0)
            {
                inputs.AddRange(named.Select(static s => Typed(s) with { Kind = TypedArgumentKind.NameValue, Optional = true }));
                continue;
            }

            ArgumentSpec? spec = InputSpec(function, parameter);
            inputs.Add(spec is null ? new TypedArgument(parameter) : Typed(spec) with { Optional = spec.Default is not null });
        }

        var outputs = new List<TypedArgument>();
        foreach (string output in function.Outputs)
        {
            ArgumentSpec? spec = signature?.Outputs.FirstOrDefault(s => s.Name == output);
            outputs.Add(output == "varargout"
                ? new TypedArgument(output) { Kind = TypedArgumentKind.Repeating }
                : spec is null ? new TypedArgument(output) : Typed(spec));
        }

        return (inputs, outputs);
    }

    /// <summary>The input block's line for a positional parameter, or null.</summary>
    private static ArgumentSpec? InputSpec(FnStmt function, string parameter)
    {
        foreach (Stmt statement in function.Body)
        {
            if (statement is not ArgumentsStmt block)
            {
                break;
            }

            if (block.Kind == ArgumentsKind.Input && block.Arguments.FirstOrDefault(s => s.Structure is null && s.Name == parameter) is { } spec)
            {
                return spec;
            }
        }

        return null;
    }

    private static TypedArgument Typed(ArgumentSpec spec) => new(spec.Name)
    {
        ClassName = spec.ClassName,
        Size = Size(spec.Dims),
        MustBeReal = spec.Validators.Any(IsMustBeReal),
    };

    private static IReadOnlyList<int?>? Size(IReadOnlyList<Expr?>? dims) =>
        dims?.Select(static d => d is NumberLiteral n ? (int?)(int)Math.Round(n.Value) : null).ToList();

    private static bool IsMustBeReal(Expr validator) =>
        validator is VariableExpr { Name: "mustBeReal" } or CallExpr { Callee: VariableExpr { Name: "mustBeReal" } };

    private static TypedClass Class(ClassdefStmt cls, string prefix)
    {
        string fullName = prefix + cls.Name;
        if (cls.Enumeration.Count > 0)
        {
            return new TypedClass(fullName) { Enumeration = [.. cls.Enumeration.Select(static m => m.Name)] };
        }

        var setters = new HashSet<string>(cls.Methods.Where(static m => m.AccessorProperty is not null && !m.IsGetter).Select(static m => m.AccessorProperty!), StringComparer.Ordinal);
        var properties = new List<TypedProperty>();
        foreach (ClassProperty property in cls.Properties)
        {
            if (property.Hidden || !property.GetAccess.IsPublic)
            {
                continue;
            }

            ArgumentSpec spec = property.Spec;
            properties.Add(new TypedProperty(spec.Name)
            {
                ClassName = spec.ClassName,
                Size = Size(spec.Dims),
                MustBeReal = spec.Validators.Count > 0 && IsMustBeReal(spec.Validators[^1]),
                Readable = true,
                Writable = property.SetAccess.IsPublic && !property.Constant && !(property.Dependent && !setters.Contains(spec.Name)),
            });
        }

        TypedMethod constructor = new(fullName, [], [new TypedArgument("obj")]);
        var methods = new List<TypedMethod>();
        foreach (IGrouping<int, ClassMethod> block in cls.Methods.GroupBy(static m => m.Block).OrderBy(static g => g.Key))
        {
            foreach (ClassMethod method in block.Reverse())
            {
                if (method.Function.Name == cls.Name)
                {
                    (List<TypedArgument> inputs, List<TypedArgument> outputs) = Arguments(method.Function);
                    constructor = new TypedMethod(fullName, inputs, outputs);
                    continue;
                }

                if (method.AccessorProperty is not null || method.Hidden || method.Abstract || !method.Access.IsPublic)
                {
                    continue;
                }

                (List<TypedArgument> methodInputs, List<TypedArgument> methodOutputs) = Arguments(method.Function);
                methods.Add(new TypedMethod(method.Function.Name, methodInputs, methodOutputs) { Static = method.Static });
            }
        }

        if (cls.IsHandle)
        {
            var own = new HashSet<string>(methods.Select(static m => m.Name), StringComparer.Ordinal);
            foreach ((string name, string[] inputs, string[] outputs) in HandleMethods)
            {
                if (!own.Contains(name))
                {
                    methods.Add(new TypedMethod(name, [.. inputs.Select(static i => new TypedArgument(i))], [.. outputs.Select(static o => new TypedArgument(o))]));
                }
            }
        }

        return new TypedClass(fullName) { IsHandle = cls.IsHandle, Properties = properties, Constructor = constructor, Methods = methods };
    }
}
