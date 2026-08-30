using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>decomposition</c> object: a matrix factored once and kept, so that solving with it many
/// times costs one factorization rather than many.
/// </summary>
/// <remarks>
/// <para>
/// A <c>decomposition</c> is a value, not a handle — <c>d2 = dA; d2 = 2*d2;</c> must leave
/// <c>dA</c> alone — so it is carried the way every other object in this build is carried: a struct
/// tagged with its class name, holding the properties MathWorks documents. The factors themselves
/// are held beside it under the matrix's own tag and are shared by every copy, which is safe
/// precisely because a factorization is never modified once taken.
/// </para>
/// <para>
/// What a decomposition remembers besides its factors is a scalar it is multiplied by and whether it
/// has been transposed. Neither refactors: multiplying by three divides the answer by three, and
/// transposing solves with the factors the other way round. That is what makes <c>3*dA'</c> free,
/// and it is why those two are properties rather than a new object.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>What <c>class(dA)</c> reports.</summary>
    internal const string DecompositionClass = "decomposition";

    /// <summary>The factorizations, kept beside the objects that name them.</summary>
    private static readonly Dictionary<long, DecompositionFactors> Factorizations = [];

    private static long _decompositionTags;

    /// <summary>Registers the constructor and the three queries that take a decomposition.</summary>
    internal static void RegisterDecompositionBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        env.Declare(DecompositionClass, JgsValue.Function(
            new BuiltinFunction(DecompositionClass, (args, line, col) => BuildDecomposition(args, line, col))
            {
                AutoCallsBare = true,
            }));

        void Wrap(string name,
            Func<Func<IReadOnlyList<JgsValue>, int, int, JgsValue>, IReadOnlyList<JgsValue>, int, int, JgsValue> over)
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.AsCallable is not { } inner)
            {
                return;
            }

            JgsValue Inner(IReadOnlyList<JgsValue> args, int line, int col) => inner.Call(args, line, col);

            // All five flags carried across, not the one that looked relevant: a wrapper that drops
            // any of them silently changes how the name behaves as a bare statement or in a
            // multi-output list, which is a lesson this repository has now learnt seven times.
            BuiltinFunction? original = inner as BuiltinFunction;
            var wrapper = new BuiltinFunction(name, (args, line, col) => over(Inner, args, line, col))
            {
                BindsAnsAsStatement = original?.BindsAnsAsStatement ?? true,
                AutoCallsBare = original?.AutoCallsBare ?? false,
                KnowsWhenDiscarded = original?.KnowsWhenDiscarded ?? false,
                KeepsStringArguments = original?.KeepsStringArguments ?? false,
                MultiOutput = original?.MultiOutput,
            };

            env.Declare(name, JgsValue.Function(wrapper));
        }

        Wrap("rank", (inner, args, line, col) =>
        {
            if (args.Count == 0 || !IsDecomposition(args[0]))
            {
                return inner(args, line, col);
            }

            DecompositionFactors factors = FactorsOf(args[0], line, col);
            if (factors.Type is not ("qr" or "cod"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:decomposition:RankNotSupported",
                    "Only supported for decompositions of type 'qr' or 'cod'.");
            }

            return JgsValue.Number(factors.Rank);
        });

        Wrap("rcond", (inner, args, line, col) =>
        {
            if (args.Count == 0 || !IsDecomposition(args[0]))
            {
                return inner(args, line, col);
            }

            DecompositionFactors factors = FactorsOf(args[0], line, col);
            if (factors.Type is "qr" or "cod")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:decomposition:RcondNotSupported",
                    "Decompositions of type 'qr' or 'cod' are not supported.");
            }

            return JgsValue.Number(factors.ReciprocalCondition);
        });

        env.Declare("isIllConditioned", JgsValue.Function(new BuiltinFunction("isIllConditioned",
            (args, line, col) =>
            {
                ArityRange("isIllConditioned", args, 1, 1, line, col);
                DecompositionFactors factors = FactorsOf(args[0], line, col);
                return JgsValue.Bool(factors.Type is "qr" or "cod"
                    ? factors.Rank < Math.Min(factors.Rows, factors.Columns)
                    : factors.ReciprocalCondition < DoubleSpacing);
            })));

        _ = host;
    }

    /// <summary>Whether a value is a decomposition object.</summary>
    internal static bool IsDecomposition(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName == DecompositionClass;

    /// <summary>
    /// The binary operators a decomposition answers to. Answers false when neither operand is one,
    /// which is what lets the ordinary numeric path stay untouched.
    /// </summary>
    internal static bool TryDecompositionBinary(
        string op, JgsValue left, JgsValue right, int line, int col, out JgsValue result)
    {
        result = JgsValue.Null;
        bool onLeft = IsDecomposition(left);
        bool onRight = IsDecomposition(right);
        if (!onLeft && !onRight)
        {
            return false;
        }

        switch (op)
        {
            case "mldivide" when onLeft:
                result = DecompositionSolve(left, right, fromRight: false, line, col);
                return true;
            case "mrdivide" when onRight:
                result = DecompositionSolve(right, left, fromRight: true, line, col);
                return true;
            case "mtimes" or "times":
                (JgsValue subject, JgsValue scalar) = onLeft ? (left, right) : (right, left);
                result = Rescaled(subject, ScalarOf(scalar, "InvalidMult",
                    "Decomposition objects can be multiplied only by a scalar number of type double or single.",
                    line, col), line, col);
                return true;
            case "mrdivide" or "rdivide" when onLeft:
                result = Rescaled(left, Complex.One / ScalarOf(right, "InvalidDivisor",
                    "Decomposition objects can be divided only by a scalar number of type double or single.",
                    line, col), line, col);
                return true;
            default:
                throw new JgsRuntimeException(line, col,
                    $"'{op}' is not defined for a decomposition and a {(onLeft ? right : left).TypeName}.");
        }
    }

    /// <summary>The unary operators: negation, the identity, and the conjugate transpose.</summary>
    internal static bool TryDecompositionUnary(string op, JgsValue operand, int line, int col, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!IsDecomposition(operand))
        {
            return false;
        }

        switch (op)
        {
            case "uminus":
                result = Rescaled(operand, new Complex(-1.0, 0.0), line, col);
                return true;
            case "uplus":
                result = operand;
                return true;
            case "ctranspose":
                Dictionary<string, JgsValue> fields = new(operand.AsStruct, StringComparer.Ordinal);
                bool already = fields["IsConjugateTransposed"].AsNumber != 0;
                fields["IsConjugateTransposed"] = JgsValue.Bool(!already);
                fields["ScaleFactor"] = Conjugated(fields["ScaleFactor"]);
                result = Tagged(fields);
                return true;
            case "transpose":
                throw new JgsRuntimeException(line, col, "MATLAB:decomposition:TransposeNotSupported",
                    "Transpose operator .' not supported for decomposition. Use the complex conjugate "
                    + "transpose operator ' instead.");
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>dA = decomposition(A)</c>, <c>decomposition(A, type)</c> and the <c>'CheckCondition'</c>
    /// name-value pair.
    /// </summary>
    private static JgsValue BuildDecomposition(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        Complex[,] a = MatBlock("decomposition", args[0], line, col);
        string type = "auto";
        bool check = false;
        int at = 1;
        if (at < args.Count && IsTextScalar(args[at]) && !string.Equals(
            args[at].AsString, "CheckCondition", StringComparison.OrdinalIgnoreCase))
        {
            type = Matched(args[at].AsString, line, col);
            at++;
        }

        while (at < args.Count)
        {
            string name = Str("decomposition", args, at, line, col);
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:decomposition:NameWithoutValue",
                    "Incorrect number of input arguments. Each parameter name must be followed by a "
                    + "corresponding value.");
            }

            if (string.Equals(name, "CheckCondition", StringComparison.OrdinalIgnoreCase))
            {
                check = args[at + 1].AsNumber != 0;
            }
            else if (name is not ("BandDensity" or "LUPivotTolerance" or "LDLPivotTolerance" or "RankTolerance"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:unrecognizedStringChoice",
                    "Expected input to match one of these values:\n\n'auto', 'qr', 'lu', 'chol', 'ldl', "
                    + "'triangular', 'diagonal', 'permutedTriangular', 'hessenberg', 'banded', 'cod', "
                    + "'CheckCondition', 'BandDensity', 'LUPivotTolerance', 'LDLPivotTolerance', "
                    + $"'RankTolerance'\n\nThe input, '{name}', did not match any of the valid values.");
            }

            at += 2;
        }

        DecompositionFactors factors = DecompositionFactors.Take(a, type, line, col);
        long tag = ++_decompositionTags;

        // The factors are kept beside the object rather than inside it. The store is cleared rather
        // than pruned once it grows past what any script plausibly holds at once: a decomposition
        // whose factors are gone still knows its matrix and takes them again.
        if (Factorizations.Count > 256)
        {
            Factorizations.Clear();
        }

        Factorizations[tag] = factors;

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = JgsValue.Str(factors.Type),
            ["ScaleFactor"] = JgsValue.Number(1.0),
            ["IsSparse"] = JgsValue.Bool(false),
            ["IsReal"] = JgsValue.Bool(!IsComplexBlock(a)),
            ["MatrixSize"] = JgsMatrix.FromColumnMajorDims(
                [a.GetLength(0), a.GetLength(1)], [1, 2]),
            ["IsConjugateTransposed"] = JgsValue.Bool(false),
            ["Datatype"] = JgsValue.Str("double"),
            ["CheckCondition"] = JgsValue.Bool(check),
            ["Underlying"] = MatValue(a),
            ["Tag"] = JgsValue.Number(tag),
        };

        return Tagged(fields);
    }

    /// <summary>The type word, matched against the eleven MathWorks documents.</summary>
    private static string Matched(string word, int line, int col)
    {
        string[] known =
        [
            "auto", "qr", "lu", "chol", "ldl", "triangular", "diagonal",
            "permutedTriangular", "hessenberg", "banded", "cod",
        ];

        foreach (string name in known)
        {
            if (string.Equals(name, word, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        throw new JgsRuntimeException(line, col, "MATLAB:unrecognizedStringChoice",
            "Expected input to match one of these values:\n\n'auto', 'qr', 'lu', 'chol', 'ldl', "
            + "'triangular', 'diagonal', 'permutedTriangular', 'hessenberg', 'banded', 'cod', "
            + "'CheckCondition', 'BandDensity', 'LUPivotTolerance', 'LDLPivotTolerance', "
            + $"'RankTolerance'\n\nThe input, '{word}', did not match any of the valid values.");
    }

    /// <summary>A struct wearing the class's name.</summary>
    private static JgsValue Tagged(Dictionary<string, JgsValue> fields)
    {
        JgsValue value = JgsValue.Struct(fields);
        value.SetClassName(DecompositionClass);
        return value;
    }

    /// <summary>The same object with its scalar multiplier changed.</summary>
    private static JgsValue Rescaled(JgsValue subject, Complex by, int line, int col)
    {
        _ = line;
        _ = col;
        Dictionary<string, JgsValue> fields = new(subject.AsStruct, StringComparer.Ordinal);
        JgsValue current = fields["ScaleFactor"];
        Complex now = current.Type == JgsType.Complex ? current.AsComplex : new Complex(current.AsNumber, 0);
        Complex scaled = now * by;
        fields["ScaleFactor"] = scaled.Imaginary == 0
            ? JgsValue.Number(scaled.Real)
            : JgsValue.ComplexNum(scaled);
        return Tagged(fields);
    }

    private static JgsValue Conjugated(JgsValue value) =>
        value.Type == JgsType.Complex
            ? JgsValue.ComplexNum(Complex.Conjugate(value.AsComplex))
            : value;

    /// <summary>The scalar an object may be multiplied or divided by.</summary>
    private static Complex ScalarOf(JgsValue value, string key, string message, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return new Complex(value.AsNumber, 0.0);
        }

        if (value.Type == JgsType.Complex)
        {
            return value.AsComplex;
        }

        throw new JgsRuntimeException(line, col, $"MATLAB:decomposition:{key}", message);
    }

    /// <summary>The factors an object names, taken again if the store no longer holds them.</summary>
    private static DecompositionFactors FactorsOf(JgsValue value, int line, int col)
    {
        if (!IsDecomposition(value))
        {
            throw new JgsRuntimeException(line, col,
                $"isIllConditioned expects a decomposition, but got a {value.TypeName}.");
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        long tag = (long)fields["Tag"].AsNumber;
        if (Factorizations.TryGetValue(tag, out DecompositionFactors? held) && held is not null)
        {
            return held;
        }

        DecompositionFactors again = DecompositionFactors.Take(
            MatBlock("decomposition", fields["Underlying"], line, col),
            fields["Type"].AsString, line, col);
        Factorizations[tag] = again;
        return again;
    }

    /// <summary>
    /// <c>dA\b</c> and <c>b/dA</c>: the solve, with the multiplier divided out and the transposition
    /// applied to the factors rather than to the matrix.
    /// </summary>
    private static JgsValue DecompositionSolve(
        JgsValue subject, JgsValue other, bool fromRight, int line, int col)
    {
        Dictionary<string, JgsValue> fields = subject.AsStruct;
        DecompositionFactors factors = FactorsOf(subject, line, col);
        bool transposed = fields["IsConjugateTransposed"].AsNumber != 0;
        JgsValue scale = fields["ScaleFactor"];
        Complex multiplier = scale.Type == JgsType.Complex
            ? scale.AsComplex
            : new Complex(scale.AsNumber, 0.0);

        if (factors.Type == "qr" && transposed != fromRight)
        {
            throw fromRight
                ? new JgsRuntimeException(line, col, "MATLAB:decomposition:QRmrdivideTransp",
                    "Forward slash supported only for transposed decomposition for decomposition type "
                    + "'qr'. Use type 'cod', or construct decomposition of transposed matrix.")
                : new JgsRuntimeException(line, col, "MATLAB:decomposition:QRmldivideTransp",
                    "Backslash with transposed decomposition not supported for decomposition type 'qr'. "
                    + "Use type 'cod', or construct decomposition of transposed matrix.");
        }

        Complex[,] b = MatBlock("decomposition", other, line, col);

        // A division from the right is a division from the left of the transposed problem, so the
        // whole thing is turned over here and turned back at the end.
        if (fromRight)
        {
            b = ConjugateTransposeBlock(b);
            transposed = !transposed;
            multiplier = Complex.Conjugate(multiplier);
        }

        int wanted = transposed ? factors.Columns : factors.Rows;
        if (b.GetLength(0) != wanted)
        {
            throw new JgsRuntimeException(line, col,
                fromRight ? "MATLAB:decomposition:mrdivide" : "MATLAB:decomposition:mldivide",
                "Matrix dimensions must agree.");
        }

        if (multiplier != Complex.One)
        {
            var scaled = new Complex[b.GetLength(0), b.GetLength(1)];
            for (int r = 0; r < b.GetLength(0); r++)
            {
                for (int c = 0; c < b.GetLength(1); c++)
                {
                    scaled[r, c] = b[r, c] / multiplier;
                }
            }

            b = scaled;
        }

        Complex[,] x = factors.Solve(b, transposed);
        return MatValue(fromRight ? ConjugateTransposeBlock(x) : x);
    }

    /// <summary>The conjugate transpose of a block.</summary>
    private static Complex[,] ConjugateTransposeBlock(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var t = new Complex[cols, rows];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                t[c, r] = Complex.Conjugate(a[r, c]);
            }
        }

        return t;
    }
}
