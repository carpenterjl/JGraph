using System.Numerics;

namespace JGraph.Signal.Rf;

/// <summary>
/// A frequency-sampled N-port network described by scattering (or, after conversion, impedance,
/// admittance, or ABCD) parameters. Each frequency point carries a full N×N complex matrix. This is
/// the in-memory domain type behind the JGS RF builtins; the scripting layer projects it to and from
/// a <c>Table</c> handle so it can flow through the existing table accessors.
/// </summary>
/// <remarks>
/// Parameters are stored in a single flat <see cref="Complex"/> array laid out
/// frequency-major then row-major (<c>[(f·Ports + i)·Ports + j]</c>), so a whole port pair over
/// frequency is a strided read. Ports and frequency indices are 0-based on this type; the JGS surface
/// exposes MATLAB's 1-based port numbers.
/// </remarks>
public sealed class SParameterNetwork
{
    private readonly Complex[] _data;

    /// <summary>Creates a network over the given frequency grid. <paramref name="data"/> is adopted, not copied.</summary>
    /// <param name="ports">The number of ports N (≥ 1).</param>
    /// <param name="referenceImpedance">The reference impedance Z₀ in ohms (&gt; 0).</param>
    /// <param name="frequencies">The frequency points in hertz, ascending.</param>
    /// <param name="data">The flat parameter array, length <c>frequencies.Length · ports · ports</c>.</param>
    public SParameterNetwork(int ports, double referenceImpedance, double[] frequencies, Complex[] data)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(data);
        if (ports < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ports), "A network needs at least one port.");
        }

        if (referenceImpedance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(referenceImpedance), "The reference impedance must be positive.");
        }

        int expected = frequencies.Length * ports * ports;
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} parameters ({frequencies.Length} points × {ports}×{ports}) but got {data.Length}.",
                nameof(data));
        }

        Ports = ports;
        ReferenceImpedance = referenceImpedance;
        Frequencies = frequencies;
        _data = data;
    }

    /// <summary>The number of ports N.</summary>
    public int Ports { get; }

    /// <summary>The reference impedance Z₀ in ohms.</summary>
    public double ReferenceImpedance { get; }

    /// <summary>The frequency points in hertz.</summary>
    public double[] Frequencies { get; }

    /// <summary>The number of frequency points.</summary>
    public int PointCount => Frequencies.Length;

    /// <summary>The parameter at frequency index <paramref name="f"/> for the (row <paramref name="i"/>, column <paramref name="j"/>) port pair, all 0-based.</summary>
    public Complex this[int f, int i, int j]
    {
        get => _data[((f * Ports) + i) * Ports + j];
        set => _data[((f * Ports) + i) * Ports + j] = value;
    }

    /// <summary>The full parameter array laid out frequency-major then row-major. Callers must not mutate it.</summary>
    public Complex[] Data => _data;

    /// <summary>Extracts the (row <paramref name="i"/>, column <paramref name="j"/>) parameter across all frequencies. Ports are 0-based.</summary>
    public Complex[] Extract(int i, int j)
    {
        if ((uint)i >= (uint)Ports || (uint)j >= (uint)Ports)
        {
            throw new ArgumentOutOfRangeException(
                nameof(i), $"Port indices must be in [0, {Ports - 1}] for a {Ports}-port network.");
        }

        var result = new Complex[PointCount];
        for (int f = 0; f < PointCount; f++)
        {
            result[f] = this[f, i, j];
        }

        return result;
    }
}
