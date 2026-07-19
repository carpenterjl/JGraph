using System.Numerics;

namespace JGraph.Signal.Rf;

/// <summary>
/// Closed-form conversions between the network representations of a 1- or 2-port: scattering (S),
/// impedance (Z), admittance (Y), and chain (ABCD) parameters, plus 2-port cascading and the
/// input/output reflection coefficients. Every conversion works per frequency point and preserves
/// the reference impedance and frequency grid.
/// </summary>
/// <remarks>
/// Ports above two are rejected: the general n-port S↔Z↔Y conversions require complex matrix
/// inversion, which is out of scope for this milestone. ABCD parameters are defined only for 2-ports.
/// </remarks>
public static class NetworkConversions
{
    /// <summary>Converts S parameters to impedance (Z) parameters.</summary>
    public static SParameterNetwork SToZ(SParameterNetwork net) =>
        Map(net, "S→Z", (s, z0) =>
        {
            if (s.Length == 1)
            {
                // 1-port: Z = Z0·(1 + S)/(1 − S).
                Complex s11 = s[0];
                return [z0 * (1 + s11) / (1 - s11)];
            }

            (Complex s11b, Complex s12, Complex s21, Complex s22) = Unpack(s);
            Complex d = ((1 - s11b) * (1 - s22)) - (s12 * s21);
            return
            [
                z0 * (((1 + s11b) * (1 - s22)) + (s12 * s21)) / d,
                z0 * (2 * s12) / d,
                z0 * (2 * s21) / d,
                z0 * (((1 - s11b) * (1 + s22)) + (s12 * s21)) / d,
            ];
        });

    /// <summary>Converts impedance (Z) parameters to S parameters.</summary>
    public static SParameterNetwork ZToS(SParameterNetwork net) =>
        Map(net, "Z→S", (z, z0) =>
        {
            if (z.Length == 1)
            {
                Complex z11 = z[0];
                return [(z11 - z0) / (z11 + z0)];
            }

            (Complex z11b, Complex z12, Complex z21, Complex z22) = Unpack(z);
            Complex d = ((z11b + z0) * (z22 + z0)) - (z12 * z21);
            return
            [
                (((z11b - z0) * (z22 + z0)) - (z12 * z21)) / d,
                (2 * z12 * z0) / d,
                (2 * z21 * z0) / d,
                (((z11b + z0) * (z22 - z0)) - (z12 * z21)) / d,
            ];
        });

    /// <summary>Converts S parameters to admittance (Y) parameters.</summary>
    public static SParameterNetwork SToY(SParameterNetwork net) =>
        Map(net, "S→Y", (s, z0) =>
        {
            double y0 = 1.0 / z0;
            if (s.Length == 1)
            {
                Complex s11 = s[0];
                return [y0 * (1 - s11) / (1 + s11)];
            }

            (Complex s11b, Complex s12, Complex s21, Complex s22) = Unpack(s);
            Complex d = ((1 + s11b) * (1 + s22)) - (s12 * s21);
            return
            [
                y0 * (((1 - s11b) * (1 + s22)) + (s12 * s21)) / d,
                y0 * (-2 * s12) / d,
                y0 * (-2 * s21) / d,
                y0 * (((1 + s11b) * (1 - s22)) + (s12 * s21)) / d,
            ];
        });

    /// <summary>Converts admittance (Y) parameters to S parameters.</summary>
    public static SParameterNetwork YToS(SParameterNetwork net) =>
        Map(net, "Y→S", (y, z0) =>
        {
            double y0 = 1.0 / z0;
            if (y.Length == 1)
            {
                Complex y11 = y[0];
                return [(y0 - y11) / (y0 + y11)];
            }

            (Complex y11b, Complex y12, Complex y21, Complex y22) = Unpack(y);
            Complex d = ((y0 + y11b) * (y0 + y22)) - (y12 * y21);
            return
            [
                (((y0 - y11b) * (y0 + y22)) + (y12 * y21)) / d,
                (-2 * y12 * y0) / d,
                (-2 * y21 * y0) / d,
                (((y0 + y11b) * (y0 - y22)) + (y12 * y21)) / d,
            ];
        });

    /// <summary>Converts 2-port S parameters to chain (ABCD) parameters.</summary>
    public static SParameterNetwork SToAbcd(SParameterNetwork net)
    {
        RequireTwoPort(net, "S→ABCD");
        return Map(net, "S→ABCD", (s, z0) =>
        {
            (Complex s11, Complex s12, Complex s21, Complex s22) = Unpack(s);
            Complex twoS21 = 2 * s21;
            return
            [
                (((1 + s11) * (1 - s22)) + (s12 * s21)) / twoS21,
                z0 * (((1 + s11) * (1 + s22)) - (s12 * s21)) / twoS21,
                (((1 - s11) * (1 - s22)) - (s12 * s21)) / (z0 * twoS21),
                (((1 - s11) * (1 + s22)) + (s12 * s21)) / twoS21,
            ];
        });
    }

    /// <summary>Converts 2-port chain (ABCD) parameters to S parameters.</summary>
    public static SParameterNetwork AbcdToS(SParameterNetwork net)
    {
        RequireTwoPort(net, "ABCD→S");
        return Map(net, "ABCD→S", (m, z0) =>
        {
            (Complex a, Complex b, Complex c, Complex d) = Unpack(m);
            Complex denom = a + (b / z0) + (c * z0) + d;
            return
            [
                (a + (b / z0) - (c * z0) - d) / denom,
                2 * ((a * d) - (b * c)) / denom,
                2 / denom,
                (-a + (b / z0) - (c * z0) + d) / denom,
            ];
        });
    }

    /// <summary>Cascades two 2-port networks (port 2 of <paramref name="first"/> into port 1 of <paramref name="second"/>).</summary>
    /// <exception cref="ArgumentException">When the networks are not both 2-port, share no common Z₀, or have different frequency grids.</exception>
    public static SParameterNetwork Cascade(SParameterNetwork first, SParameterNetwork second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        RequireTwoPort(first, "cascade");
        RequireTwoPort(second, "cascade");
        if (first.PointCount != second.PointCount)
        {
            throw new ArgumentException("Cascaded networks must have the same number of frequency points.");
        }

        for (int f = 0; f < first.PointCount; f++)
        {
            if (System.Math.Abs(first.Frequencies[f] - second.Frequencies[f]) >
                1e-6 * System.Math.Max(1.0, System.Math.Abs(first.Frequencies[f])))
            {
                throw new ArgumentException("Cascaded networks must share the same frequency grid.");
            }
        }

        SParameterNetwork abcd1 = SToAbcd(first);
        SParameterNetwork abcd2 = SToAbcd(second);
        var product = new Complex[first.PointCount * 4];
        for (int f = 0; f < first.PointCount; f++)
        {
            int b = f * 4;
            // 2×2 chain-matrix product: the electrical meaning of cascading in ABCD form.
            product[b + 0] = (abcd1[f, 0, 0] * abcd2[f, 0, 0]) + (abcd1[f, 0, 1] * abcd2[f, 1, 0]);
            product[b + 1] = (abcd1[f, 0, 0] * abcd2[f, 0, 1]) + (abcd1[f, 0, 1] * abcd2[f, 1, 1]);
            product[b + 2] = (abcd1[f, 1, 0] * abcd2[f, 0, 0]) + (abcd1[f, 1, 1] * abcd2[f, 1, 0]);
            product[b + 3] = (abcd1[f, 1, 0] * abcd2[f, 0, 1]) + (abcd1[f, 1, 1] * abcd2[f, 1, 1]);
        }

        var cascaded = new SParameterNetwork(
            2, first.ReferenceImpedance, (double[])first.Frequencies.Clone(), product);
        return AbcdToS(cascaded);
    }

    /// <summary>The input reflection coefficient Γ_in looking into port 1 with the given load termination.</summary>
    /// <param name="net">A 1- or 2-port network.</param>
    /// <param name="loadImpedance">The load impedance at port 2 (ignored for a 1-port). Default: matched (Z₀).</param>
    public static Complex[] GammaIn(SParameterNetwork net, Complex? loadImpedance = null)
    {
        ArgumentNullException.ThrowIfNull(net);
        if (net.Ports == 1)
        {
            return net.Extract(0, 0);
        }

        RequireTwoPort(net, "gammain");
        double z0 = net.ReferenceImpedance;
        Complex zl = loadImpedance ?? new Complex(z0, 0);
        Complex gammaL = (zl - z0) / (zl + z0);
        var result = new Complex[net.PointCount];
        for (int f = 0; f < net.PointCount; f++)
        {
            Complex s11 = net[f, 0, 0], s12 = net[f, 0, 1], s21 = net[f, 1, 0], s22 = net[f, 1, 1];
            result[f] = s11 + (s12 * s21 * gammaL / (1 - (s22 * gammaL)));
        }

        return result;
    }

    /// <summary>The output reflection coefficient Γ_out looking into port 2 with the given source termination.</summary>
    /// <param name="net">A 2-port network.</param>
    /// <param name="sourceImpedance">The source impedance at port 1. Default: matched (Z₀).</param>
    public static Complex[] GammaOut(SParameterNetwork net, Complex? sourceImpedance = null)
    {
        ArgumentNullException.ThrowIfNull(net);
        RequireTwoPort(net, "gammaout");
        double z0 = net.ReferenceImpedance;
        Complex zs = sourceImpedance ?? new Complex(z0, 0);
        Complex gammaS = (zs - z0) / (zs + z0);
        var result = new Complex[net.PointCount];
        for (int f = 0; f < net.PointCount; f++)
        {
            Complex s11 = net[f, 0, 0], s12 = net[f, 0, 1], s21 = net[f, 1, 0], s22 = net[f, 1, 1];
            result[f] = s22 + (s12 * s21 * gammaS / (1 - (s11 * gammaS)));
        }

        return result;
    }

    /// <summary>The voltage standing-wave ratio (1 + |Γ|)/(1 − |Γ|) for each reflection coefficient.</summary>
    public static double[] Vswr(ReadOnlySpan<Complex> gamma)
    {
        var result = new double[gamma.Length];
        for (int i = 0; i < gamma.Length; i++)
        {
            double magnitude = gamma[i].Magnitude;
            result[i] = (1 + magnitude) / (1 - magnitude);
        }

        return result;
    }

    private static SParameterNetwork Map(
        SParameterNetwork net, string operation, Func<Complex[], double, Complex[]> perPoint)
    {
        ArgumentNullException.ThrowIfNull(net);
        if (net.Ports > 2)
        {
            throw new NotSupportedException(
                $"{operation} is supported for 1- and 2-port networks only (this one has {net.Ports} ports).");
        }

        int perMatrix = net.Ports * net.Ports;
        double z0 = net.ReferenceImpedance;
        var data = new Complex[net.PointCount * perMatrix];
        var buffer = new Complex[perMatrix];
        for (int f = 0; f < net.PointCount; f++)
        {
            for (int k = 0; k < perMatrix; k++)
            {
                buffer[k] = net[f, k / net.Ports, k % net.Ports];
            }

            Complex[] converted = perPoint(buffer, z0);
            Array.Copy(converted, 0, data, f * perMatrix, perMatrix);
        }

        return new SParameterNetwork(net.Ports, z0, (double[])net.Frequencies.Clone(), data);
    }

    private static (Complex, Complex, Complex, Complex) Unpack(Complex[] m) => (m[0], m[1], m[2], m[3]);

    private static void RequireTwoPort(SParameterNetwork net, string operation)
    {
        if (net.Ports != 2)
        {
            throw new NotSupportedException(
                $"{operation} requires a 2-port network (this one has {net.Ports} ports).");
        }
    }
}
