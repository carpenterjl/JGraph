namespace JGraph.Numerics;

/// <summary>
/// What an integration did, kept so the solution can be read again afterwards.
/// </summary>
/// <remarks>
/// A solver that only returns points has thrown away the thing that makes a solution a
/// <em>function</em> rather than a table: the polynomial it carried across each step. Handing one of
/// these to <see cref="OdeSolvers.DormandPrince"/> keeps them, which is what lets a caller ask the
/// answer for a time nobody named at the time.
/// </remarks>
public sealed class OdeRecording
{
    /// <summary>Every accepted step, in the order it was taken.</summary>
    public List<OdeSolvers.OdeStep> Steps { get; } = [];

    /// <summary>How many attempted steps were rejected by the error test.</summary>
    public int Failed { get; internal set; }

    /// <summary>How many times the derivative was called.</summary>
    public int Evaluations { get; internal set; }
}
