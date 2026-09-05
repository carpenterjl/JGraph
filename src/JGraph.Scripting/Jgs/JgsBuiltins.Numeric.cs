using System.Numerics;
using System.Text;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The numeric layer below the special functions: bit manipulation, radix conversion, the elementary
/// functions that exist because the obvious formula loses precision (<c>hypot</c>, <c>log1p</c>,
/// <c>expm1</c>), and the small integer helpers (<c>gcd</c>, <c>factorial</c>, <c>primes</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The number of bits a double can hold an exact integer in: <c>log2(flintmax)</c>.</summary>
    private const int FlintBits = 53;

    /// <summary>Registers the bit, radix, and elementary numeric builtins (M38).</summary>
    private static void RegisterNumericBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        RegisterBitBuiltins(Define);
        RegisterRadixBuiltins(Define);
        RegisterElementaryMath(Define);
        RegisterIntegerMath(Define);
        RegisterDenseAnswers(Define);
    }

    // --- Bit manipulation -------------------------------------------------------------------------

    private static void RegisterBitBuiltins(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // Every one of these takes MATLAB's optional trailing class name, which sets the width the
        // operation happens in. Without it the width is a double's 53 exact integer bits.
        void Bitwise(string name, Func<ulong, ulong, ulong> op) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, 2, 3, line, col);
                int bits = AssumedBits(name, args, 2, line, col);
                return Zip(name, args[0], args[1],
                    (a, b) => Mask(op(BitOperand(name, a, bits, line, col), BitOperand(name, b, bits, line, col)), bits),
                    line, col);
            });

        Bitwise("bitand", static (a, b) => a & b);
        Bitwise("bitor", static (a, b) => a | b);
        Bitwise("bitxor", static (a, b) => a ^ b);

        Define("bitcmp", (args, line, col) =>
        {
            ArityRange("bitcmp", args, 1, 2, line, col);
            int bits = AssumedBits("bitcmp", args, 1, line, col);
            return MapNumeric("bitcmp", args[0],
                x => Mask(~BitOperand("bitcmp", x, bits, line, col), bits), line, col);
        });

        Define("bitget", (args, line, col) =>
        {
            ArityRange("bitget", args, 2, 3, line, col);
            int bits = AssumedBits("bitget", args, 2, line, col);
            return Zip("bitget", args[0], args[1],
                (value, position) =>
                    (BitOperand("bitget", value, bits, line, col) >> BitPosition("bitget", position, bits, line, col)) & 1UL,
                line, col);
        });

        Define("bitset", (args, line, col) =>
        {
            ArityRange("bitset", args, 2, 4, line, col);

            // bitset(A, pos, v) and bitset(A, pos, assumedtype) collide on the third argument, so the
            // class name is recognized by being a string — exactly how MATLAB tells them apart.
            bool typeAtThree = args.Count >= 3 && args[2].Type == JgsType.String;
            int typeIndex = typeAtThree ? 2 : 3;
            int bits = AssumedBits("bitset", args, typeIndex, line, col);
            bool set = typeAtThree || args.Count < 3 || args[2].IsTruthy;

            return Zip("bitset", args[0], args[1],
                (value, position) =>
                {
                    ulong bit = 1UL << BitPosition("bitset", position, bits, line, col);
                    ulong original = BitOperand("bitset", value, bits, line, col);
                    return Mask(set ? original | bit : original & ~bit, bits);
                },
                line, col);
        });

        Define("bitshift", (args, line, col) =>
        {
            ArityRange("bitshift", args, 2, 3, line, col);
            int bits = AssumedBits("bitshift", args, 2, line, col);
            return Zip("bitshift", args[0], args[1],
                (value, shift) =>
                {
                    ulong original = BitOperand("bitshift", value, bits, line, col);
                    int by = (int)shift;
                    if (shift != Math.Floor(shift))
                    {
                        throw new JgsRuntimeException(line, col, "bitshift: the shift count must be a whole number.");
                    }

                    // A shift wider than the operand clears it rather than invoking C#'s shift-count
                    // wraparound, which would make bitshift(1, 64) come back as 1.
                    if (by >= bits || -by >= bits)
                    {
                        return 0.0;
                    }

                    return Mask(by >= 0 ? original << by : original >> -by, bits);
                },
                line, col);
        });
    }

    /// <summary>Reads the optional trailing class name, giving the width the bit operation runs in.</summary>
    private static int AssumedBits(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (index >= args.Count)
        {
            return FlintBits;
        }

        string type = Str(name, args, index, line, col);
        return type switch
        {
            "int8" or "uint8" => 8,
            "int16" or "uint16" => 16,
            "int32" or "uint32" => 32,
            "int64" or "uint64" => 64,
            "double" => FlintBits,
            _ => throw new JgsRuntimeException(line, col, $"{name}: '{type}' is not an integer class."),
        };
    }

    /// <summary>Reads one operand of a bit operation: a non-negative integer inside the assumed width.</summary>
    private static ulong BitOperand(string name, double value, int bits, int line, int col)
    {
        if (value < 0 || value != Math.Floor(value) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects non-negative whole numbers.");
        }

        if (value > MaxOfWidth(bits))
        {
            throw new JgsRuntimeException(line, col, $"{name}: {value} does not fit in {bits} bits.");
        }

        return (ulong)value;
    }

    /// <summary>Reads a 1-based bit position, as MATLAB numbers bits from the least significant.</summary>
    private static int BitPosition(string name, double position, int bits, int line, int col)
    {
        if (position != Math.Floor(position) || position < 1 || position > bits)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the bit position must be a whole number from 1 to {bits}.");
        }

        return (int)position - 1;
    }

    /// <summary>The largest value a width holds, kept as a double so 53-bit stays exact.</summary>
    private static double MaxOfWidth(int bits) => bits >= 64 ? ulong.MaxValue : Math.Pow(2, bits) - 1;

    /// <summary>Trims a result back to the assumed width and returns it as a double.</summary>
    private static double Mask(ulong value, int bits) => bits >= 64 ? value : value & ((1UL << bits) - 1);

    // --- Radix conversion -------------------------------------------------------------------------

    private static void RegisterRadixBuiltins(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // A scalar gives a char row; an array gives a char matrix, one row per element, every row
        // zero-padded to the widest — which is MATLAB's answer, and what a caller indexing rows of
        // dec2bin(0:7) is written against. (It was a cell before there was a char matrix.)
        void ToBase(string name, int radix, int typeArgument) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, 1, typeArgument + 1, line, col);
                int given = typeArgument == 1 ? radix : (int)Num(name, args, 1, line, col);
                int minimum = args.Count > typeArgument ? Count(name, args, typeArgument, line, col) : 1;
                return TextPerElement(name, args[0], x => ToRadix(name, x, given, minimum, line, col), line, col);
            });

        ToBase("dec2bin", 2, 1);
        ToBase("dec2hex", 16, 1);
        ToBase("dec2base", 0, 2);

        void FromBase(string name, int radix, bool radixIsArgument) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, radixIsArgument ? 2 : 1, radixIsArgument ? 2 : 1, line, col);
                int given = radixIsArgument ? (int)Num(name, args, 1, line, col) : radix;
                return NumberPerText(name, args[0], text => FromRadix(name, text, given, line, col), line, col);
            });

        FromBase("bin2dec", 2, false);
        FromBase("hex2dec", 16, false);
        FromBase("base2dec", 0, true);
    }

    /// <summary>Renders one non-negative integer in <paramref name="radix"/>, left-padded with zeros.</summary>
    private static string ToRadix(string name, double value, int radix, int minimum, int line, int col)
    {
        if (radix is < 2 or > 36)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the base must be between 2 and 36.");
        }

        ulong magnitude = BitOperand(name, value, 64, line, col);
        var digits = new StringBuilder();
        do
        {
            int digit = (int)(magnitude % (ulong)radix);
            digits.Insert(0, (char)(digit < 10 ? '0' + digit : 'A' + digit - 10));
            magnitude /= (ulong)radix;
        }
        while (magnitude > 0);

        return digits.ToString().PadLeft(Math.Max(minimum, 1), '0');
    }

    /// <summary>Reads one string of digits in <paramref name="radix"/>. Spaces are skipped, as in MATLAB.</summary>
    private static double FromRadix(string name, string text, int radix, int line, int col)
    {
        if (radix is < 2 or > 36)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the base must be between 2 and 36.");
        }

        double total = 0;
        bool any = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            int digit = char.IsAsciiDigit(c) ? c - '0'
                : char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) - 'A' + 10
                : -1;
            if (digit < 0 || digit >= radix)
            {
                throw new JgsRuntimeException(line, col, $"{name}: '{c}' is not a digit in base {radix}.");
            }

            total = (total * radix) + digit;
            any = true;
        }

        if (!any)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one digit.");
        }

        return total;
    }

    /// <summary>
    /// Maps a number or numeric array to text: a char row for one number, a char matrix with one
    /// zero-padded row per element for an array.
    /// </summary>
    private static JgsValue TextPerElement(string name, JgsValue value, Func<double, string> render, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Str(render(value.AsNumber));
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
        }

        double[] values = ToDoubles(name, value, line, col);
        if (values.Length == 1)
        {
            return JgsValue.Str(render(values[0]));
        }

        var rows = new string[values.Length];
        int widest = 0;
        for (int i = 0; i < values.Length; i++)
        {
            rows[i] = render(values[i]);
            widest = Math.Max(widest, rows[i].Length);
        }

        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = rows[i].PadLeft(widest, '0');
        }

        return JgsValue.CharMatrix(rows);
    }

    /// <summary>
    /// The inverse of <see cref="TextPerElement"/>: one string back to one number, and a char matrix
    /// or a cell of strings back to a column of them. Text with no digits in it is the empty array,
    /// which is what <c>bin2dec('')</c> answers in MATLAB.
    /// </summary>
    private static JgsValue NumberPerText(string name, JgsValue value, Func<string, double> read, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return string.IsNullOrWhiteSpace(value.AsString) ? JgsEmpty.Zero() : JgsValue.Number(read(value.AsString));
        }

        string[] pieces;
        if (value.IsCharMatrix)
        {
            pieces = value.CharMatrixRows();
        }
        else if (value.Type == JgsType.Cell)
        {
            JgsValue[] cells = value.AsCell;
            pieces = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, $"{name}: cell element {i + 1} is a {cells[i].TypeName}, not a string.");
                }

                pieces[i] = cells[i].AsString;
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a string or a cell of strings, but got a {value.TypeName}.");
        }

        var numbers = new double[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            numbers[i] = read(pieces[i]);
        }

        JgsValue column = Numbers(numbers);
        column.Reshape(numbers.Length, 1);
        return column;
    }

    // --- Elementary functions ---------------------------------------------------------------------

    private static void RegisterElementaryMath(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        void Math1(string name, Func<double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapNumeric(name, args[0], f, line, col); });

        void Math2(string name, Func<double, double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 2, line, col); return Zip(name, args[0], args[1], f, line, col); });

        // hypot, log1p, and expm1 exist because the obvious formula is inaccurate near the interesting
        // values: sqrt(x²+y²) overflows, and log(1+x) loses every significant digit for tiny x.
        Math2("hypot", double.Hypot);

        // M81: log2 and log1p leave the reals below their domain and now say so in complex, and the
        // other three carry a complex definition so a complex argument is not simply refused. Log1P
        // and ExpM1 keep their careful real forms — the complex arms are the plain identities, because
        // an argument small enough to need the refinement is not one that has gone complex.
        MathX(Define, "log2", Math.Log2, NonNegative, ComplexLog2);
        MathX(Define, "log1p", Log1P, AtLeastMinusOne, ComplexLog1P);
        MathX(Define, "expm1", ExpM1, Always, ComplexExpM1);

        MathX(Define, "deg2rad", static x => x * RadiansPerDegree, Always, static z => z * RadiansPerDegree);
        MathX(Define, "rad2deg", static x => x * DegreesPerRadian, Always, static z => z * DegreesPerRadian);

        Define("pow2", (args, line, col) =>
        {
            ArityRange("pow2", args, 1, 2, line, col);

            // pow2(x) is 2^x; pow2(f, e) is f·2^e, which ScaleB does by adjusting the exponent field —
            // exact, where multiplying by a computed power of two would round twice.
            return args.Count == 1
                ? MapNumeric("pow2", args[0], static x => Math.Pow(2.0, x), line, col)
                : Zip("pow2", args[0], args[1], static (f, e) => Math.ScaleB(f, (int)e), line, col);
        });

        Math2("nthroot", (x, n) =>
        {
            if (n == 0 || (x < 0 && n % 2 == 0))
            {
                throw new JgsRuntimeException(0, 0, "nthroot: a negative value has no real even root.");
            }

            // Signed root: (-8)^(1/3) as written would be complex, and nthroot exists to say -2.
            return x < 0 ? -Math.Pow(-x, 1.0 / n) : Math.Pow(x, 1.0 / n);
        });

        // The real* family is the "I know this must stay real" spelling: where sqrt(-1) hands back a
        // complex number, realsqrt(-1) is an error, which catches the bug at its source.
        void Real1(string name, Func<double, bool> valid, Func<double, double> f, string complaint) =>
            Math1(name, x => valid(x) ? f(x) : throw new JgsRuntimeException(0, 0, $"{name}: {complaint}"));

        Real1("realsqrt", static x => x >= 0, Math.Sqrt, "the argument must not be negative.");
        Real1("reallog", static x => x >= 0, Math.Log, "the argument must not be negative.");
        Math2("realpow", static (x, y) =>
        {
            double result = Math.Pow(x, y);
            return double.IsNaN(result) && !double.IsNaN(x) && !double.IsNaN(y)
                ? throw new JgsRuntimeException(0, 0, "realpow: that power is not a real number.")
                : result;
        });

        Define("complex", (args, line, col) =>
        {
            ArityRange("complex", args, 1, 2, line, col);
            JgsValue imaginary = args.Count == 2 ? args[1] : JgsValue.Number(0);
            return ZipComplex("complex", args[0], imaginary, line, col);
        });
    }

    /// <summary>log(1+x), accurate for tiny x where 1+x has already lost the interesting digits.</summary>
    private static double Log1P(double x)
    {
        double sum = 1.0 + x;
        if (sum == 1.0)
        {
            return x; // x is below the rounding of 1.0, where log(1+x) ≈ x to full precision
        }

        // Kahan's correction: the ratio recovers the digits 1+x rounded away.
        return Math.Log(sum) * (x / (sum - 1.0));
    }

    /// <summary>exp(x)-1, accurate for tiny x where exp(x) rounds to exactly 1.</summary>
    private static double ExpM1(double x)
    {
        double raised = Math.Exp(x);
        if (raised == 1.0)
        {
            return x;
        }

        return raised - 1.0 == -1.0 ? -1.0 : (raised - 1.0) * (x / Math.Log(raised));
    }

    /// <summary>Pairs real and imaginary parts into complex values, broadcasting a scalar across an array.</summary>
    private static JgsValue ZipComplex(string name, JgsValue real, JgsValue imaginary, int line, int col)
    {
        bool realScalar = real.Type is JgsType.Number or JgsType.Bool;
        bool imaginaryScalar = imaginary.Type is JgsType.Number or JgsType.Bool;
        if (realScalar && imaginaryScalar)
        {
            return JgsValue.ComplexNum(new Complex(real.AsNumber, imaginary.AsNumber));
        }

        double[] res = realScalar ? [real.AsNumber] : ToDoubles(name, real, line, col);
        double[] ims = imaginaryScalar ? [imaginary.AsNumber] : ToDoubles(name, imaginary, line, col);
        int length = Math.Max(res.Length, ims.Length);
        if (res.Length != length && res.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} cannot pair {res.Length} real parts with {ims.Length} imaginary parts.");
        }

        if (ims.Length != length && ims.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} cannot pair {res.Length} real parts with {ims.Length} imaginary parts.");
        }

        var result = new JgsValue[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = JgsValue.ComplexNum(new Complex(res[res.Length == 1 ? 0 : i], ims[ims.Length == 1 ? 0 : i]));
        }

        return JgsValue.Array(result);
    }

    // --- Integer arithmetic -----------------------------------------------------------------------

    private static void RegisterIntegerMath(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("gcd", (args, line, col) =>
        {
            Arity("gcd", args, 2, line, col);
            return Zip("gcd", args[0], args[1], (a, b) => Gcd(Whole("gcd", a, line, col), Whole("gcd", b, line, col)), line, col);
        });

        Define("lcm", (args, line, col) =>
        {
            Arity("lcm", args, 2, line, col);
            return Zip("lcm", args[0], args[1], (a, b) =>
            {
                double x = Whole("lcm", a, line, col);
                double y = Whole("lcm", b, line, col);
                double divisor = Gcd(x, y);
                return divisor == 0 ? 0 : Math.Abs(x / divisor * y);
            }, line, col);
        });

        Define("factorial", (args, line, col) =>
        {
            Arity("factorial", args, 1, line, col);
            return MapNumeric("factorial", args[0], x =>
            {
                if (x < 0 || x != Math.Floor(x))
                {
                    throw new JgsRuntimeException(line, col, "factorial expects non-negative whole numbers.");
                }

                double product = 1;
                for (int k = 2; k <= (int)x; k++)
                {
                    product *= k;
                }

                return product;
            }, line, col);
        });

        Define("nchoosek", (args, line, col) =>
        {
            Arity("nchoosek", args, 2, line, col);
            int k = Count("nchoosek", args, 1, line, col);

            // nchoosek(n, k) counts the choices; nchoosek(v, k) lists them, one combination per row.
            if (args[0].Type is JgsType.Number or JgsType.Bool)
            {
                return JgsValue.Number(BinomialCoefficient(args[0].AsNumber, k, line, col));
            }

            double[] set = ToDoubles("nchoosek", args[0], line, col);
            var rows = new List<JgsValue>();
            var chosen = new double[k];
            Choose(set, chosen, 0, 0, rows);
            return JgsValue.Array(rows.ToArray());
        });

        Define("primes", (args, line, col) =>
        {
            Arity("primes", args, 1, line, col);
            int limit = Count("primes", args, 0, line, col);
            if (limit < 2)
            {
                return Numbers([]);
            }

            var composite = new bool[limit + 1];
            var found = new List<double>();
            for (int candidate = 2; candidate <= limit; candidate++)
            {
                if (composite[candidate])
                {
                    continue;
                }

                found.Add(candidate);
                for (long multiple = (long)candidate * candidate; multiple <= limit; multiple += candidate)
                {
                    composite[multiple] = true;
                }
            }

            return Numbers(found.ToArray());
        });

        Define("isprime", (args, line, col) =>
        {
            Arity("isprime", args, 1, line, col);
            return MapToBool("isprime", args[0], IsPrime, line, col);
        });
    }

    /// <summary>Reads a value that must be a whole number, for the integer-only builtins.</summary>
    private static double Whole(string name, double value, int line, int col) =>
        value == Math.Floor(value) && !double.IsInfinity(value) && !double.IsNaN(value)
            ? value
            : throw new JgsRuntimeException(line, col, $"{name} expects whole numbers.");

    private static double Gcd(double a, double b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b > 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    /// <summary>
    /// n-choose-k, multiplied one factor at a time so the running value stays near the answer.
    /// Building it from three factorials would overflow at n = 171 for a result of just 2.
    /// </summary>
    private static double BinomialCoefficient(double n, int k, int line, int col)
    {
        if (k < 0 || n < 0 || n != Math.Floor(n))
        {
            throw new JgsRuntimeException(line, col, "nchoosek expects non-negative whole numbers.");
        }

        if (k > n)
        {
            return 0;
        }

        k = (int)Math.Min(k, n - k);
        double total = 1;
        for (int i = 1; i <= k; i++)
        {
            total = total * (n - k + i) / i;
        }

        return Math.Round(total);
    }

    /// <summary>Appends every k-subset of <paramref name="set"/> to <paramref name="rows"/>, in order.</summary>
    private static void Choose(double[] set, double[] chosen, int start, int depth, List<JgsValue> rows)
    {
        if (depth == chosen.Length)
        {
            rows.Add(NumbersCopy(chosen));
            return;
        }

        for (int i = start; i <= set.Length - (chosen.Length - depth); i++)
        {
            chosen[depth] = set[i];
            Choose(set, chosen, i + 1, depth + 1, rows);
        }
    }

    private static bool IsPrime(double value)
    {
        if (value < 2 || value != Math.Floor(value) || value > 9.007199254740992e15)
        {
            return false;
        }

        long n = (long)value;
        if (n % 2 == 0)
        {
            return n == 2;
        }

        for (long divisor = 3; divisor * divisor <= n; divisor += 2)
        {
            if (n % divisor == 0)
            {
                return false;
            }
        }

        return true;
    }

    // --- Questions with one honest answer ---------------------------------------------------------

    private static void RegisterDenseAnswers(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // Since M42 a real sparse type exists (JgsBuiltins.Sparse.cs); these answer for both storages.
        Define("issparse", (args, line, col) => { Arity("issparse", args, 1, line, col); return JgsValue.Bool(args[0].Type == JgsType.Sparse); });
        Define("full", (args, line, col) =>
        {
            Arity("full", args, 1, line, col);
            if (args[0].Type != JgsType.Sparse)
            {
                return args[0]; // dense stays dense
            }

            JGraph.Numerics.Sparse.CscMatrix sparse = args[0].AsSparse;
            return JgsMatrix.FromColumnMajorDims(sparse.ToColumnMajor(), [sparse.Rows, sparse.Cols]);
        });

        Define("nnz", (args, line, col) =>
        {
            Arity("nnz", args, 1, line, col);
            if (args[0].Type == JgsType.Sparse)
            {
                return JgsValue.Number(args[0].AsSparse.NonZeroCount);
            }

            // A mask is the argument this is nearly always given, and Flatten would copy the whole
            // of it before counting a single element (M92).
            if (TryPackedSpan(args, out NumericBuffer packed))
            {
                return JgsValue.Number(PackedMath.CountNonZero(packed));
            }

            int count = 0;
            foreach (double value in Flatten("nnz", args[0], line, col))
            {
                if (value != 0)
                {
                    count++;
                }
            }

            return JgsValue.Number(count);
        });

        Define("nonzeros", (args, line, col) =>
        {
            Arity("nonzeros", args, 1, line, col);
            var kept = new List<double>();
            foreach (double value in Flatten("nonzeros", args[0], line, col))
            {
                if (value != 0)
                {
                    kept.Add(value);
                }
            }

            return Numbers(kept.ToArray());
        });
    }

    /// <summary>Every number in a value, walking nested rows so a matrix reads as one long sequence.</summary>
    private static IEnumerable<double> Flatten(string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            yield return value.AsNumber;
            yield break;
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
        }

        if (value.IsPacked)
        {
            foreach (double element in value.AsBuffer.AsSpan().ToArray())
            {
                yield return element;
            }

            yield break;
        }

        foreach (JgsValue element in value.AsArray)
        {
            foreach (double inner in Flatten(name, element, line, col))
            {
                yield return inner;
            }
        }
    }
}
