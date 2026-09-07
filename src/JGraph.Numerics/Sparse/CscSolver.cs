namespace JGraph.Numerics.Sparse;

/// <summary>
/// One matrix prepared for many right-hand sides — what a Krylov preconditioner needs, since it
/// applies <c>M\x</c> once per iteration and the matrix never changes.
/// </summary>
/// <remarks>
/// A triangular matrix is recognised and solved by substitution rather than factored. That is not
/// only the cheap path: it is the faithful one. MATLAB's <c>mldivide</c> tests for triangularity
/// before it does anything else, so <c>L\x</c> for an <c>ichol</c> factor is a substitution sweep
/// there too, and a sweep and an LU do not round the same way. An iteration count that turns on the
/// last bit of a preconditioned residual would part company with MATLAB's if this one factored.
/// </remarks>
internal sealed class CscSolver
{
    private readonly CscMatrix _matrix;
    private readonly Shape _shape;
    private readonly CscMatrix.Factorization _factored;

    private CscSolver(CscMatrix matrix, Shape shape, CscMatrix.Factorization factored)
    {
        _matrix = matrix;
        _shape = shape;
        _factored = factored;
    }

    private enum Shape
    {
        Lower,
        Upper,
        General,
    }

    public static CscSolver For(CscMatrix matrix)
    {
        if (matrix.Rows != matrix.Cols)
        {
            throw new ArgumentException($"A {matrix.Rows}x{matrix.Cols} matrix cannot be a preconditioner.");
        }

        Shape shape = ShapeOf(matrix);
        return new CscSolver(matrix, shape,
            shape == Shape.General ? matrix.FactorFor("\\") : default);
    }

    public double[] Solve(double[] b)
    {
        if (b.Length != _matrix.Rows)
        {
            throw new ArgumentException(
                $"A {_matrix.Rows}x{_matrix.Cols} preconditioner cannot be applied to {b.Length} values.");
        }

        return _shape switch
        {
            Shape.Lower => ForwardSubstitute(b),
            Shape.Upper => BackSubstitute(b),
            _ => _matrix.SolveWith(_factored, b),
        };
    }

    private static Shape ShapeOf(CscMatrix matrix)
    {
        bool lower = true;
        bool upper = true;
        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1]; i++)
            {
                int r = matrix.RowIndices[i];
                if (matrix.Values[i] == 0)
                {
                    continue;
                }

                if (r < c)
                {
                    lower = false;
                }
                else if (r > c)
                {
                    upper = false;
                }
            }
        }

        // A structurally triangular matrix with a missing diagonal entry is singular; leave it to
        // the general path, which says so in the terms the rest of the sparse code already uses.
        if (lower || upper)
        {
            for (int c = 0; c < matrix.Cols; c++)
            {
                if (matrix.At(c, c) == 0)
                {
                    return Shape.General;
                }
            }
        }

        return lower ? Shape.Lower : upper ? Shape.Upper : Shape.General;
    }

    private double[] ForwardSubstitute(double[] b)
    {
        var x = (double[])b.Clone();
        for (int c = 0; c < _matrix.Cols; c++)
        {
            double diagonal = 0;
            for (int i = _matrix.ColumnStarts[c]; i < _matrix.ColumnStarts[c + 1]; i++)
            {
                if (_matrix.RowIndices[i] == c)
                {
                    diagonal = _matrix.Values[i];
                }
            }

            x[c] /= diagonal;
            double value = x[c];
            if (value == 0)
            {
                continue;
            }

            for (int i = _matrix.ColumnStarts[c]; i < _matrix.ColumnStarts[c + 1]; i++)
            {
                int r = _matrix.RowIndices[i];
                if (r > c)
                {
                    x[r] -= _matrix.Values[i] * value;
                }
            }
        }

        return x;
    }

    private double[] BackSubstitute(double[] b)
    {
        var x = (double[])b.Clone();
        for (int c = _matrix.Cols - 1; c >= 0; c--)
        {
            double diagonal = 0;
            for (int i = _matrix.ColumnStarts[c]; i < _matrix.ColumnStarts[c + 1]; i++)
            {
                if (_matrix.RowIndices[i] == c)
                {
                    diagonal = _matrix.Values[i];
                }
            }

            x[c] /= diagonal;
            double value = x[c];
            if (value == 0)
            {
                continue;
            }

            for (int i = _matrix.ColumnStarts[c]; i < _matrix.ColumnStarts[c + 1]; i++)
            {
                int r = _matrix.RowIndices[i];
                if (r < c)
                {
                    x[r] -= _matrix.Values[i] * value;
                }
            }
        }

        return x;
    }
}
