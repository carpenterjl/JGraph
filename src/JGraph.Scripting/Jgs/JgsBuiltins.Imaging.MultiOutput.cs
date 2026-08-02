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
        JgsEnvironment env, JGraphScriptGlobals host, Random random, JgsDialect dialect)
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

        // M46 wave D. Each of these hands back a palette or a set of planes alongside its picture.
        // A bare `whitepoint` is the default illuminant, the way a bare `eps` is a number — M37's
        // AutoCallsBare, applied to the one wave-D builtin whose no-argument form is the common one.
        if (env.TryGet("whitepoint", out JgsValue whitepoint) && whitepoint.Type == JgsType.Function)
        {
            IJgsCallable body = whitepoint.AsCallable;
            env.Declare("whitepoint", JgsValue.Function(new BuiltinFunction(
                "whitepoint", (args, line, col) => body.Call(args, line, col))
            {
                AutoCallsBare = true,
            }));
        }

        Wrap("gray2ind", (args, wanted, line, col) => GrayToIndOutputs(args, wanted, line, col, dialect));
        Wrap("rgb2ind", (args, wanted, line, col) => RgbToIndOutputs(args, wanted, line, col, dialect));
        Wrap("imapprox", (args, wanted, line, col) => ImApproxOutputs(args, wanted, line, col, dialect));
        Wrap("imsplit", ImSplitOutputs);

        // M46 wave E. Each hands back the thing it measured alongside the picture it produced — the
        // mapping histeq built, the histogram it was matched to, the transmission map the haze
        // estimate found, the noise level non-local means chose for itself.
        Wrap("histeq", HistEqOutputs);
        Wrap("imhistmatch", HistMatchOutputs);
        Wrap("imdiffuseest", DiffuseEstimateOutputs);
        Wrap("imnlmfilt", NonLocalMeansOutputs);
        Wrap("imreducehaze", ReduceHazeOutputs);
        Wrap("imlocalbrighten", LocalBrightenOutputs);

        // M46 wave F. The distance transform's second output says which seed each pixel was measured
        // against, which is how a script turns a distance map into a nearest-object map.
        Wrap("bwdist", (args, wanted, line, col) => BwDistOutputs(args, wanted, line, col, dialect));

        // M46 wave G. Each of these measured something on the way to its answer and MATLAB hands the
        // measurement back rather than making the caller compute it twice: the label map a boundary
        // trace already built, the arrival times a front already walked, the radius a circle already
        // had to know.
        Wrap("bwboundaries", (args, wanted, line, col) => BoundariesOutputs(args, wanted, line, col, dialect));
        Wrap("multithresh", MultiThresholdOutputs);
        Wrap("imsegfmm", (args, wanted, line, col) => FastMarchOutputs(args, wanted, line, col, dialect));
        Wrap("imsegkmeans", (args, wanted, line, col) => KMeansOutputs(args, wanted, line, col, random));
        Wrap("superpixels", SuperpixelsOutputs);
        Wrap("imfindcircles", (args, wanted, line, col) => FindCirclesOutputs(args, wanted, line, col, dialect));

        // M46 wave H. Each of these knows something the caller would otherwise have to recompute from
        // the same inputs: the bin coordinates a projection was measured on, the filter a
        // reconstruction was run through, the ellipse table a phantom was drawn from, where in the
        // picture each block sits, and how well the registration it just made actually agreed.
        Wrap("radon", RadonOutputs);
        Wrap("iradon", IradonOutputs);
        Wrap("phantom", PhantomOutputs);
        Wrap("qtgetblk", (args, wanted, line, col) => QtGetBlkOutputs(args, wanted, line, col, dialect));
        Wrap("imregcorr", ImRegCorrOutputs);

        // M46 wave I. freqspace answers differently depending on how many outputs are asked for — one
        // is a half axis, two are the pair a 2-D grid is built from — and the rest hand back the
        // measurement they had to make anyway: the frequencies a response was read at, the multiplier
        // the regularization settled on, the blur the blind form found, the phase alongside the
        // magnitude.
        Wrap("freqspace", FreqSpaceOutputs);
        Wrap("freqz2", FreqZ2Outputs);
        Wrap("deconvreg", DeconvRegOutputs);
        Wrap("deconvblind", DeconvBlindOutputs);
        Wrap("imgaborfilt", GaborFiltOutputs);

        // M46 wave J. A metric's extra outputs are the working it showed on the way to the number:
        // the per-pixel map a similarity score is the mean of, the precision and recall an F1 balances,
        // the quantized picture a co-occurrence table was counted on. psnr's second output is the same
        // error against a different reference level, and improfile's are where each sample was taken.
        Wrap("psnr", (args, wanted, line, col) => PsnrOutputs(args, wanted, line, col, dialect));
        Wrap("ssim", (args, wanted, line, col) => SsimOutputs(args, wanted, line, col, dialect));
        Wrap("multissim", (args, wanted, line, col) => MultiSsimOutputs(args, wanted, line, col, dialect));
        Wrap("bfscore", (args, wanted, line, col) => BfScoreOutputs(args, wanted, line, col, dialect));
        Wrap("graycomatrix", (args, wanted, line, col) => ComatrixOutputs(args, wanted, line, col, dialect));
        Wrap("improfile", (args, wanted, line, col) => ImProfileOutputs(args, wanted, line, col, dialect));

        // M46 wave K. A volume's extra outputs are the same kinds of thing a picture's are: how many
        // regions a labelling found, where a slice was cut from, the components a magnitude was built
        // out of. Nothing here is volume-specific except that every array involved has three sizes.
        Wrap("bwlabeln", LabelNOutputs);
        Wrap("imgradientxyz", GradientXyzOutputs);
        Wrap("imgradient3", Gradient3Outputs);
        Wrap("imsegkmeans3", KMeans3Outputs);
        Wrap("superpixels3", Superpixels3Outputs);
        Wrap("multissim3", MultiSsim3Outputs);
        Wrap("obliqueslice",
            (args, wanted, line, col) => ObliqueSliceOutputs(args, wanted, line, col, dialect));

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
