namespace JGraph.Imaging;

/// <summary>
/// The Shepp–Logan head phantom — the synthetic picture tomography algorithms are judged against.
/// </summary>
/// <remarks>
/// It is ten overlapping ellipses standing in for a skull and its contents, and it earns its place
/// because of one detail: the three small ellipses near the bottom differ from their surroundings
/// by one part in a hundred. A reconstruction that looks perfect will usually have lost them, so
/// the phantom asks the question a photograph cannot — not "is the picture sharp" but "is the
/// faint thing still there".
/// </remarks>
public static class Phantoms
{
    /// <summary>
    /// The original 1974 Shepp–Logan ellipses: intensity, semi-axes, centre, and rotation in
    /// degrees, one row each.
    /// </summary>
    public static double[,] SheppLogan { get; } = Build(
        [1, -0.98, -0.02, -0.02, 0.01, 0.01, 0.01, 0.01, 0.01, 0.01]);

    /// <summary>
    /// The modified Shepp–Logan ellipses, whose contrasts are spread out enough to be visible in a
    /// display window that also shows the skull. This is what <c>phantom</c> draws by default.
    /// </summary>
    public static double[,] ModifiedSheppLogan { get; } = Build(
        [1, -0.8, -0.2, -0.2, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1]);

    /// <summary>Reads a phantom name the way MATLAB spells it.</summary>
    /// <param name="name">The word given to <c>phantom</c>.</param>
    /// <returns>The matching ellipse table.</returns>
    public static double[,] Parse(string name) =>
        name?.Replace(" ", string.Empty).ToLowerInvariant() switch
        {
            "shepp-logan" => SheppLogan,
            "modifiedshepp-logan" => ModifiedSheppLogan,
            _ => throw new ArgumentException(
                $"unknown phantom '{name}' (use 'Shepp-Logan' or 'Modified Shepp-Logan').", nameof(name)),
        };

    /// <summary>
    /// Draws a table of ellipses into a square picture. Overlapping ellipses add, which is how the
    /// skull is drawn as a bright ellipse with a slightly smaller dark one laid on top of it.
    /// </summary>
    /// <param name="ellipses">
    /// One row per ellipse: intensity, semi-axis along x, semi-axis along y, centre x, centre y, and
    /// rotation in degrees. Coordinates run over [-1, 1] in both directions.
    /// </param>
    /// <param name="size">The side of the square to draw into.</param>
    /// <returns>The picture, row-major.</returns>
    public static double[,] Draw(double[,] ellipses, int size)
    {
        ArgumentNullException.ThrowIfNull(ellipses);
        if (ellipses.GetLength(1) != 6)
        {
            throw new ArgumentException(
                "each ellipse needs six numbers: intensity, x semi-axis, y semi-axis, centre x, centre y, rotation.",
                nameof(ellipses));
        }

        if (size < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "the phantom size must be positive.");
        }

        var image = new double[size, size];
        if (size == 1)
        {
            // A one-pixel phantom is the value at the origin; the general axis below divides by zero.
            for (int e = 0; e < ellipses.GetLength(0); e++)
            {
                if (Inside(0, 0, ellipses, e))
                {
                    image[0, 0] += ellipses[e, 0];
                }
            }

            return image;
        }

        var axis = new double[size];
        for (int i = 0; i < size; i++)
        {
            axis[i] = ((2.0 * i) / (size - 1)) - 1;
        }

        for (int e = 0; e < ellipses.GetLength(0); e++)
        {
            for (int r = 0; r < size; r++)
            {
                // y counts up the picture where the row index counts down it.
                double y = axis[size - 1 - r];
                for (int c = 0; c < size; c++)
                {
                    if (Inside(axis[c], y, ellipses, e))
                    {
                        image[r, c] += ellipses[e, 0];
                    }
                }
            }
        }

        return image;
    }

    private static bool Inside(double x, double y, double[,] ellipses, int e)
    {
        double semiX = ellipses[e, 1];
        double semiY = ellipses[e, 2];
        double dx = x - ellipses[e, 3];
        double dy = y - ellipses[e, 4];
        double phi = ellipses[e, 5] * Math.PI / 180.0;
        double cos = Math.Cos(phi);
        double sin = Math.Sin(phi);
        double along = (dx * cos) + (dy * sin);
        double across = (dy * cos) - (dx * sin);
        return ((along * along) / (semiX * semiX)) + ((across * across) / (semiY * semiY)) <= 1;
    }

    private static double[,] Build(double[] intensities)
    {
        // Geometry shared by both phantoms — only the intensities differ between them.
        double[,] geometry =
        {
            { 0.69, 0.92, 0, 0, 0 },
            { 0.6624, 0.8740, 0, -0.0184, 0 },
            { 0.1100, 0.3100, 0.22, 0, -18 },
            { 0.1600, 0.4100, -0.22, 0, 18 },
            { 0.2100, 0.2500, 0, 0.35, 0 },
            { 0.0460, 0.0460, 0, 0.1, 0 },
            { 0.0460, 0.0460, 0, -0.1, 0 },
            { 0.0460, 0.0230, -0.08, -0.605, 0 },
            { 0.0230, 0.0230, 0, -0.606, 0 },
            { 0.0230, 0.0460, 0.06, -0.605, 0 },
        };

        var table = new double[10, 6];
        for (int e = 0; e < 10; e++)
        {
            table[e, 0] = intensities[e];
            for (int k = 0; k < 5; k++)
            {
                table[e, k + 1] = geometry[e, k];
            }
        }

        return table;
    }
}
