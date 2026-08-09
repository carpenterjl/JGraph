using System.Text;
using JGraph.Statistics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave J, the planning half: the designs of experiments whose run list is enumerable, the two
/// process-capability answers, and the options structure the iterative names take.
/// </summary>
/// <remarks>
/// Nothing here looks at data except <c>capability</c> and <c>gagerr</c>. A design is decided before
/// anything is measured, which is the whole point of one, so these names take counts and levels and
/// answer a matrix of runs.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec FractionOptions = new(
        "fracfact", [], ["maxint", "factornames"], StringPositionals: 1);

    private static readonly OptionSpec BehnkenOptions = new(
        "bbdesign", [], ["center", "blocksize", "state"]);

    private static readonly OptionSpec CompositeOptions = new(
        "ccdesign", [], ["center", "fraction", "type", "blocksize", "state"]);

    private static readonly OptionSpec GageOptions = new(
        "gagerr", [], ["printtable", "printgraph", "model", "spec", "sigma", "alpha"]);

    /// <summary>
    /// The fields <c>statset</c> publishes, with the value each takes when nothing was said. The order
    /// is the order MathWorks lists them in, because that is the order the structure prints in.
    /// </summary>
    private static readonly (string Name, JgsValue Value)[] StatsetFields =
    [
        ("Display", JgsValue.Str("off")),
        ("MaxFunEvals", JgsValue.Array([])),
        ("MaxIter", JgsValue.Array([])),
        ("TolBnd", JgsValue.Array([])),
        ("TolFun", JgsValue.Array([])),
        ("TolTypeFun", JgsValue.Array([])),
        ("TolX", JgsValue.Array([])),
        ("TolTypeX", JgsValue.Array([])),
        ("GradObj", JgsValue.Array([])),
        ("Jacobian", JgsValue.Array([])),
        ("DerivStep", JgsValue.Array([])),
        ("FunValCheck", JgsValue.Array([])),
        ("Robust", JgsValue.Array([])),
        ("RobustWgtFun", JgsValue.Array([])),
        ("WgtFun", JgsValue.Array([])),
        ("Tune", JgsValue.Array([])),
        ("UseParallel", JgsValue.Array([])),
        ("UseSubstreams", JgsValue.Array([])),
        ("Streams", JgsValue.Array([])),
        ("OutputFcn", JgsValue.Array([])),
    ];

    /// <summary>Registers the designs, the capability answers and the options structure.</summary>
    private static void RegisterDesignBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        Define("fullfact", FullFactorialDesign);
        Define("ff2n", TwoLevelDesign);
        DefineBoth("fracfact", FractionalDesign);
        Define("fracfactgen", FractionGenerators);
        DefineBoth("bbdesign", BehnkenDesign);
        DefineBoth("ccdesign", CompositeDesign);

        Define("capability", CapabilityIndices);
        DefineBoth("gagerr", (args, wanted, line, col) => GageStudy(host, args, wanted, line, col));

        // statset with nothing at all answers the whole structure, so the bare word has to call.
        env.Declare("statset", JgsValue.Function(new BuiltinFunction(
            "statset", Statset) { AutoCallsBare = true }));
        Define("statget", Statget);
    }

    // --- The designs --------------------------------------------------------------------------------

    /// <summary><c>d = fullfact(levels)</c>: every combination of the levels, first factor fastest.</summary>
    private static JgsValue FullFactorialDesign(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("fullfact", args, 1, 1, line, col);
        double[] levels = FlattenColumnMajor("fullfact", args[0], line, col);
        if (levels.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "fullfact needs at least one factor.");
        }

        var counts = new int[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            if (!(levels[i] >= 1) || levels[i] != Math.Floor(levels[i]))
            {
                throw new JgsRuntimeException(line, col,
                    "fullfact: every factor takes a whole number of levels, at least one.");
            }

            counts[i] = (int)levels[i];
        }

        return Rectangle(Guarded("fullfact", () => DesignOfExperiments.FullFactorial(counts), line, col));
    }

    /// <summary><c>d = ff2n(n)</c>: the two-level full factorial, coded zero and one.</summary>
    private static JgsValue TwoLevelDesign(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("ff2n", args, 1, 1, line, col);
        int factors = Count("ff2n", args, 0, line, col);
        return Rectangle(Guarded("ff2n", () => DesignOfExperiments.TwoLevelFullFactorial(factors), line, col));
    }

    /// <summary>
    /// <c>[X, conf] = fracfact(generators)</c>: a two-level fraction, and what each of its effects is
    /// confounded with.
    /// </summary>
    private static JgsValue[] FractionalDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "fracfact needs a set of generators.");
        }

        ParsedArgs parsed = FractionOptions.Parse(args, 1, line, col);
        string[] words = GeneratorWords("fracfact", parsed.Positional[0], line, col);
        DesignOfExperiments.Generator[] generators = Array.ConvertAll(words, word => ParseGenerator(word, line, col));

        double[,] design = Guarded("fracfact", () => DesignOfExperiments.Fraction(generators), line, col);
        if (wanted < 2)
        {
            return [Rectangle(design)];
        }

        int order = parsed.Whole("maxint", 2);
        if (order < 1)
        {
            throw new JgsRuntimeException(line, col, "fracfact: 'MaxInt' is an interaction order of at least one.");
        }

        string[] names = parsed.Named("factornames") is { } given
            ? TextElements("fracfact", given, line, col)
            : BuildNames(words.Length);

        if (names.Length != words.Length)
        {
            throw new JgsRuntimeException(line, col,
                "fracfact: 'FactorNames' needs one name per generator.");
        }

        return [Rectangle(design), ConfoundingTable(design, generators, names, order)];
    }

    /// <summary>
    /// <c>generators = fracfactgen(terms, k, R)</c>: generators for a fraction of the named factors in
    /// two to the k runs, at resolution R or better.
    /// </summary>
    private static JgsValue FractionGenerators(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("fracfactgen", args, 1, 4, line, col);
        string[] terms = GeneratorWords("fracfactgen", args[0], line, col);

        var letters = new SortedSet<char>();
        foreach (string term in terms)
        {
            foreach (char letter in term)
            {
                if (char.IsLetter(letter))
                {
                    letters.Add(letter);
                }
            }
        }

        int factors = letters.Count;
        if (factors == 0)
        {
            throw new JgsRuntimeException(line, col, "fracfactgen needs at least one factor to design for.");
        }

        int basics = args.Count > 1 && !IsPlaceholderValue(args[1])
            ? Count("fracfactgen", args, 1, line, col)
            : DefaultBasics(factors);

        int resolution = args.Count > 2 && !IsPlaceholderValue(args[2])
            ? Count("fracfactgen", args, 2, line, col)
            : 3;

        if (args.Count > 3 && !IsPlaceholderValue(args[3]))
        {
            throw new JgsRuntimeException(line, col,
                "fracfactgen: naming the basic factors is not supported. The search here picks them, "
                + "because it is exhaustive over the assignments and so cannot be helped by a hint.");
        }

        IReadOnlyList<string> found = Guarded(
            "fracfactgen",
            () => DesignOfExperiments.FractionGenerators(factors, basics, resolution),
            line,
            col);

        if (found.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"fracfactgen: no design of {factors} factors in 2^{basics} runs reaches resolution {resolution}.");
        }

        var cells = new JgsValue[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            cells[i] = JgsValue.Str(found[i]);
        }

        JgsValue list = JgsValue.Cell(cells);
        list.ReshapeDims([found.Count, 1]);
        return list;
    }

    /// <summary>The smallest cube that can hold the factors at all, which is the default fraction.</summary>
    private static int DefaultBasics(int factors)
    {
        int basics = 3;
        while (basics < factors && (1 << basics) < factors + 1)
        {
            basics++;
        }

        return Math.Min(basics, factors);
    }

    /// <summary><c>[d, blocks] = bbdesign(n)</c>: the Box-Behnken design of n factors.</summary>
    private static JgsValue[] BehnkenDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "bbdesign needs a number of factors.");
        }

        ParsedArgs parsed = BehnkenOptions.Parse(args, 1, line, col);
        int factors = WholeOf("bbdesign", parsed.Positional[0], line, col);
        int centre = parsed.Whole("center", DesignOfExperiments.BehnkenCentrePoints(factors));
        RefuseBlocking("bbdesign", parsed, line, col);

        double[,] design = Guarded("bbdesign", () => DesignOfExperiments.BoxBehnken(factors, centre), line, col);
        return Outputs(wanted, Rectangle(design), OneBlock(design.GetLength(0)));
    }

    /// <summary><c>[d, blocks] = ccdesign(n)</c>: the central composite design of n factors.</summary>
    private static JgsValue[] CompositeDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "ccdesign needs a number of factors.");
        }

        ParsedArgs parsed = CompositeOptions.Parse(args, 1, line, col);
        int factors = WholeOf("ccdesign", parsed.Positional[0], line, col);
        int fraction = parsed.Whole("fraction", 0);
        int centre = parsed.Whole("center", DesignOfExperiments.CompositeCentrePoints(factors, fraction));
        RefuseBlocking("ccdesign", parsed, line, col);

        DesignOfExperiments.CompositeKind kind =
            parsed.Word("type", "circumscribed", "circumscribed", "inscribed", "faced") switch
            {
                "inscribed" => DesignOfExperiments.CompositeKind.Inscribed,
                "faced" => DesignOfExperiments.CompositeKind.Faced,
                _ => DesignOfExperiments.CompositeKind.Circumscribed,
            };

        double[,] design = Guarded(
            "ccdesign",
            () => DesignOfExperiments.CentralComposite(factors, fraction, kind, centre),
            line,
            col);

        return Outputs(wanted, Rectangle(design), OneBlock(design.GetLength(0)));
    }

    /// <summary>
    /// Blocking is accepted only when the block is the whole design. Splitting one into blocks means
    /// deciding which effect the block difference is confounded with, and that decision belongs to the
    /// experimenter rather than to a default nobody chose.
    /// </summary>
    private static void RefuseBlocking(string name, ParsedArgs parsed, int line, int col)
    {
        if (parsed.Named("blocksize") is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'blocksize' splits the runs into blocks, which confounds the block difference "
                + "with an effect the design has to choose. That choice is not made here — the design "
                + "comes back in one block.");
        }

        if (parsed.Named("state") is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'state' seeded the old generator. Nothing here is random — use rng for the "
                + "names that are.");
        }
    }

    private static JgsValue OneBlock(int runs)
    {
        var blocks = new double[runs];
        Array.Fill(blocks, 1);
        return JgsMatrix.FromColumnMajor(blocks, runs, 1);
    }

    /// <summary>A whole number argument, whatever numeric shape it arrived in.</summary>
    private static int WholeOf(string name, JgsValue value, int line, int col)
    {
        double number = NumOf(name, value, line, col);
        if (number != Math.Floor(number))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a whole number, but got {number}.");
        }

        return (int)number;
    }

    // --- Reading and writing generators ------------------------------------------------------------

    /// <summary>
    /// The generator words of a design, from a char row holding them separated by spaces or from a cell
    /// of them one per element.
    /// </summary>
    private static string[] GeneratorWords(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            string[] split = value.AsString.Split(
                [' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (split.Length == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name}: the generators are empty.");
            }

            return split;
        }

        return TextElements(name, value, line, col);
    }

    /// <summary>One generator word read as the basic factors it multiplies together.</summary>
    private static DesignOfExperiments.Generator ParseGenerator(string word, int line, int col)
    {
        string letters = word.StartsWith('-') ? word[1..] : word;
        if (letters.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "fracfact: a generator cannot be empty.");
        }

        var factors = new List<int>();
        foreach (char letter in letters)
        {
            int index = letter switch
            {
                >= 'a' and <= 'z' => letter - 'a',
                >= 'A' and <= 'Z' => (letter - 'A') + 26,
                _ => throw new JgsRuntimeException(line, col,
                    $"fracfact: '{word}' is not a generator — a generator is a run of letters, each naming a factor."),
            };

            if (factors.Contains(index))
            {
                throw new JgsRuntimeException(line, col,
                    $"fracfact: '{word}' names the same factor twice, which is the constant rather than an effect.");
            }

            factors.Add(index);
        }

        factors.Sort();
        return new DesignOfExperiments.Generator(word, [.. factors]);
    }

    private static string[] BuildNames(int columns)
    {
        var names = new string[columns];
        for (int i = 0; i < columns; i++)
        {
            names[i] = "X" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return names;
    }

    /// <summary>
    /// The confounding table: one row per group of effects that share a column, written as the term, the
    /// letters that make it, and everything it cannot be told apart from.
    /// </summary>
    private static JgsValue ConfoundingTable(
        double[,] design,
        DesignOfExperiments.Generator[] generators,
        string[] names,
        int order)
    {
        IReadOnlyList<int[][]> groups = DesignOfExperiments.Confounding(design, order);

        var rows = new List<(string Term, string Word, string Group)>
        {
            ("Term", "Generator", "Confounding"),
        };

        foreach (int[][] group in groups)
        {
            var written = new List<string>(group.Length);
            foreach (int[] term in group)
            {
                written.Add(term.Length == 0 ? "Constant" : Join(term, names, "*"));
            }

            int[] first = group[0];
            string word = first.Length == 0 ? "1" : WordOf(first, generators);
            rows.Add((written[0], word, string.Join(" + ", written)));
        }

        var cells = new JgsValue[rows.Count * 3];
        for (int r = 0; r < rows.Count; r++)
        {
            cells[r] = JgsValue.Str(rows[r].Term);
            cells[rows.Count + r] = JgsValue.Str(rows[r].Word);
            cells[(2 * rows.Count) + r] = JgsValue.Str(rows[r].Group);
        }

        JgsValue table = JgsValue.Cell(cells);
        table.ReshapeDims([rows.Count, 3]);
        return table;
    }

    private static string Join(int[] term, string[] names, string between)
    {
        var written = new List<string>(term.Length);
        foreach (int column in term)
        {
            written.Add(names[column]);
        }

        return string.Join(between, written);
    }

    /// <summary>
    /// The letters a term is written with: the basic factors that appear an odd number of times across
    /// the generators of its columns, because a factor multiplied by itself is the constant.
    /// </summary>
    private static string WordOf(int[] term, DesignOfExperiments.Generator[] generators)
    {
        var present = new SortedSet<int>();
        foreach (int column in term)
        {
            foreach (int basic in generators[column].Basic)
            {
                if (!present.Add(basic))
                {
                    present.Remove(basic);
                }
            }
        }

        if (present.Count == 0)
        {
            return "1";
        }

        var word = new StringBuilder();
        foreach (int basic in present)
        {
            word.Append(DesignOfExperiments.Letter(basic));
        }

        return word.ToString();
    }

    // --- Capability and gage studies ---------------------------------------------------------------

    /// <summary><c>S = capability(data, specs)</c>: the indices a process meets its limits by.</summary>
    private static JgsValue CapabilityIndices(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("capability", args, 1, 2, line, col);
        double[] data = FlattenColumnMajor("capability", args[0], line, col);
        (double lower, double upper) = SpecificationLimits("capability", args, 1, line, col);

        ProcessCapability.Capability answer = Guarded(
            "capability", () => ProcessCapability.Capable(data, lower, upper), line, col);

        return Structure(
            ("mu", JgsValue.Number(answer.Mean)),
            ("sigma", JgsValue.Number(answer.Deviation)),
            ("P", JgsValue.Number(answer.Outside)),
            ("Pl", JgsValue.Number(answer.BelowLower)),
            ("Pu", JgsValue.Number(answer.AboveUpper)),
            ("Cp", JgsValue.Number(answer.Cp)),
            ("Cpl", JgsValue.Number(answer.Cpl)),
            ("Cpu", JgsValue.Number(answer.Cpu)),
            ("Cpk", JgsValue.Number(answer.Cpk)),
            ("Cpm", JgsValue.Number(answer.Cpm)));
    }

    /// <summary>
    /// The two specification limits, either of which may be given as an infinity or left out of a
    /// one-element vector to mean the specification is one-sided.
    /// </summary>
    private static (double Lower, double Upper) SpecificationLimits(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (args.Count <= index || IsPlaceholderValue(args[index]))
        {
            throw new JgsRuntimeException(line, col, $"{name} needs the specification limits.");
        }

        double[] limits = FlattenColumnMajor(name, args[index], line, col);
        return limits.Length switch
        {
            1 => (limits[0], double.PositiveInfinity),
            2 => (limits[0], limits[1]),
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: the specification is one limit or a [lower upper] pair."),
        };
    }

    /// <summary>
    /// <c>[sd, tbl, stats] = gagerr(y, {part, operator})</c>: how much of the spread in a set of
    /// measurements belongs to the measuring rather than to the parts.
    /// </summary>
    private static JgsValue[] GageStudy(
        JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "gagerr needs the measurements and the part each of them is of.");
        }

        ParsedArgs parsed = GageOptions.Parse(args, 3, line, col);
        double[] measurements = FlattenColumnMajor("gagerr", parsed.Positional[0], line, col);

        (int[] part, int[] people) = GageGrouping(parsed, measurements.Length, line, col);

        ProcessCapability.GageModel model =
            parsed.Word("model", "interaction", "linear", "interaction", "nested") switch
            {
                "linear" => ProcessCapability.GageModel.Linear,
                "nested" => ProcessCapability.GageModel.Nested,
                _ => ProcessCapability.GageModel.Interaction,
            };

        double tolerance = 0;
        if (parsed.Named("spec") is { } spec)
        {
            double[] limits = NumericVector("gagerr", spec, line, col);
            tolerance = limits.Length == 2 ? Math.Abs(limits[1] - limits[0])
                : limits.Length == 1 ? Math.Abs(limits[0])
                : throw new JgsRuntimeException(line, col,
                    "gagerr: 'spec' is a tolerance width or a [lower upper] pair.");
        }

        double deviations = parsed.Scalar("sigma", 5.15);
        double alpha = parsed.Scalar("alpha", 0.05);
        if (!(alpha > 0 && alpha < 1))
        {
            throw new JgsRuntimeException(line, col, "gagerr: 'alpha' sits strictly between 0 and 1.");
        }

        ProcessCapability.GageStudy study = Guarded(
            "gagerr",
            () => ProcessCapability.Gage(measurements, part, people, model, tolerance, deviations),
            line,
            col);

        if (parsed.Named("printtable") is { } table && table.IsTruthy)
        {
            PrintGageTable(host, study, tolerance > 0);
        }

        if (parsed.Named("printgraph") is { } graph && graph.IsTruthy)
        {
            GageBars(study);
        }

        int rows = study.Rows.Count;
        var cells = new JgsValue[rows * (tolerance > 0 ? 6 : 5)];
        for (int r = 0; r < rows; r++)
        {
            ProcessCapability.GageRow row = study.Rows[r];
            cells[r] = JgsValue.Str(row.Source);
            cells[rows + r] = JgsValue.Number(row.Variance);
            cells[(2 * rows) + r] = JgsValue.Number(row.PercentVariance);
            cells[(3 * rows) + r] = JgsValue.Number(row.Sigma);
            cells[(4 * rows) + r] = JgsValue.Number(row.PercentSigma);
            if (tolerance > 0)
            {
                cells[(5 * rows) + r] = JgsValue.Number(row.PercentTolerance);
            }
        }

        JgsValue answer = JgsValue.Cell(cells);
        answer.ReshapeDims([rows, tolerance > 0 ? 6 : 5]);

        JgsValue statistics = Structure(
            ("gagerr", JgsValue.Number(study.Rows[0].Variance)),
            ("repeatability", JgsValue.Number(study.Rows[1].Variance)),
            ("reproducibility", JgsValue.Number(study.Rows[2].Variance)),
            ("part", JgsValue.Number(study.Rows[^2].Variance)),
            ("total", JgsValue.Number(study.Rows[^1].Variance)),
            ("ndc", JgsValue.Number(study.DistinctCategories)));

        return Outputs(wanted, JgsValue.Number(study.GageDeviation), answer, statistics);
    }

    /// <summary>The part and the operator of every measurement, however the caller grouped them.</summary>
    private static (int[] Part, int[] Operators) GageGrouping(
        ParsedArgs parsed, int count, int line, int col)
    {
        var grouping = new List<JgsValue>();
        if (parsed.Positional[1].Type == JgsType.Cell)
        {
            grouping.AddRange(parsed.Positional[1].AsCell);
        }
        else
        {
            grouping.Add(parsed.Positional[1]);
            if (parsed.Positional.Count > 2)
            {
                grouping.Add(parsed.Positional[2]);
            }
        }

        if (grouping.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                "gagerr: the grouping is the part, or the part and the operator.");
        }

        (int[] part, _) = GroupIndex("gagerr", grouping[0], line, col);
        int[] people = grouping.Count > 1 ? GroupIndex("gagerr", grouping[1], line, col).Index : [];

        if (part.Length != count || (people.Length != 0 && people.Length != count))
        {
            throw new JgsRuntimeException(line, col,
                "gagerr: the grouping needs one label per measurement.");
        }

        return (part, people);
    }

    private static void PrintGageTable(
        JGraphScriptGlobals host, ProcessCapability.GageStudy study, bool tolerance)
    {
        var text = new StringBuilder();
        text.Append("    Source              Variance     % Variance      sigma      % sigma");
        if (tolerance)
        {
            text.Append("   % Tolerance");
        }

        host.WriteOut(text.ToString() + "\n");
        foreach (ProcessCapability.GageRow row in study.Rows)
        {
            string written = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "    {0,-18}{1,10:0.#####}{2,15:0.##}{3,11:0.####}{4,13:0.##}",
                row.Source,
                row.Variance,
                row.PercentVariance,
                row.Sigma,
                row.PercentSigma);

            if (tolerance)
            {
                written += string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, "{0,14:0.##}", row.PercentTolerance);
            }

            host.WriteOut(written + "\n");
        }

        host.WriteOut(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "    Number of distinct categories: {0}\n",
            study.DistinctCategories));
    }

    /// <summary>The bar chart of the variance decomposition, which is what the graph option draws.</summary>
    private static void GageBars(ProcessCapability.GageStudy study)
    {
        var heights = new List<double>();
        var labels = new List<string>();
        foreach (ProcessCapability.GageRow row in study.Rows)
        {
            if (row.Source != "Total")
            {
                heights.Add(row.PercentVariance);
                labels.Add(row.Source);
            }
        }

        JGraph.Api.JG.Bar([.. labels], [.. heights]);
        JGraph.Api.JG.Title("Components of variation");
        JGraph.Api.JG.YLabel("Percent of total variance");
    }

    // --- The options structure ----------------------------------------------------------------------

    /// <summary>
    /// <c>options = statset(...)</c>: the structure the iterative names take their settings from.
    /// </summary>
    /// <remarks>
    /// The structure holds every field MathWorks documents, whether or not anything here reads it, so
    /// that a script that sets one and passes it along keeps working. What is actually read is recorded
    /// as a divergence rather than left for a reader to discover.
    /// </remarks>
    private static JgsValue Statset(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var fields = new List<(string Name, JgsValue Value)>(StatsetFields);
        int from = 0;

        // A first argument that is not a name is either a function to answer the defaults for, or an
        // existing structure to change.
        if (args.Count > 0 && args[0].Type == JgsType.Struct)
        {
            IReadOnlyDictionary<string, JgsValue> existing = args[0].AsStruct;
            for (int i = 0; i < fields.Count; i++)
            {
                if (existing.TryGetValue(fields[i].Name, out JgsValue? value))
                {
                    fields[i] = (fields[i].Name, value);
                }
            }

            from = 1;
        }
        else if (args.Count == 1 && args[0].Type == JgsType.String)
        {
            // statset('fitname') answers the defaults that name would use, which here are the defaults
            // themselves: nothing in this build varies its tolerances by which function asked.
            return Structure([.. fields]);
        }

        if ((args.Count - from) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col,
                "statset: the settings come in name and value pairs.");
        }

        for (int i = from; i < args.Count; i += 2)
        {
            string name = Str("statset", args, i, line, col);
            int slot = fields.FindIndex(field =>
                string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
            if (slot < 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"statset: '{name}' is not a setting. The settings are "
                    + string.Join(", ", Array.ConvertAll(StatsetFields, field => field.Name)) + ".");
            }

            fields[slot] = (fields[slot].Name, args[i + 1]);
        }

        return Structure([.. fields]);
    }

    /// <summary><c>value = statget(options, name, default)</c>: one setting, or what to use instead.</summary>
    private static JgsValue Statget(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("statget", args, 2, 3, line, col);
        string name = Str("statget", args, 1, line, col);
        JgsValue fallback = args.Count > 2 ? args[2] : JgsValue.Array([]);

        if (args[0].Type != JgsType.Struct)
        {
            return fallback;
        }

        foreach ((string field, JgsValue value) in args[0].AsStruct)
        {
            if (string.Equals(field, name, StringComparison.OrdinalIgnoreCase))
            {
                return value.Type == JgsType.Array && value.ArrayLength == 0 ? fallback : value;
            }
        }

        return fallback;
    }

    /// <summary>
    /// A setting read out of an options structure a caller passed along, used where a name also takes
    /// the same setting as a named argument of its own — the explicit one wins, because it was written
    /// at the call.
    /// </summary>
    private static int SettingWhole(ParsedArgs parsed, string named, string field, int fallback)
    {
        if (parsed.Named(named) is not null)
        {
            return parsed.Whole(named, fallback);
        }

        if (parsed.Named("options") is { Type: JgsType.Struct } options)
        {
            foreach ((string name, JgsValue value) in options.AsStruct)
            {
                if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase)
                    && value.Type is JgsType.Number or JgsType.Bool)
                {
                    return (int)Math.Round(value.AsNumber);
                }
            }
        }

        return fallback;
    }

}
