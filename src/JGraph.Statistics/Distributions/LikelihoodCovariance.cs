namespace JGraph.Statistics.Distributions;

/// <summary>
/// How precise a maximum likelihood estimate is, read off the shape of the likelihood around it.
/// </summary>
/// <remarks>
/// The asymptotic covariance of an estimate is the inverse of the observed information — the second
/// derivative of the negative log-likelihood at the estimate. The second derivative is taken by
/// differencing rather than symbolically, because the likelihood arrives here as a function and not
/// as a formula, and the step is scaled to each parameter's own size so that a parameter measured in
/// millions and one measured in thousandths are differenced equally well.
/// </remarks>
public static class LikelihoodCovariance
{
    /// <summary>
    /// The asymptotic covariance matrix of an estimate: the inverse of the numerically differenced
    /// Hessian of <paramref name="negativeLogLikelihood"/> at <paramref name="estimate"/>.
    /// </summary>
    public static double[,] Of(
        Func<double[], double> negativeLogLikelihood, double[] estimate)
    {
        ArgumentNullException.ThrowIfNull(negativeLogLikelihood);
        ArgumentNullException.ThrowIfNull(estimate);

        int n = estimate.Length;
        var step = new double[n];
        for (int i = 0; i < n; i++)
        {
            // The cube root of the machine epsilon is the step that balances the truncation error of a
            // central second difference against the cancellation in it.
            step[i] = Math.Pow(2.2e-16, 1.0 / 3) * Math.Max(Math.Abs(estimate[i]), 1e-4);
        }

        double centre = negativeLogLikelihood(estimate);
        var hessian = new double[n, n];

        for (int i = 0; i < n; i++)
        {
            double forward = negativeLogLikelihood(Shift(estimate, i, step[i]));
            double backward = negativeLogLikelihood(Shift(estimate, i, -step[i]));
            hessian[i, i] = (forward - (2 * centre) + backward) / (step[i] * step[i]);

            for (int j = i + 1; j < n; j++)
            {
                double pp = negativeLogLikelihood(Shift(Shift(estimate, i, step[i]), j, step[j]));
                double pm = negativeLogLikelihood(Shift(Shift(estimate, i, step[i]), j, -step[j]));
                double mp = negativeLogLikelihood(Shift(Shift(estimate, i, -step[i]), j, step[j]));
                double mm = negativeLogLikelihood(Shift(Shift(estimate, i, -step[i]), j, -step[j]));
                double mixed = (pp - pm - mp + mm) / (4 * step[i] * step[j]);
                hessian[i, j] = mixed;
                hessian[j, i] = mixed;
            }
        }

        return Multivariate.SymmetricInverse(hessian);
    }

    private static double[] Shift(double[] point, int index, double by)
    {
        var moved = (double[])point.Clone();
        moved[index] += by;
        return moved;
    }
}
