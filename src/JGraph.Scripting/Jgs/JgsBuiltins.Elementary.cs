using System.Numerics;

using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The elementary layer MATLAB scripts assume is always there: the numeric constants and limits
/// (<c>eps</c>, <c>realmax</c>, <c>intmax</c>, …), the type predicates (<c>isreal</c>, <c>isscalar</c>,
/// <c>class</c>, …), and the trigonometry beyond the six functions JGS started with — degree forms,
/// hyperbolics, and the reciprocal family. None of it needs machinery the value model does not
/// already have, which is why it arrives in one file.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>MATLAB's <c>eps</c>: the distance from 1.0 to the next double, 2⁻⁵².</summary>
    private const double DoubleEps = 2.220446049250313e-16;

    /// <summary>The single-precision <c>eps</c>, 2⁻²³ — what <c>eps('single')</c> reports.</summary>
    private const float SingleEps = 1.1920929e-07f;

    /// <summary>Registers the constants, limits, predicates, and trigonometry (M37).</summary>
    private static void RegisterElementaryBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // MATLAB implements eps/realmax/intmax/… as zero-argument functions, so a bare mention is the
        // value (x = eps) while a call takes an argument (eps(x), intmax('int8')). AutoCallsBare gives
        // the first behaviour without giving up the second.
        void Constant(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        RegisterLimits(env, Constant);
        RegisterTypePredicates(env, Define, dialect);
        RegisterTrigonometry(Define);
    }

    // --- Constants and limits -------------------------------------------------------------------

    private static void RegisterLimits(
        JgsEnvironment env, Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Constant)
    {
        // MATLAB spells these with capitals; JGS has always had the lowercase pair. Both stay: they
        // are the same value, and a MATLAB script writing Inf must not have to know about JGS.
        env.Declare("Inf", JgsValue.Number(double.PositiveInfinity));
        env.Declare("NaN", JgsValue.Number(double.NaN));

        // The imaginary unit under both of its names. A script that uses i as a loop variable simply
        // shadows it, exactly as in MATLAB.
        env.Declare("i", JgsValue.ComplexNum(Complex.ImaginaryOne));
        env.Declare("j", JgsValue.ComplexNum(Complex.ImaginaryOne));
        env.Declare("newline", JgsValue.Str("\n"));

        Constant("eps", (args, line, col) =>
        {
            ArityRange("eps", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                return JgsValue.Number(DoubleEps);
            }

            if (args[0].Type == JgsType.String)
            {
                return JgsValue.Number(PrecisionOf("eps", args[0].AsString, line, col) ? DoubleEps : SingleEps);
            }

            // eps(x) is the spacing at x: the gap to the next representable double. BitIncrement
            // handles 0 (the smallest denormal) and the infinities (NaN) the way MATLAB does.
            return MapNumeric("eps", args[0], static x =>
            {
                double magnitude = Math.Abs(x);
                return Math.BitIncrement(magnitude) - magnitude;
            }, line, col);
        });

        Constant("realmax", (args, line, col) =>
            JgsValue.Number(SinglePrecisionAsked("realmax", args, line, col) ? float.MaxValue : double.MaxValue));

        Constant("realmin", (args, line, col) =>
            JgsValue.Number(SinglePrecisionAsked("realmin", args, line, col)
                ? BitConverter.Int32BitsToSingle(0x00800000) // 2⁻¹²⁶, the smallest normalized float
                : BitConverter.Int64BitsToDouble(0x0010000000000000L))); // 2⁻¹⁰²², the double equivalent

        Constant("flintmax", (args, line, col) =>
            JgsValue.Number(SinglePrecisionAsked("flintmax", args, line, col) ? 16777216.0 : 9007199254740992.0));

        Constant("intmax", (args, line, col) => JgsValue.Number(IntegerLimit("intmax", args, true, line, col)));
        Constant("intmin", (args, line, col) => JgsValue.Number(IntegerLimit("intmin", args, false, line, col)));
    }

    /// <summary>Whether a precision word names doubles; anything but 'double'/'single' is an error.</summary>
    private static bool PrecisionOf(string name, string word, int line, int col) => word switch
    {
        "double" => true,
        "single" => false,
        _ => throw new JgsRuntimeException(line, col, $"{name}: '{word}' is not a precision; use 'double' or 'single'."),
    };

    /// <summary>Reads the optional precision argument shared by realmax/realmin/flintmax.</summary>
    private static bool SinglePrecisionAsked(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 0, 1, line, col);
        return args.Count == 1 && !PrecisionOf(name, Str(name, args, 0, line, col), line, col);
    }

    /// <summary>
    /// The largest or smallest value of a MATLAB integer class. JGraph has no integer types, so the
    /// limit comes back as a double — exact for every class except int64/uint64, whose extremes are
    /// beyond a double's 53-bit integer range.
    /// </summary>
    private static double IntegerLimit(string name, IReadOnlyList<JgsValue> args, bool maximum, int line, int col)
    {
        ArityRange(name, args, 0, 1, line, col);
        string type = args.Count == 1 ? Str(name, args, 0, line, col) : "int32";
        return type switch
        {
            "int8" => maximum ? sbyte.MaxValue : sbyte.MinValue,
            "int16" => maximum ? short.MaxValue : short.MinValue,
            "int32" => maximum ? int.MaxValue : int.MinValue,
            "int64" => maximum ? long.MaxValue : long.MinValue,
            "uint8" => maximum ? byte.MaxValue : 0,
            "uint16" => maximum ? ushort.MaxValue : 0,
            "uint32" => maximum ? uint.MaxValue : 0,
            "uint64" => maximum ? ulong.MaxValue : 0,
            _ => throw new JgsRuntimeException(line, col, $"{name}: '{type}' is not an integer class."),
        };
    }

    // --- Type predicates ------------------------------------------------------------------------

    private static void RegisterTypePredicates(
        JgsEnvironment env,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        JgsDialect dialect)
    {
        void Predicate(string name, Func<JgsValue, bool> test) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return JgsValue.Bool(test(args[0])); });

        // isletter/isspace classify text, so a string argument is the usual one: each character
        // becomes one element of the mask. Numbers are read as character codes, as MATLAB does.
        void CharacterClass(string name, Func<char, bool> test) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                if (args[0].Type != JgsType.String)
                {
                    return MapToBool(name, args[0], x => test((char)(int)x), line, col);
                }

                string text = args[0].AsString;
                var mask = new JgsValue[text.Length];
                for (int k = 0; k < text.Length; k++)
                {
                    mask[k] = JgsValue.Bool(test(text[k]));
                }

                return JgsValue.Array(mask);
            });

        // The finiteness questions accept complex input — a complex value is infinite or NaN when
        // either part is (MATLAB's reading), which is what (Inf+1i*Inf)*(0+1i) needs to answer.
        void ElementwiseC(string name, Func<double, bool> test, Func<System.Numerics.Complex, bool> complexTest) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                return MapToBool(name, args[0], test, line, col, complexTest);
            });

        ElementwiseC("isfinite", double.IsFinite,
            static c => double.IsFinite(c.Real) && double.IsFinite(c.Imaginary));
        ElementwiseC("isinf", double.IsInfinity,
            static c => double.IsInfinity(c.Real) || double.IsInfinity(c.Imaginary));
        CharacterClass("isletter", char.IsLetter);
        CharacterClass("isspace", char.IsWhiteSpace);

        // Storage is always doubles, but a value remembers the class it was asked for (M47), so these
        // read the tag: isinteger is true exactly for a value that went through one of the eight
        // integer constructors, and isfloat is true for everything else numeric.
        Predicate("isfloat", static v => IsNumericValue(v) && !v.NumericClass.IsInteger());
        Predicate("isinteger", static v => v.NumericClass.IsInteger());
        Predicate("isreal", static v => !HasComplexPart(v));
        // A struct is asked by extent like a cell, because since M65 it has one: a struct array is a
        // struct value that is not a 1-by-1, and answering isscalar for it would be the old lie in a
        // new place.
        Predicate("isscalar", static v =>
            v.Type == JgsType.Array ? !v.IsNd && Extent(v) == 1
            : v.Type is not (JgsType.Cell or JgsType.Struct) || Extent(v) == 1);
        Predicate("isvector", IsVectorShape);
        Predicate("ismatrix", static v => v.Type switch
        {
            JgsType.Array => !v.IsNd,
            JgsType.Image => v.AsImage.Channels == 1,
            _ => true,
        });

        // Arrays carry a shape now (ADR 0043), so these read it: a literal is 1-by-n and answers
        // isrow, and a transposed vector or a column of a matrix genuinely answers iscolumn.
        Predicate("isrow", IsRowShape);
        Predicate("iscolumn", IsColumnShape);

        Predicate("isstr", static v => v.Type == JgsType.String); // the pre-R2016 spelling of ischar
        Predicate("isstring", static v => v.IsStringArray);       // M63: true only for double-quoted text
        Predicate("iscellstr", static v =>
            v.Type == JgsType.Cell && Array.TrueForAll(v.AsCell, static e => e.Type == JgsType.String));

        Define("issorted", (args, line, col) =>
        {
            Arity("issorted", args, 1, line, col);
            if (args[0].Type != JgsType.Array)
            {
                return JgsValue.Bool(true); // a single value is trivially in order
            }

            double[] values = ToDoubles("issorted", args[0], line, col);
            for (int k = 1; k < values.Length; k++)
            {
                if (values[k] < values[k - 1])
                {
                    return JgsValue.Bool(false);
                }
            }

            return JgsValue.Bool(true);
        });

        // isequal treats NaN as unequal to itself; these two are the variants that do not. The second
        // name is what isequaln was called before R2012a, and old scripts still use it.
        JgsValue NanEqual(string name, IReadOnlyList<JgsValue> args, int line, int col)
        {
            Arity(name, args, 2, line, col);
            return JgsValue.Bool(JgsStdlib.DeepEquals(args[0], args[1], nanEqual: true));
        }

        Define("isequaln", (args, line, col) => NanEqual("isequaln", args, line, col));
        Define("isequalwithequalnans", (args, line, col) => NanEqual("isequalwithequalnans", args, line, col));

        Define("class", (args, line, col) =>
        {
            Arity("class", args, 1, line, col);
            return JgsValue.Str(ClassOf(args[0], dialect));
        });

        Define("isa", (args, line, col) =>
        {
            Arity("isa", args, 2, line, col);
            string wanted = Str("isa", args, 1, line, col);
            string actual = ClassOf(args[0], dialect);
            JgsNumericClass? numericClass = JgsNumericClasses.Parse(actual);
            return JgsValue.Bool(wanted switch
            {
                // MATLAB accepts three category words alongside the class names themselves.
                "numeric" => numericClass is not null,
                "float" => numericClass is JgsNumericClass.Double or JgsNumericClass.Single,
                "integer" => numericClass?.IsInteger() == true,
                _ => string.Equals(actual, wanted, StringComparison.Ordinal),
            });
        });

        Define("logical", (args, line, col) =>
        {
            Arity("logical", args, 1, line, col);
            return MapToBool("logical", args[0], static x => x != 0, line, col);
        });

        // double(x) is how a script says "treat this as a number": it turns a logical mask or a
        // character code into arithmetic. single rounds to float precision but is still stored as a
        // double, since that is the only numeric storage JGraph has.
        Define("double", (args, line, col) =>
        {
            Arity("double", args, 1, line, col);
            return ToNumericClass("double", JgsNumericClass.Double, args[0], line, col);
        });

        Define("single", (args, line, col) =>
        {
            Arity("single", args, 1, line, col);
            return ToNumericClass("single", JgsNumericClass.Single, args[0], line, col);
        });

        Define("cast", (args, line, col) =>
        {
            Arity("cast", args, 2, line, col);
            string target = Str("cast", args, 1, line, col);
            if (JgsNumericClasses.Parse(target) is JgsNumericClass numericClass)
            {
                return ToNumericClass("cast", numericClass, args[0], line, col);
            }

            return target switch
            {
                "logical" => MapToBool("cast", args[0], static x => x != 0, line, col),
                "char" => JgsValue.Str(args[0].Type == JgsType.String ? args[0].AsString : args[0].Display()),
                _ => throw new JgsRuntimeException(line, col, $"cast: JGraph has no '{target}' class."),
            };
        });

        // The integer class constructors (M42): MATLAB conversion semantics — round half away from
        // zero, saturate at the class limits, NaN becomes 0 — on JGraph's double storage. M47 added
        // the tag that records which class was asked for, so class() now answers it and arithmetic
        // keeps the result inside the same class.
        foreach (string name in IntegerClassNames)
        {
            string self = name;
            JgsNumericClass numericClass = JgsNumericClasses.Parse(name)!.Value;
            Define(name, (args, line, col) =>
            {
                Arity(self, args, 1, line, col);
                return ToNumericClass(self, numericClass, args[0], line, col);
            });
        }
    }

    /// <summary>Converts one value into a numeric class and tags the result with it.</summary>
    /// <remarks>
    /// The tag rides on the wrapper <see cref="MapNumeric"/> has just built, which is always a fresh
    /// one — that is what keeps the single-wrapper invariant intact while the class changes.
    /// </remarks>
    private static JgsValue ToNumericClass(
        string name, JgsNumericClass numericClass, JgsValue value, int line, int col)
    {
        // A char array converts through its codes, which is what makes double('A') 65.
        JgsValue source = value.Type == JgsType.String ? CharactersAsCodes(value.AsString) : value;
        JgsValue result = MapNumeric(name, source, x => JgsNumericClasses.Convert(x, numericClass), line, col);
        result.SetNumericClass(numericClass);
        return result;
    }

    /// <summary>The eight MATLAB integer classes, each a constructor and an <c>.empty</c> static.</summary>
    private static readonly string[] IntegerClassNames =
        ["int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64"];

    /// <summary>
    /// The statics a class-constructor builtin exposes through field access — today just
    /// <c>uint8.empty(m, n)</c> and friends (double/single included). The interpreter's member
    /// evaluation consults this before treating the dot as a struct access.
    /// </summary>
    internal static bool TryGetBuiltinStatic(string owner, string member, out JgsValue value)
    {
        if (member == "empty" && (owner is "double" or "single" or "logical" or "char" ||
            Array.IndexOf(IntegerClassNames, owner) >= 0))
        {
            value = JgsValue.Function(new BuiltinFunction($"{owner}.empty", (args, line, col) =>
            {
                var dims = new int[System.Math.Max(args.Count, 2)];
                bool hasZero = args.Count == 0;
                for (int i = 0; i < args.Count; i++)
                {
                    dims[i] = Count($"{owner}.empty", args, i, line, col);
                    if (dims[i] < 0)
                    {
                        throw new JgsRuntimeException(line, col, $"{owner}.empty: dimensions cannot be negative.");
                    }

                    hasZero |= dims[i] == 0;
                }

                if (args.Count == 1)
                {
                    dims[1] = dims[0]; // empty(n) means n-by-n, which must still be empty
                }

                if (!hasZero)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{owner}.empty: at least one dimension must be zero.");
                }

                return JgsMatrix.FromColumnMajorDims([], dims);
            }));
            return true;
        }

        value = JgsValue.Null;
        return false;
    }

    /// <summary>Text as its character codes — what <c>double('A')</c> means.</summary>
    private static JgsValue CharactersAsCodes(string text)
    {
        var codes = new double[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            codes[i] = text[i];
        }

        return Numbers(codes);
    }

    /// <summary>The MATLAB class name of a value, as <c>class</c> reports it.</summary>
    /// <remarks>
    /// An image answers with its numeric class under MATLAB — <c>uint8</c> for a picture just read from
    /// a file — because that is what a <c>.m</c> script branches on. JGS has a first-class image type
    /// that is not a numeric array, and says so.
    /// <para>
    /// A plain number or array answers with the class it was asked for (M47): <c>uint8</c> once it
    /// has been through the constructor, <c>double</c> otherwise. Storage never changes — the tag is
    /// what carries the answer.
    /// </para>
    /// </remarks>
    internal static string ClassOf(JgsValue value, JgsDialect dialect) =>
        // A string array is asked about first of all, because it is an array of strings underneath
        // and every question below would answer for the array rather than for what it holds (M63).
        value.IsStringArray ? "string"
        // A time is asked about next, and for exactly the same reason: it is an array of milliseconds
        // underneath, so every question below would answer 'double' for it (M64).
        : value.IsTime ? TimeClassName(value)
        // A mask has to be asked about before the numeric class is, because logical is not one of
        // them: masks are structural (a Bool element, or a packed buffer of them), and adding a
        // Logical to JgsNumericClass would put a second, contradictory answer in the value.
        : IsLogicalValue(value) ? "logical" : value.Type switch
    {
        JgsType.Number or JgsType.Complex or JgsType.Array => value.NumericClass.MatlabName(),
        JgsType.String => "char",
        JgsType.Cell => "cell",
        // A transform or spatial reference is a struct carrying the name of the MATLAB class it
        // stands in for, so class(tform) answers 'affine2d' rather than the mechanism underneath.
        // The class name a struct carries (M62's MException) comes first: it is a property of the
        // value rather than a field inside it, so it cannot be spelled by accident the way a 'Type'
        // field can.
        JgsType.Struct => value.ClassName ?? TaggedClassOf(value) ?? "struct",
        JgsType.Function => "function_handle",
        JgsType.Table => "table",
        JgsType.Image => dialect.IsMatlab ? value.AsImage.Class.MatlabName() : "image",
        JgsType.Sparse => "double", // MATLAB: sparsity is an attribute, not a class
        // An instance of a user class answers with its own class name (M68). It reads the same
        // property a tagged struct does, which is why this line is the whole of the change.
        JgsType.Object => value.ClassName ?? "object",
        _ => "double",
    };

    private static bool IsNumericValue(JgsValue value) =>
        // A string array is an array of strings underneath, so it has to be excluded by name (M63) —
        // otherwise isnumeric(["a" "b"]) would answer for the container rather than what it holds.
        !value.IsStringArray
        // And a time is excluded for the same reason (M64): isnumeric(datetime) is false in MATLAB,
        // because the milliseconds underneath are storage rather than the value's meaning.
        && !value.IsTime
        && value.Type is JgsType.Number or JgsType.Complex or JgsType.Bool or JgsType.Array or JgsType.Sparse;

    /// <summary>
    /// Whether a value is a mask: a single true/false, an array of them, or an image an imaging
    /// builtin tagged logical. Both <c>islogical</c> and <c>class</c> read it, which is the point —
    /// they used to disagree, because only <c>islogical</c> looked inside an array and
    /// <c>class(mask)</c> answered <c>'double'</c>.
    /// </summary>
    /// <remarks>
    /// An empty array is not a mask. Every element of one satisfies any test you like, so the loop
    /// below would call <c>[]</c> logical and make <c>class([])</c> answer <c>'logical'</c> where
    /// MATLAB says <c>'double'</c> — the emptiness carries no type, so the default has to win.
    /// </remarks>
    private static bool IsLogicalValue(JgsValue value)
    {
        if (value.Type == JgsType.Bool)
        {
            return true;
        }

        if (value.Type == JgsType.Image)
        {
            return value.AsImage.Class == JGraph.Imaging.ImageClass.Logical;
        }

        return value.Type == JgsType.Array && value.ArrayLength > 0 && AllOfType(value, JgsType.Bool);
    }

    /// <summary>Whether a value carries a non-zero imaginary part anywhere inside it.</summary>
    private static bool HasComplexPart(JgsValue value)
    {
        if (value.Type == JgsType.Complex)
        {
            return value.AsComplex.Imaginary != 0;
        }

        if (value.Type != JgsType.Array || value.IsPacked)
        {
            return false;
        }

        return Array.Exists(value.AsArray, HasComplexPart);
    }

    /// <summary>The element count of an array or cell; 1 for anything scalar.</summary>
    private static int Extent(JgsValue value) => value.Type switch
    {
        JgsType.Array => value.ArrayLength,
        JgsType.Cell => value.AsCell.Length,
        JgsType.Struct => value.AsStructArray.Length,
        _ => 1,
    };

    /// <summary>Whether a value is a vector: a scalar, or a 2-D array with a singleton dimension.</summary>
    private static bool IsVectorShape(JgsValue value)
    {
        if (value.Type == JgsType.Struct)
        {
            return value.Rows == 1 || value.Cols == 1;
        }

        if (value.Type is not (JgsType.Array or JgsType.Cell))
        {
            return true;
        }

        if (value.Type == JgsType.Cell)
        {
            return true;
        }

        return !value.IsNd
            && (JgsMatrix.RowCount(value) == 1 || JgsMatrix.ColCount(value) == 1);
    }

    /// <summary>Whether a value is a single row — a scalar, or a 2-D array whose row count is one.</summary>
    private static bool IsRowShape(JgsValue value) =>
        value.Type == JgsType.Struct ? value.Rows == 1
        : value.Type is not (JgsType.Array or JgsType.Cell) || value.Type == JgsType.Cell
        || (!value.IsNd && JgsMatrix.RowCount(value) == 1);

    /// <summary>Whether a value is a single column.</summary>
    private static bool IsColumnShape(JgsValue value) =>
        value.Type == JgsType.Struct
            ? value.Cols == 1
            : value.Type is not (JgsType.Array or JgsType.Cell)
            ? true
            : value.Type == JgsType.Cell
                ? Extent(value) == 1
                : !value.IsNd && JgsMatrix.ColCount(value) == 1;

    // --- Trigonometry ---------------------------------------------------------------------------

    private static void RegisterTrigonometry(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        void Math1(string name, Func<double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapNumeric(name, args[0], f, line, col); });

        // Reciprocal trigonometry, radians.
        Math1("sec", static x => 1.0 / Math.Cos(x));
        Math1("csc", static x => 1.0 / Math.Sin(x));
        Math1("cot", static x => 1.0 / Math.Tan(x));
        Math1("asec", static x => Math.Acos(1.0 / x));
        Math1("acsc", static x => Math.Asin(1.0 / x));
        Math1("acot", static x => Math.Atan(1.0 / x));

        // Hyperbolics and their inverses.
        Math1("sinh", Math.Sinh);
        Math1("cosh", Math.Cosh);
        Math1("tanh", Math.Tanh);
        Math1("asinh", Math.Asinh);
        Math1("acosh", Math.Acosh);
        Math1("atanh", Math.Atanh);
        Math1("sech", static x => 1.0 / Math.Cosh(x));
        Math1("csch", static x => 1.0 / Math.Sinh(x));
        Math1("coth", static x => 1.0 / Math.Tanh(x));
        Math1("asech", static x => Math.Acosh(1.0 / x));
        Math1("acsch", static x => Math.Asinh(1.0 / x));
        Math1("acoth", static x => Math.Atanh(1.0 / x));

        // Degree forms. Going through DegreeSine/DegreeCosine rather than multiplying by pi/180 is
        // what makes sind(180) exactly 0 and cosd(90) exactly 0, as MATLAB documents.
        Math1("sind", DegreeSine);
        Math1("cosd", DegreeCosine);
        Math1("tand", static x => DegreeSine(x) / DegreeCosine(x));
        Math1("secd", static x => 1.0 / DegreeCosine(x));
        Math1("cscd", static x => 1.0 / DegreeSine(x));
        Math1("cotd", static x => DegreeCosine(x) / DegreeSine(x));
        Math1("asind", static x => Math.Asin(x) * (180.0 / Math.PI));
        Math1("acosd", static x => Math.Acos(x) * (180.0 / Math.PI));
        Math1("atand", static x => Math.Atan(x) * (180.0 / Math.PI));
        Math1("asecd", static x => Math.Acos(1.0 / x) * (180.0 / Math.PI));
        Math1("acscd", static x => Math.Asin(1.0 / x) * (180.0 / Math.PI));
        Math1("acotd", static x => Math.Atan(1.0 / x) * (180.0 / Math.PI));

        Define("atan2d", (args, line, col) =>
        {
            Arity("atan2d", args, 2, line, col);
            return Zip("atan2d", args[0], args[1], static (y, x) => Math.Atan2(y, x) * (180.0 / Math.PI), line, col);
        });
    }

    /// <summary>The sine of an angle in degrees, exact at every multiple of 90°.</summary>
    private static double DegreeSine(double degrees)
    {
        double folded = Math.IEEERemainder(degrees, 360.0);
        if (double.IsNaN(folded))
        {
            return double.NaN;
        }

        return folded switch
        {
            0.0 or 180.0 or -180.0 => 0.0,
            90.0 => 1.0,
            -90.0 => -1.0,
            _ => Math.Sin(folded * (Math.PI / 180.0)),
        };
    }

    /// <summary>The cosine of an angle in degrees, exact at every multiple of 90°.</summary>
    private static double DegreeCosine(double degrees)
    {
        double folded = Math.IEEERemainder(degrees, 360.0);
        if (double.IsNaN(folded))
        {
            return double.NaN;
        }

        return folded switch
        {
            90.0 or -90.0 => 0.0,
            0.0 => 1.0,
            180.0 or -180.0 => -1.0,
            _ => Math.Cos(folded * (Math.PI / 180.0)),
        };
    }
}
