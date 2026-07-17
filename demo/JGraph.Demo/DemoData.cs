namespace JGraph.Demo;

/// <summary>Small helpers for generating sample data for the gallery.</summary>
internal static class DemoData
{
    public static double[] Linspace(double start, double end, int count)
    {
        var result = new double[count];
        if (count == 1)
        {
            result[0] = start;
            return result;
        }

        double step = (end - start) / (count - 1);
        for (int i = 0; i < count; i++)
        {
            result[i] = start + (i * step);
        }

        return result;
    }

    public static double[] Map(double[] xs, Func<double, double> f)
    {
        var result = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            result[i] = f(xs[i]);
        }

        return result;
    }

    /// <summary>Generates an ascending-X random-walk series of the requested length.</summary>
    public static (double[] Xs, double[] Ys) RandomWalk(int count, int seed = 7)
    {
        var xs = new double[count];
        var ys = new double[count];
        var random = new Random(seed);
        double y = 0;
        for (int i = 0; i < count; i++)
        {
            xs[i] = i;
            y += random.NextDouble() - 0.5;
            ys[i] = y;
        }

        return (xs, ys);
    }

    public static (double[] Xs, double[] Ys) Cluster(int count, double cx, double cy, double spread, int seed)
    {
        var xs = new double[count];
        var ys = new double[count];
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            xs[i] = cx + ((random.NextDouble() - 0.5) * spread);
            ys[i] = cy + ((random.NextDouble() - 0.5) * spread);
        }

        return (xs, ys);
    }

    /// <summary>Draws normally distributed samples via the Box–Muller transform.</summary>
    public static double[] Gaussian(int count, double mean, double standardDeviation, int seed)
    {
        var samples = new double[count];
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double z = System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
            samples[i] = mean + (z * standardDeviation);
        }

        return samples;
    }

    /// <summary>Generates a linear chirp sweeping from <paramref name="startHz"/> to <paramref name="endHz"/>.</summary>
    public static double[] Chirp(int count, double sampleRate, double startHz, double endHz)
    {
        var signal = new double[count];
        double duration = count / sampleRate;
        double rate = duration > 0 ? (endHz - startHz) / duration : 0; // Hz per second
        for (int i = 0; i < count; i++)
        {
            double t = i / sampleRate;
            // Instantaneous frequency startHz + rate·t ⇒ phase 2π(startHz·t + ½·rate·t²).
            signal[i] = System.Math.Sin(2 * System.Math.PI * ((startHz * t) + (0.5 * rate * t * t)));
        }

        return signal;
    }

    /// <summary>Generates a smoothed, noisy NRZ (±1) bit stream suitable for an eye diagram.</summary>
    public static double[] NrzEye(int symbols, int samplesPerSymbol, double noise, int seed)
    {
        var random = new Random(seed);
        var bits = new double[symbols];
        for (int k = 0; k < symbols; k++)
        {
            bits[k] = random.Next(2) == 0 ? -1.0 : 1.0;
        }

        var signal = new double[symbols * samplesPerSymbol];
        for (int i = 0; i < signal.Length; i++)
        {
            int sym = i / samplesPerSymbol;
            double frac = (i % samplesPerSymbol) / (double)samplesPerSymbol;
            double a = bits[sym];
            double b = sym + 1 < symbols ? bits[sym + 1] : a;
            double smoothed = a + ((b - a) * (0.5 - (0.5 * System.Math.Cos(System.Math.PI * frac))));
            signal[i] = smoothed + ((random.NextDouble() - 0.5) * noise);
        }

        return signal;
    }

    /// <summary>Samples a scalar field <c>f(x, y)</c> over a [rows, cols] grid spanning the given extents.</summary>
    public static double[,] Field(int rows, int cols, double x0, double x1, double y0, double y1, Func<double, double, double> f)
    {
        var field = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            double y = y0 + ((y1 - y0) * r / System.Math.Max(1, rows - 1));
            for (int c = 0; c < cols; c++)
            {
                double x = x0 + ((x1 - x0) * c / System.Math.Max(1, cols - 1));
                field[r, c] = f(x, y);
            }
        }

        return field;
    }
}
