using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Planar storage for a packed complex array: separate real and imaginary buffers of equal length.
/// A million-bin spectrum costs 16 bytes per element instead of a boxed
/// <see cref="System.Numerics.Complex"/> object each (~3.5x less, allocation-free). Elements read
/// back through <see cref="JgsValue.ElementAt"/>, which normalizes zero-imaginary entries to plain
/// numbers — exactly the mix of Number and Complex values the boxed representation would hold.
/// </summary>
internal sealed class JgsPackedComplex : IDisposable
{
    public JgsPackedComplex(NumericBuffer re, NumericBuffer im)
    {
        if (re.Length != im.Length)
        {
            throw new ArgumentException($"plane length mismatch: {re.Length} vs {im.Length}");
        }

        Re = re;
        Im = im;
    }

    /// <summary>The real plane.</summary>
    public NumericBuffer Re { get; }

    /// <summary>The imaginary plane.</summary>
    public NumericBuffer Im { get; }

    /// <summary>Element count.</summary>
    public int Length => Re.Length;

    /// <inheritdoc />
    public void Dispose()
    {
        Re.Dispose();
        Im.Dispose();
    }
}
