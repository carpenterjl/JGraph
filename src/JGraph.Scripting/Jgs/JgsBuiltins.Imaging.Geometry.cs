using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave C: the geometric transforms — <c>affine2d</c>, <c>projective2d</c>, <c>rigid2d</c> and
/// <c>imref2d</c>, the <c>fitgeotrans</c> estimator, <c>imwarp</c> and <c>imtranslate</c>, the point
/// mappers, and the <c>impyramid</c>/<c>checkerboard</c> pair.
/// </summary>
/// <remarks>
/// MATLAB ships these four as classes, and JGraph has no object system to put them in. They are
/// therefore <em>tagged structs</em>: an ordinary struct whose <c>Type</c> field names the class it
/// stands for, so <c>tform.T</c> reads exactly as it does in MATLAB, <c>class(tform)</c> answers
/// <c>'affine2d'</c>, and every consumer here can tell a transform from a spatial reference without
/// guessing from the field names. What a script cannot do is call a method with dot syntax —
/// <c>tform.invert()</c> is not a thing — which is why <c>transformPointsForward</c> and
/// <c>transformPointsInverse</c> exist as functions taking the transform first, the form MATLAB
/// documents anyway.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The field that says which MATLAB class a tagged struct is standing in for.</summary>
    private const string TransformTag = "Type";

    private static readonly ImgOptionSpec ImWarpSpec = new(
        "imwarp",
        ["nearest", "linear", "bilinear", "cubic", "bicubic", "lanczos2", "lanczos3"],
        ["OutputView", "FillValues", "SmoothEdges"]);

    private static readonly ImgOptionSpec ImTranslateSpec = new(
        "imtranslate", [], ["OutputView", "FillValues", "Method"]);

    private static readonly ImgOptionSpec AffineOutputViewSpec = new(
        "affineOutputView", [], ["BoundsStyle"]);

    private static void DefineGeometryBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- The transform objects, as tagged structs ------------------------------------------
        define("affine2d", (args, line, col) =>
        {
            ArityRange("affine2d", args, 0, 1, line, col);
            GeometricTransform transform = args.Count == 0
                ? GeometricTransform.Identity
                : new GeometricTransform(Square3("affine2d", args, 0, line, col));
            if (!transform.IsAffine)
            {
                throw new JgsRuntimeException(line, col,
                    "affine2d: the last column of T must be [0; 0; 1] — use projective2d for a homography.");
            }

            return TransformValue("affine2d", transform);
        });

        define("projective2d", (args, line, col) =>
        {
            ArityRange("projective2d", args, 0, 1, line, col);
            return TransformValue("projective2d", args.Count == 0
                ? GeometricTransform.Identity
                : new GeometricTransform(Square3("projective2d", args, 0, line, col)));
        });

        define("rigid2d", (args, line, col) =>
        {
            ArityRange("rigid2d", args, 0, 2, line, col);
            double[,] matrix;
            if (args.Count == 0)
            {
                matrix = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            }
            else if (args.Count == 1)
            {
                matrix = Square3("rigid2d", args, 0, line, col);
            }
            else
            {
                double[,] rotation = Matrix("rigid2d", args, 0, line, col);
                if (rotation.GetLength(0) != 2 || rotation.GetLength(1) != 2)
                {
                    throw new JgsRuntimeException(line, col, "rigid2d(R, t): R is a 2-by-2 rotation matrix.");
                }

                double[] translation = NumericVector("rigid2d", args[1], line, col);
                if (translation.Length != 2)
                {
                    throw new JgsRuntimeException(line, col, "rigid2d(R, t): t is a two-element [tx ty] vector.");
                }

                matrix = new double[,]
                {
                    { rotation[0, 0], rotation[0, 1], 0 },
                    { rotation[1, 0], rotation[1, 1], 0 },
                    { translation[0], translation[1], 1 },
                };
            }

            // A rigid transform is exactly a rotation plus a shift; accepting a scaled or skewed
            // matrix here would make every later isRigid claim about it a lie.
            double a = matrix[0, 0];
            double b = matrix[0, 1];
            double c = matrix[1, 0];
            double d = matrix[1, 1];
            if (Math.Abs((a * a) + (b * b) - 1) > 1e-8 || Math.Abs((c * c) + (d * d) - 1) > 1e-8 ||
                Math.Abs((a * c) + (b * d)) > 1e-8 || Math.Abs((a * d) - (b * c) - 1) > 1e-8)
            {
                throw new JgsRuntimeException(line, col,
                    "rigid2d: the rotation part must be orthonormal with determinant 1 " +
                    "(a rotation, not a scaling or a reflection).");
            }

            return TransformValue("rigid2d", new GeometricTransform(matrix));
        });

        define("imref2d", (args, line, col) =>
        {
            ArityRange("imref2d", args, 0, 3, line, col);
            if (args.Count == 0)
            {
                return SpatialRefValue(new SpatialRef(2, 2));
            }

            double[] size = NumericVector("imref2d", args[0], line, col);
            if (size.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "imref2d: the image size is [rows, cols].");
            }

            int rows = (int)Math.Round(size[0]);
            int cols = (int)Math.Round(size[1]);
            if (rows < 1 || cols < 1)
            {
                throw new JgsRuntimeException(line, col, "imref2d: the image size must be positive.");
            }

            if (args.Count == 1)
            {
                return SpatialRefValue(new SpatialRef(rows, cols));
            }

            if (args.Count != 3)
            {
                throw new JgsRuntimeException(line, col,
                    "imref2d takes the size alone, or the size with both world limits or both pixel extents.");
            }

            double[] x = NumericVector("imref2d", args[1], line, col);
            double[] y = NumericVector("imref2d", args[2], line, col);
            if (x.Length != y.Length || x.Length is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col,
                    "imref2d: give both world limits as [min max] pairs, or both pixel extents as numbers.");
            }

            // A pair is a world limit; a bare number is the size of one pixel, which fixes the limits
            // at half a pixel outside the first and last centres.
            return SpatialRefValue(x.Length == 2
                ? new SpatialRef(rows, cols, x[0], x[1], y[0], y[1])
                : new SpatialRef(rows, cols, 0.5 * x[0], (cols + 0.5) * x[0], 0.5 * y[0], (rows + 0.5) * y[0]));
        });

        define("fitgeotrans", (args, line, col) =>
        {
            Arity("fitgeotrans", args, 3, line, col);
            double[,] moving = PointPairs("fitgeotrans", args, 0, line, col);
            double[,] fixedPoints = PointPairs("fitgeotrans", args, 1, line, col);
            string kind = Str("fitgeotrans", args, 2, line, col).ToLowerInvariant();

            if (kind is "polynomial" or "pwl" or "lwm")
            {
                throw new JgsRuntimeException(line, col,
                    $"fitgeotrans: '{kind}' is not implemented — the local transformation types " +
                    "('polynomial', 'pwl', 'lwm') have no matrix form and no imwarp path here. Use " +
                    "'affine' or 'projective'.");
            }

            TransformKind wanted = kind switch
            {
                "nonreflectivesimilarity" => TransformKind.NonreflectiveSimilarity,
                "similarity" => TransformKind.Similarity,
                "affine" => TransformKind.Affine,
                "projective" => TransformKind.Projective,
                _ => throw new JgsRuntimeException(line, col,
                    $"fitgeotrans: unknown transformation type '{kind}' (use 'nonreflectivesimilarity', " +
                    "'similarity', 'affine', or 'projective')."),
            };

            try
            {
                GeometricTransform transform = GeometricTransform.Fit(moving, fixedPoints, wanted);
                return TransformValue(
                    wanted == TransformKind.Projective ? "projective2d" : "affine2d", transform);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"fitgeotrans: {ex.Message}");
            }
        });

        // --- Point mapping ---------------------------------------------------------------------
        define("transformPointsForward", (args, line, col) =>
            TransformPointsOutputs("transformPointsForward", args, 1, line, col)[0]);

        define("transformPointsInverse", (args, line, col) =>
            TransformPointsOutputs("transformPointsInverse", args, 1, line, col)[0]);

        define("affineOutputView", (args, line, col) =>
        {
            ArityRange("affineOutputView", args, 2, 4, line, col);
            ImgArgs parsed = AffineOutputViewSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "affineOutputView(sizeA, tform) needs a size and a transform.");
            }

            double[] size = NumericVector("affineOutputView", parsed.Positional[0], line, col);
            if (size.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "affineOutputView: the size is [rows, cols].");
            }

            var input = new SpatialRef((int)Math.Round(size[0]), (int)Math.Round(size[1]));
            GeometricTransform transform = ReadTransform("affineOutputView", parsed.Positional, 1, line, col);
            string style = parsed.Text("BoundsStyle") ?? "CenterOutput";
            return SpatialRefValue(style.ToLowerInvariant() switch
            {
                "centeroutput" => Warping.CenterOutput(input, transform),
                "followoutput" => Warping.FollowOutput(input, transform),
                "sameasinput" => input,
                _ => throw new JgsRuntimeException(line, col,
                    $"affineOutputView: unknown 'BoundsStyle' value '{style}' " +
                    "(use 'CenterOutput', 'FollowOutput', or 'SameAsInput')."),
            });
        });

        // --- Warping ---------------------------------------------------------------------------
        define("imwarp", (args, line, col) => ImWarpOutputs(args, 1, line, col)[0]);
        define("imtranslate", (args, line, col) => ImTranslateOutputs(args, 1, line, col)[0]);

        // --- Pyramids and patterns -------------------------------------------------------------
        define("impyramid", (args, line, col) =>
        {
            Arity("impyramid", args, 2, line, col);
            using ImgArg source = ImgLike("impyramid", args, 0, line, col);
            string direction = Str("impyramid", args, 1, line, col).ToLowerInvariant();
            bool expand = direction switch
            {
                "reduce" => false,
                "expand" => true,
                _ => throw new JgsRuntimeException(line, col,
                    $"impyramid: unknown direction '{direction}' (use 'reduce' or 'expand')."),
            };

            return ImgLikeOut(Geometry.Pyramid(source.Buffer, expand), source);
        });

        define("checkerboard", (args, line, col) =>
        {
            ArityRange("checkerboard", args, 0, 3, line, col);
            int square = args.Count >= 1 ? Count("checkerboard", args, 0, line, col) : 10;
            int rows = args.Count >= 2 ? Count("checkerboard", args, 1, line, col) : 4;
            int cols = args.Count >= 3 ? Count("checkerboard", args, 2, line, col) : rows;
            if (square < 1 || rows < 1 || cols < 1)
            {
                throw new JgsRuntimeException(line, col, "checkerboard sizes must be positive whole numbers.");
            }

            return ImgOut(Geometry.Checkerboard(square, rows, cols), ImageClass.Double);
        });

        _ = dialect;
    }

    /// <summary>
    /// <c>[B, RB] = imwarp(A, tform)</c>. Without an output view the frame is the whole transformed
    /// image at the input's pixel size, which is why a rotation grows the picture the way it does.
    /// </summary>
    private static JgsValue[] ImWarpOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imwarp", args, 2, 10, line, col);
        ImgArgs parsed = ImWarpSpec.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "imwarp(A, tform) needs an image and a transform.");
        }

        using ImgArg source = ImgLike("imwarp", parsed.Positional, 0, line, col);
        int transformSlot = parsed.Positional.Count >= 3 ? 2 : 1;
        SpatialRef input = transformSlot == 2
            ? ReadSpatialRef("imwarp", parsed.Positional[1], source.Buffer, line, col)
            : new SpatialRef(source.Buffer.Height, source.Buffer.Width);
        GeometricTransform transform = ReadTransform("imwarp", parsed.Positional, transformSlot, line, col);

        SpatialRef output = parsed.Named("OutputView") is { } view
            ? ReadSpatialRef("imwarp", view, source.Buffer, line, col)
            : Warping.FollowOutput(input, transform);

        Geometry.Interpolation method = WarpMethod("imwarp", parsed, Geometry.Interpolation.Bilinear, line, col);
        double[] fill = FillValues("imwarp", parsed, source.Buffer, line, col);
        bool smooth = parsed.Flag("SmoothEdges", false);

        ImageBuffer warped = Warping.Warp(source.Buffer, input, transform, output, method, fill, smooth);
        JgsValue image = ImgLikeOut(warped, source);
        return wanted < 2 ? [image] : [image, SpatialRefValue(output)];
    }

    /// <summary>
    /// <c>[B, RB] = imtranslate(A, [tx ty])</c>. A translation is just a transform with a shifted last
    /// row, so it rides the same warp; 'full' is the only part that needs its own frame.
    /// </summary>
    private static JgsValue[] ImTranslateOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imtranslate", args, 2, 9, line, col);
        ImgArgs parsed = ImTranslateSpec.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "imtranslate(A, [tx ty]) needs an image and a translation.");
        }

        using ImgArg source = ImgLike("imtranslate", parsed.Positional, 0, line, col);
        int shiftSlot = parsed.Positional.Count >= 3 ? 2 : 1;
        SpatialRef input = shiftSlot == 2
            ? ReadSpatialRef("imtranslate", parsed.Positional[1], source.Buffer, line, col)
            : new SpatialRef(source.Buffer.Height, source.Buffer.Width);

        double[] shift = NumericVector("imtranslate", parsed.Positional[shiftSlot], line, col);
        if (shift.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "imtranslate: the translation is [tx ty], in pixels.");
        }

        double dx = shift[0] * input.PixelExtentX;
        double dy = shift[1] * input.PixelExtentY;
        GeometricTransform transform = GeometricTransform.Translation(dx, dy);

        string view = (parsed.Text("OutputView") ?? "same").ToLowerInvariant();
        SpatialRef output = view switch
        {
            "same" => input,
            "full" => FullTranslationView(input, dx, dy),
            _ => throw new JgsRuntimeException(line, col,
                $"imtranslate: unknown 'OutputView' value '{view}' (use 'same' or 'full')."),
        };

        Geometry.Interpolation method = parsed.Text("Method") is { } word
            ? ParseInterpolation(word, line, col)
            : Geometry.Interpolation.Bilinear;
        double[] fill = FillValues("imtranslate", parsed, source.Buffer, line, col);

        ImageBuffer moved = Warping.Warp(source.Buffer, input, transform, output, method, fill, smoothEdges: false);
        JgsValue image = ImgLikeOut(moved, source);
        return wanted < 2 ? [image] : [image, SpatialRefValue(output)];
    }

    /// <summary>
    /// <c>[U, V] = transformPointsForward(tform, x, y)</c> and its inverse. One point set as an n×2
    /// matrix comes back as an n×2 matrix; separate x and y come back separately.
    /// </summary>
    private static JgsValue[] TransformPointsOutputs(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        GeometricTransform transform = ReadTransform(name, args, 0, line, col);
        bool forward = name == "transformPointsForward";

        if (args.Count == 3)
        {
            double[] x = NumericVector(name, args[1], line, col);
            double[] y = NumericVector(name, args[2], line, col);
            if (x.Length != y.Length)
            {
                throw new JgsRuntimeException(line, col, $"{name}: x and y must have the same number of points.");
            }

            var u = new double[x.Length];
            var v = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                (u[i], v[i]) = forward ? transform.Forward(x[i], y[i]) : transform.Inverse(x[i], y[i]);
            }

            return wanted < 2 ? [Numbers(u)] : [Numbers(u), Numbers(v)];
        }

        double[,] points = PointPairs(name, args, 1, line, col);
        int count = points.GetLength(0);
        var mapped = new double[count, 2];
        for (int i = 0; i < count; i++)
        {
            (double mx, double my) = forward
                ? transform.Forward(points[i, 0], points[i, 1])
                : transform.Inverse(points[i, 0], points[i, 1]);
            mapped[i, 0] = mx;
            mapped[i, 1] = my;
        }

        return [MatrixToRows(mapped)];
    }

    /// <summary>
    /// <c>[J, rect] = imcrop(I, rect)</c> under the MATLAB dialect, where the rectangle is drawn in
    /// world coordinates and its edges fall <em>between</em> pixels. That is why cropping with
    /// <c>[60 40 100 90]</c> yields 91 rows by 101 columns and not 90 by 100: the rectangle spans the
    /// centres of pixels 40 through 130, and both ends are inside it.
    /// </summary>
    private static JgsValue[] MatlabCropOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imcrop", args, 1, 3, line, col);
        using ImgArg source = ImgLike("imcrop", args, 0, line, col);
        ImageBuffer image = source.Buffer;
        SpatialRef reference = args.Count == 3
            ? ReadSpatialRef("imcrop", args[1], image, line, col)
            : new SpatialRef(image.Height, image.Width);

        if (args.Count == 1)
        {
            // MATLAB opens a window and waits for a rectangle to be drawn. There is no window here,
            // so the honest answer is the whole picture and the rectangle that describes it.
            JgsValue whole = ImgLikeOut(image.Clone(), source);
            return wanted < 2
                ? [whole]
                : [whole, Numbers([
                    reference.XWorldMin, reference.YWorldMin,
                    reference.XWorldMax - reference.XWorldMin,
                    reference.YWorldMax - reference.YWorldMin])];
        }

        double[] rect = NumericVector("imcrop", args[^1], line, col);
        if (rect.Length != 4)
        {
            throw new JgsRuntimeException(line, col, "imcrop rect must be [xmin, ymin, width, height].");
        }

        int left = (int)Math.Round(reference.XToIntrinsic(rect[0]), MidpointRounding.AwayFromZero) - 1;
        int right = (int)Math.Round(reference.XToIntrinsic(rect[0] + rect[2]), MidpointRounding.AwayFromZero) - 1;
        int top = (int)Math.Round(reference.YToIntrinsic(rect[1]), MidpointRounding.AwayFromZero) - 1;
        int bottom = (int)Math.Round(reference.YToIntrinsic(rect[1] + rect[3]), MidpointRounding.AwayFromZero) - 1;

        left = Math.Clamp(left, 0, image.Width - 1);
        right = Math.Clamp(right, 0, image.Width - 1);
        top = Math.Clamp(top, 0, image.Height - 1);
        bottom = Math.Clamp(bottom, 0, image.Height - 1);
        if (right < left || bottom < top)
        {
            throw new JgsRuntimeException(line, col,
                "imcrop: the rectangle does not overlap the image.");
        }

        JgsValue cropped = ImgLikeOut(
            Geometry.Crop(image, left, top, right - left + 1, bottom - top + 1), source);
        return wanted < 2 ? [cropped] : [cropped, Numbers(rect)];
    }

    /// <summary>The frame <c>imtranslate</c>'s 'full' view needs: the union of the old and new extents.</summary>
    private static SpatialRef FullTranslationView(SpatialRef input, double dx, double dy)
    {
        double xMin = Math.Min(input.XWorldMin, input.XWorldMin + dx);
        double xMax = Math.Max(input.XWorldMax, input.XWorldMax + dx);
        double yMin = Math.Min(input.YWorldMin, input.YWorldMin + dy);
        double yMax = Math.Max(input.YWorldMax, input.YWorldMax + dy);

        int cols = Math.Max(1, (int)Math.Ceiling(((xMax - xMin) / input.PixelExtentX) - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling(((yMax - yMin) / input.PixelExtentY) - 1e-9));
        return new SpatialRef(
            rows, cols,
            xMin, xMin + (cols * input.PixelExtentX),
            yMin, yMin + (rows * input.PixelExtentY));
    }

    /// <summary>Wraps a transform as the tagged struct a script sees.</summary>
    private static JgsValue TransformValue(string type, GeometricTransform transform)
    {
        double[,] t = transform.Matrix;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [TransformTag] = JgsValue.Str(type),
            ["T"] = MatrixToRows(t),
            ["Dimensionality"] = JgsValue.Number(2),
        };

        if (type == "rigid2d")
        {
            fields["Rotation"] = MatrixToRows(new[,] { { t[0, 0], t[0, 1] }, { t[1, 0], t[1, 1] } });
            fields["Translation"] = Numbers([t[2, 0], t[2, 1]]);
        }

        return JgsValue.Struct(fields);
    }

    /// <summary>Wraps a spatial reference as the tagged struct <c>imref2d</c> returns.</summary>
    private static JgsValue SpatialRefValue(SpatialRef reference) =>
        JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [TransformTag] = JgsValue.Str("imref2d"),
            ["ImageSize"] = Numbers([reference.Rows, reference.Cols]),
            ["XWorldLimits"] = Numbers([reference.XWorldMin, reference.XWorldMax]),
            ["YWorldLimits"] = Numbers([reference.YWorldMin, reference.YWorldMax]),
            ["PixelExtentInWorldX"] = JgsValue.Number(reference.PixelExtentX),
            ["PixelExtentInWorldY"] = JgsValue.Number(reference.PixelExtentY),
            ["ImageExtentInWorldX"] = JgsValue.Number(reference.XWorldMax - reference.XWorldMin),
            ["ImageExtentInWorldY"] = JgsValue.Number(reference.YWorldMax - reference.YWorldMin),
            ["XIntrinsicLimits"] = Numbers([0.5, reference.Cols + 0.5]),
            ["YIntrinsicLimits"] = Numbers([0.5, reference.Rows + 0.5]),
        });

    /// <summary>The MATLAB class a tagged struct stands for, or null when the value is not one.</summary>
    private static string? TaggedClassOf(JgsValue value)
    {
        if (value.Type != JgsType.Struct ||
            !value.AsStruct.TryGetValue(TransformTag, out JgsValue? tag) ||
            tag is null || tag.Type != JgsType.String)
        {
            return null;
        }

        return tag.AsString switch
        {
            "affine2d" or "projective2d" or "rigid2d" or "imref2d" => tag.AsString,
            _ => null,
        };
    }

    /// <summary>Reads a transform argument: a tagged struct, or a bare 3×3 matrix.</summary>
    private static GeometricTransform ReadTransform(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Struct)
        {
            if (TaggedClassOf(value) == "imref2d")
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} expects argument {index + 1} to be a transform, but got an imref2d.");
            }

            if (!value.AsStruct.TryGetValue("T", out JgsValue? matrix) || matrix is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} expects argument {index + 1} to be a transform (a struct with a T field).");
            }

            return BuildTransform(name, matrix, line, col);
        }

        return BuildTransform(name, value, line, col);
    }

    private static GeometricTransform BuildTransform(string name, JgsValue matrix, int line, int col)
    {
        try
        {
            return new GeometricTransform(Square3(name, [matrix], 0, line, col));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads a spatial-reference argument, falling back to the image's own default frame when the
    /// value is an image size rather than an <c>imref2d</c>.
    /// </summary>
    private static SpatialRef ReadSpatialRef(string name, JgsValue value, ImageBuffer image, int line, int col)
    {
        if (value.Type != JgsType.Struct || TaggedClassOf(value) != "imref2d")
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the spatial reference must come from imref2d.");
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        double[] size = fields.TryGetValue("ImageSize", out JgsValue? sizeValue) && sizeValue is not null
            ? NumericVector(name, sizeValue, line, col)
            : [image.Height, image.Width];
        double[] x = fields.TryGetValue("XWorldLimits", out JgsValue? xValue) && xValue is not null
            ? NumericVector(name, xValue, line, col)
            : [0.5, size[1] + 0.5];
        double[] y = fields.TryGetValue("YWorldLimits", out JgsValue? yValue) && yValue is not null
            ? NumericVector(name, yValue, line, col)
            : [0.5, size[0] + 0.5];
        if (size.Length < 2 || x.Length != 2 || y.Length != 2)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the spatial reference is malformed.");
        }

        return new SpatialRef((int)Math.Round(size[0]), (int)Math.Round(size[1]), x[0], x[1], y[0], y[1]);
    }

    /// <summary>
    /// An n×2 array of [x y] rows, which is how every point-set argument here arrives. One point may
    /// be written as a bare <c>[x y]</c> — MATLAB never asks for a 1×2 to be built as a matrix first.
    /// </summary>
    private static double[,] PointPairs(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double[,] points = Rectangle($"{name} argument {index + 1}", args[index], line, col);
        if (points.GetLength(1) != 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be an n-by-2 array of [x y] rows.");
        }

        return points;
    }

    /// <summary>A 3×3 matrix argument, the shape every transform's T has.</summary>
    private static double[,] Square3(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double[,] matrix = Rectangle($"{name} argument {index + 1}", args[index], line, col);
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a 3-by-3 transform matrix.");
        }

        return matrix;
    }

    /// <summary>The interpolation word from a warp option tail, in any of MATLAB's spellings.</summary>
    private static Geometry.Interpolation WarpMethod(
        string name, ImgArgs parsed, Geometry.Interpolation fallback, int line, int col)
    {
        string word = parsed.OneOf(
            string.Empty, "nearest", "linear", "bilinear", "cubic", "bicubic", "lanczos2", "lanczos3");
        return word.Length == 0 ? fallback : ParseInterpolation(word, line, col);
    }

    /// <summary>
    /// A 'FillValues' option, converted from the image's own class so a script that writes 255 for a
    /// <c>uint8</c> picture gets white rather than a clipped nonsense value.
    /// </summary>
    private static double[] FillValues(string name, ImgArgs parsed, ImageBuffer image, int line, int col)
    {
        if (parsed.Named("FillValues") is not { } value)
        {
            return [0.0];
        }

        double[] raw = NumericVector(name, value, line, col);
        if (raw.Length is not 1 && raw.Length != image.Channels)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'FillValues' takes one value or one per channel ({image.Channels}).");
        }

        var fill = new double[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            fill[i] = image.Class.FromNative(raw[i]);
        }

        return fill;
    }
}
