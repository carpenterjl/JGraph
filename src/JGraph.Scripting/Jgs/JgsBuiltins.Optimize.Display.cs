using JGraph.Numerics.Optimization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a solver says while it runs and who it says it to (M99): the iteration table, the output and
/// plot callbacks, and the three plot functions MATLAB ships for the latter to name.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The per-iteration table <c>fminsearch</c> and <c>fminbnd</c> print under
    /// <c>Display</c> <c>'iter'</c>, and the closing message all four print.
    /// </summary>
    /// <remarks>
    /// The header is written the first time a row is, not when the display is built, so that a solve
    /// which refuses its arguments before taking a step prints nothing at all.
    /// </remarks>
    private sealed class OptimIterationDisplay(
        JGraphScriptGlobals host, OptimDisplay level, string header)
    {
        private bool _headerWritten;

        /// <summary>Prints one row of <c>fminsearch</c>'s table.</summary>
        public void Simplex(SearchStep step)
        {
            if (level != OptimDisplay.Iterate || step.Phase == SearchPhase.Done)
            {
                return;
            }

            // fminsearch reports twice before its simplex exists; the opening one is the watcher
            // being opened, not a row.
            if (step.Phase == SearchPhase.Init)
            {
                return;
            }

            WriteHeader();
            host.WriteOut(JgsSprintf.FormatMatlab(
                " %5.0f        %5.0f     %12.6g         %s\n",
                [
                    JgsValue.Number(step.Iteration),
                    JgsValue.Number(step.FunctionCount),
                    JgsValue.Number(step.Value),
                    JgsValue.Str(step.Procedure),
                ]));
        }

        /// <summary>Prints one row of <c>fminbnd</c>'s table.</summary>
        public void Bounded(SearchStep step)
        {
            if (level != OptimDisplay.Iterate
                || step.Phase != SearchPhase.Iterate
                || step.Point.Length == 0)
            {
                return;
            }

            WriteHeader();
            host.WriteOut(JgsSprintf.FormatMatlab(
                "%5.0f   %12.6g %12.6g %s\n",
                [
                    JgsValue.Number(step.FunctionCount),
                    JgsValue.Number(step.Point[0]),
                    JgsValue.Number(step.Value),
                    JgsValue.Str(PaddedProcedure(step.Procedure)),
                ]));
        }

        /// <summary>
        /// Prints the closing message, if this display's level is at least
        /// <paramref name="atLeast"/>.
        /// </summary>
        /// <param name="message">What to say.</param>
        /// <param name="atLeast">
        /// How loud the display must be set for this message to be worth saying. A solve that
        /// converged is only worth announcing at <c>'final'</c>; one that ran out of budget is worth
        /// announcing at <c>'notify'</c>, which is the whole point of that level.
        /// </param>
        /// <param name="blankLineFirst">
        /// Whether a line holding a single space precedes it. A message that follows a table needs
        /// the separation and an interrupted solve does not, and MATLAB spells the two differently
        /// in its own source rather than uniformly.
        /// </param>
        public void Ending(string message, OptimDisplay atLeast, bool blankLineFirst)
        {
            if (level < atLeast)
            {
                return;
            }

            // MATLAB separates with disp(' '), which is a line holding one space rather than an
            // empty one: invisible on screen, and not in a captured transcript.
            host.WriteOut((blankLineFirst ? " \n" : string.Empty) + message + "\n");
        }

        /// <summary>
        /// <c>fminbnd</c>'s procedure names carry seven leading spaces, because the column they are
        /// printed in has no separator of its own. That padding reaches a script through
        /// <c>optimValues.procedure</c> as well, so it belongs to the value rather than to the
        /// formatting.
        /// </summary>
        public static string PaddedProcedure(string procedure) =>
            procedure.Length == 0 ? procedure : "       " + procedure;

        private void WriteHeader()
        {
            if (_headerWritten)
            {
                return;
            }

            _headerWritten = true;
            host.WriteOut(" \n" + header + "\n");
        }
    }

    /// <summary>
    /// What <c>fzero</c> prints under <c>Display</c> <c>'iter'</c>: two tables, one for the outward
    /// search for a sign change and one for the zero-finding inside the bracket it found.
    /// </summary>
    private sealed class OptimRootDisplay(JGraphScriptGlobals host, OptimDisplay level, double[] start)
    {
        private bool _searchHeaderWritten;
        private bool _zeroHeaderWritten;

        /// <summary>Announces the outward search, before the first widening step.</summary>
        public void SearchingAround(double guess)
        {
            if (level != OptimDisplay.Iterate)
            {
                return;
            }

            host.WriteOut(" \n" + JgsSprintf.FormatMatlab(
                "Search for an interval around %g containing a sign change:\n",
                [JgsValue.Number(guess)]));
        }

        /// <summary>
        /// Announces the zero-finding over an interval the caller supplied, which is a blank line
        /// and nothing else: the interval is the caller's own, so there is nothing to tell them
        /// about it that they did not just write. The outward search from a single guess does have
        /// something to say, and says it in <see cref="SearchingAround"/>.
        /// </summary>
        public void OpeningInterval()
        {
            if (level == OptimDisplay.Iterate)
            {
                host.WriteOut(" \n");
            }
        }

        /// <summary>Prints one row of whichever of the two tables the step belongs to.</summary>
        public void Step(RootFinder.RootStep step)
        {
            if (level != OptimDisplay.Iterate || !step.HasPoint || step.Phase == SearchPhase.Done)
            {
                return;
            }

            // A step that carries a bracket is a widening step; one that does not is a zero-finding
            // step, and the two have different tables.
            if (step.Procedure is "initial interval" or "search")
            {
                if (!_searchHeaderWritten)
                {
                    _searchHeaderWritten = true;
                    host.WriteOut(
                        " Func-count    a          f(a)             b          f(b)        Procedure\n");
                }

                host.WriteOut(JgsSprintf.FormatMatlab(
                    "%5.0f   %13.6g %13.6g %13.6g %13.6g   %s\n",
                    [
                        JgsValue.Number(step.FunctionCount),
                        JgsValue.Number(step.Low),
                        JgsValue.Number(step.LowValue),
                        JgsValue.Number(step.High),
                        JgsValue.Number(step.HighValue),
                        JgsValue.Str(step.Procedure),
                    ]));
                return;
            }

            if (!_zeroHeaderWritten)
            {
                _zeroHeaderWritten = true;
                if (start.Length == 1)
                {
                    host.WriteOut(" \n" + JgsSprintf.FormatMatlab(
                        "Search for a zero in the interval [%g, %g]:\n",
                        [JgsValue.Number(step.Low), JgsValue.Number(step.High)]));
                }

                host.WriteOut(" Func-count    x          f(x)             Procedure\n");
            }

            host.WriteOut(JgsSprintf.FormatMatlab(
                "%5.0f   %13.6g %13.6g        %s\n",
                [
                    JgsValue.Number(step.FunctionCount),
                    JgsValue.Number(step.Point),
                    JgsValue.Number(step.Value),
                    JgsValue.Str(step.Procedure),
                ]));
        }

        /// <summary>
        /// Prints the closing message, if this display's level is at least
        /// <paramref name="atLeast"/>.
        /// </summary>
        /// <param name="message">What to say.</param>
        /// <param name="atLeast">
        /// How loud the display must be set for this message to be worth saying. A solve that
        /// converged is only worth announcing at <c>'final'</c>; one that ran out of budget is worth
        /// announcing at <c>'notify'</c>, which is the whole point of that level.
        /// </param>
        /// <param name="blankLineFirst">
        /// Whether a line holding a single space precedes it. A message that follows a table needs
        /// the separation and an interrupted solve does not, and MATLAB spells the two differently
        /// in its own source rather than uniformly.
        /// </param>
        public void Ending(string message, OptimDisplay atLeast, bool blankLineFirst)
        {
            if (level < atLeast)
            {
                return;
            }

            // MATLAB separates with disp(' '), which is a line holding one space rather than an
            // empty one: invisible on screen, and not in a captured transcript.
            host.WriteOut((blankLineFirst ? " \n" : string.Empty) + message + "\n");
        }
    }

    /// <summary>
    /// The output and plot functions a solver was given, and the <c>optimValues</c> structure they
    /// are handed at each step.
    /// </summary>
    /// <remarks>
    /// Every one of them is called, in order, on every reported step; a truthy answer from any one
    /// stops the solve, and the rest are still called first, because MATLAB writes the disjunction
    /// that way and a plot function that keeps state would otherwise miss the step that stopped it.
    /// </remarks>
    private sealed class OptimCallbacks(
        OptimSettings settings, int[] dims, IReadOnlyList<JgsValue> extra, int line, int col)
    {
        /// <summary>Reports one step of a minimize; true when something asked it to stop.</summary>
        public bool Invoke(SearchStep step)
        {
            if (!settings.HasCallbacks)
            {
                return false;
            }

            var values = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["iteration"] = JgsValue.Number(step.Iteration),
                ["funccount"] = JgsValue.Number(step.FunctionCount),
                ["fval"] = double.IsNaN(step.Value) && step.Point.Length == 0
                    ? JgsValue.Array([])
                    : JgsValue.Number(step.Value),
                ["procedure"] = JgsValue.Str(step.Procedure),
            };

            JgsValue point = step.Point.Length == 0
                ? JgsValue.Array([])
                : ShapedNumbers(step.Point, dims);
            return Fire(point, values, step.Phase);
        }

        /// <summary>Reports one step of a zero find; true when something asked it to stop.</summary>
        public bool Invoke(RootFinder.RootStep step)
        {
            if (!settings.HasCallbacks)
            {
                return false;
            }

            JgsValue Maybe(double value) =>
                step.HasPoint && !double.IsNaN(value) ? JgsValue.Number(value) : JgsValue.Array([]);

            var values = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["funccount"] = JgsValue.Number(step.FunctionCount),
                ["iteration"] = JgsValue.Number(step.Iteration),
                ["intervaliteration"] = JgsValue.Number(step.IntervalIteration),
                ["fval"] = Maybe(step.Value),
                ["procedure"] = JgsValue.Str(step.Procedure),
                ["intervala"] = Maybe(step.Low),
                ["fvala"] = Maybe(step.LowValue),
                ["intervalb"] = Maybe(step.High),
                ["fvalb"] = Maybe(step.HighValue),
            };

            return Fire(Maybe(step.Point), values, step.Phase);
        }

        private bool Fire(JgsValue point, Dictionary<string, JgsValue> values, SearchPhase phase)
        {
            JgsValue state = JgsValue.Str(phase switch
            {
                SearchPhase.Init => "init",
                SearchPhase.Done => "done",
                _ => "iter",
            });

            var arguments = new List<JgsValue>(3 + extra.Count)
            {
                point, JgsValue.Struct(values), state,
            };
            arguments.AddRange(extra);

            bool stop = false;
            foreach (IJgsCallable callable in settings.OutputFunctions)
            {
                stop |= Truthy(callable.Call(arguments, line, col));
            }

            foreach (IJgsCallable callable in settings.PlotFunctions)
            {
                stop |= Truthy(callable.Call(arguments, line, col));
            }

            // A callback may ask to stop at any point, but there is nothing left to stop once the
            // solve is done, so the answer to a closing report is discarded, as MATLAB discards it.
            return stop && phase != SearchPhase.Done;
        }

        /// <summary>
        /// Whether a callback's answer means "stop". A function that returns nothing at all is not
        /// asking to stop: MATLAB's own <c>optimplot</c> functions return a <c>stop</c> of false and
        /// a script's may simply forget to.
        /// </summary>
        private static bool Truthy(JgsValue answer) => answer.Type switch
        {
            JgsType.Bool or JgsType.Number => answer.AsNumber != 0,
            JgsType.Array => answer.ArrayLength > 0 && AllNonZero(answer),
            _ => false,
        };

        private static bool AllNonZero(JgsValue array)
        {
            foreach (JgsValue element in array.BoxedElements())
            {
                if (element.Type is not (JgsType.Bool or JgsType.Number) || element.AsNumber == 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // --- The plot functions -------------------------------------------------------------------------

    /// <summary>
    /// Registers the three plot functions <c>PlotFcns</c> exists to name.
    /// </summary>
    /// <remarks>
    /// MATLAB documents no syntax table for these, because they are not written to be called
    /// directly: a solver calls them, with the same three arguments every output function gets. They
    /// are here because <c>optimset('PlotFcns', @optimplotfval)</c> is the documented way to watch a
    /// solve, and without them that option names nothing.
    /// </remarks>
    private static void RegisterOptimPlotBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        Define("optimplotfval", (args, line, col) => PlotOptimValue(
            "optimplotfval", args, "fval", "Current Function Value: ", "Function value", line, col));
        Define("optimplotfunccount", (args, line, col) => PlotOptimValue(
            "optimplotfunccount", args, "funccount", "Total Function Count: ", "Function count",
            line, col));
        Define("optimplotx", PlotOptimPoint);
    }

    /// <summary>
    /// One scalar out of <c>optimValues</c>, drawn against the iteration number: a line that grows a
    /// point per iteration, which is what <c>optimplotfval</c> and <c>optimplotfunccount</c> both are.
    /// </summary>
    private static JgsValue PlotOptimValue(
        string name,
        IReadOnlyList<JgsValue> args,
        string field,
        string titlePrefix,
        string label,
        int line,
        int col)
    {
        if (args.Count < 2 || args[1].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} is called by a solver with (x, optimValues, state).");
        }

        if (!TryReadField(args[1], field, out JgsValue value)
            || value.Type is not (JgsType.Number or JgsType.Bool))
        {
            return JgsValue.Bool(false);
        }

        TryReadField(args[1], "iteration", out JgsValue iteration);
        double at = iteration.Type is JgsType.Number or JgsType.Bool ? iteration.AsNumber : 0;

        JGraph.Api.JG.Plot([at], [value.AsNumber], "o-");
        JGraph.Api.JG.Title(titlePrefix + JgsSprintf.FormatMatlab("%g", [value]));
        JGraph.Api.JG.XLabel("Iteration");
        JGraph.Api.JG.YLabel(label);
        return JgsValue.Bool(false);
    }

    /// <summary>
    /// The current point as a bar per parameter, which is <c>optimplotx</c>: it answers "where is the
    /// search now" for a problem with more than one unknown, where a line against iteration cannot.
    /// </summary>
    private static JgsValue PlotOptimPoint(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col,
                "optimplotx is called by a solver with (x, optimValues, state).");
        }

        double[] point = FlattenColumnMajor("optimplotx", args[0], line, col);
        if (point.Length == 0)
        {
            return JgsValue.Bool(false);
        }

        var positions = new double[point.Length];
        for (int i = 0; i < point.Length; i++)
        {
            positions[i] = i + 1;
        }

        JGraph.Api.JG.Bar(positions, point);
        JGraph.Api.JG.Title("Current Point");
        JGraph.Api.JG.XLabel("Number of variables: " + Whole(point.Length));
        JGraph.Api.JG.YLabel("Current point");
        return JgsValue.Bool(false);
    }
}
