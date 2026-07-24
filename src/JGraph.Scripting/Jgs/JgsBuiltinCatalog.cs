using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>One parameter of a JGS builtin. <see cref="Optional"/> parameters render with a trailing
/// <c>?</c> in the signature and are omitted from completion placeholders.</summary>
public sealed record JgsBuiltinParameter(string Name, bool Optional = false)
{
    /// <summary>The parameter as it appears in the signature (<c>name</c> or <c>name?</c>).</summary>
    public string Display => Optional ? Name + "?" : Name;
}

/// <summary>
/// Editor-facing metadata for one JGS builtin: its parameters and a one-line summary. The
/// <see cref="Signature"/> is derived so it can never disagree with the parameter list. An
/// <see cref="IsConstant"/> entry is a value binding (like <c>pi</c>), rendered without parentheses
/// and excluded from signature help.
/// </summary>
public sealed record JgsBuiltinInfo(string Name, IReadOnlyList<JgsBuiltinParameter> Parameters, string Summary, bool IsConstant = false)
{
    /// <summary>The rendered call signature, e.g. <c>plot(x, y, spec?)</c> — or the bare name for a constant.</summary>
    public string Signature
    {
        get
        {
            if (IsConstant)
            {
                return Name;
            }

            var sb = new StringBuilder(Name).Append('(');
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(Parameters[i].Display);
            }

            return sb.Append(')').ToString();
        }
    }
}

/// <summary>
/// The single registry describing every JGS builtin for editors: names, signatures, parameter lists, and
/// one-line summaries. Syntax highlighting, completion, and signature help all read from here, and a test
/// pins <see cref="All"/> to <see cref="JgsScriptEngine.BuiltinNames"/> (the live registration), so the
/// catalog cannot drift from the language. <see cref="Keywords"/> comes straight from the lexer's keyword
/// table for the same reason.
/// </summary>
public static class JgsBuiltinCatalog
{
    private static readonly IReadOnlyDictionary<string, JgsBuiltinInfo> ByName = Build();

    /// <summary>Every builtin, sorted by name.</summary>
    public static IReadOnlyList<JgsBuiltinInfo> All { get; } =
        ByName.Values.OrderBy(static i => i.Name, StringComparer.Ordinal).ToArray();

    /// <summary>The JGS language keywords, straight from the lexer's keyword table.</summary>
    public static IReadOnlyList<string> Keywords { get; } =
        Lexer.KeywordNames.OrderBy(static k => k, StringComparer.Ordinal).ToArray();

    /// <summary>Looks up a builtin by name; null when <paramref name="name"/> is not a builtin.</summary>
    public static JgsBuiltinInfo? Find(string name) =>
        ByName.TryGetValue(name, out JgsBuiltinInfo? info) ? info : null;

    private static IReadOnlyDictionary<string, JgsBuiltinInfo> Build()
    {
        var infos = new Dictionary<string, JgsBuiltinInfo>(StringComparer.Ordinal);

        void Add(string name, string summary, params JgsBuiltinParameter[] parameters) =>
            infos.Add(name, new JgsBuiltinInfo(name, parameters, summary));

        JgsBuiltinParameter P(string parameterName) => new(parameterName);
        JgsBuiltinParameter Opt(string parameterName) => new(parameterName, Optional: true);

        void Constant(string name, string summary) =>
            infos.Add(name, new JgsBuiltinInfo(name, System.Array.Empty<JgsBuiltinParameter>(), summary, IsConstant: true));

        // --- Constants -------------------------------------------------------------------------
        Constant("pi", "The circle constant π ≈ 3.14159.");
        Constant("e", "Euler's number ≈ 2.71828.");
        Constant("inf", "Positive infinity.");
        Constant("nan", "Not-a-number (an undefined numeric result).");

        // --- Element-wise math (number or numeric array in, same shape out) -------------------
        Add("sin", "Sine of x (radians), element-wise over arrays.", P("x"));
        Add("cos", "Cosine of x (radians), element-wise over arrays.", P("x"));
        Add("tan", "Tangent of x (radians), element-wise over arrays.", P("x"));
        Add("asin", "Inverse sine of x, in radians, element-wise over arrays.", P("x"));
        Add("acos", "Inverse cosine of x, in radians, element-wise over arrays.", P("x"));
        Add("atan", "Inverse tangent of x, in radians, element-wise over arrays.", P("x"));
        Add("atan2", "Angle of the point (x, y) in radians, in the correct quadrant.", P("y"), P("x"));
        Add("exp", "e raised to x, element-wise over arrays.", P("x"));
        Add("log", "Natural logarithm of x, element-wise over arrays.", P("x"));
        Add("log10", "Base-10 logarithm of x, element-wise over arrays.", P("x"));
        Add("sqrt", "Square root of x, element-wise over arrays.", P("x"));
        Add("abs", "Absolute value of x (magnitude for complex values), element-wise over arrays.", P("x"));
        Add("real", "Real part of x (x itself for real numbers), element-wise over arrays.", P("x"));
        Add("imag", "Imaginary part of x (0 for real numbers), element-wise over arrays.", P("x"));
        Add("conj", "Complex conjugate of x (x itself for real numbers), element-wise over arrays.", P("x"));
        Add("angle", "Phase angle of x in radians, element-wise over arrays.", P("x"));
        Add("floor", "Largest whole number not above x, element-wise over arrays.", P("x"));
        Add("ceil", "Smallest whole number not below x, element-wise over arrays.", P("x"));
        Add("round", "x rounded to the nearest whole number (halves away from zero), element-wise.", P("x"));
        Add("sign", "-1, 0, or 1 by the sign of x, element-wise over arrays.", P("x"));
        Add("pow", "x raised to exponent, element-wise over arrays.", P("x"), P("exponent"));

        // --- Array construction ----------------------------------------------------------------
        Add("linspace", "count evenly spaced values from start to stop, inclusive.", P("start"), P("stop"), P("count"));
        Add("range", "Values from start (inclusive) to stop (exclusive) in steps of step (default 1).", P("start"), P("stop"), Opt("step"));
        Add("zeros", "An array of count zeros, a rows-by-cols matrix, or the shape of a size vector (zeros(size(t))).", P("count"), Opt("cols"));
        Add("ones", "An array of count ones, a rows-by-cols matrix, or the shape of a size vector.", P("count"), Opt("cols"));
        Add("rand", "An array of count uniform random values in [0, 1).", P("count"));

        // --- DSP and audio ----------------------------------------------------------------------
        Add("fft", "Discrete Fourier transform of a (real or complex) signal; optional length pads or truncates.", P("x"), Opt("n"));
        Add("ifft", "Inverse discrete Fourier transform; optional length pads or truncates.", P("x"), Opt("n"));
        Add("fftshift", "Rotates a spectrum so DC sits at the center.", P("x"));
        Add("ifftshift", "Undoes fftshift, restoring DC-first order.", P("x"));
        Add("filter", "Applies the digital filter b/a to signal x (zero initial state).", P("b"), P("a"), P("x"));
        Add("freqz", "Frequency response of b/a: [H, f] with complex H at count points (fs defaults to 2 = normalized).", P("b"), P("a"), Opt("count"), Opt("fs"));
        Add("butter", "Butterworth design: [b, a] for order n and normalized cutoff(s) Wn; type \"low\"/\"high\"/\"bandpass\"/\"stop\".", P("n"), P("Wn"), Opt("type"));
        Add("firpm", "Parks-McClellan equiripple FIR: order n, normalized band edges f, band amplitudes a.", P("n"), P("f"), P("a"));
        Add("audioread", "Reads a .wav file: [samples, fs] with samples normalized to [-1, 1] (stereo averaged to mono).", P("path"));
        Add("sound", "Plays samples through the host's audio output without blocking (fs defaults to 8192).", P("y"), Opt("fs"));
        Add("pause", "Waits the given number of seconds (interruptible by Stop).", P("seconds"));
        Add("exit", "Ends the script and closes the application, with an optional process exit code.", Opt("code"));
        Add("quit", "An alias for exit.", Opt("code"));

        // --- Time & date ------------------------------------------------------------------------
        Add("tic", "Starts a stopwatch and returns a handle; pass it to toc to time a specific interval.");
        Add("toc", "Elapsed seconds since the last tic, or since the tic that returned handle.", Opt("handle"));
        Add("clock", "The current local time as a [year, month, day, hour, minute, seconds] vector.");
        Add("now", "The current local date and time as a serial date number (days since year 0).");
        Add("datenum", "Serial date number from year, month, day (optionally hour, minute, second), or a 3-/6-element vector.", P("year"), P("month"), P("day"), Opt("hour"), Opt("minute"), Opt("second"));
        Add("datestr", "Formats a serial date number (default: now) as text; format uses .NET date tokens.", Opt("serial"), Opt("format"));
        Add("datetime", "The current local date and time as a 'dd-MMM-yyyy HH:mm:ss' string.");
        Add("date", "The current local date as a 'dd-MMM-yyyy' string.");
        Add("time", "The current time as Unix epoch seconds (UTC), including a fractional part.");

        Add("mod", "Modulo x - floor(x/m)*m, element-wise over arrays (result takes m's sign).", P("x"), P("m"));
        Add("size", "The [rows, cols] of a matrix ([rows, cols, 3] for an RGB image); size(value, dim) returns one dimension.", P("value"), Opt("dim"));
        Add("isempty", "True when a value has no elements: null, an empty array or string, or a table with no rows.", P("value"));
        Add("disp", "Writes a value to the console (no name prefix, unlike echo).", P("value"));

        // --- RF networks and transmission lines -------------------------------------------------
        Add("sparameters", "Reads a Touchstone (.sNp) file into an S-parameter network table.", P("path"));
        Add("rffreq", "The frequency points (Hz) of a network table.", P("net"));
        Add("rfparam", "The (i, j) parameter of a network table across frequency, as a complex array (port numbers, so 1-based: s11 is rfparam(net, 1, 1)).", P("net"), P("i"), P("j"));
        Add("s2z", "Converts an S-parameter network table to impedance (Z) parameters (1- or 2-port).", P("net"));
        Add("s2y", "Converts an S-parameter network table to admittance (Y) parameters (1- or 2-port).", P("net"));
        Add("s2abcd", "Converts a 2-port S-parameter network table to chain (ABCD) parameters.", P("net"));
        Add("z2s", "Converts a Z-parameter network table to S parameters (1- or 2-port).", P("net"));
        Add("y2s", "Converts a Y-parameter network table to S parameters (1- or 2-port).", P("net"));
        Add("abcd2s", "Converts a 2-port ABCD network table to S parameters.", P("net"));
        Add("cascadesparams", "Cascades two 2-port networks (port 2 of a into port 1 of b).", P("a"), P("b"));
        Add("gammain", "Input reflection coefficient Γin over frequency, given a load impedance (default matched).", P("net"), Opt("zl"));
        Add("gammaout", "Output reflection coefficient Γout over frequency, given a source impedance (default matched).", P("net"), Opt("zs"));
        Add("vswr", "Voltage standing-wave ratio (1+|Γ|)/(1−|Γ|) from a reflection coefficient, element-wise.", P("gamma"));
        Add("db", "Decibel magnitude 20·log10|x|, element-wise (works on real or complex values).", P("x"));
        Add("rfplot", "Plots dB magnitude vs frequency for parameter (i, j), or all pairs when omitted.", P("net"), Opt("i"), Opt("j"));
        Add("smithplot", "Plots a reflection-coefficient locus on a Smith chart (a network's (i, j) or a complex array).", P("net"), Opt("i"), Opt("j"));
        Add("microstrip", "Microstrip analysis: [Z0, eeff] from trace width, substrate height, and εr.", P("w"), P("h"), P("er"));
        Add("microstripw", "Microstrip synthesis: trace width for a target Z0, given substrate height and εr.", P("z0"), P("h"), P("er"));
        Add("stripline", "Stripline analysis: Z0 from trace width, ground-plane spacing, and εr.", P("w"), P("b"), P("er"));
        Add("striplinew", "Stripline synthesis: trace width for a target Z0, given plate spacing and εr.", P("z0"), P("b"), P("er"));
        Add("wavelength", "Guided wavelength (m) at frequency f (Hz) for an effective permittivity, element-wise over f.", P("f"), P("eeff"));

        // --- Image processing -------------------------------------------------------------------
        Add("imread", "Reads an image file (PNG/JPEG/BMP) into an image value (samples in [0,1]).", P("path"));
        Add("imwrite", "Writes an image to a file; the extension (.png/.jpg/.bmp) selects the format.", P("image"), P("path"), Opt("quality"));
        Add("imshow", "Displays an image (grayscale or RGB) with equal aspect and no axes decoration.", P("image"));
        Add("rgb2gray", "Converts an RGB image to grayscale (Rec.601 luma).", P("image"));
        Add("im2gray", "Returns a grayscale image: RGB is converted (Rec.601), grayscale is passed through.", P("image"));
        Add("mat2im", "Wraps a matrix as a grayscale image, clamping values to [0, 1].", P("matrix"));
        Add("mat2gray", "Scales a matrix to a grayscale image with min→0 and max→1.", P("matrix"));
        Add("im2mat", "Copies an image channel (default 1) to a nested-array matrix.", P("image"), Opt("channel"));
        Add("imadjust", "Maps intensities [lowIn,highIn]→[lowOut,highOut] with gamma; defaults stretch the 1–99% range.", P("image"), Opt("inRange"), Opt("outRange"), Opt("gamma"));
        Add("imhist", "Histogram bin counts of a grayscale image (default 256 bins) as an array.", P("image"), Opt("bins"));
        Add("histeq", "Histogram-equalizes a grayscale image (default 64 levels).", P("image"), Opt("bins"));
        Add("graythresh", "Otsu's global threshold level in [0, 1] for a grayscale image.", P("image"));
        Add("imbinarize", "Thresholds an image to binary; the default level is Otsu's.", P("image"), Opt("level"));
        Add("imadd", "Adds two images, or an image and a scalar, clamped to [0, 1].", P("a"), P("b"));
        Add("imsubtract", "Subtracts an image or scalar from an image, clamped to [0, 1].", P("a"), P("b"));
        Add("immultiply", "Multiplies two images sample by sample (or an image by a scalar), clamped to [0, 1] — masking.", P("a"), P("b"));
        Add("imcomplement", "Inverts image intensities (1 - v).", P("image"));
        Add("imnoise", "Adds noise: 'gaussian' (variance) or 'salt & pepper' (density).", P("image"), Opt("type"), Opt("amount"));
        Add("imresize", "Resizes an image by a scale or to a [height, width]; 'nearest' or 'bilinear'.", P("image"), P("scaleOrSize"), Opt("method"));
        Add("imrotate", "Rotates an image counter-clockwise by degrees; options 'nearest'/'bilinear' and 'crop'/'loose'.", P("image"), P("degrees"), Opt("method"), Opt("bbox"));
        Add("imcrop", "Crops the rectangle [x, y, width, height] (0-based origin) from an image.", P("image"), P("rect"));
        Add("imfilter", "Correlates an image with a kernel; boundary 'zero'/'replicate'/'symmetric'.", P("image"), P("kernel"), Opt("boundary"));
        Add("conv2", "2-D convolution of two matrices; shape 'full' (default), 'same', or 'valid'.", P("a"), P("b"), Opt("shape"));
        Add("medfilt2", "Median filter over an [m, n] window (default 3×3).", P("image"), Opt("window"));
        Add("fspecial", "Builds a filter kernel: average, gaussian, sobel, prewitt, laplacian, disk, or log.", P("type"), Opt("p1"), Opt("p2"));
        Add("edge", "Detects edges (binary image): 'sobel' (default), 'prewitt', 'roberts', 'canny', or 'log'.", P("image"), Opt("method"), Opt("threshold"));
        Add("imgradient", "Gradient magnitude and direction (degrees) of an image: [mag, dir]; method 'sobel' (default), 'prewitt', or 'roberts'.", P("image"), Opt("method"));
        Add("imgradientxy", "Horizontal and vertical gradient components of an image: [Gx, Gy]; method 'sobel' (default), 'prewitt', or 'roberts'.", P("image"), Opt("method"));
        Add("strel", "Builds a structuring element matrix: 'square' (side) or 'disk' (radius).", P("shape"), Opt("size"));
        Add("imerode", "Morphological erosion (local minimum) over a structuring element (default 3×3 square).", P("image"), Opt("element"));
        Add("imdilate", "Morphological dilation (local maximum) over a structuring element (default 3×3 square).", P("image"), Opt("element"));
        Add("imopen", "Morphological opening (erode then dilate).", P("image"), Opt("element"));
        Add("imclose", "Morphological closing (dilate then erode).", P("image"), Opt("element"));
        Add("hough", "Hough line transform of a binary image: [accumulator, theta, rho].", P("image"));
        Add("houghpeaks", "The strongest peaks of a Hough accumulator, as 0-based [rhoIndex, thetaIndex] rows; pass base 1 for MATLAB numbering.", P("accumulator"), Opt("count"), Opt("threshold"), Opt("base"));
        Add("houghlines", "Line segments for the given Hough peaks, as a table of endpoints with Theta and Rho.", P("image"), P("theta"), P("rho"), P("peaks"), Opt("fillGap"), Opt("minLength"));
        Add("imfill","Fills holes in a binary image — background not reachable from the border becomes foreground.", P("image"), Opt("mode"));
        Add("bwareaopen", "Removes connected components smaller than minArea pixels from a binary image; connectivity 4 or 8 (default 8).", P("image"), P("minArea"), Opt("connectivity"));
        Add("bwlabel","Labels connected components of a binary image: [labels, count]; connectivity 4 or 8 (default 8).", P("image"), Opt("connectivity"));
        Add("regionprops", "Per-region Area/Centroid/BoundingBox of a label or binary image, as a table (0-based pixel coordinates); an intensity image adds MeanIntensity and WeightedCentroid.", P("labels"), Opt("intensity"));
        Add("imcentroid", "The intensity-weighted centre [x, y] of a whole image (0-based pixel coordinates), optionally weighing only what a mask keeps.", P("image"), Opt("mask"));

        // --- Reductions and inspection ----------------------------------------------------------
        Add("length", "The number of elements in an array, or characters in a string.", P("value"));
        Add("sum", "The sum of a numeric array, or of every sample in an image.", P("array"));
        Add("mean", "The arithmetic mean of a non-empty numeric array, or of every sample in an image.", P("array"));
        Add("min", "The smallest value: min(array), min(image), or min(a, b, ...).", P("values"));
        Add("max", "The largest value: max(array), max(image), or max(a, b, ...).", P("values"));
        Add("numel", "The number of elements in an array, or characters in a string (alias of length).", P("value"));

        // --- Statistics -------------------------------------------------------------------------
        Add("std", "Sample standard deviation (n-1 denominator) of at least 2 values.", P("array"));
        Add("variance", "Sample variance (n-1 denominator) of at least 2 values.", P("array"));
        Add("median", "Median of a non-empty numeric array.", P("array"));
        Add("mode", "Most frequent value of a non-empty numeric array (smallest wins ties).", P("array"));
        Add("percentile", "The p-th percentile (0-100) of a non-empty array, by linear interpolation.", P("array"), P("p"));
        Add("cumsum", "Running sums of a numeric array.", P("array"));
        Add("cumprod", "Running products of a numeric array.", P("array"));
        Add("diff", "Adjacent differences of a numeric array (length n-1).", P("array"));

        // --- Array operations ---------------------------------------------------------------------
        Add("sort", "A sorted copy of a numeric or string array; order \"asc\" (default) or \"desc\".", P("array"), Opt("order"));
        Add("unique", "The sorted distinct values of a numeric or string array.", P("array"));
        Add("find", "0-based indices of the truthy elements — volt(find(temp > 85)) gathers the matches; pass base 1 for MATLAB numbering.", P("mask"), Opt("base"));
        Add("any", "Whether at least one element is truthy.", P("array"));
        Add("all", "Whether every element is truthy.", P("array"));
        Add("concat", "One array from arrays and scalars, in order: concat(a, b), concat(a, 5).", P("first"), P("second"));
        Add("slice", "Elements [start, stop) by 0-based index; stop defaults to the array length.", P("array"), P("start"), Opt("stop"));
        Add("indexof", "0-based index of the first element equal to value, or -1.", P("array"), P("value"));
        Add("reverse", "A reversed copy of an array.", P("array"));
        Add("isnan", "Whether x is NaN, element-wise over arrays.", P("x"));
        Add("isequal", "Deep equality of two values (arrays element-by-element), as one bool.", P("a"), P("b"));
        Add("and", "Element-wise logical AND, broadcasting a scalar across an array.", P("a"), P("b"));
        Add("or", "Element-wise logical OR, broadcasting a scalar across an array.", P("a"), P("b"));
        Add("not", "Element-wise logical NOT over an array, or of one value.", P("a"));

        // --- Strings ------------------------------------------------------------------------------
        Add("sprintf", "Formats values C-style: %d %i %f %e %g %s %x %% with width/precision (%.2f, %8d).", P("format"), P("values"));
        Add("fprintf", "Writes a sprintf-formatted string to the console with no added newline (use \\n in the format).", P("format"), P("values"));
        Add("str", "Any value formatted as a string.", P("value"));
        Add("num", "A string parsed as a number; NaN when it does not parse (filter with isnan).", P("text"));
        Add("upper", "The string in upper case.", P("text"));
        Add("lower", "The string in lower case.", P("text"));
        Add("trim", "The string without leading/trailing whitespace.", P("text"));
        Add("split", "The pieces of text between occurrences of separator, as a string array.", P("text"), P("separator"));
        Add("join", "The array's elements joined into one string with separator between them.", P("array"), P("separator"));
        Add("startswith", "Whether text starts with prefix.", P("text"), P("prefix"));
        Add("endswith", "Whether text ends with suffix.", P("text"), P("suffix"));
        Add("replace", "text with every occurrence of old replaced by new.", P("text"), P("old"), P("new"));
        Add("contains", "Whether a string contains a substring, or an array contains a value.", P("value"), P("search"));

        // --- Table access -----------------------------------------------------------------------
        Add("readcsv", "Reads a delimited text file into a table, skipping skiprows leading junk lines first. Bare names resolve against the script, then the workspace root.", P("path"), Opt("skiprows"));
        Add("readxlsx", "Reads the first sheet of an .xlsx workbook into a table, skipping skiprows leading rows first.", P("path"), Opt("skiprows"));
        Add("readtable", "Reads a .csv/.tsv/.txt/.xlsx file into a table, picking the reader by extension.", P("path"), Opt("skiprows"));
        Add("column", "A table column as a numeric array.", P("table"), P("name"));
        Add("colnames", "The table's column names as a string array.", P("table"));
        Add("rowcount", "The number of data rows in the table.", P("table"));
        Add("textcolumn", "A table column as a string array (missing cells become \"\") — for serial numbers and IDs.", P("table"), P("name"));

        // --- Composition and output ---------------------------------------------------------------
        Add("run", "Runs another JGS script into the current global scope (an include).", P("path"));
        Add("print", "Writes the values to the console, space-separated.", P("values"));

        // --- Figure setup and plotting -------------------------------------------------------------
        Add("figure", "Starts a new figure (or selects figure n) and returns its handle (a figure number, so it starts at 1).", Opt("n"));
        Add("subplot", "Selects cell index of a rows-by-cols axes grid (a grid cell number, so 1-based, row-major).", P("rows"), P("cols"), P("index"));
        Add("plot", "Line plot: plot(y), plot(x, y, spec?), or plot(table, xColumn, yColumn, spec?).", P("x"), P("y"), Opt("spec"));
        Add("scatter", "Scatter plot: scatter(x, y) or scatter(table, xColumn, yColumn).", P("x"), P("y"));
        Add("bar", "Bar chart: bar(x, y) or bar(table, xColumn, yColumn).", P("x"), P("y"));
        Add("stem", "Stem plot: stem(y) or stem(x, y).", P("x"), Opt("y"));
        Add("histogram", "Histogram with bins bars (default 10): histogram(values, bins?) or histogram(table, column, bins?).", P("values"), Opt("bins"));
        Add("errorbar", "Line plot with symmetric error bars: errorbar(x, y, error) or errorbar(table, xColumn, yColumn, errorColumn).", P("x"), P("y"), P("error"));
        Add("semilogx", "Line plot with a logarithmic x axis.", P("x"), P("y"), Opt("spec"));
        Add("semilogy", "Line plot with a logarithmic y axis.", P("x"), P("y"), Opt("spec"));
        Add("loglog", "Line plot with logarithmic x and y axes.", P("x"), P("y"), Opt("spec"));
        Add("title", "Sets the current axes title.", P("text"));
        Add("xlabel", "Sets the x-axis label.", P("text"));
        Add("ylabel", "Sets the y-axis label.", P("text"));
        Add("xlim", "Sets the x-axis range.", P("min"), P("max"));
        Add("ylim", "Sets the y-axis range.", P("min"), P("max"));
        Add("grid", "Turns grid lines on (default) or off.", Opt("on"));
        Add("hold", "Keeps existing series when plotting more (default on).", Opt("on"));
        Add("legend", "Sets the legend to the given series names (one string per series).", P("names"));
        Add("show", "Shows the current figure (or figure fig) in its own window.", Opt("fig"));

        // --- 3D surfaces, contours, and images -------------------------------------------------
        Add("meshgrid", "Returns [X, Y] coordinate matrices over the x and y vectors: let [X, Y] = meshgrid(x, y).", P("x"), P("y"));
        Add("surf", "Colormap-filled 3D surface of matrix z: surf(z) or surf(x, y, z). Drag to rotate.", P("x"), P("y"), P("z"));
        Add("mesh", "Wireframe 3D surface of matrix z: mesh(z) or mesh(x, y, z).", P("x"), P("y"), P("z"));
        Add("meshc", "Wireframe 3D surface with contour lines projected on the floor.", P("x"), P("y"), P("z"));
        Add("contour", "Iso-line contours of matrix z at auto (or explicit) levels.", P("x"), P("y"), P("z"), Opt("levels"));
        Add("contourf", "Filled contour bands of matrix z at auto (or explicit) levels.", P("x"), P("y"), P("z"), Opt("levels"));
        Add("imagesc", "Displays matrix z as a colormapped heatmap over its cell indices.", P("z"));
        Add("pcolor", "Displays matrix z as a colormapped heatmap over the x/y extents.", P("x"), P("y"), P("z"));
        Add("zlabel", "Sets the z-axis label of a 3D axes.", P("text"));
        Add("zlim", "Sets the z-axis range of a 3D axes.", P("min"), P("max"));
        Add("view", "Sets the 3D camera angles in degrees (azimuth about z, elevation above the xy plane).", P("azimuth"), P("elevation"));
        Add("colormap", "Applies a built-in colormap (viridis, jet, hot, cool, gray) to the current axes' plots.", P("name"));
        Add("colorbar", "Shows (default) or hides the current axes' colorbar.", Opt("on"));
        Add("savefigure", "Saves the current figure (or figure fig) as a .graph document.", P("path"), Opt("fig"));
        Add("loadfigure", "Loads a .graph document as a new figure, makes it current, and returns its handle.", P("path"));
        Add("exportfigure", "Exports the current figure (or figure fig) as an image — png/jpg/bmp/tiff/svg/pdf by extension.", P("path"), Opt("fig"));

        return infos;
    }
}
