using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M67 wave C: the fan-beam family, and the surface that carries a picture.
/// <para>
/// The four fan verbs are one rebinning kernel used twice with the Radon pair on either side:
/// <c>para2fan</c> reads a parallel sinogram where the fan rays fall, <c>fan2para</c> reads a fan
/// sinogram where the parallel rays fall, <c>fanbeam</c> is <c>radon</c> then the first of those, and
/// <c>ifanbeam</c> is the second of those then <c>iradon</c>. Writing them this way is what makes the
/// pair checkable: <c>fan2para</c> undoes <c>para2fan</c> exactly, up to interpolation.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The words <c>FanSensorGeometry</c> takes.</summary>
    private static readonly string[] FanGeometries = ["arc", "line"];

    private static void RegisterFanBeamBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define)
    {
        define("fanbeam", (args, line, col) => FanBeamOutputs(args, 1, line, col)[0]);
        define("ifanbeam", (args, line, col) => IFanBeamOutputs(args, 1, line, col)[0]);
        define("fan2para", (args, line, col) => Fan2ParaOutputs(args, 1, line, col)[0]);
        define("para2fan", (args, line, col) => Para2FanOutputs(args, 1, line, col)[0]);
        define("warp", Warp);
    }

    // --- The geometry every fan verb reads -----------------------------------------------------------

    /// <summary>What a fan verb was told about where its rays and detectors are.</summary>
    private readonly record struct FanSetup(
        double Distance,
        FanBeamTransform.SensorGeometry Geometry,
        double SensorSpacing,
        double RotationIncrement);

    private static readonly string[] FanOptionNames =
    [
        "FanSensorGeometry", "FanSensorSpacing", "FanRotationIncrement", "FanCoverage",
        "Filter", "FrequencyScaling", "Interpolation", "OutputSize", "ParallelSensorSpacing",
        "ParallelRotationIncrement", "ParallelCoverage",
    ];

    private static (FanSetup Setup, ParsedArgs Options) FanOptions(
        string verb, IReadOnlyList<JgsValue> args, int positionals, int line, int col)
    {
        var spec = new OptionSpec(verb, [], FanOptionNames);
        ParsedArgs parsed = spec.Parse(args, positionals, line, col);
        if (parsed.Positional.Count < positionals)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} takes the data and the vertex-to-centre distance D before any options.");
        }

        double distance = ScalarOf($"{verb}: D", parsed.Positional[1], line, col);
        if (!(distance > 0))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: D is the distance from the fan's vertex to the centre of rotation, in pixels, "
                + "so it has to be positive.");
        }

        FanBeamTransform.SensorGeometry geometry = FanBeamTransform.SensorGeometry.Arc;
        if (parsed.Named("FanSensorGeometry") is { } word)
        {
            geometry = OneOfWord(verb, word, FanGeometries, line, col) == "line"
                ? FanBeamTransform.SensorGeometry.Line
                : FanBeamTransform.SensorGeometry.Arc;
        }

        // MATLAB's defaults, and they are different quantities: a degree between rays on an arc, a
        // pixel between detectors on a line.
        double spacing = parsed.Named("FanSensorSpacing") is { } given
            ? ScalarOf($"{verb}: FanSensorSpacing", given, line, col)
            : 1;
        double increment = parsed.Named("FanRotationIncrement") is { } step
            ? ScalarOf($"{verb}: FanRotationIncrement", step, line, col)
            : 1;
        if (!(spacing > 0) || !(increment > 0))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the sensor spacing and the rotation increment are both positive steps.");
        }

        // 'minimal' sweeps only the angles a reconstruction strictly needs, which is a different set
        // of rotation angles rather than a subset of these — so it is refused by name.
        if (parsed.Named("FanCoverage") is { } coverage
            && OneOfWord(verb, coverage, ["cycle", "minimal"], line, col) == "minimal")
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: 'minimal' coverage sweeps a different set of angles from a full cycle, "
                + "and only 'cycle' is worked out here.");
        }

        return (new FanSetup(distance, geometry, spacing, increment), parsed);
    }

    /// <summary>The rotation angles a full cycle at this increment visits.</summary>
    private static double[] FanRotations(double increment)
    {
        int count = System.Math.Max(1, (int)System.Math.Round(360 / increment));
        var angles = new double[count];
        for (int i = 0; i < count; i++)
        {
            angles[i] = i * increment;
        }

        return angles;
    }

    /// <summary>The parallel projection angles a half turn at this increment visits.</summary>
    private static double[] ParallelAngles(double increment)
    {
        int count = System.Math.Max(1, (int)System.Math.Round(180 / increment));
        var angles = new double[count];
        for (int i = 0; i < count; i++)
        {
            angles[i] = i * increment;
        }

        return angles;
    }

    /// <summary>The signed distances a parallel sinogram of <paramref name="count"/> rows stands for.</summary>
    private static double[] ParallelOffsets(int count, double spacing)
    {
        int half = (count - 1) / 2;
        var offsets = new double[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = (i - half) * spacing;
        }

        return offsets;
    }

    // --- fanbeam and ifanbeam ------------------------------------------------------------------------

    /// <summary><c>[F, sensor_positions, fan_rotation_angles] = fanbeam(I, D, …)</c>.</summary>
    private static JgsValue[] FanBeamOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "fanbeam takes the picture and the distance from the fan's vertex to the centre.");
        }

        (FanSetup setup, _) = FanOptions("fanbeam", args, 2, line, col);
        using ImgArg source = ImgLike("fanbeam", args, 0, line, col);
        double[,] image = PointOps.ToMatrix(source.Buffer, 0);

        // The parallel sinogram is taken over a half turn at the fan's own angular step, which is the
        // finest the rebinning below can use anything from.
        double[] theta = ParallelAngles(setup.RotationIncrement);
        (double[,] parallel, double[] offsets) = RadonTransform.Forward(image, theta);

        double radius = System.Math.Sqrt(
            (image.GetLength(0) * image.GetLength(0)) + (image.GetLength(1) * image.GetLength(1))) / 2;
        double[] sensors = Sensors("fanbeam", setup, radius, line, col);
        double[] beta = FanRotations(setup.RotationIncrement);

        double[,] fan = FanBeamTransform.ParallelToFan(
            parallel, offsets, theta, sensors, beta, setup.Distance, setup.Geometry);
        return Three(wanted, MatrixToRows(fan), Numbers(sensors), Numbers(beta));
    }

    /// <summary><c>[I, H] = ifanbeam(F, D, …)</c>.</summary>
    private static JgsValue[] IFanBeamOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "ifanbeam takes the fan projections and the distance from the fan's vertex to the centre.");
        }

        (FanSetup setup, ParsedArgs parsed) = FanOptions("ifanbeam", args, 2, line, col);
        (double[,] parallel, double[] theta) = ToParallel(
            "ifanbeam", parsed.Positional[0], setup, parsed, line, col);

        // Whatever iradon was told is passed straight through, because a fan reconstruction is a
        // parallel one over rebinned data and nothing about the fan changes how it is filtered.
        var forwarded = new List<JgsValue> { MatrixToRows(parallel), Numbers(theta) };
        foreach (string name in new[] { "Filter", "Interpolation" })
        {
            if (parsed.Named(name) is { } word)
            {
                forwarded.Add(word);
            }
        }

        foreach (string name in new[] { "FrequencyScaling", "OutputSize" })
        {
            if (parsed.Named(name) is { } number)
            {
                forwarded.Add(number);
            }
        }

        return IradonOutputs(forwarded, wanted, line, col);
    }

    // --- the two rebinnings on their own -------------------------------------------------------------

    /// <summary><c>[P, parallel_sensor_positions, parallel_rotation_angles] = fan2para(F, D, …)</c>.</summary>
    private static JgsValue[] Fan2ParaOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "fan2para takes the fan projections and the distance from the fan's vertex to the centre.");
        }

        (FanSetup setup, ParsedArgs parsed) = FanOptions("fan2para", args, 2, line, col);
        (double[,] parallel, double[] theta) = ToParallel(
            "fan2para", parsed.Positional[0], setup, parsed, line, col);
        double[] offsets = ParallelOffsets(parallel.GetLength(0), ParallelSpacing(parsed, line, col));
        return Three(wanted, MatrixToRows(parallel), Numbers(offsets), Numbers(theta));
    }

    /// <summary><c>[F, sensor_positions, fan_rotation_angles] = para2fan(P, D, …)</c>.</summary>
    private static JgsValue[] Para2FanOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "para2fan takes the parallel projections and the distance from the fan's vertex to the centre.");
        }

        (FanSetup setup, ParsedArgs parsed) = FanOptions("para2fan", args, 2, line, col);
        double[,] parallel = Rectangle("para2fan argument 1", parsed.Positional[0], line, col);
        double spacing = ParallelSpacing(parsed, line, col);
        double[] offsets = ParallelOffsets(parallel.GetLength(0), spacing);
        double[] theta = ParallelSweep(parsed, parallel.GetLength(1), line, col);

        double radius = offsets[^1];
        double[] sensors = Sensors("para2fan", setup, radius, line, col);
        double[] beta = FanRotations(setup.RotationIncrement);
        double[,] fan = FanBeamTransform.ParallelToFan(
            parallel, offsets, theta, sensors, beta, setup.Distance, setup.Geometry);
        return Three(wanted, MatrixToRows(fan), Numbers(sensors), Numbers(beta));
    }

    /// <summary>Fan projections read as parallel ones, which is what both consumers of a fan need.</summary>
    private static (double[,] Parallel, double[] Theta) ToParallel(
        string verb, JgsValue value, FanSetup setup, ParsedArgs parsed, int line, int col)
    {
        double[,] fan = Rectangle($"{verb} argument 1", value, line, col);
        int sensorCount = fan.GetLength(0);
        int half = (sensorCount - 1) / 2;
        var sensors = new double[sensorCount];
        for (int i = 0; i < sensorCount; i++)
        {
            sensors[i] = (i - half) * setup.SensorSpacing;
        }

        double[] beta = FanRotations(360.0 / System.Math.Max(1, fan.GetLength(1)));
        if (beta.Length != fan.GetLength(1))
        {
            beta = new double[fan.GetLength(1)];
            for (int i = 0; i < beta.Length; i++)
            {
                beta[i] = i * 360.0 / fan.GetLength(1);
            }
        }

        // How far from the centre the fan reaches is fixed by its widest ray, so the parallel
        // sampling is derived from the fan rather than asked for.
        double reach = setup.Distance
            * System.Math.Sin(FanBeamTransform.FanAngle(sensors[^1], setup.Distance, setup.Geometry));
        double spacing = ParallelSpacing(parsed, line, col);
        int rows = (2 * (int)System.Math.Floor(System.Math.Abs(reach) / spacing)) + 1;
        double[] offsets = ParallelOffsets(rows, spacing);
        double[] theta = ParallelSweep(parsed, 0, line, col);

        return (
            FanBeamTransform.FanToParallel(
                fan, sensors, beta, offsets, theta, setup.Distance, setup.Geometry),
            theta);
    }

    private static double ParallelSpacing(ParsedArgs parsed, int line, int col) =>
        parsed.Named("ParallelSensorSpacing") is { } given
            ? ScalarOf("ParallelSensorSpacing", given, line, col) is var spacing && spacing > 0
                ? spacing
                : throw new JgsRuntimeException(line, col,
                    "The parallel sensor spacing is a positive distance in pixels.")
            : 1;

    private static double[] ParallelSweep(ParsedArgs parsed, int fallbackCount, int line, int col)
    {
        if (parsed.Named("ParallelRotationIncrement") is { } given)
        {
            double increment = ScalarOf("ParallelRotationIncrement", given, line, col);
            return increment > 0
                ? ParallelAngles(increment)
                : throw new JgsRuntimeException(line, col,
                    "The parallel rotation increment is a positive number of degrees.");
        }

        return fallbackCount > 0 ? ParallelAngles(180.0 / fallbackCount) : ParallelAngles(1);
    }

    private static double[] Sensors(string verb, FanSetup setup, double radius, int line, int col)
    {
        try
        {
            return FanBeamTransform.SensorPositions(
                setup.Distance, radius, setup.Geometry, setup.SensorSpacing);
        }
        catch (ArgumentException ex)
        {
            // The framework appends "(Parameter 'x')" to an argument exception's message, which names
            // a C# parameter a script has never heard of. The sentence before it is the message.
            string said = ex.Message.Split(" (Parameter", StringSplitOptions.None)[0];
            throw new JgsRuntimeException(line, col, $"{verb}: {said}");
        }
    }

    private static JgsValue[] Three(int wanted, JgsValue first, JgsValue second, JgsValue third) =>
        wanted switch
        {
            <= 1 => [first],
            2 => [first, second],
            _ => [first, second, third],
        };

    // --- warp ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>warp</c> draws a picture on a surface. It waited on texture mapping from M46 to here, and
    /// what it needed turned out to be small: the surface renderer already asks for one colour per
    /// grid vertex, so a picture is a different answer to that question rather than a second way of
    /// drawing a surface.
    /// <para>
    /// The picture is sampled at the grid's own resolution — nearest neighbour, so a small picture on
    /// a fine grid shows its own pixels rather than a blur of them. A surface finer than the picture
    /// is therefore not a way of getting more detail than was there.
    /// </para>
    /// </summary>
    private static JgsValue Warp(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "warp expects a picture to draw.");
            }

            // warp(I), warp(I, map), warp(Z, I), warp(X, Y, Z, I) — the picture is always last, and
            // what comes before it says where to put it.
            int pictureAt = rest.Count - 1;
            if (rest.Count == 2 && IsColormapRows(rest[1]))
            {
                pictureAt = 0;
            }

            (uint[] pixels, int height, int width) = WarpPicture(rest, pictureAt, line, col);
            (double[] x, double[] y, double[,] z) = WarpSurface(rest, pictureAt, height, width, line, col);

            SurfacePlot surface = JG.Surf(x, y, z);
            surface.Style = SurfaceStyle.Filled;
            surface.TextureData = SampleTexture(pixels, height, width, z.GetLength(0), z.GetLength(1));
            surface.Name = "Warp";
            return JgsHandleRegistry.For(surface);
        });
    }

    /// <summary>Whether a value looks like a colour table rather than a picture — three columns of fractions.</summary>
    private static bool IsColormapRows(JgsValue value)
    {
        IReadOnlyList<int> dims = JgsMatrix.DimsOf(value);
        return dims.Count == 2 && dims[1] == 3 && dims[0] >= 1;
    }

    private static (uint[] Pixels, int Height, int Width) WarpPicture(
        IReadOnlyList<JgsValue> rest, int pictureAt, int line, int col)
    {
        if (pictureAt == 0 && rest.Count == 2)
        {
            // An indexed picture and its colour table, which is exactly what im2frame reads too.
            double[,] map = ColormapRows("warp", rest, 1, line, col);
            return ArgbOf("warp", IndexedToTrueColour("warp", rest[0], map, line, col), line, col);
        }

        using ImgArg source = ImgLike("warp", rest, pictureAt, line, col);
        ImageBuffer buffer = source.Buffer;
        var pixels = new uint[buffer.Width * buffer.Height];
        ReadOnlySpan<double> samples = buffer.Pixels;
        int channels = buffer.Channels;
        for (int i = 0; i < pixels.Length; i++)
        {
            uint r = ByteOf(samples[i * channels]);
            uint g = channels >= 3 ? ByteOf(samples[(i * channels) + 1]) : r;
            uint b = channels >= 3 ? ByteOf(samples[(i * channels) + 2]) : r;
            pixels[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
        }

        GC.KeepAlive(buffer);
        return (pixels, buffer.Height, buffer.Width);
    }

    /// <summary>A height-by-width-by-3 array of bytes as row-major ARGB.</summary>
    private static (uint[] Pixels, int Height, int Width) ArgbOf(
        string verb, JgsValue picture, int line, int col)
    {
        IReadOnlyList<int> dims = JgsMatrix.DimsOf(picture);
        int height = dims[0];
        int width = dims[1];
        double[] flat = ToDoubles(verb, picture, line, col);
        int plane = height * width;
        var pixels = new uint[plane];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int at = (c * height) + r;
                pixels[(r * width) + c] = 0xFF000000u
                    | ((uint)System.Math.Clamp((int)flat[at], 0, 255) << 16)
                    | ((uint)System.Math.Clamp((int)flat[at + plane], 0, 255) << 8)
                    | (uint)System.Math.Clamp((int)flat[at + (2 * plane)], 0, 255);
            }
        }

        return (pixels, height, width);
    }

    /// <summary>The surface a warped picture is laid on, defaulting to the flat rectangle it covers.</summary>
    private static (double[] X, double[] Y, double[,] Z) WarpSurface(
        IReadOnlyList<JgsValue> rest, int pictureAt, int height, int width, int line, int col)
    {
        double[,] z;
        switch (pictureAt)
        {
            case 0:
            case 1 when rest.Count == 2 && !IsColormapRows(rest[1]):
                // warp(Z, I): the heights are given and the picture goes on them.
                z = pictureAt == 1
                    ? Rectangle("warp: Z", rest[0], line, col)
                    : new double[height, width];
                break;
            case 3:
                double[,] xg = Rectangle("warp: X", rest[0], line, col);
                double[,] yg = Rectangle("warp: Y", rest[1], line, col);
                z = Rectangle("warp: Z", rest[2], line, col);
                if (xg.GetLength(0) != z.GetLength(0) || xg.GetLength(1) != z.GetLength(1)
                    || yg.GetLength(0) != z.GetLength(0) || yg.GetLength(1) != z.GetLength(1))
                {
                    throw new JgsRuntimeException(line, col,
                        "warp: X, Y and Z have to be grids of the same size.");
                }

                // A full grid is read for its edges rather than kept, because a warped picture is
                // laid on a rectangle in every form MATLAB documents and a parametric one would need
                // the texture to be sampled per vertex rather than per row and column.
                return (RowOf(xg, 0), ColumnOf(yg, 0), z);
            default:
                z = new double[height, width];
                break;
        }

        return (PixelCounts(z.GetLength(1)), PixelCounts(z.GetLength(0)), z);
    }

    /// <summary>The whole numbers a picture's rows or columns are counted along.</summary>
    private static double[] PixelCounts(int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = i + 1;
        }

        return values;
    }

    private static double[] RowOf(double[,] grid, int row)
    {
        var values = new double[grid.GetLength(1)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = grid[row, i];
        }

        return values;
    }

    private static double[] ColumnOf(double[,] grid, int column)
    {
        var values = new double[grid.GetLength(0)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = grid[i, column];
        }

        return values;
    }

    /// <summary>The picture sampled at a grid's own resolution, nearest neighbour, row-major.</summary>
    private static uint[] SampleTexture(uint[] pixels, int height, int width, int rows, int cols)
    {
        var texture = new uint[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            int sourceRow = rows == 1 ? 0 : (int)System.Math.Round((double)r * (height - 1) / (rows - 1));
            for (int c = 0; c < cols; c++)
            {
                int sourceCol = cols == 1 ? 0 : (int)System.Math.Round((double)c * (width - 1) / (cols - 1));
                texture[(r * cols) + c] =
                    pixels[(System.Math.Clamp(sourceRow, 0, height - 1) * width)
                        + System.Math.Clamp(sourceCol, 0, width - 1)];
            }
        }

        return texture;
    }
}
