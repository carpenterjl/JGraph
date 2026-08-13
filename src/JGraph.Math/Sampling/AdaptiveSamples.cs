namespace JGraph.Maths.Sampling;

/// <summary>
/// What <see cref="AdaptiveSampler1D"/> found: the parameter values it chose, in increasing order, and
/// one row of readings per component of the curve.
/// </summary>
/// <remarks>
/// A graph plot has one component — the parameter is x and the component is y. A plane curve given
/// parametrically has two, a space curve three. The parameter row is kept in every case because a
/// caller that means to draw a graph needs it as the x data, and a caller that does not can ignore it.
/// </remarks>
public sealed class AdaptiveSamples
{
    internal AdaptiveSamples(double[] parameters, double[][] components, int poles)
    {
        Parameters = parameters;
        Components = components;
        PoleCount = poles;
    }

    /// <summary>The parameter values sampled, in increasing order.</summary>
    public double[] Parameters { get; }

    /// <summary>The readings, indexed by component and then by sample.</summary>
    public double[][] Components { get; }

    /// <summary>The first component, which is the whole answer for a graph plot.</summary>
    public double[] Values => Components[0];

    /// <summary>How many breaks were left in the curve because it ran away there.</summary>
    /// <remarks>
    /// One per pole rather than one per dropped reading: a run of readings crowded against a pole is
    /// collapsed to a single gap before this is counted. A reading the function itself gave as
    /// infinite or undefined is a gap too, but it is not counted here — nothing was decided about it.
    /// </remarks>
    public int PoleCount { get; }

    /// <summary>How many readings were taken.</summary>
    public int Count => Parameters.Length;
}
