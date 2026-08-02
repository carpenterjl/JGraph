using System.Numerics;
using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave I: two-dimensional filter design (<c>freqspace</c>, <c>freqz2</c>, <c>fsamp2</c>,
/// <c>ftrans2</c>, <c>fwind1</c>, <c>fwind2</c>, <c>convmtx2</c>), the spread-function/transfer-function
/// pair, and the four deblurring methods plus Gabor filtering.
/// </summary>
/// <remarks>
/// Design goes the opposite way round from filtering: a script says what response it wants and gets a
/// kernel back, rather than handing over a kernel and getting a picture. Everything here answers with
/// plain numbers for that reason — a kernel, a response, a transfer function — and only the
/// deblurring family and <c>edgetaper</c> hand back something that is still a picture.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly ImgOptionSpec GaborSpec = new(
        "gabor",
        [],
        ["SpatialFrequencyBandwidth", "SpatialAspectRatio"]);

    private static readonly ImgOptionSpec GaborFiltSpec = new(
        "imgaborfilt",
        [],
        ["SpatialFrequencyBandwidth", "SpatialAspectRatio"]);

    private static void DefineDesignBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define)
    {
        // --- Frequency grids and responses -------------------------------------------------------
        define("freqspace", (args, line, col) => FreqSpaceOutputs(args, 1, line, col)[0]);
        define("freqz2", (args, line, col) => FreqZ2Outputs(args, 1, line, col)[0]);

        // --- Design ------------------------------------------------------------------------------
        define("fsamp2", (args, line, col) =>
        {
            ArityRange("fsamp2", args, 1, 4, line, col);
            try
            {
                if (args.Count == 1)
                {
                    return MatrixToRows(FilterDesign.FromSamples(
                        Rectangle("fsamp2 argument 1", args[0], line, col)));
                }

                if (args.Count != 4)
                {
                    throw new JgsRuntimeException(line, col,
                        "fsamp2 takes either a sampled response on its own, or f1, f2, the response " +
                        "and the size to design at.");
                }

                double[] fx = NumericVector("fsamp2", args, 0, line, col);
                double[] fy = NumericVector("fsamp2", args, 1, line, col);
                double[,] desired = Rectangle("fsamp2 argument 3", args[2], line, col);
                (int rows, int cols) = WindowOf("fsamp2", args[3], line, col);
                return MatrixToRows(FilterDesign.FromSamples(fx, fy, desired, rows, cols));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"fsamp2: {ex.Message}");
            }
        });

        define("ftrans2", (args, line, col) =>
        {
            ArityRange("ftrans2", args, 1, 2, line, col);
            double[] b = NumericVector("ftrans2", args, 0, line, col);
            double[,] transform = args.Count == 2
                ? Matrix("ftrans2", args, 1, line, col)
                : FilterDesign.McClellan;
            try
            {
                return MatrixToRows(FilterDesign.FrequencyTransform(b, transform));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"ftrans2: {ex.Message}");
            }
        });

        define("fwind1", (args, line, col) => WindowedDesign("fwind1", args, line, col));
        define("fwind2", (args, line, col) => WindowedDesign("fwind2", args, line, col));

        define("convmtx2", (args, line, col) =>
        {
            ArityRange("convmtx2", args, 2, 3, line, col);
            double[,] kernel = Matrix("convmtx2", args, 0, line, col);
            (int rows, int cols) = args.Count == 3
                ? (Count("convmtx2", args, 1, line, col), Count("convmtx2", args, 2, line, col))
                : WindowOf("convmtx2", args[1], line, col);
            try
            {
                return MatrixToRows(FilterDesign.ConvolutionMatrix(kernel, rows, cols));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"convmtx2: {ex.Message}");
            }
        });

        // --- Spread functions and transfer functions ---------------------------------------------
        define("psf2otf", (args, line, col) =>
        {
            ArityRange("psf2otf", args, 1, 2, line, col);
            double[,] psf = Matrix("psf2otf", args, 0, line, col);
            (int height, int width) = args.Count == 2
                ? WindowOf("psf2otf", args[1], line, col)
                : (psf.GetLength(0), psf.GetLength(1));
            try
            {
                return ComplexGrid(FilterDesign.PsfToOtf(psf, height, width), height, width);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"psf2otf: {ex.Message}");
            }
        });

        define("otf2psf", (args, line, col) =>
        {
            ArityRange("otf2psf", args, 1, 2, line, col);
            (Complex[] otf, int height, int width) = ComplexRect("otf2psf", args[0], line, col);
            (int rows, int cols) = args.Count == 2
                ? WindowOf("otf2psf", args[1], line, col)
                : (height, width);
            try
            {
                return MatrixToRows(FilterDesign.OtfToPsf(otf, height, width, rows, cols));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"otf2psf: {ex.Message}");
            }
        });

        define("edgetaper", (args, line, col) =>
        {
            Arity("edgetaper", args, 2, line, col);
            using ImgArg source = ImgLike("edgetaper", args, 0, line, col);
            double[,] psf = Matrix("edgetaper", args, 1, line, col);
            try
            {
                return PerChannel(source, plane => FilterDesign.EdgeTaper(plane, psf));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"edgetaper: {ex.Message}");
            }
        });

        // --- Deblurring --------------------------------------------------------------------------
        define("deconvwnr", (args, line, col) =>
        {
            ArityRange("deconvwnr", args, 2, 4, line, col);
            using ImgArg source = ImgLike("deconvwnr", args, 0, line, col);
            double[,] psf = Matrix("deconvwnr", args, 1, line, col);
            int height = source.Buffer.Height;
            int width = source.Buffer.Width;

            double ratio = 0;
            double[]? spectrum = null;
            if (args.Count == 3)
            {
                spectrum = NoiseToSignal("deconvwnr", args[2], height, width, line, col, out ratio);
            }
            else if (args.Count == 4)
            {
                // MATLAB's other reading: the autocorrelations of the noise and of the picture, from
                // which the ratio at every frequency follows by the Wiener–Khinchin relation.
                double[] noise = PowerOf("deconvwnr", args[2], height, width, line, col);
                double[] signal = PowerOf("deconvwnr", args[3], height, width, line, col);
                spectrum = new double[noise.Length];
                for (int i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = Math.Abs(signal[i]) < 1e-12 ? 0 : Math.Max(0, noise[i] / signal[i]);
                }
            }

            try
            {
                double[] captured = spectrum ?? [];
                return PerChannel(source, plane => Deconvolution.Wiener(
                    plane, psf, spectrum is null ? null : captured, ratio));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"deconvwnr: {ex.Message}");
            }
        });

        define("deconvreg", (args, line, col) => DeconvRegOutputs(args, 1, line, col)[0]);

        define("deconvlucy", (args, line, col) =>
        {
            ArityRange("deconvlucy", args, 2, 7, line, col);
            using ImgArg source = ImgLike("deconvlucy", args, 0, line, col);
            double[,] psf = Matrix("deconvlucy", args, 1, line, col);
            int iterations = args.Count >= 3 ? Count("deconvlucy", args, 2, line, col) : 10;
            double damping = args.Count >= 4 ? Num("deconvlucy", args, 3, line, col) : 0;
            double[,]? weight = args.Count >= 5
                ? Matrix("deconvlucy", args, 4, line, col)
                : null;
            double readout = args.Count >= 6 ? Num("deconvlucy", args, 5, line, col) : 0;
            if (args.Count == 7 && Count("deconvlucy", args, 6, line, col) != 1)
            {
                throw new JgsRuntimeException(line, col,
                    "deconvlucy: sub-sampling is not implemented; the spread function must be given " +
                    "at the picture's own resolution.");
            }

            try
            {
                return PerChannel(source, plane =>
                    Deconvolution.Lucy(plane, psf, iterations, damping, weight, readout));
            }
            catch (ArgumentException ex)
            {
                // ArgumentOutOfRangeException is one of these, and it is what too few iterations or a
                // spread function larger than the picture raises.
                throw new JgsRuntimeException(line, col, $"deconvlucy: {ex.Message}");
            }
        });

        define("deconvblind", (args, line, col) => DeconvBlindOutputs(args, 1, line, col)[0]);

        // --- Gabor -------------------------------------------------------------------------------
        define("gabor", (args, line, col) =>
        {
            ArityRange("gabor", args, 2, 6, line, col);
            ImgArgs parsed = GaborSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col,
                    "gabor(wavelength, orientation) needs both a wavelength and a direction.");
            }

            double[] wavelengths = NumericVector("gabor", parsed.Positional, 0, line, col);
            double[] orientations = NumericVector("gabor", parsed.Positional, 1, line, col);
            double bandwidth = parsed.Scalar("SpatialFrequencyBandwidth", 1.0);
            double aspect = parsed.Scalar("SpatialAspectRatio", 0.5);

            var bank = new List<JgsValue>();
            foreach (double wavelength in wavelengths)
            {
                foreach (double orientation in orientations)
                {
                    var parameters = new GaborParameters(wavelength, orientation, bandwidth, aspect);
                    try
                    {
                        parameters.Validate();
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        throw new JgsRuntimeException(line, col, $"gabor: {ex.Message}");
                    }

                    bank.Add(GaborValue(parameters));
                }
            }

            // One filter is one filter, not a bank of one: a script that asks for a single wavelength
            // and direction writes g.Wavelength, not g{1}.Wavelength.
            return bank.Count == 1 ? bank[0] : JgsValue.Cell([.. bank]);
        });

        define("imgaborfilt", (args, line, col) => GaborFiltOutputs(args, 1, line, col)[0]);
    }

    /// <summary><c>[f1, f2] = freqspace(n)</c>, and the one-output form that is a half axis.</summary>
    private static JgsValue[] FreqSpaceOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("freqspace", args, 1, 2, line, col);
        double[] size = NumericVector("freqspace", args, 0, line, col);
        if (size.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                "freqspace takes a size or an [m n] pair.");
        }

        int rows = WholeSize(size[0]);
        int cols = WholeSize(size[^1]);

        bool grid = false;
        if (args.Count == 2)
        {
            string word = Str("freqspace", args, 1, line, col);
            if (!word.Equals("meshgrid", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"freqspace: unknown option '{word}' (the only one is 'meshgrid').");
            }

            grid = true;
        }

        if (wanted < 2)
        {
            // One output is the one-dimensional form: the distinct half of the circle, which is what a
            // 1-D response is quoted against.
            return [Numbers(FilterDesign.HalfAxis(cols))];
        }

        double[] fx = FilterDesign.Axis(cols);
        double[] fy = FilterDesign.Axis(rows);
        if (!grid)
        {
            return [Numbers(fx), Numbers(fy)];
        }

        return
        [
            JgsMatrix.Build(rows, cols, (_, c) => fx[c]),
            JgsMatrix.Build(rows, cols, (r, _) => fy[r]),
        ];
    }

    /// <summary><c>[H, f1, f2] = freqz2(h, …)</c>.</summary>
    private static JgsValue[] FreqZ2Outputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("freqz2", args, 1, 3, line, col);
        double[,] kernel = Matrix("freqz2", args, 0, line, col);

        double[] fx;
        double[] fy;
        if (args.Count == 1)
        {
            fx = FilterDesign.Axis(64);
            fy = FilterDesign.Axis(64);
        }
        else if (args.Count == 2)
        {
            (int rows, int cols) = WindowOf("freqz2", args[1], line, col);
            fx = FilterDesign.Axis(cols);
            fy = FilterDesign.Axis(rows);
        }
        else if (args[1].Type == JgsType.Number && args[2].Type == JgsType.Number)
        {
            // Two bare numbers are counts, not frequencies: freqz2(h, n1, n2) is n2 rows by n1
            // columns, because the first names the horizontal axis.
            fx = FilterDesign.Axis(Count("freqz2", args, 1, line, col));
            fy = FilterDesign.Axis(Count("freqz2", args, 2, line, col));
        }
        else
        {
            fx = NumericVector("freqz2", args, 1, line, col);
            fy = NumericVector("freqz2", args, 2, line, col);
        }

        Complex[,] response = FilterDesign.Response(kernel, fx, fy);
        if (wanted < 2)
        {
            return [FromComplexRect(response)];
        }

        return wanted < 3
            ? [FromComplexRect(response), Numbers(fx)]
            : [FromComplexRect(response), Numbers(fx), Numbers(fy)];
    }

    /// <summary>Shared body for <c>fwind1</c> and <c>fwind2</c>: the same design, different windows.</summary>
    private static JgsValue WindowedDesign(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool rotated = name == "fwind1";
        ArityRange(name, args, 2, rotated ? 5 : 4, line, col);

        // Either the response alone, or the frequency points it was sampled at and then the response.
        bool named = args.Count >= 4 || (args.Count == 3 && !rotated);
        int responseAt = named ? 2 : 0;
        double[,] desired = Rectangle($"{name} argument {responseAt + 1}", args[responseAt], line, col);

        double[,] window;
        int windowAt = responseAt + 1;
        if (rotated && args.Count > windowAt + 1)
        {
            window = FilterDesign.OuterWindow(
                NumericVector(name, args, windowAt, line, col),
                NumericVector(name, args, windowAt + 1, line, col));
        }
        else if (rotated)
        {
            window = FilterDesign.RotateWindow(NumericVector(name, args, windowAt, line, col));
        }
        else
        {
            window = Matrix(name, args, windowAt, line, col);
        }

        try
        {
            if (!named)
            {
                return MatrixToRows(FilterDesign.Windowed(desired, window));
            }

            double[] fx = NumericVector(name, args, 0, line, col);
            double[] fy = NumericVector(name, args, 1, line, col);
            return MatrixToRows(FilterDesign.Windowed(fx, fy, desired, window));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary><c>[J, lagra] = deconvreg(I, psf, np, lrange, regop)</c>.</summary>
    private static JgsValue[] DeconvRegOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("deconvreg", args, 2, 5, line, col);
        using ImgArg source = ImgLike("deconvreg", args, 0, line, col);
        double[,] psf = Matrix("deconvreg", args, 1, line, col);
        double noisePower = args.Count >= 3 ? Num("deconvreg", args, 2, line, col) : 0;

        double lower = 1e-9;
        double upper = 1e9;
        if (args.Count >= 4)
        {
            double[] range = NumericVector("deconvreg", args, 3, line, col);
            if (range.Length is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col,
                    "deconvreg: the multiplier range is one number or a [low high] pair.");
            }

            lower = range[0];
            upper = range.Length == 2 ? range[1] : range[0];
        }

        double[,] regularizer = args.Count >= 5
            ? Matrix("deconvreg", args, 4, line, col)
            : Deconvolution.Laplacian;

        double lagrange = 0;
        JgsValue restored;
        try
        {
            restored = PerChannel(source, plane =>
            {
                (double[,] answer, double chosen) = Deconvolution.Regularized(
                    plane, psf, noisePower, lower, upper, regularizer);
                lagrange = chosen;
                return answer;
            });
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"deconvreg: {ex.Message}");
        }

        return wanted < 2 ? [restored] : [restored, JgsValue.Number(lagrange)];
    }

    /// <summary><c>[J, psf] = deconvblind(I, initpsf, …)</c>.</summary>
    private static JgsValue[] DeconvBlindOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("deconvblind", args, 2, 6, line, col);
        using ImgArg source = ImgLike("deconvblind", args, 0, line, col);
        double[,] guess = Matrix("deconvblind", args, 1, line, col);
        int iterations = args.Count >= 3 ? Count("deconvblind", args, 2, line, col) : 10;
        double damping = args.Count >= 4 ? Num("deconvblind", args, 3, line, col) : 0;
        double[,]? weight = args.Count >= 5 ? Matrix("deconvblind", args, 4, line, col) : null;
        double readout = args.Count >= 6 ? Num("deconvblind", args, 5, line, col) : 0;

        double[,] found = guess;
        JgsValue restored;
        try
        {
            restored = PerChannel(source, plane =>
            {
                (double[,] answer, double[,] blur) = Deconvolution.Blind(
                    plane, guess, iterations, damping, weight, readout);
                found = blur;
                return answer;
            });
        }
        catch (ArgumentException ex)
        {
            // ArgumentOutOfRangeException is one of these, and it is what too few iterations raises.
            throw new JgsRuntimeException(line, col, $"deconvblind: {ex.Message}");
        }

        return wanted < 2 ? [restored] : [restored, MatrixToRows(found)];
    }

    /// <summary><c>[mag, phase] = imgaborfilt(I, …)</c>.</summary>
    private static JgsValue[] GaborFiltOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imgaborfilt", args, 2, 7, line, col);
        using ImgArg source = ImgLike("imgaborfilt", args, 0, line, col);
        if (source.Buffer.Channels != 1)
        {
            throw new JgsRuntimeException(line, col,
                "imgaborfilt works on one channel at a time; convert the picture with rgb2gray or " +
                "pick a plane with imsplit.");
        }

        double[,] values = PointOps.ToMatrix(source.Buffer, 0);
        List<GaborParameters> bank = GaborBank(args, line, col);

        int height = values.GetLength(0);
        int width = values.GetLength(1);
        var magnitudes = new double[height * width * bank.Count];
        var phases = new double[height * width * bank.Count];
        for (int k = 0; k < bank.Count; k++)
        {
            (double[,] magnitude, double[,] phase) = GaborFilters.Apply(values, bank[k]);
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    int at = r + (c * height) + (k * height * width);
                    magnitudes[at] = magnitude[r, c];
                    phases[at] = phase[r, c];
                }
            }
        }

        JgsValue magnitudeValue = bank.Count == 1
            ? JgsMatrix.FromColumnMajorDims(magnitudes, [height, width])
            : JgsMatrix.FromColumnMajorDims(magnitudes, [height, width, bank.Count]);
        if (wanted < 2)
        {
            return [magnitudeValue];
        }

        JgsValue phaseValue = bank.Count == 1
            ? JgsMatrix.FromColumnMajorDims(phases, [height, width])
            : JgsMatrix.FromColumnMajorDims(phases, [height, width, bank.Count]);
        return [magnitudeValue, phaseValue];
    }

    /// <summary>
    /// The filters <c>imgaborfilt</c> was asked for, whether named directly or handed over as a bank
    /// <c>gabor</c> built.
    /// </summary>
    private static List<GaborParameters> GaborBank(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 2 && GaborParametersOf(args[1]) is { } single)
        {
            return [single];
        }

        if (args.Count == 2 && args[1].Type == JgsType.Cell)
        {
            var bank = new List<GaborParameters>();
            foreach (JgsValue element in args[1].AsCell)
            {
                bank.Add(GaborParametersOf(element) ?? throw new JgsRuntimeException(line, col,
                    "imgaborfilt: the bank holds something that is not a Gabor filter."));
            }

            if (bank.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "imgaborfilt: the bank is empty.");
            }

            return bank;
        }

        ImgArgs parsed = GaborFiltSpec.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 3)
        {
            throw new JgsRuntimeException(line, col,
                "imgaborfilt(I, wavelength, orientation) needs both a wavelength and a direction, " +
                "or a bank built with gabor.");
        }

        double[] wavelengths = NumericVector("imgaborfilt", parsed.Positional, 1, line, col);
        double[] orientations = NumericVector("imgaborfilt", parsed.Positional, 2, line, col);
        if (wavelengths.Length != 1 || orientations.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                "imgaborfilt takes one wavelength and one direction; for several, build a bank with " +
                "gabor and pass that.");
        }

        var parameters = new GaborParameters(
            wavelengths[0],
            orientations[0],
            parsed.Scalar("SpatialFrequencyBandwidth", 1.0),
            parsed.Scalar("SpatialAspectRatio", 0.5));
        try
        {
            parameters.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"imgaborfilt: {ex.Message}");
        }

        return [parameters];
    }

    /// <summary>Wraps one Gabor filter as the tagged struct a script sees.</summary>
    private static JgsValue GaborValue(GaborParameters parameters) =>
        JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [TransformTag] = JgsValue.Str("gabor"),
            ["Wavelength"] = JgsValue.Number(parameters.Wavelength),
            ["Orientation"] = JgsValue.Number(parameters.OrientationDegrees),
            ["SpatialFrequencyBandwidth"] = JgsValue.Number(parameters.Bandwidth),
            ["SpatialAspectRatio"] = JgsValue.Number(parameters.AspectRatio),
        });

    /// <summary>Reads a tagged Gabor struct back, or null when the value is not one.</summary>
    private static GaborParameters? GaborParametersOf(JgsValue value)
    {
        if (value.Type != JgsType.Struct ||
            !value.AsStruct.TryGetValue(TransformTag, out JgsValue? tag) ||
            tag is null || tag.Type != JgsType.String || tag.AsString != "gabor")
        {
            return null;
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        return new GaborParameters(
            fields["Wavelength"].AsNumber,
            fields["Orientation"].AsNumber,
            fields["SpatialFrequencyBandwidth"].AsNumber,
            fields["SpatialAspectRatio"].AsNumber);
    }

    /// <summary>
    /// Applies a plane-at-a-time operation to whatever arrived, and hands the result back in the same
    /// shape and class.
    /// </summary>
    /// <remarks>
    /// Deblurring is defined on one channel: the blur is the same for all three of a colour picture,
    /// but the arithmetic is not coupled across them, so each is done on its own and the picture is
    /// put back together.
    /// </remarks>
    private static JgsValue PerChannel(ImgArg source, Func<double[,], double[,]> operation)
    {
        ImageBuffer image = source.Buffer;
        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int channel = 0; channel < image.Channels; channel++)
        {
            double[,] answer = operation(PointOps.ToMatrix(image, channel));
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    result[r, c, channel] = answer[r, c];
                }
            }
        }

        return ImgLikeOut(result, source);
    }

    /// <summary>
    /// A noise-to-signal argument, which MATLAB lets be one number for the whole picture or a spectrum
    /// with a value at every frequency.
    /// </summary>
    private static double[]? NoiseToSignal(
        string name, JgsValue value, int height, int width, int line, int col, out double constant)
    {
        constant = 0;
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            constant = value.AsNumber;
            return null;
        }

        double[,] given = Rectangle($"{name} argument 3", value, line, col);
        if (given.GetLength(0) != height || given.GetLength(1) != width)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the noise-to-signal spectrum is {given.GetLength(0)}-by-{given.GetLength(1)} " +
                $"but the picture is {height}-by-{width}.");
        }

        var spectrum = new double[height * width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                spectrum[(r * width) + c] = given[r, c];
            }
        }

        return spectrum;
    }

    /// <summary>
    /// A power spectrum from either a plain power or an autocorrelation, which is the pair of readings
    /// <c>deconvwnr</c>'s four-argument form accepts.
    /// </summary>
    private static double[] PowerOf(
        string name, JgsValue value, int height, int width, int line, int col)
    {
        var spectrum = new double[height * width];
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            Array.Fill(spectrum, value.AsNumber);
            return spectrum;
        }

        double[,] correlation = Rectangle($"{name} autocorrelation", value, line, col);
        if (correlation.GetLength(0) > height || correlation.GetLength(1) > width)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the autocorrelation is larger than the picture.");
        }

        return Deconvolution.PowerSpectrum(correlation, height, width);
    }

    /// <summary>A complex grid as a script value.</summary>
    private static JgsValue ComplexGrid(Complex[] grid, int height, int width)
    {
        var rect = new Complex[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                rect[r, c] = grid[(r * width) + c];
            }
        }

        return FromComplexRect(rect);
    }

    /// <summary>Reads a complex matrix argument as a row-major grid.</summary>
    /// <remarks>
    /// A transfer function is genuinely complex — that is the whole point of it — so this cannot go
    /// through the ordinary numeric readers, which turn a complex element into an error rather than
    /// into a number.
    /// </remarks>
    private static (Complex[] Grid, int Height, int Width) ComplexRect(
        string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
        {
            return ([value.AsComplex], 1, 1);
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects a transfer function, but got a {value.TypeName}.");
        }

        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length != 2 || dims[0] == 0 || dims[1] == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the transfer function must be a non-empty matrix.");
        }

        int height = dims[0];
        int width = dims[1];
        var grid = new Complex[height * width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                // Column-major storage, row-major grid: the one place the two conventions meet.
                grid[(r * width) + c] = value.ElementAt(r + (c * height)).AsComplex;
            }
        }

        return (grid, height, width);
    }
}
