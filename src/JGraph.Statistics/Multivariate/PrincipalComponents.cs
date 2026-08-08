using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Statistics.Multivariate;

/// <summary>
/// The directions a cloud of observations varies in: principal components from the data or from a
/// covariance already formed, the residual left when only some of them are kept, the probabilistic
/// version that tolerates gaps, the non-negative factorization, and the rotations that make a set of
/// loadings readable.
/// </summary>
/// <remarks>
/// <para>
/// The components come from a singular value decomposition of the centred data rather than from the
/// eigenvectors of its covariance. The two are the same answer in exact arithmetic; they are not the
/// same answer in floating point, because forming the covariance squares the condition number, and a
/// data set whose variables are nearly collinear — which is the case anyone runs this on — loses half
/// its digits in the forming. <see cref="FromCovariance"/> exists for the caller who has only the
/// covariance, and takes that loss knowingly.
/// </para>
/// <para>
/// A component's sign is arbitrary: negating a loading vector and its scores describes the same
/// direction. So that two runs on the same data agree, and so that a test can pin a value, each
/// component is turned so its largest-magnitude loading is positive.
/// </para>
/// </remarks>
public static class PrincipalComponents
{
    /// <summary>What a principal component analysis produced.</summary>
    /// <param name="Coefficients">The loadings, one column per component.</param>
    /// <param name="Scores">The observations in component coordinates.</param>
    /// <param name="Latent">The variance each component carries.</param>
    /// <param name="TSquared">Hotelling's statistic, one per observation.</param>
    /// <param name="Explained">The percentage of the total variance each component carries.</param>
    /// <param name="Centre">The column means that were removed, or zeros.</param>
    public readonly record struct Analysis(
        double[,] Coefficients,
        double[,] Scores,
        double[] Latent,
        double[] TSquared,
        double[] Explained,
        double[] Centre);

    /// <summary>Analyses <paramref name="data"/>, one observation per row.</summary>
    /// <param name="data">The observations.</param>
    /// <param name="centre">Whether to remove each variable's mean first.</param>
    /// <param name="wanted">How many components to keep, or null for as many as the data supports.</param>
    /// <param name="weights">One weight per observation, or null for equal weights.</param>
    /// <param name="variableWeights">One weight per variable, or null for equal weights.</param>
    public static Analysis Analyse(
        double[,] data,
        bool centre = true,
        int? wanted = null,
        double[]? weights = null,
        double[]? variableWeights = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        int n = data.GetLength(0);
        int p = data.GetLength(1);
        if (n == 0 || p == 0)
        {
            throw new ArgumentException("There is nothing to analyse.", nameof(data));
        }

        double[] observationWeights = weights ?? Ones(n);
        double[] scaling = variableWeights ?? Ones(p);
        if (observationWeights.Length != n)
        {
            throw new ArgumentException("There must be one weight for each observation.", nameof(weights));
        }

        if (scaling.Length != p)
        {
            throw new ArgumentException("There must be one weight for each variable.", nameof(variableWeights));
        }

        double totalWeight = 0;
        foreach (double w in observationWeights)
        {
            if (!(w >= 0))
            {
                throw new ArgumentException("An observation weight must be non-negative.", nameof(weights));
            }

            totalWeight += w;
        }

        var means = new double[p];
        if (centre)
        {
            for (int c = 0; c < p; c++)
            {
                double sum = 0;
                for (int r = 0; r < n; r++)
                {
                    sum += observationWeights[r] * data[r, c];
                }

                means[c] = totalWeight > 0 ? sum / totalWeight : 0;
            }
        }

        // The weights enter by scaling: an observation weighted w counts as √w rows, and a variable
        // weighted v is measured in units of 1/√v. Factoring the scaled matrix and unscaling the
        // answer is the same analysis and needs no weighted decomposition of its own.
        var scaled = new double[n, p];
        for (int r = 0; r < n; r++)
        {
            double rowScale = Math.Sqrt(observationWeights[r]);
            for (int c = 0; c < p; c++)
            {
                scaled[r, c] = rowScale * (data[r, c] - means[c]) * Math.Sqrt(scaling[c]);
            }
        }

        Svd svd = Svd.Factor(scaled);
        int available = Math.Min(centre ? Math.Max(n - 1, 1) : n, p);
        int keep = Math.Min(wanted ?? available, Math.Min(available, svd.Values.Length));
        if (keep < 1)
        {
            throw new ArgumentException("At least one component must be kept.", nameof(wanted));
        }

        double denominator = centre ? Math.Max(totalWeight - 1, 1) : Math.Max(totalWeight, 1);
        var latent = new double[keep];
        var coefficients = new double[p, keep];
        var scores = new double[n, keep];

        for (int c = 0; c < keep; c++)
        {
            double s = svd.Values[c];
            latent[c] = s * s / denominator;

            int largest = 0;
            for (int r = 0; r < p; r++)
            {
                if (Math.Abs(svd.V[r, c]) > Math.Abs(svd.V[largest, c]))
                {
                    largest = r;
                }
            }

            double sign = svd.V[largest, c] < 0 ? -1 : 1;
            for (int r = 0; r < p; r++)
            {
                coefficients[r, c] = sign * svd.V[r, c] / Math.Sqrt(scaling[r]);
            }

            for (int r = 0; r < n; r++)
            {
                double rowScale = Math.Sqrt(observationWeights[r]);
                scores[r, c] = rowScale > 0 ? sign * svd.U[r, c] * s / rowScale : 0;
            }
        }

        double total = 0;
        for (int c = 0; c < Math.Min(available, svd.Values.Length); c++)
        {
            total += svd.Values[c] * svd.Values[c] / denominator;
        }

        var explained = new double[keep];
        for (int c = 0; c < keep; c++)
        {
            explained[c] = total > 0 ? 100 * latent[c] / total : 0;
        }

        // Hotelling's statistic measures each observation against every component the data supports,
        // not only the ones kept — which is why an observation can be an outlier in a direction the
        // analysis discarded and still be reported as one.
        int forStatistic = Math.Min(available, svd.Values.Length);
        var tsquared = new double[n];
        for (int r = 0; r < n; r++)
        {
            double rowScale = Math.Sqrt(observationWeights[r]);
            for (int c = 0; c < forStatistic; c++)
            {
                double variance = svd.Values[c] * svd.Values[c] / denominator;
                if (!(variance > 0))
                {
                    continue;
                }

                double score = rowScale > 0 ? svd.U[r, c] * svd.Values[c] / rowScale : 0;
                tsquared[r] += score * score / variance;
            }
        }

        return new Analysis(coefficients, scores, latent, tsquared, explained, means);
    }

    /// <summary>The components of a covariance matrix that was formed elsewhere.</summary>
    public static (double[,] Coefficients, double[] Latent, double[] Explained) FromCovariance(double[,] covariance)
    {
        ArgumentNullException.ThrowIfNull(covariance);
        int p = covariance.GetLength(0);
        if (covariance.GetLength(1) != p)
        {
            throw new ArgumentException("A covariance matrix must be square.", nameof(covariance));
        }

        Eigen eigen = Eigen.Factor(Symmetrized(covariance));
        var order = new int[p];
        for (int i = 0; i < p; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            int byValue = eigen.Values[b].Real.CompareTo(eigen.Values[a].Real);
            return byValue != 0 ? byValue : a.CompareTo(b);
        });

        // A covariance's eigenvalues are non-negative in exact arithmetic; rounding can push the
        // smallest slightly below zero, and a negative variance would make the percentages nonsense.
        var latent = new double[p];
        double total = 0;
        for (int c = 0; c < p; c++)
        {
            latent[c] = Math.Max(eigen.Values[order[c]].Real, 0);
            total += latent[c];
        }

        var coefficients = new double[p, p];
        for (int c = 0; c < p; c++)
        {
            int largest = 0;
            for (int r = 0; r < p; r++)
            {
                if (Math.Abs(eigen.Vectors[r, order[c]].Real) > Math.Abs(eigen.Vectors[largest, order[c]].Real))
                {
                    largest = r;
                }
            }

            double sign = eigen.Vectors[largest, order[c]].Real < 0 ? -1 : 1;
            for (int r = 0; r < p; r++)
            {
                coefficients[r, c] = sign * eigen.Vectors[r, order[c]].Real;
            }
        }

        var explained = new double[p];
        for (int c = 0; c < p; c++)
        {
            explained[c] = total > 0 ? 100 * latent[c] / total : 0;
        }

        return (coefficients, latent, explained);
    }

    /// <summary>
    /// What the first <paramref name="kept"/> components fail to account for.
    /// </summary>
    /// <returns>The residuals, and the data as those components reconstruct it.</returns>
    public static (double[,] Residuals, double[,] Reconstructed) Residuals(double[,] data, int kept)
    {
        ArgumentNullException.ThrowIfNull(data);
        int n = data.GetLength(0);
        int p = data.GetLength(1);
        if (kept < 0 || kept > p)
        {
            throw new ArgumentException(
                "The number of components kept must be between zero and the number of variables.", nameof(kept));
        }

        Analysis analysis = kept > 0 ? Analyse(data, centre: true, wanted: kept) : Analyse(data, centre: true);
        var reconstructed = new double[n, p];
        var residuals = new double[n, p];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                double fitted = analysis.Centre[c];
                for (int k = 0; k < kept; k++)
                {
                    fitted += analysis.Scores[r, k] * analysis.Coefficients[c, k];
                }

                reconstructed[r, c] = fitted;
                residuals[r, c] = data[r, c] - fitted;
            }
        }

        return (residuals, reconstructed);
    }

    /// <summary>What a probabilistic analysis produced.</summary>
    /// <param name="Coefficients">The loadings, orthonormal columns.</param>
    /// <param name="Scores">The observations in component coordinates.</param>
    /// <param name="Variances">The variance each component carries.</param>
    /// <param name="Centre">The estimated column means.</param>
    /// <param name="Noise">The estimated variance of what is left over.</param>
    /// <param name="Iterations">How many passes the search took.</param>
    /// <param name="Converged">Whether the likelihood settled before the limit.</param>
    /// <param name="LogLikelihood">The log-likelihood at the answer.</param>
    public readonly record struct Probabilistic(
        double[,] Coefficients,
        double[,] Scores,
        double[] Variances,
        double[] Centre,
        double Noise,
        int Iterations,
        bool Converged,
        double LogLikelihood);

    /// <summary>
    /// The probabilistic analysis: the same components, fitted by maximum likelihood so that a missing
    /// value costs its own observation rather than the whole row.
    /// </summary>
    /// <remarks>
    /// The search alternates two closed-form steps — the expected component scores given the loadings,
    /// then the loadings given those scores — which is what lets a NaN be simply left out of the sums
    /// it would have entered. A complete data set converges to the ordinary components with the noise
    /// variance absorbing the discarded directions, and that identity is what the fit is pinned by.
    /// </remarks>
    public static Probabilistic Probabilistically(double[,] data, int components, int maxIterations = 1000, double tolerance = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(data);
        int n = data.GetLength(0);
        int p = data.GetLength(1);
        if (components < 1 || components >= p)
        {
            throw new ArgumentException(
                "The number of components must be at least one and fewer than the number of variables.",
                nameof(components));
        }

        var observed = new bool[n, p];
        var means = new double[p];
        var counts = new int[p];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                observed[r, c] = !double.IsNaN(data[r, c]);
                if (observed[r, c])
                {
                    means[c] += data[r, c];
                    counts[c]++;
                }
            }
        }

        for (int c = 0; c < p; c++)
        {
            if (counts[c] == 0)
            {
                throw new ArgumentException("A variable has no observed values at all.", nameof(data));
            }

            means[c] /= counts[c];
        }

        // The gaps are filled with the variable's mean to start, so the first loadings come from a
        // complete matrix; from then on each pass refills them with what the model predicts.
        var filled = new double[n, p];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                filled[r, c] = observed[r, c] ? data[r, c] : means[c];
            }
        }

        double[,] loadings = Analyse(filled, centre: true, wanted: components).Coefficients;
        double noise = 1;
        var scores = new double[n, components];
        double previous = double.NegativeInfinity;
        bool converged = false;
        int iteration = 0;

        for (; iteration < maxIterations; iteration++)
        {
            for (int c = 0; c < p; c++)
            {
                double sum = 0;
                for (int r = 0; r < n; r++)
                {
                    sum += filled[r, c];
                }

                means[c] = sum / n;
            }

            var centred = new double[n, p];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < p; c++)
                {
                    centred[r, c] = filled[r, c] - means[c];
                }
            }

            double[,] cross = AtA(loadings);
            for (int i = 0; i < components; i++)
            {
                cross[i, i] += noise;
            }

            double[,] inverse = Inverse(cross);
            for (int r = 0; r < n; r++)
            {
                for (int i = 0; i < components; i++)
                {
                    double projected = 0;
                    for (int j = 0; j < components; j++)
                    {
                        double partial = 0;
                        for (int c = 0; c < p; c++)
                        {
                            partial += loadings[c, j] * centred[r, c];
                        }

                        projected += inverse[i, j] * partial;
                    }

                    scores[r, i] = projected;
                }
            }

            double[,] scoreCross = AtA(scores);
            for (int i = 0; i < components; i++)
            {
                for (int j = 0; j < components; j++)
                {
                    scoreCross[i, j] += n * noise * inverse[i, j];
                }
            }

            double[,] scoreInverse = Inverse(scoreCross);
            var updated = new double[p, components];
            for (int c = 0; c < p; c++)
            {
                for (int i = 0; i < components; i++)
                {
                    double value = 0;
                    for (int j = 0; j < components; j++)
                    {
                        double partial = 0;
                        for (int r = 0; r < n; r++)
                        {
                            partial += centred[r, c] * scores[r, j];
                        }

                        value += partial * scoreInverse[j, i];
                    }

                    updated[c, i] = value;
                }
            }

            loadings = updated;

            double residual = 0;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < p; c++)
                {
                    double fitted = 0;
                    for (int i = 0; i < components; i++)
                    {
                        fitted += loadings[c, i] * scores[r, i];
                    }

                    double gap = centred[r, c] - fitted;
                    residual += gap * gap;
                    if (!observed[r, c])
                    {
                        filled[r, c] = means[c] + fitted;
                    }
                }
            }

            noise = Math.Max(residual / (n * p), 1e-12);
            double likelihood = -0.5 * n * p * (Math.Log(2 * Math.PI * noise) + 1);
            if (Math.Abs(likelihood - previous) < tolerance * (1 + Math.Abs(previous)))
            {
                converged = true;
                previous = likelihood;
                iteration++;
                break;
            }

            previous = likelihood;
        }

        // The loadings the search converges to span the right space but are neither orthogonal nor
        // ordered; one more decomposition turns them into components that can be compared with the
        // ordinary analysis, which is what makes the two agree on complete data.
        Svd svd = Svd.Factor(loadings);
        var coefficients = new double[p, components];
        var variances = new double[components];
        for (int i = 0; i < components; i++)
        {
            int largest = 0;
            for (int r = 0; r < p; r++)
            {
                if (Math.Abs(svd.U[r, i]) > Math.Abs(svd.U[largest, i]))
                {
                    largest = r;
                }
            }

            double sign = svd.U[largest, i] < 0 ? -1 : 1;
            for (int r = 0; r < p; r++)
            {
                coefficients[r, i] = sign * svd.U[r, i];
            }

            variances[i] = (svd.Values[i] * svd.Values[i]) + noise;
        }

        var rotated = new double[n, components];
        for (int r = 0; r < n; r++)
        {
            for (int i = 0; i < components; i++)
            {
                double value = 0;
                for (int c = 0; c < p; c++)
                {
                    value += (filled[r, c] - means[c]) * coefficients[c, i];
                }

                rotated[r, i] = value;
            }
        }

        return new Probabilistic(coefficients, rotated, variances, means, noise, iteration, converged, previous);
    }

    /// <summary>
    /// The non-negative factorization <c>A ≈ W·H</c>, both factors held at or above zero.
    /// </summary>
    /// <remarks>
    /// The constraint is what makes the factors readable — a part that adds rather than a direction
    /// that can subtract — and it is also what makes the problem non-convex, so the answer depends on
    /// where the search started and the caller who wants a reproducible one seeds the stream.
    /// </remarks>
    /// <param name="a">The matrix to factor, no entry below zero.</param>
    /// <param name="k">How many factors.</param>
    /// <param name="random">The stream the starting factors are drawn from.</param>
    /// <param name="replicates">How many times to start over, keeping the best.</param>
    /// <param name="maxIterations">The most passes any one run may take.</param>
    /// <param name="tolerance">How little the residual must change for the run to stop.</param>
    /// <param name="multiplicative">Whether to use the multiplicative update rather than alternating least squares.</param>
    public static (double[,] W, double[,] H, double Residual) NonNegativeFactors(
        double[,] a,
        int k,
        Random random,
        int replicates = 1,
        int maxIterations = 100,
        double tolerance = 1e-4,
        bool multiplicative = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(random);
        int n = a.GetLength(0);
        int p = a.GetLength(1);
        if (k < 1 || k > Math.Min(n, p))
        {
            throw new ArgumentException(
                "The number of factors must be between one and the smaller side of the matrix.", nameof(k));
        }

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                if (a[r, c] < 0 || double.IsNaN(a[r, c]))
                {
                    throw new ArgumentException(
                        "A non-negative factorization needs a matrix with no negative or missing entries.",
                        nameof(a));
                }
            }
        }

        double[,] bestW = new double[n, k];
        double[,] bestH = new double[k, p];
        double bestResidual = double.PositiveInfinity;

        for (int attempt = 0; attempt < Math.Max(1, replicates); attempt++)
        {
            var w = new double[n, k];
            var h = new double[k, p];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < k; c++)
                {
                    w[r, c] = random.NextDouble();
                }
            }

            for (int r = 0; r < k; r++)
            {
                for (int c = 0; c < p; c++)
                {
                    h[r, c] = random.NextDouble();
                }
            }

            double previous = double.PositiveInfinity;
            for (int pass = 0; pass < maxIterations; pass++)
            {
                if (multiplicative)
                {
                    MultiplyUpdate(a, w, h);
                }
                else
                {
                    h = ClampedSolve(w, a);
                    w = Transpose(ClampedSolve(Transpose(h), Transpose(a)));
                }

                double residual = Discrepancy(a, w, h);

                // The first pass has nothing to compare against. Writing the test without saying so
                // reads as harmless — until the starting value is infinite, when the tolerance scaled
                // by it is infinite too and the "has it settled" question answers yes immediately.
                if (double.IsFinite(previous) && Math.Abs(previous - residual) <= tolerance * Math.Max(1, previous))
                {
                    previous = residual;
                    break;
                }

                previous = residual;
            }

            if (previous < bestResidual)
            {
                bestResidual = previous;
                bestW = w;
                bestH = h;
            }
        }

        // The scale is free — doubling one factor and halving the other changes nothing — so each row
        // of H is normalized and the scale pushed into W, which is what makes two runs comparable.
        for (int i = 0; i < k; i++)
        {
            double norm = 0;
            for (int c = 0; c < p; c++)
            {
                norm += bestH[i, c] * bestH[i, c];
            }

            norm = Math.Sqrt(norm);
            if (!(norm > 0))
            {
                continue;
            }

            for (int c = 0; c < p; c++)
            {
                bestH[i, c] /= norm;
            }

            for (int r = 0; r < n; r++)
            {
                bestW[r, i] *= norm;
            }
        }

        return (bestW, bestH, bestResidual);
    }

    /// <summary>Which rotation of a loading matrix to look for.</summary>
    public enum Rotation
    {
        /// <summary>Orthogonal, maximizing the variance of the squared loadings within each column.</summary>
        Varimax,

        /// <summary>Orthogonal, maximizing it within each row instead.</summary>
        Quartimax,

        /// <summary>Orthogonal, halfway between the two.</summary>
        Equamax,

        /// <summary>Orthogonal, with the trade-off given as a number.</summary>
        Orthomax,

        /// <summary>Oblique: a varimax rotation, then relaxed towards a raised-power target.</summary>
        Promax,

        /// <summary>Towards a target matrix the caller supplies.</summary>
        Procrustes,
    }

    /// <summary>
    /// Rotates loadings so that each variable loads heavily on few components, which is what makes them
    /// interpretable without changing the space they span.
    /// </summary>
    /// <returns>The rotated loadings and the rotation that produced them.</returns>
    public static (double[,] Rotated, double[,] Transform) Rotate(
        double[,] loadings,
        Rotation rotation,
        double coefficient = 1,
        double power = 4,
        double[,]? target = null,
        bool normalize = true,
        int maxIterations = 250,
        double tolerance = 1e-8)
    {
        ArgumentNullException.ThrowIfNull(loadings);
        int p = loadings.GetLength(0);
        int k = loadings.GetLength(1);

        if (rotation == Rotation.Procrustes)
        {
            double[,] wanted = target
                ?? throw new ArgumentException("A Procrustes rotation needs a target.", nameof(target));
            if (wanted.GetLength(0) != p || wanted.GetLength(1) != k)
            {
                throw new ArgumentException("The target must be the same size as the loadings.", nameof(target));
            }

            // The oblique Procrustes rotation is a least-squares problem per column, not an orthogonal
            // one: each column of the transform is whatever combination of the loadings comes closest
            // to the corresponding target column.
            double[,] cross = AtA(loadings);
            double[,] inverse = Inverse(cross);
            var transform = new double[k, k];
            for (int c = 0; c < k; c++)
            {
                for (int i = 0; i < k; i++)
                {
                    double value = 0;
                    for (int j = 0; j < k; j++)
                    {
                        double partial = 0;
                        for (int r = 0; r < p; r++)
                        {
                            partial += loadings[r, j] * wanted[r, c];
                        }

                        value += inverse[i, j] * partial;
                    }

                    transform[i, c] = value;
                }
            }

            NormalizeColumns(transform);
            return (Multiply(loadings, transform), transform);
        }

        double gamma = rotation switch
        {
            Rotation.Varimax => 1,
            Rotation.Quartimax => 0,
            Rotation.Equamax => k / 2.0,
            Rotation.Orthomax => coefficient,
            _ => 1,
        };

        // Kaiser normalization scales each variable to unit length before rotating, so a variable with
        // large loadings does not decide the rotation for the rest, and the scale is put back after.
        var scales = new double[p];
        var working = new double[p, k];
        for (int r = 0; r < p; r++)
        {
            double norm = 0;
            for (int c = 0; c < k; c++)
            {
                norm += loadings[r, c] * loadings[r, c];
            }

            scales[r] = normalize && norm > 0 ? Math.Sqrt(norm) : 1;
            for (int c = 0; c < k; c++)
            {
                working[r, c] = loadings[r, c] / scales[r];
            }
        }

        double[,] orthogonal = Orthomax(working, gamma, maxIterations, tolerance);
        double[,] rotated = Multiply(working, orthogonal);

        if (rotation == Rotation.Promax)
        {
            // Promax relaxes the orthogonal solution towards a target formed by raising each loading to
            // a power, which drives the small ones towards zero far faster than the large ones and so
            // asks the rotation to become oblique only where it pays.
            var wanted = new double[p, k];
            for (int r = 0; r < p; r++)
            {
                for (int c = 0; c < k; c++)
                {
                    wanted[r, c] = Math.Sign(rotated[r, c]) * Math.Pow(Math.Abs(rotated[r, c]), power);
                }
            }

            (double[,] oblique, double[,] second) = Rotate(
                rotated, Rotation.Procrustes, target: wanted, normalize: false);
            rotated = oblique;
            orthogonal = Multiply(orthogonal, second);
        }

        var answer = new double[p, k];
        for (int r = 0; r < p; r++)
        {
            for (int c = 0; c < k; c++)
            {
                answer[r, c] = rotated[r, c] * scales[r];
            }
        }

        return (answer, orthogonal);
    }

    private static double[,] Orthomax(double[,] loadings, double gamma, int maxIterations, double tolerance)
    {
        int p = loadings.GetLength(0);
        int k = loadings.GetLength(1);
        var rotation = new double[k, k];
        for (int i = 0; i < k; i++)
        {
            rotation[i, i] = 1;
        }

        if (k < 2)
        {
            return rotation;
        }

        double previous = 0;
        for (int pass = 0; pass < maxIterations; pass++)
        {
            double[,] rotated = Multiply(loadings, rotation);

            // The gradient of the orthomax criterion, written the standard way: each loading cubed,
            // less that loading times gamma over p times its column's mean square.
            var gradient = new double[p, k];
            for (int c = 0; c < k; c++)
            {
                double meanSquare = 0;
                for (int r = 0; r < p; r++)
                {
                    meanSquare += rotated[r, c] * rotated[r, c];
                }

                meanSquare = gamma * meanSquare / p;
                for (int r = 0; r < p; r++)
                {
                    gradient[r, c] = (rotated[r, c] * rotated[r, c] * rotated[r, c]) - (rotated[r, c] * meanSquare);
                }
            }

            var cross = new double[k, k];
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    double value = 0;
                    for (int r = 0; r < p; r++)
                    {
                        value += loadings[r, i] * gradient[r, j];
                    }

                    cross[i, j] = value;
                }
            }

            Svd svd = Svd.Factor(cross);
            var updated = new double[k, k];
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    double value = 0;
                    for (int m = 0; m < svd.Values.Length; m++)
                    {
                        value += svd.U[i, m] * svd.V[j, m];
                    }

                    updated[i, j] = value;
                }
            }

            rotation = updated;
            double criterion = 0;
            foreach (double value in svd.Values)
            {
                criterion += value;
            }

            if (Math.Abs(criterion - previous) < tolerance * Math.Max(1, criterion))
            {
                break;
            }

            previous = criterion;
        }

        return rotation;
    }

    private static void MultiplyUpdate(double[,] a, double[,] w, double[,] h)
    {
        int n = a.GetLength(0);
        int p = a.GetLength(1);
        int k = h.GetLength(0);
        double[,] fitted = Multiply(w, h);

        for (int i = 0; i < k; i++)
        {
            for (int c = 0; c < p; c++)
            {
                double numerator = 0;
                double denominator = 0;
                for (int r = 0; r < n; r++)
                {
                    numerator += w[r, i] * a[r, c];
                    denominator += w[r, i] * fitted[r, c];
                }

                h[i, c] *= numerator / Math.Max(denominator, 1e-12);
            }
        }

        fitted = Multiply(w, h);
        for (int r = 0; r < n; r++)
        {
            for (int i = 0; i < k; i++)
            {
                double numerator = 0;
                double denominator = 0;
                for (int c = 0; c < p; c++)
                {
                    numerator += a[r, c] * h[i, c];
                    denominator += fitted[r, c] * h[i, c];
                }

                w[r, i] *= numerator / Math.Max(denominator, 1e-12);
            }
        }
    }

    private static double[,] ClampedSolve(double[,] basis, double[,] targets)
    {
        // Alternating least squares solves the unconstrained problem and then clamps, which is not the
        // same as solving the constrained one but is what MathWorks' 'als' does and converges to the
        // same place on data where the answer is interior.
        int k = basis.GetLength(1);
        int p = targets.GetLength(1);
        double[,] cross = AtA(basis);
        for (int i = 0; i < k; i++)
        {
            cross[i, i] += 1e-10;
        }

        double[,] inverse = Inverse(cross);
        var answer = new double[k, p];
        for (int c = 0; c < p; c++)
        {
            var projected = new double[k];
            for (int i = 0; i < k; i++)
            {
                double value = 0;
                for (int r = 0; r < basis.GetLength(0); r++)
                {
                    value += basis[r, i] * targets[r, c];
                }

                projected[i] = value;
            }

            for (int i = 0; i < k; i++)
            {
                double value = 0;
                for (int j = 0; j < k; j++)
                {
                    value += inverse[i, j] * projected[j];
                }

                answer[i, c] = Math.Max(value, 0);
            }
        }

        return answer;
    }

    private static double Discrepancy(double[,] a, double[,] w, double[,] h)
    {
        double[,] fitted = Multiply(w, h);
        double total = 0;
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                double gap = a[r, c] - fitted[r, c];
                total += gap * gap;
            }
        }

        return Math.Sqrt(total / (a.GetLength(0) * (double)a.GetLength(1)));
    }

    private static void NormalizeColumns(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        for (int c = 0; c < columns; c++)
        {
            double norm = 0;
            for (int r = 0; r < rows; r++)
            {
                norm += matrix[r, c] * matrix[r, c];
            }

            norm = Math.Sqrt(norm);
            if (!(norm > 0))
            {
                continue;
            }

            for (int r = 0; r < rows; r++)
            {
                matrix[r, c] /= norm;
            }
        }
    }

    private static double[] Ones(int n)
    {
        var values = new double[n];
        Array.Fill(values, 1);
        return values;
    }

    private static double[,] Symmetrized(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var symmetric = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                symmetric[r, c] = (matrix[r, c] + matrix[c, r]) / 2;
            }
        }

        return symmetric;
    }

    internal static double[,] AtA(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var cross = new double[columns, columns];
        for (int i = 0; i < columns; i++)
        {
            for (int j = i; j < columns; j++)
            {
                double value = 0;
                for (int r = 0; r < rows; r++)
                {
                    value += matrix[r, i] * matrix[r, j];
                }

                cross[i, j] = value;
                cross[j, i] = value;
            }
        }

        return cross;
    }

    internal static double[,] Multiply(double[,] left, double[,] right)
    {
        int rows = left.GetLength(0);
        int inner = left.GetLength(1);
        int columns = right.GetLength(1);
        if (right.GetLength(0) != inner)
        {
            throw new ArgumentException("The two matrices do not share an inner dimension.", nameof(right));
        }

        var product = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int k = 0; k < inner; k++)
            {
                double value = left[r, k];
                if (value == 0)
                {
                    continue;
                }

                for (int c = 0; c < columns; c++)
                {
                    product[r, c] += value * right[k, c];
                }
            }
        }

        return product;
    }

    internal static double[,] Transpose(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var transposed = new double[columns, rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                transposed[c, r] = matrix[r, c];
            }
        }

        return transposed;
    }

    /// <summary>The inverse of a small square matrix, by elimination with partial pivoting.</summary>
    internal static double[,] Inverse(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n)
        {
            throw new ArgumentException("Only a square matrix has an inverse.", nameof(matrix));
        }

        var work = new double[n, 2 * n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                work[r, c] = matrix[r, c];
            }

            work[r, n + r] = 1;
        }

        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(work[r, c]) > Math.Abs(work[pivot, c]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(work[pivot, c]) < 1e-14)
            {
                throw new ArgumentException("The matrix is singular, so it has no inverse.", nameof(matrix));
            }

            if (pivot != c)
            {
                for (int k = 0; k < 2 * n; k++)
                {
                    (work[c, k], work[pivot, k]) = (work[pivot, k], work[c, k]);
                }
            }

            double lead = work[c, c];
            for (int k = 0; k < 2 * n; k++)
            {
                work[c, k] /= lead;
            }

            for (int r = 0; r < n; r++)
            {
                if (r == c || work[r, c] == 0)
                {
                    continue;
                }

                double factor = work[r, c];
                for (int k = 0; k < 2 * n; k++)
                {
                    work[r, k] -= factor * work[c, k];
                }
            }
        }

        var inverse = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                inverse[r, c] = work[r, n + c];
            }
        }

        return inverse;
    }
}
