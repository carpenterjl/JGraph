using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Argument parsing for the image-processing builtins. MATLAB's imaging functions take a handful of
/// positional arguments followed by a free-order tail of bare option words (<c>'replicate'</c>,
/// <c>'conv'</c>) and <c>'Name', value</c> pairs (<c>'Sensitivity', 0.6</c>), and before M46 not one
/// imaging builtin here read either shape. One declared spec per builtin replaces what would
/// otherwise be forty hand-rolled tails, and — because the spec knows every legal word — an
/// unrecognized option can name the alternatives instead of just refusing.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>One builtin's option surface.</summary>
    /// <param name="Builtin">The name to put in diagnostics.</param>
    /// <param name="Flags">Bare words accepted anywhere in the option tail.</param>
    /// <param name="Names">Names that consume the argument after them.</param>
    /// <param name="AllowNumericFlag">Whether a bare number may appear in the tail (imfilter's pad value).</param>
    /// <param name="StringPositionals">
    /// How many leading positional slots may legitimately hold a string — imwrite's path, for one.
    /// Past that a string is read as an option word, so a misspelling like <c>'adaptiv'</c> is reported
    /// against the list of real options instead of being swallowed as data and failing later with a
    /// message about the wrong thing.
    /// </param>
    internal sealed record ImgOptionSpec(
        string Builtin,
        string[] Flags,
        string[] Names,
        bool AllowNumericFlag = false,
        int StringPositionals = 0)
    {
        /// <summary>
        /// Splits <paramref name="args"/> into positional arguments and options. The option tail starts
        /// at the first string that matches a declared flag or name, which means a string that is
        /// genuinely positional (a file path, a method word consumed positionally) has to come before
        /// any option — exactly MATLAB's own rule.
        /// </summary>
        /// <param name="args">The call's arguments.</param>
        /// <param name="positionalMax">How many leading arguments may be positional.</param>
        /// <param name="line">Source line, for diagnostics.</param>
        /// <param name="col">Source column, for diagnostics.</param>
        public ImgArgs Parse(IReadOnlyList<JgsValue> args, int positionalMax, int line, int col)
        {
            var positional = new List<JgsValue>();
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var named = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
            JgsValue? numericFlag = null;

            int i = 0;
            while (i < args.Count && positional.Count < positionalMax && !StartsOptions(args[i], positional.Count))
            {
                positional.Add(args[i]);
                i++;
            }

            while (i < args.Count)
            {
                JgsValue arg = args[i];
                if (arg.Type == JgsType.String)
                {
                    string word = arg.AsString;
                    if (MatchName(word) is { } canonical)
                    {
                        if (i + 1 >= args.Count)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{Builtin}: '{canonical}' needs a value after it.");
                        }

                        named[canonical] = args[i + 1];
                        i += 2;
                        continue;
                    }

                    if (MatchFlag(word) is { } flag)
                    {
                        flags.Add(flag);
                        i++;
                        continue;
                    }

                    throw new JgsRuntimeException(line, col,
                        $"{Builtin}: unknown option '{word}'{Alternatives()}.");
                }

                if (AllowNumericFlag && arg.Type == JgsType.Number && numericFlag is null)
                {
                    numericFlag = arg;
                    i++;
                    continue;
                }

                throw new JgsRuntimeException(line, col,
                    $"{Builtin}: unexpected argument {i + 1}; options come after the data" +
                    $"{Alternatives()}.");
            }

            return new ImgArgs(Builtin, positional, flags, named, numericFlag, line, col);
        }

        private bool StartsOptions(JgsValue value, int slot) =>
            value.Type == JgsType.String && slot >= StringPositionals;

        // Case-insensitive but never partial: MATLAB tolerates unambiguous abbreviations, and copying
        // that would mean a later option could silently change what an existing script's abbreviation
        // resolves to. Spelling an option in full is a fixed target.
        private string? MatchName(string word) => Find(Names, word);

        private string? MatchFlag(string word) => Find(Flags, word);

        private static string? Find(string[] candidates, string word)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string Alternatives()
        {
            var all = new List<string>();
            foreach (string flag in Flags)
            {
                all.Add($"'{flag}'");
            }

            foreach (string name in Names)
            {
                all.Add($"'{name}'");
            }

            return all.Count == 0 ? string.Empty : $" (options: {string.Join(", ", all)})";
        }
    }

    /// <summary>The result of <see cref="ImgOptionSpec.Parse"/>: positional arguments plus looked-up options.</summary>
    internal sealed class ImgArgs(
        string builtin,
        IReadOnlyList<JgsValue> positional,
        HashSet<string> flags,
        Dictionary<string, JgsValue> named,
        JgsValue? numericFlag,
        int line,
        int col)
    {
        /// <summary>The leading arguments, before any option word.</summary>
        public IReadOnlyList<JgsValue> Positional => positional;

        /// <summary>A bare number in the option tail, when the spec allows one.</summary>
        public JgsValue? NumericFlag => numericFlag;

        /// <summary>Whether a bare option word was given.</summary>
        public bool Has(string flag) => flags.Contains(flag);

        /// <summary>The first of a mutually exclusive set that was given, or <paramref name="fallback"/>.</summary>
        public string OneOf(string fallback, params string[] choices)
        {
            string? found = null;
            foreach (string choice in choices)
            {
                if (!flags.Contains(choice))
                {
                    continue;
                }

                if (found is not null)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{builtin}: '{found}' and '{choice}' cannot both be given.");
                }

                found = choice;
            }

            return found ?? fallback;
        }

        /// <summary>The raw value of a name-value option, or null when it was not given.</summary>
        public JgsValue? Named(string name) => named.TryGetValue(name, out JgsValue? value) ? value : null;

        /// <summary>A numeric name-value option, or <paramref name="fallback"/>.</summary>
        public double Scalar(string name, double fallback)
        {
            if (Named(name) is not { } value)
            {
                return fallback;
            }

            if (value.Type != JgsType.Number)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes a number.");
            }

            return value.AsNumber;
        }

        /// <summary>A logical name-value option, or <paramref name="fallback"/>.</summary>
        public bool Flag(string name, bool fallback)
        {
            if (Named(name) is not { } value)
            {
                return fallback;
            }

            return value.Type switch
            {
                JgsType.Bool => value.AsBool,
                JgsType.Number => value.AsNumber != 0,
                _ => throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes true or false."),
            };
        }

        /// <summary>A string name-value option, or null.</summary>
        public string? Text(string name)
        {
            if (Named(name) is not { } value)
            {
                return null;
            }

            if (value.Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes a word.");
            }

            return value.AsString;
        }

        /// <summary>A numeric-vector name-value option, or null.</summary>
        public double[]? Vector(string name) =>
            Named(name) is { } value ? ToDoubles(builtin, value, line, col) : null;

        /// <summary>
        /// A window-size option: one number means a square window, a pair means (rows, cols). Null when
        /// the option was not given.
        /// </summary>
        public (int Height, int Width)? Window(string name)
        {
            double[]? size = Vector(name);
            if (size is null)
            {
                return null;
            }

            return size.Length switch
            {
                1 => (Whole(size[0]), Whole(size[0])),
                2 => (Whole(size[0]), Whole(size[1])),
                _ => throw new JgsRuntimeException(line, col,
                    $"{builtin}: '{name}' takes a size or a [rows, cols] pair."),
            };
        }

        private int Whole(double value)
        {
            int rounded = (int)Math.Round(value);
            if (rounded < 1)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: sizes must be positive whole numbers.");
            }

            return rounded;
        }
    }

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
    /// argument is owned by its <see cref="JgsValue"/> and must not be — hence the flag rather than a
    /// blanket <c>using</c>.
    /// </remarks>
    private readonly struct ImgArg(ImageBuffer buffer, bool fromMatrix) : IDisposable
    {
        /// <summary>The samples, however they arrived.</summary>
        public ImageBuffer Buffer => buffer;

        /// <summary>Whether the caller passed a matrix, and so expects a matrix back.</summary>
        public bool FromMatrix => fromMatrix;

        /// <summary>Releases the temporary buffer a matrix argument was wrapped in.</summary>
        public void Dispose()
        {
            if (fromMatrix)
            {
                buffer.Dispose();
            }
        }
    }

    /// <summary>Reads an argument that may be an image value or a numeric matrix.</summary>
    private static ImgArg ImgLike(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        return value.Type == JgsType.Image
            ? new ImgArg(value.AsImage, false)
            : new ImgArg(PointOps.WrapValues(Rectangle($"{name} argument {index + 1}", value, line, col)), true);
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
        if (!source.FromMatrix)
        {
            return ImgOut(result, source.Buffer.Class);
        }

        using (result)
        {
            return MatrixToRows(PointOps.ToMatrix(result, 0));
        }
    }
}
