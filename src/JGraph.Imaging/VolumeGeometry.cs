namespace JGraph.Imaging;

/// <summary>
/// Resampling a <see cref="Volume"/>: resize, rotate about an arbitrary axis, crop, and cut an
/// arbitrarily oriented plane out of it.
/// </summary>
/// <remarks>
/// Every operation here maps each output sample back into the input and interpolates, rather than
/// pushing input samples forward. Forward mapping leaves holes wherever the transform stretches and
/// writes the same output twice wherever it compresses; inverse mapping visits each output exactly
/// once and always has an answer for it.
/// </remarks>
public static class VolumeGeometry
{
    /// <summary>How a resampled sample is interpolated from the samples around it.</summary>
    public enum Interpolation
    {
        /// <summary>The nearest sample: no new values, so a label volume stays a label volume.</summary>
        Nearest,

        /// <summary>Trilinear — the eight samples around the point, weighted by proximity.</summary>
        Linear,

        /// <summary>Keys cubic with a = −½, over the sixty-four samples around the point.</summary>
        Cubic,
    }

    /// <summary>
    /// Resizes a volume (MATLAB <c>imresize3</c>). Each axis is resampled in its own pass, so the cost
    /// is the sum of the three kernel widths per sample rather than their product.
    /// </summary>
    /// <param name="volume">The volume to resample.</param>
    /// <param name="size">The output size.</param>
    /// <param name="method">The interpolation kernel; MATLAB's default is <see cref="Interpolation.Linear"/>.</param>
    /// <param name="antialiasing">
    /// Whether to widen the kernel when shrinking, so discarded detail is averaged in rather than
    /// aliased. Null follows MATLAB: on for every method but <see cref="Interpolation.Nearest"/>.
    /// </param>
    public static Volume Resize(
        Volume volume,
        (int Rows, int Cols, int Planes) size,
        Interpolation method = Interpolation.Linear,
        bool? antialiasing = null)
    {
        ArgumentNullException.ThrowIfNull(volume);
        if (size.Rows < 1 || size.Cols < 1 || size.Planes < 1)
        {
            throw new ArgumentException("imresize3 needs a positive output size.", nameof(size));
        }

        bool antialias = antialiasing ?? method != Interpolation.Nearest;
        Volume rows = Along(volume, 0, size.Rows, method, antialias);
        Volume cols = Along(rows, 1, size.Cols, method, antialias);
        rows.Dispose();
        Volume planes = Along(cols, 2, size.Planes, method, antialias);
        cols.Dispose();
        return planes;
    }

    /// <summary>
    /// Rotates a volume about an axis through its centre (MATLAB <c>imrotate3</c>), counter-clockwise
    /// looking back along the axis — the right-hand rule.
    /// </summary>
    /// <param name="volume">The volume to rotate.</param>
    /// <param name="degrees">The rotation angle.</param>
    /// <param name="axis">The axis as (x, y, z) — column, row, plane; need not be a unit vector.</param>
    /// <param name="method">The interpolation kernel.</param>
    /// <param name="loose">Grow the output to hold the whole rotated volume, rather than keeping the input size.</param>
    /// <param name="fill">The value samples that fall outside the input take.</param>
    /// <remarks>
    /// Rows increase downwards while the y axis of the rotation points up, so the row coordinate is
    /// negated on the way in and back out. Without that, a positive angle about the plane axis would
    /// turn the opposite way from <see cref="Geometry.Rotate"/> on the same slice.
    /// </remarks>
    public static Volume Rotate(
        Volume volume,
        double degrees,
        (double X, double Y, double Z) axis,
        Interpolation method = Interpolation.Linear,
        bool loose = true,
        double fill = 0.0)
    {
        ArgumentNullException.ThrowIfNull(volume);
        double length = Math.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y) + (axis.Z * axis.Z));
        if (length <= 0)
        {
            throw new ArgumentException("imrotate3 needs an axis with a direction.", nameof(axis));
        }

        double ux = axis.X / length;
        double uy = axis.Y / length;
        double uz = axis.Z / length;
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // Rodrigues' rotation as a matrix, then transposed on use: the output sample asks where it
        // came from, which is the inverse rotation, and for a rotation the inverse is the transpose.
        double[,] r = Rodrigues(ux, uy, uz, cos, sin);

        int outHeight = volume.Height;
        int outWidth = volume.Width;
        int outDepth = volume.Depth;
        if (loose)
        {
            (outHeight, outWidth, outDepth) = LooseSize(volume, r);
        }

        double srcCx = (volume.Width - 1) / 2.0;
        double srcCy = (volume.Height - 1) / 2.0;
        double srcCz = (volume.Depth - 1) / 2.0;
        double dstCx = (outWidth - 1) / 2.0;
        double dstCy = (outHeight - 1) / 2.0;
        double dstCz = (outDepth - 1) / 2.0;

        var result = new Volume(outHeight, outWidth, outDepth);
        Span<double> target = result.Samples;
        if (fill != 0)
        {
            target.Fill(fill);
        }

        for (int p = 0; p < outDepth; p++)
        {
            double z = p - dstCz;
            for (int c = 0; c < outWidth; c++)
            {
                double x = c - dstCx;
                for (int rr = 0; rr < outHeight; rr++)
                {
                    double y = -(rr - dstCy);
                    double sx = (r[0, 0] * x) + (r[1, 0] * y) + (r[2, 0] * z);
                    double sy = (r[0, 1] * x) + (r[1, 1] * y) + (r[2, 1] * z);
                    double sz = (r[0, 2] * x) + (r[1, 2] * y) + (r[2, 2] * z);
                    double srcRow = srcCy - sy;
                    double srcCol = srcCx + sx;
                    double srcPlane = srcCz + sz;
                    if (srcRow < -0.5 || srcRow > volume.Height - 0.5
                        || srcCol < -0.5 || srcCol > volume.Width - 0.5
                        || srcPlane < -0.5 || srcPlane > volume.Depth - 0.5)
                    {
                        continue;
                    }

                    target[rr + (c * outHeight) + (p * outHeight * outWidth)] =
                        Sample(volume, srcRow, srcCol, srcPlane, method);
                }
            }
        }

        GC.KeepAlive(volume);
        return result;
    }

    /// <summary>
    /// Cuts a box out of a volume (MATLAB <c>imcrop3</c>). Coordinates are zero-based sample indices
    /// and are clamped to the volume, so a box that hangs over an edge returns the part that exists.
    /// </summary>
    public static Volume Crop(
        Volume volume,
        (int Row, int Col, int Plane) start,
        (int Rows, int Cols, int Planes) extent)
    {
        ArgumentNullException.ThrowIfNull(volume);
        int r0 = Math.Clamp(start.Row, 0, volume.Height - 1);
        int c0 = Math.Clamp(start.Col, 0, volume.Width - 1);
        int p0 = Math.Clamp(start.Plane, 0, volume.Depth - 1);
        int r1 = Math.Clamp(r0 + extent.Rows - 1, r0, volume.Height - 1);
        int c1 = Math.Clamp(c0 + extent.Cols - 1, c0, volume.Width - 1);
        int p1 = Math.Clamp(p0 + extent.Planes - 1, p0, volume.Depth - 1);

        var result = new Volume(r1 - r0 + 1, c1 - c0 + 1, p1 - p0 + 1);
        for (int p = 0; p < result.Depth; p++)
        {
            for (int c = 0; c < result.Width; c++)
            {
                for (int r = 0; r < result.Height; r++)
                {
                    result[r, c, p] = volume[r0 + r, c0 + c, p0 + p];
                }
            }
        }

        GC.KeepAlive(volume);
        return result;
    }

    /// <summary>
    /// The slice a plane cuts through a volume (MATLAB <c>obliqueslice</c>), together with the
    /// coordinates each sample was read at.
    /// </summary>
    /// <param name="volume">The volume to cut.</param>
    /// <param name="point">A point the plane passes through, as zero-based (row, column, plane).</param>
    /// <param name="normal">The plane normal as (x, y, z) — column, row, plane.</param>
    /// <param name="method">The interpolation kernel.</param>
    /// <param name="full">
    /// Size the grid so it holds the plane at any orientation, rather than trimming it to the part
    /// that actually meets the volume.
    /// </param>
    /// <param name="fill">The value samples that fall outside the volume take.</param>
    /// <remarks>
    /// A plane has no preferred direction within itself, so the two in-plane axes are a choice. They
    /// are built here from whichever coordinate axis the normal leans on least, which makes the answer
    /// deterministic and keeps the slice upright for the common axis-aligned normals; a different
    /// choice would rotate the same picture within its own frame.
    /// </remarks>
    public static (ImageBuffer Slice, double[,] X, double[,] Y, double[,] Z) ObliqueSlice(
        Volume volume,
        (double Row, double Col, double Plane) point,
        (double X, double Y, double Z) normal,
        Interpolation method = Interpolation.Linear,
        bool full = false,
        double fill = 0.0)
    {
        ArgumentNullException.ThrowIfNull(volume);
        double length = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        if (length <= 0)
        {
            throw new ArgumentException("obliqueslice needs a normal with a direction.", nameof(normal));
        }

        // Work in (row, column, plane) throughout; the caller's normal is (x, y, z).
        var n = (Row: normal.Y / length, Col: normal.X / length, Plane: normal.Z / length);
        // (u, v, n) is right-handed with v taken as u × n rather than n × u, which is what makes a
        // slice cut along the plane axis come back the same way up as the plane itself.
        (double Row, double Col, double Plane) u = InPlaneAxis(n);
        (double Row, double Col, double Plane) v = Cross(u, n);

        int half = full
            ? (int)Math.Ceiling(0.5 * Math.Sqrt(
                ((double)volume.Height * volume.Height) + ((double)volume.Width * volume.Width) +
                ((double)volume.Depth * volume.Depth)))
            : 0;

        (int MinU, int MaxU, int MinV, int MaxV) window = full
            ? (-half, half, -half, half)
            : Extent(volume, point, u, v);

        int height = window.MaxV - window.MinV + 1;
        int width = window.MaxU - window.MinU + 1;
        var slice = new ImageBuffer(height, width, 1);
        var xs = new double[height, width];
        var ys = new double[height, width];
        var zs = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            double b = window.MinV + r;
            for (int c = 0; c < width; c++)
            {
                double a = window.MinU + c;
                double row = point.Row + (a * u.Row) + (b * v.Row);
                double col = point.Col + (a * u.Col) + (b * v.Col);
                double plane = point.Plane + (a * u.Plane) + (b * v.Plane);
                xs[r, c] = col;
                ys[r, c] = row;
                zs[r, c] = plane;
                slice[r, c, 0] = Inside(volume, row, col, plane)
                    ? Sample(volume, row, col, plane, method)
                    : fill;
            }
        }

        GC.KeepAlive(volume);
        return (slice, xs, ys, zs);
    }

    /// <summary>One sample read at fractional coordinates, through the given kernel.</summary>
    public static double Sample(Volume volume, double row, double col, double plane, Interpolation method)
    {
        ArgumentNullException.ThrowIfNull(volume);
        if (method == Interpolation.Nearest)
        {
            return volume.At(
                (int)Math.Round(row, MidpointRounding.AwayFromZero),
                (int)Math.Round(col, MidpointRounding.AwayFromZero),
                (int)Math.Round(plane, MidpointRounding.AwayFromZero),
                Filters.Boundary.Replicate);
        }

        int support = method == Interpolation.Cubic ? 2 : 1;
        int r0 = (int)Math.Floor(row);
        int c0 = (int)Math.Floor(col);
        int p0 = (int)Math.Floor(plane);
        double total = 0;
        for (int dp = 1 - support; dp <= support; dp++)
        {
            double wp = Weight(plane - (p0 + dp), method);
            if (wp == 0)
            {
                continue;
            }

            for (int dc = 1 - support; dc <= support; dc++)
            {
                double wc = Weight(col - (c0 + dc), method);
                if (wc == 0)
                {
                    continue;
                }

                for (int dr = 1 - support; dr <= support; dr++)
                {
                    double wr = Weight(row - (r0 + dr), method);
                    if (wr == 0)
                    {
                        continue;
                    }

                    total += wr * wc * wp * volume.At(
                        r0 + dr, c0 + dc, p0 + dp, Filters.Boundary.Replicate);
                }
            }
        }

        return total;
    }

    // Resampling one axis, with the half-pixel-centre mapping Geometry.Resize uses: output sample x
    // reads the input at (x + ½)/scale − ½, which is what makes resizing by a factor and back land on
    // the grid it started from.
    private static Volume Along(Volume volume, int axis, int count, Interpolation method, bool antialias)
    {
        int extent = axis switch { 0 => volume.Height, 1 => volume.Width, _ => volume.Depth };
        double scale = count / (double)extent;
        (double[][] weights, int[][] indices) = Contributions(extent, count, scale, method, antialias);

        var result = new Volume(
            axis == 0 ? count : volume.Height,
            axis == 1 ? count : volume.Width,
            axis == 2 ? count : volume.Depth);
        for (int p = 0; p < result.Depth; p++)
        {
            for (int c = 0; c < result.Width; c++)
            {
                for (int r = 0; r < result.Height; r++)
                {
                    int along = axis switch { 0 => r, 1 => c, _ => p };
                    double[] w = weights[along];
                    int[] idx = indices[along];
                    double sum = 0;
                    for (int k = 0; k < w.Length; k++)
                    {
                        sum += w[k] * (axis switch
                        {
                            0 => volume[idx[k], c, p],
                            1 => volume[r, idx[k], p],
                            _ => volume[r, c, idx[k]],
                        });
                    }

                    result[r, c, p] = sum;
                }
            }
        }

        GC.KeepAlive(volume);
        return result;
    }

    private static (double[][] Weights, int[][] Indices) Contributions(
        int inputLength, int outputLength, double scale, Interpolation method, bool antialias)
    {
        // Shrinking widens the kernel by the scale factor and evaluates it in output units, so every
        // input sample that maps into an output sample contributes to it. Without that, a 10:1
        // reduction would point-sample one input in ten and alias the other nine.
        bool stretch = scale >= 1 || !antialias;
        double kernelScale = stretch ? 1.0 : scale;
        double support = method switch
        {
            Interpolation.Nearest => 0.5,
            Interpolation.Cubic => 2.0,
            _ => 1.0,
        } / kernelScale;

        var weights = new double[outputLength][];
        var indices = new int[outputLength][];
        for (int i = 0; i < outputLength; i++)
        {
            double centre = ((i + 0.5) / scale) - 0.5;
            int first = (int)Math.Floor(centre - support + 0.5);
            int last = (int)Math.Ceiling(centre + support - 0.5);
            int taps = last - first + 1;
            var w = new double[taps];
            var idx = new int[taps];
            double sum = 0;
            for (int k = 0; k < taps; k++)
            {
                int sampleIndex = first + k;
                double weight = Weight((centre - sampleIndex) * kernelScale, method) * kernelScale;
                w[k] = weight;
                sum += weight;
                idx[k] = Math.Clamp(sampleIndex, 0, inputLength - 1);
            }

            if (sum != 0)
            {
                for (int k = 0; k < taps; k++)
                {
                    w[k] /= sum;
                }
            }

            weights[i] = w;
            indices[i] = idx;
        }

        return (weights, indices);
    }

    private static double Weight(double d, Interpolation method)
    {
        double x = Math.Abs(d);
        switch (method)
        {
            case Interpolation.Nearest:
                return x <= 0.5 ? 1 : 0;

            case Interpolation.Cubic:
            {
                // Keys with a = −½, the one cubic that reproduces a linear ramp exactly.
                const double A = -0.5;
                if (x <= 1)
                {
                    return (((A + 2) * x * x * x) - ((A + 3) * x * x)) + 1;
                }

                if (x < 2)
                {
                    return (A * x * x * x) - (5 * A * x * x) + (8 * A * x) - (4 * A);
                }

                return 0;
            }

            default:
                return x < 1 ? 1 - x : 0;
        }
    }

    private static double[,] Rodrigues(double x, double y, double z, double cos, double sin)
    {
        double t = 1 - cos;
        return new[,]
        {
            { cos + (t * x * x), (t * x * y) - (sin * z), (t * x * z) + (sin * y) },
            { (t * x * y) + (sin * z), cos + (t * y * y), (t * y * z) - (sin * x) },
            { (t * x * z) - (sin * y), (t * y * z) + (sin * x), cos + (t * z * z) },
        };
    }

    // The output box a 'loose' rotation needs: the extent of the eight rotated corners.
    private static (int Height, int Width, int Depth) LooseSize(Volume volume, double[,] r)
    {
        double halfX = (volume.Width - 1) / 2.0;
        double halfY = (volume.Height - 1) / 2.0;
        double halfZ = (volume.Depth - 1) / 2.0;
        double maxX = 0;
        double maxY = 0;
        double maxZ = 0;
        for (int i = 0; i < 8; i++)
        {
            double x = ((i & 1) == 0 ? -halfX : halfX);
            double y = ((i & 2) == 0 ? -halfY : halfY);
            double z = ((i & 4) == 0 ? -halfZ : halfZ);
            maxX = Math.Max(maxX, Math.Abs((r[0, 0] * x) + (r[0, 1] * y) + (r[0, 2] * z)));
            maxY = Math.Max(maxY, Math.Abs((r[1, 0] * x) + (r[1, 1] * y) + (r[1, 2] * z)));
            maxZ = Math.Max(maxZ, Math.Abs((r[2, 0] * x) + (r[2, 1] * y) + (r[2, 2] * z)));
        }

        return (
            Math.Max(1, (int)Math.Ceiling((2 * maxY) + 1 - 1e-9)),
            Math.Max(1, (int)Math.Ceiling((2 * maxX) + 1 - 1e-9)),
            Math.Max(1, (int)Math.Ceiling((2 * maxZ) + 1 - 1e-9)));
    }

    // An in-plane axis: the coordinate direction the normal leans on least, made perpendicular to it.
    private static (double Row, double Col, double Plane) InPlaneAxis((double Row, double Col, double Plane) n)
    {
        (double Row, double Col, double Plane) reference =
            Math.Abs(n.Row) <= Math.Abs(n.Col) && Math.Abs(n.Row) <= Math.Abs(n.Plane) ? (1, 0, 0)
            : Math.Abs(n.Col) <= Math.Abs(n.Plane) ? (0, 1, 0)
            : (0, 0, 1);
        (double Row, double Col, double Plane) axis = Cross(n, reference);
        double length = Math.Sqrt((axis.Row * axis.Row) + (axis.Col * axis.Col) + (axis.Plane * axis.Plane));
        return (axis.Row / length, axis.Col / length, axis.Plane / length);
    }

    private static (double Row, double Col, double Plane) Cross(
        (double Row, double Col, double Plane) a, (double Row, double Col, double Plane) b) =>
        ((a.Col * b.Plane) - (a.Plane * b.Col),
         (a.Plane * b.Row) - (a.Row * b.Plane),
         (a.Row * b.Col) - (a.Col * b.Row));

    // How far along each in-plane axis the plane still meets the volume — the 'limit' output size.
    private static (int MinU, int MaxU, int MinV, int MaxV) Extent(
        Volume volume,
        (double Row, double Col, double Plane) point,
        (double Row, double Col, double Plane) u,
        (double Row, double Col, double Plane) v)
    {
        int reach = (int)Math.Ceiling(Math.Sqrt(
            ((double)volume.Height * volume.Height) + ((double)volume.Width * volume.Width) +
            ((double)volume.Depth * volume.Depth)));
        int minU = int.MaxValue;
        int maxU = int.MinValue;
        int minV = int.MaxValue;
        int maxV = int.MinValue;
        for (int b = -reach; b <= reach; b++)
        {
            for (int a = -reach; a <= reach; a++)
            {
                double row = point.Row + (a * u.Row) + (b * v.Row);
                double col = point.Col + (a * u.Col) + (b * v.Col);
                double plane = point.Plane + (a * u.Plane) + (b * v.Plane);
                if (!Inside(volume, row, col, plane))
                {
                    continue;
                }

                minU = Math.Min(minU, a);
                maxU = Math.Max(maxU, a);
                minV = Math.Min(minV, b);
                maxV = Math.Max(maxV, b);
            }
        }

        // A plane that misses the volume entirely still has to return something with a size.
        return minU > maxU ? (0, 0, 0, 0) : (minU, maxU, minV, maxV);
    }

    private static bool Inside(Volume volume, double row, double col, double plane) =>
        row >= -0.5 && row <= volume.Height - 0.5
        && col >= -0.5 && col <= volume.Width - 0.5
        && plane >= -0.5 && plane <= volume.Depth - 0.5;
}
