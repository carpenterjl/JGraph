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

        if (value.Type == JgsType.Array && JgsMatrix.DimsOf(value) is [int high, int wide, 3])
        {
            var planes = new ImageBuffer(high, wide, 3);
            for (int ch = 0; ch < 3; ch++)
            {
                for (int c = 0; c < wide; c++)
                {
                    for (int r = 0; r < high; r++)
                    {
                        // Column-major storage: page ch, column c, row r.
                        planes[r, c, ch] = value.ElementAt(r + (c * high) + (ch * high * wide)).AsNumber;
                    }
                }
            }

            return new ImgArg(planes, ImgShape.Planes);
        }

        return new ImgArg(
            PointOps.WrapValues(Rectangle($"{name} argument {index + 1}", value, line, col)), ImgShape.Matrix);
    }

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

        using (result)
        {
            // Colour planes came in, so colour planes go back — unless the operation collapsed the
            // picture to one value per pixel, which is a plain matrix in anybody's reading.
            if (source.Shape == ImgShape.Planes && result.Channels == 3)
            {
                var flat = new double[result.Height * result.Width * 3];
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int c = 0; c < result.Width; c++)
                    {
                        for (int r = 0; r < result.Height; r++)
                        {
                            flat[r + (c * result.Height) + (ch * result.Height * result.Width)] = result[r, c, ch];
                        }
                    }
                }

                return JgsMatrix.FromColumnMajorDims(flat, [result.Height, result.Width, 3]);
            }

            return MatrixToRows(PointOps.ToMatrix(result, 0));
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
            : ImgLikeOut(result, source);
}
