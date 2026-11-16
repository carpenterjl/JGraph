using System.Numerics;
using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>imadd</c>, <c>imsubtract</c>, <c>immultiply</c>, <c>imdivide</c>, <c>imabsdiff</c>,
/// <c>imcomplement</c>, <c>imlincomb</c> and <c>imtile</c> on plain arrays in MATLAB code, by R2025b's
/// own class rules (open item 322, ADR 0367).
/// </summary>
/// <remarks>
/// <para>
/// The picture arithmetic these verbs share with the JGS dialect works on normalized samples and clamps
/// every answer to [0, 1], which is the right reading of a picture value and the wrong one of an array:
/// R2025b's <c>imadd</c> of two doubles is <c>X + Y</c>, unclamped, and its integer arithmetic rounds
/// half away from zero in the class's own units. In MATLAB code no picture value arrives any more
/// (<c>imread</c> answers an array, ADR 0356), so these verbs read their arrays as R2025b's sources in
/// toolbox/images/images do: the same argument checks in the same order, the same identifiers and
/// words, the same fast paths and the same class of answer.
/// </para>
/// <para>
/// The element arithmetic is this file's own (<see cref="ImBinary"/>) rather than the interpreter's
/// operators, so a change to general operator dispatch cannot move these answers; it carries exactly
/// the rules the sources reach: an integer with its own class or a scalar double, single over double,
/// logical and char read as double, and implicit expansion. A picture value (only a JGraph extra such
/// as <c>mat2im</c> makes one in MATLAB code) keeps the picture road, and so does every call in JGS.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class lists R2025b's validateattributes prints for these verbs.</summary>
    private const string ImNumericTypes = "double, single, uint8, uint16, uint32, uint64, int8, int16, int32, int64";

    private const string ImNumericLogicalTypes = ImNumericTypes + ", logical";

    private const string ImLogicalNumericTypes = "logical, " + ImNumericTypes;

    /// <summary>The output classes imlincomb's validatestring takes, in its order.</summary>
    private static readonly string[] ImLincombClasses =
        ["uint8", "uint16", "uint32", "int8", "int16", "int32", "single", "double"];

    /// <summary>
    /// Whether a call takes the MATLAB road: MATLAB code, and no picture value among the arguments.
    /// </summary>
    private static bool TakesMatlabImageRoad(IReadOnlyList<JgsValue> args, JgsDialect dialect)
    {
        if (!dialect.IsMatlab)
        {
            return false;
        }

        foreach (JgsValue arg in args)
        {
            if (arg.Type == JgsType.Image)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One argument as R2025b's checks see it: its class name, whether it is numeric, logical or char,
    /// real and full, and its samples column by column with its dimensions.
    /// </summary>
    private sealed class ImArray
    {
        public required string ClassName { get; init; }

        public required double[] Data { get; init; }

        public required int[] Dims { get; init; }

        public JgsNumericClass Class { get; init; }

        public bool IsNumeric { get; init; }

        public bool IsLogical { get; init; }

        public bool IsChar { get; init; }

        public bool IsComplex { get; init; }

        public bool IsSparse { get; init; }

        /// <summary><c>numel</c>, read from the dimensions so a complex value has one too.</summary>
        public int Count
        {
            get
            {
                int count = 1;
                foreach (int d in Dims)
                {
                    count *= d;
                }

                return count;
            }
        }

        public bool IsEmpty => Count == 0;

        /// <summary>Whether the samples were read: a sparse or complex value carries none here.</summary>
        public bool HasData => Data.Length == Count;

        /// <summary>A numeric class or a mask: what <c>{'numeric','logical'}</c> admits.</summary>
        public bool IsNumericOrLogical => IsNumeric || IsLogical;

        /// <summary><c>isa(Y, 'double') &amp;&amp; numel(Y) == 1</c>.</summary>
        public bool IsScalarDouble => ClassName == "double" && Count == 1;

        public bool IsInteger => IsNumeric && Class.IsInteger();

        public bool IsSingle => IsNumeric && Class == JgsNumericClass.Single;
    }

    /// <summary>Reads one argument for the MATLAB road.</summary>
    private static ImArray ReadImArray(JgsValue value)
    {
        string className = ClassOf(value, JgsDialect.Matlab);
        bool logical = IsLogicalValue(value);
        bool numeric = !logical && IsNumericValue(value);
        bool isChar = className == "char";
        // A boxed array with a complex element is complex too (open item 335: [1+2i 3; 0.5 2i]).
        bool complex = value.Type == JgsType.Complex || value.IsPackedComplex || HasComplexElements(value);
        bool sparse = value.Type == JgsType.Sparse;
        double[] data = [];
        int[] dims = [0, 0];
        if (value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
        {
            dims = [1, 1];
        }
        else if (value.Type == JgsType.Array && !value.IsStringArray)
        {
            dims = JgsMatrix.DimsOf(value);
        }
        else if (sparse)
        {
            dims = [value.Rows, value.Cols];
        }

        if (!complex && !sparse)
        {
            if (value.Type is JgsType.Number or JgsType.Bool)
            {
                data = [value.AsNumber];
                dims = [1, 1];
            }
            else if (value.Type == JgsType.String && isChar)
            {
                string text = value.AsString;
                data = new double[text.Length];
                for (int i = 0; i < text.Length; i++)
                {
                    data[i] = text[i];
                }

                dims = [text.Length == 0 ? 0 : 1, text.Length];
            }
            else if (value.Type == JgsType.Array && (numeric || logical) && !value.IsStringArray)
            {
                dims = JgsMatrix.DimsOf(value);
                if (value.IsPacked)
                {
                    data = value.AsBuffer.AsSpan().ToArray();
                    GC.KeepAlive(value);
                }
                else
                {
                    data = new double[value.ArrayLength];
                    for (int i = 0; i < data.Length; i++)
                    {
                        JgsValue element = value.ElementAt(i);
                        data[i] = element.Type is JgsType.Number or JgsType.Bool ? element.AsNumber : double.NaN;
                    }
                }
            }
        }

        return new ImArray
        {
            ClassName = className,
            Data = data,
            Dims = dims,
            Class = numeric ? value.NumericClass : JgsNumericClass.Double,
            IsNumeric = numeric,
            IsLogical = logical,
            IsChar = isChar,
            IsComplex = complex,
            IsSparse = sparse,
        };
    }

    /// <summary>An answer of a class, with its dimensions; a 1-by-1 is the bare scalar.</summary>
    private static JgsValue ImArrayOut(double[] data, int[] dims, JgsNumericClass numericClass, bool logical)
    {
        bool scalar = data.Length == 1 && dims.All(static d => d == 1);
        if (logical)
        {
            if (data.Length == 0)
            {
                return dims.Length == 2 ? EmptyLogical(dims[0], dims[1]) : JgsValue.Shaped([], 0, 0, JgsPackedKind.Bool);
            }

            var flags = new JgsValue[data.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] = JgsValue.Bool(data[i] != 0);
            }

            return scalar ? flags[0] : JgsMatrix.FromElementsDims(flags, dims);
        }

        JgsValue answer = scalar ? JgsValue.Number(data[0]) : JgsMatrix.FromColumnMajorDims(data, dims);
        if (numericClass != JgsNumericClass.Double)
        {
            answer.SetNumericClass(numericClass);
        }

        return answer;
    }

    /// <summary>The same array handed back unchanged in its own class.</summary>
    private static JgsValue ImArrayOut(ImArray a) =>
        ImArrayOut((double[])a.Data.Clone(), a.Dims, a.Class, a.IsLogical);

    private static bool SameDims(int[] a, int[] b)
    {
        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < a.Length ? a[i] : 1;
            int y = i < b.Length ? b[i] : 1;
            if (x != y)
            {
                return false;
            }
        }

        return true;
    }

    // --- validateattributes, as these verbs call it ---------------------------------------------------

    /// <summary>The start of every validateattributes message: which input, by number and name.</summary>
    private static string ImInputWords(int number, string name) =>
        number > 0 ? $"Expected input number {number}, {name}, to be " : "Expected input to be ";

    /// <summary>
    /// The class check: <c>MATLAB:fn:invalidType</c>. R2025b leaves the "Instead its type was" line
    /// off for a char (measured for imadd, imsubtract, immultiply, imdivide, imabsdiff, imcomplement).
    /// </summary>
    private static JgsRuntimeException ImTypeRefusal(
        string fn, int number, string name, string types, string className, int line, int col) =>
        new(line, col, $"MATLAB:{fn}:invalidType",
            ImInputWords(number, name) + "one of these types:\n\n" + types
            + (className == "char" ? string.Empty : $"\n\nInstead its type was {className}."));

    /// <summary>
    /// validateattributes(a, classes, {'real', 'nonsparse'}, fn, name, number): the class first, then
    /// each attribute in the order the source lists it.
    /// </summary>
    private static void ImValidate(
        string fn, ImArray a, int number, string name, string types, Func<ImArray, bool> admits,
        bool real, bool nonsparse, int line, int col)
    {
        if (!admits(a))
        {
            throw ImTypeRefusal(fn, number, name, types, a.ClassName, line, col);
        }

        if (real && a.IsComplex)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{fn}:expectedReal", ImInputWords(number, name) + "real.");
        }

        if (nonsparse && a.IsSparse)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{fn}:expectedNonsparse", ImInputWords(number, name) + "nonsparse.");
        }
    }

    private static void ImValidateNumericOrLogical(string fn, ImArray a, int number, string name, bool nonsparse, int line, int col) =>
        ImValidate(fn, a, number, name, ImNumericLogicalTypes, static x => x.IsNumericOrLogical, real: true, nonsparse, line, col);

    /// <summary>checkForSameSizeAndClass, the private helper immultiply, imdivide and imabsdiff share.</summary>
    private static void ImSameSizeAndClass(ImArray x, ImArray y, int line, int col)
    {
        if (x.ClassName != y.ClassName)
        {
            throw new JgsRuntimeException(line, col, "images:checkForSameSizeAndClass:mismatchedClass",
                "X and Y must have the same class.");
        }

        if (!SameDims(x.Dims, y.Dims))
        {
            throw new JgsRuntimeException(line, col, "images:checkForSameSizeAndClass:mismatchedSize",
                "X and Y must be the same size.");
        }
    }

    private static void ImArgumentCount(IReadOnlyList<JgsValue> args, int min, int max, bool narginchk, int line, int col)
    {
        if (args.Count < min)
        {
            throw narginchk
                ? new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.")
                : new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > max)
        {
            throw narginchk
                ? new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.")
                : new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }
    }

    // --- the element arithmetic ------------------------------------------------------------------------

    /// <summary>
    /// <c>x op y</c> by the rules the sources reach: an integer class combines only with its own class
    /// or a scalar (anything else is <c>MATLAB:mixedClasses</c>) and wins; single wins over double;
    /// logical and char read as double. The two shapes expand against each other as MATLAB's operators
    /// do. An integer answer is computed in double, then rounded half away from zero and saturated
    /// (NaN is 0), so 255 / 0 is 255 and -5 / 0 is intmin; a single answer is the double result of the
    /// two operands' float values, rounded to float.
    /// </summary>
    private static JgsValue ImBinary(ImArray x, ImArray y, Func<double, double, double> op, int line, int col)
    {
        ImRequireData(x, line, col);
        ImRequireData(y, line, col);
        JgsNumericClass result;
        if (x.IsInteger || y.IsInteger)
        {
            ImArray integer = x.IsInteger ? x : y;
            ImArray other = x.IsInteger ? y : x;
            if ((other.IsInteger && other.Class != integer.Class) || (!other.IsInteger && other.Count != 1))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:mixedClasses",
                    "Integers can only be combined with integers of the same class, or scalar doubles.");
            }

            result = integer.Class;
        }
        else
        {
            result = x.IsSingle || y.IsSingle ? JgsNumericClass.Single : JgsNumericClass.Double;
        }

        int[] dims = ImExpandedDims(x.Dims, y.Dims, line, col);
        int count = 1;
        foreach (int d in dims)
        {
            count *= d;
        }

        bool single = result == JgsNumericClass.Single;
        var answer = new double[count];
        if (SameDims(x.Dims, y.Dims) || x.Count == 1 || y.Count == 1)
        {
            for (int i = 0; i < count; i++)
            {
                double a = x.Data[x.Count == 1 ? 0 : i];
                double b = y.Data[y.Count == 1 ? 0 : i];
                answer[i] = ImFinish(single ? op((float)a, (float)b) : op(a, b), result);
            }
        }
        else
        {
            var at = new int[dims.Length];
            for (int i = 0; i < count; i++)
            {
                double a = x.Data[ImExpandedIndex(at, x.Dims)];
                double b = y.Data[ImExpandedIndex(at, y.Dims)];
                answer[i] = ImFinish(single ? op((float)a, (float)b) : op(a, b), result);
                for (int k = 0; k < at.Length && ++at[k] == dims[k]; k++)
                {
                    at[k] = 0;
                }
            }
        }

        return ImArrayOut(answer, dims, result, logical: false);
    }

    /// <summary>A sparse or complex operand past the checks is not computed here.</summary>
    private static void ImRequireData(ImArray a, int line, int col)
    {
        if (!a.HasData)
        {
            throw new JgsRuntimeException(line, col,
                $"This image verb takes a real, full array here, but got a {(a.IsSparse ? "sparse" : a.IsComplex ? "complex" : a.ClassName)} one.");
        }
    }

    /// <summary>One computed value through the class of the answer.</summary>
    private static double ImFinish(double value, JgsNumericClass numericClass) =>
        numericClass == JgsNumericClass.Double ? value : JgsNumericClasses.Convert(value, numericClass);

    /// <summary>The shape two operands expand to, or MATLAB's refusal.</summary>
    private static int[] ImExpandedDims(int[] a, int[] b, int line, int col)
    {
        int n = Math.Max(a.Length, b.Length);
        var dims = new int[n];
        for (int i = 0; i < n; i++)
        {
            int x = i < a.Length ? a[i] : 1;
            int y = i < b.Length ? b[i] : 1;
            if (x != y && x != 1 && y != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch",
                    "Arrays have incompatible sizes for this operation.");
            }

            dims[i] = x == 1 ? y : x;
        }

        return dims;
    }

    /// <summary>The column-major index of a subscript in an operand that may be singleton along it.</summary>
    private static int ImExpandedIndex(int[] at, int[] dims)
    {
        int index = 0;
        int stride = 1;
        for (int k = 0; k < at.Length; k++)
        {
            int size = k < dims.Length ? dims[k] : 1;
            index += (size == 1 ? 0 : at[k]) * stride;
            stride *= size;
        }

        return index;
    }

    // --- the verbs -------------------------------------------------------------------------------------

    /// <summary><c>Z = imadd(X, Y)</c> and <c>imadd(X, Y, outputClass)</c>, as imadd.m.</summary>
    private static JgsValue MatlabImadd(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ImArgumentCount(args, 2, 3, narginchk: false, line, col);
        ImArray x = ReadImArray(args[0]);
        ImArray y = ReadImArray(args[1]);
        string inputClass = x.ClassName;
        JgsValue outputClass = args.Count > 2
            ? args[2]
            : JgsValue.Str(x.IsLogical ? "double" : inputClass);

        bool scalarDoubleY = y.IsScalarDouble;
        bool sameInputOutputClass = IsTextScalar(outputClass) && TextOf(outputClass) == inputClass;
        if (sameInputOutputClass)
        {
            bool sameSizeAndClass = SameDims(x.Dims, y.Dims) && inputClass == y.ClassName;
            if (!scalarDoubleY && !sameSizeAndClass)
            {
                throw new JgsRuntimeException(line, col, "images:imadd:invalidInput",
                    "X and Y must have the same size and class or Y must be a scalar double.");
            }

            ImValidateNumericOrLogical("imadd", x, 1, "X", nonsparse: true, line, col);
            if (scalarDoubleY)
            {
                ImValidate("imadd", y, 2, "Y", "double", static a => a.ClassName == "double", real: true, nonsparse: true, line, col);
            }
            else
            {
                ImValidateNumericOrLogical("imadd", y, 2, "Y", nonsparse: true, line, col);
            }

            return ImBinary(x, y, static (a, b) => a + b, line, col);
        }

        return scalarDoubleY
            ? MatlabImlincomb([JgsValue.Number(1), args[0], args[1], outputClass], line, col)
            : MatlabImlincomb([JgsValue.Number(1), args[0], JgsValue.Number(1), args[1], outputClass], line, col);
    }

    /// <summary><c>Z = imsubtract(X, Y)</c>, as imsubtract.m: a logical X goes through imlincomb.</summary>
    private static JgsValue MatlabImsubtract(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ImArgumentCount(args, 2, 2, narginchk: false, line, col);
        ImArray x = ReadImArray(args[0]);
        ImArray y = ReadImArray(args[1]);
        bool scalarDoubleY = y.IsScalarDouble;
        if (!x.IsLogical)
        {
            bool sameSizeAndClass = SameDims(x.Dims, y.Dims) && x.ClassName == y.ClassName;
            if (!scalarDoubleY && !sameSizeAndClass)
            {
                throw new JgsRuntimeException(line, col, "images:imsubtract:invalidInput",
                    "X and Y must have the same size and class, or Y must be a scalar double.");
            }

            ImValidateNumericOrLogical("imsubtract", x, 1, "X", nonsparse: true, line, col);
            if (scalarDoubleY)
            {
                ImValidate("imsubtract", y, 2, "Y", "double", static a => a.ClassName == "double", real: true, nonsparse: true, line, col);
            }
            else
            {
                ImValidateNumericOrLogical("imsubtract", y, 2, "Y", nonsparse: true, line, col);
            }

            return ImBinary(x, y, static (a, b) => a - b, line, col);
        }

        JgsValue asDouble = JgsValue.Str("double");
        return scalarDoubleY
            ? MatlabImlincomb([JgsValue.Number(1), args[0], JgsValue.Number(-y.Data[0]), asDouble], line, col)
            : MatlabImlincomb([JgsValue.Number(1), args[0], JgsValue.Number(-1), args[1], asDouble], line, col);
    }

    /// <summary><c>Z = immultiply(X, Y)</c>, as immultiply.m, with its logical masking.</summary>
    private static JgsValue MatlabImmultiply(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ImArgumentCount(args, 2, 2, narginchk: false, line, col);
        ImArray x = ReadImArray(args[0]);
        ImArray y = ReadImArray(args[1]);

        // The source names Y as input number 1 as well.
        ImValidateNumericOrLogical("immultiply", x, 1, "X", nonsparse: true, line, col);
        ImValidateNumericOrLogical("immultiply", y, 1, "Y", nonsparse: true, line, col);
        if (x.IsLogical || y.IsLogical)
        {
            if (!SameDims(x.Dims, y.Dims))
            {
                throw new JgsRuntimeException(line, col, "images:immultiply:mismatchedSize", "X and Y must be the same size.");
            }

            if (x.IsLogical && y.IsLogical)
            {
                var both = new double[x.Count];
                for (int i = 0; i < both.Length; i++)
                {
                    both[i] = x.Data[i] != 0 && y.Data[i] != 0 ? 1 : 0;
                }

                return ImArrayOut(both, x.Dims, JgsNumericClass.Double, logical: true);
            }

            // Z = Y; Z(~X) = 0 for a logical X, and the other way round: the numeric one keeps its class.
            ImArray mask = x.IsLogical ? x : y;
            ImArray kept = x.IsLogical ? y : x;
            var masked = (double[])kept.Data.Clone();
            for (int i = 0; i < masked.Length; i++)
            {
                if (mask.Data[i] == 0)
                {
                    masked[i] = 0;
                }
            }

            return ImArrayOut(masked, kept.Dims, kept.Class, logical: false);
        }

        if (y.IsScalarDouble)
        {
            return ImBinary(x, y, static (a, b) => a * b, line, col);
        }

        ImSameSizeAndClass(x, y, line, col);
        return y.IsEmpty
            ? ImArrayOut([], y.Dims, y.Class, logical: false)
            : ImBinary(x, y, static (a, b) => a * b, line, col);
    }

    /// <summary><c>Z = imdivide(X, Y)</c>, as imdivide.m: a scalar double Y divides anything.</summary>
    private static JgsValue MatlabImdivide(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ImArgumentCount(args, 2, 2, narginchk: false, line, col);
        ImArray x = ReadImArray(args[0]);
        ImArray y = ReadImArray(args[1]);
        if (y.IsScalarDouble && y.HasData && (x.IsNumericOrLogical || x.IsChar) && x.HasData)
        {
            return ImBinary(x, y, static (a, b) => a / b, line, col);
        }

        if (x.IsLogical && y.IsLogical)
        {
            // double(X) ./ double(Y).
            return ImBinary(x, y, static (a, b) => a / b, line, col);
        }

        ImValidate("imdivide", x, 1, "X", ImNumericTypes, static a => a.IsNumeric, real: true, nonsparse: false, line, col);
        ImValidate("imdivide", y, 2, "Y", ImNumericTypes, static a => a.IsNumeric, real: true, nonsparse: false, line, col);
        ImSameSizeAndClass(x, y, line, col);
        return y.IsEmpty
            ? ImArrayOut([], y.Dims, y.Class, logical: false)
            : ImBinary(x, y, static (a, b) => a / b, line, col);
    }

    /// <summary><c>Z = imabsdiff(X, Y)</c>, as imabsdiff.m: |X - Y| in the class, saturated.</summary>
    private static JgsValue MatlabImabsdiff(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ImArgumentCount(args, 2, 2, narginchk: true, line, col);
        ImArray x = ReadImArray(args[0]);
        ImArray y = ReadImArray(args[1]);
        ImValidate("imabsdiff", x, 1, "X", ImNumericLogicalTypes, static a => a.IsNumericOrLogical, real: true, nonsparse: false, line, col);
        ImValidate("imabsdiff", y, 2, "Y", ImNumericLogicalTypes, static a => a.IsNumericOrLogical, real: true, nonsparse: false, line, col);
        ImSameSizeAndClass(x, y, line, col);
        ImRequireData(x, line, col);
        ImRequireData(y, line, col);
        var answer = new double[x.Count];
        bool single = x.IsSingle;
        for (int i = 0; i < answer.Length; i++)
        {
            double d = Math.Abs(x.Data[i] - y.Data[i]);
            answer[i] = x.IsLogical ? (d != 0 ? 1 : 0) : single ? (float)d : ImFinish(d, x.Class);
        }

        return ImArrayOut(answer, x.Dims, x.Class, x.IsLogical);
    }

    /// <summary>
    /// <c>imcomplement(im)</c>, as imcomplement.m: <c>~im</c>, <c>intmax - im</c>, <c>bitcmp(im)</c>
    /// or <c>1 - im</c> by class. A complex double is <c>1 - im</c> too (imcomplement(1+2i) is 0-2i,
    /// and a result with no imaginary part left is real, as the operator narrows it: open item 335,
    /// ADR 0373); any other complex class is refused.
    /// </summary>
    private static JgsValue MatlabImcomplement(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ImArgumentCount(args, 1, 1, narginchk: false, line, col);
        ImArray im = ReadImArray(args[0]);
        ImValidate("imcomplement", im, 0, string.Empty, ImLogicalNumericTypes, static a => a.IsNumericOrLogical,
            real: false, nonsparse: true, line, col);
        if (im.IsComplex)
        {
            if (im.ClassName != "double")
            {
                throw new JgsRuntimeException(line, col, "images:imcomplement:complexInput", "Input array must be real.");
            }

            Complex[] flat = ColumnComplex("imcomplement", args[0], line, col);
            var complemented = new Complex[flat.Length];
            bool anyImaginary = false;
            for (int i = 0; i < flat.Length; i++)
            {
                complemented[i] = Complex.One - flat[i];
                anyImaginary |= complemented[i].Imaginary != 0;
            }

            if (!anyImaginary)
            {
                return ImArrayOut(Array.ConvertAll(complemented, static z => z.Real), im.Dims, JgsNumericClass.Double, logical: false);
            }

            return complemented.Length == 1 && im.Dims.All(static d => d == 1)
                ? JgsValue.ComplexNum(complemented[0])
                : ComplexStorage(complemented, im.Dims);
        }

        ImRequireData(im, line, col);
        var answer = new double[im.Count];
        (double min, double max) = JgsNumericClasses.Range(im.Class);
        for (int i = 0; i < answer.Length; i++)
        {
            double v = im.Data[i];
            answer[i] = im.IsLogical ? (v == 0 ? 1 : 0)
                : !im.IsInteger ? (im.IsSingle ? (float)(1 - v) : 1 - v)
                : min == 0 ? max - v
                : -v - 1;
        }

        return ImArrayOut(answer, im.Dims, im.Class, im.IsLogical);
    }

    /// <summary>
    /// <c>imlincomb(K1, A1, K2, A2, ..., Kn, An, K, outputClass)</c>, as imlincomb.m: its argument
    /// checks, its four fast paths through the operators (which expand and refuse as the operators do),
    /// and otherwise imlincombc: the weighted sum in double, then the output class (rounded half away
    /// from zero and saturated for an integer class).
    /// </summary>
    private static JgsValue MatlabImlincomb(IReadOnlyList<JgsValue> givenArgs, int line, int col)
    {
        ImArgumentCount(givenArgs, 2, int.MaxValue, narginchk: true, line, col);
        var args = givenArgs.ToList();
        string outputClass;
        if (args[^1].Type == JgsType.String || IsStringScalar(args[^1]))
        {
            outputClass = ImLincombClass(TextOf(args[^1]), args.Count, line, col);
            args.RemoveAt(args.Count - 1);
        }
        else
        {
            outputClass = args.Count > 1 && IsLogicalValue(args[1]) ? "double" : ClassOf(args[1], JgsDialect.Matlab);
        }

        var images = new List<ImArray>();
        for (int p = 1; p < args.Count; p += 2)
        {
            images.Add(ReadImArray(args[p]));
        }

        if (images.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "imlincomb needs an array after its first weight.");
        }

        var scalars = new List<double>();
        for (int p = 0; p < args.Count; p += 2)
        {
            ImArray k = ReadImArray(args[p]);
            string name = $"K{(p / 2) + 1}";
            ImValidate("imlincomb", k, p + 1, name, "double", static a => a.ClassName == "double", real: true, nonsparse: true, line, col);
            if (k.Count != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:imlincomb:expectedScalar", ImInputWords(p + 1, name) + "a scalar.");
            }

            scalars.Add(k.Data[0]);
        }

        if (images[0].ClassName == outputClass)
        {
            if (images.Count == 2 && scalars.Count == 2)
            {
                if (scalars[0] == 1 && scalars[1] == 1)
                {
                    return ImBinary(images[0], images[1], static (a, b) => a + b, line, col);
                }

                if (scalars[0] == 1 && scalars[1] == -1)
                {
                    return ImBinary(images[0], images[1], static (a, b) => a - b, line, col);
                }

                if (scalars[0] == -1 && scalars[1] == 1)
                {
                    return ImBinary(images[1], images[0], static (a, b) => a - b, line, col);
                }
            }

            if (images.Count == 1 && scalars.Count == 2 && scalars[0] == 1)
            {
                ImArray constant = ReadImArray(JgsValue.Number(scalars[1]));
                return ImBinary(images[0], constant, static (a, b) => a + b, line, col);
            }
        }

        return ImLincombC(images, scalars, outputClass, line, col);
    }

    /// <summary>validatestring(word, imlincomb's classes, 'imlincomb', 'OUTPUT_CLASS', number).</summary>
    private static string ImLincombClass(string word, int number, int line, int col)
    {
        string choices = string.Join(", ", ImLincombClasses.Select(static c => $"'{c}'"));
        string head = $"Expected input number {number}, OUTPUT_CLASS, to match one of these values:\n\n{choices}\n\n";
        string? exact = ImLincombClasses.FirstOrDefault(c => c.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        string[] starts = word.Length == 0
            ? []
            : [.. ImLincombClasses.Where(c => c.StartsWith(word, StringComparison.OrdinalIgnoreCase))];
        return starts.Length switch
        {
            1 => starts[0],
            0 => throw new JgsRuntimeException(line, col, "MATLAB:imlincomb:unrecognizedStringChoice",
                head + $"The input, '{word}', did not match any of the valid values."),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:imlincomb:ambiguousStringChoice",
                head + $"The input, '{word}', matched more than one valid value."),
        };
    }

    /// <summary>imlincombc: the arrays agree in class and size, and the sum goes into the class.</summary>
    private static JgsValue ImLincombC(List<ImArray> images, List<double> scalars, string outputClass, int line, int col)
    {
        ImArray first = images[0];
        foreach (ImArray image in images)
        {
            if (!image.IsNumericOrLogical || !image.HasData)
            {
                throw new JgsRuntimeException(line, col,
                    $"imlincomb: the arrays must be real, full numeric or logical arrays, but one is {image.ClassName}.");
            }

            if (image.ClassName != first.ClassName)
            {
                throw new JgsRuntimeException(line, col, "images:imlincomb:mismatchedArrayClass",
                    "Function imlincomb expected its array input arguments (A1, A2, ...) to have the same class.");
            }
        }

        foreach (ImArray image in images)
        {
            if (!SameDims(image.Dims, first.Dims))
            {
                throw new JgsRuntimeException(line, col, "images:imlincomb:mismatchedArraySize",
                    "Function imlincomb expected its array input arguments (A1, A2, ...) to be the same size.");
            }
        }

        JgsNumericClass target = JgsNumericClasses.Parse(outputClass) ?? JgsNumericClass.Double;
        double constant = scalars.Count > images.Count ? scalars[^1] : 0;
        var answer = new double[first.Count];
        for (int i = 0; i < answer.Length; i++)
        {
            double sum = 0;
            for (int j = 0; j < images.Count; j++)
            {
                sum += scalars[j] * images[j].Data[i];
            }

            answer[i] = JgsNumericClasses.Convert(sum + constant, target);
        }

        return ImArrayOut(answer, first.Dims, target, logical: false);
    }

    // --- imtile ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>out = imtile(I, ...)</c> in MATLAB code, as imtile.m and images.internal.createMontage: the
    /// frames of a cell of arrays, an M-by-N-by-P stack or an M-by-N-by-1-or-3-by-P array laid out on a
    /// grid, answered as an array of the frames' class. The first frame sets the thumbnail size and any
    /// other frame is resized to it (bicubic by default); frames of different classes go to double, a
    /// grey frame among colour ones is repeated across three planes, and a logical first frame in a
    /// cell becomes uint8. A background colour makes every frame uint8 RGB. Frames may be named by
    /// file (a char row, a string array or a cell of names, read as imread reads them and expanded
    /// through the file's own colour map), and an indexed image's colour map may follow the frames
    /// (open item 335, ADR 0373).
    /// </summary>
    private static JgsValue MatlabImtile(
        IReadOnlyList<JgsValue> args, int line, int col, Func<string, string> resolve, JgsRunningDialect dialect)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 12)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsValue source = args[0];
        if (source.Type == JgsType.Cell && source.AsCell.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:badsubscript", "Index exceeds array bounds.");
        }

        static bool IsName(JgsValue v) => v.Type == JgsType.String || v.IsStringArray;
        if (source.Type == JgsType.Cell)
        {
            bool names = IsName(source.AsCell[0]);
            if (source.AsCell.Any(v => IsName(v) != names))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:images:montage:mixedInput",
                    "Cannot mix workspace variables with filenames.");
            }
        }

        // parse_inputs: the first char argument after the frames starts the name-value pairs; a
        // second argument before it is the colour map.
        int charStart = -1;
        for (int k = 1; k < args.Count; k++)
        {
            if (args[k].Type == JgsType.String || IsStringScalar(args[k]))
            {
                charStart = k;
                break;
            }
        }

        JgsValue? cmapValue = null;
        if ((charStart < 0 && args.Count == 2) || charStart == 2)
        {
            cmapValue = args[1];
        }

        if (charStart < 0 && args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:images:montage:nonCharParam",
                "Parameter names must be character arrays.");
        }

        double[]? gridSize = null;
        double[]? thumbnailSize = null;
        int[] border = [0, 0];
        double[]? background = null;
        double[]? frames = null;
        Geometry.Interpolation method = Geometry.Interpolation.Bicubic;
        string[] parameters = ["GridSize", "Frames", "ThumbnailSize", "ThumbnailInterpolation", "BorderSize", "BackgroundColor"];
        for (int k = charStart < 0 ? args.Count : charStart; k < args.Count; k += 2)
        {
            string word = TextOf(args[k]);
            string? parameter = parameters.FirstOrDefault(p => p.StartsWith(word, StringComparison.OrdinalIgnoreCase) && word.Length > 0)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:imtile:unrecognizedStringChoice",
                    $"Expected input number {k + 1}, PARAM, to match one of these values:\n\n"
                    + string.Join(", ", parameters.Select(static p => $"'{p}'"))
                    + $"\n\nThe input, '{word}', did not match any of the valid values.");
            if (k + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:images:montage:missingParameterValue",
                    $"Missing value for parameter '{parameter}'.");
            }

            JgsValue value = args[k + 1];
            switch (parameter)
            {
                case "GridSize":
                    ImtileNumericParameter(value, k + 2, "GridSize", line, col);
                    gridSize = ToDoubles("imtile", value, line, col);
                    if (gridSize.Length != 2)
                    {
                        throw new JgsRuntimeException(line, col, "imtile: GridSize is two numbers, [rows cols].");
                    }

                    if (gridSize.All(static g => !double.IsFinite(g)))
                    {
                        gridSize = null;
                    }

                    break;
                case "ThumbnailSize":
                    // validateattributes(..., {'numeric'}) on a nonempty value: "auto" is a char here
                    // and refused, as R2025b's imtile refuses it (createMontage's "auto" is montage's).
                    if (!ImIsEmptyValue(value))
                    {
                        ImtileNumericParameter(value, k + 2, "ThumbnailSize", line, col);
                    }

                    thumbnailSize = ToDoubles("imtile", value, line, col);
                    if (thumbnailSize.Length == 0 || thumbnailSize.All(static t => !double.IsFinite(t)))
                    {
                        thumbnailSize = null;
                    }
                    else if (thumbnailSize.Length != 2)
                    {
                        throw new JgsRuntimeException(line, col, "imtile: ThumbnailSize is two numbers, [rows cols].");
                    }

                    break;
                case "ThumbnailInterpolation":
                    method = TextOf(value).ToLowerInvariant() switch
                    {
                        "nearest" => Geometry.Interpolation.Nearest,
                        "bilinear" => Geometry.Interpolation.Bilinear,
                        "bicubic" => Geometry.Interpolation.Bicubic,
                        "lanczos2" => Geometry.Interpolation.Lanczos2,
                        "lanczos3" => Geometry.Interpolation.Lanczos3,
                        string other => throw new JgsRuntimeException(line, col,
                            $"imtile: ThumbnailInterpolation '{other}' is not one this engine resizes with."),
                    };
                    break;
                case "Frames":
                    frames = ToDoubles("imtile", value, line, col);
                    if (IsLogicalValue(value))
                    {
                        frames = [.. frames.Select(static (f, i) => (f, i)).Where(static p => p.f != 0).Select(static p => (double)(p.i + 1))];
                    }

                    break;
                case "BorderSize":
                    double[] b = ToDoubles("imtile", value, line, col);
                    if (b.Length == 1)
                    {
                        b = [b[0], b[0]];
                    }

                    if (b.Length != 2 || b.Any(static v => v < 0 || v != Math.Floor(v)))
                    {
                        throw new JgsRuntimeException(line, col, "imtile: BorderSize is one or two nonnegative integers.");
                    }

                    border = [(int)b[0], (int)b[1]];
                    break;
                case "BackgroundColor":
                    background = ImtileBackground(value, line, col);
                    break;
            }
        }

        // The frames, each an array of its own class, at most three planes.
        var frameList = new List<ImArray>();
        ImArray? cmap = cmapValue is null ? null : ReadImArray(cmapValue);
        if (cmap is not null && cmap.Count == 0)
        {
            cmap = null;
        }

        // getImages' sources: a cell of arrays or of file names, and a string array or char row of names.
        List<JgsValue>? items = source.Type == JgsType.Cell ? [.. source.AsCell]
            : source.IsStringArray ? [.. Enumerable.Range(0, source.ArrayLength).Select(source.ElementAt)]
            : source.Type == JgsType.String ? [source]
            : null;
        bool fromCell = items is not null;
        if (fromCell)
        {
            foreach (JgsValue element in items!)
            {
                ImArray frame;
                double[,]? fileMap = null;
                if (IsName(element))
                {
                    (frame, fileMap) = ImtileReadFile(TextOf(element), resolve, dialect, line, col);
                }
                else
                {
                    frame = ReadImArray(element.Type == JgsType.Image ? ImageNumbers(element) : element);
                }

                if (frame.IsEmpty)
                {
                    frame = ReadImArray(JgsValue.Number(0));
                }

                if (frame.Dims.Length > 3 || (frame.Dims.Length == 3 && frame.Dims[2] != 3))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:images:imageDisplayValidateParams:unsupportedDimension",
                        "Multi-plane image inputs must be RGB images of size MxNx3.");
                }

                ImtileCheckFrame(frame, line, col);
                if (frame.Dims.Length < 3 && cmap is not null)
                {
                    frame = ImIndexToRgb(frame, ImMapRows(cmap), line, col);
                }
                else if (frame.Dims.Length < 3 && fileMap is not null)
                {
                    frame = ImIndexToRgb(frame, fileMap, line, col);
                }

                frameList.Add(frame);
            }
        }
        else
        {
            ImArray stack = ReadImArray(source.Type == JgsType.Image ? ImageNumbers(source) : source);
            ImtileCheckFrame(stack, line, col);
            if (stack.Class == JgsNumericClass.Int16 && stack.IsNumeric && cmap is not null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:images:montage:invalidIndexedImage",
                    "An indexed image can only be uint8, uint16, double, single, or logical.");
            }

            if (cmapValue is not null && ClassOf(cmapValue, JgsDialect.Matlab) != "double")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:createMontage:invalidType",
                    "Expected input number 1, MAP, to be one of these types:\n\ndouble\n\n"
                    + $"Instead its type was {ClassOf(cmapValue, JgsDialect.Matlab)}.");
            }

            if (stack.IsEmpty)
            {
                return JgsMatrix.FromColumnMajor(new double[0], 0, 0);
            }

            int[] d = stack.Dims;
            int rows = d[0];
            int cols = d.Length > 1 ? d[1] : 1;
            int bands;
            int count;
            if (d.Length == 4)
            {
                if (d[2] != 1 && d[2] != 3)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:images:montage:notVolume",
                        "Multi-frame image arrays must be M-by-N-by-1-by-P or M-by-N-by-3-by-P.");
                }

                bands = d[2];
                count = d[3];
            }
            else if (d.Length > 4)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:images:montage:notVolume",
                    "Multi-frame image arrays must be M-by-N-by-1-by-P or M-by-N-by-3-by-P.");
            }
            else
            {
                bands = 1;
                count = d.Length == 3 ? d[2] : 1;
            }

            int frameSize = rows * cols * bands;
            for (int f = 0; f < count; f++)
            {
                var slice = new ImArray
                {
                    ClassName = stack.ClassName,
                    Data = stack.Data.AsSpan(f * frameSize, frameSize).ToArray(),
                    Dims = bands == 3 ? [rows, cols, 3] : [rows, cols],
                    Class = stack.Class,
                    IsNumeric = stack.IsNumeric,
                    IsLogical = stack.IsLogical,
                };
                frameList.Add(bands == 1 && cmap is not null ? ImIndexToRgb(slice, ImMapRows(cmap), line, col) : slice);
            }
        }

        if (frames is not null)
        {
            if (frames.Any(f => f < 1 || f > frameList.Count || f != Math.Floor(f)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:images:montage:invalidFrames",
                    "An index in FRAMES is outside the range of frames in the image source.");
            }

            frameList = [.. frames.Select(f => frameList[(int)f - 1])];
            if (frameList.Count == 0)
            {
                return JgsMatrix.FromColumnMajor(new double[0], 0, 0);
            }
        }

        int nFrames = frameList.Count;
        List<ImArray> tiles = fromCell
            ? ImtileCellFrames(frameList, thumbnailSize, method, border, background)
            : [.. frameList.Select(f => ImtileThumbnail(f, thumbnailSize, method, border, background))];

        ImArray sample = tiles[0];
        int tileRows = sample.Dims[0];
        int tileCols = sample.Dims[1];
        int planes = tiles.Max(static t => t.Dims.Length == 3 ? 3 : 1);

        double[]? grid = gridSize is null ? null : [.. gridSize];
        if (grid is not null && grid.Count(static g => !double.IsFinite(g)) == 1)
        {
            int nan = double.IsFinite(grid[0]) ? 1 : 0;
            grid[nan] = Math.Ceiling(nFrames / grid[1 - nan]);
        }

        int gridRows;
        int gridCols;
        if (grid is null)
        {
            if (nFrames == 1)
            {
                gridRows = 1;
                gridCols = 1;
            }
            else
            {
                gridCols = (int)Math.Ceiling(Math.Sqrt(1.0 * tileRows * nFrames / tileCols));
                gridRows = (int)Math.Ceiling(nFrames / (double)gridCols);
            }
        }
        else
        {
            gridRows = (int)grid[0];
            gridCols = (int)grid[1];
            if ((long)gridRows * gridCols < nFrames)
            {
                nFrames = gridRows * gridCols;
            }
        }

        int bigRows = gridRows * tileRows;
        int bigCols = gridCols * tileCols;
        var big = new double[bigRows * bigCols * planes];
        if (background is not null)
        {
            // bigImage = repmat(backgroundColor, ...): the frames are uint8 by now.
            for (int p = 0; p < planes; p++)
            {
                Array.Fill(big, background[p], p * bigRows * bigCols, bigRows * bigCols);
            }
        }

        for (int k = 0; k < nFrames; k++)
        {
            ImArray tile = tiles[k];
            int top = (k / gridCols) * tileRows;
            int left = (k % gridCols) * tileCols;
            bool grey = tile.Dims.Length < 3;
            for (int p = 0; p < planes; p++)
            {
                int from = grey ? 0 : p;
                for (int c = 0; c < tileCols; c++)
                {
                    for (int r = 0; r < tileRows; r++)
                    {
                        big[(p * bigRows * bigCols) + ((left + c) * bigRows) + top + r] =
                            tile.Data[(from * tileRows * tileCols) + (c * tileRows) + r];
                    }
                }
            }
        }

        int[] dims = planes == 3 ? [bigRows, bigCols, 3] : [bigRows, bigCols];
        return ImArrayOut(big, dims, sample.Class, sample.IsLogical);
    }

    /// <summary>validateattributes(value, {'numeric'}, ..., 'imtile', name, number): the class check.</summary>
    private static void ImtileNumericParameter(JgsValue value, int number, string name, int line, int col)
    {
        if (!IsNumericValue(value) || IsLogicalValue(value))
        {
            throw ImTypeRefusal("imtile", number, name, ImNumericTypes, ClassOf(value, JgsDialect.Matlab), line, col);
        }
    }

    /// <summary><c>isempty(value)</c> for a parameter value.</summary>
    private static bool ImIsEmptyValue(JgsValue value) =>
        value.Type == JgsType.String ? value.AsString.Length == 0
        : value.Type == JgsType.Array && value.ArrayLength == 0;

    /// <summary>
    /// images.internal.getImageFromFile: a missing file is refused by name (as given), and otherwise the
    /// file is read as imread reads it, with its colour map (null for a file without one).
    /// </summary>
    private static (ImArray Frame, double[,]? Map) ImtileReadFile(
        string name, Func<string, string> resolve, JgsRunningDialect dialect, int line, int col)
    {
        string path = resolve(name);
        if (!File.Exists(path))
        {
            throw new JgsRuntimeException(line, col, "images:getImageFromFile:fileDoesNotExist",
                $"Cannot find the specified file: \"{name}\".");
        }

        try
        {
            (ImageBuffer image, double[,]? palette, ImageBuffer? alpha) = ReadImageFileSamples(path, 0, dialect.IsMatlab);
            alpha?.Dispose();
            JgsValue picture = ImreadPicture(image, dialect);
            ImArray frame = ReadImArray(picture.Type == JgsType.Image ? ImageNumbers(picture) : picture);
            return (frame, palette is { Length: > 0 } ? palette : null);
        }
        catch (JGraph.Imaging.Codecs.ImageFileRefusal refusal)
        {
            throw new JgsRuntimeException(line, col, refusal.Identifier, refusal.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new JgsRuntimeException(line, col, $"imread: cannot read '{path}': {ex.Message}");
        }
    }

    /// <summary>A colour map argument as rows of three.</summary>
    private static double[,] ImMapRows(ImArray map)
    {
        int rows = map.Dims[0];
        int cols = map.Dims.Length > 1 ? map.Dims[1] : 1;
        var rgb = new double[rows, 3];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < Math.Min(cols, 3); c++)
            {
                rgb[r, c] = map.Data[(c * rows) + r];
            }
        }

        return rgb;
    }

    /// <summary>
    /// ind2rgb, as matlab.images.internal.ind2rgb: an integer or logical index is zero-based and a
    /// float one-based, each clamped to the map, and the answer a double M-by-N-by-3 array.
    /// </summary>
    private static ImArray ImIndexToRgb(ImArray indices, double[,] map, int line, int col)
    {
        int entries = map.GetLength(0);
        int count = indices.Count;
        var data = new double[count * 3];
        bool floating = indices.IsNumeric && indices.Class is JgsNumericClass.Double or JgsNumericClass.Single && !indices.IsLogical;
        for (int i = 0; i < count; i++)
        {
            double index = floating ? indices.Data[i] : indices.Data[i] + 1;
            index = double.IsNaN(index) ? entries : Math.Max(1, Math.Min(index, entries));
            if (index != Math.Floor(index))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:badsubscript",
                    "Array indices must be positive integers or logical values.");
            }

            for (int p = 0; p < 3; p++)
            {
                data[(p * count) + i] = map[(int)index - 1, p];
            }
        }

        return new ImArray
        {
            ClassName = "double",
            Data = data,
            Dims = [indices.Dims[0], indices.Dims.Length > 1 ? indices.Dims[1] : 1, 3],
            Class = JgsNumericClass.Double,
            IsNumeric = true,
        };
    }

    /// <summary>A frame must be one of the classes montage takes.</summary>
    private static void ImtileCheckFrame(ImArray frame, int line, int col)
    {
        if (!(frame.IsLogical || (frame.IsNumeric && frame.Class is JgsNumericClass.UInt8 or JgsNumericClass.Double
            or JgsNumericClass.UInt16 or JgsNumericClass.Single or JgsNumericClass.Int16)) || frame.IsComplex || frame.IsSparse)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:imtile:invalidType",
                "Expected input number 1, I, BW, or RGB, to be one of these types:\n\n"
                + $"uint8, double, uint16, logical, single, int16\n\nInstead its type was {frame.ClassName}.");
        }
    }

    /// <summary>
    /// getImages: every frame resized to the first one's thumbnail, the class settled as frames arrive
    /// (a logical first frame becomes uint8; a frame of another class sends all of them to double),
    /// and grey frames repeated across three planes once a colour frame has been seen.
    /// </summary>
    private static List<ImArray> ImtileCellFrames(
        List<ImArray> frames, double[]? thumbnailSize, Geometry.Interpolation method, int[] border, double[]? background)
    {
        ImArray first = ImtileThumbnail(frames[0], thumbnailSize, method, border, background);
        double[] thumbnail = [first.Dims[0] - (2 * border[0]), first.Dims[1] - (2 * border[1])];
        if (first.IsLogical)
        {
            first = ImConvert(first, JgsNumericClass.UInt8);
        }

        var tiles = new List<ImArray> { first };
        string tileClass = first.ClassName;
        for (int k = 1; k < frames.Count; k++)
        {
            ImArray tile = ImtileThumbnail(frames[k], thumbnail, method, border, background);
            if (tile.ClassName != tileClass)
            {
                if (tileClass != "double")
                {
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        tiles[i] = ImConvert(tiles[i], JgsNumericClass.Double);
                    }

                    tileClass = "double";
                }

                tile = ImConvert(tile, JgsNumericClass.Double);
            }

            tiles.Add(tile);
        }

        return tiles;
    }

    /// <summary>
    /// resizeToThumbnail: resize to the thumbnail (keeping the aspect ratio), pad it centred, add the
    /// border, and with a background colour make it uint8 RGB with that colour round it.
    /// </summary>
    private static ImArray ImtileThumbnail(
        ImArray frame, double[]? thumbnailSize, Geometry.Interpolation method, int[] border, double[]? background)
    {
        int rows = frame.Dims[0];
        int cols = frame.Dims.Length > 1 ? frame.Dims[1] : 1;
        double[] target = thumbnailSize is null ? [rows, cols] : [.. thumbnailSize.Select(static t => double.IsFinite(t) ? t : double.NaN)];
        ImArray resized = ImtileResize(frame, target, method);
        int newRows = resized.Dims[0];
        int newCols = resized.Dims[1];
        int planes = resized.Dims.Length == 3 ? 3 : 1;

        static int Pre(double pad) => double.IsNaN(pad) ? 0 : Math.Max((int)Math.Floor(pad / 2), 0);
        static int Post(double pad) => double.IsNaN(pad) ? 0 : Math.Max((int)(pad - Math.Max(Math.Floor(pad / 2), 0)), 0);
        double padRows = target[0] - newRows;
        double padCols = target[1] - newCols;
        int top = Pre(padRows) + border[0];
        int bottom = Post(padRows) + border[0];
        int left = Pre(padCols) + border[1];
        int right = Post(padCols) + border[1];
        if (top == 0 && bottom == 0 && left == 0 && right == 0 && background is null)
        {
            return resized;
        }

        if (background is not null)
        {
            resized = ImConvert(resized, JgsNumericClass.UInt8);
            planes = 3;
        }

        int outRows = top + newRows + bottom;
        int outCols = left + newCols + right;
        var data = new double[outRows * outCols * planes];
        for (int p = 0; p < planes; p++)
        {
            if (background is not null)
            {
                Array.Fill(data, background[p], p * outRows * outCols, outRows * outCols);
            }

            int from = resized.Dims.Length == 3 ? p : 0;
            for (int c = 0; c < newCols; c++)
            {
                for (int r = 0; r < newRows; r++)
                {
                    data[(p * outRows * outCols) + ((left + c) * outRows) + top + r] =
                        resized.Data[(from * newRows * newCols) + (c * newRows) + r];
                }
            }
        }

        return new ImArray
        {
            ClassName = resized.ClassName,
            Data = data,
            Dims = planes == 3 ? [outRows, outCols, 3] : [outRows, outCols],
            Class = resized.Class,
            IsNumeric = resized.IsNumeric,
            IsLogical = resized.IsLogical,
        };
    }

    /// <summary>
    /// resizeImage: imresize to [rows NaN] or [NaN cols], whichever pads less, the missing size by
    /// ceil as imresize computes it. A frame already the size is left alone.
    /// </summary>
    private static ImArray ImtileResize(ImArray frame, double[] target, Geometry.Interpolation method)
    {
        int rows = frame.Dims[0];
        int cols = frame.Dims.Length > 1 ? frame.Dims[1] : 1;
        double aspect = cols / (double)rows;
        double wantRows = target[0];
        double wantCols = target[1];
        if (double.IsNaN(wantRows))
        {
            wantRows = Math.Ceiling(wantCols / aspect);
        }
        else if (double.IsNaN(wantCols))
        {
            wantCols = Math.Ceiling(wantRows * aspect);
        }

        double padRowsOut = Math.Ceiling(wantRows * aspect) - wantCols;
        double padColsOut = Math.Ceiling(wantCols / aspect) - wantRows;
        // imresize(I, [r NaN]) scales both directions by r / rows (and the other way round).
        int newRows;
        int newCols;
        double scale;
        if (padRowsOut <= padColsOut)
        {
            newRows = (int)wantRows;
            newCols = (int)Math.Ceiling(wantRows * cols / rows);
            scale = newRows / (double)rows;
        }
        else
        {
            newCols = (int)wantCols;
            newRows = (int)Math.Ceiling(wantCols * rows / cols);
            scale = newCols / (double)cols;
        }

        if (newRows == rows && newCols == cols)
        {
            return frame;
        }

        int planes = frame.Dims.Length == 3 ? 3 : 1;
        using var buffer = new ImageBuffer(rows, cols, planes);
        for (int p = 0; p < planes; p++)
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    buffer[r, c, p] = frame.Data[(p * rows * cols) + (c * rows) + r];
                }
            }
        }

        using ImageBuffer resized = Geometry.ResizeByScale(buffer, newRows, newCols, scale, scale, method);
        var data = new double[newRows * newCols * planes];
        for (int p = 0; p < planes; p++)
        {
            for (int c = 0; c < newCols; c++)
            {
                for (int r = 0; r < newRows; r++)
                {
                    double v = resized[r, c, p];
                    data[(p * newRows * newCols) + (c * newRows) + r] = frame.IsLogical
                        ? (Math.Round(v * 255.0, MidpointRounding.AwayFromZero) > 128 ? 1 : 0)
                        : ImFinish(frame.IsSingle ? (float)v : v, frame.Class);
                }
            }
        }

        return new ImArray
        {
            ClassName = frame.ClassName,
            Data = data,
            Dims = planes == 3 ? [newRows, newCols, 3] : [newRows, newCols],
            Class = frame.Class,
            IsNumeric = frame.IsNumeric,
            IsLogical = frame.IsLogical,
        };
    }

    /// <summary>
    /// <c>im2double</c> or <c>images.internal.touint8</c> of an array: an integer class is read in its
    /// own range (int16 offset by 32768), a float clamped to [0, 1] for uint8.
    /// </summary>
    private static ImArray ImConvert(ImArray a, JgsNumericClass target)
    {
        if (!a.IsLogical && a.Class == target)
        {
            return a;
        }

        var data = new double[a.Count];
        for (int i = 0; i < data.Length; i++)
        {
            double unit = a.IsLogical ? (a.Data[i] != 0 ? 1 : 0)
                : a.Class switch
                {
                    JgsNumericClass.UInt8 => a.Data[i] / 255.0,
                    JgsNumericClass.UInt16 => a.Data[i] / 65535.0,
                    JgsNumericClass.Int16 => (a.Data[i] + 32768.0) / 65535.0,
                    _ => a.Data[i],
                };
            data[i] = target == JgsNumericClass.UInt8
                ? (a.Class == JgsNumericClass.UInt16 && !a.IsLogical ? JgsNumericClasses.Convert(a.Data[i] / 257.0, target)
                    : a.Class == JgsNumericClass.Int16 && !a.IsLogical ? JgsNumericClasses.Convert((a.Data[i] + 32768.0) / 257.0, target)
                    : JgsNumericClasses.Convert(double.IsNaN(unit) ? 0 : Math.Clamp(unit, 0, 1) * 255.0, target))
                : unit;
        }

        return new ImArray
        {
            ClassName = target.MatlabName(),
            Data = data,
            Dims = a.Dims,
            Class = target,
            IsNumeric = true,
        };
    }

    /// <summary>The background colour as touint8 makes it: three levels 0 to 255.</summary>
    private static double[] ImtileBackground(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String || IsStringScalar(value))
        {
            JGraph.Core.Drawing.Color color = OptionColor(value, line, col, "imtile");
            return [color.R, color.G, color.B];
        }

        double[] rgb = ToDoubles("imtile", value, line, col);
        if (rgb.Length != 3)
        {
            throw new JgsRuntimeException(line, col, "imtile: BackgroundColor is an RGB triplet or a colour name.");
        }

        ImArray triplet = ReadImArray(value);
        return [.. ImConvert(triplet, JgsNumericClass.UInt8).Data];
    }
}
