using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The image-specific half of imaging argument handling: how a picture arrives, and how a result
/// goes back in the shape it came in. The option parser these builtins declare their specs against
/// is <see cref="OptionSpec"/> in <c>JgsBuiltins.Options.cs</c> — it was written here for M46 and
/// moved out in M52 once the base-language builtins needed the same parsing.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Wraps a freshly computed image as a value of the given class, snapping integer classes onto
    /// their sample grid. Every image-returning builtin goes through here, which is the one place the
    /// output class of an operation is decided.
    /// </summary>
    private static JgsValue ImgOut(ImageBuffer result, ImageClass imageClass)
    {
        result.Class = imageClass;
        ImageClassInfo.Quantize(result);
        return JgsValue.Image(result);
    }

    /// <summary>Wraps a result that keeps its input's class — filters, geometry, most arithmetic.</summary>
    private static JgsValue ImgOut(ImageBuffer result, ImageBuffer source) => ImgOut(result, source.Class);

    /// <summary>
    /// An argument to a builtin that MATLAB applies to images and to plain numeric data alike —
    /// <c>imfilter</c>, <c>padarray</c>, <c>ordfilt2</c>, the block family. MATLAB draws no line here
    /// because an image simply is a matrix; JGraph has a distinct image value, so the line is drawn
    /// once, at the boundary, and the result is handed back in whatever form the argument arrived in.
    /// </summary>
    /// <remarks>
    /// A matrix argument is wrapped in a temporary buffer this owns and must dispose. An image
    /// argument is owned by its <see cref="JgsValue"/> and must not be — hence the shape rather than
    /// a blanket <c>using</c>.
    /// </remarks>
    private readonly struct ImgArg(ImageBuffer buffer, ImgShape shape) : IDisposable
    {
        /// <summary>The samples, however they arrived.</summary>
        public ImageBuffer Buffer => buffer;

        /// <summary>Which form arrived, and so which must come back.</summary>
        public ImgShape Shape => shape;

        /// <summary>Whether the caller passed plain numbers, and so expects plain numbers back.</summary>
        public bool FromMatrix => shape != ImgShape.Image;

        /// <summary>Releases the temporary buffer a numeric argument was wrapped in.</summary>
        public void Dispose()
        {
            if (shape != ImgShape.Image)
            {
                buffer.Dispose();
            }
        }
    }

    /// <summary>The three forms a picture arrives in.</summary>
    private enum ImgShape
    {
        /// <summary>An image value, carrying a class tag.</summary>
        Image,

        /// <summary>A plain numeric matrix — one channel.</summary>
        Matrix,

        /// <summary>
        /// A plain <c>h×w×3</c> array. MATLAB has no separate image type, so a script that wrote
        /// <c>zeros(h, w, 3)</c> and filled the planes is holding what MATLAB calls an RGB image, and
        /// every function that takes a colour picture has to take this too.
        /// </summary>
        Planes,
    }

    /// <summary>Reads an argument that may be an image value, a numeric matrix, or colour planes.</summary>
    private static ImgArg ImgLike(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Image)
        {
            return new ImgArg(value.AsImage, ImgShape.Image);
        }

        // The samples arrive in the units of the class the array carries, and the buffer beneath every
        // imaging algorithm here holds [0, 1]. A double array is already in those units, which is why
        // this stayed invisible until a script passed the uint8 array getframe hands back: at 0–255
        // every sample sat far above the top of the range and the picture came out white.
        ImageClass carried = CarriedClass(value);

        if (value.Type == JgsType.Array && JgsMatrix.DimsOf(value) is [int high, int wide, 3])
        {
            var planes = new ImageBuffer(high, wide, 3) { Class = carried };
            for (int ch = 0; ch < 3; ch++)
            {
                for (int c = 0; c < wide; c++)
                {
                    for (int r = 0; r < high; r++)
                    {
                        // Column-major storage: page ch, column c, row r.
                        planes[r, c, ch] = carried.FromNative(
                            value.ElementAt(r + (c * high) + (ch * high * wide)).AsNumber);
                    }
                }
            }

            return new ImgArg(planes, ImgShape.Planes);
        }

        ImageBuffer wrapped =
            PointOps.WrapValues(Rectangle($"{name} argument {index + 1}", value, line, col));
        wrapped.Class = carried;
        if (carried.IsInteger())
        {
            Span<double> samples = wrapped.Pixels;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = carried.FromNative(samples[i]);
            }
        }

        return new ImgArg(wrapped, ImgShape.Matrix);
    }

    /// <summary>
    /// The image class a plain numeric argument stands for: the tag <c>uint8</c> and its siblings left
    /// on the array, or <c>logical</c> for a mask.
    /// </summary>
    /// <remarks>
    /// MATLAB's rule is that the class decides the range — <c>uint8</c> is 0-255, <c>uint16</c> and
    /// <c>int16</c> span 16 bits, and only the floating classes are [0, 1]. The five integer classes
    /// no image format stores (<c>int8</c>, <c>int32</c>, <c>int64</c>, <c>uint32</c>, <c>uint64</c>)
    /// have no reading here, so they keep the one they have always had; <c>imwrite</c> refuses them by
    /// name rather than writing whatever a [0, 1] reading of them would produce.
    /// </remarks>
    private static ImageClass CarriedClass(JgsValue value) =>
        IsLogicalValue(value)
            ? ImageClass.Logical
            : value.NumericClass switch
            {
                JgsNumericClass.UInt8 => ImageClass.UInt8,
                JgsNumericClass.UInt16 => ImageClass.UInt16,
                JgsNumericClass.Int16 => ImageClass.Int16,
                JgsNumericClass.Single => ImageClass.Single,
                _ => ImageClass.Double,
            };

    /// <summary>The tag a returned array carries so <c>class</c> answers what it was handed.</summary>
    private static JgsNumericClass NumericClassOf(ImageClass imageClass) => imageClass switch
    {
        ImageClass.UInt8 => JgsNumericClass.UInt8,
        ImageClass.UInt16 => JgsNumericClass.UInt16,
        ImageClass.Int16 => JgsNumericClass.Int16,
        ImageClass.Single => JgsNumericClass.Single,
        _ => JgsNumericClass.Double,
    };

    /// <summary>
    /// Numeric data as a rectangular field. A scalar is a 1×1, and a plain vector — a range, a
    /// <c>linspace</c>, anything that never had a shape put on it — is one row. MATLAB makes no
    /// distinction between those and a matrix, and neither can any function that claims to take "an
    /// array": <c>imresize(1:8, [1 16])</c> is ordinary code.
    /// </summary>
    private static double[,] Rectangle(string what, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return new[,] { { value.AsNumber } };
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} must be numeric data, but got a {value.TypeName}.");
        }

        double[][] rows = JgsMatrix.ToRows(what, value, line, col);
        int cols = rows.Length == 0 ? 0 : rows[0].Length;
        if (rows.Length == 0 || cols == 0)
        {
            throw new JgsRuntimeException(line, col, $"{what} is empty.");
        }

        var result = new double[rows.Length, cols];
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                result[r, c] = rows[r][c];
            }
        }

        return result;
    }

    /// <summary>
    /// Hands a result back in the shape its input arrived in: a matrix for a matrix, a classed image
    /// for an image.
    /// </summary>
    private static JgsValue ImgLikeOut(ImageBuffer result, ImgArg source)
    {
        if (source.Shape == ImgShape.Image)
        {
            return ImgOut(result, source.Buffer.Class);
        }

        return NumbersOut(result, source.Shape, source.Buffer.Class);
    }

    /// <summary>
    /// Hands a buffer back as plain numbers in the units of the class it was read in, undoing the
    /// normalization <see cref="ImgLike"/> applied so a class survives a round trip through an imaging
    /// builtin the way it does in MATLAB.
    /// </summary>
    private static JgsValue NumbersOut(ImageBuffer result, ImgShape shape, ImageClass carried)
    {
        using (result)
        {
            // Colour planes came in, so colour planes go back — unless the operation collapsed the
            // picture to one value per pixel, which is a plain matrix in anybody's reading.
            if (shape == ImgShape.Planes && result.Channels == 3)
            {
                var flat = new double[result.Height * result.Width * 3];
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int c = 0; c < result.Width; c++)
                    {
                        for (int r = 0; r < result.Height; r++)
                        {
                            flat[r + (c * result.Height) + (ch * result.Height * result.Width)] =
                                carried.ToNative(result[r, c, ch]);
                        }
                    }
                }

                JgsValue planes = JgsMatrix.FromColumnMajorDims(flat, [result.Height, result.Width, 3]);
                planes.SetNumericClass(NumericClassOf(carried));
                return planes;
            }

            double[,] plane = PointOps.ToMatrix(result, 0);
            if (carried.IsInteger())
            {
                for (int r = 0; r < plane.GetLength(0); r++)
                {
                    for (int c = 0; c < plane.GetLength(1); c++)
                    {
                        plane[r, c] = carried.ToNative(plane[r, c]);
                    }
                }
            }

            JgsValue numbers = MatrixToRows(plane);
            numbers.SetNumericClass(NumericClassOf(carried));
            return numbers;
        }
    }

    /// <summary>
    /// Hands back a result that is a mask rather than a picture — an edge map, a set of extrema, a
    /// perimeter. It follows the input's shape like <see cref="ImgLikeOut"/>, but an image result is
    /// stamped logical instead of inheriting the input's class: thresholding a <c>uint8</c> photograph
    /// does not produce a <c>uint8</c> answer.
    /// </summary>
    private static JgsValue ImgMaskOut(ImageBuffer result, ImgArg source) =>
        source.Shape == ImgShape.Image
            ? ImgOut(result, ImageClass.Logical)
            // A mask is 0 or 1 whatever it was computed over, so it goes back in logical's units and
            // not in the units of the picture that produced it: thresholding a uint8 array must not
            // hand back 255s.
            : MaskOut(result, source.Shape);

    /// <summary>
    /// Hands a mask back as a plain logical array, the way MATLAB's <c>imbinarize</c> answers
    /// <c>logical</c> over a <c>uint8</c> matrix rather than the class it was handed.
    /// </summary>
    /// <remarks>
    /// A logical array in this interpreter is one whose <em>elements</em> are <see cref="JgsType.Bool"/>
    /// rather than a numeric array wearing a class tag — <c>islogical</c> and <c>class</c> both read the
    /// elements. So the mask is built out of <see cref="JgsValue.Bool"/>: going through
    /// <see cref="NumbersOut"/> gave the right zeros and ones under a <c>double</c> tag, which is what
    /// made <c>class(imbinarize(uint8Matrix, 0.5))</c> answer <c>'double'</c>.
    /// </remarks>
    private static JgsValue MaskOut(ImageBuffer result, ImgShape shape)
    {
        using (result)
        {
            // Colour planes came in, so colour planes go back — unless the operation collapsed the
            // picture to one value per pixel, which is a plain matrix in anybody's reading.
            if (shape == ImgShape.Planes && result.Channels == 3)
            {
                var flat = new JgsValue[result.Height * result.Width * 3];
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int c = 0; c < result.Width; c++)
                    {
                        for (int r = 0; r < result.Height; r++)
                        {
                            flat[r + (c * result.Height) + (ch * result.Height * result.Width)] =
                                JgsValue.Bool(result[r, c, ch] != 0);
                        }
                    }
                }

                return JgsMatrix.FromElementsDims(flat, [result.Height, result.Width, 3]);
            }

            int rows = result.Height;
            int cols = result.Width;
            var elements = new JgsValue[rows * cols];
            for (int c = 0; c < cols; c++)
            {
                int origin = c * rows;
                for (int r = 0; r < rows; r++)
                {
                    elements[origin + r] = JgsValue.Bool(result[r, c, 0] != 0);
                }
            }

            // A one-pixel mask is a single true or false, the way a one-element matrix is a scalar.
            return rows == 1 && cols == 1
                ? elements[0]
                : JgsMatrix.FromElements(elements, rows, cols);
        }
    }
}
