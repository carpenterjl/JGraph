using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Deploy;

/// <summary>
/// What R2025b's C++ generator learns of a class an argument or property names (MATLABInfo,
/// CPPTypeConverter.isMathWorksRestricted), which the build answers from where it looks for classes.
/// </summary>
/// <param name="Restricted">MATLAB's own class, built in or in its toolbox: passed as a <c>matlab::data::Array</c>.</param>
/// <param name="External">A class of the user's that the build found: wrapped by the C++ class generated for it.</param>
/// <param name="Enumeration">An enumeration class.</param>
/// <param name="Handle">A handle class.</param>
public readonly record struct CppClassFacts(bool Restricted, bool External, bool Enumeration, bool Handle);

/// <summary>A generated C++ header: its text, how many classes and functions it holds, and the generator's warnings.</summary>
public sealed record CppInterface(string Text, int Classes, int Functions, IReadOnlyList<(string Identifier, string Message)> Warnings);

/// <summary>
/// The C++ strongly typed interface R2025b writes beside a C++ shared library's archive for the MATLAB
/// Data API (compiler and SDK plan, stage K12), transcribed from its
/// toolbox/matlab/external/engines/codegen/cpp (CPPCodeGenerator, CppClass, CppConstructor,
/// CppProperty, CppCallable, CPPTypeConverter and utilcpp's writeReturnSection, writeTypeStruct,
/// writeTemplateDefault, getOutputTypeCategory, generateNamespace and KeywordsCPP) over the common
/// templates (ClassTpl, PropertyTpl, MethodTpl, FunctionTpl, ArgumentTpl, MATLABInfo,
/// classifyArraySize, classifyArraySize2, resolveClassDependencies): the classes first in the order
/// their dependencies sort them, then the functions in the order given, every function and method a
/// template on its output count, each built from the same token-replaced text R2025b's is so the
/// header is its text line for line (record-k12.ps1, recordings\k12_out). JGraph's header line, and two
/// of R2025b's defects corrected: a name-value argument was passed by position (k12_client: t.nv,
/// refused by the function), and an output of MATLAB's own class (a typed cell or struct) was
/// returned uninitialized, where R2025b's own comment says it passes the array as it came.
/// </summary>
public static class TypedInterfaceCpp
{
    private const string OneIndent = "    ";

    /// <summary>R2025b's C++ keywords (KeywordsCPP).</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit", "atomic_noexcept", "auto", "bitand", "bitor", "bool",
        "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class", "compl", "concept", "const", "consteval", "constexpr",
        "constinit", "const_cast", "continue", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double",
        "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long",
        "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "private", "protected", "public",
        "reflexpr", "register", "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast",
        "struct", "switch", "synchronized", "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
        "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq", "final", "override", "transaction_safe",
        "transaction_safe_dynamic", "_Pragma",
    };

    /// <summary>R2025b's conversion table (CPPTypeConverter): C++ type, MATLAB class, scalar, real (1), complex (0) or either (-1).</summary>
    private static readonly (string Cpp, string Matlab, bool Scalar, int Real)[] Table = BuildTable();

    /// <summary>Handle's methods R2025b's C++ generator keeps of those the reader lists (ClassTpl.read): the comparisons and isvalid.</summary>
    private static readonly HashSet<string> HandleKept = new(StringComparer.Ordinal) { "eq", "ne", "lt", "le", "gt", "ge", "isvalid" };

    private static readonly Regex ComplexType = new(@"std::complex<(\w+)>", RegexOptions.CultureInvariant);

    private enum Dim
    {
        Unknown,
        Empty,
        Scalar,
        Vector,
        MultiDim,
    }

    /// <summary>utilcpp.OutputTypeCategory, with None for the case R2025b's getOutputTypeCategory leaves unset.</summary>
    private enum Category
    {
        None,
        SimpleScalar,
        SimpleVector,
        SimpleComplexScalar,
        SimpleComplexVector,
        MultiDim,
        GenEnumScalar,
        GenEnumVector,
        GenExternalClassScalar,
        GenExternalClassVector,
        MissingMeta,
        MWAuthoredClass,
    }

    /// <summary>
    /// The header for <paramref name="functions"/> and <paramref name="classes"/>, its comment naming
    /// <paramref name="fileName"/> and dated <paramref name="date"/>; <paramref name="facts"/> says what
    /// each class an argument or property names is.
    /// </summary>
    /// <exception cref="TypedInterfaceException">The classes depend on each other in a circle, or a class or function named for generation cannot be.</exception>
    public static CppInterface Generate(
        IReadOnlyList<TypedFunction> functions, IReadOnlyList<TypedClass> classes, string fileName, DateTime date,
        Func<string, CppClassFacts> facts, string version)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(facts);
        var generator = new Generator(facts);
        return generator.Run(functions, classes, fileName, date, version);
    }

    private static (string Cpp, string Matlab, bool Scalar, int Real)[] BuildTable()
    {
        var rows = new List<(string, string, bool, int)>
        {
            ("double", "double", true, 1), ("std::vector<double>", "double", false, 1),
            ("std::complex<double>", "double", true, 0), ("std::vector<std::complex<double>>", "double", false, 0),
            ("float", "single", true, 1), ("std::vector<float>", "single", false, 1),
            ("std::complex<float>", "single", true, 0), ("std::vector<std::complex<float>>", "single", false, 0),
        };
        foreach (string bits in new[] { "uint8", "uint16", "uint32", "uint64", "int8", "int16", "int32", "int64" })
        {
            string c = bits + "_t";
            rows.Add((c, bits, true, 1));
            rows.Add(("std::vector<" + c + ">", bits, false, 1));
            rows.Add(("std::complex<" + c + ">", bits, true, 0));
            rows.Add(("std::vector<std::complex<" + c + ">>", bits, false, 0));
        }

        rows.AddRange(
        [
            ("bool", "logical", true, -1), ("std::vector<bool>", "logical", false, -1),
            ("[MyEnumClass]", "[MyEnumClass]", true, -1), ("std::vector<[MyEnumClass]>", "[MyEnumClass]", false, -1),
            ("char16_t", "char", true, -1), ("std::u16string", "char", false, -1),
            ("std::u16string", "string", true, -1), ("std::vector<std::u16string>", "string", false, -1),
            ("matlab::data::Array", "dictionary", true, -1), ("matlab::data::Array", "dictionary", false, -1),
        ]);
        return [.. rows];
    }

    private static string Rep(string text, string token, string value) => text.Replace(token, value, StringComparison.Ordinal);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Last(string fullName) => fullName[(fullName.LastIndexOfAny(['.', '/']) + 1)..];

    private static string RootIndents(int level) => string.Concat(Enumerable.Repeat("[oneIndent]", level));

    /// <summary>util.rootIndentCode: the root indent's token at the start of every line.</summary>
    private static string RootIndentCode(string code) => "[rootIndent]" + Rep(code, "\n", "\n[rootIndent]");

    /// <summary>utilcpp.generateNamespace: <c>namespace a { namespace b {</c> and its close, for a dotted name's prefix.</summary>
    private static (string Open, string Close) GenerateNamespace(string fullName)
    {
        string open = string.Empty;
        string close = "\n";
        string[] parts = fullName.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            open += "namespace " + parts[i] + " { ";
            close += "} ";
        }

        return (open.TrimEnd() + "\n", close.TrimEnd() + "\n");
    }

    private static bool IsTableType(string matlab) => Table.Any(t => t.Matlab == matlab);

    /// <summary>CPPTypeConverter.convertType2CPP.</summary>
    private static (string Cpp, bool Matched) ConvertType2Cpp(string matlabType, Dim dimension, bool isReal)
    {
        bool isScalar = dimension == Dim.Scalar;
        if (isScalar || dimension == Dim.Vector)
        {
            int real = isReal ? 1 : 0;
            (string Cpp, string Matlab, bool Scalar, int Real)[] matches =
                [.. Table.Where(t => t.Matlab == matlabType && t.Scalar == isScalar && (t.Real == real || t.Real == -1))];
            return matches.Length == 1 ? (matches[0].Cpp, true) : ("matlab::data::Array", false);
        }

        return dimension == Dim.MultiDim ? ("matlab::data::Array", false) : (string.Empty, false);
    }

    /// <summary>classifyArraySize2: an argument's declared size, <c>:</c> (null) counting as longer than one.</summary>
    private static Dim ClassifyArgument(IReadOnlyList<int?> size)
    {
        if (size.Count < 2)
        {
            return Dim.Empty;
        }

        int longer = 0;
        foreach (int? d in size)
        {
            if (d == 0)
            {
                return Dim.Empty;
            }

            if (d is null || d > 1)
            {
                longer++;
            }
        }

        return longer == 0 ? Dim.Scalar : longer == 1 ? Dim.Vector : Dim.MultiDim;
    }

    /// <summary>classifyArraySize: a property's declared size, <c>:</c> read as inf.</summary>
    private static Dim ClassifyProperty(IReadOnlyList<int?> dims)
    {
        if (dims.Count < 2 || dims.Any(static d => d == 0))
        {
            return Dim.Empty;
        }

        int longer = dims.Count(static d => d is null || d > 1);
        return longer == 0 ? Dim.Scalar : longer == 1 ? Dim.Vector : Dim.MultiDim;
    }

    /// <summary>ArgumentTpl with its MATLABInfo.</summary>
    private sealed class Arg
    {
        public required string Name { get; init; }

        public TypedArgumentKind Kind { get; init; }

        public string ClassName { get; init; } = string.Empty;

        public Dim SizeClassification { get; init; } = Dim.Unknown;

        public bool HasType { get; init; }

        public bool HasSize { get; init; }

        public bool IsReal { get; init; }

        public bool IsExternal { get; init; }

        public bool IsEnum { get; init; }
    }

    /// <summary>FunctionTpl or MethodTpl: what CppCallable writes.</summary>
    private sealed class Callable
    {
        public bool IsMethod { get; init; }

        public string SectionName { get; init; } = string.Empty;

        public string FullName { get; init; } = string.Empty;

        public string ShortName { get; init; } = string.Empty;

        public string DefiningClass { get; init; } = string.Empty;

        public string EncapsulatingClass { get; init; } = string.Empty;

        public bool IsConstructor { get; init; }

        public bool IsVarargin { get; init; }

        public bool IsStatic { get; init; }

        public int IndentLevel { get; init; }

        public List<Arg> InputArgs { get; init; } = [];

        public List<Arg> OutputArgs { get; init; } = [];

        public string SectionContent { get; set; } = string.Empty;

        public string TemplateSpecSection { get; set; } = string.Empty;

        public int NumArgIn => InputArgs.Count;

        public int NumArgOut => OutputArgs.Count;
    }

    /// <summary>PropertyTpl.</summary>
    private sealed class Prop
    {
        public string SectionName { get; init; } = string.Empty;

        public int IndentLevel { get; init; }

        public bool IsGetAccessible { get; init; }

        public bool IsSetAccessible { get; init; }

        public bool IsEnumeration { get; init; }

        public string MatlabPropertyClass { get; init; } = string.Empty;

        public string CppPropertyClass { get; init; } = string.Empty;

        public Dim ArrayType { get; init; }
    }

    /// <summary>ClassTpl.</summary>
    private sealed class Cls
    {
        public string SectionName { get; init; } = string.Empty;

        public string FullName { get; init; } = string.Empty;

        public bool IsHandleClass { get; init; }

        public bool IsEnumeration { get; set; }

        public bool IsAbstract { get; init; }

        public List<string> Enumerations { get; } = [];

        public List<Prop> Properties { get; } = [];

        public List<Callable> Methods { get; } = [];

        public Callable? Constructor { get; set; }

        public List<string> Dependencies { get; set; } = [];
    }

    /// <summary>One generation: the facts the build supplied, and what it drops along the way.</summary>
    private sealed class Generator(Func<string, CppClassFacts> facts)
    {
        private readonly List<(string Defining, string Name, string Kind, string Reason)> _implicitDrops = [];
        private readonly List<(string Name, string Kind, string Reason)> _explicitDrops = [];

        public CppInterface Run(IReadOnlyList<TypedFunction> functions, IReadOnlyList<TypedClass> classes, string fileName, DateTime date, string version)
        {
            // CPPCodeGenerator.read: the classes and functions as given, abstract classes dropped,
            // names that are C++ keywords dropped, the classes sorted by what each depends on.
            List<Cls> classList = [.. classes.Select(MakeClass).Where(static c => !c.IsAbstract)];
            foreach (Cls c in classList.Where(static c => Keywords.Contains(c.SectionName)).ToList())
            {
                _explicitDrops.Add((c.FullName, "Class", "Class name conflicts with C++ keyword"));
                classList.Remove(c);
            }

            List<Callable> functionList = [.. functions.Select(MakeFunction)];
            foreach (Callable f in functionList.Where(static f => Keywords.Contains(f.ShortName)).ToList())
            {
                _explicitDrops.Add((f.FullName, "Function", "Function name conflicts with C++ keyword"));
                functionList.Remove(f);
            }

            (List<Cls> sorted, List<string> missing) = ResolveClassDependencies(classList, functionList);
            var warnings = CheckErrorsWarnings(missing);

            // CPPCodeGenerator.write.
            var contents = new StringBuilder(Header(fileName, date, version));
            foreach (Cls c in sorted)
            {
                contents.Append(ClassText(c));
            }

            foreach (Callable f in functionList)
            {
                contents.Append(CallableText(f));
            }

            string text = Rep(contents.ToString(), "[oneIndent]", OneIndent).TrimEnd();
            return new CppInterface(text, sorted.Count, functionList.Count, warnings);
        }

        /// <summary>CppHeader with JGraph's version line.</summary>
        private static string Header(string fileName, DateTime date, string version) =>
            "/* File: " + fileName + "\n*\n* JGraph Strongly Typed Interface Version: " + version + " (R2025b)\n* C++ source code generated on: "
            + date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) + "\n*/\n#include \"MatlabTypesInterface.hpp\"\n#include <map>\n";

        private bool Restricted(string className) => className.Length > 0 && facts(className).Restricted;

        private Arg MakeArg(TypedArgument argument)
        {
            bool hasType = argument.ClassName is not null;
            CppClassFacts known = hasType ? facts(argument.ClassName!) : default;
            return new Arg
            {
                Name = argument.Name,
                Kind = argument.Kind,
                ClassName = argument.ClassName ?? string.Empty,
                HasType = hasType,
                HasSize = argument.Size is not null,
                SizeClassification = argument.Size is { } size ? ClassifyArgument(size) : Dim.Unknown,
                IsReal = argument.MustBeReal,
                IsExternal = hasType && known.External,
                IsEnum = hasType && known.Enumeration,
            };
        }

        /// <summary>CPPTypeConverter.convertArg.</summary>
        private static (string Cpp, bool Matched) ConvertArg(Arg arg) => ConvertType2Cpp(arg.ClassName, arg.SizeClassification, arg.IsReal);

        private Callable MakeFunction(TypedFunction function) => new()
        {
            SectionName = function.FullName,
            FullName = function.FullName,
            ShortName = Last(function.FullName),
            IndentLevel = 0,
            InputArgs = [.. function.Inputs.Select(MakeArg)],
            OutputArgs = [.. function.Outputs.Select(MakeArg)],
            IsVarargin = function.Inputs.Any(static a => a.Kind == TypedArgumentKind.Repeating),
        };

        private Callable MakeMethod(TypedMethod method, string className, string name, bool constructor)
        {
            string defining = method.DefiningClass ?? className;
            return new Callable
            {
                IsMethod = true,
                SectionName = name,
                ShortName = name,
                DefiningClass = defining,
                EncapsulatingClass = className,
                FullName = defining + "/" + name,
                IsConstructor = constructor,
                IsStatic = method.Static,
                IndentLevel = 2,
                InputArgs = [.. method.Inputs.Select(MakeArg)],
                OutputArgs = [.. method.Outputs.Select(MakeArg)],
                IsVarargin = method.Inputs.Any(static a => a.Kind == TypedArgumentKind.Repeating),
            };
        }

        /// <summary>PropertyTpl.read: the C++ type from the declared class and size, the accessors from the access.</summary>
        private Prop MakeProperty(TypedProperty property)
        {
            string matlab = property.ClassName ?? "unknown";
            Dim dim = property.Size is { } size ? ClassifyProperty(size) : Dim.MultiDim;
            bool enumeration = property.ClassName is { } named && facts(named).Enumeration;
            return new Prop
            {
                SectionName = property.Name,
                IndentLevel = 2,
                MatlabPropertyClass = matlab,
                ArrayType = dim,
                IsEnumeration = enumeration,
                CppPropertyClass = ConvertType2Cpp(matlab, dim, property.MustBeReal).Cpp,
                IsGetAccessible = property.Readable && (!property.Dependent || property.HasGetMethod),
                IsSetAccessible = property.Writable,
            };
        }

        /// <summary>ClassTpl.read and its handleNameConflicts for C++.</summary>
        private Cls MakeClass(TypedClass typed)
        {
            var cls = new Cls
            {
                FullName = typed.FullName,
                SectionName = Last(typed.FullName),
                IsHandleClass = typed.IsHandle,
                IsAbstract = typed.Abstract,
                IsEnumeration = typed.Enumeration.Count > 0,
            };
            cls.Enumerations.AddRange(typed.Enumeration);
            var dependencies = new List<string>();
            if (!cls.IsAbstract && !cls.IsEnumeration)
            {
                foreach (TypedProperty property in typed.Properties)
                {
                    Prop made = MakeProperty(property);
                    if (made.IsGetAccessible || made.IsSetAccessible)
                    {
                        cls.Properties.Add(made);
                        dependencies.Add(made.MatlabPropertyClass);
                    }
                }

                Callable constructor = MakeMethod(typed.Constructor, typed.FullName, cls.SectionName, constructor: true);
                foreach (TypedMethod method in typed.Methods)
                {
                    // Static methods are not generated for C++ (GetMethodOverloads), nor handle's methods but its comparisons and isvalid.
                    if (method.Name == "empty" || method.Static || (method.DefiningClass == "handle" && !HandleKept.Contains(method.Name))
                        || cls.Methods.Any(m => m.ShortName == method.Name))
                    {
                        continue;
                    }

                    cls.Methods.Add(MakeMethod(method, typed.FullName, method.Name, constructor: false));
                }

                foreach (Callable method in cls.Methods.Prepend(constructor))
                {
                    int start = method.IsStatic || method.IsConstructor ? 0 : 1;
                    dependencies.AddRange(method.InputArgs.Skip(start).Select(static a => a.ClassName));
                    dependencies.AddRange(method.OutputArgs.Select(static a => a.ClassName));
                }

                cls.Constructor = constructor;
            }

            // setdiff: unique and sorted, the class itself left out.
            cls.Dependencies = [.. dependencies.Where(d => d != cls.FullName).Distinct(StringComparer.Ordinal).OrderBy(static d => d, StringComparer.Ordinal)];
            HandleNameConflicts(cls);
            return cls;
        }

        private void HandleNameConflicts(Cls cls)
        {
            if (cls.IsEnumeration && cls.Enumerations.Count > 0)
            {
                foreach (string member in cls.Enumerations.Where(Keywords.Contains).ToList())
                {
                    _implicitDrops.Add((cls.FullName, member, "EnumMember", "Enum member name conflicts with C++ keyword"));
                    cls.Enumerations.Remove(member);
                }
            }

            if (cls.Properties.Count > 0 && cls.Methods.Count > 0)
            {
                var accessors = new HashSet<string>(cls.Properties.SelectMany(static p => new[] { "set" + p.SectionName, "get" + p.SectionName }), StringComparer.Ordinal);
                foreach (Callable method in cls.Methods.Where(m => accessors.Contains(m.SectionName)).ToList())
                {
                    _implicitDrops.Add((cls.FullName, method.SectionName, "ClassMethod", "Method name conflicts with property accessor"));
                    cls.Methods.Remove(method);
                }
            }

            foreach (Prop property in cls.Properties.Where(p => Keywords.Contains("set" + p.SectionName) || Keywords.Contains("get" + p.SectionName)).ToList())
            {
                _implicitDrops.Add((cls.FullName, property.SectionName, "ClassProperty", "Property accessor name conflicts with C++ keyword"));
                cls.Properties.Remove(property);
            }

            foreach (Prop property in cls.Properties.Where(p => "set" + p.SectionName == cls.SectionName || "get" + p.SectionName == cls.SectionName).ToList())
            {
                _implicitDrops.Add((cls.FullName, property.SectionName, "ClassProperty", "Property accessor name conflicts with class name"));
                cls.Properties.Remove(property);
            }

            foreach (Callable method in cls.Methods.Where(m => Keywords.Contains(m.SectionName)).ToList())
            {
                _implicitDrops.Add((cls.FullName, method.SectionName, "ClassMethod", "Method name conflicts with C++ keyword"));
                cls.Methods.Remove(method);
            }
        }

        /// <summary>
        /// resolveClassDependencies: the classes in the order MATLAB's digraph toposort gives them
        /// (a node is put in front of the order once everything reachable from it is, the nodes
        /// started in the order they were added and each one's successors in theirs), each edge
        /// running from a class to the classes that depend on it; and the classes named that the
        /// build neither generates nor knows as MATLAB's own.
        /// </summary>
        private (List<Cls> Sorted, List<string> Missing) ResolveClassDependencies(List<Cls> classes, List<Callable> functions)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (Cls c in classes)
            {
                index[c.FullName] = names.Count;
                names.Add(c.FullName);
            }

            var edges = new List<(int From, int To)>();
            foreach (Cls c in classes)
            {
                foreach (string d in c.Dependencies)
                {
                    if (!index.TryGetValue(d, out int from))
                    {
                        from = names.Count;
                        index[d] = from;
                        names.Add(d);
                    }

                    edges.Add((from, index[c.FullName]));
                }
            }

            var successors = new SortedSet<int>[names.Count];
            for (int n = 0; n < names.Count; n++)
            {
                successors[n] = [];
            }

            foreach ((int from, int to) in edges)
            {
                successors[from].Add(to);
            }

            var state = new int[names.Count]; // 0 unvisited, 1 on the path, 2 done
            var order = new List<int>();
            for (int n = 0; n < names.Count; n++)
            {
                if (state[n] == 0)
                {
                    Visit(n, successors, state, order, names);
                }
            }

            order.Reverse();
            var sorted = new List<Cls>();
            var missing = new List<string>();
            foreach (int n in order)
            {
                if (n < classes.Count)
                {
                    sorted.Add(classes[n]);
                }
                else if (!IsTableType(names[n]) && !Restricted(names[n]))
                {
                    missing.Add(names[n]);
                }
            }

            var classNames = new HashSet<string>(classes.Select(static c => c.FullName), StringComparer.Ordinal);
            foreach (Callable f in functions)
            {
                foreach (Arg arg in f.InputArgs.Concat(f.OutputArgs))
                {
                    string d = arg.ClassName;
                    if (d.Length > 0 && d != "dictionary" && !classNames.Contains(d) && !IsTableType(d) && !Restricted(d) && !missing.Contains(d))
                    {
                        missing.Add(d);
                    }
                }
            }

            missing.RemoveAll(static d => d.Length == 0 || d == "unknown");
            return (sorted, missing);
        }

        private static void Visit(int node, SortedSet<int>[] successors, int[] state, List<int> order, List<string> names)
        {
            state[node] = 1;
            foreach (int next in successors[node])
            {
                if (state[next] == 1)
                {
                    throw new TypedInterfaceException("MATLAB:engine_codegen:CircularClassDependencies",
                        "The following classes form circular dependencies and must be excluded:\n" + names[next] + " " + names[node]);
                }

                if (state[next] == 0)
                {
                    Visit(next, successors, state, order, names);
                }
            }

            state[node] = 2;
            order.Add(node);
        }

        /// <summary>ReportData.checkErrorsWarnings: a class or function given that could not be generated is an error, a member a warning.</summary>
        private List<(string, string)> CheckErrorsWarnings(List<string> missing)
        {
            if (_explicitDrops.Count > 0)
            {
                var s = new StringBuilder("Specified input(s) not included in C++ interface:\n");
                List<(string Name, string Kind, string Reason)> droppedClasses = [.. _explicitDrops.Where(static d => d.Kind == "Class").OrderBy(static d => d.Name, StringComparer.Ordinal)];
                if (droppedClasses.Count > 0)
                {
                    s.Append(OneIndent).Append("Classes:\n");
                    foreach ((string name, _, string reason) in droppedClasses)
                    {
                        s.Append(OneIndent).Append(OneIndent).Append(name).Append(": ").Append(reason).Append('\n');
                    }
                }

                List<(string Name, string Kind, string Reason)> droppedFunctions = [.. _explicitDrops.Where(static d => d.Kind == "Function").OrderBy(static d => d.Name, StringComparer.Ordinal)];
                if (droppedFunctions.Count > 0)
                {
                    s.Append(OneIndent).Append("Functions:\n");
                    foreach ((string name, _, string reason) in droppedFunctions)
                    {
                        s.Append(OneIndent).Append(OneIndent).Append(name).Append(": ").Append(reason).Append('\n');
                    }
                }

                throw new TypedInterfaceException("MATLAB:engine_codegen:SpecifiedItemNotGenerated", s.ToString());
            }

            var warnings = new List<(string, string)>();
            if (_implicitDrops.Count > 0)
            {
                var s = new StringBuilder("Unable to generate C++ interface to MATLAB item(s):\n").Append(OneIndent).Append("Classes:\n");
                foreach (IGrouping<string, (string Defining, string Name, string Kind, string Reason)> family in _implicitDrops.GroupBy(static d => d.Defining).OrderBy(static g => g.Key, StringComparer.Ordinal))
                {
                    s.Append(OneIndent).Append(OneIndent).Append(family.Key).Append(":\n");
                    foreach (string kind in new[] { "ClassProperty", "ClassMethod", "EnumMember" })
                    {
                        foreach ((_, string name, _, string reason) in family.Where(d => d.Kind == kind).OrderBy(static d => d.Name, StringComparer.Ordinal))
                        {
                            s.Append(OneIndent).Append(OneIndent).Append(OneIndent).Append(name).Append(": ").Append(reason).Append('\n');
                        }
                    }
                }

                warnings.Add(("MATLAB:engine_codegen:ImplicitItemNotGenerated", s.ToString()));
            }

            if (missing.Count > 0)
            {
                warnings.Add(("MATLAB:engine_codegen:GeneratorHasUnresolvedClassDependencies",
                    "Unresolved class dependencies. For code to build, generate or provide these classes: " + string.Join(" ", missing)));
            }

            return warnings;
        }

        // --- CppClass ---------------------------------------------------------------------------

        private string ClassText(Cls cls)
        {
            string section = "[namespaceSection][oneIndent]class [className] : public [objectType]<MATLABControllerType> { \n"
                + "[rootIndent][oneIndent]public:\n\n"
                + "[comparisonFriendSection][constructorSection][propertySection][methodSection]"
                + "[rootIndent][oneIndent]};\n"
                + "[templateSpecSection]\n"
                + "[comparisonOperatorSection][namespaceClose]";
            if (cls.IsEnumeration)
            {
                section = "[namespaceSection][oneIndent]enum class [className] { \n[enumSection][rootIndent][oneIndent]};[namespaceClose]\n[enumHelpers]\n";
            }

            (string namespaceSection, string namespaceClose) = GenerateNamespace(cls.FullName);
            string[] parts = cls.FullName.Split('.');
            string fullNameSpace = string.Concat(parts[..^1].Select(static p => p + "::"));

            string constructorSection = "[rootIndent][oneIndent][oneIndent]// constructors\n";
            if (cls.Constructor is { } constructor)
            {
                constructorSection += ConstructorText(constructor);
            }

            string propertySection = "[rootIndent][oneIndent][oneIndent]// properties\n";
            foreach (Prop property in cls.Properties)
            {
                propertySection += PropertyText(property);
            }

            propertySection += "\n";

            string templateSpecSection = string.Empty;
            string methodSection = "[rootIndent][oneIndent][oneIndent]// methods\n";
            foreach (Callable method in cls.Methods)
            {
                // handle.isvalid is MATLABHandleObject's own.
                if (method.DefiningClass == "handle" && method.ShortName == "isvalid")
                {
                    continue;
                }

                methodSection += CallableText(method);
                templateSpecSection += method.TemplateSpecSection + "\n";
            }

            (string comparisonOperatorSection, string comparisonFriendSection) = ComparisonOperators(cls);

            string enumHelpers = string.Empty;
            string enumSection = string.Empty;
            if (cls.Enumerations.Count > 0)
            {
                enumSection = "[rootIndent][oneIndent][oneIndent]// enumerations\n";
                for (int i = 0; i < cls.Enumerations.Count; i++)
                {
                    enumSection += "[rootIndent][oneIndent][oneIndent]" + cls.Enumerations[i] + (i != cls.Enumerations.Count - 1 ? "," : string.Empty) + "\n";
                }

                string scoped = fullNameSpace + cls.SectionName;
                string flat = Rep(fullNameSpace, "::", "_") + cls.SectionName;
                string[] mapTo = [.. cls.Enumerations.Select(m => "{" + scoped + "::" + m + ", \"" + m + "\"}")];
                string[] mapFrom = [.. cls.Enumerations.Select(m => "{\"" + m + "\", " + scoped + "::" + m + "}")];
                for (int i = 0; i < mapTo.Length - 1; i++)
                {
                    mapTo[i] += ",";
                    mapFrom[i] += ",";
                }

                enumHelpers = "namespace {\n"
                    + "[oneIndent]std::string get" + flat + "String(const " + scoped + "& _enum) {\n"
                    + "[oneIndent][oneIndent]static const std::map<" + scoped + ", std::string> map = { " + string.Join(" ", mapTo) + " };\n"
                    + "[oneIndent][oneIndent]return (map.find(_enum))->second;\n"
                    + "[oneIndent]}\n"
                    + "[oneIndent]" + scoped + " get" + flat + "Enum(const std::string& _str) {\n"
                    + "[oneIndent][oneIndent]static const std::map<std::string, " + scoped + "> map = { " + string.Join(" ", mapFrom) + " };\n"
                    + "[oneIndent][oneIndent]return (map.find(_str))->second;\n"
                    + "[oneIndent]}\n"
                    + "}\n";
            }

            section = Rep(section, "[namespaceSection]", namespaceSection);
            section = Rep(section, "[namespaceClose]", namespaceClose);
            section = Rep(section, "[className]", cls.SectionName);
            section = Rep(section, "[constructorSection]", constructorSection);
            section = Rep(section, "[propertySection]", propertySection);
            section = Rep(section, "[methodSection]", methodSection);
            section = Rep(section, "[enumSection]", enumSection);
            section = Rep(section, "[enumHelpers]", enumHelpers);
            section = Rep(section, "[templateSpecSection]", templateSpecSection);
            section = Rep(section, "[comparisonOperatorSection]", comparisonOperatorSection);
            section = Rep(section, "[comparisonFriendSection]", comparisonFriendSection);
            section = Rep(section, "[objectType]", cls.IsHandleClass ? "MATLABHandleObject" : "MATLABObject");
            return Rep(section, "[rootIndent]", string.Empty);
        }

        /// <summary>CppClass.generateComparisonOperators: a free operator and its friend line for each comparison the class has.</summary>
        private static (string Operators, string Friends) ComparisonOperators(Cls cls)
        {
            (string Method, string Symbol)[] comparisons = [("eq", "=="), ("ne", "!="), ("lt", "<"), ("gt", ">"), ("le", "<="), ("ge", ">=")];
            var operators = new StringBuilder();
            var friends = new StringBuilder();
            string fullClass = Rep(cls.FullName, ".", "::");
            foreach ((string method, string symbol) in comparisons)
            {
                Callable? found = cls.Methods.FirstOrDefault(m => m.ShortName == method);
                if (found is null)
                {
                    continue;
                }

                string arg1 = found.NumArgIn >= 1 ? found.InputArgs[0].Name : "obj1";
                string arg2 = found.NumArgIn >= 2 ? found.InputArgs[1].Name : "obj2";
                operators.Append("bool operator").Append(symbol).Append("(const ").Append(fullClass).Append("& ").Append(arg1).Append(", const ")
                    .Append(fullClass).Append("& ").Append(arg2).Append(") {\n")
                    .Append("[oneIndent]std::vector<matlab::data::Array> _args = { ").Append(arg1).Append(".m_object, ").Append(arg2).Append(".m_object };\n")
                    .Append("[oneIndent]matlab::data::TypedArray<bool> _result_mda = ").Append(arg1).Append(".m_matlabPtr->feval(u\"").Append(method).Append("\", _args);\n")
                    .Append("[oneIndent]bool _result = _result_mda[0];\n")
                    .Append("[oneIndent]return _result;\n")
                    .Append("}\n\n\n");
                friends.Append("[rootIndent][oneIndent]friend bool operator").Append(symbol).Append("(const ").Append(fullClass).Append("& ").Append(arg1)
                    .Append(", const ").Append(fullClass).Append("& ").Append(arg2).Append(");\n");
            }

            return (operators.ToString(), friends.ToString());
        }

        /// <summary>CppConstructor.</summary>
        private string ConstructorText(Callable method)
        {
            bool hasDefaultSignature = method.NumArgIn == 0 && method.NumArgOut == 1;
            string content = "[rootIndent][ClassName]() : [objectType]() {}\n\n";

            // An empty constructor, also when the constructor takes varargin (g2890342).
            if (hasDefaultSignature || method.IsVarargin)
            {
                content += "[rootIndent][ClassName](std::shared_ptr<MATLABControllerType> matlabPtr) :\n[rootIndent][oneIndent][objectType](matlabPtr, u\"[classNameFull]\")\n[rootIndent]{}\n";
            }

            if (!hasDefaultSignature)
            {
                content += CallableText(method) + "\n";
            }

            content += "[rootIndent][copyConstructor]";

            // No copy constructor where a constructor of one input would be the same signature.
            bool writeCopyConstructor = true;
            if (method.NumArgIn == 1)
            {
                string cppArgType = ConvertArg(method.InputArgs[0]).Cpp;
                if (cppArgType == "matlab::data::Array" || (cppArgType.Length == 0 && method.InputArgs[0].Kind != TypedArgumentKind.Repeating))
                {
                    writeCopyConstructor = false;
                }
            }

            content = Rep(content, "[copyConstructor]", writeCopyConstructor
                ? "[rootIndent][ClassName](std::shared_ptr<MATLABControllerType> matlabPtr, matlab::data::Array obj) :\n[rootIndent][oneIndent][objectType](matlabPtr, obj)\n[rootIndent]{}\n\n"
                : string.Empty);
            content = Rep(content, "[ClassName]", Last(method.EncapsulatingClass));
            content = Rep(content, "[classNameFull]", method.DefiningClass);
            return Rep(content, "[rootIndent]", RootIndents(method.IndentLevel));
        }

        // --- CppProperty ------------------------------------------------------------------------

        private string PropertyText(Prop property)
        {
            string scalarBaseType = string.Empty;
            string realNumericType = string.Empty;
            string scalarOrVector = string.Empty;
            if (property.ArrayType == Dim.Scalar || property.CppPropertyClass == "matlab::data::Array" || property.CppPropertyClass == "std::u16string")
            {
                scalarOrVector = "Scalar";
                scalarBaseType = property.CppPropertyClass;
            }
            else if (property.ArrayType == Dim.Vector)
            {
                scalarOrVector = "Vector";
                scalarBaseType = Regex.Match(property.CppPropertyClass, "std::vector<(.*)>").Groups[1].Value;
            }

            string getSection = string.Empty;
            string setSection = string.Empty;
            bool scalar = property.ArrayType == Dim.Scalar;
            bool vector = property.ArrayType == Dim.Vector;
            string enumDotted = property.MatlabPropertyClass;
            string enumScoped = Rep(enumDotted, ".", "::");
            string enumFlat = Rep(enumDotted, ".", "_");
            if (IsTableType(property.MatlabPropertyClass))
            {
                setSection = "[rootIndent]void set[PropertyName]([CppPropertyClass] value) { return MATLABSet[ScalarOrVector]Property(u\"[PropertyName]\", value); }\n";
                bool isComplex = property.CppPropertyClass.Contains("std::complex", StringComparison.Ordinal);

                // A number MATLAB may hold complex is also set from a real one.
                if (isComplex && (scalar || vector))
                {
                    Match match = Regex.Match(property.CppPropertyClass, "std::complex<(.*?)>");
                    realNumericType = Rep(property.CppPropertyClass, match.Value, match.Groups[1].Value);
                    setSection = "[rootIndent]void set[PropertyName]([realNumericType] value) { return MATLABSet[ScalarOrVector]Property(u\"[PropertyName]\", value); }\n" + setSection;
                }

                getSection = isComplex && vector
                    ? "[rootIndent]matlab::data::Array get[PropertyName]() { return MATLABGetScalarProperty<matlab::data::Array>(u\"[PropertyName]\"); }\n"
                    : isComplex && scalar
                        ? "[rootIndent][CppPropertyClass] get[PropertyName]() { return MATLABGetComplexScalarProperty<[realNumericType]>(u\"[PropertyName]\"); }\n"
                        : "[rootIndent][CppPropertyClass] get[PropertyName]() { return MATLABGet[ScalarOrVector]Property<[scalarBaseType]>(u\"[PropertyName]\"); }\n";
            }
            else if (property.IsEnumeration && scalar)
            {
                getSection = "[rootIndent][MatlabPropertyClass] get[PropertyName]() {\n"
                    + "[rootIndent][oneIndent]matlab::data::Array obj = MATLABGetScalarProperty<matlab::data::Array>(u\"[PropertyName]\");\n"
                    + "[rootIndent][oneIndent]const std::string& _str = obj[0];\n"
                    + "[rootIndent][oneIndent]" + enumScoped + " ret = ::get" + enumFlat + "Enum(_str);\n"
                    + "[rootIndent][oneIndent]return ret; \n"
                    + "[rootIndent]}\n";
                setSection = "[rootIndent]void set[PropertyName]([MatlabPropertyClass] enumProp) {\n"
                    + "[rootIndent][oneIndent]matlab::data::ArrayFactory _arrayFactory;\n"
                    + "[rootIndent][oneIndent]matlab::data::ArrayDimensions _dims = {1, 1};\n"
                    + "[rootIndent][oneIndent]std::string _str = ::get" + enumFlat + "String(enumProp);\n"
                    + "[rootIndent][oneIndent]matlab::data::Array _enum = _arrayFactory.createEnumArray(_dims, \"" + enumDotted + "\", { _str });\n"
                    + "[rootIndent][oneIndent]MATLABSetScalarProperty(u\"[PropertyName]\", _enum);\n"
                    + "[rootIndent]}\n";
            }
            else if (property.IsEnumeration && vector)
            {
                getSection = "[rootIndent]std::vector<[MatlabPropertyClass]> get[PropertyName]() {\n"
                    + "[rootIndent][oneIndent]matlab::data::Array arr = MATLABGetArrayProperty(u\"[PropertyName]\");\n"
                    + "[rootIndent][oneIndent]std::vector<[MatlabPropertyClass]> enum_objs;\n"
                    + "[rootIndent][oneIndent]for(size_t k=0; k<arr.getNumberOfElements(); k++){\n"
                    + "[rootIndent][oneIndent][oneIndent]enum_objs.push_back(::get" + enumFlat + "Enum(arr[k]));\n"
                    + "[rootIndent][oneIndent]}\n"
                    + "[rootIndent][oneIndent]return enum_objs;\n"
                    + "[rootIndent]}\n";
                setSection = "[rootIndent]void set[PropertyName](std::vector<[MatlabPropertyClass]> enumProp) {\n"
                    + "[rootIndent][oneIndent]matlab::data::ArrayFactory _arrayFactory;\n"
                    + "[rootIndent][oneIndent]matlab::data::ArrayDimensions _dims = {1, enumProp.size()};\n"
                    + "[rootIndent][oneIndent]std::vector<std::string> _enum_strings(enumProp.size());\n"
                    + "[rootIndent][oneIndent]std::transform(enumProp.begin(), enumProp.end(), _enum_strings.begin(), ::get" + enumFlat + "String);\n"
                    + "[rootIndent][oneIndent]matlab::data::Array _enumProp_mda = _arrayFactory.createEnumArray(_dims, \"" + enumDotted + "\", _enum_strings);\n"
                    + "[rootIndent][oneIndent]MATLABSetArrayProperty(u\"[PropertyName]\", _enumProp_mda);\n"
                    + "[rootIndent]}\n";
            }
            else if (((property.MatlabPropertyClass == "unknown" || Restricted(property.MatlabPropertyClass)) && property.CppPropertyClass == "matlab::data::Array")
                || property.ArrayType == Dim.MultiDim)
            {
                // No class, MATLAB's own class or more than two dimensions: the array as it is.
                getSection = "[rootIndent]matlab::data::Array get[PropertyName]() {\n"
                    + "[rootIndent][oneIndent]matlab::data::Array obj = MATLABGetScalarProperty<matlab::data::Array>(u\"[PropertyName]\");\n"
                    + "[rootIndent][oneIndent]return obj;\n"
                    + "[rootIndent]}\n";
                setSection = "[rootIndent]void set[PropertyName](matlab::data::Array obj) {\n"
                    + "[rootIndent][oneIndent]MATLABSetScalarProperty(u\"[PropertyName]\", obj);\n"
                    + "[rootIndent]}\n";
            }
            else if (scalar)
            {
                getSection = "[rootIndent][MatlabPropertyClass] get[PropertyName]() {\n"
                    + "[rootIndent][oneIndent]matlab::data::Array obj = MATLABGetScalarProperty<matlab::data::Array>(u\"[PropertyName]\");\n"
                    + "[rootIndent][oneIndent]return [MatlabPropertyClass](m_matlabPtr, obj);\n"
                    + "[rootIndent]}\n";
                setSection = "[rootIndent]void set[PropertyName]([MatlabPropertyClass] obj) {\n"
                    + "[rootIndent][oneIndent]MATLABSetScalarProperty(u\"[PropertyName]\", matlab::data::Array(obj));\n"
                    + "[rootIndent]}\n";
            }
            else if (vector)
            {
                getSection = "[rootIndent]std::vector<[MatlabPropertyClass]> get[PropertyName]() {\n"
                    + "[rootIndent][oneIndent]std::vector<matlab::data::Object> _raw_objs = MATLABGetVectorProperty<matlab::data::Object>(u\"[PropertyName]\");\n"
                    + "[rootIndent][oneIndent]std::vector<[MatlabPropertyClass]> _objs;\n"
                    + "[rootIndent][oneIndent]matlab::data::ArrayFactory _arrayFactory;\n"
                    + "[rootIndent][oneIndent]for(size_t k=0; k<_raw_objs.size(); k++){\n"
                    + "[rootIndent][oneIndent][oneIndent]matlab::data::ObjectArray _obj_arr = _arrayFactory.createScalar(_raw_objs.at(k));\n"
                    + "[rootIndent][oneIndent][oneIndent]_objs.push_back([MatlabPropertyClass](m_matlabPtr, _obj_arr));\n"
                    + "[rootIndent][oneIndent]}\n"
                    + "[rootIndent][oneIndent]return _objs;\n"
                    + "[rootIndent]}\n";
                setSection = "[rootIndent]void set[PropertyName](std::vector<[MatlabPropertyClass]> objs) {\n"
                    + "[rootIndent][oneIndent]std::vector<matlab::data::Object> raw_objs;\n"
                    + "[rootIndent][oneIndent]for(size_t k=0; k<objs.size(); k++){\n"
                    + "[rootIndent][oneIndent][oneIndent]matlab::data::Array _mda = objs.at(k);\n"
                    + "[rootIndent][oneIndent][oneIndent]matlab::data::ObjectArray obj_arr = _mda;\n"
                    + "[rootIndent][oneIndent][oneIndent]raw_objs.push_back(obj_arr[0]);\n"
                    + "[rootIndent][oneIndent]}\n"
                    + "[rootIndent][oneIndent]MATLABSetVectorProperty(u\"[PropertyName]\", raw_objs);\n"
                    + "[rootIndent]}\n";
            }

            if (!property.IsGetAccessible)
            {
                getSection = string.Empty;
            }

            if (!property.IsSetAccessible)
            {
                setSection = string.Empty;
            }

            string content = getSection + setSection;
            content = Rep(content, "[ScalarOrVector]", scalarOrVector);
            content = Rep(content, "[rootIndent]", RootIndents(property.IndentLevel));
            content = Rep(content, "[CppPropertyClass]", property.CppPropertyClass);
            content = Rep(content, "[MatlabPropertyClass]", Rep(property.MatlabPropertyClass, ".", "::"));
            content = Rep(content, "[realNumericType]", realNumericType);
            content = Rep(content, "[scalarBaseType]", scalarBaseType);
            return Rep(content, "[PropertyName]", property.SectionName);
        }

        // --- CppCallable ------------------------------------------------------------------------

        /// <summary>KeywordsCPP.resolveInputArgNameKeywordConflicts: an input named as a C++ keyword gets a '_' in front.</summary>
        private static string[] InputArgNamesCpp(List<Arg> inputs) => [.. inputs.Select(static a => Keywords.Contains(a.Name) ? "_" + a.Name : a.Name)];

        private string CallableText(Callable c)
        {
            bool isFunction = !c.IsMethod;
            string returnType;
            string returnSection;
            switch (c.NumArgOut)
            {
                case 0:
                    returnType = "void";
                    returnSection = c.IsConstructor
                        ? "[rootIndent][oneIndent][matlabPointer]->feval(u\"[EncapsulatingClass]\", 0, _args);"
                        : "[rootIndent][oneIndent][matlabPointer]->feval(u\"[MethodName]\", 0, _args);";
                    break;
                case 1:
                    returnType = "matlab::data::Array";
                    returnSection = c.IsConstructor
                        ? "[rootIndent][oneIndent][returnType] _result = [matlabPointer]->feval(u\"[EncapsulatingClass]\", _args);\n[rootIndent][oneIndent]m_object = _result;"
                        : "[rootIndent][oneIndent][returnType] _result = [matlabPointer]->feval(u\"[MethodName]\", _args);\n[rootIndent][oneIndent]return _result;";
                    break;
                default:
                    // A constructor's outputs after the object are left behind.
                    returnType = "std::vector<matlab::data::Array>";
                    returnSection = c.IsConstructor
                        ? "[rootIndent][oneIndent][returnType] _result = [matlabPointer]->feval(u\"[EncapsulatingClass]\", [NumArgOut], _args);\n[rootIndent][oneIndent]m_object = _result.at(0);"
                        : "[rootIndent][oneIndent][returnType] _result = [matlabPointer]->feval(u\"[MethodName]\", [NumArgOut], _args);\n[rootIndent][oneIndent]return _result;";
                    break;
            }

            returnSection = Rep(returnSection, "[MethodName]", c.SectionName);
            returnSection = Rep(returnSection, "[returnType]", returnType);
            returnSection = Rep(returnSection, "[NumArgOut]", Num(c.NumArgOut));

            string argsString = string.Empty;
            string mlArraysString = string.Empty;
            string enumTransformationSection = string.Empty;
            string vectorTransformationInitSection = string.Empty;
            string vectorTransformationSection = string.Empty;
            bool callsWithPointer = c.IsConstructor || isFunction;
            if (callsWithPointer)
            {
                argsString = "std::shared_ptr<MATLABControllerType> _matlabPtr";
                if (c.NumArgIn > 0)
                {
                    argsString += ", ";
                }
            }

            string[] names = InputArgNamesCpp(c.InputArgs);
            for (int i = 1; i <= c.NumArgIn; i++)
            {
                if (i == 1)
                {
                    mlArraysString = "\n";
                }

                // An instance method's object is the class's own.
                if (i == 1 && !callsWithPointer)
                {
                    mlArraysString += "[rootIndent][oneIndent][oneIndent]m_object";
                    continue;
                }

                if (i == 2 && !callsWithPointer)
                {
                    mlArraysString += ",\n";
                }

                Arg arg = c.InputArgs[i - 1];
                (string cppclass, bool argMatched) = ConvertArg(arg);
                string argname = names[i - 1];
                if (argname == "m_object" || argname == "m_matlabPtr" || argname.StartsWith("MATLAB", StringComparison.Ordinal))
                {
                    argname = "_" + argname;
                }

                // JGraph passes a name-value argument with its name before it, where R2025b passed the
                // value alone at its position, which the function refuses (k12_client: t.nv).
                if (arg.Kind == TypedArgumentKind.NameValue)
                {
                    mlArraysString += "[rootIndent][oneIndent][oneIndent]_arrayFactory.createCharArray(\"" + arg.Name + "\"),\n";
                }

                Dim arrtype = arg.SizeClassification;
                if (arrtype is Dim.Scalar or Dim.Vector)
                {
                    string mclass = arg.ClassName;
                    if (argMatched)
                    {
                        argsString += cppclass + " " + argname;
                        mlArraysString += arrtype == Dim.Vector
                            ? "[rootIndent][oneIndent][oneIndent]_arrayFactory.createArray({1," + argname + ".size()}, " + argname + ".begin(), " + argname + ".end())"
                            : "[rootIndent][oneIndent][oneIndent]_arrayFactory.createArray<" + cppclass + ">({1,1}, {" + argname + "})";
                    }
                    else if (!Restricted(mclass) && mclass.Length > 0)
                    {
                        // A class of the user's, as the C++ class generated for it; packages are namespaces.
                        string translatedClass = Rep(mclass, ".", "::");
                        string mdaStr;
                        if (arrtype == Dim.Scalar)
                        {
                            if (arg.IsEnum)
                            {
                                string getStringFuncCall = "get" + Rep(mclass, ".", "_") + "String(" + argname + ")";
                                enumTransformationSection += "[rootIndent][oneIndent]matlab::data::Array _" + argname + "_Enum = _arrayFactory.createEnumArray({1,1}, \""
                                    + mclass + "\", {" + getStringFuncCall + "});\n";
                                mdaStr = "_" + argname + "_Enum";
                            }
                            else
                            {
                                mdaStr = argname;
                            }
                        }
                        else if (arg.IsEnum)
                        {
                            vectorTransformationSection += "[rootIndent][oneIndent]std::vector<std::string> _" + argname + "_EnumStrVector(" + argname + ".size());\n"
                                + "[rootIndent][oneIndent]std::transform(" + argname + ".begin(), " + argname + ".end(), _" + argname + "_EnumStrVector.begin(), get"
                                + Rep(mclass, ".", "_") + "String);\n"
                                + "[rootIndent][oneIndent]matlab::data::Array _" + argname + "_EnumArray = _arrayFactory.createEnumArray({1," + argname + ".size()}, \""
                                + mclass + "\", _" + argname + "_EnumStrVector);\n";
                            mdaStr = "_" + argname + "_EnumArray";
                            translatedClass = "std::vector<" + translatedClass + ">";
                        }
                        else
                        {
                            vectorTransformationInitSection = "[rootIndent][oneIndent]std::vector<matlab::data::Object> _objHolder;\n"
                                + "[rootIndent][oneIndent]matlab::data::Array _mda;\n"
                                + "[rootIndent][oneIndent]size_t _numobj;\n\n";
                            translatedClass = "std::vector<" + translatedClass + ">";
                            vectorTransformationSection += "[rootIndent][oneIndent]_numobj = " + argname + ".size();\n"
                                + "[rootIndent][oneIndent]_objHolder.clear();\n"
                                + "[rootIndent][oneIndent]for(size_t _i=0; _i<_numobj; _i++) {\n"
                                + "[rootIndent][oneIndent][oneIndent]_mda = " + argname + "[_i];\n"
                                + "[rootIndent][oneIndent][oneIndent]_objHolder.push_back(((matlab::data::ObjectArray)_mda)[0]);\n"
                                + "[rootIndent][oneIndent]}\n"
                                + "[rootIndent][oneIndent]matlab::data::ObjectArray _" + argname + "_ConcatArray = _arrayFactory.createArray({1,_numobj}, _objHolder.begin(), _objHolder.end());\n\n";
                            mdaStr = "_" + argname + "_ConcatArray";
                        }

                        argsString += translatedClass + " " + argname;
                        mlArraysString += "[rootIndent][oneIndent][oneIndent]" + mdaStr;
                    }
                    else
                    {
                        // MATLAB's own classes need no C++ class.
                        argsString += "matlab::data::Array " + argname;
                        mlArraysString += "[rootIndent][oneIndent][oneIndent]" + argname;
                    }
                }
                else
                {
                    argsString += "matlab::data::Array " + argname;
                    mlArraysString += "[rootIndent][oneIndent][oneIndent]" + argname;
                }

                if (i != c.NumArgIn)
                {
                    argsString += ", ";
                    mlArraysString += ",\n";
                }
            }

            c.SectionContent = "[namespaceSection][rootIndent][returnType] [MethodName]([argsString]) { \n"
                + "[rootIndent][oneIndent]matlab::data::ArrayFactory _arrayFactory;\n"
                + "[enumTransformationSection][vectorTransformationInitSection][vectorTransformationSection]"
                + "[rootIndent][oneIndent]std::vector<matlab::data::Array> _args = {[mlArraysString] };\n"
                + "[returnSection]\n"
                + "[rootIndent]}[namespaceClose]\n\n";
            if (c.IsConstructor)
            {
                c.SectionContent = "[rootIndent][MethodName]([argsString]) : \n"
                    + "[rootIndent][oneIndent][objectType]()\n"
                    + "[rootIndent]{ \n"
                    + "[rootIndent][oneIndent][matlabPointer] = _matlabPtr;\n"
                    + "[rootIndent][oneIndent]matlab::data::ArrayFactory _arrayFactory;\n"
                    + "[enumTransformationSection][vectorTransformationInitSection][vectorTransformationSection]"
                    + "[rootIndent][oneIndent]std::vector<matlab::data::Array> _args = {[mlArraysString] };\n"
                    + "[returnSection]\n"
                    + "[rootIndent]}\n\n";
            }

            // The output count as a template's argument: one specialization for each count, from 0.
            bool templated = !c.IsConstructor && c.NumArgOut > 0;
            if (templated)
            {
                var s = new StringBuilder();
                for (int i = 0; i <= c.NumArgOut; i++)
                {
                    s.Append("[rootIndent]template<>\n[rootIndent][returnType").Append(i).Append("] [ClassPrefix][MethodName]<").Append(i).Append(">([argsString]) { \n")
                        .Append("[rootIndent][oneIndent]matlab::data::ArrayFactory _arrayFactory;\n")
                        .Append("[enumTransformationSection][vectorTransformationInitSection][vectorTransformationSection]")
                        .Append("[rootIndent][oneIndent]std::vector<matlab::data::Array> _args = {[mlArraysString] };\n")
                        .Append("[returnSection").Append(i).Append("]\n")
                        .Append("[rootIndent]}\n\n");
                }

                c.SectionContent = s.ToString();
            }

            // Inputs that may be complex are also taken real, in an overload of their own.
            string originalCallable = c.SectionContent.Trim();
            string argsStringOverload = string.Empty;
            string mlArraysStringOverload = string.Empty;
            if (argsString.Contains("std::complex<", StringComparison.Ordinal))
            {
                argsStringOverload = ComplexType.Replace(argsString, "$1");
                mlArraysStringOverload = ComplexType.Replace(mlArraysString, "$1");
                string realOverloadSection = Rep(Rep(originalCallable, "[argsString]", "[argsStringOverload]"), "[mlArraysString]", "[mlArraysStringOverload]");
                c.SectionContent = realOverloadSection + "\n" + c.SectionContent;
            }

            // varargin: one overload taking a vector of arrays, in place of the others.
            string argsStringGenericOverload = string.Empty;
            if (c.IsVarargin)
            {
                string[] args = [.. argsString.Split(',').Select(static a => a.Trim())];
                string generic = "std::vector<matlab::data::Array> _args";
                string overloadGenericSection;
                if (c.IsConstructor)
                {
                    argsStringGenericOverload = args[0] + ", " + generic;
                    overloadGenericSection = "[rootIndent][MethodName]([argsStringGenericOverload]) { \n"
                        + "[rootIndent][oneIndent][matlabPointer] = _matlabPtr;\n"
                        + "[returnSection]\n"
                        + "[rootIndent]}";
                }
                else if (c.IsMethod)
                {
                    argsStringGenericOverload = "std::vector<matlab::data::Array> args";
                    overloadGenericSection = templated
                        ? "[rootIndent]template<>\n[rootIndent][returnType" + Num(c.NumArgOut) + "] [ClassPrefix][MethodName]<" + Num(c.NumArgOut) + ">([argsStringGenericOverload]) { \n"
                            + "[rootIndent][oneIndent]std::vector<matlab::data::Array> _args = { m_object };\n"
                            + "[rootIndent][oneIndent]_args.insert(_args.end(), args.begin(), args.end());\n"
                            + "[returnSection" + Num(c.NumArgOut) + "]\n"
                            + "[rootIndent]}"
                        : "[rootIndent][returnType] [MethodName]([argsStringGenericOverload]) { \n"
                            + "[rootIndent][oneIndent]std::vector<matlab::data::Array> _args = { m_object };\n"
                            + "[rootIndent][oneIndent]_args.insert(_args.end(), args.begin(), args.end());\n"
                            + "[returnSection]\n"
                            + "[rootIndent]}";
                }
                else
                {
                    argsStringGenericOverload = args[0] + ", " + generic;
                    overloadGenericSection = c.NumArgOut > 0
                        ? "[rootIndent]template<>\n[rootIndent][returnType" + Num(c.NumArgOut) + "] [MethodName]<" + Num(c.NumArgOut) + ">([argsStringGenericOverload]) { \n"
                            + "[returnSection" + Num(c.NumArgOut) + "]\n"
                            + "[rootIndent]}"
                        : "[namespaceSection][rootIndent][returnType] [MethodName]([argsStringGenericOverload]) { \n"
                            + "[returnSection]\n"
                            + "[rootIndent]}[namespaceClose]";
                }

                c.SectionContent = "\n" + overloadGenericSection + "\n\n";
            }

            if (templated)
            {
                c.SectionContent = WriteReturnSection(c.OutputArgs, c, c.SectionContent);
            }

            string t = c.SectionContent;
            t = Rep(t, "[returnType]", returnType);
            t = Rep(t, "[argsString]", argsString);
            t = Rep(t, "[argsStringOverload]", argsStringOverload);
            t = Rep(t, "[enumTransformationSection]", enumTransformationSection);
            t = Rep(t, "[vectorTransformationInitSection]", vectorTransformationInitSection);
            t = Rep(t, "[vectorTransformationSection]", vectorTransformationSection);
            t = Rep(t, "[mlArraysString]", mlArraysString);
            t = Rep(t, "[mlArraysStringOverload]", mlArraysStringOverload);
            t = Rep(t, "[argsStringGenericOverload]", argsStringGenericOverload);
            t = Rep(t, "[returnSection]", returnSection);

            string namespaceSection = string.Empty;
            string namespaceClose = string.Empty;
            if (c.IsMethod)
            {
                // A method is named without its class, which the class around it gives.
                t = Rep(t, "[MethodName]", c.ShortName);
                t = Rep(t, "[matlabPointer]", "m_matlabPtr");
                t = Rep(t, "[namespaceSection]", string.Empty);
                t = Rep(t, "[namespaceClose]", string.Empty);
                t = Rep(t, "[EncapsulatingClass]", c.EncapsulatingClass);
                t = Rep(t, "[ClassPrefix]", Last(c.EncapsulatingClass) + "::");
            }
            else
            {
                t = Rep(t, "[MethodName]", c.ShortName);
                t = Rep(t, "[matlabPointer]", "_matlabPtr");
                (namespaceSection, namespaceClose) = GenerateNamespace(c.FullName);
                t = Rep(t, "[namespaceSection]", namespaceSection);
                t = Rep(t, "[namespaceClose]", namespaceClose);
                t = Rep(t, "[ClassPrefix]", string.Empty);
            }

            c.SectionContent = t;
            string typeStructDefSection = string.Empty;
            string defaultTemplateSection = string.Empty;
            if (templated)
            {
                // The specializations move out of the way: a method's go after its class, a function's after its default template.
                c.TemplateSpecSection = c.SectionContent;
                c.SectionContent = c.IsMethod
                    ? "[typeStructDefSection][defaultTemplateSection]\n"
                    : namespaceSection + "[typeStructDefSection][defaultTemplateSection][TemplateSpecSection]" + namespaceClose;
                (typeStructDefSection, string typeStructSpecSection) = WriteTypeStruct(c.OutputArgs, c);
                c.TemplateSpecSection = typeStructSpecSection + c.TemplateSpecSection;
                if (isFunction)
                {
                    c.SectionContent = Rep(c.SectionContent, "[TemplateSpecSection]", c.TemplateSpecSection);
                }

                string tempSection = WriteTemplateDefault(c.OutputArgs, c);
                defaultTemplateSection = tempSection;
                if (argsStringOverload.Length > 0)
                {
                    defaultTemplateSection += "\n" + Rep(tempSection, "[argsString]", "[argsStringOverload]");
                }

                if (argsStringGenericOverload.Length > 0)
                {
                    defaultTemplateSection += "\n" + Rep(tempSection, "[argsString]", "[argsStringGenericOverload]");
                }

                defaultTemplateSection = Rep(defaultTemplateSection, "[argsString]", argsString);
                defaultTemplateSection = Rep(defaultTemplateSection, "[argsStringOverload]", argsStringOverload);
                defaultTemplateSection = Rep(defaultTemplateSection, "[argsStringGenericOverload]", argsStringGenericOverload);
            }

            c.SectionContent = Rep(c.SectionContent, "[typeStructDefSection]", typeStructDefSection);
            c.SectionContent = Rep(c.SectionContent, "[defaultTemplateSection]", defaultTemplateSection);
            c.SectionContent = Rep(c.SectionContent, "[rootIndent]", RootIndents(c.IndentLevel));
            return c.SectionContent;
        }

        /// <summary>
        /// utilcpp.getOutputTypeCategory, with what R2025b leaves unset (a typed output of MATLAB's own
        /// class, or of a size it cannot use) read as the MathWorks class its comments say it is.
        /// </summary>
        private static Category Categorize(Arg output)
        {
            Dim sizeType = output.SizeClassification;
            (string cppType, bool matched) = ConvertArg(output);
            if (!output.HasSize || !output.HasType)
            {
                return Category.MissingMeta;
            }

            if (sizeType == Dim.MultiDim)
            {
                return Category.MultiDim;
            }

            bool complex = cppType.Contains("std::complex", StringComparison.Ordinal);
            if (matched && !complex)
            {
                if (sizeType == Dim.Scalar || (sizeType == Dim.Vector && output.ClassName == "char"))
                {
                    return Category.SimpleScalar;
                }

                if (sizeType == Dim.Vector)
                {
                    return Category.SimpleVector;
                }
            }
            else if (matched)
            {
                if (sizeType == Dim.Scalar)
                {
                    return Category.SimpleComplexScalar;
                }

                if (sizeType == Dim.Vector)
                {
                    return Category.SimpleComplexVector;
                }
            }
            else if (output.IsEnum && output.IsExternal)
            {
                if (sizeType == Dim.Scalar)
                {
                    return Category.GenEnumScalar;
                }

                if (sizeType == Dim.Vector)
                {
                    return Category.GenEnumVector;
                }
            }
            else if (output.IsExternal)
            {
                if (sizeType == Dim.Scalar)
                {
                    return Category.GenExternalClassScalar;
                }

                if (sizeType == Dim.Vector)
                {
                    return Category.GenExternalClassVector;
                }
            }

            return Category.MWAuthoredClass;
        }

        /// <summary>The C++ type an output is returned as (writeReturnSection's and writeTypeStruct's last loops).</summary>
        private static string OutputType(Arg output)
        {
            Category category = Categorize(output);
            string ctype = ConvertArg(output).Cpp;
            switch (category)
            {
                case Category.SimpleComplexVector:
                    ctype = "matlab::data::Array";
                    break;
                case Category.GenEnumScalar or Category.GenExternalClassScalar:
                    ctype = Rep(output.ClassName, ".", "::");
                    break;
                case Category.GenEnumVector or Category.GenExternalClassVector:
                    ctype = Rep("std::vector<" + output.ClassName + ">", ".", "::");
                    break;
            }

            return ctype.Length == 0 ? "matlab::data::Array" : ctype;
        }

        /// <summary>utilcpp.writeReturnSection: each output count's call and the conversion of what it returns.</summary>
        private static string WriteReturnSection(List<Arg> outputArgs, Callable caller, string cppIn)
        {
            string cppOut = cppIn;
            int numOut = outputArgs.Count;
            cppOut = Rep(cppOut, "[returnSection0]", "[rootIndent][oneIndent][matlabPointer]->feval(u\"[MethodOrFuncName]\", 0, _args);");

            string complexCatchClassSection = string.Empty;
            string complexTransformSection = string.Empty;
            string returnSection1 = "[rootIndent][oneIndent]matlab::data::Array _result_mda = [matlabPointer]->feval(u\"[MethodOrFuncName]\", _args);\n"
                + "[complexCatchClassSection][complexTransformSection]"
                + "[rootIndent][oneIndent][returnType1] _result;\n"
                + "[rootIndent][oneIndent][castSection]\n"
                + "[rootIndent][oneIndent]return _result;";
            string castSection = string.Empty;
            const string useSimpleScalar = "_result = MatlabTypesInterface::convertMDAtoScalar<[out1]>(_result_mda);";
            const string useMda = "_result = _result_mda;";
            Arg first = outputArgs[0];
            switch (Categorize(first))
            {
                case Category.MissingMeta or Category.MultiDim or Category.MWAuthoredClass or Category.SimpleComplexVector:
                    castSection = useMda;
                    break;
                case Category.SimpleComplexScalar:
                    // Caught as complex, so an array whose numbers have no imaginary part still converts.
                    complexCatchClassSection = "[rootIndent][oneIndent]MatlabTypesInterface::ConvertToComplex _converterObj;\n";
                    complexTransformSection = "[rootIndent][oneIndent]_result_mda = matlab::data::apply_visitor(_result_mda, _converterObj);\n";
                    castSection = useSimpleScalar;
                    break;
                case Category.SimpleScalar:
                    castSection = useSimpleScalar;
                    break;
                case Category.SimpleVector:
                    castSection = "_result = MatlabTypesInterface::convertMDAtoVector<[out1_vectorBaseType]>(_result_mda);";
                    break;
                case Category.GenEnumScalar:
                {
                    string enumName = first.ClassName;
                    returnSection1 = "[rootIndent][oneIndent]matlab::data::Array _result_mda = [matlabPointer]->feval(u\"[MethodOrFuncName]\", _args);\n"
                        + "[rootIndent][oneIndent]" + Rep(enumName, ".", "::") + " _ret;\n"
                        + "[rootIndent][oneIndent]const std::string& _str = _result_mda[0];\n"
                        + "[rootIndent][oneIndent]_ret = get" + Rep(enumName, ".", "_") + "Enum(_str);\n"
                        + "[rootIndent][oneIndent]return _ret;";
                    break;
                }

                case Category.GenEnumVector:
                {
                    string getSection = Rep(first.ClassName, ".", "_");
                    string enumName = Rep(first.ClassName, ".", "::");
                    returnSection1 = "[rootIndent][oneIndent]matlab::data::Array _result_mda = [matlabPointer]->feval(u\"[MethodOrFuncName]\", _args);\n"
                        + "[rootIndent][oneIndent]std::vector<" + enumName + "> _objects;\n"
                        + "[rootIndent][oneIndent]for(size_t k=0; k<_result_mda.getNumberOfElements(); k++){\n"
                        + "[rootIndent][oneIndent][oneIndent]_objects.push_back(get" + getSection + "Enum(_result_mda[k]));\n"
                        + "[rootIndent][oneIndent]}\n"
                        + "[rootIndent][oneIndent] return _objects;";
                    break;
                }

                case Category.GenExternalClassScalar:
                {
                    string className = Rep(first.ClassName, ".", "::");
                    returnSection1 = "[rootIndent][oneIndent]matlab::data::Array _result_mda = [matlabPointer]->feval(u\"[MethodOrFuncName]\", _args);\n"
                        + "[rootIndent][oneIndent]" + className + " _object([matlabPointer], _result_mda);\n"
                        + "[rootIndent][oneIndent]return _object;";
                    break;
                }

                case Category.GenExternalClassVector:
                {
                    string className = Rep(first.ClassName, ".", "::");
                    returnSection1 = "[rootIndent][oneIndent]matlab::data::Array _result_mda = [matlabPointer]->feval(u\"[MethodOrFuncName]\", _args);\n"
                        + "[rootIndent][oneIndent]matlab::data::TypedArray<matlab::data::Object> _MDAobjects = static_cast<matlab::data::TypedArray<matlab::data::Object> >(_result_mda);\n"
                        + "[rootIndent][oneIndent]std::vector<matlab::data::Object> _vectorOfObjects (_MDAobjects.cbegin(), _MDAobjects.cend());\n"
                        + "[rootIndent][oneIndent]std::vector<" + className + "> _objects;\n"
                        + "[rootIndent][oneIndent]for(size_t k=0; k<_vectorOfObjects.size(); k++){\n"
                        + "[rootIndent][oneIndent][oneIndent] matlab::data::ObjectArray _obj_arr = _arrayFactory.createScalar(_vectorOfObjects.at(k));\n"
                        + "[rootIndent][oneIndent][oneIndent]_objects.push_back(" + className + "([matlabPointer],_obj_arr));\n"
                        + "[rootIndent][oneIndent]}\n"
                        + "[rootIndent][oneIndent] return _objects;";
                    break;
                }
            }

            returnSection1 = Rep(returnSection1, "[complexCatchClassSection]", complexCatchClassSection);
            returnSection1 = Rep(returnSection1, "[complexTransformSection]", complexTransformSection);
            returnSection1 = Rep(returnSection1, "[castSection]", castSection);
            cppOut = Rep(cppOut, "[returnSection1]", returnSection1);

            // Two outputs or more come back as a tuple, each element converted in turn.
            for (int x = 2; x <= numOut; x++)
            {
                string returnSectionX = "[rootIndent][oneIndent]std::vector<matlab::data::Array> _result_mda = [matlabPointer]->feval(u\"[MethodOrFuncName]\"," + Num(x) + ", _args);\n"
                    + "[complexCatchClassSection]"
                    + "[rootIndent][oneIndent][returnType" + Num(x) + "] _result;"
                    + "[castSection]\n"
                    + "[rootIndent][oneIndent]return _result;";
                castSection = string.Empty;
                complexCatchClassSection = string.Empty;
                for (int i = 1; i <= x; i++)
                {
                    string at = Num(i - 1);
                    string castPart = "\n[rootIndent][oneIndent]std::get<" + at + ">(_result) = MatlabTypesInterface::convertMDAtoScalar<[out" + Num(i) + "]>([complexTransformSection]);";
                    string useMdaPart = "\n[rootIndent][oneIndent]std::get<" + at + ">(_result) = _result_mda.at(" + at + ");";
                    Arg output = outputArgs[i - 1];
                    switch (Categorize(output))
                    {
                        case Category.MissingMeta or Category.MultiDim or Category.MWAuthoredClass:
                            castPart = useMdaPart;
                            break;
                        case Category.SimpleComplexScalar:
                            complexTransformSection = "matlab::data::apply_visitor(_result_mda.at(" + at + "), _converterObj)";
                            complexCatchClassSection = "[rootIndent][oneIndent]MatlabTypesInterface::ConvertToComplex _converterObj;\n";
                            break;
                        case Category.SimpleScalar:
                            complexTransformSection = "_result_mda.at(" + at + ")";
                            break;
                        case Category.SimpleVector:
                            castPart = "\n[rootIndent][oneIndent]std::get<" + at + ">(_result) = MatlabTypesInterface::convertMDAtoVector<[out" + Num(i) + "_vectorBaseType]>(_result_mda.at(" + at + "));";
                            break;
                        case Category.SimpleComplexVector:
                            castPart = "\n[rootIndent][oneIndent]std::get<" + at + ">(_result) = _result_mda.at(" + at + ");";
                            break;
                        case Category.GenEnumScalar:
                            castPart = "\n[rootIndent][oneIndent]const std::string& _str" + Num(i) + " = _result_mda.at(" + at + ")[0];\n"
                                + "[rootIndent][oneIndent]std::get<" + at + ">(_result) = get" + Rep(output.ClassName, ".", "_") + "Enum(_str" + Num(i) + ");";
                            break;
                        case Category.GenEnumVector:
                        {
                            string enumName = Rep(output.ClassName, ".", "_");
                            string enumWithScope = Rep(output.ClassName, ".", "::");
                            castPart = "\n[rootIndent][oneIndent]std::vector<" + enumWithScope + "> _objects" + Num(i) + ";\n"
                                + "[rootIndent][oneIndent] matlab::data::Array _enumValues" + Num(i) + "= _result_mda.at(" + at + ");\n"
                                + "[rootIndent][oneIndent]for(size_t k=0; k<_enumValues" + Num(i) + ".getNumberOfElements(); k++){\n"
                                + "[rootIndent][oneIndent][oneIndent]std::string _enumString" + Num(i) + " = _enumValues" + Num(i) + "[k];\n"
                                + "[rootIndent][oneIndent][oneIndent]_objects" + Num(i) + ".push_back(get" + enumName + "Enum(_enumString" + Num(i) + "));\n"
                                + "[rootIndent][oneIndent]}\n"
                                + "[rootIndent][oneIndent]std::get<" + at + ">(_result) = _objects" + Num(i) + ";\n";
                            break;
                        }

                        case Category.GenExternalClassScalar:
                            castPart = Rep("\n[rootIndent][oneIndent]" + output.ClassName + " _obj" + Num(i) + " ([matlabPointer], _result_mda[" + at + "]);\n"
                                + "[rootIndent][oneIndent]std::get<" + at + ">(_result) = _obj" + Num(i) + ";", ".", "::");
                            break;
                        case Category.GenExternalClassVector:
                        {
                            string classNameWithScope = Rep(output.ClassName, ".", "::");
                            castPart = "\n[rootIndent][oneIndent]matlab::data::TypedArray<matlab::data::Object> _MDAobjects" + Num(i)
                                + " = static_cast<matlab::data::TypedArray<matlab::data::Object> >(_result_mda[" + at + "]);\n"
                                + "[rootIndent][oneIndent]std::vector<matlab::data::Object> _vectorOfObjects" + Num(i) + " (_MDAobjects" + Num(i) + ".cbegin(), _MDAobjects" + Num(i) + ".cend());\n"
                                + "[rootIndent][oneIndent]std::vector<" + classNameWithScope + " > _objects" + Num(i) + ";\n"
                                + "[rootIndent][oneIndent]for(size_t k=0; k<_vectorOfObjects" + Num(i) + ".size(); k++){\n"
                                + "[rootIndent][oneIndent][oneIndent] matlab::data::ObjectArray _obj_arr" + Num(i) + " = _arrayFactory.createScalar(_vectorOfObjects" + Num(i) + ".at(k));\n"
                                + "[rootIndent][oneIndent][oneIndent]_objects" + Num(i) + ".push_back(" + classNameWithScope + "([matlabPointer],_obj_arr" + Num(i) + "));\n"
                                + "[rootIndent][oneIndent]}\n"
                                + "[rootIndent][oneIndent]std::get<" + at + ">(_result) = _objects" + Num(i) + ";";
                            break;
                        }
                    }

                    castSection += Rep(castPart, "[complexTransformSection]", complexTransformSection);
                }

                returnSectionX = Rep(returnSectionX, "[complexCatchClassSection]", complexCatchClassSection);
                returnSectionX = Rep(returnSectionX, "[castSection]", castSection);
                cppOut = Rep(cppOut, "[returnSection" + Num(x) + "]", returnSectionX);
            }

            var returnTypes = new List<string> { "void", "[out1]" };
            string tupleType = "[out1]";
            for (int i = 2; i <= numOut; i++)
            {
                tupleType += ", [out" + Num(i) + "]";
                returnTypes.Add("std::tuple<" + tupleType + ">");
            }

            for (int i = 0; i < returnTypes.Count; i++)
            {
                cppOut = Rep(cppOut, "[returnType" + Num(i) + "]", returnTypes[i]);
            }

            for (int i = 1; i <= numOut; i++)
            {
                string ctype = OutputType(outputArgs[i - 1]);
                cppOut = Rep(cppOut, "[out" + Num(i) + "]", ctype);
                if (ctype.Contains("std::vector", StringComparison.Ordinal))
                {
                    cppOut = Rep(cppOut, "[out" + Num(i) + "_vectorBaseType]", Regex.Match(ctype, "std::vector<(.*)>").Groups[1].Value);
                }
            }

            if (caller.IsMethod)
            {
                cppOut = Rep(cppOut, "[matlabPointer]", "m_matlabPtr");
                return Rep(cppOut, "[MethodOrFuncName]", caller.SectionName);
            }

            cppOut = Rep(cppOut, "[matlabPointer]", "_matlabPtr");
            return Rep(cppOut, "[MethodOrFuncName]", caller.FullName);
        }

        /// <summary>utilcpp.writeTypeStruct: the struct whose specializations give each output count's type.</summary>
        private static (string Def, string Spec) WriteTypeStruct(List<Arg> outputArgs, Callable caller)
        {
            string def = "[private:]\ntemplate<size_t nargout>\nstruct return_type_[name] { typedef void type; };\n\n[public:]";
            string spec = "template<>\nstruct [prefix]return_type_[name]<0> { typedef void type; };\n\n"
                + "template<>\nstruct [prefix]return_type_[name]<1> { typedef [out1] type; };\n\n";
            string tupleType = "[out1]";
            for (int i = 2; i <= outputArgs.Count; i++)
            {
                tupleType += ", [out" + Num(i) + "]";
                spec += "template<>\nstruct [prefix]return_type_[name]<" + Num(i) + "> { typedef std::tuple<" + tupleType + "> type; };\n\n";
            }

            for (int i = 1; i <= outputArgs.Count; i++)
            {
                spec = Rep(spec, "[out" + Num(i) + "]", OutputType(outputArgs[i - 1]));
            }

            if (caller.IsMethod)
            {
                def = Rep(Rep(Rep(def, "[private:]", "private:"), "[public:]", "public:"), "[name]", caller.SectionName);
                spec = Rep(Rep(spec, "[name]", caller.SectionName), "[prefix]", Last(caller.EncapsulatingClass) + "::");
            }
            else
            {
                string funcName = Rep(caller.FullName, ".", "_");
                def = Rep(Rep(Rep(def, "[private:]", string.Empty), "[public:]", string.Empty), "[name]", funcName);
                spec = Rep(Rep(spec, "[name]", funcName), "[prefix]", string.Empty);
            }

            return (RootIndentCode(def) + "\n", RootIndentCode(spec));
        }

        /// <summary>utilcpp.writeTemplateDefault: the template a call with too many outputs fails to compile against.</summary>
        private static string WriteTemplateDefault(List<Arg> outputArgs, Callable caller)
        {
            string cppOut = "template<size_t nargout = 1>\ntypename return_type_[name]<nargout>::type [localName]([argsString]) {\n"
                + "[oneIndent]static_assert(nargout<=[numOutputArgs], \"Too many outputs specified. Maximum outputs is [numOutputArgs].\");\n}";
            cppOut = Rep(cppOut, "[numOutputArgs]", Num(outputArgs.Count));
            if (caller.IsMethod)
            {
                cppOut = Rep(Rep(cppOut, "[name]", caller.SectionName), "[localName]", caller.SectionName);
            }
            else
            {
                cppOut = Rep(Rep(cppOut, "[name]", Rep(caller.FullName, ".", "_")), "[localName]", Last(caller.FullName));
            }

            return RootIndentCode(cppOut) + "\n";
        }
    }
}
