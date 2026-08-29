using JGraph.Numerics.Optimization;

namespace JGraph.Scripting.Jgs;

/// <summary>The four optimfun solvers themselves (M99).</summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>[x, fval, exitflag, output] = fminsearch(fun, x0, options, ...)</c>: an unconstrained
    /// minimum found by the Nelder-Mead simplex, using no derivative.
    /// </summary>
    private static JgsValue[] Fminsearch(
        JgsEnvironment env,
        JGraphScriptGlobals host,
        IReadOnlyList<JgsValue> args,
        int wanted,
        int line,
        int col)
    {
        if (AsksForDefaults(args, wanted))
        {
            return [SolverDefaults("fminsearch", line, col)];
        }

        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fminsearch:NotEnoughInputs",
                "FMINSEARCH requires at least two input arguments.");
        }

        JgsValue objectiveValue = args[0];
        JgsValue startValue;
        JgsValue? options = null;
        IReadOnlyList<JgsValue> extra = [];

        if (args.Count == 1)
        {
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fminsearch:InputArg",
                    "FMINSEARCH requires at least two input arguments or a structure with valid fields.");
            }

            JgsValue[] unpacked = UnpackProblem(
                "fminsearch", args[0], ["objective", "x0"], line, col);
            objectiveValue = unpacked[0];
            startValue = unpacked[1];
            if (TryReadField(args[0], "options", out JgsValue given) && !IsUnsetOption(given))
            {
                options = given;
            }
        }
        else
        {
            startValue = args[1];
            if (args.Count > 2 && !IsUnsetOption(args[2]))
            {
                options = args[2];
            }

            extra = args.Count > 3 ? args.Skip(3).ToArray() : [];
        }

        double[] start = DoubleStartingPoint("fminsearch", startValue, line, col);
        int[] dims = SizeDims(startValue);
        OptimSettings settings = ReadOptimSettings(
            "fminsearch", env, options, start.Length, line, col);
        IJgsCallable objective = ObjectiveOf("fminsearch", env, objectiveValue, line, col);

        var display = new OptimIterationDisplay(
            host, settings.Display, " Iteration   Func-count         f(x)         Procedure");
        var callbacks = new OptimCallbacks(settings, dims, extra, line, col);

        SimplexSearch.Result found = SimplexSearch.Minimize(
            ObjectiveCaller("fminsearch", objective, dims, extra, settings.CheckValues, line, col),
            start,
            new SimplexSearch.Settings(
                settings.MaxIterations,
                settings.MaxFunctionEvaluations,
                settings.ToleranceX,
                settings.ToleranceFunction),
            step =>
            {
                display.Simplex(step);
                return callbacks.Invoke(step);
            });

        string message = found.ExitFlag switch
        {
            SearchExit.StoppedByWatcher => "Optimization terminated prematurely by user.",
            SearchExit.BudgetExhausted when found.FunctionCount >= settings.MaxFunctionEvaluations =>
                "Exiting: Maximum number of function evaluations has been exceeded\n"
                + "         - increase MaxFunEvals option.\n"
                + $"         Current function value: {Formatted("%f", found.Value)} \n",
            SearchExit.BudgetExhausted =>
                "Exiting: Maximum number of iterations has been exceeded\n"
                + "         - increase MaxIter option.\n"
                + $"         Current function value: {Formatted("%f", found.Value)} \n",
            _ =>
                "Optimization terminated:\n"
                + $" the current x satisfies the termination criteria using OPTIONS.TolX of "
                + $"{Formatted("%e", settings.ToleranceX)} \n"
                + $" and F(X) satisfies the convergence criteria using OPTIONS.TolFun of "
                + $"{Formatted("%e", settings.ToleranceFunction)} \n",
        };

        display.Ending(
            message,
            found.ExitFlag == SearchExit.Converged ? OptimDisplay.Final : OptimDisplay.Notify,
            blankLineFirst: found.ExitFlag != SearchExit.StoppedByWatcher);

        var output = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["iterations"] = JgsValue.Number(found.Iterations),
            ["funcCount"] = JgsValue.Number(found.FunctionCount),
            ["algorithm"] = JgsValue.Str("Nelder-Mead simplex direct search"),
            ["message"] = JgsValue.Str(message),
        };

        return Outputs(
            wanted,
            ShapedNumbers(found.Solution, dims),
            JgsValue.Number(found.Value),
            JgsValue.Number(found.ExitFlag),
            JgsValue.Struct(output));
    }

    /// <summary>
    /// <c>[x, fval, exitflag, output] = fminbnd(fun, x1, x2, options, ...)</c>: the minimum of a
    /// function of one variable inside a closed interval.
    /// </summary>
    private static JgsValue[] Fminbnd(
        JgsEnvironment env,
        JGraphScriptGlobals host,
        IReadOnlyList<JgsValue> args,
        int wanted,
        int line,
        int col)
    {
        if (AsksForDefaults(args, wanted))
        {
            return [SolverDefaults("fminbnd", line, col)];
        }

        JgsValue objectiveValue;
        JgsValue lowValue;
        JgsValue highValue;
        JgsValue? options = null;
        IReadOnlyList<JgsValue> extra = [];

        if (args.Count == 1)
        {
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fminbnd:InputArg",
                    "The input to FMINBND should be either a structure with valid fields "
                    + "or consist of at least three arguments.");
            }

            JgsValue[] unpacked = UnpackProblem(
                "fminbnd", args[0], ["objective", "x1", "x2"], line, col);
            objectiveValue = unpacked[0];
            lowValue = unpacked[1];
            highValue = unpacked[2];
            if (TryReadField(args[0], "options", out JgsValue given) && !IsUnsetOption(given))
            {
                options = given;
            }
        }
        else
        {
            if (args.Count < 3)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fminbnd:NotEnoughInputs",
                    "FMINBND requires three input arguments.");
            }

            objectiveValue = args[0];
            lowValue = args[1];
            highValue = args[2];
            if (args.Count > 3 && !IsUnsetOption(args[3]))
            {
                options = args[3];
            }

            extra = args.Count > 4 ? args.Skip(4).ToArray() : [];
        }

        double low = FiniteBound(lowValue, line, col);
        double high = FiniteBound(highValue, line, col);
        OptimSettings settings = ReadOptimSettings("fminbnd", env, options, 1, line, col);
        IJgsCallable objective = ObjectiveOf("fminbnd", env, objectiveValue, line, col);

        var display = new OptimIterationDisplay(
            host, settings.Display, " Func-count     x          f(x)         Procedure");
        var callbacks = new OptimCallbacks(settings, [1, 1], extra, line, col);

        // An interval that runs backwards is not searched at all: MATLAB reports the infeasibility
        // and hands back the lower bound untouched, having spent nothing.
        if (low > high)
        {
            const string Infeasible = "Exiting due to infeasibility: the lower bound exceeds the upper bound.";
            display.Ending(Infeasible, OptimDisplay.Notify, blankLineFirst: true);
            var refused = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["iterations"] = JgsValue.Number(0),
                ["funcCount"] = JgsValue.Number(0),
                ["algorithm"] = JgsValue.Str("golden section search, parabolic interpolation"),
                ["message"] = JgsValue.Str(Infeasible),
            };

            return Outputs(
                wanted,
                JgsValue.Array([]),
                JgsValue.Array([]),
                JgsValue.Number(-2),
                JgsValue.Struct(refused));
        }

        Func<double[], double> objectiveCall = ObjectiveCaller(
            "fminbnd", objective, [1, 1], extra, settings.CheckValues, line, col);

        BoundedMinimizer.Result found = BoundedMinimizer.Minimize(
            at => objectiveCall([at]),
            low,
            high,
            new BoundedMinimizer.Settings(
                settings.MaxIterations, settings.MaxFunctionEvaluations, settings.ToleranceX),
            step =>
            {
                display.Bounded(step);
                return callbacks.Invoke(step);
            });

        string message = found.ExitFlag switch
        {
            SearchExit.StoppedByWatcher => "Optimization terminated prematurely by user.",
            SearchExit.BudgetExhausted when found.FunctionCount >= settings.MaxFunctionEvaluations =>
                "Exiting: Maximum number of function evaluations has been exceeded\n"
                + "         - increase MaxFunEvals option.\n"
                + $"         Current function value: {Formatted("%f", found.Value)} \n",
            SearchExit.BudgetExhausted =>
                "Exiting: Maximum number of iterations has been exceeded\n"
                + "         - increase MaxIter option.\n"
                + $"         Current function value: {Formatted("%f", found.Value)} \n",
            _ =>
                "Optimization terminated:\n"
                + $" the current x satisfies the termination criteria using OPTIONS.TolX of "
                + $"{Formatted("%e", settings.ToleranceX)} \n",
        };

        display.Ending(
            message,
            found.ExitFlag == SearchExit.Converged ? OptimDisplay.Final : OptimDisplay.Notify,
            blankLineFirst: found.ExitFlag != SearchExit.StoppedByWatcher);

        var output = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["iterations"] = JgsValue.Number(found.Iterations),
            ["funcCount"] = JgsValue.Number(found.FunctionCount),
            ["algorithm"] = JgsValue.Str("golden section search, parabolic interpolation"),
            ["message"] = JgsValue.Str(message),
        };

        return Outputs(
            wanted,
            JgsValue.Number(found.Solution),
            JgsValue.Number(found.Value),
            JgsValue.Number(found.ExitFlag),
            JgsValue.Struct(output));
    }

    /// <summary>
    /// <c>[x, fval, exitflag, output] = fzero(fun, x0, options, ...)</c>: a zero of a function of one
    /// variable, from a starting guess or from an interval the function changes sign across.
    /// </summary>
    private static JgsValue[] Fzero(
        JgsEnvironment env,
        JGraphScriptGlobals host,
        IReadOnlyList<JgsValue> args,
        int wanted,
        int line,
        int col)
    {
        if (AsksForDefaults(args, wanted))
        {
            return [SolverDefaults("fzero", line, col)];
        }

        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fzero:NotEnoughInputs",
                "FZERO requires at least two input arguments.");
        }

        JgsValue objectiveValue = args[0];
        JgsValue startValue;
        JgsValue? options = null;
        IReadOnlyList<JgsValue> extra = [];

        if (args.Count == 1)
        {
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fzero:InputArg",
                    "FZERO requires at least two input arguments or a structure with valid fields.");
            }

            JgsValue[] unpacked = UnpackProblem("fzero", args[0], ["objective", "x0"], line, col);
            objectiveValue = unpacked[0];
            startValue = unpacked[1];
            if (TryReadField(args[0], "options", out JgsValue given) && !IsUnsetOption(given))
            {
                options = given;
            }
        }
        else
        {
            startValue = args[1];
            if (args.Count > 2 && !IsUnsetOption(args[2]))
            {
                options = args[2];
            }

            extra = args.Count > 3 ? args.Skip(3).ToArray() : [];
        }

        double[] start = DoubleStartingPoint("fzero", startValue, line, col);
        foreach (double coordinate in start)
        {
            if (!double.IsFinite(coordinate))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fzero:Arg2NotFinite",
                    "Second argument must be finite.");
            }
        }

        if (start.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fzero:LengthArg2",
                "Second argument must be a scalar or vector of length 2.");
        }

        OptimSettings settings = ReadOptimSettings("fzero", env, options, 1, line, col);
        IJgsCallable objective = ObjectiveOf("fzero", env, objectiveValue, line, col);
        Func<double[], double> objectiveCall = ObjectiveCaller(
            "fzero", objective, [1, 1], extra, settings.CheckValues, line, col);
        double At(double x) => objectiveCall([x]);

        var display = new OptimRootDisplay(host, settings.Display, start);
        var callbacks = new OptimCallbacks(settings, [1, 1], extra, line, col);
        bool Watch(RootFinder.RootStep step)
        {
            display.Step(step);
            return callbacks.Invoke(step);
        }

        RootFinder.Result found;
        if (start.Length == 2)
        {
            double lowValue = At(start[0]);
            double highValue = At(start[1]);
            if (!double.IsFinite(lowValue) || !double.IsFinite(highValue))
            {
                throw new JgsRuntimeException(line, col,
                    "MATLAB:fzero:ValuesAtEndPtsComplexOrNotFinite",
                    "Function values at interval endpoints must be finite and real.");
            }

            if (lowValue == 0.0 || highValue == 0.0)
            {
                // An end that is already a zero is the answer, and the search never opens.
                bool atLeft = lowValue == 0.0;
                return ZeroFindTerminated(
                    host, settings, wanted, atLeft ? start[0] : start[1], atLeft ? lowValue : highValue, 2);
            }

            if (lowValue > 0 == highValue > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fzero:ValuesAtEndPtsSameSign",
                    "Function values at the interval endpoints must differ in sign.");
            }

            display.OpeningInterval();
            found = RootFinder.SolveBracketed(
                At, start[0], lowValue, start[1], highValue, settings.ToleranceX, Watch,
                intervalIterations: 0, evaluations: 2);
        }
        else
        {
            double atGuess = At(start[0]);
            if (atGuess == 0.0)
            {
                return ZeroFindTerminated(host, settings, wanted, start[0], atGuess, 1);
            }

            if (!double.IsFinite(atGuess))
            {
                throw new JgsRuntimeException(line, col,
                    "MATLAB:fzero:ValueAtInitGuessComplexOrNotFinite",
                    "Initial function value must be finite and real.");
            }

            display.SearchingAround(start[0]);
            found = RootFinder.Solve(At, start[0], settings.ToleranceX, Watch);
        }

        string message = found.ExitFlag switch
        {
            SearchExit.StoppedByWatcher => "Optimization terminated prematurely by user.",
            RootFinder.RootExit.NotFinite =>
                "Exiting fzero: aborting search for an interval containing a sign change\n"
                + "    because NaN or Inf function value encountered during search.\n"
                + $"(Function value at {Formatted("%g", found.FailedAt)} is "
                + $"{Formatted("%g", found.FailedValue)}.)\n"
                + "Check function or try again with a different starting value.",
            RootFinder.RootExit.NoSignChange =>
                "Exiting fzero: aborting search for an interval containing a sign change\n"
                + "    because no sign change is detected during search.\nFunction may not have a root.",
            RootFinder.RootExit.NearSingularity =>
                $"Current point x may be near a singular point. The interval "
                + $"[{Formatted("%g", found.Low)}, {Formatted("%g", found.High)}] \n"
                + "reduced to the requested tolerance and the function changes sign in the interval,\n"
                + "but f(x) increased in magnitude as the interval reduced.",
            _ => $"Zero found in the interval [{Formatted("%g", found.Low)}, "
                + $"{Formatted("%g", found.High)}]",
        };

        display.Ending(
            message,
            found.ExitFlag is SearchExit.StoppedByWatcher or RootFinder.RootExit.NotFinite
                or RootFinder.RootExit.NoSignChange
                ? OptimDisplay.Notify
                : OptimDisplay.Final,
            blankLineFirst: found.ExitFlag is SearchExit.Converged
                or RootFinder.RootExit.NearSingularity);

        var output = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["intervaliterations"] = JgsValue.Number(found.IntervalIterations),
            ["iterations"] = JgsValue.Number(found.Iterations),
            ["funcCount"] = JgsValue.Number(found.FunctionCount),
            ["algorithm"] = JgsValue.Str("bisection, interpolation"),
            ["message"] = JgsValue.Str(message),
        };

        return Outputs(
            wanted,
            JgsValue.Number(found.Solution),
            JgsValue.Number(found.Value),
            JgsValue.Number(found.ExitFlag),
            JgsValue.Struct(output));
    }

    /// <summary>
    /// The answer when a point handed to <c>fzero</c> was already a zero, so no search ever ran.
    /// </summary>
    private static JgsValue[] ZeroFindTerminated(
        JGraphScriptGlobals host, OptimSettings settings, int wanted,
        double at, double value, int evaluations)
    {
        const string Message = "Zero find terminated.";
        if (settings.Display >= OptimDisplay.Final)
        {
            host.WriteOut(Message + "\n");
        }

        var output = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["intervaliterations"] = JgsValue.Number(0),
            ["iterations"] = JgsValue.Number(0),
            ["funcCount"] = JgsValue.Number(evaluations),
            ["algorithm"] = JgsValue.Str("bisection, interpolation"),
            ["message"] = JgsValue.Str(Message),
        };

        return Outputs(
            wanted,
            JgsValue.Number(at),
            JgsValue.Number(value),
            JgsValue.Number(SearchExit.Converged),
            JgsValue.Struct(output));
    }

    /// <summary>
    /// <c>[x, resnorm, residual, exitflag, output, lambda] = lsqnonneg(C, d, options)</c>: the
    /// least-squares solution of C x = d over the non-negative orthant.
    /// </summary>
    private static JgsValue[] Lsqnonneg(
        JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (AsksForDefaults(args, wanted))
        {
            return [SolverDefaults("lsqnonneg", line, col)];
        }

        if (args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:TooManyInputs",
                "Too many input arguments.");
        }

        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:NotEnoughInputs",
                "LSQNONNEG requires at least two input arguments.");
        }

        JgsValue matrixValue;
        JgsValue rightValue;
        JgsValue? options = null;

        if (args.Count == 1)
        {
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:InputArg",
                    "The input should be either a structure with valid fields "
                    + "or at least two arguments to LSQNONNEG.");
            }

            JgsValue[] unpacked = UnpackProblem("lsqnonneg", args[0], ["C", "d"], line, col);
            matrixValue = unpacked[0];
            rightValue = unpacked[1];
            if (TryReadField(args[0], "options", out JgsValue given) && !IsUnsetOption(given))
            {
                options = given;
            }
        }
        else
        {
            matrixValue = args[0];
            rightValue = args[1];

            // The third argument used to be a starting point and is now the options structure. A
            // caller who passes something that is not a structure is passing the old X0, which
            // MATLAB warns about and ignores.
            if (args.Count > 2 && !IsUnsetOption(args[2]))
            {
                if (args[2].Type == JgsType.Struct)
                {
                    options = args[2];
                }
                else
                {
                    host.WriteErr(
                        "Warning: Ignoring input argument X0. The input for X0 will be removed in a "
                        + "future release. See the help for valid syntax.\n");
                }
            }

            if (args.Count > 3 && !IsUnsetOption(args[3]))
            {
                host.WriteErr(
                    "Warning: Ignoring input argument X0. The input for X0 will be removed in a "
                    + "future release. See the help for valid syntax.\n");
                options = args[3].Type == JgsType.Struct ? args[3] : options;
            }
        }

        if (HasComplexPart(matrixValue) || HasComplexPart(rightValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:ComplexCorD",
                "C and d must be real.");
        }

        double[,] matrix = AsRectangle("lsqnonneg", matrixValue, line, col);
        double[] right = FlattenColumnMajor("lsqnonneg", rightValue, line, col);
        if (right.Length != matrix.GetLength(0))
        {
            throw new JgsRuntimeException(line, col,
                "lsqnonneg: d must have one entry per row of C.");
        }

        double tolerance = 0;
        OptimDisplay display = OptimDisplay.Notify;
        if (options is { } given2)
        {
            if (given2.Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:ArgNotStruct",
                    "Argument 4 must be an options structure.");
            }

            display = ReadDisplay("lsqnonneg", ReadOr(given2, "Display", JgsValue.Str("notify")), line, col);
            if (display == OptimDisplay.Iterate)
            {
                host.WriteErr("Warning: 'iter' value not valid for 'Display' parameter for LSQNONNEG.\n");
            }

            JgsValue told = ReadOr(given2, "TolX", JgsValue.Array([]));
            if (told.Type is JgsType.Number or JgsType.Bool)
            {
                tolerance = told.AsNumber;
            }
            else if (told.Type == JgsType.String
                && !string.Equals(told.AsString, "10*eps*norm(c,1)*length(c)",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:OptTolXNotPosScalar",
                    "Option 'TolX' must be an positive scalar if not the default.");
            }
        }

        NonnegativeLeastSquares.Result found = Guarded(
            "lsqnonneg", () => NonnegativeLeastSquares.Solve(matrix, right, tolerance), line, col);

        string message = found.ExitFlag == SearchExit.Converged
            ? "Optimization terminated."
            : "Exiting: Iteration count is exceeded, exiting LSQNONNEG.\n"
                + "Try raising the tolerance (OPTIONS.TolX).";

        if (display >= OptimDisplay.Final
            || (display == OptimDisplay.Notify && found.ExitFlag != SearchExit.Converged))
        {
            host.WriteOut(message + "\n");
        }

        var output = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["iterations"] = JgsValue.Number(found.Iterations),
            ["algorithm"] = JgsValue.Str("active-set"),
            ["message"] = JgsValue.Str(message),
        };

        // x, the residual and the multipliers are all columns, whatever shape d arrived in: they are
        // indexed by C's columns and rows, not by how the caller happened to write the data.
        return Outputs(
            wanted,
            Column(found.Solution),
            JgsValue.Number(found.ResidualNormSquared),
            Column(found.Residual),
            JgsValue.Number(found.ExitFlag),
            JgsValue.Struct(output),
            Column(found.Dual));
    }

    /// <summary>One field of an options structure, or <paramref name="fallback"/> when it is unset.</summary>
    private static JgsValue ReadOr(JgsValue structure, string field, JgsValue fallback) =>
        TryReadField(structure, field, out JgsValue value) && !IsUnsetOption(value) ? value : fallback;

    /// <summary>
    /// The starting point a solver was given, refused unless it is real and of class double.
    /// </summary>
    /// <remarks>
    /// MATLAB checks this by hand — <c>~isa(x,'double')</c> — and it is not pedantry: an integer
    /// class saturates rather than overflowing, so a simplex walking outwards on an <c>int32</c>
    /// start would silently stop moving at the class limit.
    /// </remarks>
    private static double[] DoubleStartingPoint(string solver, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Complex
            || (value.Type is JgsType.Bool or JgsType.String)
            || value.NumericClass != JgsNumericClass.Double)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:NonDoubleInput",
                $"{solver.ToUpperInvariant()} accepts inputs only of data type double.");
        }

        return FlattenColumnMajor(solver, value, line, col);
    }

    /// <summary>One end of <c>fminbnd</c>'s interval: a finite real scalar double, and nothing else.</summary>
    private static double FiniteBound(JgsValue value, int line, int col)
    {
        if (value.Type is not (JgsType.Number or JgsType.Array)
            || value.NumericClass != JgsNumericClass.Double)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fminbnd:InvalidBoundInput",
                "Bounds must be finite scalars of data type double.");
        }

        double[] flat = FlattenColumnMajor("fminbnd", value, line, col);
        if (flat.Length != 1 || !double.IsFinite(flat[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fminbnd:InvalidBoundInput",
                "Bounds must be finite scalars of data type double.");
        }

        return flat[0];
    }
}
