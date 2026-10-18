using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>imshow</c> for MATLAB-dialect code (open item 142), as R2025b's own does it: base MATLAB's
/// <c>imshow.m</c> and <c>images.internal.imageDisplayParsePVPairs</c>,
/// <c>imageDisplayValidateParams</c> and <c>basicImageDisplay</c> were read, and 80 forms recorded
/// headless (probes <c>oi142_imshow</c>, <c>oi142_errors</c>).
/// <list type="bullet">
/// <item>The picture is made by the high-level <c>image</c> call, so its limits are the pixel edges set
/// by hand on an axes not holding (ADR 0241): a grey or binary picture mapped through the axes'
/// colours, an indexed or true-colour one directly.</item>
/// <item>The axes is then set as <c>basicImageDisplay</c> sets it: reversed y, ticks outward, no grid,
/// a 1:1:1 data aspect, the plot box free, the axes invisible; a grey picture's 256 greys and display
/// range go on its colormap and CLim (the class's range by default, the data's for <c>[]</c>), an
/// indexed one's map on its colormap. Its title and labels stay drawn.</item>
/// <item>A lone picture in a lone axes at the default place makes the figure's NextPlot
/// <c>'replacechildren'</c>, so the next plot starts the figure afresh; R2025b also sizes the
/// figure and the axes to show it at 100%, which this build does not (a recorded divergence).</item>
/// </list>
/// JGS keeps its own <c>imshow</c>, which takes JGraph's image values.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The names imshow takes after its positional arguments, in R2025b's order.</summary>
    private static readonly string[] ImshowNames =
        ["Colormap", "DisplayRange", "InitialMagnification", "XData", "YData", "Interpolation", "Parent", "Border", "Reduce"];

    private static JgsValue MatlabImshow(JGraphScriptGlobals host, IReadOnlyList<JgsValue> given, int line, int col)
    {
        // convertStringsToChars: a string scalar is a char row to everything below.
        List<JgsValue> args = [.. given.Select(static v => IsStringScalar(v) ? JgsValue.Str(TextOf(v)) : v)];
        int count = args.Count;
        if (count == 0)
        {
            throw new JgsRuntimeException(line, col, "images:imshow:tooFewArgs",
                "IMSHOW expected at least 1 input argument but was called instead with 0 input arguments.");
        }

        // The removed syntaxes: imshow(x, y, A) and imshow(I, N).
        if ((count is 3 or 4) && IsImshowVector(args[0]) && IsImshowVector(args[1])
            && IsImshowNumeric(args[0]) && IsImshowNumeric(args[1]) && (IsImshowNumeric(args[2]) || IsLogicalValue(args[2])))
        {
            throw new JgsRuntimeException(line, col, "images:imshow:invalidSyntax", "Invalid IMSHOW syntax.");
        }

        if (count == 2 && ElementCountOf(args[1]) == 1)
        {
            throw new JgsRuntimeException(line, col, "images:imshow:invalidSyntax", "Invalid IMSHOW syntax.");
        }

        // Where the name-value pairs start: the first char argument, or the second when the first is
        // a file name followed by whole pairs.
        List<int> chars = [.. Enumerable.Range(0, count).Where(i => IsCharArgument(args[i]))];
        int positional = count;
        int firstPair = -1;
        if (chars.Count > 0 && count > 1)
        {
            if (chars[0] == 0 && (count - 1) % 2 == 0)
            {
                if (chars.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:badsubscript",
                        "Index exceeds the number of array elements. Index must not exceed 1.");
                }

                firstPair = chars[1];
            }
            else
            {
                firstPair = chars[0];
                string start = TextOf(args[firstPair]);
                if (!ImshowNames.Any(name => name.StartsWith(start, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new JgsRuntimeException(line, col, "images:imageDisplayParsePVPairs:unrecognizedParameter",
                        $"The parameter, {start}, is not recognized by imageDisplayParsePVPairs");
                }
            }

            positional = firstPair;
        }

        if (positional > 2)
        {
            throw new JgsRuntimeException(line, col, "images:validate:tooManyInputs",
                "Too many input arguments were passed to imageDisplayParsePVPairs.");
        }

        JgsValue? cdata = null;
        string? file = null;
        JgsValue? map = null;
        bool autoRange = false;
        double[]? range = null;
        if (positional == 1)
        {
            if (IsCharArgument(args[0]))
            {
                file = TextOf(args[0]);
            }
            else
            {
                cdata = args[0];
            }
        }
        else if (positional == 2)
        {
            cdata = args[0];
            JgsValue second = args[1];
            int[] shape = ShapeOfArgument(second);
            if (ElementCountOf(second) == 0)
            {
                autoRange = true;
            }
            else if (ElementCountOf(second) == 2)
            {
                range = CheckDisplayRange(second, "imageDisplayValidateParams", line, col);
            }
            else if (shape.Length == 2 && shape[1] == 3)
            {
                map = second;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "images:imageDisplayParsePVPairs:invalidInputs", "Invalid input arguments.");
            }
        }

        // The pairs, each name a start of one of R2025b's in any case.
        JgsValue? colormapOption = null, xdata = null, ydata = null, magnification = null, reduce = null;
        string interpolation = "nearest";
        string? border = null;
        AxesModel? parent = null;
        if (firstPair >= 0)
        {
            if ((count - firstPair) % 2 != 0)
            {
                throw new JgsRuntimeException(line, col, "images:imageDisplayParsePVPairs:oddNumberArgs",
                    "Function IMAGEDISPLAYPARSEPVPAIRS expected an even number of parameter/value arguments.");
            }

            for (int i = firstPair; i < count; i += 2)
            {
                string name = ImshowParameter(args[i], i + 1, line, col);
                JgsValue value = args[i + 1];
                switch (name)
                {
                    case "DisplayRange":
                        if (ElementCountOf(value) == 0)
                        {
                            autoRange = true;
                            range = null;
                        }
                        else
                        {
                            autoRange = false;
                            range = CheckDisplayRange(value, "imageDisplayParsePVPairs", line, col);
                        }

                        break;
                    case "Colormap":
                        colormapOption = value;
                        break;
                    case "InitialMagnification":
                        magnification = value;
                        break;
                    case "XData":
                        xdata = value;
                        break;
                    case "YData":
                        ydata = value;
                        break;
                    case "Interpolation":
                        interpolation = ImshowWord(value, ["nearest", "bilinear"], "MATLAB:imageDisplayValidateParams:unrecognizedStringChoice", "Interpolation", line, col);
                        break;
                    case "Parent":
                        parent =JgsHandleRegistry.TryGet(value, out JgsHandleEntry? held) && held.Target is AxesModel { IsPolar: false } axes
                            ? axes
                            : throw new JgsRuntimeException(line, col, "images:imshow:invalidAxes", "HAX must be a valid axes handle.");
                        break;
                    case "Border":
                        border = ImshowWord(value, ["loose", "tight"], "MATLAB:imshow:unrecognizedStringChoice", "BORDER", line, col);
                        break;
                    case "Reduce":
                        reduce = value;
                        break;
                }
            }
        }

        // 'Reduce' subsamples a large TIFF named by file; this build reads the file whole.
        if (reduce is not null && IsTruthyArgument(reduce) && file is null)
        {
            throw new JgsRuntimeException(line, col, "images:imshow:badReduceSyntax", "The Reduce parameter requires a filename syntax");
        }

        if (file is not null)
        {
            string path = host.Resolve(file);
            if (!File.Exists(path))
            {
                throw new JgsRuntimeException(line, col, "images:getImageFromFile:fileDoesNotExist",
                    $"Cannot find the specified file: \"{file}\".");
            }

            try
            {
                cdata = ImageNumbers(JgsValue.Image(ImageCodec.Read(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                throw new JgsRuntimeException(line, col, $"imshow: cannot read '{file}': {ex.Message}");
            }
        }

        cdata = ImageNumbers(cdata!);
        if (cdata.Type is not (JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array) || cdata.IsCharMatrix || cdata.IsStringArray)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:imageDisplayValidateParams:invalidType",
                "Expected input number 1, I, to be one of these types:\n\ndouble, single, uint8, uint16, uint32, uint64, int8, int16, int32, int64, logical\n\n"
                + $"Instead its type was {ClassOf(cdata, JgsDialect.Matlab)}.");
        }

        if (xdata is not null)
        {
            CheckImshowCoords(xdata, "XDATA", line, col);
        }

        if (ydata is not null)
        {
            CheckImshowCoords(ydata, "YDATA", line, col);
        }

        int[] dims = ShapeOfArgument(cdata);
        bool logical = IsLogicalValue(cdata);
        string kind = map is not null ? "indexed" : dims.Length == 3 ? "truecolor" : logical ? "binary" : "intensity";

        // validateCData
        if (dims.Length > 3 || (dims.Length == 3 && dims[2] != 1 && dims[2] != 3))
        {
            throw new JgsRuntimeException(line, col, "images:imageDisplayValidateParams:unsupportedDimension",
                "Multi-plane image inputs must be RGB images of size MxNx3.");
        }

        if (logical && dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "images:imageDisplayValidateParams:expected2D",
                "If input is logical (binary), it must be two-dimensional.");
        }

        JgsNumericClass numericClass = cdata.NumericClass;
        if (dims.Length == 3 && (logical || numericClass is not (JgsNumericClass.Double or JgsNumericClass.UInt8 or JgsNumericClass.UInt16 or JgsNumericClass.Single)))
        {
            throw new JgsRuntimeException(line, col, "images:imageDisplayValidateParams:invalidRGBClass",
                "RGB images must be uint8, uint16, single, or double.");
        }

        if (kind == "indexed" && numericClass == JgsNumericClass.Int16 && !logical)
        {
            throw new JgsRuntimeException(line, col, "images:imageDisplayValidateParams:invalidIndexedImage",
                "An indexed image can be uint8, uint16, double, single, or logical.");
        }

        if (dims.Length == 3 && numericClass is JgsNumericClass.Double or JgsNumericClass.Single)
        {
            cdata = MapImshowElements(cdata, static v => System.Math.Clamp(v, 0, 1));
        }

        if (HasComplexElements(cdata))
        {
            Warn(host, "images:imageDisplayValidateParams:displayingRealPart", "Displaying real part of complex input.");
            cdata = MapImshowElements(cdata, static v => v);
        }

        // validateMap: the 'Colormap' option wins; a true-colour picture takes none.
        if (colormapOption is not null)
        {
            map = colormapOption;
        }

        if (map is not null && kind == "truecolor")
        {
            Warn(host, "images:imageDisplayValidateParams:colormapWithTruecolor", "Ignoring 'Colormap' parameter provided with a truecolor image.");
            map = null;
        }

        if (map is not null)
        {
            int[] mapShape = ShapeOfArgument(map);
            if (mapShape.Length != 2 || mapShape[1] != 3 || !IsImshowNumeric(map))
            {
                throw new JgsRuntimeException(line, col, "images:imageDisplayValidateParams:invalidColormap",
                    "Colormap must be an m-by-3 numeric matrix.");
            }

            if (ToDoubles("imshow", map, line, col).Any(static v => !(v >= 0 && v <= 1)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:hg:shaped_arrays:ColorMapType",
                    "Error setting property 'Colormap' of class 'Axes':\nValue must be either:\nan Mx3 array of type single or double in the range [0 1]\nan Mx3 array of type uint8.");
            }
        }

        bool scaled = kind is "intensity" or "binary";
        if (autoRange)
        {
            range = kind == "truecolor" ? ImshowClassRange(cdata) : DataRangeOf(cdata);
        }

        if (scaled)
        {
            map ??= GrayMap(256);
            if (range is null || range[0] == range[1])
            {
                range = ImshowClassRange(cdata);
            }
        }

        if (range is not null && !(range[1] > range[0]))
        {
            throw new JgsRuntimeException(line, col, "images:checkDisplayRange:badDisplayRangeValues", "HIGH must be greater than LOW.");
        }

        bool fit = false;
        if (magnification is not null)
        {
            if (IsCharArgument(magnification))
            {
                fit = ImshowWord(magnification, ["fit"], "MATLAB:imshow:unrecognizedStringChoice", "INITIAL_MAG", line, col) == "fit";
            }
            else if (!(ElementCountOf(magnification) == 1 && ToDoubles("imshow", magnification, line, col) is [var mag] && mag > 0 && double.IsFinite(mag)))
            {
                throw new JgsRuntimeException(line, col, "imshow: 'InitialMagnification' is a positive number or 'fit'.");
            }
        }

        bool tight = border == "tight";

        // The picture, made by the high-level image call into the axes asked for or the current one.
        var imageArgs = new List<JgsValue>();
        if (xdata is not null || ydata is not null)
        {
            imageArgs.Add(xdata ?? Row1(1, dims[1]));
            imageArgs.Add(ydata ?? Row1(1, dims[0]));
        }

        imageArgs.Add(cdata);
        imageArgs.AddRange([JgsValue.Str("CDataMapping"), JgsValue.Str(scaled ? "scaled" : "direct"), JgsValue.Str("Interpolation"), JgsValue.Str(interpolation)]);
        JgsValue handle = OnAxes(parent, () => DrawImage("image", imageArgs, scaled: false, line, col));
        JgsHandleEntry imageEntry = JgsHandleRegistry.Require(handle, line, col);
        JgsGraphicsProperties.Set(imageEntry, "BusyAction", JgsValue.Str("cancel"), line, col);
        JgsGraphicsProperties.Set(imageEntry, "Interruptible", JgsValue.Str("off"), line, col);

        AxesModel target = imageEntry.Target.Parent as AxesModel ?? parent ?? JG.Gca();
        JgsHandleEntry axesEntry = JgsHandleRegistry.EntryFor(target);
        JgsGraphicsProperties.Set(axesEntry, "YDir", JgsValue.Str("reverse"), line, col);
        JgsGraphicsProperties.Set(axesEntry, "TickDir", JgsValue.Str("out"), line, col);
        JgsGraphicsProperties.Set(axesEntry, "XGrid", JgsValue.Str("off"), line, col);
        JgsGraphicsProperties.Set(axesEntry, "YGrid", JgsValue.Str("off"), line, col);
        JgsGraphicsProperties.Set(axesEntry, "DataAspectRatio", JgsGraphicsProperties.Row(1, 1, 1), line, col);
        JgsGraphicsProperties.Set(axesEntry, "PlotBoxAspectRatioMode", JgsValue.Str("auto"), line, col);
        JgsGraphicsProperties.Set(axesEntry, "Visible", JgsValue.Str("off"), line, col);
        if (map is not null)
        {
            JgsGraphicsProperties.Set(axesEntry, "Colormap", map, line, col);
        }

        if (range is not null)
        {
            JgsGraphicsProperties.Set(axesEntry, "CLim", JgsGraphicsProperties.Row(range[0], range[1]), line, col);
        }

        // Its title and labels are set visible, so they are drawn on the invisible axes (oi142_errors P1).
        target.PrimaryXAxis.LabelVisible = true;
        target.ActiveYAxis.LabelVisible = true;

        // A lone picture at the default place: R2025b sizes the figure to it (not here) and makes the
        // next plot start the figure afresh; with 'fit' and 'tight' the picture fills the figure.
        if (target.Parent is FigureModel figure && SoleDefaultImageAxes(figure, target) && (!fit || tight))
        {
            if (fit && tight)
            {
                JgsGraphicsProperties.Set(axesEntry, "Units", JgsValue.Str("normalized"), line, col);
                JgsGraphicsProperties.Set(axesEntry, "Position", JgsGraphicsProperties.Row(0, 0, 1, 1), line, col);
            }

            figure.NextPlot = FigureNextPlot.ReplaceChildren;
        }

        return handle;
    }

    /// <summary>
    /// <c>images.internal.isSingleImageDefaultPos</c>: one picture in the axes, the axes alone in the
    /// figure (panels searched too) with no visible classic control beside it, in normalized units at
    /// the place a new axes takes.
    /// </summary>
    private static bool SoleDefaultImageAxes(FigureModel figure, AxesModel axes) =>
        axes.Plots.Count(static p => p is ImagePlot or RgbImagePlot) <= 1
        && figure.Axes.Count == 1
        && !figure.Components.Any(static c => c is UiControlModel { Visible: true })
        && !axes.IsUiAxes
        && axes.Units == UiUnits.Normalized
        && axes.LayoutTile is null
        && axes.InnerTarget is null
        && axes.NormalizedBounds.X == 0 && axes.NormalizedBounds.Y == 0
        && axes.NormalizedBounds.Width == 1 && axes.NormalizedBounds.Height == 1;

    /// <summary>The display range a class gives (<c>getrangefromclass</c>): [0 1] for floating and logical, the integer's span.</summary>
    private static double[] ImshowClassRange(JgsValue cdata)
    {
        if (IsLogicalValue(cdata) || !cdata.NumericClass.IsInteger())
        {
            return [0, 1];
        }

        (double min, double max) = JgsNumericClasses.Range(cdata.NumericClass);
        return [min, max];
    }

    /// <summary>The data's own [min max], NaNs passed over, as <c>imshow(I, [])</c> takes it.</summary>
    private static double[] DataRangeOf(JgsValue cdata)
    {
        double[] values = cdata.Type == JgsType.Bool ? [cdata.AsNumber] : ToDoubles("imshow", cdata, 0, 0);
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (double v in values)
        {
            if (double.IsNaN(v))
            {
                continue;
            }

            low = System.Math.Min(low, v);
            high = System.Math.Max(high, v);
        }

        return double.IsInfinity(low) ? [0, 1] : [low, high];
    }

    /// <summary><c>checkDisplayRange</c>: a real, non-NaN vector of two, low below high.</summary>
    private static double[] CheckDisplayRange(JgsValue value, string parser, int line, int col)
    {
        double[] pair = ToDoubles("imshow", value, line, col);
        if (pair.Any(double.IsNaN))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{parser}:expectedNonNaN", "Expected input number 2, [LOW HIGH], to be non-NaN.");
        }

        if (pair.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "images:checkDisplayRange:not2ElementVector", "[LOW HIGH] must be a 2-element vector.");
        }

        if (!(pair[1] > pair[0]))
        {
            throw new JgsRuntimeException(line, col, "images:checkDisplayRange:badDisplayRangeValues", "HIGH must be greater than LOW.");
        }

        return pair;
    }

    /// <summary><c>validatestring</c> over imshow's names: a start of one, in any case, or the shortest that starts every match.</summary>
    private static string ImshowParameter(JgsValue value, int position, int line, int col)
    {
        string list = string.Join(", ", ImshowNames.Select(static n => $"'{n}'"));
        string given = IsCharArgument(value) ? TextOf(value) : string.Empty;
        string[] matches = given.Length == 0 ? [] : [.. ImshowNames.Where(n => n.StartsWith(given, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (matches.Length > 1)
        {
            string shortest = matches.MinBy(static m => m.Length)!;
            if (matches.All(m => m.StartsWith(shortest, StringComparison.OrdinalIgnoreCase)))
            {
                return shortest;
            }

            throw new JgsRuntimeException(line, col, "MATLAB:imageDisplayParsePVPairs:ambiguousStringChoice",
                $"Expected input number {position}, PARAM, to match one of these values:\n\n{list}\n\nThe input, '{given}', matched more than one valid value.");
        }

        throw new JgsRuntimeException(line, col, "MATLAB:imageDisplayParsePVPairs:unrecognizedStringChoice",
            $"Expected input number {position}, PARAM, to match one of these values:\n\n{list}\n\nThe input, '{given}', did not match any of the valid values.");
    }

    /// <summary>One of a few words, a start of it in any case, or <c>validatestring</c>'s refusal under <paramref name="what"/>.</summary>
    private static string ImshowWord(JgsValue value, string[] words, string identifier, string what, int line, int col)
    {
        string given = IsCharArgument(value) ? TextOf(value) : string.Empty;
        string[] matches = given.Length == 0 ? [] : [.. words.Where(w => w.StartsWith(given, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length == 1)
        {
            return matches[0];
        }

        throw new JgsRuntimeException(line, col, identifier,
            $"Expected {what} to match one of these values:\n\n{string.Join(", ", words.Select(static w => $"'{w}'"))}\n\n"
            + $"The input, '{given}', did not match any of the valid values.");
    }

    /// <summary><c>checkCoords</c>: a finite real vector of at least two.</summary>
    private static void CheckImshowCoords(JgsValue value, string what, int line, int col)
    {
        double[] coords = ToDoubles("imshow", value, line, col);
        if (coords.Length < 2)
        {
            throw new JgsRuntimeException(line, col, "images:imageDisplayValidateParams:need2Coords", $"{what} must be a 2-element vector.");
        }

        if (coords.Any(static c => !double.IsFinite(c)))
        {
            throw new JgsRuntimeException(line, col, $"imshow: {what} must be finite.");
        }
    }

    private static bool IsCharArgument(JgsValue value) => value.Type == JgsType.String || value.IsCharMatrix;

    private static bool IsTruthyArgument(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool ? value.IsTruthy : ToDoubles("imshow", value, 0, 0).Any(static v => v != 0);

    private static bool IsImshowNumeric(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Complex or JgsType.Array && !value.IsCharMatrix && !value.IsStringArray && !IsLogicalValue(value);

    private static bool IsImshowVector(JgsValue value)
    {
        int[] shape = ShapeOfArgument(value);
        return shape.Length == 2 && (shape[0] == 1 || shape[1] == 1) && shape[0] * shape[1] >= 1;
    }

    private static int ElementCountOf(JgsValue value) => ShapeOfArgument(value).Aggregate(1, static (a, b) => a * b);

    /// <summary>A value's size as MATLAB's <c>size</c> gives it: a lone number or logical is 1-by-1, a char row 1-by-n.</summary>
    private static int[] ShapeOfArgument(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool or JgsType.Complex => [1, 1],
        JgsType.String => value.AsString.Length == 0 ? [0, 0] : [1, value.AsString.Length],
        JgsType.Cell => value.AsCell.Length == 0 ? [0, 0] : JgsMatrix.DimsOf(value),
        _ => JgsMatrix.DimsOf(value),
    };

    /// <summary>Each element's real part through <paramref name="map"/>, the shape and class kept.</summary>
    private static JgsValue MapImshowElements(JgsValue value, Func<double, double> map)
    {
        if (value.Type is JgsType.Number or JgsType.Complex)
        {
            return JgsValue.Number(map(value.Type == JgsType.Complex ? value.AsComplex.Real : value.AsNumber));
        }

        var flat = new double[value.ArrayLength];
        for (int i = 0; i < flat.Length; i++)
        {
            JgsValue element = value.ElementAt(i);
            flat[i] = map(element.Type == JgsType.Complex ? element.AsComplex.Real : element.AsNumber);
        }

        JgsValue result = JgsMatrix.FromColumnMajorDims(flat, JgsMatrix.DimsOf(value));
        result.SetNumericClass(value.NumericClass);
        return result;
    }

    /// <summary>MATLAB's <c>gray(n)</c>: n levels from black to white, the same in each column.</summary>
    private static JgsValue GrayMap(int n)
    {
        var columns = new double[n * 3];
        for (int i = 0; i < n; i++)
        {
            double level = n == 1 ? 0 : (double)i / (n - 1);
            columns[i] = level;
            columns[i + n] = level;
            columns[i + (2 * n)] = level;
        }

        return JgsMatrix.FromColumnMajor(columns, n, 3);
    }

    private static JgsValue Row1(double first, double last) => JgsGraphicsProperties.Row(first, last);
}
