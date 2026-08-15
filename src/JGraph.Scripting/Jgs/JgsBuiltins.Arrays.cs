namespace JGraph.Scripting.Jgs;

/// <summary>
/// The array-level statistics and rearrangements (M38): the function-application family
/// (<c>arrayfun</c>, <c>bsxfun</c>, <c>structfun</c>), the running extremes (<c>cummax</c>,
/// <c>cummin</c>), the random draws and <c>rng</c>, and the rearrangements. The set, selection and
/// sliding-window builtins registered from here live in JgsBuiltins.SetsAndWindows.cs, which is where
/// their option surfaces are.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the array and moving-statistics builtins (M38), and <c>rng</c> (M52).</summary>
    private static void RegisterArrayBuiltins(JgsEnvironment env, JgsRandomSource random, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        RegisterApplication(env, Define);
        RegisterRunningStatistics(Define);
        RegisterSelectionAndSets(env, dialect);
        RegisterRandomDraws(Define, random);
        RegisterRng(env, random);
        RegisterRearrangements(Define);
        RegisterMovingStatistics(env);
    }

    // --- Applying a function ----------------------------------------------------------------------

    private static readonly OptionSpec ArrayOptions = new(
        "arrayfun", Flags: [], Names: ["UniformOutput", "ErrorHandler"]);

    /// <summary>
    /// <c>arrayfun</c> over any number of arrays, producing any number of outputs — <c>cellfun</c>'s
    /// loop, over elements rather than cells.
    /// </summary>
    /// <remarks>
    /// M52 recorded that this verb read <c>'UniformOutput'</c> by scanning for the word and ignored
    /// everything else in the tail, so a misspelling was accepted in silence and
    /// <c>'ErrorHandler'</c> did nothing. Sharing the option table is what closes both at once, and
    /// asking each element for several answers is what M61's multiple-output work makes possible.
    /// </remarks>
    private static JgsValue[] ApplyOverArrays(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "arrayfun(f, a) applies a function handle to each element.");
        }

        var inputs = new List<JgsValue[]>();
        int i = 1;
        while (i < args.Count && args[i].Type != JgsType.String)
        {
            inputs.Add(ElementsOf("arrayfun", args[i]));
            i++;
        }

        if (inputs.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"arrayfun expects an array to walk, but got a {args[1].TypeName}.");
        }

        var tail = new List<JgsValue>();
        for (int t = i; t < args.Count; t++)
        {
            tail.Add(args[t]);
        }

        ParsedArgs parsed = ArrayOptions.Parse(tail, 0, line, col);
        bool uniform = parsed.Flag("UniformOutput", true);
        JgsValue? handler = parsed.Named("ErrorHandler");
        if (handler is { Type: not JgsType.Function })
        {
            throw new JgsRuntimeException(line, col, "arrayfun: 'ErrorHandler' takes a function handle.");
        }

        int length = inputs[0].Length;
        foreach (JgsValue[] input in inputs)
        {
            if (input.Length != length)
            {
                throw new JgsRuntimeException(line, col, "arrayfun needs every array to be the same length.");
            }
        }

        int produced = Math.Max(wanted, 1);
        var collected = new JgsValue[produced][];
        for (int o = 0; o < produced; o++)
        {
            collected[o] = new JgsValue[length];
        }

        for (int k = 0; k < length; k++)
        {
            var call = new JgsValue[inputs.Count];
            for (int c = 0; c < inputs.Count; c++)
            {
                call[c] = inputs[c][k];
            }

            JgsValue[] answers;
            try
            {
                answers = CallForOutputs(args[0].AsCallable, call, produced, line, col);
            }
            catch (JgsRuntimeException failure) when (handler is { } catcher)
            {
                var handed = new JgsValue[call.Length + 1];
                handed[0] = FailureRecord(failure, k);
                call.CopyTo(handed, 1);
                answers = CallForOutputs(catcher.AsCallable, handed, produced, line, col);
            }

            if (answers.Length < produced)
            {
                throw new JgsRuntimeException(line, col,
                    $"arrayfun: element {k + 1} produced {answers.Length} output(s), but {produced} were asked for.");
            }

            for (int o = 0; o < produced; o++)
            {
                if (uniform && answers[o].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col,
                        "arrayfun: the function returned something that is not a number — pass 'UniformOutput', false.");
                }

                collected[o][k] = answers[o];
            }
        }

        var outputs = new JgsValue[produced];
        for (int o = 0; o < produced; o++)
        {
            // The uniform result takes the first array's shape — 2-D or N-D — the way MATLAB's does.
            outputs[o] = uniform
                ? JgsMatrix.Like(args[1], JgsMatrix.FromElements(collected[o], 1, length))
                : JgsMatrix.Like(args[1], JgsValue.Cell(collected[o]));
        }

        return outputs;
    }

    private static void RegisterApplication(
        JgsEnvironment env, Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // Declared with its several-output form rather than wrapped later: this registrar runs after
        // the MATLAB one, and that wrapper takes an already-registered name and silently does nothing
        // when there is none. The multi-output form went missing exactly that quietly (M61).
        env.Declare("arrayfun", JgsValue.Function(new BuiltinFunction(
            "arrayfun",
            (args, line, col) => ApplyOverArrays(args, 1, line, col)[0])
        {
            MultiOutput = ApplyOverArrays,
        }));

        Define("bsxfun", (args, line, col) =>
        {
            if (args.Count != 3 || args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "bsxfun(f, a, b) applies a function handle pairwise.");
            }

            // Singleton expansion is the one shape rule bsxfun exists to spell out; the same engine
            // now backs the elementwise operators, so bsxfun and '+' cannot disagree about a shape.
            IJgsCallable f = args[0].AsCallable;
            if (args[1].Type != JgsType.Array || args[2].Type != JgsType.Array)
            {
                JgsValue direct = f.Call([args[1], args[2]], line, col);
                return direct;
            }

            return JgsBroadcast.Map(args[1], args[2], "bsxfun", line, col,
                (a, b) => f.Call([a, b], line, col));
        });

        Define("structfun", (args, line, col) =>
        {
            if (args.Count < 2 || args[0].Type != JgsType.Function || args[1].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "structfun(f, s) applies a function handle to each field.");
            }

            bool uniform = UniformOutputWanted(args, 2);
            Dictionary<string, JgsValue> fields = args[1].AsStruct;
            var results = new List<JgsValue>();
            var names = new List<string>();
            foreach ((string field, JgsValue value) in fields)
            {
                names.Add(field);
                results.Add(args[0].AsCallable.Call([value], line, col));
            }

            if (uniform)
            {
                return JgsValue.Array(results.ToArray());
            }

            // Non-uniform structfun hands back a struct with the same field names, not a cell —
            // the one place the family's shape rule differs from cellfun's.
            var mapped = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                mapped[names[i]] = results[i];
            }

            return JgsValue.Struct(mapped);
        });

        Define("struct2cell", (args, line, col) =>
        {
            Arity("struct2cell", args, 1, line, col);
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, $"struct2cell expects a struct, but got a {args[0].TypeName}.");
            }

            return JgsValue.Cell(args[0].AsStruct.Values.ToArray());
        });

        Define("cell2struct", (args, line, col) =>
        {
            ArityRange("cell2struct", args, 2, 3, line, col);
            if (args[0].Type != JgsType.Cell || args[1].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "cell2struct(values, names) takes two cell arrays.");
            }

            JgsValue[] values = args[0].AsCell;
            JgsValue[] names = args[1].AsCell;
            if (values.Length != names.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"cell2struct got {values.Length} values for {names.Length} field names.");
            }

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, $"cell2struct: field name {i + 1} is a {names[i].TypeName}, not a string.");
                }

                fields[names[i].AsString] = values[i];
            }

            return JgsValue.Struct(fields);
        });
    }

    /// <summary>Reads the trailing 'UniformOutput' switch the apply family shares.</summary>
    private static bool UniformOutputWanted(IReadOnlyList<JgsValue> args, int from)
    {
        for (int i = from; i + 1 < args.Count; i++)
        {
            if (args[i].Type == JgsType.String
                && string.Equals(args[i].AsString, "UniformOutput", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].IsTruthy;
            }
        }

        return true;
    }

    /// <summary>A value's elements as a boxed list; a scalar is a one-element list.</summary>
    private static JgsValue[] ElementsOf(string name, JgsValue value) => value.Type switch
    {
        JgsType.Array => value.BoxedElements(),
        JgsType.Cell => value.AsCell,

        // A struct array walks element by element (M65), so arrayfun(@(s) s.a, stats) works — the
        // idiom for pulling one measurement out of a regionprops result.
        JgsType.Struct => StructElementValues(value),
        _ => [value],
    };

    /// <summary>Each element of a struct array as a 1-by-1 struct value of its own.</summary>
    private static JgsValue[] StructElementValues(JgsValue value)
    {
        JgsStructArray payload = value.AsStructArray;
        var elements = new JgsValue[payload.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = JgsValue.Struct(payload.Elements[i]);
        }

        return elements;
    }

    // --- Running statistics -----------------------------------------------------------------------

    private static void RegisterRunningStatistics(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        void Running(string name, Func<double, double, double> pick) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                double[] values = ToDoubles(name, args[0], line, col);
                var running = new double[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    running[i] = i == 0 ? values[0] : pick(running[i - 1], values[i]);
                }

                return Numbers(running);
            });

        Running("cummax", Math.Max);
        Running("cummin", Math.Min);
    }

    // --- Random draws -----------------------------------------------------------------------------

    /// <summary>
    /// Registers <c>rng</c> (M52). Declared against the scope rather than through the local
    /// <c>Define</c> because it needs two flags: it auto-calls on its bare name, so <c>s = rng</c> is a
    /// state rather than the builtin itself, and it prints nothing when a statement seeds the stream.
    /// </summary>
    private static void RegisterRng(JgsEnvironment env, JgsRandomSource random)
    {
        // rng(seed) — and the query/restore pair that lets a script put the stream back where it was.
        // The state is two plain numbers rather than MATLAB's 625-element vector, because that vector
        // is the Mersenne Twister's internals and this is not that generator; see JgsRandomSource.
        JgsValue Rng(IReadOnlyList<JgsValue> args, int line, int col)
        {
            ArityRange("rng", args, 0, 2, line, col);

            if (args.Count == 0)
            {
                return RngState(random);
            }

            if (args.Count == 2)
            {
                // The second argument names the generator. One is offered, and a script asking for
                // another gets told so rather than quietly getting this one.
                string generator = Str("rng", args, 1, line, col);
                if (!string.Equals(generator, "twister", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"rng: generator '{generator}' is not available (only 'twister').");
                }
            }

            switch (args[0].Type)
            {
                case JgsType.Number:
                {
                    double seed = args[0].AsNumber;
                    if (seed != System.Math.Floor(seed) || seed < 0 || seed > uint.MaxValue)
                    {
                        throw new JgsRuntimeException(line, col,
                            "rng: a seed is a whole number from 0 to 2^32-1.");
                    }

                    random.Reset(unchecked((int)(uint)seed));
                    return JgsValue.Null;
                }

                case JgsType.String:
                {
                    string word = args[0].AsString;
                    if (string.Equals(word, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        random.Reset(0);
                        return JgsValue.Null;
                    }

                    if (string.Equals(word, "shuffle", StringComparison.OrdinalIgnoreCase))
                    {
                        random.Shuffle();
                        return JgsValue.Null;
                    }

                    throw new JgsRuntimeException(line, col,
                        $"rng: unknown option '{word}' (options: 'default', 'shuffle').");
                }

                case JgsType.Struct:
                {
                    // rng(s), where s came from a previous `s = rng`.
                    IReadOnlyDictionary<string, JgsValue> fields = args[0].AsStruct;
                    if (!fields.TryGetValue("Seed", out JgsValue? seed)
                        || !fields.TryGetValue("Draws", out JgsValue? draws)
                        || seed.Type != JgsType.Number
                        || draws.Type != JgsType.Number)
                    {
                        throw new JgsRuntimeException(line, col,
                            "rng: that struct did not come from rng — it needs Seed and Draws.");
                    }

                    random.Restore((unchecked((int)(uint)seed.AsNumber), (long)draws.AsNumber));
                    return JgsValue.Null;
                }

                default:
                    throw new JgsRuntimeException(line, col,
                        $"rng takes a seed, 'default', 'shuffle', or a saved state, but got a {args[0].TypeName}.");
            }
        }

        env.Declare("rng", JgsValue.Function(
            new BuiltinFunction("rng", Rng) { AutoCallsBare = true, BindsAnsAsStatement = false }));
    }

    /// <summary>The stream's position as a script sees it: which generator, which seed, how far in.</summary>
    private static JgsValue RngState(JgsRandomSource random)
    {
        (int seed, long draws) = random.Snapshot();
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = JgsValue.Str(random.Kind),
            ["Seed"] = JgsValue.Number(unchecked((uint)seed)),
            ["Draws"] = JgsValue.Number(draws),
        });
    }

    private static void RegisterRandomDraws(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define, JgsRandomSource random)
    {

        Define("randi", (args, line, col) =>
        {
            ArityRange("randi", args, 1, int.MaxValue, line, col);

            // A trailing class name says what the numbers come back as. It is peeled off first so the
            // shape arguments below never have to wonder whether their last slot is a size or a word.
            (args, JgsNumericClass? asClass, bool asLogical) = DrawnClass("randi", args, line, col);

            // randi(imax) draws from 1..imax; randi([lo hi]) from the range it names.
            int low = 1;
            int high;
            if (args[0].Type == JgsType.Array)
            {
                double[] bounds = ToDoubles("randi", args[0], line, col);
                if (bounds.Length != 2)
                {
                    throw new JgsRuntimeException(line, col, "randi: a range is [low high].");
                }

                low = (int)bounds[0];
                high = (int)bounds[1];
            }
            else
            {
                high = Count("randi", args, 0, line, col);
            }

            if (high < low)
            {
                throw new JgsRuntimeException(line, col, "randi: the range is empty.");
            }

            JgsValue Tagged(JgsValue drawn)
            {
                if (asLogical)
                {
                    return MapToBool("randi", drawn, static x => x != 0, line, col);
                }

                if (asClass is { } numericClass)
                {
                    drawn.SetNumericClass(numericClass);
                }

                return drawn;
            }

            if (args.Count == 1)
            {
                return Tagged(JgsValue.Number(random.Next(low, high + 1)));
            }

            // Everything after the range is the requested shape: (n), (r, c, …), or a size vector
            // (randi([0 1], size(A))), any number of dimensions.
            var sizeArgs = new JgsValue[args.Count - 1];
            for (int i = 1; i < args.Count; i++)
            {
                sizeArgs[i - 1] = args[i];
            }

            int[] dims = SquareDims("randi", sizeArgs, line, col);
            long total = 1;
            foreach (int dim in dims)
            {
                total *= dim;
            }

            if (Array.TrueForAll(dims, static d => d == 1))
            {
                return Tagged(JgsValue.Number(random.Next(low, high + 1)));
            }

            var flat = new double[total];
            for (int i = 0; i < flat.Length; i++)
            {
                flat[i] = random.Next(low, high + 1);
            }

            return Tagged(JgsMatrix.FromColumnMajorDims(flat, dims));
        });

        Define("randperm", (args, line, col) =>
        {
            ArityRange("randperm", args, 1, 2, line, col);
            int n = Count("randperm", args, 0, line, col);
            int wanted = args.Count == 2 ? Count("randperm", args, 1, line, col) : n;
            if (wanted < 0 || wanted > n)
            {
                throw new JgsRuntimeException(line, col, $"randperm cannot take {wanted} values out of {n}.");
            }

            var order = new double[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i + 1; // randperm is 1-based in both dialects: it permutes 1..n by definition
            }

            for (int i = n - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return Numbers(order[..wanted]);
        });
    }

    // --- Rearrangement ----------------------------------------------------------------------------

    private static void RegisterRearrangements(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // circshift(A, k), circshift(A, k, dim) and circshift(A, [k1 k2 …]): every form is the same
        // rotation applied to one dimension, so the third form is the first one repeated. The default
        // dimension is MATLAB's first non-singleton, which is what makes a vector shift along itself
        // and a matrix shift its rows without either being a special case.
        Define("circshift", (args, line, col) =>
        {
            ArityRange("circshift", args, 2, 3, line, col);
            if (args[0].Type == JgsType.Array && args[0].ArrayLength == 0)
            {
                return args[0];
            }

            int[] dims = JgsMatrix.DimsOf(args[0]);
            double[] flat = FlattenColumnMajor("circshift", args[0], line, col);
            double[] by = NumericVector("circshift", args[1], line, col);

            if (args.Count == 3)
            {
                if (by.Length != 1)
                {
                    throw new JgsRuntimeException(line, col,
                        "circshift: naming a dimension shifts along that one, so it takes a single amount.");
                }

                flat = RotateAlong(flat, dims, Count("circshift", args, 2, line, col), (int)by[0], line, col);
            }
            else if (by.Length == 1)
            {
                flat = RotateAlong(flat, dims, JgsMatrix.DefaultDim(dims), (int)by[0], line, col);
            }
            else
            {
                for (int d = 0; d < by.Length; d++)
                {
                    flat = RotateAlong(flat, dims, d + 1, (int)by[d], line, col);
                }
            }

            return JgsMatrix.FromColumnMajorDims(flat, dims);
        });

        Define("rot90", (args, line, col) =>
        {
            ArityRange("rot90", args, 1, 2, line, col);
            double[,] a = RectOf("rot90", args[0], line, col);
            int quarters = ((args.Count == 2 ? Count("rot90", args, 1, line, col) : 1) % 4 + 4) % 4;
            for (int turn = 0; turn < quarters; turn++)
            {
                int rows = a.GetLength(0);
                int cols = a.GetLength(1);
                var turned = new double[cols, rows];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        // One counter-clockwise quarter turn: the last column becomes the first row.
                        turned[cols - 1 - c, r] = a[r, c];
                    }
                }

                a = turned;
            }

            return FromRect(a);
        });

        Define("accumarray", (args, line, col) =>
        {
            ArityRange("accumarray", args, 2, 5, line, col);
            double[] subscripts = ToDoubles("accumarray", args[0], line, col);
            double[] values = args[1].Type is JgsType.Number or JgsType.Bool
                ? Enumerable.Repeat(args[1].AsNumber, subscripts.Length).ToArray()
                : ToDoubles("accumarray", args[1], line, col);
            if (values.Length != subscripts.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"accumarray got {subscripts.Length} subscripts for {values.Length} values.");
            }

            int size = 0;
            foreach (double subscript in subscripts)
            {
                if (subscript < 1 || subscript != Math.Floor(subscript))
                {
                    throw new JgsRuntimeException(line, col, "accumarray subscripts are whole numbers from 1 up.");
                }

                size = Math.Max(size, (int)subscript);
            }

            if (args.Count >= 3 && args[2].Type is JgsType.Number or JgsType.Bool)
            {
                size = Math.Max(size, (int)args[2].AsNumber);
            }
            else if (args.Count >= 3 && args[2].Type == JgsType.Array)
            {
                double[] shape = ToDoubles("accumarray", args[2], line, col);
                size = Math.Max(size, (int)shape[0]);
            }

            // With a function handle each bin's values are collected and handed over together;
            // without one the bins simply sum, which is what accumarray is nearly always used for.
            IJgsCallable? reducer = args.Count >= 4 && args[3].Type == JgsType.Function ? args[3].AsCallable : null;
            double fill = args.Count >= 5 ? Num("accumarray", args, 4, line, col) : 0;

            if (reducer is null)
            {
                var sums = new double[size];
                Array.Fill(sums, fill);
                var touched = new bool[size];
                for (int i = 0; i < subscripts.Length; i++)
                {
                    int bin = (int)subscripts[i] - 1;
                    sums[bin] = touched[bin] ? sums[bin] + values[i] : values[i];
                    touched[bin] = true;
                }

                return Numbers(sums);
            }

            var buckets = new List<double>[size];
            for (int i = 0; i < subscripts.Length; i++)
            {
                int bin = (int)subscripts[i] - 1;
                (buckets[bin] ??= new List<double>()).Add(values[i]);
            }

            var reduced = new double[size];
            for (int bin = 0; bin < size; bin++)
            {
                if (buckets[bin] is null)
                {
                    reduced[bin] = fill;
                    continue;
                }

                JgsValue outcome = reducer.Call([Numbers(buckets[bin]!.ToArray())], line, col);
                reduced[bin] = outcome.Type is JgsType.Number or JgsType.Bool
                    ? outcome.AsNumber
                    : throw new JgsRuntimeException(line, col, "accumarray: the function must return one number per bin.");
            }

            return Numbers(reduced);
        });
    }

    /// <summary>
    /// Peels a trailing class name off a random-draw call — <c>randi(10, 1, 5, 'int32')</c>, or
    /// <c>'like'</c> followed by a value whose class to copy — and hands back the arguments without
    /// it, so the shape arguments behind it never have to tell a size from a word.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Args, JgsNumericClass? Class, bool Logical) DrawnClass(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count >= 3 && args[^2].Type == JgsType.String
            && string.Equals(args[^2].AsString, "like", StringComparison.OrdinalIgnoreCase))
        {
            // 'like' copies a prototype's class rather than naming one, which is how a script keeps a
            // draw in whatever class the data it is about to join already uses.
            JgsValue prototype = args[^1];
            bool copyLogical = IsLogicalValue(prototype);
            return (args.Take(args.Count - 2).ToArray(), copyLogical ? null : prototype.NumericClass, copyLogical);
        }

        if (args.Count >= 2 && args[^1].Type == JgsType.String)
        {
            string word = args[^1].AsString;
            JgsValue[] trimmed = args.Take(args.Count - 1).ToArray();
            if (string.Equals(word, "logical", StringComparison.OrdinalIgnoreCase))
            {
                return (trimmed, null, true);
            }

            return JgsNumericClasses.Parse(word) is { } numericClass
                ? (trimmed, numericClass, false)
                : throw new JgsRuntimeException(line, col, $"{name}: JGraph has no '{word}' class.");
        }

        return (args, null, false);
    }

    /// <summary>
    /// One dimension's worth of rotation over column-major storage, wrapping around. Reading the
    /// slices through <see cref="JgsMatrix.SlicesAlong"/> rather than indexing by hand is what lets
    /// circshift take a vector of amounts: shifting several dimensions is this, several times.
    /// </summary>
    private static double[] RotateAlong(double[] flat, int[] dims, int dim, int by, int line, int col)
    {
        if (dim < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"circshift: the dimension must be a positive whole number, but was {dim}.");
        }

        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, dim);
        foreach (double[] slice in slices)
        {
            if (slice.Length < 2)
            {
                continue;
            }

            var moved = new double[slice.Length];
            for (int i = 0; i < slice.Length; i++)
            {
                moved[i] = slice[(((i - by) % slice.Length) + slice.Length) % slice.Length];
            }

            moved.CopyTo(slice, 0);
        }

        (double[] joined, _) = JgsMatrix.JoinAlong(slices, dims, dim);
        return joined;
    }
}
