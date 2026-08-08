using JGraph.Statistics.Multivariate;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave H, part two: what a cloud of observations looks like from the directions that matter —
/// the principal components and the four analyses built beside them, the two ways of relating one
/// configuration of points to another, a covariance outliers cannot move, the hidden Markov family,
/// and the four small names that turn labels into numbers and back.
/// </summary>
/// <remarks>
/// Everything that takes data reads one observation per row, as the rest of the toolbox does. The two
/// names that answer a matrix of loadings — <c>pca</c> and <c>rotatefactors</c> — turn each component
/// so that its largest loading is positive, because a component and its negation describe the same
/// direction and a script that compares two runs, or a test that pins a number, needs the choice to
/// be made the same way twice.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec ComponentOptions = new(
        "pca",
        [],
        ["algorithm", "centered", "economy", "numcomponents", "rows", "weights", "variableweights", "coeff0", "score0", "options"]);

    private static readonly OptionSpec ProbabilisticOptions = new(
        "ppca", [], ["w0", "options"]);

    private static readonly OptionSpec FactorizationOptions = new(
        "nnmf", [], ["algorithm", "w0", "h0", "replicates", "options", "tolfun", "tolx", "maxiter", "display"]);

    private static readonly OptionSpec RotationOptions = new(
        "rotatefactors",
        [],
        ["method", "coeff", "power", "target", "type", "normalize", "reltol", "maxit"]);

    private static readonly OptionSpec RobustCovarianceOptions = new(
        "robustcov",
        [],
        ["method", "outlierfraction", "numtrials", "biascorrection", "numoglecsteps", "reweight", "startmethod", "outlierprobability"]);

    private static readonly OptionSpec ConfusionOptions = new(
        "confusionmat", [], ["order"]);

    private static readonly OptionSpec HiddenMarkovOptions = new(
        "hmmtrain",
        [],
        ["algorithm", "symbols", "statenames", "tolerance", "maxiterations", "verbose", "pseudoemissions", "pseudotransitions"]);

    /// <summary>Registers the component, scaling, robust-covariance, Markov and label builtins.</summary>
    private static void RegisterComponentBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        void DefineSeeded(string name, Func<Random, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            DefineBoth(name, (args, wanted, line, col) => both(random, args, wanted, line, col));

        DefineBoth("pca", ComponentAnalysis);
        DefineBoth("pcacov", ComponentsOfCovariance);
        DefineBoth("pcares", ComponentResiduals);
        DefineBoth("ppca", ProbabilisticComponents);
        DefineSeeded("nnmf", NonNegativeFactorization);
        DefineBoth("rotatefactors", RotateLoadings);

        DefineBoth("cmdscale", ClassicalScaling);
        DefineBoth("procrustes", ProcrustesFit);
        DefineBoth("canoncorr", CanonicalCorrelations);
        DefineSeeded("robustcov", RobustCovarianceFit);

        DefineBoth("grp2idx", GroupToIndex);
        DefineBoth("confusionmat", ConfusionMatrix);
        Define("onehotencode", OneHotEncode);
        Define("onehotdecode", OneHotDecode);

        DefineSeeded("hmmgenerate", MarkovGenerate);
        DefineBoth("hmmdecode", MarkovDecode);
        DefineBoth("hmmviterbi", MarkovViterbi);
        DefineBoth("hmmestimate", MarkovEstimate);
        DefineBoth("hmmtrain", MarkovTrain);
    }

    // --- pca, pcacov, pcares, ppca, nnmf, rotatefactors -----------------------------------------------

    /// <summary><c>[coeff, score, latent, tsquared, explained, mu] = pca(X, …)</c>.</summary>
    private static JgsValue[] ComponentAnalysis(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = ComponentOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "pca takes the data and its options.");
        }

        double[,] data = Dense("pca", parsed.Positional[0], line, col);

        string algorithm = parsed.Word("algorithm", "svd", "svd", "eig", "als");
        if (algorithm == "als")
        {
            throw new JgsRuntimeException(line, col,
                "pca: the alternating least squares algorithm exists to handle missing values; ppca does that here "
                + "and answers the same components.");
        }

        string rows = parsed.Word("rows", "complete", "complete", "all", "pairwise");
        if (rows == "pairwise")
        {
            throw new JgsRuntimeException(line, col,
                "pca: 'pairwise' builds a covariance from whatever pairs are observed, which need not be a "
                + "covariance at all. Use 'complete' to drop incomplete rows, or ppca to model the gaps.");
        }

        if (rows == "complete")
        {
            data = WithoutIncompleteRows(data);
            if (data.GetLength(0) == 0)
            {
                throw new JgsRuntimeException(line, col, "pca: every observation has a missing value.");
            }
        }

        bool centred = parsed.Flag("centered", true);
        int? components = parsed.Named("numcomponents") is { } asked
            ? (int)Math.Round(ClusterNumber("pca", asked, line, col))
            : null;
        double[]? weights = parsed.Vector("weights");
        double[]? variableWeights = VariableWeights("pca", parsed, data.GetLength(1), line, col);

        // 'Economy' off asks for the components a rank-deficient data set does not have, padded with
        // zeros. The columns it adds carry no variance and span nothing, so they are refused by name
        // rather than fabricated.
        if (!parsed.Flag("economy", true))
        {
            throw new JgsRuntimeException(line, col,
                "pca: 'Economy' off pads the answer with components the data does not support; ask for the ones "
                + "it does, with 'NumComponents'.");
        }

        PrincipalComponents.Analysis analysis = Guarded("pca",
            () => PrincipalComponents.Analyse(data, centred, components, weights, variableWeights), line, col);

        return Outputs(wanted,
            Rectangle(analysis.Coefficients),
            Rectangle(analysis.Scores),
            ColumnOfAnswers(analysis.Latent),
            ColumnOfAnswers(analysis.TSquared),
            ColumnOfAnswers(analysis.Explained),
            RowVector(analysis.Centre));
    }

    /// <summary><c>[coeff, latent, explained] = pcacov(V)</c>.</summary>
    private static JgsValue[] ComponentsOfCovariance(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("pcacov", args, 1, line, col);
        (double[] flat, int order) = SquareMatrix("pcacov", args[0], line, col);
        (double[,] coefficients, double[] latent, double[] explained) =
            Guarded("pcacov", () => PrincipalComponents.FromCovariance(Square(flat, order)), line, col);
        return Outputs(wanted, Rectangle(coefficients), ColumnOfAnswers(latent), ColumnOfAnswers(explained));
    }

    /// <summary><c>[residuals, reconstructed] = pcares(X, ndim)</c>.</summary>
    private static JgsValue[] ComponentResiduals(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("pcares", args, 2, line, col);
        double[,] data = Dense("pcares", args[0], line, col);
        int kept = (int)Math.Round(ClusterNumber("pcares", args[1], line, col));
        (double[,] residuals, double[,] reconstructed) =
            Guarded("pcares", () => PrincipalComponents.Residuals(data, kept), line, col);
        return Outputs(wanted, Rectangle(residuals), Rectangle(reconstructed));
    }

    /// <summary><c>[coeff, score, pcvar, mu, v, S] = ppca(Y, k)</c>.</summary>
    private static JgsValue[] ProbabilisticComponents(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = ProbabilisticOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "ppca takes the data and how many components to fit.");
        }

        double[,] data = Dense("ppca", parsed.Positional[0], line, col);
        int components = (int)Math.Round(ClusterNumber("ppca", parsed.Positional[1], line, col));
        PrincipalComponents.Probabilistic fit = Guarded("ppca",
            () => PrincipalComponents.Probabilistically(data, components), line, col);

        JgsValue diagnostics = Structure(
            ("Recon", Rectangle(Reconstruct(fit))),
            ("W", Rectangle(fit.Coefficients)),
            ("v", JgsValue.Number(fit.Noise)),
            ("NumIter", JgsValue.Number(fit.Iterations)),
            ("RMSResid", JgsValue.Number(Math.Sqrt(fit.Noise))),
            ("nloglk", JgsValue.Number(-fit.LogLikelihood)));

        return Outputs(wanted,
            Rectangle(fit.Coefficients),
            Rectangle(fit.Scores),
            ColumnOfAnswers(fit.Variances),
            RowVector(fit.Centre),
            JgsValue.Number(fit.Noise),
            diagnostics);
    }

    /// <summary><c>[W, H, D] = nnmf(A, k, …)</c>.</summary>
    private static JgsValue[] NonNegativeFactorization(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = FactorizationOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "nnmf takes the matrix and how many factors to find.");
        }

        double[,] a = Dense("nnmf", parsed.Positional[0], line, col);
        int k = (int)Math.Round(ClusterNumber("nnmf", parsed.Positional[1], line, col));
        string algorithm = parsed.Word("algorithm", "als", "als", "mult");
        (double[,] w, double[,] h, double residual) = Guarded("nnmf",
            () => PrincipalComponents.NonNegativeFactors(
                a,
                k,
                random,
                parsed.Whole("replicates", 1),
                parsed.Whole("maxiter", algorithm == "mult" ? 1000 : 100),
                parsed.Scalar("tolfun", 1e-4),
                algorithm == "mult"),
            line, col);

        return Outputs(wanted, Rectangle(w), Rectangle(h), JgsValue.Number(residual));
    }

    /// <summary><c>[B, T] = rotatefactors(A, …)</c>.</summary>
    private static JgsValue[] RotateLoadings(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = RotationOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "rotatefactors takes the loadings and its options.");
        }

        double[,] loadings = Dense("rotatefactors", parsed.Positional[0], line, col);
        string method = parsed.Word(
            "method", "varimax", "varimax", "quartimax", "equamax", "parsimax", "orthomax", "promax", "procrustes");

        PrincipalComponents.Rotation rotation = method switch
        {
            "quartimax" => PrincipalComponents.Rotation.Quartimax,
            "equamax" => PrincipalComponents.Rotation.Equamax,
            "orthomax" => PrincipalComponents.Rotation.Orthomax,
            "parsimax" => PrincipalComponents.Rotation.Orthomax,
            "promax" => PrincipalComponents.Rotation.Promax,
            "procrustes" => PrincipalComponents.Rotation.Procrustes,
            _ => PrincipalComponents.Rotation.Varimax,
        };

        // Parsimax is orthomax with the trade-off fixed by the shape of the loadings rather than given,
        // which is the one place the coefficient is not the caller's to choose.
        int p = loadings.GetLength(0);
        int k = loadings.GetLength(1);
        double coefficient = method == "parsimax"
            ? p * (k - 1.0) / (p + k - 2.0)
            : parsed.Scalar("coeff", 1);

        double[,]? target = parsed.Named("target") is { } given ? Dense("rotatefactors", given, line, col) : null;
        if (rotation == PrincipalComponents.Rotation.Procrustes && target is null)
        {
            throw new JgsRuntimeException(line, col, "rotatefactors: a Procrustes rotation needs a 'Target'.");
        }

        if (parsed.Named("type") is { } type
            && !string.Equals(ClusterWord("rotatefactors", type, line, col), "oblique", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                "rotatefactors: an orthogonal Procrustes rotation is not supported; the oblique one, which is the "
                + "default, is.");
        }

        (double[,] rotated, double[,] transform) = Guarded("rotatefactors",
            () => PrincipalComponents.Rotate(
                loadings,
                rotation,
                coefficient,
                parsed.Scalar("power", 4),
                target,
                parsed.Flag("normalize", true),
                parsed.Whole("maxit", 250),
                parsed.Scalar("reltol", 1e-8)),
            line, col);

        return Outputs(wanted, Rectangle(rotated), Rectangle(transform));
    }

    // --- cmdscale, procrustes, canoncorr, robustcov ----------------------------------------------------

    /// <summary><c>[Y, e] = cmdscale(D)</c>: coordinates that reproduce the distances.</summary>
    private static JgsValue[] ClassicalScaling(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("cmdscale", args, 1, 2, line, col);
        (double[] flat, int rows, int columns) = DenseMatrix("cmdscale", args[0], line, col);

        double[,] square;
        if (rows == columns && rows > 1)
        {
            square = new double[rows, columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    square[r, c] = flat[r + (c * rows)];
                }
            }
        }
        else
        {
            square = Guarded("cmdscale", () => JGraph.Statistics.Cluster.Distances.SquareForm(flat), line, col);
        }

        (double[,] coordinates, double[] values) =
            Guarded("cmdscale", () => Scaling.Classical(square), line, col);

        if (args.Count > 1)
        {
            int wantedDimensions = (int)Math.Round(ClusterNumber("cmdscale", args[1], line, col));
            if (wantedDimensions < 1 || wantedDimensions > coordinates.GetLength(1))
            {
                throw new JgsRuntimeException(line, col,
                    $"cmdscale: the distances support {coordinates.GetLength(1)} dimensions, not "
                    + $"{wantedDimensions}.");
            }

            var trimmed = new double[coordinates.GetLength(0), wantedDimensions];
            for (int r = 0; r < trimmed.GetLength(0); r++)
            {
                for (int c = 0; c < wantedDimensions; c++)
                {
                    trimmed[r, c] = coordinates[r, c];
                }
            }

            coordinates = trimmed;
        }

        return Outputs(wanted, Rectangle(coordinates), ColumnOfAnswers(values));
    }

    /// <summary><c>[d, Z, transform] = procrustes(X, Y, …)</c>.</summary>
    private static JgsValue[] ProcrustesFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        var options = new OptionSpec("procrustes", [], ["scaling", "reflection"]);
        ParsedArgs parsed = options.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "procrustes takes the target and the configuration to move.");
        }

        double[,] x = Dense("procrustes", parsed.Positional[0], line, col);
        double[,] y = Dense("procrustes", parsed.Positional[1], line, col);
        bool scaling = parsed.Flag("scaling", true);

        // 'Reflection' takes three values, not two: true, false, and 'best' — which is the default and
        // means "take whichever fits better", so it is the absence of a constraint rather than one.
        bool? reflection = null;
        if (parsed.Named("reflection") is { } given)
        {
            reflection = given.Type == JgsType.String
                ? string.Equals(given.AsString, "best", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : throw new JgsRuntimeException(line, col,
                        "procrustes: 'Reflection' is true, false, or 'best'.")
                : given.IsTruthy;
        }

        (double dissimilarity, double[,] transformed, Scaling.Transformation transform) =
            Guarded("procrustes", () => Scaling.Procrustes(x, y, scaling, reflection), line, col);

        JgsValue record = Structure(
            ("T", Rectangle(transform.Rotation)),
            ("b", JgsValue.Number(transform.Scale)),
            ("c", Rectangle(RepeatRow(transform.Translation, x.GetLength(0)))));

        return Outputs(wanted, JgsValue.Number(dissimilarity), Rectangle(transformed), record);
    }

    /// <summary><c>[A, B, r, U, V, stats] = canoncorr(X, Y)</c>.</summary>
    private static JgsValue[] CanonicalCorrelations(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("canoncorr", args, 2, line, col);
        double[,] x = Dense("canoncorr", args[0], line, col);
        double[,] y = Dense("canoncorr", args[1], line, col);
        Scaling.Canonical fit = Guarded("canoncorr", () => Scaling.CanonicalCorrelation(x, y), line, col);

        JgsValue stats = Structure(
            ("Wilks", RowVector(fit.Wilks)),
            ("df1", RowVector(fit.Df)),
            ("df2", RowVector(SecondDegreesOfFreedom(fit, x.GetLength(0), x.GetLength(1), y.GetLength(1)))),
            ("F", RowVector(ApproximateF(fit, x.GetLength(0), x.GetLength(1), y.GetLength(1)))),
            ("pF", RowVector(fit.P)),
            ("chisq", RowVector(fit.ChiSquared)),
            ("pChisq", RowVector(fit.P)));

        return Outputs(wanted,
            Rectangle(fit.A), Rectangle(fit.B), RowVector(fit.R), Rectangle(fit.U), Rectangle(fit.V), stats);
    }

    /// <summary><c>[sig, mu, mah, outliers, s] = robustcov(X, …)</c>.</summary>
    private static JgsValue[] RobustCovarianceFit(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = RobustCovarianceOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "robustcov takes the data and its options.");
        }

        double[,] data = Dense("robustcov", parsed.Positional[0], line, col);
        string method = parsed.Word("method", "fmcd", "fmcd", "ogk", "olivehawkins");
        RobustCovarianceMethod rule = method switch
        {
            "ogk" => RobustCovarianceMethod.Orthogonalized,
            "olivehawkins" => RobustCovarianceMethod.OliveHawkins,
            _ => RobustCovarianceMethod.MinimumDeterminant,
        };

        RobustCovariance.Estimate estimate = Guarded("robustcov",
            () => RobustCovariance.Fit(
                data,
                rule,
                random,
                parsed.Scalar("outlierfraction", 0.5),
                parsed.Whole("numtrials", 500),
                parsed.Scalar("outlierprobability", 0.025)),
            line, col);

        JgsValue diagnostics = Structure(
            ("Method", JgsValue.Str(method)),
            ("OutlierFraction", JgsValue.Number(parsed.Scalar("outlierfraction", 0.5))),
            ("NumOGKIter", JgsValue.Number(0)),
            ("NumTrials", JgsValue.Number(parsed.Whole("numtrials", 500))),
            ("BiasCorrection", JgsValue.Bool(true)),
            ("cutoff", JgsValue.Number(estimate.Cutoff)),
            ("s", ColumnOfAnswers(Shifted(estimate.Subset))));

        return Outputs(wanted,
            Rectangle(estimate.Covariance),
            RowVector(estimate.Centre),
            ColumnOfAnswers(estimate.Distances),
            ColumnOfFlags(estimate.Outliers),
            diagnostics);
    }

    // --- grp2idx, confusionmat, onehotencode, onehotdecode --------------------------------------------

    /// <summary><c>[G, GN, GL] = grp2idx(S)</c>: a grouping variable as numbers and its level names.</summary>
    private static JgsValue[] GroupToIndex(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("grp2idx", args, 1, line, col);
        (int[] index, string[] names) = GroupIndex("grp2idx", args[0], line, col);

        var numbers = new double[index.Length];
        for (int i = 0; i < index.Length; i++)
        {
            // A missing value belongs to no group, and MathWorks answers NaN there rather than putting
            // it in a group of its own.
            numbers[i] = index[i] < 0 ? double.NaN : index[i] + 1;
        }

        return Outputs(wanted, ColumnOfAnswers(numbers), TextColumn(names), TextColumn(names));
    }

    /// <summary><c>[C, order] = confusionmat(known, predicted)</c>.</summary>
    private static JgsValue[] ConfusionMatrix(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = ConfusionOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "confusionmat takes the known labels and the predicted ones.");
        }

        // Both label vectors are levelled together, so a class that appears in only one of them still
        // gets its own row and column — which is the whole point of the matrix, since a class the
        // model never predicts is exactly what one is looking for.
        (string[] known, string[] predicted) = LabelPair("confusionmat", parsed, line, col);
        if (known.Length != predicted.Length)
        {
            throw new JgsRuntimeException(line, col,
                "confusionmat: there must be one predicted label for each known one.");
        }

        List<string> order = parsed.Named("order") is { } given
            ? [.. TextElements("confusionmat", given, line, col)]
            : DistinctLabels(known, predicted);

        var counts = new double[order.Count, order.Count];
        for (int i = 0; i < known.Length; i++)
        {
            int row = order.IndexOf(known[i]);
            int column = order.IndexOf(predicted[i]);
            if (row >= 0 && column >= 0)
            {
                counts[row, column]++;
            }
        }

        var cells = new JgsValue[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            cells[i] = JgsValue.Str(order[i]);
        }

        JgsValue labels = JgsValue.Cell(cells);
        labels.ReshapeDims([order.Count, 1]);
        return Outputs(wanted, Rectangle(counts), labels);
    }

    /// <summary><c>onehotencode(labels, dim)</c>: one column per class, a single one in each row.</summary>
    private static JgsValue OneHotEncode(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("onehotencode", args, 1, 3, line, col);
        (int[] index, string[] names) = GroupIndex("onehotencode", args[0], line, col);
        int dimension = args.Count > 1 ? (int)Math.Round(ClusterNumber("onehotencode", args[1], line, col)) : 2;
        if (dimension is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                "onehotencode: the classes go along dimension one or two.");
        }

        int n = index.Length;
        int k = names.Length;
        var encoded = dimension == 2 ? new double[n, k] : new double[k, n];
        for (int i = 0; i < n; i++)
        {
            if (index[i] < 0)
            {
                // A missing label encodes as a row of NaN rather than a row of zeros, so that it cannot
                // be mistaken for a class none of the indicators fired for.
                for (int c = 0; c < k; c++)
                {
                    if (dimension == 2)
                    {
                        encoded[i, c] = double.NaN;
                    }
                    else
                    {
                        encoded[c, i] = double.NaN;
                    }
                }

                continue;
            }

            if (dimension == 2)
            {
                encoded[i, index[i]] = 1;
            }
            else
            {
                encoded[index[i], i] = 1;
            }
        }

        return Rectangle(encoded);
    }

    /// <summary><c>onehotdecode(A, classes, dim)</c>: the class each row's largest entry names.</summary>
    private static JgsValue OneHotDecode(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("onehotdecode", args, 2, 4, line, col);
        double[,] encoded = Dense("onehotdecode", args[0], line, col);
        string[] classes = TextElements("onehotdecode", args[1], line, col);
        int dimension = args.Count > 2 ? (int)Math.Round(ClusterNumber("onehotdecode", args[2], line, col)) : 2;
        if (dimension is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                "onehotdecode: the classes lie along dimension one or two.");
        }

        int along = dimension == 2 ? encoded.GetLength(1) : encoded.GetLength(0);
        int count = dimension == 2 ? encoded.GetLength(0) : encoded.GetLength(1);
        if (along != classes.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"onehotdecode: there are {along} indicators and {classes.Length} class names.");
        }

        var decoded = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            int best = 0;
            double highest = double.NegativeInfinity;
            for (int c = 0; c < along; c++)
            {
                double value = dimension == 2 ? encoded[i, c] : encoded[c, i];
                if (value > highest)
                {
                    highest = value;
                    best = c;
                }
            }

            decoded[i] = JgsValue.Str(classes[best]);
        }

        JgsValue answer = JgsValue.Cell(decoded);
        answer.ReshapeDims(dimension == 2 ? [count, 1] : [1, count]);
        return answer;
    }

    // --- the hidden Markov family ----------------------------------------------------------------------

    /// <summary><c>[seq, states] = hmmgenerate(len, TRANS, EMIS, …)</c>.</summary>
    private static JgsValue[] MarkovGenerate(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = HiddenMarkovOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "hmmgenerate takes the length, the transition matrix and the emission matrix.");
        }

        int length = (int)Math.Round(ClusterNumber("hmmgenerate", parsed.Positional[0], line, col));
        double[,] transition = Dense("hmmgenerate", parsed.Positional[1], line, col);
        double[,] emission = Dense("hmmgenerate", parsed.Positional[2], line, col);
        RefuseTrainingOptions("hmmgenerate", parsed, line, col);

        (int[] sequence, int[] states) = Guarded("hmmgenerate",
            () => HiddenMarkov.Generate(length, transition, emission, random), line, col);

        JgsValue symbols = parsed.Named("symbols") is { } names
            ? NamedSequence("hmmgenerate", names, sequence, emission.GetLength(1), line, col)
            : RowVector(Shifted(sequence));
        JgsValue path = parsed.Named("statenames") is { } stateNames
            ? NamedSequence("hmmgenerate", stateNames, states, transition.GetLength(0), line, col)
            : RowVector(Shifted(states));

        return Outputs(wanted, symbols, path);
    }

    /// <summary><c>[pstates, logpseq, fs, bs, s] = hmmdecode(seq, TRANS, EMIS)</c>.</summary>
    private static JgsValue[] MarkovDecode(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = HiddenMarkovOptions.Parse(args, 3, line, col);
        (int[] sequence, double[,] transition, double[,] emission) =
            MarkovArguments("hmmdecode", parsed, line, col);

        HiddenMarkov.Decoding decoded = Guarded("hmmdecode",
            () => HiddenMarkov.Decode(sequence, transition, emission), line, col);

        return Outputs(wanted,
            Rectangle(decoded.Probabilities),
            JgsValue.Number(decoded.LogLikelihood),
            Rectangle(decoded.Forward),
            Rectangle(decoded.Backward),
            RowVector(decoded.Scale));
    }

    /// <summary><c>[states, logp] = hmmviterbi(seq, TRANS, EMIS, …)</c>.</summary>
    private static JgsValue[] MarkovViterbi(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = HiddenMarkovOptions.Parse(args, 3, line, col);
        (int[] sequence, double[,] transition, double[,] emission) =
            MarkovArguments("hmmviterbi", parsed, line, col);

        (int[] path, double logProbability) = Guarded("hmmviterbi",
            () => HiddenMarkov.Viterbi(sequence, transition, emission), line, col);

        JgsValue states = parsed.Named("statenames") is { } names
            ? NamedSequence("hmmviterbi", names, path, transition.GetLength(0), line, col)
            : RowVector(Shifted(path));
        return Outputs(wanted, states, JgsValue.Number(logProbability));
    }

    /// <summary><c>[TRANS, EMIS] = hmmestimate(seq, states, …)</c>: counting, when the path is known.</summary>
    private static JgsValue[] MarkovEstimate(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = HiddenMarkovOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "hmmestimate takes the observed sequence and the states that produced it.");
        }

        (int[] sequence, int symbolCount) = SymbolSequence(
            "hmmestimate", parsed.Positional[0], parsed.Named("symbols"), line, col);
        (int[] states, int stateCount) = SymbolSequence(
            "hmmestimate", parsed.Positional[1], parsed.Named("statenames"), line, col);

        double[,]? pseudoTransitions = parsed.Named("pseudotransitions") is { } pt
            ? Dense("hmmestimate", pt, line, col)
            : null;
        double[,]? pseudoEmissions = parsed.Named("pseudoemissions") is { } pe
            ? Dense("hmmestimate", pe, line, col)
            : null;

        // Pseudocounts widen the model to whatever they describe, which is how a state or a symbol that
        // the observed run never showed still gets a row.
        stateCount = Math.Max(stateCount, pseudoTransitions?.GetLength(0) ?? 0);
        symbolCount = Math.Max(symbolCount, pseudoEmissions?.GetLength(1) ?? 0);

        (double[,] transition, double[,] emission) = Guarded("hmmestimate",
            () => HiddenMarkov.EstimateFromStates(
                sequence, states, stateCount, symbolCount, pseudoTransitions, pseudoEmissions),
            line, col);

        return Outputs(wanted, Rectangle(transition), Rectangle(emission));
    }

    /// <summary><c>[TRANS, EMIS] = hmmtrain(seqs, guessTR, guessE, …)</c>: Baum-Welch.</summary>
    private static JgsValue[] MarkovTrain(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = HiddenMarkovOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "hmmtrain takes the sequences and the two matrices to start from.");
        }

        double[,] guessTransition = Dense("hmmtrain", parsed.Positional[1], line, col);
        double[,] guessEmission = Dense("hmmtrain", parsed.Positional[2], line, col);
        int symbols = guessEmission.GetLength(1);

        var sequences = new List<int[]>();
        if (parsed.Positional[0].Type == JgsType.Cell)
        {
            foreach (JgsValue element in parsed.Positional[0].AsCell)
            {
                sequences.Add(SymbolSequence("hmmtrain", element, parsed.Named("symbols"), line, col).Sequence);
            }
        }
        else
        {
            // A matrix of sequences is one per row, which is how MathWorks reads a rectangular argument;
            // a single vector is one sequence, and both go through the same reader.
            (double[] flat, int rows, int columns) = DenseMatrix("hmmtrain", parsed.Positional[0], line, col);
            if (rows == 1 || columns == 1)
            {
                sequences.Add(SymbolSequence("hmmtrain", parsed.Positional[0], parsed.Named("symbols"), line, col)
                    .Sequence);
            }
            else
            {
                for (int r = 0; r < rows; r++)
                {
                    var row = new int[columns];
                    for (int c = 0; c < columns; c++)
                    {
                        row[c] = (int)Math.Round(flat[r + (c * rows)]) - 1;
                    }

                    sequences.Add(row);
                }
            }
        }

        string algorithm = parsed.Word("algorithm", "baumwelch", "baumwelch", "viterbi");
        if (algorithm == "viterbi")
        {
            throw new JgsRuntimeException(line, col,
                "hmmtrain: the Viterbi training algorithm assigns each observation to its single most likely "
                + "state, which is a different estimator; Baum-Welch, which weights every path, is supported.");
        }

        double[,]? pseudoTransitions = parsed.Named("pseudotransitions") is { } pt
            ? Dense("hmmtrain", pt, line, col)
            : null;
        double[,]? pseudoEmissions = parsed.Named("pseudoemissions") is { } pe
            ? Dense("hmmtrain", pe, line, col)
            : null;

        (double[,] transition, double[,] emission, double logLikelihood, int iterations, bool converged) =
            Guarded("hmmtrain",
                () => HiddenMarkov.Train(
                    sequences,
                    guessTransition,
                    guessEmission,
                    parsed.Whole("maxiterations", 500),
                    parsed.Scalar("tolerance", 1e-6),
                    pseudoTransitions,
                    pseudoEmissions),
                line, col);

        _ = symbols;
        return Outputs(wanted,
            Rectangle(transition),
            Rectangle(emission),
            JgsValue.Number(logLikelihood),
            JgsValue.Number(iterations),
            JgsValue.Bool(converged));
    }

    // --- shared helpers ----------------------------------------------------------------------------------

    /// <summary>A numeric argument read as a two-dimensional matrix.</summary>
    private static double[,] Dense(string name, JgsValue value, int line, int col)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, value, line, col);
        var matrix = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                matrix[r, c] = flat[r + (c * rows)];
            }
        }

        return matrix;
    }

    private static double[,] WithoutIncompleteRows(double[,] data)
    {
        int n = data.GetLength(0);
        int p = data.GetLength(1);
        var keep = new List<int>(n);
        for (int r = 0; r < n; r++)
        {
            bool complete = true;
            for (int c = 0; c < p && complete; c++)
            {
                complete = !double.IsNaN(data[r, c]);
            }

            if (complete)
            {
                keep.Add(r);
            }
        }

        if (keep.Count == n)
        {
            return data;
        }

        var trimmed = new double[keep.Count, p];
        for (int i = 0; i < keep.Count; i++)
        {
            for (int c = 0; c < p; c++)
            {
                trimmed[i, c] = data[keep[i], c];
            }
        }

        return trimmed;
    }

    private static double[]? VariableWeights(string name, ParsedArgs parsed, int width, int line, int col)
    {
        if (parsed.Named("variableweights") is not { } given)
        {
            return null;
        }

        // 'variance' asks for each variable to be weighted by the reciprocal of its own, which is the
        // same as analysing the correlation matrix rather than the covariance.
        if (given.Type == JgsType.String)
        {
            if (!string.Equals(given.AsString, "variance", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'VariableWeights' is a weight for each variable, or the word 'variance'.");
            }

            return null;
        }

        double[] weights = FlattenColumnMajor(name, given, line, col);
        if (weights.Length != width)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: there must be one variable weight for each column.");
        }

        return weights;
    }

    private static double[,] Reconstruct(PrincipalComponents.Probabilistic fit)
    {
        int n = fit.Scores.GetLength(0);
        int k = fit.Scores.GetLength(1);
        int p = fit.Coefficients.GetLength(0);
        var reconstructed = new double[n, p];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < p; c++)
            {
                double value = fit.Centre[c];
                for (int i = 0; i < k; i++)
                {
                    value += fit.Scores[r, i] * fit.Coefficients[c, i];
                }

                reconstructed[r, c] = value;
            }
        }

        return reconstructed;
    }

    private static double[,] RepeatRow(double[] row, int times)
    {
        var matrix = new double[times, row.Length];
        for (int r = 0; r < times; r++)
        {
            for (int c = 0; c < row.Length; c++)
            {
                matrix[r, c] = row[c];
            }
        }

        return matrix;
    }

    /// <summary>Rao's approximate second degrees of freedom for the canonical correlation test.</summary>
    private static double[] SecondDegreesOfFreedom(Scaling.Canonical fit, int n, int p, int q)
    {
        var df = new double[fit.R.Length];
        for (int k = 0; k < df.Length; k++)
        {
            double a = p - k;
            double b = q - k;
            double s = a * a * b * b > 4 ? Math.Sqrt(((a * a * b * b) - 4) / ((a * a) + (b * b) - 5)) : 1;
            df[k] = (s * (n - 1 - k - ((a + b + 1) / 2))) - ((a * b / 2) - 1);
        }

        return df;
    }

    /// <summary>Rao's F approximation to Wilks' lambda, one per successive test.</summary>
    private static double[] ApproximateF(Scaling.Canonical fit, int n, int p, int q)
    {
        double[] df2 = SecondDegreesOfFreedom(fit, n, p, q);
        var f = new double[fit.R.Length];
        for (int k = 0; k < f.Length; k++)
        {
            double a = p - k;
            double b = q - k;
            double s = a * a * b * b > 4 ? Math.Sqrt(((a * a * b * b) - 4) / ((a * a) + (b * b) - 5)) : 1;
            double root = Math.Pow(Math.Max(fit.Wilks[k], 0), 1 / s);
            f[k] = root > 0 ? (1 - root) / root * df2[k] / (a * b) : double.PositiveInfinity;
        }

        return f;
    }

    private static (string[] Known, string[] Predicted) LabelPair(
        string name, ParsedArgs parsed, int line, int col)
    {
        string[] Read(JgsValue value) => value.Type is JgsType.Cell or JgsType.String
            ? TextElements(name, value, line, col)
            : Formatted(FlattenColumnMajor(name, value, line, col));

        return (Read(parsed.Positional[0]), Read(parsed.Positional[1]));
    }

    private static string[] Formatted(double[] values)
    {
        var text = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            text[i] = FormatNumber(values[i]);
        }

        return text;
    }

    private static List<string> DistinctLabels(string[] known, string[] predicted)
    {
        var order = new List<string>();
        foreach (string label in known)
        {
            if (!order.Contains(label, StringComparer.Ordinal))
            {
                order.Add(label);
            }
        }

        foreach (string label in predicted)
        {
            if (!order.Contains(label, StringComparer.Ordinal))
            {
                order.Add(label);
            }
        }

        order.Sort(StringComparer.Ordinal);
        return order;
    }

    private static JgsValue TextColumn(string[] names)
    {
        var cells = new JgsValue[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            cells[i] = JgsValue.Str(names[i]);
        }

        JgsValue column = JgsValue.Cell(cells);
        column.ReshapeDims([names.Length, 1]);
        return column;
    }

    private static double[] Shifted(int[] values)
    {
        var numbers = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            numbers[i] = values[i] + 1;
        }

        return numbers;
    }

    private static (int[] Sequence, double[,] Transition, double[,] Emission) MarkovArguments(
        string name, ParsedArgs parsed, int line, int col)
    {
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes the sequence, the transition matrix and the emission matrix.");
        }

        double[,] transition = Dense(name, parsed.Positional[1], line, col);
        double[,] emission = Dense(name, parsed.Positional[2], line, col);
        (int[] sequence, _) = SymbolSequence(name, parsed.Positional[0], parsed.Named("symbols"), line, col);
        return (sequence, transition, emission);
    }

    /// <summary>
    /// A sequence read as symbol numbers from zero, whether it was written as numbers or as names.
    /// </summary>
    private static (int[] Sequence, int Count) SymbolSequence(
        string name, JgsValue value, JgsValue? alphabet, int line, int col)
    {
        if (alphabet is { } given)
        {
            string[] words = TextElements(name, given, line, col);
            string[] observed = TextElements(name, value, line, col);
            var mapped = new int[observed.Length];
            for (int i = 0; i < observed.Length; i++)
            {
                int at = Array.IndexOf(words, observed[i]);
                mapped[i] = at >= 0
                    ? at
                    : throw new JgsRuntimeException(line, col,
                        $"{name}: '{observed[i]}' is not one of the symbols given.");
            }

            return (mapped, words.Length);
        }

        double[] numbers = FlattenColumnMajor(name, value, line, col);
        var sequence = new int[numbers.Length];
        int highest = 0;
        for (int i = 0; i < numbers.Length; i++)
        {
            double raw = numbers[i];
            if (raw != Math.Floor(raw) || raw < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a symbol is a whole number of at least one.");
            }

            sequence[i] = (int)raw - 1;
            highest = Math.Max(highest, sequence[i] + 1);
        }

        return (sequence, highest);
    }

    private static JgsValue NamedSequence(
        string name, JgsValue alphabet, int[] indices, int count, int line, int col)
    {
        string[] words = TextElements(name, alphabet, line, col);
        if (words.Length != count)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: there are {count} of those in the model and {words.Length} names for them.");
        }

        var cells = new JgsValue[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            cells[i] = JgsValue.Str(words[indices[i]]);
        }

        JgsValue row = JgsValue.Cell(cells);
        row.ReshapeDims([1, indices.Length]);
        return row;
    }

    private static void RefuseTrainingOptions(string name, ParsedArgs parsed, int line, int col)
    {
        foreach (string option in (string[])["algorithm", "tolerance", "maxiterations", "verbose",
            "pseudoemissions", "pseudotransitions"])
        {
            if (parsed.Named(option) is not null)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} takes no '{option}'; that option belongs to hmmtrain.");
            }
        }
    }
}
