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
/// <see cref="Signature"/> is derived so it can never disagree with the parameter list.
/// </summary>
public sealed record JgsBuiltinInfo(string Name, IReadOnlyList<JgsBuiltinParameter> Parameters, string Summary)
{
    /// <summary>The rendered call signature, e.g. <c>plot(x, y, spec?)</c>.</summary>
    public string Signature
    {
        get
        {
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
        Add("abs", "Absolute value of x, element-wise over arrays.", P("x"));
        Add("floor", "Largest whole number not above x, element-wise over arrays.", P("x"));
        Add("ceil", "Smallest whole number not below x, element-wise over arrays.", P("x"));
        Add("round", "x rounded to the nearest whole number (halves away from zero), element-wise.", P("x"));
        Add("sign", "-1, 0, or 1 by the sign of x, element-wise over arrays.", P("x"));
        Add("pow", "x raised to exponent, element-wise over arrays.", P("x"), P("exponent"));

        // --- Array construction ----------------------------------------------------------------
        Add("linspace", "count evenly spaced values from start to stop, inclusive.", P("start"), P("stop"), P("count"));
        Add("range", "Values from start (inclusive) to stop (exclusive) in steps of step (default 1).", P("start"), P("stop"), Opt("step"));
        Add("zeros", "An array of count zeros, or a rows-by-cols matrix of zeros.", P("count"), Opt("cols"));
        Add("ones", "An array of count ones, or a rows-by-cols matrix of ones.", P("count"), Opt("cols"));
        Add("rand", "An array of count uniform random values in [0, 1).", P("count"));

        // --- Reductions and inspection ----------------------------------------------------------
        Add("length", "The number of elements in an array, or characters in a string.", P("value"));
        Add("sum", "The sum of a numeric array.", P("array"));
        Add("mean", "The arithmetic mean of a non-empty numeric array.", P("array"));
        Add("min", "The smallest value: min(array) or min(a, b, ...).", P("values"));
        Add("max", "The largest value: max(array) or max(a, b, ...).", P("values"));
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
        Add("find", "0-based indices of the truthy elements — find(temp > 85) gives matching row numbers.", P("mask"));
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
        Add("run", "Runs another JGS script into the current global scope (MATLAB-style include).", P("path"));
        Add("print", "Writes the values to the console, space-separated.", P("values"));

        // --- Figure setup and plotting -------------------------------------------------------------
        Add("figure", "Starts a new figure (or selects figure n) and returns its 1-based handle.", Opt("n"));
        Add("subplot", "Selects cell index (1-based, row-major) of a rows-by-cols axes grid.", P("rows"), P("cols"), P("index"));
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
        Add("view", "Sets the 3D camera angles in degrees (MATLAB view convention).", P("azimuth"), P("elevation"));
        Add("colormap", "Applies a built-in colormap (viridis, jet, hot, cool, gray) to the current axes' plots.", P("name"));
        Add("colorbar", "Shows (default) or hides the current axes' colorbar.", Opt("on"));
        Add("savefigure", "Saves the current figure (or figure fig) as a .graph document.", P("path"), Opt("fig"));
        Add("loadfigure", "Loads a .graph document as a new figure, makes it current, and returns its handle.", P("path"));
        Add("exportfigure", "Exports the current figure (or figure fig) as an image — png/jpg/bmp/tiff/svg/pdf by extension.", P("path"), Opt("fig"));

        return infos;
    }
}
