using JGraph.Imaging;
using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The multiple-output forms of the imaging builtins under the MATLAB dialect —
/// <c>[L, n] = bwlabel(BW)</c>, <c>[counts, x] = imhist(I)</c>, <c>[level, EM] = graythresh(I)</c>.
/// </summary>
/// <remarks>
/// JGS reaches a builtin's extra outputs by destructuring a returned array
/// (<c>let [gx, gy] = imgradientxy(I)</c>), so the imaging builtins have always returned pairs
/// directly. MATLAB has no such form, and the array leaked out of the single-output call:
/// <c>L = bwlabel(BW)</c> in a <c>.m</c> file evaluated to the <c>[labels, count]</c> pair rather than
/// the label image. Routing those builtins through <see cref="BuiltinFunction.MultiOutput"/> under the
/// MATLAB dialect fixes the single-output form and provides the bracketed one, while JGS keeps the
/// array returns its own scripts and tests are written against.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterImagingMultiOutputForms(
        JgsEnvironment env, JGraphScriptGlobals host, JgsDialect dialect)
    {
        if (!dialect.IsMatlab)
        {
            return;
        }

        // Under MATLAB the single-output call must yield output one alone, so `single` is replaced
        // rather than kept: returning the whole array is the bug this exists to fix.
        void Wrap(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> outputs)
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            env.Declare(name, JgsValue.Function(new BuiltinFunction(
                name,
                (args, line, col) => outputs(args, 1, line, col)[0])
            {
                MultiOutput = outputs,
            }));
        }

        // Builtins that already return a JGS array: unpack it into real outputs.
        void Unpack(string name)
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            IJgsCallable single = existing.AsCallable;
            Wrap(name, (args, wanted, line, col) =>
            {
                JgsValue result = single.Call(args, line, col);
                if (result.Type != JgsType.Array)
                {
                    return [result];
                }

                JgsValue[] parts = result.BoxedElements();
                return wanted >= parts.Length ? parts : parts[..Math.Max(1, wanted)];
            });
        }

        Unpack("bwlabel");
        Unpack("imgradient");
        Unpack("imgradientxy");
        Unpack("hough");
        Unpack("imcentroid");

        // M46 wave B. These three share their single-output body with the bracketed form rather than
        // returning an array, because their first output is itself a vector in one case (bestblk) and
        // an image in the others — unpacking an array would be ambiguous.
        Wrap("edge", EdgeOutputs);
        Wrap("wiener2", WienerOutputs);
        Wrap("bestblk", BestBlkOutputs);

        // M46 wave C. Each of these hands back a spatial reference or a second coordinate alongside
        // the picture, and imcrop is replaced outright: MATLAB's rect is a rectangle in world
        // coordinates whose edges fall between pixels, where JGS keeps the 0-based pixel box.
        Wrap("imwarp", ImWarpOutputs);
        Wrap("imtranslate", ImTranslateOutputs);
        Wrap("imcrop", (args, wanted, line, col) => MatlabCropOutputs(args, wanted, line, col));
        Wrap("transformPointsForward",
            (args, wanted, line, col) => TransformPointsOutputs("transformPointsForward", args, wanted, line, col));
        Wrap("transformPointsInverse",
            (args, wanted, line, col) => TransformPointsOutputs("transformPointsInverse", args, wanted, line, col));

        // [counts, binLocations] = imhist(I). The locations are quoted in the image's own class, so a
        // uint8 picture's bins are centred on 0…255 while a double one's span [0, 1].
        Wrap("imhist", (args, wanted, line, col) =>
        {
            ArityRange("imhist", args, 1, 2, line, col);
            ImageBuffer image = Img("imhist", args, 0, line, col);
            int bins = args.Count == 2 ? Count("imhist", args, 1, line, col) : DefaultBins(image);
            double[] counts;
            try
            {
                counts = Histograms.Histogram(image, bins);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            if (wanted < 2)
            {
                return [Numbers(counts)];
            }

            var locations = new double[bins];
            for (int i = 0; i < bins; i++)
            {
                locations[i] = image.Class.IsInteger()
                    ? image.Class.ToNative(i / (double)(bins - 1))
                    : i / (double)(bins - 1);
            }

            return [Numbers(counts), Numbers(locations)];
        });

        // [level, EM] = graythresh(I) — EM is how much of the variance the split explains.
        Wrap("graythresh", (args, wanted, line, col) =>
        {
            Arity("graythresh", args, 1, line, col);
            (double level, double metric) = Histograms.OtsuLevelAndMetric(Img("graythresh", args, 0, line, col));
            return wanted < 2 ? [JgsValue.Number(level)] : [JgsValue.Number(level), JgsValue.Number(metric)];
        });

        // [level, EM] = otsuthresh(counts), the same measure taken from a histogram alone.
        Wrap("otsuthresh", (args, wanted, line, col) =>
        {
            Arity("otsuthresh", args, 1, line, col);
            try
            {
                (double level, double metric) = Histograms.OtsuFromCounts(
                    ToDoubles("otsuthresh", args[0], line, col));
                return wanted < 2 ? [JgsValue.Number(level)] : [JgsValue.Number(level), JgsValue.Number(metric)];
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        // [X, map, alpha] = imread(path). The map is always empty: Skia decodes a palettized file
        // straight to truecolour and never exposes the palette, which is MATLAB's own answer for a
        // non-indexed file and a recorded divergence for a genuinely indexed one.
        Wrap("imread", (args, wanted, line, col) =>
        {
            ArityRange("imread", args, 1, 2, line, col);
            string path = host.Resolve(Str("imread", args, 0, line, col));
            int frame = args.Count == 2 ? Count("imread", args, 1, line, col) - dialect.IndexBase : 0;
            try
            {
                (ImageBuffer image, ImageBuffer? alpha) = ImageCodec.ReadWithAlpha(path, frame);
                if (wanted < 2)
                {
                    alpha?.Dispose();
                    return [JgsValue.Image(image)];
                }

                JgsValue map = JgsMatrix.Build(0, 3, static (_, _) => 0.0);
                if (wanted < 3)
                {
                    alpha?.Dispose();
                    return [JgsValue.Image(image), map];
                }

                return
                [
                    JgsValue.Image(image),
                    map,
                    alpha is null ? JgsMatrix.Build(0, 0, static (_, _) => 0.0) : JgsValue.Image(alpha),
                ];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or InvalidDataException or ArgumentOutOfRangeException)
            {
                throw new JgsRuntimeException(line, col, $"imread: cannot read '{path}': {ex.Message}");
            }
        });
    }
}
