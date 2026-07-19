using System.Globalization;
using System.Numerics;

namespace JGraph.Signal.Rf;

/// <summary>
/// A reader for Touchstone (.sNp) files, the de-facto exchange format for S-parameter data. This
/// supports the version 1.x grammar: an optional <c>!</c>-comment preamble, a single option line
/// (<c># &lt;unit&gt; S &lt;RI|MA|DB&gt; R &lt;z0&gt;</c>), then whitespace-separated numeric rows —
/// one frequency followed by the network parameters as (value, value) pairs, possibly wrapped across
/// several physical lines for higher port counts.
/// </summary>
/// <remarks>
/// Two conventions are easy to get silently wrong and are handled explicitly here:
/// <list type="bullet">
/// <item>For a 2-port file the data order is S11, S21, S12, S22 (column-major) — the off-diagonal
/// terms are swapped relative to the natural row-major reading used for N ≥ 3.</item>
/// <item>Pair encoding depends on the option word: RI is (real, imaginary); MA is (linear magnitude,
/// angle°); DB is (20·log10 magnitude, angle°).</item>
/// </list>
/// Only the S parameter type is accepted; Y/Z/H/G files are rejected with a clear message.
/// </remarks>
public static class Touchstone
{
    private enum PairFormat
    {
        RealImaginary,
        MagnitudeAngle,
        DecibelAngle,
    }

    /// <summary>Reads a Touchstone file, taking the port count from the <c>.sNp</c> extension.</summary>
    /// <exception cref="InvalidDataException">When the file is not decodable Touchstone S-parameter data.</exception>
    public static SParameterNetwork Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int ports = PortsFromExtension(path);
        using var reader = new StreamReader(path);
        return Read(reader, ports);
    }

    /// <summary>Reads Touchstone data for a known port count from a text reader.</summary>
    /// <exception cref="InvalidDataException">When the text is not decodable Touchstone S-parameter data.</exception>
    public static SParameterNetwork Read(TextReader reader, int ports)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (ports < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ports), "A network needs at least one port.");
        }

        // Option-line defaults per the spec: gigahertz, S parameters, magnitude/angle, 50 Ω.
        double frequencyScale = 1e9;
        var format = PairFormat.MagnitudeAngle;
        double referenceImpedance = 50.0;
        bool optionSeen = false;

        var frequencies = new List<double>();
        var matrices = new List<Complex>();
        int perPoint = ports * ports;
        var pending = new List<double>();

        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                if (optionSeen)
                {
                    throw new InvalidDataException("The Touchstone file has more than one option line.");
                }

                ParseOptionLine(line, ref frequencyScale, ref format, ref referenceImpedance);
                optionSeen = true;
                continue;
            }

            foreach (string token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw new InvalidDataException($"Could not parse the Touchstone value '{token}'.");
                }

                pending.Add(value);
            }

            // A full record is one frequency plus 2·N² parameter numbers. Records may wrap across
            // physical lines, so accumulate until a whole record is available, then flush.
            while (pending.Count >= 1 + (2 * perPoint))
            {
                double frequency = pending[0] * frequencyScale;
                frequencies.Add(frequency);
                for (int k = 0; k < perPoint; k++)
                {
                    double a = pending[1 + (2 * k)];
                    double b = pending[2 + (2 * k)];
                    matrices.Add(ToComplex(a, b, format));
                }

                pending.RemoveRange(0, 1 + (2 * perPoint));
            }
        }

        if (pending.Count != 0)
        {
            throw new InvalidDataException(
                $"The Touchstone file ended mid-record with {pending.Count} leftover numbers.");
        }

        if (frequencies.Count == 0)
        {
            throw new InvalidDataException("The Touchstone file has no data points.");
        }

        Complex[] data = matrices.ToArray();
        if (ports == 2)
        {
            SwapTwoPortColumnOrder(data, frequencies.Count);
        }

        return new SParameterNetwork(ports, referenceImpedance, frequencies.ToArray(), data);
    }

    /// <summary>Reads the port count N from a <c>.sNp</c> file extension.</summary>
    public static int PortsFromExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string ext = Path.GetExtension(path);
        if (ext.Length >= 4 &&
            (ext[1] == 's' || ext[1] == 'S') &&
            (ext[^1] == 'p' || ext[^1] == 'P') &&
            int.TryParse(ext.AsSpan(2, ext.Length - 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
            n >= 1)
        {
            return n;
        }

        throw new InvalidDataException(
            $"'{path}' does not have a Touchstone .sNp extension (for example .s2p for a 2-port).");
    }

    private static void ParseOptionLine(
        string line, ref double frequencyScale, ref PairFormat format, ref double referenceImpedance)
    {
        // Grammar: # [freq-unit] [parameter] [format] [R impedance]; any subset, any order, defaults apply.
        string[] tokens = line[1..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int t = 0; t < tokens.Length; t++)
        {
            string token = tokens[t];
            switch (token.ToUpperInvariant())
            {
                case "HZ":
                    frequencyScale = 1.0;
                    break;
                case "KHZ":
                    frequencyScale = 1e3;
                    break;
                case "MHZ":
                    frequencyScale = 1e6;
                    break;
                case "GHZ":
                    frequencyScale = 1e9;
                    break;
                case "RI":
                    format = PairFormat.RealImaginary;
                    break;
                case "MA":
                    format = PairFormat.MagnitudeAngle;
                    break;
                case "DB":
                    format = PairFormat.DecibelAngle;
                    break;
                case "S":
                    break; // scattering parameters — the only supported kind
                case "Y":
                case "Z":
                case "H":
                case "G":
                    throw new InvalidDataException(
                        $"Touchstone '{token}' parameters are not supported; only S parameters are read.");
                case "R":
                    if (t + 1 < tokens.Length &&
                        double.TryParse(tokens[t + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double z0))
                    {
                        referenceImpedance = z0;
                        t++;
                    }

                    break;
                default:
                    // Unknown tokens (e.g. a stray impedance without R) are ignored, matching lenient readers.
                    break;
            }
        }
    }

    private static Complex ToComplex(double a, double b, PairFormat format)
    {
        switch (format)
        {
            case PairFormat.RealImaginary:
                return new Complex(a, b);
            case PairFormat.MagnitudeAngle:
                return Complex.FromPolarCoordinates(a, b * System.Math.PI / 180.0);
            case PairFormat.DecibelAngle:
                double magnitude = System.Math.Pow(10.0, a / 20.0);
                return Complex.FromPolarCoordinates(magnitude, b * System.Math.PI / 180.0);
            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Rewrites 2-port data from the file's S11, S21, S12, S22 order into the row-major S11, S12,
    /// S21, S22 order the rest of the code assumes. Only the two off-diagonal terms move.
    /// </summary>
    private static void SwapTwoPortColumnOrder(Complex[] data, int pointCount)
    {
        for (int f = 0; f < pointCount; f++)
        {
            int baseIndex = f * 4;
            (data[baseIndex + 1], data[baseIndex + 2]) = (data[baseIndex + 2], data[baseIndex + 1]);
        }
    }

    private static string StripComment(string line)
    {
        int bang = line.IndexOf('!');
        return bang < 0 ? line : line[..bang];
    }
}
