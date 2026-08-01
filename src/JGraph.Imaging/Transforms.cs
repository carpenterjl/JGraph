using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Imaging;

/// <summary>The families <c>fitgeotrans</c> can estimate from matched point pairs.</summary>
public enum TransformKind
{
    /// <summary>Rotation, uniform scale and translation — four degrees of freedom, two point pairs.</summary>
    NonreflectiveSimilarity,

    /// <summary>As above but a reflection is allowed; three point pairs.</summary>
    Similarity,

    /// <summary>Any linear map plus translation — parallel lines stay parallel; three point pairs.</summary>
    Affine,

    /// <summary>A homography: straight lines stay straight but parallels converge; four point pairs.</summary>
    Projective,
}

/// <summary>
/// The world coordinates an image occupies (MATLAB <c>imref2d</c>) — the bridge between array
/// subscripts and the plane a geometric transform acts on.
/// </summary>
/// <remarks>
/// MATLAB's <em>intrinsic</em> coordinates put pixel centres at 1…N with the outer edges at 0.5 and
/// N + 0.5, and the default world frame simply coincides with that. Keeping the two apart is what
/// lets <c>imwarp</c> place a rotated image somewhere other than the origin, and what lets
/// <c>imcrop</c> take a rectangle in the units a plot was drawn in.
/// </remarks>
public sealed class SpatialRef
{
    /// <summary>The default frame for an image of the given size: world equals intrinsic.</summary>
    public SpatialRef(int rows, int cols)
        : this(rows, cols, 0.5, cols + 0.5, 0.5, rows + 0.5)
    {
    }

    /// <summary>A frame with explicit world limits.</summary>
    public SpatialRef(int rows, int cols, double xMin, double xMax, double yMin, double yMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        Rows = rows;
        Cols = cols;
        XWorldMin = xMin;
        XWorldMax = xMax;
        YWorldMin = yMin;
        YWorldMax = yMax;
    }

    /// <summary>Row count of the image this frame describes.</summary>
    public int Rows { get; }

    /// <summary>Column count of the image this frame describes.</summary>
    public int Cols { get; }

    /// <summary>The left edge of the first column, in world units.</summary>
    public double XWorldMin { get; }

    /// <summary>The right edge of the last column, in world units.</summary>
    public double XWorldMax { get; }

    /// <summary>The top edge of the first row, in world units.</summary>
    public double YWorldMin { get; }

    /// <summary>The bottom edge of the last row, in world units.</summary>
    public double YWorldMax { get; }

    /// <summary>How wide one pixel is in world units.</summary>
    public double PixelExtentX => (XWorldMax - XWorldMin) / Cols;

    /// <summary>How tall one pixel is in world units.</summary>
    public double PixelExtentY => (YWorldMax - YWorldMin) / Rows;

    /// <summary>Converts an intrinsic x (pixel centres at 1…Cols) to world units.</summary>
    public double XToWorld(double x) => XWorldMin + ((x - 0.5) * PixelExtentX);

    /// <summary>Converts an intrinsic y (pixel centres at 1…Rows) to world units.</summary>
    public double YToWorld(double y) => YWorldMin + ((y - 0.5) * PixelExtentY);

    /// <summary>Converts a world x back to intrinsic.</summary>
    public double XToIntrinsic(double x) => ((x - XWorldMin) / PixelExtentX) + 0.5;

    /// <summary>Converts a world y back to intrinsic.</summary>
    public double YToIntrinsic(double y) => ((y - YWorldMin) / PixelExtentY) + 0.5;
}

/// <summary>
/// A 2-D geometric transform as a 3×3 matrix in MATLAB's row-vector convention:
/// <c>[x y 1] · T = [u v w]</c>, with the result divided through by <c>w</c>. The one type stands
/// behind <c>affine2d</c>, <c>rigid2d</c> and <c>projective2d</c> alike, because they differ only in
/// which entries they are allowed to fill in.
/// </summary>
public sealed class GeometricTransform
{
    private readonly double[,] _forward;
    private readonly double[,] _inverse;

    /// <summary>Wraps a 3×3 matrix.</summary>
    /// <exception cref="ArgumentException">The matrix is not 3×3, or is singular.</exception>
    public GeometricTransform(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
        {
            throw new ArgumentException("a geometric transform is a 3-by-3 matrix", nameof(matrix));
        }

        _forward = (double[,])matrix.Clone();
        _inverse = Invert3(_forward);
    }

    /// <summary>The identity transform.</summary>
    public static GeometricTransform Identity { get; } =
        new(new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } });

    /// <summary>The 3×3 matrix, in MATLAB's <c>T</c> layout.</summary>
    public double[,] Matrix => (double[,])_forward.Clone();

    /// <summary>Whether the last column is <c>[0; 0; 1]</c>, so the map is affine rather than projective.</summary>
    public bool IsAffine =>
        Math.Abs(_forward[0, 2]) < 1e-12 &&
        Math.Abs(_forward[1, 2]) < 1e-12 &&
        Math.Abs(_forward[2, 2] - 1.0) < 1e-12;

    /// <summary>A pure translation.</summary>
    public static GeometricTransform Translation(double dx, double dy) =>
        new(new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { dx, dy, 1 } });

    /// <summary>Multiplies two transforms: applying <paramref name="first"/> then <paramref name="second"/>.</summary>
    public static GeometricTransform Compose(GeometricTransform first, GeometricTransform second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return new GeometricTransform(Multiply3(first._forward, second._forward));
    }

    /// <summary>Maps a point through the transform.</summary>
    public (double X, double Y) Forward(double x, double y) => Apply(_forward, x, y);

    /// <summary>Maps a point back through the transform.</summary>
    public (double X, double Y) Inverse(double x, double y) => Apply(_inverse, x, y);

    /// <summary>The transform that undoes this one.</summary>
    public GeometricTransform Invert() => new(_inverse);

    /// <summary>
    /// The world rectangle the given frame maps into. A projective map can bow an edge outside the
    /// box its corners span, so the edges are sampled rather than only their ends.
    /// </summary>
    public (double XMin, double XMax, double YMin, double YMax) OutputLimits(SpatialRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        int steps = IsAffine ? 1 : 100;
        double xMin = double.PositiveInfinity;
        double xMax = double.NegativeInfinity;
        double yMin = double.PositiveInfinity;
        double yMax = double.NegativeInfinity;

        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double x = reference.XWorldMin + (t * (reference.XWorldMax - reference.XWorldMin));
            double y = reference.YWorldMin + (t * (reference.YWorldMax - reference.YWorldMin));
            foreach ((double px, double py) in new[]
                     {
                         (x, reference.YWorldMin), (x, reference.YWorldMax),
                         (reference.XWorldMin, y), (reference.XWorldMax, y),
                     })
            {
                (double u, double v) = Forward(px, py);
                xMin = Math.Min(xMin, u);
                xMax = Math.Max(xMax, u);
                yMin = Math.Min(yMin, v);
                yMax = Math.Max(yMax, v);
            }
        }

        return (xMin, xMax, yMin, yMax);
    }

    /// <summary>
    /// Estimates a transform that carries <paramref name="moving"/> onto <paramref name="fixedPoints"/>
    /// (MATLAB <c>fitgeotrans</c>). Both are n×2 arrays of <c>[x y]</c> rows.
    /// </summary>
    /// <exception cref="ArgumentException">The arrays disagree, or there are too few pairs.</exception>
    public static GeometricTransform Fit(double[,] moving, double[,] fixedPoints, TransformKind kind)
    {
        ArgumentNullException.ThrowIfNull(moving);
        ArgumentNullException.ThrowIfNull(fixedPoints);
        int n = moving.GetLength(0);
        if (moving.GetLength(1) != 2 || fixedPoints.GetLength(1) != 2)
        {
            throw new ArgumentException("point sets are n-by-2 arrays of [x y] rows");
        }

        if (fixedPoints.GetLength(0) != n)
        {
            throw new ArgumentException("the two point sets must have the same number of rows");
        }

        int needed = kind switch
        {
            TransformKind.NonreflectiveSimilarity => 2,
            TransformKind.Similarity or TransformKind.Affine => 3,
            _ => 4,
        };
        if (n < needed)
        {
            throw new ArgumentException(
                $"a {Name(kind)} transform needs at least {needed} point pairs, but {n} were given");
        }

        return kind switch
        {
            TransformKind.NonreflectiveSimilarity => FitNonreflective(moving, fixedPoints),
            TransformKind.Similarity => FitSimilarity(moving, fixedPoints),
            TransformKind.Affine => FitAffine(moving, fixedPoints),
            _ => FitProjective(moving, fixedPoints),
        };
    }

    /// <summary>The MATLAB spelling of a transformation type.</summary>
    public static string Name(TransformKind kind) => kind switch
    {
        TransformKind.NonreflectiveSimilarity => "nonreflectivesimilarity",
        TransformKind.Similarity => "similarity",
        TransformKind.Affine => "affine",
        _ => "projective",
    };

    private static (double X, double Y) Apply(double[,] t, double x, double y)
    {
        double w = (x * t[0, 2]) + (y * t[1, 2]) + t[2, 2];
        if (w == 0.0)
        {
            w = double.Epsilon;
        }

        return (
            ((x * t[0, 0]) + (y * t[1, 0]) + t[2, 0]) / w,
            ((x * t[0, 1]) + (y * t[1, 1]) + t[2, 1]) / w);
    }

    private static GeometricTransform FitNonreflective(double[,] moving, double[,] fixedPoints)
    {
        // Unknowns [sc ss tx ty] with u = sc·x − ss·y + tx and v = ss·x + sc·y + ty: the rotation and
        // the scale share two numbers, which is exactly what "no reflection, uniform scale" means.
        int n = moving.GetLength(0);
        var design = new double[2 * n, 4];
        var rhs = new double[2 * n, 1];
        for (int i = 0; i < n; i++)
        {
            double x = moving[i, 0];
            double y = moving[i, 1];
            design[2 * i, 0] = x;
            design[2 * i, 1] = -y;
            design[2 * i, 2] = 1;
            rhs[2 * i, 0] = fixedPoints[i, 0];

            design[(2 * i) + 1, 0] = y;
            design[(2 * i) + 1, 1] = x;
            design[(2 * i) + 1, 3] = 1;
            rhs[(2 * i) + 1, 0] = fixedPoints[i, 1];
        }

        double[,] p = Solve(design, rhs);
        return new GeometricTransform(new double[,]
        {
            { p[0, 0], p[1, 0], 0 },
            { -p[1, 0], p[0, 0], 0 },
            { p[2, 0], p[3, 0], 1 },
        });
    }

    private static GeometricTransform FitSimilarity(double[,] moving, double[,] fixedPoints)
    {
        // A reflective similarity is a nonreflective one applied to mirrored points, so fit both and
        // keep whichever explains the pairs better rather than guessing the handedness.
        GeometricTransform direct = FitNonreflective(moving, fixedPoints);

        int n = moving.GetLength(0);
        var mirrored = new double[n, 2];
        for (int i = 0; i < n; i++)
        {
            mirrored[i, 0] = -moving[i, 0];
            mirrored[i, 1] = moving[i, 1];
        }

        var flip = new GeometricTransform(new double[,] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } });
        GeometricTransform reflected = Compose(flip, FitNonreflective(mirrored, fixedPoints));
        return Residual(direct, moving, fixedPoints) <= Residual(reflected, moving, fixedPoints)
            ? direct
            : reflected;
    }

    private static GeometricTransform FitAffine(double[,] moving, double[,] fixedPoints)
    {
        int n = moving.GetLength(0);
        var design = new double[n, 3];
        var rhs = new double[n, 2];
        for (int i = 0; i < n; i++)
        {
            design[i, 0] = moving[i, 0];
            design[i, 1] = moving[i, 1];
            design[i, 2] = 1;
            rhs[i, 0] = fixedPoints[i, 0];
            rhs[i, 1] = fixedPoints[i, 1];
        }

        double[,] p = Solve(design, rhs);
        return new GeometricTransform(new double[,]
        {
            { p[0, 0], p[0, 1], 0 },
            { p[1, 0], p[1, 1], 0 },
            { p[2, 0], p[2, 1], 1 },
        });
    }

    private static GeometricTransform FitProjective(double[,] moving, double[,] fixedPoints)
    {
        // Homography coefficients mix pixel coordinates with their products, so an unnormalized fit on
        // a 1000-pixel image asks the solver to balance terms six orders of magnitude apart. Hartley
        // normalization centres and scales each set first, and the two frames are composed back in.
        (GeometricTransform toMoving, double[,] normMoving) = Normalize(moving);
        (GeometricTransform toFixed, double[,] normFixed) = Normalize(fixedPoints);

        int n = moving.GetLength(0);
        var design = new double[2 * n, 8];
        var rhs = new double[2 * n, 1];
        for (int i = 0; i < n; i++)
        {
            double x = normMoving[i, 0];
            double y = normMoving[i, 1];
            double u = normFixed[i, 0];
            double v = normFixed[i, 1];

            int r0 = 2 * i;
            design[r0, 0] = x;
            design[r0, 1] = y;
            design[r0, 2] = 1;
            design[r0, 6] = -u * x;
            design[r0, 7] = -u * y;
            rhs[r0, 0] = u;

            int r1 = r0 + 1;
            design[r1, 3] = x;
            design[r1, 4] = y;
            design[r1, 5] = 1;
            design[r1, 6] = -v * x;
            design[r1, 7] = -v * y;
            rhs[r1, 0] = v;
        }

        double[,] p = Solve(design, rhs);
        var h = new GeometricTransform(new double[,]
        {
            { p[0, 0], p[3, 0], p[6, 0] },
            { p[1, 0], p[4, 0], p[7, 0] },
            { p[2, 0], p[5, 0], 1 },
        });

        return Compose(toMoving, Compose(h, toFixed.Invert()));
    }

    /// <summary>Builds the frame that centres a point set on the origin at mean distance √2.</summary>
    private static (GeometricTransform ToNormalized, double[,] Points) Normalize(double[,] points)
    {
        int n = points.GetLength(0);
        double mx = 0;
        double my = 0;
        for (int i = 0; i < n; i++)
        {
            mx += points[i, 0];
            my += points[i, 1];
        }

        mx /= n;
        my /= n;

        double distance = 0;
        for (int i = 0; i < n; i++)
        {
            distance += Math.Sqrt(((points[i, 0] - mx) * (points[i, 0] - mx)) +
                                  ((points[i, 1] - my) * (points[i, 1] - my)));
        }

        distance /= n;
        double scale = distance > 1e-12 ? Math.Sqrt(2.0) / distance : 1.0;

        var normalized = new double[n, 2];
        for (int i = 0; i < n; i++)
        {
            normalized[i, 0] = (points[i, 0] - mx) * scale;
            normalized[i, 1] = (points[i, 1] - my) * scale;
        }

        var frame = new GeometricTransform(new double[,]
        {
            { scale, 0, 0 },
            { 0, scale, 0 },
            { -mx * scale, -my * scale, 1 },
        });
        return (frame, normalized);
    }

    private static double Residual(GeometricTransform t, double[,] moving, double[,] fixedPoints)
    {
        double sum = 0;
        for (int i = 0; i < moving.GetLength(0); i++)
        {
            (double u, double v) = t.Forward(moving[i, 0], moving[i, 1]);
            sum += ((u - fixedPoints[i, 0]) * (u - fixedPoints[i, 0])) +
                   ((v - fixedPoints[i, 1]) * (v - fixedPoints[i, 1]));
        }

        return sum;
    }

    private static double[,] Solve(double[,] design, double[,] rhs)
    {
        try
        {
            return Linear.Solve(design, rhs);
        }
        catch (InvalidOperationException)
        {
            throw new ArgumentException(
                "the point pairs are degenerate — three collinear points, or two that coincide — " +
                "so no transform of this type fits them");
        }
    }

    private static double[,] Multiply3(double[,] a, double[,] b)
    {
        var result = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++)
                {
                    sum += a[r, k] * b[k, c];
                }

                result[r, c] = sum;
            }
        }

        return result;
    }

    private static double[,] Invert3(double[,] t)
    {
        double a = t[0, 0];
        double b = t[0, 1];
        double c = t[0, 2];
        double d = t[1, 0];
        double e = t[1, 1];
        double f = t[1, 2];
        double g = t[2, 0];
        double h = t[2, 1];
        double i = t[2, 2];

        double detA = (e * i) - (f * h);
        double detB = (d * i) - (f * g);
        double detC = (d * h) - (e * g);
        double det = (a * detA) - (b * detB) + (c * detC);
        if (Math.Abs(det) < 1e-15)
        {
            throw new ArgumentException("the transform matrix is singular and cannot be inverted");
        }

        return new double[,]
        {
            { detA / det, ((c * h) - (b * i)) / det, ((b * f) - (c * e)) / det },
            { -detB / det, ((a * i) - (c * g)) / det, ((c * d) - (a * f)) / det },
            { detC / det, ((b * g) - (a * h)) / det, ((a * e) - (b * d)) / det },
        };
    }
}

/// <summary>Applies a <see cref="GeometricTransform"/> to an image (MATLAB <c>imwarp</c>).</summary>
public static class Warping
{
    /// <summary>
    /// Resamples <paramref name="image"/> onto <paramref name="outputRef"/>, pulling each output pixel
    /// from wherever the inverse transform says it came from.
    /// </summary>
    /// <param name="image">The image to warp.</param>
    /// <param name="inputRef">Where the input sits in the world.</param>
    /// <param name="transform">The forward map.</param>
    /// <param name="outputRef">Where the output sits, and how large it is.</param>
    /// <param name="method">The interpolation kernel.</param>
    /// <param name="fill">One fill value, or one per channel, for output pixels with no source.</param>
    /// <param name="smoothEdges">
    /// Whether the fill also participates in interpolation at the border, which fades the edge instead
    /// of extending it. MATLAB's default is off — a sharp edge, at the cost of a one-pixel seam.
    /// </param>
    public static ImageBuffer Warp(
        ImageBuffer image,
        SpatialRef inputRef,
        GeometricTransform transform,
        SpatialRef outputRef,
        Geometry.Interpolation method = Geometry.Interpolation.Bilinear,
        double[]? fill = null,
        bool smoothEdges = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(inputRef);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(outputRef);

        var result = new ImageBuffer(outputRef.Rows, outputRef.Cols, image.Channels);
        for (int r = 0; r < outputRef.Rows; r++)
        {
            double worldY = outputRef.YToWorld(r + 1);
            for (int c = 0; c < outputRef.Cols; c++)
            {
                double worldX = outputRef.XToWorld(c + 1);
                (double sourceX, double sourceY) = transform.Inverse(worldX, worldY);

                // Intrinsic coordinates count pixel centres from one; the buffer counts from zero.
                double col = inputRef.XToIntrinsic(sourceX) - 1.0;
                double row = inputRef.YToIntrinsic(sourceY) - 1.0;
                bool inside = col >= -0.5 && col <= image.Width - 0.5 &&
                              row >= -0.5 && row <= image.Height - 0.5;

                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double fillValue = FillFor(fill, ch);
                    result[r, c, ch] = inside
                        ? Geometry.Sample(image, row, col, ch, method, smoothEdges ? fillValue : null)
                        : fillValue;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The frame <c>imwarp</c> uses when no output view is given: the whole transformed image, at the
    /// input's pixel size.
    /// </summary>
    public static SpatialRef FollowOutput(SpatialRef inputRef, GeometricTransform transform)
    {
        ArgumentNullException.ThrowIfNull(inputRef);
        ArgumentNullException.ThrowIfNull(transform);
        (double xMin, double xMax, double yMin, double yMax) = transform.OutputLimits(inputRef);

        int cols = Math.Max(1, (int)Math.Ceiling(((xMax - xMin) / inputRef.PixelExtentX) - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling(((yMax - yMin) / inputRef.PixelExtentY) - 1e-9));

        // A whole number of pixels rarely covers the bounding box exactly; share the surplus so the
        // transformed image stays centred in the frame it is given.
        double xSlack = ((cols * inputRef.PixelExtentX) - (xMax - xMin)) / 2.0;
        double ySlack = ((rows * inputRef.PixelExtentY) - (yMax - yMin)) / 2.0;
        return new SpatialRef(rows, cols, xMin - xSlack, xMax + xSlack, yMin - ySlack, yMax + ySlack);
    }

    /// <summary>
    /// The frame <c>affineOutputView</c>'s 'CenterOutput' style produces: the input's size and pixel
    /// extent, moved so the transformed centre of the input is the centre of the output.
    /// </summary>
    public static SpatialRef CenterOutput(SpatialRef inputRef, GeometricTransform transform)
    {
        ArgumentNullException.ThrowIfNull(inputRef);
        ArgumentNullException.ThrowIfNull(transform);
        (double centreX, double centreY) = transform.Forward(
            (inputRef.XWorldMin + inputRef.XWorldMax) / 2.0,
            (inputRef.YWorldMin + inputRef.YWorldMax) / 2.0);

        double halfWidth = (inputRef.XWorldMax - inputRef.XWorldMin) / 2.0;
        double halfHeight = (inputRef.YWorldMax - inputRef.YWorldMin) / 2.0;
        return new SpatialRef(
            inputRef.Rows, inputRef.Cols,
            centreX - halfWidth, centreX + halfWidth,
            centreY - halfHeight, centreY + halfHeight);
    }

    private static double FillFor(double[]? fill, int channel)
    {
        if (fill is null || fill.Length == 0)
        {
            return 0.0;
        }

        return fill.Length == 1 ? fill[0] : fill[Math.Min(channel, fill.Length - 1)];
    }
}
