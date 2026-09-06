using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    /// <summary>
    /// Fourier–Bessel approximation to the first twelve Dirichlet modes of the L-shaped domain.
    /// Boundary collocation determines the coefficients; a partial expansion permits free edges.
    /// See Fox, Henrici and Moler, SIAM Journal on Numerical Analysis 4 (1967), 89–102.
    /// </summary>
    private static JgsValue Membrane(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("membrane", args, 0, 4, line, col);
        int Positive(int index, int fallback)
        {
            if (index >= args.Count) return fallback;
            double v = Num("membrane", args, index, line, col);
            if (!double.IsFinite(v) || v < 1 || v > int.MaxValue || !double.IsInteger(v))
                throw new JgsRuntimeException(line, col, "membrane parameters must be positive integer scalars.");
            return (int)v;
        }

        int mode = Positive(0, 1), mesh = Positive(1, 15);
        int terms = Positive(2, Math.Min(mesh, 9)), partial = Positive(3, Math.Min(terms, 2));
        if (mode > 12) throw new JgsRuntimeException(line, col, "membrane supports eigenfunctions 1 through 12.");
        if (partial > terms || terms > 3L * mesh)
            throw new JgsRuntimeException(line, col, "membrane needs np <= n <= 3*m.");
        if (2L * mesh + 1 > Math.Sqrt(int.MaxValue))
            throw new JgsRuntimeException(line, col, "membrane grid is too large.");

        double[] eigenvalues = [9.6397238445, 15.19725192, 2 * Math.PI * Math.PI, 29.5214811,
            31.9126360, 41.4745099, 44.948488, 5 * Math.PI * Math.PI, 5 * Math.PI * Math.PI,
            56.709610, 65.376535, 71.057755];
        int[] symmetries = [1, 2, 3, 2, 1, 1, 2, 3, 3, 1, 2, 1];
        int symmetry = symmetries[mode - 1];
        double frequency = Math.Sqrt(eigenvalues[mode - 1]);
        var orders = new double[terms];
        int order = symmetry;
        for (int j = 0; j < terms; j++)
        {
            orders[j] = order * (2.0 / 3);
            do { order += symmetry == 3 ? 3 : 2; } while (symmetry != 3 && order % 3 == 0);
        }

        var boundary = new double[3 * mesh, terms];
        for (int row = 0; row < 3 * mesh; row++)
        {
            double angle = (row + 1) * Math.PI / (4 * mesh);
            double radius = 1 / (row < mesh ? Math.Cos(angle) : Math.Sin(angle));
            for (int j = 0; j < terms; j++)
                boundary[row, j] = BesselFunctions.J(orders[j], frequency * radius) * Math.Sin(orders[j] * angle);
        }
        var qr = QrDecomposition.Factor(boundary, pivot: true);
        double[,] upper = qr.R;
        var solution = new double[terms];
        solution[^1] = 1;
        for (int row = terms - 2; row >= 0; row--)
        {
            double sum = 0;
            for (int j = row + 1; j < terms; j++) sum += upper[row, j] * solution[j];
            solution[row] = -sum / upper[row, row];
        }
        var coefficients = new double[terms];
        int[] permutation = qr.PivotVector;
        for (int j = 0; j < terms; j++) coefficients[permutation[j]] = solution[j];
        if (coefficients[0] < 0)
            for (int j = 0; j < terms; j++) coefficients[j] = -coefficients[j];

        int side = 2 * mesh + 1;
        var triangle = new double[side, side];
        for (int row = 0; row <= mesh; row++)
            for (int column = row; column < side; column++)
            {
                double x = (column - mesh) / (double)mesh, y = (mesh - row) / (double)mesh;
                double angle = Math.Atan2(y, x), radius = frequency * Math.Sqrt(x * x + y * y);
                for (int j = 0; j < partial; j++)
                    triangle[row, column] += coefficients[j] * BesselFunctions.J(orders[j], radius) * Math.Sin(orders[j] * angle);
            }
        var surface = new double[side, side];
        double peak = 0;
        for (int row = 0; row < side; row++)
            for (int column = 0; column < side; column++)
            {
                double z = symmetry == 2 ? triangle[row, column] - triangle[column, row]
                    : row == column ? triangle[row, column] : triangle[row, column] + triangle[column, row];
                surface[column, side - 1 - row] = z;
                peak = Math.Max(peak, Math.Abs(z));
            }
        return JgsMatrix.Build(side, side, (r, c) => surface[r, c] / peak);
    }
}
