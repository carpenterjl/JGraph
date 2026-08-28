using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace JGraph.Numerics;

/// <summary>
/// Chunked, cancellable elementwise kernels over <see cref="NumericBuffer"/>s. Hardware SIMD comes
/// from <see cref="TensorPrimitives"/>; operations without a vector kernel — or whose vectorized
/// form would change results (<see cref="BinaryOp.Power"/> for negative bases,
/// <see cref="BinaryOp.Remainder"/>) — run as scalar loops so packed math is semantically
/// identical to the boxed interpreter paths, not just close.
/// </summary>
/// <remarks>
/// <para>
/// Every operation sweeps its buffers in fixed grains through <see cref="ParallelKernels.For"/> and
/// invokes the caller's <c>betweenChunks</c> callback between grains, so the script interpreter can
/// poll its cancellation token mid-operation. All public methods end with
/// <see cref="GC.KeepAlive(object)"/> on their buffer arguments, honoring the
/// <see cref="NumericBuffer"/> lifetime contract.
/// </para>
/// <para>
/// A large enough sweep runs on several threads (M93). Every operation swept that way is
/// per-element independent, so which thread wrote which element is not something an answer can
/// depend on; the reductions — <see cref="Sum"/> above all — stay on the calling thread precisely
/// because their answers <em>would</em> depend on how the work was cut.
/// </para>
/// </remarks>
public static class PackedMath
{
    /// <summary>
    /// Elements per chunk (4M ≈ 32 MB). The elementwise sweeps are now cut into the far smaller
    /// <see cref="ParallelKernels.GrainElements"/>, so their cancellation poll runs oftener than it
    /// used to; this is still the unit the serial reductions walk.
    /// </summary>
    public const int ChunkElements = 1 << 22;

    /// <summary>
    /// Elements per tile when an operation reads its input twice (8K ≈ 64 KB): small enough that the
    /// second read comes from cache rather than from memory.
    /// </summary>
    private const int DomainTileElements = 1 << 13;

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

    /// <summary>
    /// Whether a vector kernel's answers are the scalar loop's answers, bit for bit.
    /// </summary>
    /// <remarks>
    /// <see cref="Determinism.Exact"/> means the vector form is provably the same arithmetic: the
    /// IEEE operations (negate, abs, sqrt, floor, ceil) compute one correctly-rounded result per
    /// element however many of them a register holds, so wiring them changes nothing a script can
    /// read. <see cref="Determinism.Approximate"/> means the vector form is a different polynomial
    /// from the scalar one — <see cref="TensorPrimitives"/>' transcendentals land within a few ulps
    /// of <see cref="Math"/>'s, not on them — so a packed array and a boxed one would print
    /// differently at full precision. Those wait behind <see cref="ApproximateThreshold"/>.
    /// </remarks>
    public enum Determinism
    {
        /// <summary>The vector kernel and the scalar loop agree bit for bit.</summary>
        Exact,

        /// <summary>The vector kernel is within a few ulps of the scalar loop, not on it.</summary>
        Approximate,
    }

    /// <summary>The determinism tier of <paramref name="op"/>'s vector kernel.</summary>
    public static Determinism DeterminismOf(UnaryOp op) => op switch
    {
        UnaryOp.Negate or UnaryOp.Abs or UnaryOp.Sqrt or UnaryOp.Floor or UnaryOp.Ceiling
            or UnaryOp.Round => Determinism.Exact,
        _ => Determinism.Approximate,
    };

    /// <summary>
    /// The element count at or above which an <see cref="Determinism.Approximate"/> kernel is worth
    /// the last few ulps: 32K by default, <see cref="int.MaxValue"/> — never — when
    /// <c>JGRAPH_FAST_MATH=0</c>.
    /// </summary>
    /// <remarks>
    /// Two things meet at this number. Below it live every printed array, every parity-corpus
    /// script and every hand-checked expected value, all of which must keep answering what
    /// <see cref="Math"/> answers to the last bit. Above it live the arrays whose transcendentals
    /// are the cost of the statement, where five times the speed is worth landing within a couple of
    /// ulps of a function that was never exact anyway. ADR 0093 is where the trade is argued;
    /// <c>JGRAPH_FAST_MATH=0</c> is how a caller who wants none of it says so.
    /// </remarks>
    public static int ApproximateThreshold { get; set; } = ResolveApproximateThreshold();

    /// <summary>The default <see cref="ApproximateThreshold"/> when the environment does not object.</summary>
    public const int DefaultApproximateThreshold = 1 << 15;

    /// <summary>Whether <paramref name="length"/> elements of <paramref name="op"/> take the vector kernel.</summary>
    public static bool Vectorizes(UnaryOp op, long length) =>
        DeterminismOf(op) == Determinism.Exact || length >= ApproximateThreshold;

    /// <summary>dest[i] = a[i] op b[i]. All three buffers must share a length; dest may alias a source.</summary>
    public static void Binary(BinaryOp op, NumericBuffer a, NumericBuffer b, NumericBuffer dest,
                              Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        RequireSameLength(b.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
            BinaryChunk(op, a.AsSpan(start, len), b.AsSpan(start, len), dest.AsSpan(start, len)));

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = a[i] op scalar.</summary>
    public static void BinaryScalarRight(BinaryOp op, NumericBuffer a, double scalar, NumericBuffer dest,
                                         Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
            BinaryScalarRightChunk(op, a.AsSpan(start, len), scalar, dest.AsSpan(start, len)));

        GC.KeepAlive(a);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = scalar op b[i].</summary>
    public static void BinaryScalarLeft(BinaryOp op, double scalar, NumericBuffer b, NumericBuffer dest,
                                        Action? betweenChunks = null)
    {
        RequireSameLength(b.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
            BinaryScalarLeftChunk(op, scalar, b.AsSpan(start, len), dest.AsSpan(start, len)));

        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = op(source[i]). dest may alias source.</summary>
    public static void Unary(UnaryOp op, NumericBuffer source, NumericBuffer dest,
                             Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
            UnaryChunk(op, source.AsSpan(start, len), dest.AsSpan(start, len)));

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// dest[i] = op(source[i]) through <see cref="Math"/>'s own scalar functions — the same
    /// arithmetic <see cref="Map"/> would run, without a delegate call per element.
    /// </summary>
    /// <remarks>
    /// This is what an <see cref="Determinism.Approximate"/> operation runs below
    /// <see cref="ApproximateThreshold"/>: identical to the boxed interpreter's answers by
    /// construction, because it calls the very functions the boxed interpreter calls. The switch is
    /// outside the loop, so the call it leaves behind is a direct one.
    /// </remarks>
    public static void UnaryScalar(UnaryOp op, NumericBuffer source, NumericBuffer dest,
                                   Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
            UnaryScalarChunk(op, source.AsSpan(start, len), dest.AsSpan(start, len)));

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// dest[i] = op(source[i]) through whichever kernel this operation's determinism tier allows at
    /// this length: the vector one when it is exact or the array is large enough to be worth a few
    /// ulps, the scalar one otherwise. The caller does not have to know which tier an operation is in.
    /// </summary>
    public static void UnaryTiered(UnaryOp op, NumericBuffer source, NumericBuffer dest,
                                   Action? betweenChunks = null)
    {
        if (Vectorizes(op, source.Length))
        {
            Unary(op, source, dest, betweenChunks);
        }
        else
        {
            UnaryScalar(op, source, dest, betweenChunks);
        }
    }

    /// <summary>
    /// <see cref="UnaryTiered"/>, but only where every element is at least
    /// <paramref name="lowerBound"/> — the domain check a function with a half-line for a real
    /// domain needs, in the same pass over storage as the arithmetic rather than a pass of its own.
    /// NaN is admitted: it is at no distance from the bound and answers NaN either way.
    /// </summary>
    /// <remarks>
    /// Answers false the moment a tile falls outside, having already written the tiles before it —
    /// so <paramref name="dest"/> must be a buffer the caller is free to throw away, which is what a
    /// caller about to take a promoting road has. The tile is sized to stay in cache between the
    /// check and the arithmetic, so the second read of it is not a second read of memory.
    /// </remarks>
    public static bool TryUnaryAtLeast(UnaryOp op, double lowerBound, NumericBuffer source,
                                       NumericBuffer dest, Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        bool vector = Vectorizes(op, source.Length);

        // One grain leaving the domain does not stop the others: they are writing a buffer the
        // caller is about to throw away, so the only thing that matters is that the answer comes
        // back false, and a flag any grain may set says so without the grains having to agree on
        // an order.
        bool outside = false;
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
        {
            if (Volatile.Read(ref outside))
            {
                return; // a grain before this one already answered the question
            }

            Span<double> x = source.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            for (int at = 0; at < len; at += DomainTileElements)
            {
                int tile = Math.Min(DomainTileElements, len - at);
                Span<double> xt = x.Slice(at, tile);
                if (!NoneBelow(xt, lowerBound))
                {
                    Volatile.Write(ref outside, true);
                    return;
                }

                Span<double> dt = d.Slice(at, tile);
                if (vector)
                {
                    UnaryChunk(op, xt, dt);
                }
                else
                {
                    UnaryScalarChunk(op, xt, dt);
                }
            }
        });

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
        return !Volatile.Read(ref outside);
    }

    /// <summary>
    /// What a numeric class does to an element the moment it is computed: nothing, a rounding to
    /// <see cref="float"/> precision, or a round-half-away-from-zero into an integer range with
    /// everything outside it saturated and NaN read as zero.
    /// </summary>
    /// <remarks>
    /// This is MATLAB's integer classes turned back into arithmetic. Storage stays double whatever
    /// the class says, so an <c>int32</c> array is doubles that have been rounded and clamped and
    /// have to stay that way; every kernel that writes one owes it the same treatment. Carrying the
    /// rule as a value is what lets the kernel that computed an element also finish it, instead of
    /// handing the whole array to a second sweep that reads it back out of memory.
    /// </remarks>
    public readonly struct Rounding
    {
        private readonly double _min;
        private readonly double _max;
        private readonly Kind _kind;

        private Rounding(Kind kind, double min, double max)
        {
            _kind = kind;
            _min = min;
            _max = max;
        }

        private enum Kind : byte
        {
            None,
            Single,
            Integer,
        }

        /// <summary>Leave the element as it was computed — what <c>double</c> asks for.</summary>
        public static Rounding None => default;

        /// <summary>Round to <see cref="float"/> precision and back, the storage staying double.</summary>
        public static Rounding ToSingle => new(Kind.Single, 0, 0);

        /// <summary>Round half away from zero, saturate into [min, max], read NaN as zero.</summary>
        public static Rounding Between(double min, double max) => new(Kind.Integer, min, max);

        /// <summary>Whether this rule can move an element at all.</summary>
        public bool Moves => _kind != Kind.None;

        /// <summary>
        /// One element through the rule, spelled the way the interpreter's own conversion spells it.
        /// The span kernels below owe their answers to this, element for element.
        /// </summary>
        public double Apply(double x) => _kind switch
        {
            Kind.Single => (float)x,
            Kind.Integer => double.IsNaN(x)
                ? 0
                : Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), _min, _max),
            _ => x,
        };

        /// <summary>The rule over a span, in place — what a kernel that just wrote the span wants.</summary>
        internal void Apply(Span<double> d) => Apply(d, d);

        /// <summary>
        /// The rule from one span into another, which may be the same span. Reading and writing in
        /// the one pass matters: a copy followed by a rounding is two passes over the destination
        /// where the delegate this replaced only ever made one, and on the cheapest rule — the cast
        /// to <see cref="float"/> — that second pass costs more than the delegate did.
        /// </summary>
        internal void Apply(ReadOnlySpan<double> x, Span<double> d)
        {
            switch (_kind)
            {
                case Kind.Single:
                    ToSingleChunk(x, d);
                    break;

                case Kind.Integer:
                    RoundClampChunk(x, d, _min, _max);
                    break;

                default:
                    if (x != d)
                    {
                        x.CopyTo(d);
                    }

                    break;
            }
        }
    }

    /// <summary>dest[i] = the rule's answer for source[i]; dest may be source.</summary>
    public static void Round(NumericBuffer source, NumericBuffer dest, Rounding into,
                             Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks, (start, len) =>
            into.Apply(source.AsSpan(start, len), dest.AsSpan(start, len)));

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// dest[i] = the rule's answer for (a[i] op b[i]) — the arithmetic and the class in one sweep,
    /// so the rounding reads each element out of cache instead of out of memory.
    /// </summary>
    /// <remarks>
    /// The arithmetic is the kernel it would have been on its own and the rounding is the kernel it
    /// would have been on its own; the only new thing is the tile they share. That is what makes a
    /// fused answer the unfused answer bit for bit, and it is why these stay two loops over a tile
    /// rather than becoming one hand-written expression per operator.
    ///
    /// A rule that moves nothing takes the untiled arm instead, so a <c>double</c> — which is every
    /// operation in most scripts — pays nothing for the fact that this road can carry a class. The
    /// tiling is not free: it cuts each grain into eight, and the per-call overhead of a
    /// <see cref="TensorPrimitives"/> kernel is then paid eight times over.
    /// </remarks>
    public static void Binary(BinaryOp op, NumericBuffer a, NumericBuffer b, NumericBuffer dest,
                              Rounding into, Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        RequireSameLength(b.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
        {
            Span<double> x = a.AsSpan(start, len);
            Span<double> y = b.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            if (!into.Moves)
            {
                BinaryChunk(op, x, y, d);
                return;
            }

            for (int at = 0; at < len; at += DomainTileElements)
            {
                int tile = Math.Min(DomainTileElements, len - at);
                Span<double> dt = d.Slice(at, tile);
                BinaryChunk(op, x.Slice(at, tile), y.Slice(at, tile), dt);
                into.Apply(dt);
            }
        });

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = the rule's answer for (a[i] op scalar) — the fused form of <see cref="Binary(BinaryOp, NumericBuffer, NumericBuffer, NumericBuffer, Rounding, Action)"/>.</summary>
    public static void BinaryScalarRight(BinaryOp op, NumericBuffer a, double scalar, NumericBuffer dest,
                                         Rounding into, Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
        {
            Span<double> x = a.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            if (!into.Moves)
            {
                BinaryScalarRightChunk(op, x, scalar, d);
                return;
            }

            for (int at = 0; at < len; at += DomainTileElements)
            {
                int tile = Math.Min(DomainTileElements, len - at);
                Span<double> dt = d.Slice(at, tile);
                BinaryScalarRightChunk(op, x.Slice(at, tile), scalar, dt);
                into.Apply(dt);
            }
        });

        GC.KeepAlive(a);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = the rule's answer for (scalar op b[i]).</summary>
    public static void BinaryScalarLeft(BinaryOp op, double scalar, NumericBuffer b, NumericBuffer dest,
                                        Rounding into, Action? betweenChunks = null)
    {
        RequireSameLength(b.Length, dest.Length);
        ParallelKernels.For(dest.Length, ThresholdOf(op), betweenChunks, (start, len) =>
        {
            Span<double> y = b.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            if (!into.Moves)
            {
                BinaryScalarLeftChunk(op, scalar, y, d);
                return;
            }

            for (int at = 0; at < len; at += DomainTileElements)
            {
                int tile = Math.Min(DomainTileElements, len - at);
                Span<double> dt = d.Slice(at, tile);
                BinaryScalarLeftChunk(op, scalar, y.Slice(at, tile), dt);
                into.Apply(dt);
            }
        });

        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// Rounds every element half away from zero, saturates it into [min, max] and reads NaN as
    /// zero — MATLAB's whole integer-class conversion, in place over one span.
    /// </summary>
    /// <remarks>
    /// The fraction is compared against a half rather than added to the element, which is what keeps
    /// the value just below a half from being carried over it: in doubles
    /// <c>0.49999999999999994 + 0.5</c> is exactly 1, where the comparison says what it should.
    /// Above 2^52 a double has no fraction left, so it subtracts from its own truncation to zero and
    /// is already its own answer; an infinity subtracts to NaN, whose comparison is false, and
    /// saturates at the clamp instead. The step away from zero is selected rather than added,
    /// because a negative element with no fraction keeps its own signed zero and adding a positive
    /// zero to it would not.
    /// </remarks>
    private static void RoundClampChunk(ReadOnlySpan<double> source, Span<double> d, double min, double max)
    {
        int i = 0;
        int width = Vector<double>.Count;
        if (Vector.IsHardwareAccelerated && d.Length >= width)
        {
            ref double sr = ref MemoryMarshal.GetReference(source);
            ref double dr = ref MemoryMarshal.GetReference(d);
            var half = new Vector<double>(0.5);
            var one = new Vector<double>(1.0);
            var low = new Vector<double>(min);
            var high = new Vector<double>(max);
            Vector<double> zero = Vector<double>.Zero;
            for (; i <= d.Length - width; i += width)
            {
                Vector<double> x = Vector.LoadUnsafe(ref sr, (nuint)i);
                x = Vector.ConditionalSelect(Vector.Equals(x, x), x, zero);
                Vector<long> negative = Vector.LessThan(x, zero);
                Vector<double> whole = Vector.ConditionalSelect(negative, Vector.Ceiling(x), Vector.Floor(x));
                Vector<long> carries = Vector.GreaterThanOrEqual(Vector.Abs(x - whole), half);
                Vector<double> away = whole + Vector.ConditionalSelect(negative, -one, one);
                Vector<double> r = Vector.ConditionalSelect(carries, away, whole);
                r = Vector.ConditionalSelect(Vector.LessThan(r, low), low, r);
                r = Vector.ConditionalSelect(Vector.GreaterThan(r, high), high, r);
                r.StoreUnsafe(ref dr, (nuint)i);
            }
        }

        for (; i < d.Length; i++)
        {
            double x = source[i];
            d[i] = double.IsNaN(x) ? 0 : Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), min, max);
        }
    }

    /// <summary>Rounds every element to <see cref="float"/> precision and back, in place.</summary>
    /// <remarks>
    /// Two double registers narrow into one float register and widen back, which is the same pair of
    /// conversion instructions a cast writes one element at a time, under the same rounding mode.
    /// </remarks>
    private static void ToSingleChunk(ReadOnlySpan<double> source, Span<double> d)
    {
        int i = 0;
        int pair = Vector<double>.Count * 2;
        if (Vector.IsHardwareAccelerated && d.Length >= pair)
        {
            ref double sr = ref MemoryMarshal.GetReference(source);
            ref double dr = ref MemoryMarshal.GetReference(d);
            for (; i <= d.Length - pair; i += pair)
            {
                Vector<float> narrowed = Vector.Narrow(
                    Vector.LoadUnsafe(ref sr, (nuint)i),
                    Vector.LoadUnsafe(ref sr, (nuint)(i + Vector<double>.Count)));
                Vector.Widen(narrowed, out Vector<double> low, out Vector<double> high);
                low.StoreUnsafe(ref dr, (nuint)i);
                high.StoreUnsafe(ref dr, (nuint)(i + Vector<double>.Count));
            }
        }

        for (; i < d.Length; i++)
        {
            d[i] = (float)source[i];
        }
    }

    /// <summary>dest[i] = f(source[i]) — the scalar escape hatch for operations without an enum entry.</summary>
    /// <remarks>
    /// <paramref name="f"/> is called from several threads at once on a large enough buffer, so it
    /// must be a function of its argument and nothing else. Every delegate that reaches here is one
    /// of <see cref="Math"/>'s or a numeric-class conversion; a caller with state to keep wants a
    /// loop of its own, not this.
    /// </remarks>
    public static void Map(NumericBuffer source, NumericBuffer dest, Func<double, double> f,
                           Action? betweenChunks = null)
    {
        RequireSameLength(source.Length, dest.Length);
        ParallelKernels.For(dest.Length, ParallelKernels.ComputeBoundThreshold, betweenChunks, (start, len) =>
        {
            Span<double> x = source.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            for (int i = 0; i < len; i++)
            {
                d[i] = f(x[i]);
            }
        });

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = f(a[i], b[i]) — <see cref="Map"/>'s two-operand form (atan2, hypot, mod).</summary>
    public static void Zip(NumericBuffer a, NumericBuffer b, NumericBuffer dest,
                           Func<double, double, double> f, Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        RequireSameLength(b.Length, dest.Length);
        ParallelKernels.For(dest.Length, ParallelKernels.ComputeBoundThreshold, betweenChunks, (start, len) =>
        {
            Span<double> x = a.AsSpan(start, len);
            Span<double> y = b.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            for (int i = 0; i < len; i++)
            {
                d[i] = f(x[i], y[i]);
            }
        });

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// dest[i] = f(a[i], scalar), or f(scalar, a[i]) when <paramref name="scalarOnLeft"/> — the
    /// mixed-arity arm of <see cref="Zip"/>.
    /// </summary>
    public static void ZipScalar(NumericBuffer a, double scalar, NumericBuffer dest,
                                 Func<double, double, double> f, bool scalarOnLeft = false,
                                 Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        ParallelKernels.For(dest.Length, ParallelKernels.ComputeBoundThreshold, betweenChunks, (start, len) =>
        {
            Span<double> x = a.AsSpan(start, len);
            Span<double> d = dest.AsSpan(start, len);
            if (scalarOnLeft)
            {
                for (int i = 0; i < len; i++) { d[i] = f(scalar, x[i]); }
            }
            else
            {
                for (int i = 0; i < len; i++) { d[i] = f(x[i], scalar); }
            }
        });

        GC.KeepAlive(a);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = start + i * step (colon-range materialization).</summary>
    public static void Fill(NumericBuffer dest, double start, double step, Action? betweenChunks = null)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks, (at, len) =>
        {
            Span<double> d = dest.AsSpan(at, len);
            for (int i = 0; i < len; i++)
            {
                d[i] = start + (at + i) * step;
            }
        });

        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = value.</summary>
    public static void FillConstant(NumericBuffer dest, double value, Action? betweenChunks = null)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks,
            (start, len) => dest.AsSpan(start, len).Fill(value));

        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = a[i] op b[i] ? 1.0 : 0.0.</summary>
    public static void Compare(CompareOp op, NumericBuffer a, NumericBuffer b, NumericBuffer dest,
                               Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);
        RequireSameLength(b.Length, dest.Length);
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks, (start, len) =>
            CompareChunk(op, a.AsSpan(start, len), b.AsSpan(start, len), dest.AsSpan(start, len)));

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        GC.KeepAlive(dest);
    }

    /// <summary>dest[i] = a[i] op scalar ? 1.0 : 0.0. Pass <paramref name="scalarOnLeft"/> for scalar op a[i].</summary>
    public static void CompareScalar(CompareOp op, NumericBuffer a, double scalar, NumericBuffer dest,
                                     bool scalarOnLeft = false, Action? betweenChunks = null)
    {
        RequireSameLength(a.Length, dest.Length);

        // `scalar op x` is `x` mirrored-op `scalar` for every pair of doubles there is, NaN included
        // (both readings are false there), so one kernel serves both sides and neither has a branch
        // inside the loop.
        CompareOp effective = scalarOnLeft ? Mirror(op) : op;
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks, (start, len) =>
            CompareScalarChunk(effective, a.AsSpan(start, len), scalar, dest.AsSpan(start, len)));

        GC.KeepAlive(a);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// How many elements are not zero — <c>nnz</c>, and the count behind a mask. Negative zero is
    /// zero and NaN is not, which is what <c>!= 0</c> says of both.
    /// </summary>
    public static long CountNonZero(NumericBuffer a, Action? betweenChunks = null)
    {
        long[] tallies = new long[GrainsOf(a.Length)];
        ParallelKernels.For(a.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks,
            (start, len) => tallies[start / ParallelKernels.GrainElements] = CountNonZeroSpan(a.AsSpan(start, len)));

        // Summed in index order on this thread, which is what makes the total the same total on a
        // machine with one core and on a machine with sixteen.
        long count = 0;
        foreach (long tally in tallies)
        {
            count += tally;
        }

        GC.KeepAlive(a);
        return count;
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
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks,
            (start, len) => source.AsSpan(start, len).CopyTo(dest.AsSpan(start, len)));

        GC.KeepAlive(source);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// dest[k++] = source[i] for every i where mask[i] is not zero — a masked read that never builds
    /// the list of positions it would otherwise gather through. <paramref name="dest"/> must be
    /// exactly <see cref="CountNonZero"/> of the mask long. Returns how many elements were written.
    /// </summary>
    public static int Compact(NumericBuffer source, NumericBuffer mask, NumericBuffer dest,
                              Action? betweenChunks = null)
    {
        RequireSameLength(mask.Length, source.Length);
        int grains = GrainsOf(source.Length);
        if (grains <= 1)
        {
            int written = CompactSpan(source.AsSpan(), mask.AsSpan(), dest.AsSpan(), 0);
            betweenChunks?.Invoke();
            GC.KeepAlive(source);
            GC.KeepAlive(mask);
            GC.KeepAlive(dest);
            return written;
        }

        // Where each grain's matches begin, before any of them are moved: count first, add the
        // counts up in index order, and every grain then knows its own stretch of the destination
        // without having to wait for the grain before it. The result is the order a single thread
        // would have written, which is the only order the answer is allowed to be in.
        int[] offsets = new int[grains + 1];
        ParallelKernels.For(source.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks,
            (start, len) => offsets[(start / ParallelKernels.GrainElements) + 1] =
                (int)CountNonZeroSpan(mask.AsSpan(start, len)));

        for (int g = 1; g <= grains; g++)
        {
            offsets[g] += offsets[g - 1];
        }

        ParallelKernels.For(source.Length, ParallelKernels.MemoryBoundThreshold, betweenChunks,
            (start, len) => CompactSpan(source.AsSpan(start, len), mask.AsSpan(start, len),
                                        dest.AsSpan(), offsets[start / ParallelKernels.GrainElements]));

        GC.KeepAlive(source);
        GC.KeepAlive(mask);
        GC.KeepAlive(dest);
        return offsets[grains];
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

    /// <summary><c>JGRAPH_FAST_MATH=0</c> turns the approximate tier off; anything else leaves it on.</summary>
    private static int ResolveApproximateThreshold() =>
        Environment.GetEnvironmentVariable("JGRAPH_FAST_MATH") == "0"
            ? int.MaxValue
            : DefaultApproximateThreshold;

    /// <summary>How many grains <see cref="ParallelKernels.For"/> cuts this many elements into.</summary>
    private static int GrainsOf(int length) =>
        length <= 0 ? 0 : ((length - 1) / ParallelKernels.GrainElements) + 1;

    /// <summary>
    /// How much of an operation is worth splitting across threads: an operation whose time goes into
    /// moving memory needs a big enough array to be worth the fork, while one whose time goes into
    /// arithmetic has work to divide long before that.
    /// </summary>
    private static int ThresholdOf(BinaryOp op) =>
        op is BinaryOp.Remainder or BinaryOp.Power
            ? ParallelKernels.ComputeBoundThreshold
            : ParallelKernels.MemoryBoundThreshold;

    /// <inheritdoc cref="ThresholdOf(BinaryOp)"/>
    private static int ThresholdOf(UnaryOp op) =>
        DeterminismOf(op) == Determinism.Exact
            ? ParallelKernels.MemoryBoundThreshold
            : ParallelKernels.ComputeBoundThreshold;

    /// <summary>How many elements of one span are not zero.</summary>
    private static long CountNonZeroSpan(Span<double> x)
    {
        long count = 0;
        int i = 0;
        int width = Vector<double>.Count;
        if (Vector.IsHardwareAccelerated && x.Length >= width)
        {
            ref double xr = ref MemoryMarshal.GetReference(x);
            Vector<double> zero = Vector<double>.Zero;

            // Equals gives all-ones per zero lane; its complement gives -1 per non-zero lane,
            // and subtracting that adds one. NaN is unequal to zero, so it counts — as it must.
            Vector<long> tally = Vector<long>.Zero;
            for (; i <= x.Length - width; i += width)
            {
                tally -= Vector.OnesComplement(Vector.Equals(Vector.LoadUnsafe(ref xr, (nuint)i), zero));
            }

            for (int lane = 0; lane < width; lane++)
            {
                count += tally[lane];
            }
        }

        for (; i < x.Length; i++)
        {
            if (x[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Copies the matching elements of one span into <paramref name="dest"/> from <paramref name="at"/>.</summary>
    private static int CompactSpan(Span<double> source, Span<double> mask, Span<double> dest, int at)
    {
        int start = at;
        for (int i = 0; i < source.Length; i++)
        {
            if (mask[i] != 0)
            {
                dest[at++] = source[i];
            }
        }

        return at - start;
    }

    /// <summary>
    /// Whether no element is strictly below <paramref name="bound"/> — the domain test of a function
    /// whose real domain is a half-line.
    /// </summary>
    /// <remarks>
    /// An any-below test rather than a minimum, because a minimum propagates NaN and would then be
    /// NaN for a tile holding a NaN <em>and</em> a negative — passing the tile and answering NaN for
    /// the element that should have promoted to a complex number. A NaN fails every comparison
    /// including this one, so it is admitted here on its own, without hiding anything beside it.
    /// </remarks>
    private static bool NoneBelow(Span<double> x, double bound)
    {
        int i = 0;
        int width = Vector<double>.Count;
        if (Vector.IsHardwareAccelerated && x.Length >= width)
        {
            ref double xr = ref MemoryMarshal.GetReference(x);
            var limit = new Vector<double>(bound);
            Vector<long> below = Vector<long>.Zero;
            for (; i <= x.Length - width; i += width)
            {
                below |= Vector.LessThan(Vector.LoadUnsafe(ref xr, (nuint)i), limit);
            }

            if (below != Vector<long>.Zero)
            {
                return false;
            }
        }

        for (; i < x.Length; i++)
        {
            if (x[i] < bound)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One span of <see cref="Unary"/>: the vector kernel of every operation that has one.</summary>
    private static void UnaryChunk(UnaryOp op, Span<double> x, Span<double> d)
    {
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
                for (int i = 0; i < d.Length; i++) { d[i] = Math.Round(x[i]); }
                break;
            case UnaryOp.Sin: TensorPrimitives.Sin<double>(x, d); break;
            case UnaryOp.Cos: TensorPrimitives.Cos<double>(x, d); break;
            case UnaryOp.Tan: TensorPrimitives.Tan<double>(x, d); break;
            case UnaryOp.Exp: TensorPrimitives.Exp<double>(x, d); break;
            case UnaryOp.Log: TensorPrimitives.Log<double>(x, d); break;
            case UnaryOp.Log10: TensorPrimitives.Log10<double>(x, d); break;
            default: throw UnknownOp(op);
        }
    }

    /// <summary>One span of <see cref="UnaryScalar"/>: <see cref="Math"/>'s own functions, called directly.</summary>
    private static void UnaryScalarChunk(UnaryOp op, Span<double> x, Span<double> d)
    {
        int len = d.Length;
        switch (op)
        {
            case UnaryOp.Negate: for (int i = 0; i < len; i++) { d[i] = -x[i]; } break;
            case UnaryOp.Abs: for (int i = 0; i < len; i++) { d[i] = Math.Abs(x[i]); } break;
            case UnaryOp.Sqrt: for (int i = 0; i < len; i++) { d[i] = Math.Sqrt(x[i]); } break;
            case UnaryOp.Floor: for (int i = 0; i < len; i++) { d[i] = Math.Floor(x[i]); } break;
            case UnaryOp.Ceiling: for (int i = 0; i < len; i++) { d[i] = Math.Ceiling(x[i]); } break;
            case UnaryOp.Round: for (int i = 0; i < len; i++) { d[i] = Math.Round(x[i]); } break;
            case UnaryOp.Sin: for (int i = 0; i < len; i++) { d[i] = Math.Sin(x[i]); } break;
            case UnaryOp.Cos: for (int i = 0; i < len; i++) { d[i] = Math.Cos(x[i]); } break;
            case UnaryOp.Tan: for (int i = 0; i < len; i++) { d[i] = Math.Tan(x[i]); } break;
            case UnaryOp.Exp: for (int i = 0; i < len; i++) { d[i] = Math.Exp(x[i]); } break;
            case UnaryOp.Log: for (int i = 0; i < len; i++) { d[i] = Math.Log(x[i]); } break;
            case UnaryOp.Log10: for (int i = 0; i < len; i++) { d[i] = Math.Log10(x[i]); } break;
            default: throw UnknownOp(op);
        }
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

    /// <summary>One span of <see cref="BinaryScalarRight(BinaryOp, NumericBuffer, double, NumericBuffer, Action)"/>.</summary>
    private static void BinaryScalarRightChunk(BinaryOp op, Span<double> x, double scalar, Span<double> d)
    {
        int len = d.Length;
        switch (op)
        {
            case BinaryOp.Add: TensorPrimitives.Add<double>(x, scalar, d); break;
            case BinaryOp.Subtract: TensorPrimitives.Add<double>(x, -scalar, d); break;
            case BinaryOp.Multiply: TensorPrimitives.Multiply<double>(x, scalar, d); break;
            case BinaryOp.Divide: TensorPrimitives.Divide<double>(x, scalar, d); break;
            case BinaryOp.Remainder:
                for (int i = 0; i < len; i++) { d[i] = x[i] % scalar; }
                break;

            // Not special-cased to x*x for an exponent of 2, tempting as it is: Math.Pow is not
            // correctly rounded, and over 212 million random doubles it disagreed with the product
            // on 52,298 of them by one ulp. The product is the better answer and the boxed
            // interpreter does not give it, so taking it here would buy speed with parity.
            case BinaryOp.Power:
                for (int i = 0; i < len; i++) { d[i] = Math.Pow(x[i], scalar); }
                break;
            default: throw UnknownOp(op);
        }
    }

    /// <summary>One span of <see cref="BinaryScalarLeft(BinaryOp, double, NumericBuffer, NumericBuffer, Action)"/>.</summary>
    private static void BinaryScalarLeftChunk(BinaryOp op, double scalar, Span<double> y, Span<double> d)
    {
        int len = d.Length;
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

    /// <summary>
    /// dest[i] = x[i] op y[i] ? 1.0 : 0.0, a register at a time. The comparison instruction leaves
    /// all-ones where it holds, so selecting between one and zero is the whole conversion — and it
    /// is exact: a mask is 1.0 or 0.0 with no arithmetic in between, so the vector form and the
    /// scalar form cannot disagree.
    /// </summary>
    private static void CompareChunk(CompareOp op, Span<double> x, Span<double> y, Span<double> d)
    {
        int i = 0;
        int width = Vector<double>.Count;
        if (Vector.IsHardwareAccelerated && d.Length >= width)
        {
            ref double xr = ref MemoryMarshal.GetReference(x);
            ref double yr = ref MemoryMarshal.GetReference(y);
            ref double dr = ref MemoryMarshal.GetReference(d);
            var one = new Vector<double>(1.0);
            for (; i <= d.Length - width; i += width)
            {
                Vector<long> mask = Mask(op, Vector.LoadUnsafe(ref xr, (nuint)i), Vector.LoadUnsafe(ref yr, (nuint)i));
                Vector.ConditionalSelect(mask, one, Vector<double>.Zero).StoreUnsafe(ref dr, (nuint)i);
            }
        }

        for (; i < d.Length; i++)
        {
            d[i] = Holds(op, x[i], y[i]) ? 1.0 : 0.0;
        }
    }

    /// <summary>dest[i] = x[i] op scalar ? 1.0 : 0.0 — <see cref="CompareChunk"/> against a broadcast.</summary>
    private static void CompareScalarChunk(CompareOp op, Span<double> x, double scalar, Span<double> d)
    {
        int i = 0;
        int width = Vector<double>.Count;
        if (Vector.IsHardwareAccelerated && d.Length >= width)
        {
            ref double xr = ref MemoryMarshal.GetReference(x);
            ref double dr = ref MemoryMarshal.GetReference(d);
            var one = new Vector<double>(1.0);
            var right = new Vector<double>(scalar);
            for (; i <= d.Length - width; i += width)
            {
                Vector<long> mask = Mask(op, Vector.LoadUnsafe(ref xr, (nuint)i), right);
                Vector.ConditionalSelect(mask, one, Vector<double>.Zero).StoreUnsafe(ref dr, (nuint)i);
            }
        }

        for (; i < d.Length; i++)
        {
            d[i] = Holds(op, x[i], scalar) ? 1.0 : 0.0;
        }
    }

    /// <summary>The all-ones-where-it-holds mask of a comparison.</summary>
    private static Vector<long> Mask(CompareOp op, Vector<double> a, Vector<double> b) => op switch
    {
        CompareOp.Less => Vector.LessThan(a, b),
        CompareOp.LessEqual => Vector.LessThanOrEqual(a, b),
        CompareOp.Greater => Vector.GreaterThan(a, b),
        CompareOp.GreaterEqual => Vector.GreaterThanOrEqual(a, b),
        CompareOp.Equal => Vector.Equals(a, b),

        // Not "greater or less": NaN is unequal to everything including itself, and only the
        // complement of equality says so.
        CompareOp.NotEqual => Vector.OnesComplement(Vector.Equals(a, b)),
        _ => throw UnknownOp(op),
    };

    /// <summary>The same comparison read from the other side, so <c>s &lt; x</c> becomes <c>x &gt; s</c>.</summary>
    private static CompareOp Mirror(CompareOp op) => op switch
    {
        CompareOp.Less => CompareOp.Greater,
        CompareOp.LessEqual => CompareOp.GreaterEqual,
        CompareOp.Greater => CompareOp.Less,
        CompareOp.GreaterEqual => CompareOp.LessEqual,
        CompareOp.Equal => CompareOp.Equal,
        CompareOp.NotEqual => CompareOp.NotEqual,
        _ => throw UnknownOp(op),
    };

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
