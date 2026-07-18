using System.Numerics.Tensors;

namespace JGraph.Numerics;

/// <summary>
/// Chunked, cancellable elementwise kernels over <see cref="NumericBuffer"/>s. Hardware SIMD comes
/// from <see cref="TensorPrimitives"/>; operations without a vector kernel — or whose vectorized
/// form would change results (<see cref="BinaryOp.Power"/> for negative bases,
/// <see cref="BinaryOp.Remainder"/>) — run as scalar loops so packed math is semantically
/// identical to the boxed interpreter paths, not just close.
/// </summary>
/// <remarks>
/// Every operation processes <see cref="ChunkElements"/> at a time and invokes the caller's
/// <c>betweenChunks</c> callback between chunks, so the script interpreter can poll its
/// cancellation token mid-operation. All public methods end with <see cref="GC.KeepAlive(object)"/>
/// on their buffer arguments, honoring the <see cref="NumericBuffer"/> lifetime contract.
/// </remarks>
public static class PackedMath
{
    /// <summary>Elements per chunk (4M ≈ 32 MB): milliseconds of work between cancellation polls.</summary>
    public const int ChunkElements = 1 << 22;

    /// <summary>Elementwise binary operations.</summary>
    public enum BinaryOp
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
        Power,
    }

    /// <summary>Elementwise unary operations.</summary>
    public enum UnaryOp
    {
        Negate,
        Abs,
        Sqrt,
        Floor,
        Ceiling,
        Round,
        Sin,
        Cos,
        Tan,
        Exp,
        Log,
        Log10,
    }

    /// <summary>Elementwise comparisons, producing 0.0 / 1.0 into the destination.</summary>
    public enum CompareOp
    {
        Less,
        LessEqual,
        Greater,
        GreaterEqual,
        Equal,
        NotEqual,
    }

    /// <summary>dest[i] = a[i] op b[i]. All three buffers must share a length; dest may alias a source.</summary>
    public static void Binary(BinaryOp op, NumericBuffer a, NumericBuffer b, NumericBuffer dest,
                              Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        RequireSameLength(b.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            BinaryChunk(op, a.AsSpan(start, len), b.AsSpan(start, len), dest.AsSpan(start, len));
            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = a[i] op scalar.</summary>
    public static void BinaryScalarRight(BinaryOp op, NumericBuffer a, double scalar, NumericBuffer dest,
                                         Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            Span<double> x = a.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            switch (op)
            {
                case BinaryOp.Add: TensorPrimitives.Add<double>(x, scalar, d); break;
                case BinaryOp.Subtract: TensorPrimitives.Add<double>(x, -scalar, d); break;
                case BinaryOp.Multiply: TensorPrimitives.Multiply<double>(x, scalar, d); break;
                case BinaryOp.Divide: TensorPrimitives.Divide<double>(x, scalar, d); break;
                case BinaryOp.Remainder:
                    for (int i = 0; i < len; i++) { d[i] = x[i] % scalar; }
                    break;
                case BinaryOp.Power:
                    for (int i = 0; i < len; i++) { d[i] = Math.Pow(x[i], scalar); }
                    break;
                default: throw UnknownOp(op);
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = scalar op b[i].</summary>
    public static void BinaryScalarLeft(BinaryOp op, double scalar, NumericBuffer b, NumericBuffer dest,
                                        Action? betweenChunks = null)
    {
        RequireSameLength(b.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            Span<double> y = b.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            switch (op)
            {
                case BinaryOp.Add: TensorPrimitives.Add<double>(y, scalar, d); break;
                case BinaryOp.Subtract:
                    // scalar - y, vectorized in two passes: d = scalar; d -= y.
                    d.Fill(scalar);
                    TensorPrimitives.Subtract<double>(d, y, d);
                    break;
                case BinaryOp.Multiply: TensorPrimitives.Multiply<double>(y, scalar, d); break;
                case BinaryOp.Divide:
                    d.Fill(scalar);
                    TensorPrimitives.Divide<double>(d, y, d);
                    break;
                case BinaryOp.Remainder:
                    for (int i = 0; i < len; i++) { d[i] = scalar % y[i]; }
                    break;
                case BinaryOp.Power:
                    for (int i = 0; i < len; i++) { d[i] = Math.Pow(scalar, y[i]); }
                    break;
                default: throw UnknownOp(op);
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = op(source[i]). dest may alias source.</summary>
    public static void Unary(UnaryOp op, NumericBuffer source, NumericBuffer dest,
                             Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            Span<double> x = source.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            switch (op)
            {
                case UnaryOp.Negate: TensorPrimitives.Negate<double>(x, d); break;
                case UnaryOp.Abs: TensorPrimitives.Abs<double>(x, d); break;
                case UnaryOp.Sqrt: TensorPrimitives.Sqrt<double>(x, d); break;
                case UnaryOp.Floor: TensorPrimitives.Floor<double>(x, d); break;
                case UnaryOp.Ceiling: TensorPrimitives.Ceiling<double>(x, d); break;
                case UnaryOp.Round:
                    // Match Math.Round's banker's rounding exactly; TensorPrimitives.Round agrees,
                    // but the scalar loop keeps midpoint semantics pinned to the boxed path.
                    for (int i = 0; i < len; i++) { d[i] = Math.Round(x[i]); }
                    break;
                case UnaryOp.Sin: TensorPrimitives.Sin<double>(x, d); break;
                case UnaryOp.Cos: TensorPrimitives.Cos<double>(x, d); break;
                case UnaryOp.Tan: TensorPrimitives.Tan<double>(x, d); break;
                case UnaryOp.Exp: TensorPrimitives.Exp<double>(x, d); break;
                case UnaryOp.Log: TensorPrimitives.Log<double>(x, d); break;
                case UnaryOp.Log10: TensorPrimitives.Log10<double>(x, d); break;
                default: throw UnknownOp(op);
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = f(source[i]) — the scalar escape hatch for operations without an enum entry.</summary>
    public static void Map(NumericBuffer source, NumericBuffer dest, Func<double, double> f,
                           Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            Span<double> x = source.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            for (int i = 0; i < len; i++)
            {
                d[i] = f(x[i]);
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = start + i * step (colon-range materialization).</summary>
    public static void Fill(NumericBuffer dest, double start, double step, Action? betweenChunks = null)
    {
        for (int chunk = 0; chunk < dest.Length; chunk += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - chunk);
            Span<double> d = dest.AsSpan(chunk, len);
            for (int i = 0; i < len; i++)
            {
                d[i] = start + (chunk + i) * step;
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = value.</summary>
    public static void FillConstant(NumericBuffer dest, double value, Action? betweenChunks = null)
    {
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            dest.AsSpan(start, len).Fill(value);
            betweenChunks?.Invoke();
        }

        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = a[i] op b[i] ? 1.0 : 0.0.</summary>
    public static void Compare(CompareOp op, NumericBuffer a, NumericBuffer b, NumericBuffer dest,
                               Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        RequireSameLength(b.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            Span<double> x = a.AsSpan(start, len);
            Span<double> y = b.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            for (int i = 0; i < len; i++)
            {
                d[i] = Holds(op, x[i], y[i]) ? 1.0 : 0.0;
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = a[i] op scalar ? 1.0 : 0.0. Pass <paramref name="scalarOnLeft"/> for scalar op a[i].</summary>
    public static void CompareScalar(CompareOp op, NumericBuffer a, double scalar, NumericBuffer dest,
                                     bool scalarOnLeft = false, Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            Span<double> x = a.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            if (scalarOnLeft)
            {
                for (int i = 0; i < len; i++) { d[i] = Holds(op, scalar, x[i]) ? 1.0 : 0.0; }
            }
            else
            {
                for (int i = 0; i < len; i++) { d[i] = Holds(op, x[i], scalar) ? 1.0 : 0.0; }
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        GC.KeepAlive(dest);
    }

    /// <summary>Left-fold sum in index order — bit-identical to the boxed interpreter's accumulation.</summary>
    public static double Sum(NumericBuffer a, Action? betweenChunks = null)
    {
        double total = 0;
        for (int start = 0; start < a.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, a.Length - start);
            Span<double> x = a.AsSpan(start, len);
            for (int i = 0; i < len; i++)
            {
                total += x[i];
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        return total;
    }

    /// <summary>Minimum element (NaN propagates, matching <see cref="Math.Min(double, double)"/> folds).</summary>
    public static double Min(NumericBuffer a, Action? betweenChunks = null) =>
        Reduce(a, TensorPrimitives.Min, Math.Min, betweenChunks);

    /// <summary>Maximum element (NaN propagates, matching <see cref="Math.Max(double, double)"/> folds).</summary>
    public static double Max(NumericBuffer a, Action? betweenChunks = null) =>
        Reduce(a, TensorPrimitives.Max, Math.Max, betweenChunks);

    /// <summary>Dot product, chunked.</summary>
    public static double Dot(NumericBuffer a, NumericBuffer b, Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, b.Length);
        double total = 0;
        for (int start = 0; start < a.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, a.Length - start);
            total += TensorPrimitives.Dot<double>(a.AsSpan(start, len), b.AsSpan(start, len));
            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        return total;
    }

    /// <summary>Whether every element is nonzero (array truthiness). Empty buffers are false.</summary>
    public static bool AllNonZero(NumericBuffer a, Action? betweenChunks = null)
    {
        if (a.Length == 0)
        {
            return false;
        }

        bool all = true;
        for (int start = 0; all && start < a.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, a.Length - start);
            Span<double> x = a.AsSpan(start, len);
            for (int i = 0; i < len; i++)
            {
                if (x[i] == 0)
                {
                    all = false;
                    break;
                }
            }

            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        return all;
    }

    /// <summary>Bulk copy source → dest (same length).</summary>
    public static void Copy(NumericBuffer source, NumericBuffer dest, Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        for (int start = 0; start < dest.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, dest.Length - start);
            source.AsSpan(start, len).CopyTo(dest.AsSpan(start, len));
            betweenChunks?.Invoke();
        }

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = source[picks[i]] (slice/mask read).</summary>
    public static void Gather(NumericBuffer source, ReadOnlySpan<int> picks, NumericBuffer dest)
    {
        RequireSameLength(picks.Length, dest.Length);
        Span<double> s = source.AsSpan();
        Span<double> d = dest.AsSpan();
        for (int i = 0; i < picks.Length; i++)
        {
            d[i] = s[picks[i]];
        }

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[picks[i]] = source[i] (slice write, array right-hand side).</summary>
    public static void Scatter(NumericBuffer dest, ReadOnlySpan<int> picks, NumericBuffer source)
    {
        RequireSameLength(picks.Length, source.Length);
        Span<double> s = source.AsSpan();
        Span<double> d = dest.AsSpan();
        for (int i = 0; i < picks.Length; i++)
        {
            d[picks[i]] = s[i];
        }

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[picks[i]] = value (slice write, scalar right-hand side).</summary>
    public static void ScatterConstant(NumericBuffer dest, ReadOnlySpan<int> picks, double value)
    {
        Span<double> d = dest.AsSpan();
        for (int i = 0; i < picks.Length; i++)
        {
            d[picks[i]] = value;
        }

        GC.KeepAlive(dest);
    }

    private static void BinaryChunk(BinaryOp op, Span<double> x, Span<double> y, Span<double> d)
    {
        switch (op)
        {
            case BinaryOp.Add: TensorPrimitives.Add<double>(x, y, d); break;
            case BinaryOp.Subtract: TensorPrimitives.Subtract<double>(x, y, d); break;
            case BinaryOp.Multiply: TensorPrimitives.Multiply<double>(x, y, d); break;
            case BinaryOp.Divide: TensorPrimitives.Divide<double>(x, y, d); break;
            case BinaryOp.Remainder:
                // C# remainder semantics; no TensorPrimitives kernel.
                for (int i = 0; i < d.Length; i++) { d[i] = x[i] % y[i]; }
                break;
            case BinaryOp.Power:
                // Math.Pow semantics: a vectorized exp/log form would return NaN for negative
                // bases with integral exponents, so this stays scalar deliberately.
                for (int i = 0; i < d.Length; i++) { d[i] = Math.Pow(x[i], y[i]); }
                break;
            default: throw UnknownOp(op);
        }
    }

    private delegate double SpanReduce(ReadOnlySpan<double> span);

    private static double Reduce(NumericBuffer a,
                                 SpanReduce chunkReduce,
                                 Func<double, double, double> combine,
                                 Action? betweenChunks)
    {
        if (a.Length == 0)
        {
            throw new InvalidOperationException("cannot reduce an empty buffer");
        }

        double result = double.NaN;
        bool first = true;
        for (int start = 0; start < a.Length; start += ChunkElements)
        {
            int len = Math.Min(ChunkElements, a.Length - start);
            double chunk = chunkReduce(a.AsSpan(start, len));
            result = first ? chunk : combine(result, chunk);
            first = false;
            betweenChunks?.Invoke();
        }

        GC.KeepAlive(a);
        return result;
    }

    private static bool Holds(CompareOp op, double left, double right) => op switch
    {
        CompareOp.Less => left < right,
        CompareOp.LessEqual => left <= right,
        CompareOp.Greater => left > right,
        CompareOp.GreaterEqual => left >= right,
        CompareOp.Equal => left == right,
        CompareOp.NotEqual => left != right,
        _ => throw UnknownOp(op),
    };

    private static void RequireSameLength(int actual, int expected)
    {
        if (actual != expected)
        {
            throw new ArgumentException($"buffer length mismatch: {actual} vs {expected}");
        }
    }

    private static ArgumentOutOfRangeException UnknownOp(object op) =>
        new(nameof(op), op, "unknown operation");
}
