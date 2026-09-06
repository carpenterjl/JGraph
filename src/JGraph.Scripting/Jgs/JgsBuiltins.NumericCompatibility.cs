using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    private static Complex[] ColumnComplex(string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Array) return FlattenedComplex(name, value, line, col);
        if (value.IsNd || value.IsShaped || value.IsPacked || value.IsPackedComplex)
            return Enumerable.Range(0, value.ArrayLength).Select(i => ScalarComplex(name, value.ElementAt(i), line, col)).ToArray();
        Complex[,] matrix = ComplexRectOf(name, value, line, col);
        return Enumerable.Range(0, matrix.Length).Select(i => matrix[i % matrix.GetLength(0), i / matrix.GetLength(0)]).ToArray();
    }

    private static JgsValue[] IntegrateComponents(string name, IJgsCallable f, Complex a, Complex b,
        Complex[]? waypoints, bool arrayValued, bool scalarCalls, double relative, double absolute,
        int maximum, int wanted, int line, int col)
    {
        bool contour = a.Imaginary != 0 || b.Imaginary != 0 || waypoints?.Any(z => z.Imaginary != 0) == true;
        var path = new List<Complex> { a };
        if (waypoints is not null) path.AddRange(waypoints);
        path.Add(b);
        if (contour && path.Any(z => !double.IsFinite(z.Real) || !double.IsFinite(z.Imaginary)))
            throw new JgsRuntimeException(line, col, "Complex integration limits and waypoints must be finite.");
        Complex probe = contour ? (path[0] + path[1]) / 2 : new Complex(double.IsFinite(a.Real) && double.IsFinite(b.Real) ? a.Real / 2 + b.Real / 2 : double.IsFinite(a.Real) ? a.Real + 1 : double.IsFinite(b.Real) ? b.Real - 1 : 0, 0);
        JgsValue sample = f.Call([JgsValue.ComplexNum(probe)], line, col);
        Complex[] initial = ColumnComplex(name, sample, line, col);
        if (!arrayValued && initial.Length != 1) throw new JgsRuntimeException(line, col, "integral: scalar integrands must return one value per point.");
        int[] shape = SizeDims(sample);
        var total = new Complex[initial.Length];
        double error = 0;
        int segments = contour ? path.Count - 1 : 1;
        for (int segment = 0; segment < segments; segment++)
        {
            Complex origin = contour ? path[segment] : Complex.Zero;
            Complex delta = contour ? path[segment + 1] - origin : Complex.One;
            if (contour && delta == Complex.Zero) continue;
            var cache = new Dictionary<double, Complex[]>();
            Complex[] At(double x)
            {
                if (cache.TryGetValue(x, out var saved)) return saved;
                JgsValue value = f.Call([JgsValue.ComplexNum(origin + delta * x)], line, col);
                Complex[] flat = ColumnComplex(name, value, line, col);
                if (flat.Length != initial.Length || arrayValued && !SizeDims(value).SequenceEqual(shape))
                    throw new JgsRuntimeException(line, col, "integral: integrand output size changed during integration.");
                return cache[x] = flat.Select(z => z * delta).ToArray();
            }
            for (int component = 0; component < total.Length; component++)
            {
                double[] Sample(double[] points, bool imaginary)
                {
                    if (!scalarCalls)
                    {
                        var missing = points.Where(x => !cache.ContainsKey(x)).Distinct().ToArray();
                        if (missing.Length > 0)
                        {
                            var positions = missing.Select(x => origin + delta * x).ToArray();
                            JgsValue input = contour ? ComplexStorage(positions, [1, positions.Length]) : Numbers(missing);
                            Complex[] values = ColumnComplex(name, f.Call([input], line, col), line, col);
                            if (values.Length != missing.Length) throw new JgsRuntimeException(line, col, "integral: output must match input size.");
                            for (int i = 0; i < missing.Length; i++) cache[missing[i]] = [values[i] * delta];
                        }
                    }
                    return points.Select(x => imaginary ? At(x)[component].Imaginary : At(x)[component].Real).ToArray();
                }
                try
                {
                    var re = Quadrature.Integrate(x => Sample(x, false), contour ? 0 : a.Real, contour ? 1 : b.Real, relative, absolute / segments, contour ? null : waypoints?.Select(z => z.Real).ToArray(), maximum);
                    var im = Quadrature.Integrate(x => Sample(x, true), contour ? 0 : a.Real, contour ? 1 : b.Real, relative, absolute / segments, contour ? null : waypoints?.Select(z => z.Real).ToArray(), maximum);
                    total[component] += new Complex(re.Value, im.Value);
                    error += re.ErrorBound + im.ErrorBound;
                }
                catch (ArgumentException ex) { throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}"); }
            }
        }
        JgsValue result = total.Any(z => z.Imaginary != 0) ? ComplexStorage(total, shape) : ShapedNumbers(total.Select(z => z.Real).ToArray(), shape);
        return wanted > 1 ? [result, JgsValue.Number(error)] : [result];
    }

    private static bool NeedsRankedExtreme(IReadOnlyList<JgsValue> args) =>
        args.Count > 0 && (HasComplexElements(args[0]) || args.Skip(1).Any(v => IsTextScalar(v) && TextOf(v).Equals("ComparisonMethod", StringComparison.OrdinalIgnoreCase))
            || args.Count > 1 && HasComplexElements(args[1]));

    private static JgsValue[] RankedExtreme(IReadOnlyList<JgsValue> args, int wanted,
        Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> run, int line, int col)
    {
        var clean = args.ToList();
        string method = "auto";
        for (int i = 1; i < clean.Count; i++)
            if (IsTextScalar(clean[i]) && TextOf(clean[i]).Equals("ComparisonMethod", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= clean.Count) throw new JgsRuntimeException(line, col, "ComparisonMethod requires a value.");
                method = TextOf(clean[i + 1]).ToLowerInvariant();
                if (method is not ("auto" or "real" or "abs")) throw new JgsRuntimeException(line, col, "Unknown ComparisonMethod.");
                clean.RemoveRange(i, 2); break;
            }
        bool pair = clean.Count > 1 && !IsTextScalar(clean[1]) && !JgsEmpty.IsEmptyArray(clean[1]);
        bool complex = HasComplexElements(clean[0]) || pair && HasComplexElements(clean[1]);
        bool magnitude = method == "abs" || method == "auto" && complex;
        var subjects = pair ? new[] { clean[0], clean[1] } : new[] { clean[0] };
        var values = subjects.Select(v => ColumnComplex("max/min", v, line, col)).ToArray();
        bool Missing(Complex z) => double.IsNaN(z.Real) || double.IsNaN(z.Imaginary);
        int Compare(Complex a, Complex b)
        {
            int c = (magnitude ? a.Magnitude : a.Real).CompareTo(magnitude ? b.Magnitude : b.Real);
            return c != 0 ? c : magnitude ? a.Phase.CompareTo(b.Phase) : a.Imaginary.CompareTo(b.Imaginary);
        }
        var ordered = values.SelectMany(v => v).Where(v => !Missing(v)).ToList();
        ordered.Sort(Compare);
        // Equal comparison keys share a rank, keeping the reduction's first-index tie rule.
        var unique = new List<Complex>();
        foreach (var v in ordered) if (unique.Count == 0 || Compare(unique[^1], v) != 0) unique.Add(v);
        for (int i = 0; i < subjects.Length; i++)
            clean[i] = ShapedNumbers(values[i].Select(v => Missing(v) ? double.NaN : unique.BinarySearch(v, Comparer<Complex>.Create(Compare)) + 1.0).ToArray(), SizeDims(subjects[i]));
        JgsValue[] answer = run(clean, wanted, line, col);
        double[] ranks = FlattenColumnMajor("max/min", answer[0], line, col);
        Complex[] restored = ranks.Select(v => double.IsNaN(v) ? new Complex(double.NaN, 0) : unique[(int)v - 1]).ToArray();
        answer[0] = complex ? ComplexStorage(restored, SizeDims(answer[0])) : ShapedNumbers(restored.Select(v => v.Real).ToArray(), SizeDims(answer[0]));
        return answer;
    }

    private static JgsValue[] SvdFormatted(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("svd", args, 1, 3, line, col);
        var core = new List<JgsValue> { args[0] };
        bool vector = wanted <= 1;
        bool formatSeen = false;
        for (int i = 1; i < args.Count; i++)
        {
            string word = IsTextScalar(args[i]) ? TextOf(args[i]).ToLowerInvariant() : "";
            if (word is "vector" or "matrix")
            {
                if (formatSeen) throw new JgsRuntimeException(line, col, "svd: specify one output format.");
                vector = word == "vector"; formatSeen = true;
            }
            else
            {
                if (core.Count > 1 || formatSeen) throw new JgsRuntimeException(line, col, "svd: economy option must precede the output format.");
                core.Add(args[i]);
            }
        }
        if (wanted <= 1 && vector) return [SingularValueList(core, line, col)];
        JgsValue[] result = SingularValueFactors(core, Math.Max(2, wanted), line, col);
        if (vector) result[1] = DiagonalOf(result[1], 0, line, col);
        return wanted <= 1 ? [result[1]] : result;
    }

    private static Complex ScalarComplex(string name, JgsValue value, int line, int col)
    {
        Complex[] values = FlattenedComplex(name, value, line, col);
        return values.Length == 1 ? values[0] : throw new JgsRuntimeException(line, col, $"{name} requires a scalar.");
    }

    private static JgsValue ComplexStorage(Complex[] values, int[] dims, bool preserveComplex = false)
    {
        var re = JgsPacking.Allocate(values.Length);
        var im = JgsPacking.Allocate(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            re.AsSpan()[i] = values[i].Real;
            im.AsSpan()[i] = values[i].Imaginary;
        }
        return JgsMatrix.Like(JgsMatrix.FromColumnMajorDims(new double[values.Length], dims),
            JgsValue.PackedComplexArray(new JgsPackedComplex(re, im, preserveComplex)));
    }

    private static JgsValue ConstructorTraits(string name, IReadOnlyList<JgsValue> args,
        JgsValue built, JgsNumericClass? asked, int line, int col)
    {
        bool like = args.Count >= 2 && IsTextScalar(args[^2]) && TextOf(args[^2]).Equals("like", StringComparison.OrdinalIgnoreCase);
        if (asked is { } numericClass) built = ToNumericClass(name, numericClass, built, line, col);
        if (like && HasComplexElements(args[^1]))
        {
            built = ComplexStorage(FlattenColumnMajor(name, built, line, col).Select(v => new Complex(v, 0)).ToArray(), SizeDims(built), preserveComplex: true);
            built.SetNumericClass(asked ?? JgsNumericClass.Double);
        }
        if (like && args[^1].Type == JgsType.Sparse)
            built = JgsValue.Sparse(CscFromDense(name, built, line, col));
        if (like && IsLogicalValue(args[^1]) || args.Count > 0 && IsTextScalar(args[^1]) && TextOf(args[^1]).Equals("logical", StringComparison.OrdinalIgnoreCase)) built = AsMask(built);
        return built;
    }

    // Group by the unreduced coordinates so vecdim medians use all observations together.
    private static JgsValue MedianReduction(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("median", args, 1, 7, line, col);
        JgsValue input = args[0];
        int[] shape = SizeDims(input);
        if (shape.All(d => d == 0) && args.Count == 1) return JgsValue.Number(double.NaN);
        int first = Array.FindIndex(shape, d => d != 1);
        int[] dimensions = [first < 0 ? 1 : first + 1];
        bool omit = false;
        double[]? weights = null;
        bool multipleDimensions = false;
        for (int i = 1; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                dimensions = ReadVecdim("median", args[i].Type == JgsType.Number ? Numbers([args[i].AsNumber]) : args[i], line, col);
                if (dimensions.Length == 0) throw new JgsRuntimeException(line, col, "median: dimensions cannot be empty.");
                multipleDimensions = dimensions.Length > 1;
                continue;
            }
            string word = TextOf(args[i]).ToLowerInvariant();
            switch (word)
            {
                case "all": dimensions = Enumerable.Range(1, shape.Length).ToArray(); multipleDimensions = true; break;
                case "omitnan": case "omitmissing": omit = true; break;
                case "includenan": case "includemissing": omit = false; break;
                case "weights":
                    if (++i >= args.Count) throw new JgsRuntimeException(line, col, "median: Weights requires a value.");
                    weights = FlattenColumnMajor("median", args[i], line, col); break;
                default: throw new JgsRuntimeException(line, col, $"median: unknown option '{word}'.");
            }
        }
        if (weights is not null && multipleDimensions) throw new JgsRuntimeException(line, col, "median: Weights cannot be combined with vecdim or all.");
        int rank = Math.Max(shape.Length, dimensions.Max());
        shape = Enumerable.Range(0, rank).Select(i => i < shape.Length ? shape[i] : 1).ToArray();
        int[] reduced = (int[])shape.Clone();
        foreach (int d in dimensions) reduced[d - 1] = 1;
        double[] flat = FlattenColumnMajor("median", input, line, col);
        int count = reduced.Aggregate(1, (a, b) => a * b);
        var groups = Enumerable.Range(0, count).Select(_ => new List<(double V, double W)>()).ToArray();
        int reducedSize = dimensions.Aggregate(1, (a, d) => a * shape[d - 1]);
        if (weights is not null && weights.Length != flat.Length && weights.Length != reducedSize)
            throw new JgsRuntimeException(line, col, "median: Weights must match the data or the reduced observations.");
        if (weights is not null && weights.Any(w => w < 0 || double.IsInfinity(w)))
            throw new JgsRuntimeException(line, col, "median: Weights must be nonnegative and finite.");
        for (int i = 0; i < flat.Length; i++)
        {
            int remaining = i, group = 0, stride = 1, wi = 0, ws = 1;
            for (int d = 0; d < rank; d++)
            {
                int coord = remaining % shape[d]; remaining /= shape[d];
                if (dimensions.Contains(d + 1)) { wi += coord * ws; ws *= shape[d]; }
                else { group += coord * stride; stride *= shape[d]; }
            }
            double w = weights is null ? 1 : weights[weights.Length == flat.Length ? i : wi];
            if (omit && (double.IsNaN(flat[i]) || double.IsNaN(w))) continue;
            groups[group].Add((flat[i], w));
        }
        var answers = new double[count];
        for (int i = 0; i < count; i++)
        {
            var data = groups[i];
            if (data.Count == 0 || data.Any(p => double.IsNaN(p.V) || double.IsNaN(p.W))) { answers[i] = double.NaN; continue; }
            data.Sort((a, b) => a.V.CompareTo(b.V));
            if (weights is null)
                answers[i] = data.Count % 2 == 1 ? data[data.Count / 2].V : data[data.Count / 2 - 1].V / 2 + data[data.Count / 2].V / 2;
            else
            {
                double half = data.Sum(p => p.W) / 2, cumulative = 0;
                answers[i] = double.NaN;
                if (half <= 0) continue;
                for (int k = 0; k < data.Count; k++)
                {
                    cumulative += data[k].W;
                    if (cumulative < half || data[k].W == 0) continue;
                    int next = k + 1;
                    while (next < data.Count && data[next].W == 0) next++;
                    answers[i] = cumulative == half && next < data.Count ? data[k].V / 2 + data[next].V / 2 : data[k].V;
                    break;
                }
            }
        }
        JgsValue result = JgsMatrix.FromColumnMajorDims(answers, reduced);
        return ToNumericClass("median", input.NumericClass, result, line, col);
    }
}
