namespace JGraph.Numerics.Optimization;

/// <summary>Where a search is in its life when it reports.</summary>
public enum SearchPhase
{
    /// <summary>Before the first step, once.</summary>
    Init,

    /// <summary>After a step that moved the search along.</summary>
    Iterate,

    /// <summary>After the last step, once; a stop asked for here is not honoured.</summary>
    Done,
}

/// <summary>
/// One report from a running search: what it has found so far, how much it has spent getting there,
/// and what it just did.
/// </summary>
/// <param name="Phase">Where in its life the search is.</param>
/// <param name="Iteration">Iterations taken so far.</param>
/// <param name="FunctionCount">Objective evaluations spent so far.</param>
/// <param name="Value">The objective at <paramref name="Point"/>.</param>
/// <param name="Procedure">
/// What the step just taken was — <c>expand</c>, <c>bisection</c>, and so on. Empty before the
/// first step. These spellings are MATLAB's and reach a script through <c>optimValues.procedure</c>,
/// so they are part of the contract rather than commentary.
/// </param>
/// <param name="Point">The best point so far. The array belongs to the search; copy it to keep it.</param>
public readonly record struct SearchStep(
    SearchPhase Phase,
    int Iteration,
    int FunctionCount,
    double Value,
    string Procedure,
    double[] Point);

/// <summary>
/// Something watching a search — a display, a plot, a script's own output function. Returning true
/// asks the search to stop, which it does at the next point where stopping leaves a usable answer.
/// </summary>
public delegate bool SearchWatcher(SearchStep step);

/// <summary>Why a search ended.</summary>
public static class SearchExit
{
    /// <summary>The tolerances were met.</summary>
    public const int Converged = 1;

    /// <summary>The iteration or evaluation budget ran out first.</summary>
    public const int BudgetExhausted = 0;

    /// <summary>A watcher asked it to stop.</summary>
    public const int StoppedByWatcher = -1;
}
