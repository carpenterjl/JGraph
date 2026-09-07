namespace JGraph.Numerics.Sparse;

/// <summary>
/// What the eleven iterative solvers are allowed to know about their matrix: how to multiply a
/// vector by it, and how to multiply by its transpose. A matrix answers both from its storage; a
/// script's function handle answers whichever of the two it was written to answer.
/// </summary>
/// <remarks>
/// <para>
/// MATLAB draws this line in <c>private\iterchk.m</c> and <c>private\iterapp.m</c>: every solver
/// asks for <c>A*x</c> or <c>M\x</c> through one applier, and the applier is the only thing that
/// knows whether it holds a matrix or a handle. Keeping the same line here is what lets
/// <c>pcg(A, b)</c> and <c>pcg(@(x) A*x, b)</c> take the identical path, and it is what lets a
/// preconditioner be a matrix in one argument and a handle in the next.
/// </para>
/// <para>
/// A preconditioner given as a matrix is applied as <c>M\x</c>. When the matrix is triangular —
/// which is what <c>ichol</c> and <c>ilu</c> hand back, and the overwhelmingly common case — the
/// solve is one substitution sweep, the same operation MATLAB's <c>mldivide</c> picks after its own
/// triangularity test. Anything else is factored once, at construction, and the factors are reused
/// for every iteration: the alternative is an LU per application, which would make the
/// preconditioner cost more than the system.
/// </para>
/// </remarks>
public sealed class KrylovOperator
{
    private readonly Func<double[], double[]> _forward;
    private readonly Func<double[], double[]>? _adjoint;

    private KrylovOperator(Func<double[], double[]> forward, Func<double[], double[]>? adjoint)
    {
        _forward = forward;
        _adjoint = adjoint;
    }

    /// <summary>Whether the transposed direction can be asked for at all.</summary>
    public bool HasAdjoint => _adjoint is not null;

    /// <summary>Multiplication by a stored matrix, both directions from the one storage.</summary>
    public static KrylovOperator Multiply(CscMatrix matrix)
    {
        CscMatrix? transposed = null;
        return new KrylovOperator(
            matrix.MultiplyVector,
            x => (transposed ??= matrix.Transpose()).MultiplyVector(x));
    }

    /// <summary>
    /// <c>M\x</c> for a stored preconditioner, factored once. The transposed direction is
    /// <c>M'\x</c>, which <c>bicg</c>, <c>qmr</c> and <c>lsqr</c> ask for.
    /// </summary>
    public static KrylovOperator Solve(CscMatrix matrix)
    {
        CscSolver solver = CscSolver.For(matrix);
        CscSolver? adjoint = null;
        return new KrylovOperator(
            solver.Solve,
            x => (adjoint ??= CscSolver.For(matrix.Transpose())).Solve(x));
    }

    /// <summary>
    /// A script's function handle. <paramref name="adjoint"/> is null when the handle takes only a
    /// vector, which is every solver but the three that need <c>A'</c>.
    /// </summary>
    public static KrylovOperator Function(Func<double[], double[]> forward, Func<double[], double[]>? adjoint) =>
        new(forward, adjoint);

    /// <summary>The forward direction: <c>A*x</c>, or <c>M\x</c> for a preconditioner.</summary>
    public double[] Apply(double[] x) => _forward(x);

    /// <summary>The transposed direction.</summary>
    public double[] ApplyTransposed(double[] x) =>
        _adjoint is null
            ? throw new InvalidOperationException(
                "This operator was given as a function handle that does not accept a transpose flag.")
            : _adjoint(x);
}
