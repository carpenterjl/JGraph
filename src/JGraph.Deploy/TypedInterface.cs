using System.Globalization;
using System.Text;

namespace JGraph.Deploy;

/// <summary>How an argument is passed: by position, as a name-value pair, or as one of a repeating group.</summary>
public enum TypedArgumentKind
{
    Positional,
    NameValue,
    Repeating,
}

/// <summary>
/// One input or output of a function or method as its <c>arguments</c> block declares it: what R2025b's
/// metafunction reports and its strongly typed generator reads (MATLABInfo, ArgumentTpl).
/// </summary>
/// <param name="Name">The argument's name; a name-value argument's is the name a caller passes.</param>
public sealed record TypedArgument(string Name)
{
    /// <summary>Positional, name-value or repeating.</summary>
    public TypedArgumentKind Kind { get; init; }

    /// <summary>Whether the caller may leave it out: a default, or a name-value argument.</summary>
    public bool Optional { get; init; }

    /// <summary>The declared class, or null.</summary>
    public string? ClassName { get; init; }

    /// <summary>The declared size, a null entry for <c>:</c>; null when none is declared.</summary>
    public IReadOnlyList<int?>? Size { get; init; }

    /// <summary>Whether a validator is <c>mustBeReal</c>.</summary>
    public bool MustBeReal { get; init; }
}

/// <summary>A function the interface wraps: its name with any package prefix, and its arguments.</summary>
public sealed record TypedFunction(string FullName, IReadOnlyList<TypedArgument> Inputs, IReadOnlyList<TypedArgument> Outputs);

/// <summary>A class's public property, as R2025b's PropertyTpl reads it.</summary>
/// <param name="Name">The property's name.</param>
public sealed record TypedProperty(string Name)
{
    /// <summary>The declared class, or null (R2025b's "unknown").</summary>
    public string? ClassName { get; init; }

    /// <summary>The declared size with <c>:</c> as null; null when none is declared.</summary>
    public IReadOnlyList<int?>? Size { get; init; }

    /// <summary>Whether the last validator is <c>mustBeReal</c>, which is how R2025b's PropertyTpl reads it.</summary>
    public bool MustBeReal { get; init; }

    /// <summary>Whether a client may read it.</summary>
    public bool Readable { get; init; }

    /// <summary>Whether a client may write it.</summary>
    public bool Writable { get; init; }
}

/// <summary>A class's method, its object first among an instance method's inputs.</summary>
public sealed record TypedMethod(string Name, IReadOnlyList<TypedArgument> Inputs, IReadOnlyList<TypedArgument> Outputs)
{
    /// <summary>Whether the method is static.</summary>
    public bool Static { get; init; }
}

/// <summary>A class the interface wraps: a value or handle class, or an enumeration.</summary>
public sealed record TypedClass(string FullName)
{
    /// <summary>Whether it derives from handle (a C# class; a value class is a C# struct).</summary>
    public bool IsHandle { get; init; }

    /// <summary>The members an enumeration declares, not hidden; empty for any other class.</summary>
    public IReadOnlyList<string> Enumeration { get; init; } = [];

    /// <summary>The public properties, in the order the class lists them.</summary>
    public IReadOnlyList<TypedProperty> Properties { get; init; } = [];

    /// <summary>The constructor.</summary>
    public TypedMethod Constructor { get; init; } = new("", [], [new TypedArgument("obj")]);

    /// <summary>The accessible methods in R2025b's meta.class order, a handle class's eq, ne, lt, gt, le, ge, delete and isvalid after its own.</summary>
    public IReadOnlyList<TypedMethod> Methods { get; init; } = [];
}

/// <summary>A strongly typed interface could not be generated, for <see cref="Exception.Message"/>'s reason.</summary>
public sealed class TypedInterfaceException(string identifier, string message) : Exception(message)
{
    /// <summary>R2025b's identifier for the reason.</summary>
    public string Identifier { get; } = identifier;
}

/// <summary>
/// The C# strongly typed interface R2025b writes beside a MATLAB Data API archive (stage K9, ADR
/// 0265), transcribed from its toolbox/matlab/external/engines/codegen/csharp (CSharpCodeGenerator,
/// CSharpTypeConverter, CSharpStaticClass, CSharpNonStaticClass, CSharpEnum and their methods): one
/// file per function, a static partial class of its overloads; one per class, a struct (value
/// class) or class (handle class) over the MATLAB object; one per enumeration. JGraph's header and
/// namespaces, and two of R2025b's defects corrected: an instance method whose only input is optional
/// was written with a dangling comma (k9_build: optmethod, which did not compile), and name-value
/// arguments were passed by position (k9_client: typed.nv.2, refused by every such function).
/// </summary>
public static class TypedInterface
{
    private const string OneIndent = "    ";

    /// <summary>The C# keywords and contextual words R2025b's generator prefixes with '@' (fixCSharpKeywordConflict).</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "add", "and", "alias", "ascending", "args", "async", "await", "base", "bool", "break", "by", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "descending", "do", "double", "else", "enum",
        "equals", "event", "explicit", "extern", "false", "file", "finally", "for", "foreach", "from", "get", "global", "goto", "group",
        "if", "implicit", "in", "init", "int", "interface", "internal", "into", "is", "join", "let", "lock", "long", "managed", "nameof",
        "namespace", "new", "nint", "not", "notnull", "nuint", "null", "object", "on", "operator", "or", "orderby", "out", "override",
        "params", "partial", "private", "protected", "public", "readonly", "record", "ref", "remove", "required", "return", "sbyte",
        "scoped", "sealed", "select", "set", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unmanaged", "unsafe", "ushort", "using", "value", "var", "virtual",
        "void", "volatile", "where", "while", "with", "yield",
    };

    /// <summary>R2025b's conversion table (CSharpTypeConverter): C# type, MATLAB class, scalar, real (1), complex (0) or either (-1).</summary>
    private static readonly (string CSharp, string Matlab, bool Scalar, int Real)[] Table =
    [
        ("double", "double", true, 1), ("double [[CommaToken]]", "double", false, 1),
        ("System.Numerics.Complex", "double", true, 0), ("System.Numerics.Complex [[CommaToken]]", "double", false, 0),
        ("Single", "single", true, 1), ("Single [[CommaToken]]", "single", false, 1),
        ("dynamic", "single", true, 0), ("dynamic", "single", false, 0),
        ("byte", "uint8", true, 1), ("byte [[CommaToken]]", "uint8", false, 1), ("dynamic", "uint8", true, 0), ("dynamic", "uint8", false, 0),
        ("UInt16", "uint16", true, 1), ("UInt16 [[CommaToken]]", "uint16", false, 1), ("dynamic", "uint16", true, 0), ("dynamic", "uint16", false, 0),
        ("uint", "uint32", true, 1), ("uint [[CommaToken]]", "uint32", false, 1), ("dynamic", "uint32", true, 0), ("dynamic", "uint32", false, 0),
        ("UInt64", "uint64", true, 1), ("UInt64 [[CommaToken]]", "uint64", false, 1), ("dynamic", "uint64", true, 0), ("dynamic", "uint64", false, 0),
        ("sbyte", "int8", true, 1), ("sbyte [[CommaToken]]", "int8", false, 1), ("dynamic", "int8", true, 0), ("dynamic", "int8", false, 0),
        ("Int16", "int16", true, 1), ("Int16 [[CommaToken]]", "int16", false, 1), ("dynamic", "int16", true, 0), ("dynamic", "int16", false, 0),
        ("int", "int32", true, 1), ("int [[CommaToken]]", "int32", false, 1), ("dynamic", "int32", true, 0), ("dynamic", "int32", false, 0),
        ("Int64", "int64", true, 1), ("Int64 [[CommaToken]]", "int64", false, 1), ("dynamic", "int64", true, 0), ("dynamic", "int64", false, 0),
        ("bool", "logical", true, -1), ("bool[[CommaToken]]", "logical", false, -1),
        ("[MyEnumClass]", "[MyEnumClass]", true, -1), ("[MyEnumClass] [[CommaToken]]", "[MyEnumClass]", false, -1),
        ("char", "char", true, -1), ("char [[CommaToken]]", "char", false, -1),
        ("string", "string", true, -1), ("string [[CommaToken]]", "string", false, -1),
        ("MATLABStruct", "struct", true, -1), ("MATLABStruct[[CommaToken]]", "struct", false, -1),
        ("dynamic", "dictionary", true, -1), ("dynamic", "dictionary", false, -1),
    ];

    private enum Dim
    {
        Unknown,
        Empty,
        Scalar,
        Vector,
        MultiDim,
    }

    /// <summary>
    /// The interface's files, file name and text, for <paramref name="functions"/> and
    /// <paramref name="classes"/> under the namespace <paramref name="outerNamespace"/>, dated
    /// <paramref name="date"/>; <paramref name="isClass"/> says whether a name is a MATLAB class.
    /// </summary>
    /// <exception cref="TypedInterfaceException">A function takes varargin or gives varargout, which no strongly typed interface wraps.</exception>
    public static IReadOnlyList<(string File, string Text)> Generate(
        IReadOnlyList<TypedFunction> functions, IReadOnlyList<TypedClass> classes, string outerNamespace, DateTime date,
        Func<string, bool> isClass, string version)
    {
        foreach (TypedFunction function in functions)
        {
            string section = function.FullName;
            if (function.Inputs.Any(static a => a.Kind == TypedArgumentKind.Repeating))
            {
                throw new TypedInterfaceException("MATLAB:engine_codegen:FunctionVararginNotSupported",
                    $"Function '{section}' takes varargin, which a strongly typed interface does not support.");
            }

            if (function.Outputs.Any(static a => a.Kind == TypedArgumentKind.Repeating))
            {
                throw new TypedInterfaceException("MATLAB:engine_codegen:FunctionVarargoutNotSupported",
                    $"Function '{section}' gives varargout, which a strongly typed interface does not support.");
            }
        }

        var converter = new Converter(isClass);
        var files = new List<(string File, string Text)>();
        foreach (TypedClass cls in classes)
        {
            if (cls.Enumeration.Count > 0)
            {
                string name = Fix(Last(cls.FullName));
                files.Add((name, EnumSource(cls, outerNamespace)));
            }
            else
            {
                files.Add((cls.FullName.Replace('.', '_'), ClassSource(cls, outerNamespace, converter)));
            }
        }

        foreach (TypedFunction function in functions)
        {
            files.Add((function.FullName.Replace('.', '_'), FunctionSource(function, outerNamespace, converter)));
        }

        return [.. files.Select(f => (f.File + ".cs", Header(f.File + ".cs", date, version) + f.Text))];
    }

    /// <summary>R2025b's header (CSharpIncludes) with JGraph's version and namespaces.</summary>
    private static string Header(string file, DateTime date, string version) =>
        $"/* File: {file}\n*\n* JGraph Strongly Typed Interface Version: {version} (R2025b)\n* C# source code generated on: "
        + date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) + "\n*/\nusing System;\nusing JGraph.MATLAB.Types;\nusing JGraph.MATLAB.Exceptions;\n";

    private static string Last(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];

    private static string Fix(string name) => Keywords.Contains(name) ? "@" + name : name;

    private static string FixFull(string fullName) => string.Join(".", fullName.Split('.').Select(Fix));

    /// <summary>The C# namespace a full name's package prefix gives (generateCSharpNamespace), under the outer one.</summary>
    private static string Namespace(string fullName, string outer)
    {
        string[] parts = fullName.Split('.');
        string own = string.Join(".", parts[..^1].Select(Fix));
        return outer.Length > 0 && own.Length > 0 ? outer + "." + own : outer.Length > 0 ? outer : own;
    }

    // --- Types ---------------------------------------------------------------------------------

    /// <summary>R2025b's CSharpTypeConverter.</summary>
    private sealed class Converter(Func<string, bool> isClass)
    {
        /// <summary>An argument's C# type (convertArg).</summary>
        public string Of(TypedArgument argument)
        {
            (Dim dim, int trueDimension) = Classify(argument.Size);
            return Convert(argument.ClassName ?? string.Empty, dim, argument.MustBeReal ? 1 : 0, trueDimension, null);
        }

        /// <summary>A property's C# type (CSharpProperty.convertProperty).</summary>
        public string Of(TypedProperty property)
        {
            Dim dim;
            int?[]? dims = property.Size?.ToArray();
            if (dims is null || property.ClassName is null && dims is null)
            {
                dim = Dim.MultiDim;
            }
            else
            {
                dim = ClassifyFixed(dims);
            }

            return Convert(property.ClassName ?? "unknown", dim, property.MustBeReal ? 1 : 0, -2, dims);
        }

        private string Convert(string matlab, Dim dim, int real, int trueDimension, int?[]? propertyDims)
        {
            bool scalar = dim == Dim.Scalar;
            bool vector = dim == Dim.Vector;
            bool multi = dim == Dim.MultiDim;
            if (!(scalar || vector || multi))
            {
                return "dynamic";
            }

            string Commas() => trueDimension != -2
                ? new string(',', Math.Max(0, trueDimension - 1))
                : multi ? new string(',', Math.Max(0, propertyDims!.Length - 1)) : string.Empty;

            (string CSharp, string Matlab, bool Scalar, int Real)[] matches =
                [.. Table.Where(t => t.Matlab == matlab && t.Scalar == scalar && (t.Real == real || t.Real == -1))];
            if (matches.Length == 1)
            {
                string type = matches[0].CSharp;
                return vector || multi ? type.Replace("[CommaToken]", Commas(), StringComparison.Ordinal) : type;
            }

            if (matlab == "cell")
            {
                return scalar ? "object []" : "object [" + Commas() + "]";
            }

            if (matlab.Length > 0 && isClass(matlab))
            {
                return vector || multi ? matlab + "[" + Commas() + "]" : matlab;
            }

            return "dynamic";
        }

        /// <summary>classifyArraySize2: an argument's size, by dimensions that may be longer than one.</summary>
        private static (Dim Dim, int TrueDimension) Classify(IReadOnlyList<int?>? size)
        {
            if (size is null)
            {
                return (Dim.Unknown, -2);
            }

            if (size.Count < 2 || size.Any(static d => d == 0))
            {
                return (Dim.Empty, -1);
            }

            int longer = size.Count(static d => d is null || d > 1);
            return (longer == 0 ? Dim.Scalar : longer == 1 ? Dim.Vector : Dim.MultiDim, longer);
        }

        /// <summary>classifyArraySize: a property's size, a ':' counting as longer than one.</summary>
        private static Dim ClassifyFixed(int?[] dims)
        {
            if (dims.Length < 2 || dims.Any(static d => d == 0))
            {
                return Dim.Empty;
            }

            int longer = dims.Count(static d => d is null || d > 1);
            return longer == 0 ? Dim.Scalar : longer == 1 ? Dim.Vector : Dim.MultiDim;
        }
    }

    // --- Functions -----------------------------------------------------------------------------

    /// <summary>CSharpStaticClass: a function's file, a static partial class of its overloads.</summary>
    private static string FunctionSource(TypedFunction function, string outer, Converter converter)
    {
        string ns = Namespace(function.FullName, outer);
        string root = ns.Length > 0 ? OneIndent : string.Empty;
        var methods = new StringBuilder();
        int optional = function.Inputs.Count(static a => a.Optional);
        for (int i = 0; i <= optional; i++)
        {
            for (int j = 0; j <= function.Outputs.Count; j++)
            {
                methods.Append(new Overload(function.FullName, function.Inputs, function.Outputs, i, j, converter, OverloadKind.Function).Text(root, null));
            }
        }

        return Wrapped(ns, string.Empty, root + "public static partial class MATLABFunctions { \n" + methods + root + "}\n");
    }

    /// <summary>The namespace around a file's body: <c>namespace N{</c> and its close, or nothing.</summary>
    private static string Wrapped(string ns, string attribute, string body) =>
        (ns.Length > 0 ? "namespace " + ns + "{" : string.Empty) + "\n" + attribute + "\n" + body + (ns.Length > 0 ? "}" : string.Empty) + "\n";

    private enum OverloadKind
    {
        Function,
        Instance,
        Static,
        Constructor,
    }

    /// <summary>One generated method: CSharpStaticMethod, CSharpInstanceMethod or CSharpConstructor over CSharpMethod.</summary>
    private sealed class Overload
    {
        private readonly string _fullName;
        private readonly IReadOnlyList<TypedArgument> _inputs;
        private readonly IReadOnlyList<TypedArgument> _outputs;
        private readonly int _optional;
        private readonly int _nargout;
        private readonly OverloadKind _kind;
        private readonly string[] _inputNames;
        private readonly string[] _outputNames;
        private readonly string[] _inputTypes;
        private readonly string[] _outputTypes;
        private readonly List<int> _refIn = [];
        private readonly List<int> _refOut = [];

        public Overload(string fullName, IReadOnlyList<TypedArgument> inputs, IReadOnlyList<TypedArgument> outputs, int optional, int nargout,
            Converter converter, OverloadKind kind)
        {
            _fullName = fullName;
            _inputs = inputs;
            _outputs = outputs;
            _optional = optional;
            _nargout = nargout;
            _kind = kind;
            _inputTypes = [.. inputs.Where(static a => a.Kind != TypedArgumentKind.Repeating).Select(converter.Of)];
            _outputTypes = [.. outputs.Where(static a => a.Kind != TypedArgumentKind.Repeating).Select(converter.Of)];
            _inputNames = [.. inputs.Where(static a => a.Name != "varargin").Select(static a => Fix(a.Name))];
            string[] outputNames = [.. outputs.Select(static a => Fix(a.Name))];
            _outputNames = HandleRefCase(outputNames, kind == OverloadKind.Instance ? 1 : 0);
        }

        /// <summary>handleRefCase: an output named as an input of the same type is passed by ref; of another type, renamed with '_'.</summary>
        private string[] HandleRefCase(string[] outputNames, int skip)
        {
            var named = new List<string>();
            for (int i = 0; i < _nargout; i++)
            {
                bool matched = false;
                for (int j = skip; j < _inputNames.Length; j++)
                {
                    if (outputNames[i] == _inputNames[j])
                    {
                        if (_outputTypes[i] == _inputTypes[j])
                        {
                            named.Add(outputNames[i]);
                            _refIn.Add(j + 1);
                            _refOut.Add(i + 1);
                        }
                        else
                        {
                            named.Add("_" + outputNames[i]);
                        }

                        matched = true;
                    }
                }

                if (!matched)
                {
                    named.Add(outputNames[i]);
                }
            }

            return [.. named, .. outputNames.Skip(_nargout)];
        }

        // MATLAB's `ismember(obj.RefInIndices, i)` in an if: true when every index there is i.
        private static bool Only(List<int> indices, int i) => indices.Count > 0 && indices.TrueForAll(x => x == i);

        /// <summary>The call's arguments, a name-value input passed with its name (R2025b passed it by position, a defect corrected here).</summary>
        private string Argument(int i) =>
            _inputs[i].Kind == TypedArgumentKind.NameValue ? "\"" + _inputs[i].Name + "\"," + _inputNames[i] : _inputNames[i];

        /// <summary>The method's text, its body and signature indented by <paramref name="root"/>.</summary>
        public string Text(string root, string? className)
        {
            int numArgIn = _inputs.Count - _optional;
            string call = MethodCall(numArgIn);
            string body = root + OneIndent + Assignment() + Cast() + call + ";";
            string signature;
            switch (_kind)
            {
                case OverloadKind.Function:
                    body = root + OneIndent + "dynamic _dynMatlab = _matlab;\n" + body;
                    signature = "public static void " + Fix(Last(_fullName)) + "(" + FunctionParameters(numArgIn) + ")";
                    break;
                case OverloadKind.Static:
                    body = root + OneIndent + "dynamic _dynMatlab = _matlab;\n" + body;
                    signature = "public static void " + Fix(Last(_fullName.Replace('/', '.'))) + "(" + StaticMethodParameters(numArgIn) + ")";
                    break;
                case OverloadKind.Constructor:
                    body = root + OneIndent + "this._matlab = _matlab;\n" + body;
                    signature = "public  " + Fix(className!) + "(" + ConstructorParameters(numArgIn) + ")";
                    break;
                default:
                    signature = "public void " + Fix(Last(_fullName)) + "(" + InstanceParameters(numArgIn) + ")";
                    break;
            }

            return root + signature + "{\n" + body + "\n" + root + "}\n";
        }

        private string MethodCall(int numArgIn)
        {
            string head = "new RunOptions(nargout:" + _nargout.ToString(CultureInfo.InvariantCulture) + ")";
            switch (_kind)
            {
                case OverloadKind.Function:
                {
                    string args = string.Join(",", Enumerable.Range(0, numArgIn).Select(Argument));
                    return "_dynMatlab." + FixFull(_fullName) + "(" + head + (numArgIn == 0 ? "" : ",") + args + ")";
                }

                case OverloadKind.Constructor:
                {
                    int count = _inputNames.Length == 0 ? 0 : numArgIn;
                    string args = string.Join(",", Enumerable.Range(0, count).Select(Argument));
                    return "this._matlab." + FixFull(_fullName) + "(" + head + (count < 1 ? "" : ",") + args + ")";
                }

                case OverloadKind.Static:
                {
                    // R2025b's skipped a static method's first input as though it were the object; a static method has none.
                    string args = string.Join(",", Enumerable.Range(0, numArgIn).Select(Argument));
                    return "_dynMatlab." + FixFull(_fullName.Replace('/', '.')) + "(" + head + (numArgIn == 0 ? "" : ",") + args + ")";
                }

                default:
                {
                    // R2025b wrote the comma whenever the method names an input besides the object, even in the
                    // overload that passes none of them: "(new RunOptions(nargout:0),)", which does not compile.
                    string args = string.Join(",", Enumerable.Range(1, Math.Max(0, numArgIn - 1)).Select(Argument));
                    return "_objrep." + Fix(Last(_fullName)) + "(" + head + (numArgIn <= 1 ? "" : ",") + args + ")";
                }
            }
        }

        private string Assignment()
        {
            if (_nargout == 0)
            {
                return string.Empty;
            }

            string names = string.Join(",", _kind == OverloadKind.Constructor
                ? ["_objrep", .. _outputNames.Skip(1).Take(_nargout - 1)]
                : _outputNames.Take(_nargout));
            return (_nargout > 1 ? "(" + names + ")" : names) + " = ";
        }

        private string Cast()
        {
            if (_nargout == 0)
            {
                return string.Empty;
            }

            // Each output is cast to its own type, an untyped one to MATLABArray.
            string casts = string.Join(",", _outputTypes.Take(_nargout).Select(static type => type == "dynamic" ? "MATLABArray" : type));
            return _nargout == 1 ? "(" + casts + ")" : "((" + casts + "))";
        }

        private string Inputs(int from, int to)
        {
            var s = new StringBuilder();
            for (int i = from; i < to; i++)
            {
                if (i != from)
                {
                    s.Append(", ");
                }

                if (Only(_refIn, i + 1))
                {
                    s.Append("ref ");
                }

                s.Append(_inputTypes[i]).Append(' ').Append(_inputNames[i]);
            }

            return s.ToString();
        }

        private string Outputs(int from)
        {
            var s = new StringBuilder();
            for (int i = from; i < _nargout; i++)
            {
                if (Only(_refOut, i + 1))
                {
                    continue;
                }

                if (i != from && _refOut.Count != _nargout)
                {
                    s.Append(", ");
                }

                s.Append(_outputTypes[i] == "object" ? " out dynamic " + _outputNames[i] : " out " + _outputTypes[i] + " " + _outputNames[i]);
            }

            return s.ToString();
        }

        /// <summary>CSharpStaticMethod.generateParameters for a function.</summary>
        private string FunctionParameters(int numArgIn)
        {
            string inputs = "MATLABProvider _matlab" + (numArgIn != 0 || _nargout != 0 ? ", " : "") + Inputs(0, numArgIn);
            string comma = _nargout != 0 && numArgIn >= 1 && _refOut.Count != _nargout ? ", " : "";
            return inputs + comma + Outputs(0);
        }

        /// <summary>CSharpMethod.generateParameters for a static method, its inputs from the first (R2025b's from the second).</summary>
        private string StaticMethodParameters(int numArgIn)
        {
            string inputs = "MATLABProvider _matlab" + (numArgIn >= 1 ? ", " : "") + Inputs(0, numArgIn);
            string comma = _nargout != 0 && _refOut.Count != _nargout ? ", " : "";
            return inputs + comma + Outputs(0);
        }

        /// <summary>CSharpMethod.generateParameters for an instance method: the object left out.</summary>
        private string InstanceParameters(int numArgIn)
        {
            string inputs = Inputs(1, numArgIn);
            string comma = _nargout != 0 && numArgIn != 1 && _refOut.Count != _nargout ? ", " : "";
            return inputs + comma + Outputs(0);
        }

        /// <summary>CSharpConstructor.generateParameters: the provider, the inputs, the outputs after the object.</summary>
        private string ConstructorParameters(int numArgIn)
        {
            int count = _inputTypes.Length - _optional;
            var s = new StringBuilder("MATLABProvider _matlab");
            if (count >= 1)
            {
                s.Append(", ");
            }

            for (int i = 0; i < count; i++)
            {
                s.Append(_inputTypes[i]).Append(' ').Append(Fix(_inputs[i].Name));
                if (i != count - 1)
                {
                    s.Append(", ");
                }
            }

            var outputs = new StringBuilder();
            for (int i = 1; i < _nargout; i++)
            {
                if (i != 1)
                {
                    outputs.Append(", ");
                }

                outputs.Append(_outputTypes[i] == "object" ? " out dynamic " + _outputNames[i] : " out " + _outputTypes[i] + " " + _outputNames[i]);
            }

            _ = numArgIn;
            return s + (_nargout > 1 ? ", " : "") + outputs;
        }
    }

    // --- Classes -------------------------------------------------------------------------------

    /// <summary>CSharpNonStaticClass: a value class's struct or a handle class's class over its MATLAB object.</summary>
    private static string ClassSource(TypedClass cls, string outer, Converter converter)
    {
        string name = Fix(Last(cls.FullName));
        string lower = name.ToLowerInvariant();
        bool isStruct = !cls.IsHandle;
        string ns = Namespace(cls.FullName, outer);
        var s = new StringBuilder();
        s.Append("public  ").Append(isStruct ? "struct" : "class").Append(' ').Append(name).Append(":IEquatable<").Append(name).Append("> { \n");
        if (isStruct)
        {
            s.Append(OneIndent).Append("private void ThrowIfDefaultValue()\n").Append(OneIndent).Append("{\n")
                .Append(OneIndent).Append(OneIndent).Append("if (_matlab == null || _objrep == null)\n").Append(OneIndent).Append(OneIndent).Append("{\n")
                .Append(OneIndent).Append(OneIndent).Append(OneIndent).Append("throw new UnsupportedTypeException(\"").Append(name).Append(" object is not initialized.\");\n")
                .Append(OneIndent).Append(OneIndent).Append("}\n").Append(OneIndent).Append("}\n");
        }

        s.Append(OneIndent).Append("private dynamic _objrep;\n").Append(OneIndent).Append("private dynamic _matlab;\n");

        TypedMethod constructor = cls.Constructor;
        int constructorOptional = constructor.Inputs.Count(static a => a.Optional);
        for (int i = 0; i <= constructorOptional; i++)
        {
            for (int j = 1; j <= constructor.Outputs.Count; j++)
            {
                s.Append(new Overload(cls.FullName, constructor.Inputs, constructor.Outputs, i, j, converter, OverloadKind.Constructor).Text(OneIndent, Last(cls.FullName)));
            }
        }

        if (!isStruct)
        {
            s.Append(OneIndent).Append("private ").Append(name).Append(" (){}\n\n");
        }

        foreach (TypedProperty property in cls.Properties)
        {
            string propertyName = Fix(property.Name);
            s.Append(OneIndent).Append("public ").Append(converter.Of(property)).Append(' ').Append(propertyName).Append(" {\n");
            if (property.Readable)
            {
                s.Append(OneIndent).Append(OneIndent).Append("get => _matlab.matlab.@internal.engine.getProperty(_objrep, \"").Append(property.Name).Append("\");");
            }

            s.Append('\n');
            if (property.Writable)
            {
                s.Append(OneIndent).Append(OneIndent).Append("set => _objrep = _matlab.matlab.@internal.engine.setProperty(_objrep,\"").Append(property.Name).Append("\",value);\n");
            }

            s.Append(OneIndent).Append("}\n");
        }

        var comparisons = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypedMethod method in cls.Methods)
        {
            if (method.Name is "lt" or "le" or "gt" or "ge")
            {
                if (comparisons.Add(method.Name))
                {
                    string op = method.Name switch { "lt" => "<", "le" => "<=", "gt" => ">", _ => ">=" };
                    s.Append(OneIndent).Append("public static bool operator ").Append(op).Append(" (").Append(name).Append(" obj1, ").Append(name).Append(" obj2){\n")
                        .Append(OneIndent).Append(OneIndent).Append("bool ret = obj1._matlab.").Append(method.Name).Append("(obj1,obj2);\n")
                        .Append(OneIndent).Append(OneIndent).Append("return ret;\n").Append(OneIndent).Append("}\n\n");
                }

                continue;
            }

            int optional = method.Inputs.Count(static a => a.Optional);
            string fullName = method.Static ? cls.FullName + "/" + method.Name : method.Name;
            for (int i = 0; i <= optional; i++)
            {
                for (int j = 0; j <= method.Outputs.Count; j++)
                {
                    s.Append(new Overload(fullName, method.Inputs, method.Outputs, i, j, converter, method.Static ? OverloadKind.Static : OverloadKind.Instance).Text(OneIndent, null));
                }
            }
        }

        s.Append(Equatable(name));
        s.Append(OneIndent).Append("public static implicit operator MATLABObject(").Append(name).Append(' ').Append(lower).Append("){\n");
        if (isStruct)
        {
            s.Append(OneIndent).Append(OneIndent).Append(lower).Append(".ThrowIfDefaultValue();\n");
        }

        s.Append(OneIndent).Append(OneIndent).Append("return ").Append(lower).Append("._objrep;\n").Append(OneIndent).Append("}\n");
        s.Append(OneIndent).Append("public static implicit operator ").Append(name).Append("(MATLABObject _obj){\n");
        s.Append(OneIndent).Append(OneIndent).Append(name).Append(' ').Append(lower).Append(isStruct ? " = default;\n" : " = new " + name + "();\n");
        s.Append(OneIndent).Append(OneIndent).Append(lower).Append("._objrep = _obj;\n");
        s.Append("#pragma warning disable CS8601, CS8602\n");
        s.Append(OneIndent).Append(OneIndent).Append(lower)
            .Append("._matlab = typeof(MATLABObject).GetField(\"Matlab\", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(_obj);\n");
        s.Append("#pragma warning restore\n");
        s.Append(OneIndent).Append(OneIndent).Append("return ").Append(lower).Append(";\n").Append(OneIndent).Append("}\n");
        s.Append("}\n");
        return Wrapped(ns, "[MATLABClass(\"" + name + "\")]", s.ToString());
    }

    /// <summary>The IEquatable members every generated class has (CSharpNonStaticClass.buildIEquatableMethodString).</summary>
    private static string Equatable(string name) =>
        $$"""
    public static bool operator == ({{name}} obj1, {{name}} obj2){
        bool ret = obj1._matlab.eq(obj1,obj2);
        return ret;
    }

    public static bool operator != ({{name}} obj1, {{name}} obj2){
        bool ret = obj1._matlab.ne(obj1,obj2);
        return ret;
    }

    public override bool Equals (Object obj){
        if (obj == null){
            return false;
        }

        try{

            {{name}} _obj = ({{name}})obj;
            return (this == _obj);
        }

        catch(Exception){

            return false;
        }

    }

    public bool Equals({{name}} obj){
        return this == obj;
    }

    public override int GetHashCode(){
        UInt64 hashcode = _matlab.keyHash(_objrep);
        return hashcode.GetHashCode();
    }


""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>CSharpEnum: a .NET enum of the members' names, which ends without a newline as R2025b's does.</summary>
    private static string EnumSource(TypedClass cls, string outer)
    {
        string name = Fix(Last(cls.FullName));
        string ns = Namespace(cls.FullName, outer);
        string members = string.Join(",\n", cls.Enumeration.Select(static m => OneIndent + Fix(m)));
        return (ns.Length > 0 ? "namespace " + ns + "{" : string.Empty) + "\n[MATLABEnum(\"" + name + "\")]\npublic enum " + name + "{\n" + members + "\n}\n"
            + (ns.Length > 0 ? "}" : string.Empty);
    }
}
