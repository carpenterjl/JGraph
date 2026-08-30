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

    /// <summary>The MATLAB language keywords, from the same table the MATLAB lexer reads.</summary>
    public static IReadOnlyList<string> MatlabKeywords { get; } =
        Lexer.MatlabKeywordNames.OrderBy(static k => k, StringComparer.Ordinal).ToArray();

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
        Constant("Inf", "Positive infinity (MATLAB's spelling of inf).");
        Constant("NaN", "Not-a-number (MATLAB's spelling of nan).");
        Constant("i", "The imaginary unit, √-1.");
        Constant("j", "The imaginary unit, √-1 (the engineering spelling of i).");
        Constant("newline", "A single newline character.");

        // --- Numeric limits (MATLAB writes these as functions; a bare mention is the value) ----
        Add("eps", "Floating-point spacing: eps is 2.2e-16, eps(x) the gap to the next value after x.", Opt("x"));
        Add("realmax", "The largest finite double, or single with 'single'.", Opt("precision"));
        Add("realmin", "The smallest normalized double, or single with 'single'.", Opt("precision"));
        Add("flintmax", "The largest consecutive integer a double represents exactly, 2^53.", Opt("precision"));
        Add("intmax", "The largest value of an integer class, 'int32' by default.", Opt("type"));
        Add("intmin", "The smallest value of an integer class, 'int32' by default.", Opt("type"));

        // --- Element-wise math (number or numeric array in, same shape out) -------------------
        Add("sin", "Sine of x (radians), element-wise over arrays.", P("x"));
        Add("cos", "Cosine of x (radians), element-wise over arrays.", P("x"));
        Add("tan", "Tangent of x (radians), element-wise over arrays.", P("x"));
        Add("asin", "Inverse sine of x, in radians, element-wise over arrays.", P("x"));
        Add("acos", "Inverse cosine of x, in radians, element-wise over arrays.", P("x"));
        Add("atan", "Inverse tangent of x, in radians, element-wise over arrays.", P("x"));
        Add("atan2", "Angle of the point (x, y) in radians, in the correct quadrant.", P("y"), P("x"));
        Add("sec", "Secant of x (radians), element-wise over arrays.", P("x"));
        Add("csc", "Cosecant of x (radians), element-wise over arrays.", P("x"));
        Add("cot", "Cotangent of x (radians), element-wise over arrays.", P("x"));
        Add("asec", "Inverse secant of x, in radians.", P("x"));
        Add("acsc", "Inverse cosecant of x, in radians.", P("x"));
        Add("acot", "Inverse cotangent of x, in radians.", P("x"));
        Add("sinh", "Hyperbolic sine of x, element-wise over arrays.", P("x"));
        Add("cosh", "Hyperbolic cosine of x, element-wise over arrays.", P("x"));
        Add("tanh", "Hyperbolic tangent of x, element-wise over arrays.", P("x"));
        Add("sech", "Hyperbolic secant of x.", P("x"));
        Add("csch", "Hyperbolic cosecant of x.", P("x"));
        Add("coth", "Hyperbolic cotangent of x.", P("x"));
        Add("asinh", "Inverse hyperbolic sine of x.", P("x"));
        Add("acosh", "Inverse hyperbolic cosine of x.", P("x"));
        Add("atanh", "Inverse hyperbolic tangent of x.", P("x"));
        Add("asech", "Inverse hyperbolic secant of x.", P("x"));
        Add("acsch", "Inverse hyperbolic cosecant of x.", P("x"));
        Add("acoth", "Inverse hyperbolic cotangent of x.", P("x"));
        Add("sind", "Sine of x in degrees; exact zero at multiples of 180.", P("x"));
        Add("cosd", "Cosine of x in degrees; exact zero at odd multiples of 90.", P("x"));
        Add("tand", "Tangent of x in degrees.", P("x"));
        Add("secd", "Secant of x in degrees.", P("x"));
        Add("cscd", "Cosecant of x in degrees.", P("x"));
        Add("cotd", "Cotangent of x in degrees.", P("x"));
        Add("asind", "Inverse sine of x, in degrees.", P("x"));
        Add("acosd", "Inverse cosine of x, in degrees.", P("x"));
        Add("atand", "Inverse tangent of x, in degrees.", P("x"));
        Add("asecd", "Inverse secant of x, in degrees.", P("x"));
        Add("acscd", "Inverse cosecant of x, in degrees.", P("x"));
        Add("acotd", "Inverse cotangent of x, in degrees.", P("x"));
        Add("atan2d", "Angle of the point (x, y) in degrees, in the correct quadrant.", P("y"), P("x"));
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
        Add("round", "x rounded (halves away from zero), element-wise: round(x), round(x, n), round(x, n, 'significant').", P("x"), Opt("n"), Opt("type"));
        Add("sign", "-1, 0, or 1 by the sign of x, element-wise over arrays.", P("x"));
        Add("hypot", "sqrt(a² + b²) without the overflow the written-out formula suffers.", P("a"), P("b"));
        Add("log2", "Base-2 logarithm of x, element-wise over arrays.", P("x"));
        Add("log1p", "log(1 + x), accurate for x near zero.", P("x"));
        Add("expm1", "exp(x) - 1, accurate for x near zero.", P("x"));
        Add("pow2", "2^x, or f·2^e when two arguments are given.", P("x"), Opt("e"));
        Add("nthroot", "The real nth root of x, so nthroot(-8, 3) is -2.", P("x"), P("n"));
        Add("realsqrt", "Square root of x, an error where the result would be complex.", P("x"));
        Add("reallog", "Natural logarithm of x, an error where the result would be complex.", P("x"));
        Add("realpow", "x raised to y, an error where the result would be complex.", P("x"), P("y"));
        Add("deg2rad", "Degrees converted to radians, element-wise.", P("x"));
        Add("rad2deg", "Radians converted to degrees, element-wise.", P("x"));
        Add("complex", "A complex value from real and imaginary parts (imaginary 0 by default).", P("re"), Opt("im"));

        // --- Integer arithmetic --------------------------------------------------------------------
        Add("gcd", "The greatest common divisor of a and b, element-wise.", P("a"), P("b"));
        Add("lcm", "The least common multiple of a and b, element-wise.", P("a"), P("b"));
        Add("factorial", "n! for a non-negative whole number, element-wise.", P("n"));
        Add("nchoosek", "The number of ways to choose k from n — or, given a vector, every such combination.", P("n"), P("k"));
        Add("primes", "Every prime up to and including n.", P("n"));
        Add("isprime", "Whether each element is prime.", P("x"));

        // --- Logical constructors, 2-D transforms, geometry ------------------------------------------
        Add("true", "A logical array of the given size, all true; bare true is still the literal.", Opt("rows"), Opt("cols"));
        Add("false", "A logical array of the given size, all false; bare false is still the literal.", Opt("rows"), Opt("cols"));
        Add("fft2", "The two-dimensional discrete Fourier transform of a matrix; optional sizes pad or truncate.", P("a"), Opt("m"), Opt("n"));
        Add("ifft2", "The inverse two-dimensional discrete Fourier transform.", P("a"), Opt("m"), Opt("n"), Opt("symflag"));
        Add("fftn", "The Fourier transform along every dimension; an optional size vector pads or truncates each.", P("a"), Opt("sz"));
        Add("ifftn", "The inverse transform along every dimension.", P("a"), Opt("sz"), Opt("symflag"));
        Add("convhull", "The convex hull of a set of points: closed counter-clockwise indices in the plane, triangles in space, with the area or volume as a second output.", P("x"), Opt("y"), Opt("z"), Opt("Simplify"));

        // --- Evaluating text and asking about the workspace -----------------------------------------
        Add("eval", "Runs a string as code in the current scope; a second string runs if the first fails.", P("code"), Opt("onError"));
        Add("evalc", "Runs a string as code and returns everything it printed.", P("code"));
        Add("evalin", "Runs a string as code in the 'base' or 'caller' workspace.", P("workspace"), P("code"));
        Add("assignin", "Creates a variable in the 'base' or 'caller' workspace.", P("workspace"), P("name"), P("value"));
        Add("str2func", "A function handle from its name, or from an @(x) … expression.", P("text"));
        Add("str2num", "Text evaluated as an expression: '[1 2 3]' is a vector. Empty when it does not evaluate.", P("text"));
        Add("exist", "What a name is: 1 a variable, 2 a file, 5 a builtin, 7 a folder, 0 nothing.", P("name"), Opt("kind"));
        Add("who", "The names of the variables in scope, as a cell array.", Opt("pattern"));
        Add("which", "Where a name comes from — a builtin, or the file it resolves to.", P("name"));
        Add("narginchk", "Fails unless the enclosing function got between low and high arguments.", P("low"), P("high"));
        Add("nargoutchk", "Fails unless the enclosing function was asked for between low and high outputs.", P("low"), P("high"));
        Add("nargchk", "The pre-R2011 spelling of narginchk.", P("low"), P("high"));
        Add("lasterr", "The message of the last caught error; an argument replaces it.", Opt("message"));
        Add("lasterror", "The last caught error as a struct with message and identifier fields.", Opt("err"));
        Add("lastwarn", "The message of the last warning; an argument replaces it.", Opt("message"));
        Add("rethrow", "Raises the error a catch block was handed, unchanged.", P("err"));
        Add("func2str", "The source text of a function handle.", P("f"));
        Add("functions", "What a function handle is, as a struct with function, type, and file.", P("f"));
        Add("mfilename", "The name of the running script file; 'fullpath' asks for its whole path.", Opt("option"));
        Add("inputname", "The caller's variable name for the k-th argument, or '' if it had none.", P("k"));

        // --- The console session --------------------------------------------------------------------
        Add("diary", "Echoes console output to a file; 'off' stops, a name chooses the file.", Opt("target"));
        Add("echo", "Turns function-file line echoing on or off.", Opt("state"), Opt("target"));
        Add("home", "Clears the console, the way a terminal's home key once did.");
        Add("more", "Turns output paging on or off, or sets the page size.", Opt("state"));
        Add("input", "Prompts for a value at the console; 's' keeps the reply as text.", P("prompt"), Opt("kind"));
        Add("lookfor", "Lists the builtins whose name or summary mentions a word.", P("word"));
        Add("what", "The script, data, and figure files in a folder, grouped by kind.", Opt("folder"));
        Add("beep", "Sounds the console bell, or turns it on or off.", Opt("state"));
        Add("pack", "Asks the runtime to collect and compact memory now.", Opt("target"));
        Add("recycle", "Reports whether delete recycles rather than removes; JGraph always removes.", Opt("state"));
        Add("rehash", "A no-op: a function file is re-read whenever its timestamp has moved, so there is no stale cache to drop.", Opt("scope"));
        Add("display", "Prints a value the way the console would.", P("value"));

        // --- The installation -----------------------------------------------------------------------
        Add("version", "The JGraph version; '-release' and '-date' ask for parts of it.", Opt("option"));
        Add("computer", "The platform name, or its short form with 'arch'.", Opt("option"));
        Add("matlabroot", "The folder JGraph is installed in.");
        Add("matlabdrive", "Empty: JGraph has no cloud drive to point at.");
        Add("license", "The licence name, or 1 from license('test', feature) — everything is present.", Opt("option"), Opt("feature"));
        Add("isstudent", "False: JGraph has no student edition.");
        Add("memory", "A struct of available and used memory, in bytes.");
        Add("maxNumCompThreads", "The computation thread count; an argument sets it and returns the old one.", Opt("count"));
        Add("fftw", "Reports or sets the FFT planner mode; JGraph's transform has no plan to tune.", P("what"), Opt("mode"));

        // --- Files, folders, and the machine --------------------------------------------------------
        Add("pwd", "The working directory relative paths resolve against.");
        Add("cd", "Moves to a folder; with no argument it reports where it is.", Opt("folder"));
        Add("mkdir", "Creates a folder, including any parents it needs.", P("folder"), Opt("name"));
        Add("rmdir", "Removes an empty folder — or a whole tree when the second argument is 's'.", P("folder"), Opt("s"));
        Add("copyfile", "Copies a file, overwriting the destination.", P("source"), P("destination"), Opt("mode"));
        Add("movefile", "Moves a file, overwriting the destination.", P("source"), P("destination"), Opt("mode"));
        Add("delete", "Deletes the named files, or removes the figure objects a handle names: delete(h).", P("path"));
        Add("fileattrib", "A struct of a file's attributes, or false when it does not exist.", P("path"));
        Add("filesep", "The character that separates folders on this system.");
        Add("tempdir", "The system's folder for temporary files.");
        Add("tempname", "A full path in the temporary folder that nothing is using yet.");
        Add("filemarker", "The character that separates a file from a function inside it.");
        Add("isfile", "Whether the path names a file that exists.", P("path"));
        Add("isfolder", "Whether the path names a folder that exists.", P("path"));
        Add("fullfile", "Path pieces joined with the right separator.", P("part"), Opt("more"));
        Add("fileparts", "A path split into {folder, name, extension}.", P("path"));
        Add("feof", "Whether an open file is at its end.", P("fid"));
        Add("ferror", "The last error on an open file — empty, since failures are raised instead.", P("fid"), Opt("clear"));
        Add("ftell", "The current byte position in an open file.", P("fid"));
        Add("fseek", "Moves the position in an open file; 0 on success, -1 on failure.", P("fid"), P("offset"), Opt("origin"));
        Add("fgets", "The next line of an open file, keeping its newline and stopping after a given number of characters; -1 at the end.", P("fid"), Opt("nchar"));
        Add("fscanf", "Numbers or text read from an open file under a scanf format, bounded by a count or an [m n] shape, leaving the file where the reading stopped.", P("fid"), P("format"), Opt("size"));
        Add("textscan", "A file or a piece of text read under a format as a table: one cell per conversion, optionally a set number of times, with Delimiter, HeaderLines, Whitespace, EmptyValue and CollectOutput.", P("source"), P("format"), Opt("N"));
        Add("type", "Prints a file's contents to the console.", P("path"));
        Add("getenv", "The value of an environment variable, or '' when it is not set.", P("name"));
        Add("setenv", "Sets an environment variable for this process.", P("name"), Opt("value"));
        Add("ispc", "Whether this machine runs Windows.");
        Add("isunix", "Whether this machine runs Linux or macOS.");
        Add("ismac", "Whether this machine runs macOS.");
        Add("namelengthmax", "The longest name a variable may have.");
        Add("cputime", "Seconds of processor time used, for timing a long computation.");
        Add("drawnow", "Shows touched figures, runs queued callbacks and flushes rendering; 'limitrate' caps the rendering, 'nocallbacks' skips the queue.", Opt("mode"), Opt("mode2"));
        Add("jsonencode", "A value written as JSON text.", P("x"), Opt("options"));
        Add("jsondecode", "JSON text read back as numbers, cells, and structs.", P("text"));

        // --- Array statistics and rearrangement -----------------------------------------------------
        Add("arrayfun", "Applies a function to each element; 'UniformOutput', false gives a cell.", P("f"), P("a"), Opt("option"));
        Add("bsxfun", "Applies a function pairwise, expanding a scalar across the other array.", P("f"), P("a"), P("b"));
        Add("structfun", "Applies a function to each field of a struct.", P("f"), P("s"), Opt("option"));
        Add("struct2cell", "A struct's field values as a cell array.", P("s"));
        Add("cell2struct", "A struct built from a cell of values and a cell of field names.", P("values"), P("names"), Opt("dim"));
        Add("accumarray", "Sums values into bins their subscripts name; a function handle reduces differently.", P("subs"), P("values"), Opt("size"), Opt("f"), Opt("fill"));
        Add("cummax", "The running maximum so far at each position, down each column or along a named dimension.", P("x"), Opt("dim"));
        Add("cummin", "The running minimum so far at each position, down each column or along a named dimension.", P("x"), Opt("dim"));
        Add("maxk", "The k largest values of each slice, largest first: [b, i] = maxk(x, k, dim).", P("x"), P("k"), Opt("dim"));
        Add("mink", "The k smallest values of each slice, smallest first: [b, i] = mink(x, k, dim).", P("x"), P("k"), Opt("dim"));
        Add("histc", "How many values fall in each bin the edges define, per slice along dim.", P("x"), P("edges"), Opt("dim"));
        Add("uniquetol", "The unique values, treating any two within a tolerance as one: [c, ia, ic] = uniquetol(x, tol, 'ByRows', true, 'DataScale', s, 'OutputAllIndices', true).", P("x"), Opt("tol"), Opt("options"));
        Add("ismembertol", "Whether each value is within a tolerance of something in the set: [lia, locb] = ismembertol(x, set, tol, 'ByRows', true).", P("x"), P("set"), Opt("tol"), Opt("options"));
        Add("issortedrows", "Whether a matrix's rows are in lexicographic order.", P("a"));
        Add("randi", "Uniform whole numbers from 1 to imax, or from the range [low high]; a trailing class name (or 'like', x) says what they come back as.", P("imax"), Opt("rows"), Opt("cols"), Opt("class"));
        Add("randperm", "A random permutation of 1..n, or k values drawn from it.", P("n"), Opt("k"));
        Add("rng", "Seeds the random stream, or reports its state: rng(seed), rng('default'), rng('shuffle'), s = rng.", Opt("seed"), Opt("generator"));
        Add("circshift", "The values moved along by k places, wrapping around: circshift(x, k, dim), or a k per dimension.", P("x"), P("k"), Opt("dim"));
        Add("rot90", "A matrix turned a quarter turn counter-clockwise, k times.", P("a"), Opt("k"));
        Add("movmean", "The mean over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movmedian", "The median over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movsum", "The sum over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movprod", "The product over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movmax", "The maximum over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movmin", "The minimum over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movstd", "The standard deviation over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movvar", "The variance over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));
        Add("movmad", "The mean absolute deviation over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("options"));

        // --- Text search, shaping, and regular expressions ------------------------------------------
        Add("strfind", "Every position where a pattern appears in a string.", P("text"), P("pattern"));
        Add("findstr", "The positions where the shorter of two strings appears in the longer.", P("a"), P("b"));
        Add("strncmp", "Whether two strings agree in their first n characters.", P("a"), P("b"), P("n"));
        Add("strncmpi", "Whether two strings agree in their first n characters, ignoring case.", P("a"), P("b"), P("n"));
        Add("count", "How many times a pattern appears in a string, or in each of a cell of strings.", P("text"), P("pattern"));
        Add("matches", "Whether a string is exactly the pattern.", P("text"), P("pattern"));
        Add("strlength", "The number of characters in a string, or in each of a cell of strings.", P("text"));
        Add("deblank", "A string with its trailing whitespace removed.", P("text"));
        Add("blanks", "A string of n spaces.", P("n"));
        Add("strcat", "Strings joined end to end, each with its trailing whitespace dropped.", P("a"), Opt("b"));
        Add("setstr", "Character codes as text (the pre-R2006 spelling of char).", P("codes"));
        Add("convertCharsToStrings", "The value unchanged: JGraph's text is char, and there is no string type to convert to.", P("x"));
        Add("convertStringsToChars", "The value unchanged: JGraph's text is already char.", P("x"));
        Add("convertContainedStringsToChars", "The value unchanged: JGraph's text is already char.", P("x"));
        Add("regexp", "Regular expression search: start positions, or the outputs the option words name.", P("text"), P("expr"), Opt("option"));
        Add("regexpi", "Regular expression search, ignoring case.", P("text"), P("expr"), Opt("option"));
        Add("regexprep", "Every match of a regular expression replaced; $1 refers to a captured group. Option words: 'once', 'ignorecase', 'preservecase', 'emptymatch', 'dotexceptnewline', 'lineanchors', 'freespacing'.", P("text"), P("expr"), P("replacement"), Opt("option"), Opt("option2"));
        Add("regexptranslate", "A string turned into a regular expression: 'escape', 'wildcard', or 'flexible'.", P("mode"), P("text"));
        Add("isstrprop", "Which characters belong to a category ('alpha', 'digit', 'wspace', …).", P("text"), P("category"));
        Add("unicode2native", "The bytes a string encodes to (UTF-8 by default).", P("text"), Opt("encoding"));
        Add("native2unicode", "The string a sequence of bytes decodes to (UTF-8 by default).", P("bytes"), Opt("encoding"));
        Add("typecast", "The same bits read as another numeric class.", P("x"), P("type"));
        Add("sscanf", "Numbers or text read out of a string under a scanf format.", P("text"), P("format"), Opt("count"));

        // --- Matrix shape questions and linear algebra -----------------------------------------------
        Add("istril", "Whether every entry above the diagonal is zero.", P("a"));
        Add("istriu", "Whether every entry below the diagonal is zero.", P("a"));
        Add("isdiag", "Whether every entry off the diagonal is zero.", P("a"));
        Add("issymmetric", "Whether the matrix equals its own transpose, exactly.", P("a"));
        Add("ishermitian", "Whether the matrix equals its conjugate transpose (its transpose, for real data).", P("a"));
        Add("isbanded", "Whether the matrix fits inside the given lower and upper bandwidths.", P("a"), P("lower"), P("upper"));
        Add("bandwidth", "How far the non-zeros reach below the diagonal — or 'upper' for above; [lo, up] for both.", P("a"), Opt("which"));
        Add("tril", "The lower triangle of a matrix, from the k-th diagonal down.", P("a"), Opt("k"));
        Add("triu", "The upper triangle of a matrix, from the k-th diagonal up.", P("a"), Opt("k"));
        Add("chol", "The Cholesky factor of a positive definite matrix: upper by default, 'lower' for L.", P("a"), Opt("shape"));
        Add("ldl", "The LDL' factorization of a symmetric matrix, as [L, D] or [L, D, P].", P("a"));
        Add("hess", "The upper Hessenberg form of a matrix, as H or [Q, H].", P("a"));
        Add("expm", "The matrix exponential e^A — not exp applied element by element.", P("a"));
        Add("linsolve", "Solves the linear system a·x = b.", P("a"), P("b"));
        Add("rcond", "The reciprocal condition number in the 1-norm; near 0 means near singular.", P("a"));
        Add("null", "An orthonormal basis for the null space of a.", P("a"));
        Add("orth", "An orthonormal basis for the range of a.", P("a"));
        Add("pinv", "The Moore-Penrose pseudoinverse of a.", P("a"));
        Add("cross", "The cross product of two 3-element vectors.", P("a"), P("b"));
        Add("vecnorm", "The p-norm of a vector, or of each slice along a dimension (p = 2 by default).", P("a"), Opt("p"), Opt("dim"));
        Add("schur", "The real Schur form T, or [U, T] with U orthogonal and U*T*U' equal to a.", P("a"), Opt("kind"));
        Add("ordeig", "The eigenvalues of a quasi-triangular matrix, in the order its blocks appear.", P("t"));
        Add("ordschur", "Reorders a Schur form so the selected eigenvalues come first.", P("u"), P("t"), P("select"));
        Add("cholupdate", "The Cholesky factor of r'*r + x*x', or of r'*r - x*x' with '-'.", P("r"), P("x"), Opt("sign"));
        Add("qrupdate", "The QR factors of a + u*v', from the factors of a.", P("q"), P("r"), P("u"), P("v"));
        Add("delaunay", "The Delaunay triangulation of a set of points: triangles in the plane, tetrahedra in space.", P("x"), Opt("y"), Opt("z"));
        Add("voronoin", "The Voronoi diagram of the rows of X: [V, C] with the vertices and one cell of vertex numbers per point, row 1 of V being the point at infinity.", P("X"));
        Add("contourc", "The contour matrix of z at the given levels, without drawing anything.", P("z"), Opt("a"), Opt("b"), Opt("levels"));

        // --- Special functions ---------------------------------------------------------------------
        Add("erf", "The error function, element-wise.", P("x"));
        Add("erfc", "1 - erf(x), evaluated so the tail keeps its digits.", P("x"));
        Add("erfcx", "exp(x²)·erfc(x), the scaled complementary error function.", P("x"));
        Add("erfinv", "The inverse error function.", P("y"));
        Add("erfcinv", "The inverse complementary error function.", P("y"));
        Add("gamma", "The gamma function Γ(x), element-wise.", P("x"));
        Add("gammaln", "ln Γ(x), which stays finite where Γ itself overflows.", P("x"));
        Add("gammainc", "The regularized incomplete gamma P(a, x), or Q with 'upper'.", P("x"), P("a"), Opt("tail"));
        Add("gammaincinv", "The x whose incomplete gamma is y.", P("y"), P("a"), Opt("tail"));
        Add("beta", "The beta function B(a, b).", P("a"), P("b"));
        Add("betaln", "ln B(a, b).", P("a"), P("b"));
        Add("betainc", "The regularized incomplete beta I_x(a, b), or its upper tail with 'upper'.", P("x"), P("a"), P("b"), Opt("tail"));
        Add("betaincinv", "The x whose incomplete beta is y.", P("y"), P("a"), P("b"), Opt("tail"));
        Add("psi", "The digamma function ψ(x), or its k-th derivative as psi(k, x).", P("x"), Opt("x2"));
        Add("besselj", "The Bessel function of the first kind J_nu(x).", P("nu"), P("x"), Opt("scale"));
        Add("bessely", "The Bessel function of the second kind Y_nu(x).", P("nu"), P("x"), Opt("scale"));
        Add("besseli", "The modified Bessel function I_nu(x); scale gives exp(-abs(x))·I.", P("nu"), P("x"), Opt("scale"));
        Add("besselk", "The modified Bessel function K_nu(x); scale gives exp(x)·K.", P("nu"), P("x"), Opt("scale"));
        Add("besselh", "The Hankel function H_nu of kind 1 (default) or 2.", P("nu"), P("kind"), Opt("x"), Opt("scale"));
        Add("airy", "The Airy function Ai (kind 0), Ai' (1), Bi (2), or Bi' (3).", P("kind"), Opt("x"), Opt("scale"));

        // --- Bit manipulation ----------------------------------------------------------------------
        Add("bitand", "Bitwise AND, element-wise; the optional class name sets the width.", P("a"), P("b"), Opt("type"));
        Add("bitor", "Bitwise OR, element-wise; the optional class name sets the width.", P("a"), P("b"), Opt("type"));
        Add("bitxor", "Bitwise exclusive OR, element-wise; the optional class name sets the width.", P("a"), P("b"), Opt("type"));
        Add("bitcmp", "Bitwise complement within the assumed width (53 bits by default).", P("x"), Opt("type"));
        Add("bitget", "The bit at a 1-based position, counting from the least significant.", P("x"), P("position"), Opt("type"));
        Add("bitset", "x with the bit at a 1-based position set (or cleared when value is 0).", P("x"), P("position"), Opt("value"), Opt("type"));
        Add("bitshift", "x shifted left by k bits, or right when k is negative.", P("x"), P("k"), Opt("type"));

        // --- Radix conversion ----------------------------------------------------------------------
        Add("dec2bin", "A number as binary text, zero-padded to at least minLength digits.", P("x"), Opt("minLength"));
        Add("dec2hex", "A number as hexadecimal text, zero-padded to at least minLength digits.", P("x"), Opt("minLength"));
        Add("dec2base", "A number as text in a base from 2 to 36.", P("x"), P("base"), Opt("minLength"));
        Add("bin2dec", "The number a string of binary digits stands for.", P("text"));
        Add("hex2dec", "The number a string of hexadecimal digits stands for.", P("text"));
        Add("base2dec", "The number a string of digits in the given base stands for.", P("text"), P("base"));

        // --- Dense storage answers (JGraph has no sparse type) --------------------------------------
        Add("issparse", "Always false: JGraph stores every matrix densely.", P("x"));
        Add("full", "A sparse matrix in dense storage; anything already dense is itself.", P("x"));
        Add("nnz", "How many elements are not zero.", P("x"));
        Add("nonzeros", "The non-zero elements, as a vector.", P("x"));

        // --- Operator function forms (the interpreter declares these; see RegisterOperatorFunctions)
        Add("plus", "a + b.", P("a"), P("b"));
        Add("minus", "a - b.", P("a"), P("b"));
        Add("times", "a .* b, element-wise multiplication.", P("a"), P("b"));
        Add("mtimes", "a * b, matrix multiplication.", P("a"), P("b"));
        Add("rdivide", "a ./ b, element-wise right division.", P("a"), P("b"));
        Add("ldivide", "a .\\ b, element-wise left division.", P("a"), P("b"));
        Add("mrdivide", "a / b, matrix right division.", P("a"), P("b"));
        Add("mldivide", "a \\ b, matrix left division (solves a system).", P("a"), P("b"));
        Add("power", "a .^ b, element-wise power.", P("a"), P("b"));
        Add("mpower", "a ^ b, matrix power.", P("a"), P("b"));
        Add("uminus", "-a.", P("a"));
        Add("uplus", "+a.", P("a"));
        Add("eq", "a == b, element-wise.", P("a"), P("b"));
        Add("ne", "a ~= b, element-wise.", P("a"), P("b"));
        Add("lt", "a < b, element-wise.", P("a"), P("b"));
        Add("le", "a <= b, element-wise.", P("a"), P("b"));
        Add("gt", "a > b, element-wise.", P("a"), P("b"));
        Add("ge", "a >= b, element-wise.", P("a"), P("b"));
        Add("xor", "Exclusive or, element-wise over arrays.", P("a"), P("b"));
        Add("colon", "The range a:b, or a:step:b when three arguments are given.", P("a"), P("b"), Opt("stop"));

        Add("pow","x raised to exponent, element-wise over arrays.", P("x"), P("exponent"));

        // --- Array construction ----------------------------------------------------------------
        Add("linspace", "count (default 100) evenly spaced values from start to stop, inclusive.", P("start"), P("stop"), Opt("count"));
        Add("range", "In JGS, values from start (inclusive) to stop (exclusive) in steps of step (default 1). In the MATLAB dialect the statistic instead: range(x) is max minus min, with range(A, dim) and range(A, 'all').", P("start"), P("stop"), Opt("step"));
        Add("zeros", "An array of count zeros, a rows-by-cols matrix, or the shape of a size vector (zeros(size(t))).", P("count"), Opt("cols"));
        Add("ones", "An array of count ones, a rows-by-cols matrix, or the shape of a size vector.", P("count"), Opt("cols"));
        Add("rand", "Uniform random values in [0, 1): rand(count) — and rand(), rand(n) as n-by-n, rand(r, c) in MATLAB.", P("count"));
        Add("eye", "The identity matrix: eye(n), eye(r, c), or eye([r c]).", Opt("n"), Opt("cols"));
        Add("diag", "A diagonal matrix from a vector, or a matrix's diagonal as a vector; offset k picks another diagonal.", P("x"), Opt("k"));
        Add("magic", "An n-by-n magic square (every row, column, and diagonal sums alike).", P("n"));
        Add("logspace", "count (default 50) logarithmically spaced values from 10^start to 10^stop.", P("start"), P("stop"), Opt("count"));
        Add("ndims", "The number of dimensions: 2 for everything here except multi-channel images (3).", P("x"));
        Add("reshape", "The same elements in a new rows-by-cols shape, read and filled column by column; one dimension may be [].", P("x"), P("rows"), P("cols"));
        Add("cat", "Concatenates values along a dimension: 1 stacks rows, 2 joins columns.", P("dim"), P("first"), Opt("more..."));
        Add("horzcat", "Joins values side by side — [a, b] as a function.", P("first"), Opt("more..."));
        Add("vertcat", "Stacks values top to bottom — [a; b] as a function.", P("first"), Opt("more..."));
        Add("flip", "Reverses a vector, or a matrix along dim (default 1, its rows).", P("x"), Opt("dim"));
        Add("fliplr", "Reverses left-right: a vector's order, or each matrix row.", P("x"));
        Add("flipud", "Reverses up-down: a matrix's row order (a vector is a single row, so it is unchanged).", P("x"));
        Add("squeeze", "Removes singleton dimensions — a no-op here, where values are at most 2-D.", P("x"));
        Add("permute", "Rearranges dimensions: [1 2] leaves x alone, [2 1] transposes it.", P("x"), P("order"));
        Add("transpose", "The non-conjugate transpose, x.' as a function.", P("x"));
        Add("ctranspose", "The complex-conjugate transpose, x' as a function.", P("x"));
        Add("prod", "The product of a numeric array, over one dimension, several, or 'all'.", P("array"), Opt("dim"));
        Add("ismember", "Whether each element of x is in the set: [tf, loc] = ismember(x, set, 'rows') also says where.", P("x"), P("set"), Opt("option"));
        Add("union", "Every value in either set, once: [c, ia, ib] = union(a, b, 'rows', 'stable').", P("a"), P("b"), Opt("option"));
        Add("intersect", "The values in both sets: [c, ia, ib] = intersect(a, b, 'rows', 'stable').", P("a"), P("b"), Opt("option"));
        Add("setdiff", "The values in a that are not in b: [c, ia] = setdiff(a, b, 'rows', 'stable').", P("a"), P("b"), Opt("option"));
        Add("setxor", "The values in exactly one of the sets: [c, ia, ib] = setxor(a, b, 'rows', 'stable').", P("a"), P("b"), Opt("option"));
        Add("dot", "The inner product of two equal-length vectors (conjugating the first when complex).", P("a"), P("b"));
        Add("inv", "The inverse of a square matrix (errors when singular).", P("A"));
        Add("det", "The determinant of a square matrix.", P("A"));
        Add("rank", "The number of linearly independent rows/columns, by singular values above tol.", P("A"), Opt("tol"));
        Add("trace", "The sum of a square matrix's diagonal (complex-aware).", P("A"));
        Add("hilb", "The n-by-n Hilbert matrix, H(i,j) = 1/(i+j-1) — the classic ill-conditioned test matrix.", P("n"));
        Add("polyval", "Evaluates polynomial p (highest power first) at x; [y, delta] = polyval(p, x, s, mu) adds polyfit's error estimate.", P("p"), P("x"), Opt("s"), Opt("mu"));
        Add("peaks", "The peaks demonstration surface; [X, Y, Z] = peaks(n) hands back the grids too.", Opt("n"));
        Add("cond", "The condition number of a matrix (2-norm by default; 1, Inf, and 'fro' accepted).", P("A"), Opt("p"));
        Add("sqrtm", "The principal matrix square root, by the Denman-Beavers iteration.", P("A"));
        Add("logm", "The principal matrix logarithm, by inverse scaling and squaring over sqrtm.", P("A"));
        Add("ode45", "Solves dy/dt = f(t, y): [t, y] = ode45(f, tspan, y0), Dormand-Prince with adaptive steps.", P("f"), P("tspan"), P("y0"));
        Add("sparse", "Converts to sparse storage: sparse(A), sparse(m, n), sparse(i, j, v), or sparse(i, j, v, m, n).", P("A"), Opt("j"), Opt("v"), Opt("m"), Opt("n"));
        Add("sprand", "A sparse random matrix with roughly m*n*density uniform nonzeros.", P("m"), P("n"), P("density"));
        Add("eigs", "The k eigenvalues of largest magnitude (Arnoldi); [V, D] = eigs(A, k) adds Ritz vectors.", P("A"), P("k"));
        Add("spy", "Plots the nonzero pattern of a matrix, row 1 at the top.", P("A"));

        // --- Data types and conversions (M43) ---------------------------------------------------
        Add("table", "Builds a table from column variables; a trailing 'VariableNames', {…} names them (default Var1…VarN).", P("var1"), Opt("var2"));
        Add("timetable", "A table whose first variable is the row times: timetable(rowTimes, var1, …).", P("rowTimes"), P("var1"));
        Add("categorical", "Category labels from a cell or array (represented as the cell of names).", P("x"));
        Add("summary", "Per-variable statistics of a table, or category counts of a categorical, as a struct.", P("x"));
        Add("string", "The value as a string array: a char row becomes one string, a cell or array one per element.", P("x"));
        Add("strings", "An array of empty strings: strings(n) is n-by-n, strings(r, c) is r-by-c.", P("rows?"), P("cols?"));
        Add("char", "Text as a char row: a string, a cell, or code points; several arguments stack into a char matrix.", P("x"), P("more?"));
        Add("strip", "Whitespace removed from a string: strip(s), or strip(s, 'left'|'right'|'both').", P("s"), P("side?"));
        Add("pad", "A string padded to a width: pad(s, width) or pad(s, width, 'left'|'right'|'both').", P("s"), P("width?"), P("side?"));
        Add("erase", "The string with every occurrence of a piece of text taken out.", P("s"), P("what"));
        Add("insertAfter", "Text inserted after a marker or a position.", P("s"), P("marker"), P("what"));
        Add("insertBefore", "Text inserted before a marker or a position.", P("s"), P("marker"), P("what"));
        Add("extractAfter", "What follows a marker or a position, as a string.", P("s"), P("marker"));
        Add("extractBefore", "What precedes a marker or a position, as a string.", P("s"), P("marker"));
        Add("extractBetween", "What lies between two markers, or between two positions.", P("s"), P("from"), P("to"));
        Add("cellstr", "A string array as a cell of character rows.", P("x"));
        Add("compose", "Formats each element through the format string, one output string per element.", P("format"), P("values"));
        Add("missing", "The missing value: a string slot with nothing in it (displays as <missing>).");
        Add("ismissing", "Whether each element is missing (the missing string, or NaN).", P("x"));
        Add("tiledlayout", "Starts an r-by-c tile grid (or 'flow') and answers the layout object.", P("rows"), P("cols"), Opt("name"), Opt("value"));
        Add("nexttile", "Takes the next tile of the grid (or tile n, spanning [r c]) and answers its axes.", Opt("n"), Opt("span"));
        Add("axis", "Aspect and limit control: axis equal/image/square/tight/off, or axis([xmin xmax ymin ymax]).", Opt("option"));
        Add("shading", "Surface face shading: faceted (flat faces with grid lines), flat (no lines), or interp.", Opt("mode"));
        Add("lighting", "How surfaces respond to lights: none, flat (one normal per facet), or gouraud.", Opt("mode"));
        Add("material", "Surface reflectance: shiny, dull, metal, default, or [ambient diffuse specular exponent reflectance].", Opt("preset"));
        Add("light", "Adds a light to the current axes: light('Position', [x y z], 'Color', c, 'Style', 'infinite'|'local').", Opt("name"), Opt("value"));
        Add("lightangle", "Adds a light at an azimuth and elevation, on the same convention as view.", P("az"), P("el"));
        Add("camlight", "Adds a light beside the camera: right (default), left, headlight, or camlight(az, el).", Opt("position"));
        Add("rotate3d", "Accepted for compatibility: 3-D rotation is always interactive (drag the axes).", Opt("state"));
        Add("norm", "Vector norms (2 by default, any p, inf) and matrix norms (1, 2, inf, 'fro').", P("x"), Opt("p"));
        Add("eig", "Eigenvalues of a square matrix; [V, D] = eig(A) adds the eigenvectors.", P("A"));
        Add("lu", "LU factorization: [L, U, P] = lu(A) with P*A = L*U ([L, U] folds P into L).", P("A"));
        Add("qr", "QR factorization: [Q, R] = qr(A) with A = Q*R (economy-size Q).", P("A"));
        Add("svd", "Singular values of a matrix; [U, S, V] = svd(A) adds the singular vectors (economy-size).", P("A"));

        // --- DSP and audio ----------------------------------------------------------------------
        Add("fft", "Discrete Fourier transform, down each column by default; optional length pads or truncates, and a dimension picks the direction.", P("x"), Opt("n"), Opt("dim"));
        Add("ifft", "Inverse discrete Fourier transform; optional length pads or truncates, a dimension picks the direction, and 'symmetric' promises a real answer.", P("x"), Opt("n"), Opt("dim"), Opt("symflag"));
        Add("fftshift", "Rotates a spectrum so DC sits at the center, along one dimension or all of them.", P("x"), Opt("dim"));
        Add("ifftshift", "Undoes fftshift, restoring DC-first order.", P("x"), Opt("dim"));
        Add("filter", "Applies the digital filter b/a down each column of x, from rest or from given initial conditions; [y, zf] hands the final ones back.", P("b"), P("a"), P("x"), Opt("zi"), Opt("dim"));
        Add("freqz", "Frequency response of b/a: [H, f] with complex H at count points (fs defaults to 2 = normalized).", P("b"), P("a"), Opt("count"), Opt("fs"));
        Add("butter", "Butterworth design: [b, a] for order n and normalized cutoff(s) Wn; type \"low\"/\"high\"/\"bandpass\"/\"stop\".", P("n"), P("Wn"), Opt("type"));
        Add("firpm", "Parks-McClellan equiripple FIR: order n, normalized band edges f, band amplitudes a.", P("n"), P("f"), P("a"));
        Add("audioread", "Reads a .wav file: [samples, fs] with samples normalized to [-1, 1] (stereo averaged to mono).", P("path"));
        Add("sound", "Plays samples through the host's audio output without blocking (fs defaults to 8192).", P("y"), Opt("fs"));
        Add("pause", "Waits: pause(seconds) for a fixed wait (interruptible by Stop), bare pause for a key press, and pause('on'|'off'|'query') to turn every pause in a script on or off, answering the state as it was.", Opt("seconds"));
        Add("exit", "Ends the script and closes the application, with an optional process exit code.", Opt("code"));
        Add("quit", "An alias for exit.", Opt("code"));

        // --- MATLAB names (M28) -----------------------------------------------------------------
        Add("rem", "Remainder after division, taking the sign of the dividend (mod takes the divisor's).", P("x"), P("divisor"));
        Add("fix", "Rounds toward zero.", P("x"));
        Add("randn", "Normally distributed random numbers: randn(n), randn(r, c), or randn(size(x)).", Opt("n"), Opt("m"));
        Add("repmat", "Repeats a value or array end to end the given number of times.", P("x"), P("times"), Opt("times2"));
        Add("isnumeric", "True for a number, a complex number, or an array of numbers.", P("x"));
        Add("ischar", "True for a string.", P("x"));
        Add("islogical", "True for a bool or an array of bools (a mask).", P("x"));
        Add("iscell", "True for a cell array.", P("x"));
        Add("isstruct", "True for a struct.", P("x"));
        Add("strcmp", "Compares two strings (or a cell of strings against one), case-sensitively.", P("a"), P("b"));
        Add("strcmpi", "Compares two strings ignoring case.", P("a"), P("b"));
        Add("strrep", "Replaces every occurrence of one substring with another.", P("text"), P("find"), P("replace"));
        Add("strtrim", "Removes leading and trailing whitespace.", P("text"));
        Add("strsplit", "Splits text into a cell of pieces, on a delimiter (or a cell of them) or on whitespace; [C, matches] also reports the delimiters cut on.", P("text"), Opt("delimiter"), Opt("options"));
        Add("strjoin", "Joins a cell (or array) of pieces into one string; a cell separator gives every gap its own.", P("parts"), Opt("separator"));
        Add("num2str", "Formats a number or an array as text, optionally to a given number of significant digits or a sprintf format.", P("x"), Opt("digits"));
        Add("mat2str", "Writes a value the way the language reads it back: '[1 2;3 4]', to n significant digits.", P("x"), Opt("digits"));
        Add("int2str", "Rounds to whole numbers and formats them as text.", P("x"));
        Add("deal", "Hands one value to every output, or one value each: [a, b] = deal(1, 2).", P("value"), Opt("more..."));
        Add("str2double", "Parses text as a number, or NaN when it is not one.", P("text"));
        Add("error", "Stops the script with a message (accepts a format string and an optional 'id:sub' first).", P("message"), Opt("args..."));
        Add("warning", "Writes a warning to the console without stopping; warning('off') is accepted and ignored.", P("message"), Opt("args..."));
        Add("assert", "Stops the script when the condition is false, with an optional message.", P("condition"), Opt("message"));
        Add("cell", "Creates a cell array of the given size, filled with empty arrays.", P("count"), Opt("count2"));
        Add("struct", "Builds a struct from name/value pairs; a cell value spreads across a struct array, so struct('a', {1, 2}) is 1-by-2 and struct('a', {}) is empty.", Opt("name"), Opt("value"));
        Add("fieldnames", "The names of a struct's fields, as a cell.", P("s"));
        Add("isfield", "True when the struct has the named field; a cell of names gives one answer each.", P("s"), P("name"));
        Add("rmfield", "A copy of the struct, or of every element of a struct array, without the named field (or cell of names).", P("s"), P("name"));
        Add("orderfields", "The same struct with its fields in order — alphabetical, or the order a cell of names gives.", P("s"), Opt("order"));
        Add("getfield", "The value of a named field — getfield(s, 'a') is s.a written as a call.", P("s"), P("name"));
        Add("setfield", "A copy of the struct with the named field set — setfield(s, 'a', v) is s.a = v as an expression.", P("s"), P("name"), P("value"));
        Add("num2cell", "Puts each element of an array into its own cell.", P("x"));
        Add("cell2mat", "Flattens a cell of numbers (or arrays of numbers) into one array.", P("c"));
        Add("feval", "Calls a function handle with the given arguments.", P("f"), Opt("args..."));
        Add("cellfun", "Applies a function — or a named question like 'isempty' — to every cell of one or more cells; add 'UniformOutput', false to collect a cell.", P("f"), P("c"), Opt("options..."));
        Add("sub2ind", "The single index of a row/column position in an array of the given size.", P("size"), P("row"), P("column"));
        Add("ind2sub", "The row and column of a single index in an array of the given size.", P("size"), P("index"));

        // --- Time & date ------------------------------------------------------------------------
        Add("tic", "Starts a stopwatch and returns a handle; pass it to toc to time a specific interval.");
        Add("toc", "Elapsed seconds since the last tic, or since the tic that returned handle.", Opt("handle"));
        Add("clock", "The current local time as a [year, month, day, hour, minute, seconds] vector.");
        Add("now", "The current local date and time as a serial date number (days since year 0).");
        Add("datenum", "Serial date number from year, month, day (optionally hour, minute, second), or a 3-/6-element vector.", P("year"), P("month"), P("day"), Opt("hour"), Opt("minute"), Opt("second"));
        Add("datestr", "Formats a datetime or a serial date number (default: now) as text.", Opt("when"), Opt("format"));
        Add("date", "The current local date as a 'dd-MMM-yyyy' string.");
        Add("time", "The current time as Unix epoch seconds (UTC), including a fractional part.");

        // --- The datetime and duration types (M64) ------------------------------------------------
        Add("datetime", "A point in time: no arguments for now, text to parse, or year/month/day (optionally hour/minute/second). Options: 'InputFormat', 'Format', 'TimeZone', 'ConvertFrom'.", Opt("year"), Opt("month"), Opt("day"), Opt("hour"), Opt("minute"), Opt("second"));
        Add("duration", "A length of time from hours, minutes and seconds (optionally milliseconds), a matrix of those rows, or text written hh:mm:ss.", P("hours"), Opt("minutes"), Opt("seconds"), Opt("milliseconds"));
        Add("NaT", "The missing datetime; NaT(m, n) makes an m-by-n array of them.", Opt("rows"), Opt("cols"));
        Add("seconds", "A duration of x seconds, or — handed a duration — how many seconds it is.", P("x"));
        Add("minutes", "A duration of x minutes, or how many minutes a duration is.", P("x"));
        Add("hours", "A duration of x hours, or how many hours a duration is.", P("x"));
        Add("days", "A duration of x days, or how many days a duration is.", P("x"));
        Add("years", "A duration of x average years (365.2425 days), or how many a duration is.", P("x"));
        Add("milliseconds", "A duration of x milliseconds, or how many milliseconds a duration is.", P("x"));
        Add("microseconds", "A duration of x microseconds, or how many microseconds a duration is.", P("x"));
        Add("nanoseconds", "A duration of x nanoseconds, or how many nanoseconds a duration is.", P("x"));

        // The calendar units (M82). A month is not a count of milliseconds, so these answer with a
        // calendarDuration — three components that stay apart, because adding a month then a day is
        // not the same moment as adding a day then a month.
        Add("caldays", "A calendarDuration of x days.", P("x"));
        Add("calweeks", "A calendarDuration of x weeks.", P("x"));
        Add("calmonths", "A calendarDuration of x months — a month, whatever length that month is.", P("x"));
        Add("calyears", "A calendarDuration of x years.", P("x"));
        Add("calquarters", "A calendarDuration of x quarters.", P("x"));
        Add("calendarDuration", "A calendar length from years, months and days (optionally hours, minutes, seconds).", P("years"), P("months"), P("days"), Opt("hours"), Opt("minutes"), Opt("seconds"));
        Add("caldiff", "The calendar differences between successive moments of a datetime.", P("t"), Opt("components"));
        Add("between", "The calendar duration between two datetimes, in the components asked for.", P("from"), P("to"), Opt("components"));
        Add("isdatetime", "True for a datetime.", P("value"));
        Add("isduration", "True for a duration.", P("value"));
        Add("iscalendarduration", "True for a calendarDuration.", P("value"));
        Add("isnat", "True for each element of a datetime that is NaT.", P("t"));

        // Time zones (M82). A zoned datetime stores the instant and reads as the wall clock its zone
        // shows, so these three are what a script asks about the lens rather than about the moment.
        Add("tzoffset", "The offset from UTC of each moment of a zoned datetime, as a duration.", P("t"));
        Add("isdst", "True for each moment of a zoned datetime that falls in daylight saving time.", P("t"));
        Add("timezones", "The time zone names this machine accepts, optionally filtered by a substring.", Opt("area"));

        // The field accessors, conversions and boundary moves (M64).
        Add("year", "The year of each moment of a datetime.", P("t"));
        Add("month", "The month (1-12) of each moment of a datetime.", P("t"));
        Add("day", "The day of the month of each moment of a datetime.", P("t"));
        Add("hour", "The hour (0-23) of each moment of a datetime.", P("t"));
        Add("minute", "The minute (0-59) of each moment of a datetime.", P("t"));
        Add("second", "The second of each moment, carrying its fractional part.", P("t"));
        Add("week", "The ISO week number of each moment of a datetime.", P("t"));
        Add("quarter", "The quarter (1-4) of each moment of a datetime.", P("t"));
        Add("weekday", "The day of the week, Sunday = 1, of each moment of a datetime.", P("t"));
        Add("ymd", "The year, month and day of a datetime: [y, m, d] = ymd(t).", P("t"));
        Add("hms", "The hour, minute and second of a datetime: [h, m, s] = hms(t).", P("t"));
        Add("datevec", "A datetime or serial date number as one [y m d h mi s] row per moment.", P("when"));
        Add("timeofday", "The duration since midnight of each moment of a datetime.", P("t"));
        Add("dateshift", "Moves each moment to the start or end of a unit: dateshift(t, 'start', 'month', offset?).", P("t"), P("where"), P("unit"), Opt("offset"));
        Add("isbetween", "True for each moment that falls within the two bounds, inclusive.", P("t"), P("lower"), P("upper"));
        Add("posixtime", "Seconds since 1970-01-01 UTC for each moment of a datetime.", P("t"));
        Add("exceltime", "The Excel serial date number for each moment of a datetime.", P("t"));
        Add("juliandate", "The Julian date for each moment of a datetime.", P("t"));
        Add("yyyymmdd", "Each moment of a datetime as the number yyyymmdd.", P("t"));
        Add("etime", "Seconds between two six-element clock vectors: etime(later, earlier).", P("later"), P("earlier"));
        Add("addtodate", "Adds a quantity of a named unit to a serial date number.", P("serial"), P("quantity"), P("unit"));
        Add("eomday", "The number of days in the given month of the given year.", P("year"), P("month"));
        Add("calendar", "A 6-by-7 grid of the month's day numbers, Sunday first, zero outside the month.", Opt("year"), Opt("month"));

        // --- The keyed collections (M64) ----------------------------------------------------------
        Add("containers", "The namespace holding containers.Map — a keyed collection with handle semantics, read and written as m(key).");
        Add("dictionary", "A keyed collection with value semantics: dictionary(keys, values), read and written as d(key).", Opt("keys"), Opt("values"));
        Add("isKey", "True for each key the collection holds.", P("collection"), P("key"));
        Add("keys", "The collection's keys — a cell for a containers.Map, an array for a dictionary.", P("collection"));
        Add("values", "The collection's values as a cell, or just the ones named by the second argument.", P("collection"), Opt("keys"));
        Add("remove", "Removes the named keys from a collection and hands it back.", P("collection"), P("keys"));
        Add("numEntries", "How many entries a collection holds.", P("collection"));
        Add("isConfigured", "True once a dictionary has entries, so its key and value types are known.", P("collection"));
        Add("lookup", "Reads keys from a collection, with an optional 'FallbackValue' for the missing ones.", P("collection"), P("keys"), Opt("'FallbackValue'"), Opt("fallback"));
        Add("insert", "Adds or replaces entries in a collection and hands it back.", P("collection"), P("keys"), P("values"));
        Add("entries", "The collection's entries as a cell of structs with Key and Value fields.", P("collection"));

        Add("mod", "Modulo x - floor(x/m)*m, element-wise over arrays (result takes m's sign).", P("x"), P("m"));
        Add("size", "The [rows, cols] of a matrix ([rows, cols, 3] for an RGB image); size(value, dim) returns one dimension.", P("value"), Opt("dim"));
        Add("height", "The number of rows: size(value, 1), and the row count of a table.", P("value"));
        Add("width", "The number of columns: size(value, 2), and the variable count of a table.", P("value"));
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
        Add("imread", "Reads an image file (PNG/JPEG/BMP/GIF/ICO/WEBP) into an image value; a second argument picks a frame.", P("path"), Opt("frame"));
        Add("imfinfo", "File facts about an image without loading it for use: Filename, FileSize, Format, Width, Height, BitDepth, ColorType.", P("path"));
        Add("imwrite", "Writes an image to a file; the extension (.png/.jpg/.bmp/.webp/.gif) selects the format. imwrite(X, map, path) writes an indexed picture. Options: 'Quality', 'BitDepth', 'Alpha', and for GIF 'WriteMode' ('overwrite'/'append'), 'DelayTime', 'LoopCount'.", P("image"), P("path"), Opt("options"));
        Add("imshow", "Displays an image with equal aspect and no axes decoration; a [low high] range (or []) sets the display window.", P("image"), Opt("range"));
        Add("im2double", "Converts an image to the double class, or scales a matrix into [0, 1].", P("image"));
        Add("im2single", "Converts an image to the single class.", P("image"));
        Add("im2uint8", "Converts an image to the uint8 class, so its samples read as 0–255.", P("image"));
        Add("im2uint16", "Converts an image to the uint16 class, so its samples read as 0–65535.", P("image"));
        Add("im2int16", "Converts an image to the int16 class, so its samples read as −32768–32767.", P("image"));
        Add("intlut", "Maps every sample of an integer-class image through a lookup table.", P("image"), P("table"));
        Add("otsuthresh", "Otsu's threshold level in [0, 1] from histogram counts alone.", P("counts"));
        Add("stretchlim", "The [low; high] intensity limits that clip a fraction of the histogram (default 1%).", P("image"), Opt("tolerance"));
        Add("adaptthresh", "A per-pixel threshold surface from local statistics. Options: 'NeighborhoodSize', 'Statistic', 'ForegroundPolarity'.", P("image"), Opt("sensitivity"), Opt("options"));
        Add("imdivide", "Divides one image by another, or by a constant, clamped to the class range.", P("a"), P("b"));
        Add("imabsdiff", "The absolute difference of two images.", P("a"), P("b"));
        Add("imlincomb", "A weighted sum of images: imlincomb(k1, A, k2, B), with an optional trailing constant.", P("weightsAndImages"));
        Add("imapplymatrix", "Mixes colour channels through a matrix, with optional per-channel offsets.", P("matrix"), P("image"), Opt("offsets"));
        Add("rgb2gray", "Converts an RGB image to grayscale (Rec.601 luma); a colormap becomes its grayscale equivalent.", P("imageOrMap"));
        Add("im2gray", "Returns a grayscale image: RGB is converted (Rec.601), grayscale is passed through.", P("image"));
        Add("mat2im", "Wraps a matrix as a grayscale image, clamping values to [0, 1].", P("matrix"));
        Add("mat2gray", "Scales a matrix to a grayscale image with min→0 and max→1, or over an explicit [amin amax] window.", P("matrix"), Opt("limits"));
        Add("im2mat", "Copies an image channel (default 1) to a nested-array matrix.", P("image"), Opt("channel"));
        Add("imadjust", "Maps intensities [lowIn,highIn]→[lowOut,highOut] with gamma; defaults stretch the 1–99% range.", P("image"), Opt("inRange"), Opt("outRange"), Opt("gamma"));
        Add("imhist", "Histogram bin counts of a grayscale image (default 256 bins); under MATLAB [counts, binLocations].", P("image"), Opt("bins"));
        Add("histeq", "Histogram-equalizes a grayscale image (default 64 levels), or matches a given target histogram; [J, T] also returns the mapping.", P("image"), Opt("binsOrHgram"));
        Add("graythresh", "Otsu's global threshold level in [0, 1]; under MATLAB [level, effectiveness].", P("image"));
        Add("imbinarize", "Thresholds an image to a logical one: a global level (Otsu by default), a threshold surface, or 'adaptive' with 'Sensitivity'.", P("image"), Opt("levelOrOptions"));
        Add("imadd", "Adds two images, or an image and a scalar, clamped to [0, 1].", P("a"), P("b"));
        Add("imsubtract", "Subtracts an image or scalar from an image, clamped to [0, 1].", P("a"), P("b"));
        Add("immultiply", "Multiplies two images sample by sample (or an image by a scalar), clamped to [0, 1] — masking.", P("a"), P("b"));
        Add("imcomplement", "Inverts image intensities across the class range.", P("image"));
        Add("imnoise", "Adds noise: 'gaussian' (mean, variance), 'localvar' (a variance image, or an intensity/variance curve), 'poisson', 'salt & pepper' (density), or 'speckle' (variance).", P("image"), Opt("type"), Opt("p1"), Opt("p2"));
        Add("imresize", "Resizes an image or matrix by a scale or to a [rows, cols] (one may be NaN); methods 'nearest', 'box', 'bilinear', 'bicubic' (default), 'lanczos2', 'lanczos3', with 'Antialiasing', 'Scale', and 'OutputSize'.", P("image"), Opt("scaleOrSize"), Opt("options"));
        Add("imrotate", "Rotates an image counter-clockwise by degrees; methods 'nearest' (default), 'bilinear', 'bicubic', and bounds 'crop'/'loose'.", P("image"), P("degrees"), Opt("method"), Opt("bbox"));
        Add("imcrop", "Crops a rectangle from an image. JGS takes [x, y, width, height] in 0-based pixels; MATLAB takes a spatial [xmin, ymin, width, height] and [J, rect] reports the one used.", P("image"), Opt("refOrRect"), Opt("rect"));
        Add("imfilter", "Filters an image or matrix with a kernel; 'corr'/'conv', 'same'/'full', 'replicate'/'symmetric'/'circular' or a pad value.", P("image"), P("kernel"), Opt("options"));
        Add("conv2", "2-D convolution; conv2(A, B, shape) or the separable conv2(u, v, A, shape). Shape 'full' (default), 'same', or 'valid'.", P("a"), P("b"), Opt("c"), Opt("shape"));
        Add("medfilt2", "Median filter over an [m, n] window (default 3×3); padding 'zeros' (default) or 'symmetric'.", P("image"), Opt("window"), Opt("padopt"));
        Add("fspecial", "Builds a filter kernel: average, gaussian, sobel, prewitt, laplacian, disk, log, motion, or unsharp.", P("type"), Opt("p1"), Opt("p2"));
        Add("edge", "Detects edges (binary image): 'sobel' (default), 'prewitt', 'roberts', 'canny', 'approxcanny', or 'log'. [BW, threshOut] reports the level used.", P("image"), Opt("method"), Opt("threshold"), Opt("sigmaOrDirection"));
        Add("imgradient", "Gradient magnitude and direction (degrees): [mag, dir]. Takes an image with a method — 'sobel', 'prewitt', 'roberts', 'central', 'intermediate' — or the components Gx and Gy.", P("image"), Opt("methodOrGy"));
        Add("imgradientxy", "Horizontal and vertical gradient components: [Gx, Gy]; method 'sobel' (default), 'prewitt', 'roberts', 'central', or 'intermediate'.", P("image"), Opt("method"));

        // M46 wave B — filtering, neighbourhood statistics, and block processing. Each of these takes
        // an image or a plain matrix and answers in kind.
        Add("padarray", "Extends an array with padding: a constant, or 'replicate'/'symmetric'/'circular', in direction 'both' (default), 'pre', or 'post'.", P("array"), P("padsize"), Opt("padval"), Opt("direction"));
        Add("imgaussfilt", "Gaussian smoothing with a separable kernel; sigma is one number or [sy sx]. Options: 'FilterSize', 'Padding', 'FilterDomain'.", P("image"), Opt("sigma"), Opt("options"));
        Add("imboxfilt", "Local mean over an odd [m, n] window, computed with running sums. Options: 'Padding', 'NormalizationFactor'.", P("image"), Opt("filterSize"), Opt("options"));
        Add("integralImage", "The summed-area table of a plane, one row and column larger than it; orientation 'upright' (default) or 'rotated'.", P("image"), Opt("orientation"));
        Add("integralBoxFilter", "Box filtering straight off an integral image, over the region where the window fits. Option: 'NormalizationFactor'.", P("integral"), Opt("filterSize"), Opt("options"));
        Add("ordfilt2", "The order-th smallest value in each neighbourhood, with optional per-position offsets; padding 'zeros' (default) or 'symmetric'.", P("array"), P("order"), P("domain"), Opt("offsets"), Opt("padopt"));
        Add("stdfilt", "The local standard deviation over a neighbourhood (default 3×3).", P("image"), Opt("nhood"));
        Add("rangefilt", "The local max minus min over a neighbourhood (default 3×3).", P("image"), Opt("nhood"));
        Add("entropyfilt", "The local entropy in bits over a neighbourhood (default 9×9).", P("image"), Opt("nhood"));
        Add("modefilt", "The most frequent value in each [m, n] window (default 3×3), ties going to the smallest.", P("image"), Opt("window"), Opt("padopt"));
        Add("wiener2", "Adaptive noise-removal filtering; [J, noise] also reports the noise power used.", P("image"), Opt("window"), Opt("noise"));
        Add("nlfilter", "Applies a function to every sliding [m, n] neighbourhood, one scalar per pixel.", P("array"), P("window"), P("fun"));
        Add("im2col", "Rearranges 'sliding' (default) or 'distinct' image blocks into the columns of a matrix.", P("array"), P("block"), Opt("kind"));
        Add("col2im", "Rebuilds a matrix from column-packed blocks; 'sliding' (default) or 'distinct'.", P("columns"), P("block"), P("size"), Opt("kind"));
        Add("colfilt", "Column-oriented block filtering: the function is handed every block at once as columns.", P("array"), P("block"), P("kind"), P("fun"));
        Add("bestblk", "A block size at most k (default 100) that divides the array as evenly as possible; [mb, nb] splits the pair.", P("size"), Opt("k"));
        Add("blockproc", "Applies a function to each distinct block, which arrives as a struct with data, blockSize, border, imageSize, and location. Options: 'BorderSize', 'TrimBorder', 'PadPartialBlocks', 'PadMethod'.", P("array"), P("block"), P("fun"), Opt("options"));
        // M46 wave C — geometric transforms. affine2d, projective2d, rigid2d and imref2d are MATLAB
        // classes; here they are structs whose Type field names the class, so class(tform) still
        // answers 'affine2d' and tform.T reads the same way.
        Add("affine2d", "An affine transform from a 3-by-3 T whose last column is [0; 0; 1]; no argument gives the identity.", Opt("t"));
        Add("projective2d", "A projective transform (homography) from a 3-by-3 T; no argument gives the identity.", Opt("t"));
        Add("rigid2d", "A rotation-and-translation transform, from a 3-by-3 T or from a 2-by-2 rotation and a [tx ty].", Opt("tOrRotation"), Opt("translation"));
        Add("imref2d", "The world coordinates an image occupies: a size with world limits, or with the size of one pixel.", Opt("imageSize"), Opt("xLimitsOrExtent"), Opt("yLimitsOrExtent"));
        Add("fitgeotrans", "Estimates a transform carrying moving points onto fixed ones: 'nonreflectivesimilarity', 'similarity', 'affine', or 'projective'.", P("movingPoints"), P("fixedPoints"), P("transformType"));
        Add("transformPointsForward", "Maps points through a transform: an n-by-2 array of [x y] rows, or separate x and y.", P("tform"), P("pointsOrX"), Opt("y"));
        Add("transformPointsInverse", "Maps points back through a transform: an n-by-2 array of [x y] rows, or separate x and y.", P("tform"), P("pointsOrX"), Opt("y"));
        Add("affineOutputView", "The imref2d a warp should target: 'BoundsStyle' 'CenterOutput' (default), 'FollowOutput', or 'SameAsInput'.", P("imageSize"), P("tform"), Opt("options"));
        Add("imwarp", "Applies a geometric transform to an image; [B, RB] also reports where the result sits. Options: 'OutputView', 'FillValues', 'SmoothEdges', and a method word.", P("image"), Opt("refOrTform"), Opt("tform"), Opt("options"));
        Add("imtranslate", "Shifts an image by [tx ty] pixels; [B, RB] also reports the frame. Options: 'OutputView' ('same' or 'full'), 'FillValues', 'Method'.", P("image"), Opt("refOrShift"), Opt("shift"), Opt("options"));
        Add("impyramid", "One level of a Gaussian pyramid: 'reduce' halves the image, 'expand' doubles it.", P("image"), P("direction"));
        Add("checkerboard", "A checkerboard test pattern: squares of n pixels, p tile rows by q tile columns, the right half grey.", Opt("squareSize"), Opt("rows"), Opt("cols"));

        // M46 wave D — colour. Every conversion takes a three-channel image or an n-by-3 colormap
        // and answers in the same shape.
        Add("rgb2hsv", "Converts RGB to hue, saturation and value, all in [0, 1].", P("rgb"));
        Add("hsv2rgb", "Converts hue, saturation and value back to RGB.", P("hsv"));
        Add("whitepoint", "The CIE XYZ of a standard illuminant: 'a', 'c', 'e', 'd50', 'd55', 'd65', or 'icc' (the default).", Opt("illuminant"));
        Add("rgb2xyz", "Converts RGB to CIE 1931 XYZ. Options: 'ColorSpace', 'WhitePoint'.", P("rgb"), Opt("options"));
        Add("xyz2rgb", "Converts CIE 1931 XYZ to RGB. Options: 'ColorSpace', 'WhitePoint', 'OutputType'.", P("xyz"), Opt("options"));
        Add("rgb2lab", "Converts RGB to CIE L*a*b*. Options: 'ColorSpace', 'WhitePoint'.", P("rgb"), Opt("options"));
        Add("lab2rgb", "Converts CIE L*a*b* to RGB. Options: 'ColorSpace', 'WhitePoint', 'OutputType'.", P("lab"), Opt("options"));
        Add("xyz2lab", "Converts CIE XYZ to CIE L*a*b*. Option: 'WhitePoint'.", P("xyz"), Opt("options"));
        Add("lab2xyz", "Converts CIE L*a*b* to CIE XYZ. Option: 'WhitePoint'.", P("lab"), Opt("options"));
        Add("rgb2lightness", "The L* channel alone — perceptual lightness with the colour taken out.", P("rgb"));
        Add("rgb2ycbcr", "Converts RGB to studio-swing Y'CbCr (BT.601), luma running 16 to 235.", P("rgb"));
        Add("ycbcr2rgb", "Converts studio-swing Y'CbCr back to RGB.", P("ycbcr"));
        Add("rgb2ntsc", "Converts RGB to the NTSC luminance and chrominance triple (YIQ).", P("rgb"));
        Add("ntsc2rgb", "Converts an NTSC YIQ triple back to RGB.", P("yiq"));
        Add("rgb2lin", "Undoes a colour space's transfer function, giving values proportional to light. Options: 'ColorSpace', 'OutputType'.", P("rgb"), Opt("options"));
        Add("lin2rgb", "Applies a colour space's transfer function to linear values. Options: 'ColorSpace', 'OutputType'.", P("linear"), Opt("options"));
        Add("chromadapt", "White-balances an image so the given illuminant comes out neutral. Options: 'ColorSpace', 'Method' ('bradford', 'vonkries', 'simple').", P("image"), P("illuminant"), Opt("options"));
        Add("illumgray", "The grey-world illuminant estimate, over the pixels left after trimming both tails. Options: 'Mask', 'Norm'.", P("image"), Opt("percentile"), Opt("options"));
        Add("illumwhite", "The white-patch illuminant estimate: the mean of the brightest pixels. Option: 'Mask'.", P("image"), Opt("topPercentile"), Opt("options"));
        Add("illumpca", "The principal-component illuminant estimate, built from the most strongly coloured pixels. Option: 'Mask'.", P("image"), Opt("percentage"), Opt("options"));
        Add("colorangle", "The angle in degrees between two RGB triples read as vectors.", P("rgb1"), P("rgb2"));
        Add("deltaE", "CIE76 colour difference between two images. Option: 'isInputLab'.", P("a"), P("b"), Opt("options"));
        Add("imcolordiff", "Colour difference by 'CIEDE2000' (default) or 'CIE94'. Options: 'Standard', 'isInputLab', 'kL', 'K1', 'K2'.", P("a"), P("b"), Opt("options"));
        Add("lab2double", "Decodes a uint8 or uint16 encoded L*a*b* array back to double.", P("lab"));
        Add("lab2uint8", "Encodes L*a*b* as uint8: L over 0 to 255, a and b offset by 128.", P("lab"));
        Add("lab2uint16", "Encodes L*a*b* as uint16.", P("lab"));
        Add("xyz2double", "Decodes a uint16 encoded XYZ array back to double.", P("xyz"));
        Add("xyz2uint16", "Encodes XYZ as uint16, with 1.0 at 32768.", P("xyz"));
        Add("gray2ind", "Converts a grayscale image to indices into a grey colormap: [X, map]; default 64 levels.", P("image"), Opt("levels"));
        Add("ind2gray", "Converts an indexed image and its colormap to grayscale.", P("indices"), P("map"));
        Add("ind2rgb", "Expands an indexed image through its colormap into an RGB image.", P("indices"), P("map"));
        Add("rgb2ind", "Reduces an RGB image to a palette: [X, map]. Give a colour count (median cut), a tolerance below one (uniform grid), or a colormap; 'dither' (default) or 'nodither'.", P("rgb"), Opt("colorsOrMap"), Opt("dither"));
        Add("imapprox", "Re-quantizes an indexed image over a smaller palette: [Y, newmap].", P("indices"), P("map"), P("colorsOrTolerance"), Opt("dither"));
        Add("cmap2gray", "Converts a colormap to its grayscale equivalent.", P("map"));
        Add("imsplit", "Splits a colour image into its channels: [R, G, B].", P("image"));
        Add("demosaic", "Reconstructs colour from a Bayer colour-filter array; alignment 'gbrg', 'grbg', 'bggr', or 'rggb'.", P("cfa"), P("sensorAlignment"));

        // M46 wave E — enhancement and denoising. The defaults MATLAB states against the image class
        // (0.01·range² for a degree of smoothing, 0.1·range for a gradient threshold) come out as one
        // number here, because images are carried on [0, 1] whatever class tag they wear.
        Add("adapthisteq", "Contrast-limited adaptive histogram equalization: equalizes each tile, then blends the tiles' mappings. Options: 'NumTiles', 'ClipLimit', 'NBins', 'Range', 'Distribution', 'Alpha'.", P("image"), Opt("options"));
        Add("imhistmatch", "Reshapes an image's histogram to resemble a reference's: [J, hgram]. Default 64 bins; 'Method' is 'uniform' or 'polynomial'.", P("image"), P("reference"), Opt("bins"), Opt("options"));
        Add("imflatfield", "Removes a smooth illumination gradient by dividing out a wide Gaussian blur. Option: 'FilterSize'.", P("image"), P("sigma"), Opt("mask"), Opt("options"));
        Add("decorrstretch", "Decorrelates and rescales an image's bands, pulling colour out of channels that nearly agree. Options: 'Mode', 'TargetMean', 'TargetSigma', 'Tol', 'SampleSubs'.", P("image"), Opt("options"));
        Add("imsharpen", "Unsharp masking: adds back what a Gaussian blur removed. Options: 'Radius', 'Amount', 'Threshold'.", P("image"), Opt("options"));
        Add("imbilatfilt", "Bilateral filtering — a Gaussian blur that will not cross an edge. Options: 'NeighborhoodSize', 'Padding'.", P("image"), Opt("degreeOfSmoothing"), Opt("spatialSigma"), Opt("options"));
        Add("imguidedfilter", "Guided filtering: smooths one image while borrowing another's edges; one argument guides itself. Options: 'NeighborhoodSize', 'DegreeOfSmoothing'.", P("image"), Opt("guide"), Opt("options"));
        Add("imdiffusefilt", "Anisotropic (Perona–Malik) diffusion. Options: 'GradientThreshold', 'NumberOfIterations', 'Connectivity', 'ConductionMethod'.", P("image"), Opt("options"));
        Add("imdiffuseest", "Suggests diffusion settings for an image: [gradThresh, numIter]. Options: 'Connectivity', 'ConductionMethod', 'NumberOfIterations'.", P("image"), Opt("options"));
        Add("imnlmfilt", "Non-local means: averages each pixel with distant pixels whose surroundings match. [B, estDoS] also reports the noise estimate. Options: 'DegreeOfSmoothing', 'SearchWindowSize', 'ComparisonWindowSize'.", P("image"), Opt("options"));
        Add("imreducehaze", "Removes atmospheric haze by the dark-channel prior: [B, T] also gives the transmission map. Options: 'Method', 'AtmosphericLight', 'ContrastEnhancement', 'BoostAmount'.", P("image"), Opt("amount"), Opt("options"));
        Add("imlocalbrighten", "Brightens dark regions — haze removal run on the negative: [B, T]. Option: 'AlphaBlend'.", P("image"), Opt("amount"), Opt("options"));
        Add("fibermetric", "Frangi vesselness: how tube-like each pixel is, taken over a range of fibre widths. Options: 'StructureSensitivity', 'ObjectPolarity'.", P("image"), Opt("thickness"), Opt("options"));
        Add("maxhessiannorm", "The largest Frobenius norm of the image Hessian at one scale; half of it is fibermetric's usual structure sensitivity.", P("image"), Opt("thickness"));

        // M46 wave F — morphology and distance. A structuring element is now a tagged struct with a
        // Neighborhood field rather than a bare matrix, and every operation that takes one still
        // accepts the matrix, which is what scripts written before this wave hand over.
        Add("strel", "Builds a structuring element: 'square', 'rectangle', 'disk', 'diamond', 'octagon', 'line', 'cube', 'cuboid', 'sphere', or a 0/1 neighbourhood matrix.", P("shape"), Opt("size"), Opt("angle"));
        Add("offsetstrel", "Builds a non-flat structuring element: 'ball' (radius, height) or 'offset' (a height matrix, -Inf outside).", P("shape"), Opt("radius"), Opt("height"));
        Add("conndef", "The default connectivity neighbourhood for a rank: 'minimal' (4 or 6) or 'maximal' (8 or 26).", P("rank"), Opt("type"));
        Add("iptcheckconn", "Errors unless the value is a valid connectivity — 1, 4, 8, 6, 18, 26, or a symmetric odd-sized 0/1 array.", P("conn"), Opt("caller"), Opt("variable"), Opt("position"));
        Add("imerode", "Morphological erosion (local minimum) over a structuring element (default 3×3 square).", P("image"), Opt("element"));
        Add("imdilate", "Morphological dilation (local maximum) over a structuring element, reflected about its origin.", P("image"), Opt("element"));
        Add("imopen", "Morphological opening (erode then dilate).", P("image"), Opt("element"));
        Add("imclose", "Morphological closing (dilate then erode).", P("image"), Opt("element"));
        Add("imtophat", "Top-hat transform: the image minus its own opening — small bright detail, whatever the background does.", P("image"), Opt("element"));
        Add("imbothat", "Bottom-hat transform: the closing minus the image — small dark detail.", P("image"), Opt("element"));
        Add("bwhitmiss", "Hit-or-miss: pixels matching one element in the foreground and another in the background, or a single 1/-1/0 interval.", P("image"), P("element"), Opt("background"));
        Add("imreconstruct", "Morphological reconstruction: grows the marker by dilation, never above the mask.", P("marker"), P("mask"), Opt("connectivity"));
        Add("imclearborder", "Removes whatever touches the image border (default 8-connectivity).", P("image"), Opt("connectivity"));
        Add("imhmax", "Suppresses maxima that rise less than h above their surroundings.", P("image"), P("h"), Opt("connectivity"));
        Add("imhmin", "Suppresses minima shallower than h.", P("image"), P("h"), Opt("connectivity"));
        Add("imextendedmax", "The regional maxima of the h-maxima transform: significant peaks as a mask.", P("image"), P("h"), Opt("connectivity"));
        Add("imextendedmin", "The regional minima of the h-minima transform.", P("image"), P("h"), Opt("connectivity"));
        Add("imregionalmax", "The regional maxima: connected plateaux with nothing higher beside them.", P("image"), Opt("connectivity"));
        Add("imregionalmin", "The regional minima.", P("image"), Opt("connectivity"));
        Add("imimposemin", "Forces the regional minima to sit exactly where the marker is, and nowhere else.", P("image"), P("marker"), Opt("connectivity"));
        Add("hough", "Hough line transform of a binary image: [accumulator, theta, rho].", P("image"));
        Add("houghpeaks", "The strongest peaks of a Hough accumulator, as 0-based [rhoIndex, thetaIndex] rows; pass base 1 for MATLAB numbering.", P("accumulator"), Opt("count"), Opt("threshold"), Opt("base"));
        Add("houghlines", "Line segments for the given Hough peaks, as a table of endpoints with Theta and Rho.", P("image"), P("theta"), P("rho"), P("peaks"), Opt("fillGap"), Opt("minLength"));
        Add("imfill","Fills holes — background not reachable from the border — or, given locations, the background regions containing them.", P("image"), Opt("locations"), Opt("connectivity"));
        Add("makelut", "Answers a rule for every possible 2×2 or 3×3 neighbourhood, giving a 16- or 512-entry lookup table.", P("function"), Opt("order"));
        Add("bwlookup", "Applies a neighbourhood lookup table to a binary image.", P("image"), P("lut"));
        Add("applylut", "Applies a neighbourhood lookup table to a binary image (the older name for bwlookup).", P("image"), P("lut"));
        Add("bwperim", "The perimeter pixels: foreground with at least one background neighbour.", P("image"), Opt("connectivity"));
        Add("bwmorph", "One of the named binary operations — 'skel', 'thin', 'clean', 'bridge', 'spur', 'majority', 'branchpoints' and the rest — repeated n times (Inf for until stable).", P("image"), P("operation"), Opt("n"));
        Add("bwskel", "Reduces objects to single-pixel strokes, pruning branches shorter than 'MinBranchLength'.", P("image"), Opt("options"));
        Add("bwulterode", "The ultimate erosion: the last points of each object to survive continued erosion.", P("image"), Opt("method"));
        Add("bwdist", "Distance to the nearest nonzero pixel: [D, idx]. Methods 'euclidean' (exact, default), 'cityblock', 'chessboard', 'quasi-euclidean'.", P("image"), Opt("method"));
        Add("bwdistgeodesic", "Geodesic distance from the seeds, travelling only inside the mask; unreachable pixels are Inf.", P("mask"), P("seeds"), Opt("method"));
        Add("graydist", "Gray-weighted distance: each step costs the average of the two samples it joins.", P("image"), P("seeds"), Opt("method"));

        // M46 wave G — segmentation, regions and ROI. regionprops answers with a struct array under
        // the MATLAB dialect and a Table under JGS, because only one of the two can hold a pixel list.
        Add("bwconncomp", "Connected components as a struct: Connectivity, ImageSize, NumObjects and PixelIdxList.", P("image"), Opt("connectivity"));
        Add("labelmatrix", "Turns a bwconncomp struct back into a label map.", P("components"));
        Add("label2idx", "The pixel indices of each label, as a cell array.", P("labels"));
        Add("bwarea", "The area of a binary image, weighting each 2×2 pattern so a diagonal edge measures its true length.", P("image"));
        Add("bweuler", "The Euler number: objects minus holes.", P("image"), Opt("connectivity"));
        Add("bwferet", "Feret measurements per object, as a table: MaxDiameter, MaxAngle, MinDiameter, MinAngle.", P("image"), Opt("properties"));
        Add("bwselect", "Keeps only the components containing the given pixels.", P("image"), P("columns"), Opt("rows"), Opt("connectivity"));
        Add("bwareafilt", "Keeps components by area: a count of the largest or smallest, or a [low high] range.", P("image"), P("countOrRange"), Opt("keep"), Opt("connectivity"));
        Add("bwpropfilt", "Keeps components by any regionprops measurement.", P("image"), P("property"), P("countOrRange"), Opt("keep"), Opt("connectivity"));
        Add("bwboundaries", "Traces every object outline (and hole): [B, L, n, A]. Options 'holes' or 'noholes'.", P("image"), Opt("connectivity"), Opt("mode"));
        Add("bwtraceboundary", "Traces one outline from a starting pixel and a compass direction.", P("image"), P("point"), P("direction"), Opt("connectivity"), Opt("maxPoints"), Opt("clockwise"));
        Add("boundarymask", "Pixels sitting on a border between two labels.", P("labels"), Opt("connectivity"));
        Add("bwconvhull", "The convex hull of every object ('objects') or of all of them together ('union').", P("image"), Opt("method"), Opt("connectivity"));
        Add("reducepoly", "Drops vertices a polyline can do without, by Ramer–Douglas–Peucker.", P("points"), Opt("tolerance"));
        Add("multithresh", "Otsu's method carried to N thresholds: [thresh, metric].", P("image"), Opt("levels"));
        Add("imquantize", "Assigns each sample the level its value falls in, numbering from 1; optional per-level values.", P("image"), P("thresholds"), Opt("values"));
        Add("grayslice", "Slices an intensity image into equal bands, numbering from 0.", P("image"), Opt("levels"));
        Add("watershed", "Watershed segmentation by flooding from every regional minimum; ridges are 0.", P("image"), Opt("connectivity"));
        Add("grayconnected", "Grows a region from a seed while the intensity stays within a tolerance.", P("image"), P("row"), P("column"), Opt("tolerance"));
        Add("gradientweight", "A weight image that is small where the gradient is large.", P("image"), Opt("sigma"));
        Add("graydiffweight", "A weight image that is small where the intensity is far from the seeds'.", P("image"), P("seeds"), Opt("column"));
        Add("imsegfmm", "Fast marching from seeds over a weight image: [BW, D].", P("weight"), P("seeds"), P("threshold"), Opt("more"));
        Add("imsegkmeans", "k-means over pixel colour: [L, centers]. Options: 'NormalizeInput', 'NumAttempts', 'MaxIterations'.", P("image"), P("clusters"), Opt("options"));
        Add("superpixels", "SLIC superpixels: [L, N]. Options: 'Compactness', 'Method' ('slic0' or 'slic'), 'NumIterations'.", P("image"), P("count"), Opt("options"));
        Add("activecontour", "Evolves a mask to the region the image says is there: 'Chan-Vese' (default) or 'edge'. Options: 'SmoothFactor', 'ContractionBias'.", P("image"), P("mask"), Opt("iterations"), Opt("options"));
        Add("poly2mask", "Rasterizes a polygon: a pixel joins the mask when its centre falls inside.", P("x"), P("y"), P("rows"), P("cols"));
        Add("poly2label", "Labels a picture by which of several polygons each pixel falls in.", P("polygons"), P("ids"), P("size"));
        Add("roipoly", "A polygon mask over an image; with no polygon, the whole picture.", P("image"), Opt("x"), Opt("y"));
        Add("roicolor", "Selects samples in an intensity range, or matching any of a set of values.", P("image"), P("lowOrValues"), Opt("high"));
        Add("roifilt2", "Puts a filtered version of a picture back only where a mask allows.", P("kernelOrImage"), P("imageOrMask"), P("maskOrFunction"));
        Add("regionfill", "Fills a region smoothly from its own boundary by solving Laplace's equation inside it.", P("image"), P("maskOrX"), Opt("y"));
        Add("label2rgb", "Colours a label map; background takes the zero colour, 'shuffle' reorders the palette.", P("labels"), Opt("colormap"), Opt("zeroColor"), Opt("order"));
        Add("labeloverlay", "Blends a label map over a picture. Options: 'Colormap', 'IncludedLabels', 'Transparency'.", P("image"), P("labels"), Opt("options"));
        Add("imoverlay", "Burns a binary mask into a picture in one flat colour.", P("image"), P("mask"), Opt("color"));
        Add("viscircles", "Draws circles on the current axes. Options: 'Color', 'LineWidth'.", P("centers"), P("radii"), Opt("options"));
        Add("visboundaries", "Draws region outlines on the current axes, from a mask or a set of boundaries.", P("maskOrBoundaries"), Opt("options"));
        Add("imfindcircles", "Circular Hough detection: [centers, radii, metric]. Options: 'ObjectPolarity', 'Sensitivity', 'EdgeThreshold'.", P("image"), P("radiusRange"), Opt("options"));
        Add("bwareaopen", "Removes connected components smaller than minArea pixels from a binary image; connectivity 4 or 8 (default 8).", P("image"), P("minArea"), Opt("connectivity"));
        Add("bwlabel","Labels connected components of a binary image: [labels, count]; connectivity 4 or 8 (default 8).", P("image"), Opt("connectivity"));
        Add("regionprops", "Per-region Area/Centroid/BoundingBox of a label or binary image, as a table (0-based pixel coordinates); an intensity image adds MeanIntensity and WeightedCentroid.", P("labels"), Opt("intensity"));
        Add("imcentroid", "The intensity-weighted centre [x, y] of a whole image (0-based pixel coordinates), optionally weighing only what a mask keeps.", P("image"), Opt("mask"));

        // M46 wave H — transforms and correlation. Each of these answers with plain numbers rather
        // than a picture: coefficients, projections and correlation surfaces are measurements about
        // an image, not images.
        Add("dct2", "The two-dimensional discrete cosine transform, padded or cropped to an optional size.", P("a"), Opt("rowsOrSize"), Opt("cols"));
        Add("idct2", "The inverse two-dimensional discrete cosine transform.", P("b"), Opt("rowsOrSize"), Opt("cols"));
        Add("dctmtx", "The n-by-n orthonormal DCT matrix, so that dct2(A) is D*A*D'.", P("n"));
        Add("radon", "Projects an image at each angle: [sinogram, xp]; the angles default to 0:179.", P("image"), Opt("theta"));
        Add("iradon", "Filtered backprojection: [image, filterResponse]. Words choose the interpolation ('nearest', 'linear') and the filter ('Ram-Lak', 'Shepp-Logan', 'Cosine', 'Hamming', 'Hann', 'none'); a number at most 1 is the frequency scaling and a larger one the output size.", P("sinogram"), P("theta"), Opt("options"));
        Add("fanbeam", "Fan-beam projections of a picture: [F, sensorPositions, rotationAngles] = fanbeam(I, D). D is the vertex-to-centre distance in pixels.", P("image"), P("D"), Opt("name"), Opt("value"));
        Add("ifanbeam", "Reconstructs a picture from fan-beam projections: I = ifanbeam(F, D). Takes iradon's Filter, Interpolation, FrequencyScaling and OutputSize as well.", P("F"), P("D"), Opt("name"), Opt("value"));
        Add("fan2para", "Rebins fan-beam projections as parallel-beam ones: [P, positions, angles] = fan2para(F, D).", P("F"), P("D"), Opt("name"), Opt("value"));
        Add("para2fan", "Rebins parallel-beam projections as fan-beam ones: [F, positions, angles] = para2fan(P, D).", P("P"), P("D"), Opt("name"), Opt("value"));
        Add("warp", "Draws a picture on a surface: warp(I), warp(Z, I), or warp(X, Y, Z, I).", Opt("x"), Opt("y"), Opt("z"), P("image"));
        Add("phantom", "The Shepp-Logan head phantom: [picture, ellipses]. Takes a name ('Shepp-Logan', 'Modified Shepp-Logan'), or your own six-column ellipse table.", Opt("definition"), Opt("n"));
        Add("qtdecomp", "Quadtree decomposition as a sparse map of block sizes; splits while a block's spread exceeds the threshold, or while your own test says so.", P("image"), Opt("thresholdOrFun"), Opt("sizeLimits"));
        Add("qtgetblk", "The blocks of a given size a quadtree found: [values, r, c], the values stacked as pages.", P("image"), P("sizes"), P("dim"));
        Add("qtsetblk", "Writes a stack of blocks back into the picture at the corners of the given size.", P("image"), P("sizes"), P("dim"), P("values"));
        Add("normxcorr2", "Normalized cross-correlation of a template against a picture; the peak is the match, and its value is bounded by one.", P("template"), P("image"));
        Add("imregcorr", "Registers two pictures by phase correlation: [tform, peak]. 'translation', 'rigid' or 'similarity' (the default).", P("moving"), P("fixed"), Opt("options"));

        // M46 wave I — filter design and deblurring. Design runs the opposite way round from
        // filtering: say what response you want and a kernel comes back. Deblurring runs it backwards
        // again, and every method differs only in how much it trusts the data where the blur left
        // nothing behind.
        Add("freqspace", "The frequency grid a response is sampled on: [f1, f2] for a size or an [m n] pair, or one vector on its own. 'meshgrid' returns them as matrices.", P("nOrSize"), Opt("meshgrid"));
        Add("freqz2", "The frequency response of a 2-D filter: [H, f1, f2]. Sizes default to 64 by 64; give two vectors instead to read named frequencies.", P("kernel"), Opt("n1OrF1"), Opt("n2OrF2"));
        Add("fsamp2", "Designs a 2-D FIR filter whose response matches the samples given, exactly at those points.", P("f1OrResponse"), Opt("f2"), Opt("response"), Opt("size"));
        Add("ftrans2", "Designs a 2-D FIR filter by mapping a 1-D filter's frequency axis onto the plane; the transform defaults to McClellan's.", P("b"), Opt("t"));
        Add("fwind1", "Designs a 2-D FIR filter from a sampled response and a 1-D window, turned about its centre — or two windows, multiplied out.", P("f1OrResponse"), P("windowOrF2"), Opt("window2OrResponse"), Opt("window"), Opt("window2"));
        Add("fwind2", "Designs a 2-D FIR filter from a sampled response and a 2-D window, which fixes the answer's size.", P("f1OrResponse"), P("windowOrF2"), Opt("response"), Opt("window"));
        Add("convmtx2", "The matrix that performs a convolution on a picture read out column by column.", P("kernel"), P("rowsOrSize"), Opt("cols"));
        Add("psf2otf", "The optical transfer function of a point spread function, padded to an optional size with its centre tap on zero frequency.", P("psf"), Opt("outSize"));
        Add("otf2psf", "The point spread function a transfer function stands for; psf2otf undone.", P("otf"), Opt("outSize"));
        Add("edgetaper", "Blurs a picture's borders into its own wrapped edges, so a deconvolution has no false seam to ring against.", P("image"), P("psf"));
        Add("deconvwnr", "Wiener deconvolution. The third argument is the noise-to-signal ratio (a number or a spectrum), or give the noise and signal autocorrelations as two.", P("image"), P("psf"), Opt("nsrOrNcorr"), Opt("icorr"));
        Add("deconvreg", "Regularized deconvolution: [image, lagrange]. Solves for the multiplier that fits the stated noise power, searching within an optional [low high] range.", P("image"), P("psf"), Opt("noisePower"), Opt("range"), Opt("regularizer"));
        Add("deconvlucy", "Richardson-Lucy deconvolution, accelerated: non-negative, brightness-preserving, ten iterations by default. Damping suppresses corrections smaller than the noise.", P("image"), P("psf"), Opt("iterations"), Opt("damping"), Opt("weight"), Opt("readout"));
        Add("deconvblind", "Blind deconvolution: [image, psf]. Improves the picture and the blur in turn, starting from a guess whose size is the largest blur that can be found.", P("image"), P("initialPsf"), Opt("iterations"), Opt("damping"), Opt("weight"), Opt("readout"));
        Add("gabor", "A Gabor filter, or a bank of them: a sinusoid of one wavelength and direction seen through a Gaussian window. 'SpatialFrequencyBandwidth', 'SpatialAspectRatio'.", P("wavelength"), P("orientation"), Opt("options"));
        Add("imgaborfilt", "Applies a Gabor filter to a picture: [magnitude, phase]. Takes a wavelength and a direction, or a bank built with gabor.", P("image"), P("wavelengthOrBank"), Opt("orientation"), Opt("options"));

        // M46 wave J — measurement, texture and composites. Everything here answers a question about
        // a picture rather than producing another one: how much it says, how close it is to another,
        // how its grey levels are arranged, and what two of them look like in one frame.
        Add("mean2", "The mean of every sample in a picture.", P("image"));
        Add("std2", "The standard deviation of every sample in a picture, normalized by n-1.", P("image"));
        Add("corr2", "The correlation coefficient between two same-size pictures; NaN when either is flat.", P("a"), P("b"));
        Add("entropy", "The Shannon entropy of a picture's histogram, in bits: 256 levels, or 2 for a mask.", P("image"));
        Add("immse", "The mean squared difference between two same-size pictures.", P("a"), P("b"));
        Add("psnr", "Peak signal-to-noise ratio in decibels: [peaksnr, snr]. The peak defaults to the largest value the class holds.", P("image"), P("reference"), Opt("peak"));
        Add("ssim", "Structural similarity: [score, map]. 'DynamicRange', 'Radius', 'Exponents', 'RegularizationConstants'.", P("image"), P("reference"), Opt("options"));
        Add("multissim", "Structural similarity down a pyramid: [score, maps]. 'NumScales', 'ScaleWeights', 'Sigma', 'DynamicRange'.", P("image"), P("reference"), Opt("options"));
        Add("dice", "The Dice overlap of two masks, or one value per label for two label maps.", P("a"), P("b"));
        Add("jaccard", "The Jaccard overlap of two masks, or one value per label for two label maps.", P("a"), P("b"));
        Add("bfscore", "Boundary F1 score against a truth: [score, precision, recall]. The tolerance defaults to 0.75% of the diagonal.", P("prediction"), P("truth"), Opt("threshold"));
        Add("graycomatrix", "Grey-level co-occurrence matrices: [glcms, scaled]. 'NumLevels', 'GrayLimits', 'Offset', 'Symmetric'.", P("image"), Opt("options"));
        Add("graycoprops", "Contrast, Correlation, Energy and Homogeneity read off a co-occurrence matrix, as a struct.", P("glcm"), Opt("properties"));
        Add("impixel", "The colour at each named point, as an n-by-3 list; a grey picture answers three times over.", P("image"), P("columns"), P("rows"));
        Add("improfile", "The samples along a path: [cx, cy, profile]. 'nearest' (the default), 'bilinear' or 'bicubic'.", P("image"), P("x"), P("y"), Opt("n"), Opt("method"));
        Add("imcontour", "Draws a picture's level lines on picture axes: square pixels, row one at the top.", P("image"), Opt("levelsOrCount"));
        Add("montage", "Lays pictures out in a grid and shows them. 'Size', 'BorderSize', 'BackgroundColor', 'DisplayRange', 'ThumbnailSize'.", P("images"), Opt("options"));
        Add("imfuse", "Combines two pictures into one: 'falsecolor' (the default), 'blend', 'diff' or 'montage'. 'Scaling', 'ColorChannels'.", P("a"), P("b"), Opt("method"), Opt("options"));
        Add("imshowpair", "Shows two pictures combined, with the same methods and options as imfuse.", P("a"), P("b"), Opt("method"), Opt("options"));
        Add("iptgetpref", "An image-processing preference, or a struct of all of them when called bare.", Opt("name"));
        Add("iptsetpref", "Sets an image-processing preference for the rest of the session.", P("name"), P("value"));

        // M46 wave K — volumes. A volume is a plain three-dimensional array, not an image with extra
        // channels: its third dimension is depth, so every filter here reaches through the stack
        // rather than treating each plane on its own, and connectivity becomes a real choice.
        Add("medfilt3", "A 3-D median filter, 3x3x3 by default. 'symmetric' (the default), 'replicate' or 'zeros'.", P("volume"), Opt("size"), Opt("padding"));
        Add("imgaussfilt3", "A separable 3-D Gaussian blur. 'FilterSize', 'Padding', 'FilterDomain'.", P("volume"), Opt("sigma"), Opt("options"));
        Add("imboxfilt3", "A 3-D box mean over an odd window. 'NormalizationFactor', 'Padding'.", P("volume"), Opt("size"), Opt("options"));
        Add("integralImage3", "The summed-area volume, one sample larger per axis, so any box sum costs eight lookups.", P("volume"));
        Add("integralBoxFilter3", "A box filter read off an integral volume. 'NormalizationFactor'.", P("integral"), Opt("size"), Opt("options"));
        Add("fspecial3", "A 3-D filter kernel: 'average', 'ellipsoid', 'gaussian', 'laplacian', 'log', 'prewitt' or 'sobel'.", P("type"), Opt("size"), Opt("parameter"));
        Add("imadjustn", "Remaps a volume's values, defaulting to its own 1% stretch limits.", P("volume"), Opt("inRange"), Opt("outRange"), Opt("gamma"));
        Add("imhistmatchn", "Remaps a volume so its histogram matches a reference volume's.", P("volume"), P("reference"), Opt("bins"));
        Add("edge3", "Finds surfaces in a volume: 'approxcanny' or 'Sobel', with a threshold or a [low high] pair.", P("volume"), P("method"), P("threshold"), Opt("sigma"));
        Add("imgradientxyz", "The three directional gradients: [Gx, Gy, Gz]. 'sobel', 'prewitt', 'central' or 'intermediate'.", P("volume"), Opt("method"));
        Add("imgradient3", "Gradient magnitude with azimuth and elevation in degrees: [Gmag, Gaz, Gel].", P("volume"), Opt("method"));
        Add("imresize3", "Resizes a volume by a factor or to a size. 'nearest', 'linear' (the default), 'cubic'; 'Antialiasing'.", P("volume"), P("scaleOrSize"), Opt("options"));
        Add("imrotate3", "Rotates a volume about an axis through its centre. 'crop' or 'loose'; 'FillValues'.", P("volume"), P("degrees"), P("axis"), Opt("options"));
        Add("imcrop3", "Cuts a box out of a volume, given as [x y z width height depth].", P("volume"), P("cuboid"));
        Add("obliqueslice", "The slice a plane cuts through a volume: [B, x, y, z]. 'OutputSize', 'Method', 'FillValues'.", P("volume"), P("point"), P("normal"), Opt("options"));
        Add("bwlabeln", "Labels the connected regions of a binary volume: [L, n]. Connectivity 6, 18 or 26.", P("mask"), Opt("connectivity"));
        Add("bwmorph3", "A 3-D neighbourhood operation: 'branchpoints', 'clean', 'endpoints', 'fill', 'majority' or 'remove'.", P("mask"), P("operation"));
        Add("bwselect3", "The regions of a volume containing the given seed voxels.", P("mask"), P("columns"), P("rows"), P("planes"), Opt("connectivity"));
        Add("regionprops3", "Measures the regions of a volume as a table: 'Volume', 'Centroid', 'PrincipalAxisLength', 'all'.", P("volume"), Opt("intensity"), Opt("properties"));
        Add("imsegkmeans3", "Clusters a volume's values with k-means: [L, centers]. 'MaxIterations'.", P("volume"), P("clusters"), Opt("options"));
        Add("superpixels3", "SLIC supervoxels: [L, count]. 'Compactness', 'NumIterations'.", P("volume"), P("count"), Opt("options"));
        Add("multissim3", "Multiscale structural similarity between two volumes: [score, maps]. 'NumScales', 'ScaleWeights', 'Sigma'.", P("volume"), P("reference"), Opt("options"));

        // --- Reductions and inspection ----------------------------------------------------------
        Add("length", "The number of elements in an array, or characters in a string.", P("value"));
        Add("sum", "The sum of a numeric array, over one dimension, several, or 'all'.", P("array"), Opt("dim"));
        Add("mean", "The arithmetic mean of a non-empty numeric array, or of every sample in an image.", P("array"));
        Add("min", "The smallest value: min(array), min(image), or min(a, b, ...).", P("values"));
        Add("max", "The largest value: max(array), max(image), or max(a, b, ...).", P("values"));
        Add("numel", "The number of elements in an array, or characters in a string (alias of length).", P("value"));

        // --- Statistics -------------------------------------------------------------------------
        Add("std", "Standard deviation: weight 0 (default) divides by n-1, 1 divides by n, a vector weights each value.", P("array"), Opt("weight"), Opt("dim"));
        Add("variance", "Variance: weight 0 (default) divides by n-1, 1 divides by n, a vector weights each value.", P("array"), Opt("weight"), Opt("dim"));
        Add("var", "Variance, MATLAB's spelling: var(x), var(x, 1), var(x, w), var(x, w, dim).", P("array"), Opt("weight"), Opt("dim"));
        Add("median", "Median of a non-empty numeric array.", P("array"));
        Add("mode", "Most frequent value of a non-empty numeric array (smallest wins ties).", P("array"));
        Add("percentile", "The p-th percentile (0-100) of a non-empty array, by linear interpolation.", P("array"), P("p"));
        Add("rms", "The root mean square, per slice along dim: rms(x), rms(x, dim), rms(x, 'all'), rms(x, 'omitnan').", P("array"), Opt("dim"));
        Add("bounds", "The smallest and largest together: [s, l] = bounds(x, dim), or bounds(x, 'all').", P("array"), Opt("dim"));
        Add("cov", "Covariance between columns: cov(A), cov(x, y), cov(..., 1) to divide by n, then 'omitrows' or 'partialrows'.", P("A"), Opt("B"), Opt("weight"), Opt("nanflag"));
        Add("corrcoef", "Correlation between columns: [r, p, rl, ru] = corrcoef(A, 'Alpha', 0.05, 'Rows', 'complete').", P("A"), Opt("B"), Opt("options"));
        Add("histcounts", "Values per bin: [n, edges, bin] = histcounts(x, nbins | edges, 'BinWidth', w, 'BinLimits', [a b], 'BinMethod', m, 'Normalization', how).", P("x"), Opt("bins"), Opt("options"));
        Add("balance", "Rows and columns scaled by powers of two so neither dominates: [T, B] = balance(A), where B is T \\ A * T.", P("A"), Opt("option"));
        Add("qz", "The generalized Schur form of a pencil: [AA, BB, Q, Z] = qz(A, B), with Q * A * Z = AA and Q * B * Z = BB.", P("A"), P("B"), Opt("form"));
        Add("ordqz", "A generalized Schur form reordered so the selected eigenvalues come first: ordqz(AA, BB, Q, Z, select).", P("AA"), P("BB"), P("Q"), P("Z"), P("select"));
        Add("spalloc", "An all-zero sparse matrix: spalloc(m, n) with an optional room-to-grow hint.", P("m"), P("n"), Opt("nz"));
        Add("speye", "A sparse identity: speye(n), speye(m, n), or speye([m n]).", P("n"), Opt("cols"));
        Add("nzmax", "How much room the stored entries take; here always the nonzero count itself.", P("S"));
        Add("symrcm", "The reverse Cuthill-McKee ordering, which gathers the nonzeros towards the diagonal.", P("S"));
        Add("amd", "A minimum-degree ordering, which keeps elimination fill small.", P("S"));
        Add("symamd", "A minimum-degree ordering of a symmetric matrix; the same ordering as amd here.", P("S"));
        Add("dissect", "A nested-dissection ordering: each half first, the separator between them last.", P("S"));
        Add("dmperm", "A row permutation putting nonzeros on the diagonal, from a maximum matching; 0 where a column has none.", P("S"));
        Add("etree", "The elimination tree: [parent, postorder] = etree(S), with 0 marking a root.", P("S"));
        Add("symbfact", "How many nonzeros each column of the Cholesky factor will hold, counted without forming it.", P("S"));
        Add("ichol", "Incomplete Cholesky with no fill: the factorization restricted to the pattern S already has.", P("S"));
        Add("ilu", "Incomplete LU with no fill: [L, U] = ilu(S), restricted to the pattern S already has.", P("S"));
        Add("kron", "The Kronecker product: every element of A times the whole of B, laid out in blocks.", P("A"), P("B"));
        Add("perms", "Every arrangement of a vector's values, one per row, in reverse lexicographic order.", P("v"));
        Add("factor", "The prime factors of a positive whole number, smallest first and repeated as they divide.", P("n"));
        Add("idivide", "Integer division that names its rounding: idivide(a, b, 'fix' | 'floor' | 'ceil' | 'round').", P("a"), P("b"), Opt("option"));
        Add("interp2", "Values read off a grid between its samples: interp2(V, Xq, Yq) or interp2(X, Y, V, Xq, Yq), 'linear' or 'nearest'.", P("first"), P("second"), P("third"), Opt("Xq"), Opt("Yq"), Opt("method"));
        Add("discretize", "Which bin each value falls in: [bin, edges] = discretize(x, edges | n, 'IncludedEdge', 'left' | 'right').", P("x"), P("bins"), Opt("values"), Opt("options"));
        Add("normalize", "Data centred and scaled: normalize(x, 'zscore' | 'norm' | 'range' | 'center' | 'scale' | 'medianiqr', setting), along dim.", P("x"), Opt("dim"), Opt("method"), Opt("setting"));
        Add("rescale", "The whole array stretched onto an interval: rescale(x), rescale(x, l, u), 'InputMin' and 'InputMax' to fix the source range.", P("x"), Opt("low"), Opt("high"), Opt("options"));
        Add("fillmissing", "Gaps filled: [f, tf] = fillmissing(x, 'linear' | 'previous' | 'next' | 'nearest' | 'constant' | 'movmean' | 'movmedian', setting).", P("x"), Opt("method"), Opt("setting"), Opt("options"));
        Add("rmmissing", "Missing entries dropped, or whole rows of a matrix: [r, tf] = rmmissing(x, dim, 'MinNumMissing', k).", P("x"), Opt("dim"), Opt("options"));
        Add("islocalmax", "Local maxima and their prominence: [tf, p] = islocalmax(x, 'MinProminence', p, 'MinSeparation', s, 'FlatSelection', how, 'MaxNumExtrema', n).", P("x"), Opt("dim"), Opt("options"));
        Add("islocalmin", "Local minima and their prominence, with the same options as islocalmax.", P("x"), Opt("dim"), Opt("options"));
        Add("smoothdata", "Smoothed data and the window it used: [b, k] = smoothdata(x, dim, 'movmean' | 'movmedian' | 'gaussian' | 'lowess' | 'loess' | 'rlowess' | 'rloess' | 'sgolay', k).", P("x"), Opt("dim"), Opt("method"), Opt("window"), Opt("options"));
        Add("groupsummary", "One summary per group: [b, g] = groupsummary(x, groups, method), or a table of groups when given a table.", P("data"), P("groups"), Opt("method"), Opt("datavars"));
        Add("sortrows", "Rows in order of whole columns: [b, i] = sortrows(a, cols, 'descend'); a negative column is descending.", P("a"), Opt("columns"), Opt("direction"));
        Add("gradient", "Numerical gradient by central differences: [fx, fy] = gradient(f, hx, hy).", P("f"), Opt("hx"), Opt("hy"));
        Add("trapz", "The area under sampled data by the trapezoid rule: trapz(y), trapz(x, y), trapz(y, dim), trapz(x, y, dim).", P("first"), Opt("y"), Opt("dim"));
        Add("cumtrapz", "The running area under sampled data, starting at zero: cumtrapz(y), cumtrapz(x, y), cumtrapz(x, y, dim).", P("first"), Opt("y"), Opt("dim"));
        Add("interp1", "Values between samples: interp1(x, v, xq, method, 'extrap'); methods 'linear' 'nearest' 'next' 'previous' 'pchip' 'spline'.", P("x"), P("v"), Opt("xq"), Opt("method"), Opt("extrapolation"));
        Add("polyfit", "The least-squares polynomial of degree n: [p, s, mu] = polyfit(x, y, n).", P("x"), P("y"), P("n"));
        Add("cumsum", "Running sums of a numeric array.", P("array"));
        Add("cumprod", "Running products of a numeric array.", P("array"));
        Add(
            "diff",
            "Adjacent differences of a numeric array (length n-1); n differences along a dimension.",
            P("array"),
            Opt("n"),
            Opt("dim"));

        // --- Descriptive and robust statistics (M53) ----------------------------------------------
        Add("prctile", "Percentiles by MATLAB's midpoint rule: prctile(x, p), prctile(A, p, dim), prctile(A, p, 'all').", P("array"), P("percent"), Opt("dim"));
        Add("quantile", "Quantiles at probabilities, or N evenly spaced ones: quantile(x, p), quantile(x, N), quantile(A, p, dim).", P("array"), P("p"), Opt("dim"));
        Add("skewness", "Skewness: skewness(x), skewness(x, 0) for the bias-corrected form, skewness(A, flag, dim).", P("array"), Opt("flag"), Opt("dim"));
        Add("kurtosis", "Kurtosis, 3 for a normal sample: kurtosis(x), kurtosis(x, 0), kurtosis(A, flag, dim).", P("array"), Opt("flag"), Opt("dim"));
        Add("moment", "The k-th central moment: moment(x, k), moment(A, k, dim).", P("array"), P("order"), Opt("dim"));
        Add("mad", "Absolute deviation: mad(x) about the mean, mad(x, 1) about the median, mad(A, flag, dim).", P("array"), Opt("flag"), Opt("dim"));
        Add("iqr", "The distance between the quartiles, of data or of a distribution object: r = iqr(x, 2) or iqr(pd).", P("x"), Opt("dim"));
        Add("trimmean", "The mean with a percentage trimmed from each tail: trimmean(x, pct, 'round' | 'floor' | 'weighted', dim).", P("array"), P("percent"), Opt("rule"), Opt("dim"));
        Add("geomean", "The geometric mean: geomean(x), geomean(A, dim), geomean(A, 'all'), geomean(x, 'omitnan').", P("array"), Opt("dim"));
        Add("harmmean", "The harmonic mean: harmmean(x), harmmean(A, dim), harmmean(A, 'all'), harmmean(x, 'omitnan').", P("array"), Opt("dim"));
        Add("zscore", "Standardized scores: [z, mu, sigma] = zscore(x), zscore(x, 1) to divide by n, zscore(A, flag, dim).", P("array"), Opt("flag"), Opt("dim"));
        Add("tiedrank", "Ranks with ties averaged: [r, tieadj] = tiedrank(x); tiedrank(x, 1) for the Wilcoxon correction, tiedrank(x, 0, 1) to rank from the outside in.", P("array"), Opt("tieflag"), Opt("bootflag"));
        Add("tabulate", "A frequency table: a row per value with its count and percentage.", P("array"));
        Add("crosstab", "Counts per combination of grouping values: [tbl, chi2, p, labels] = crosstab(x1, x2).", P("group1"), Opt("group2"), Opt("group3"));
        Add("grpstats", "Summary statistics by group: [means, sem, counts, names] = grpstats(X, group).", P("X"), Opt("group"));
        Add("corr", "Correlation between columns: [rho, p] = corr(X, Y, 'type', 'Pearson' | 'Kendall' | 'Spearman', 'rows', how, 'tail', side).", P("X"), Opt("Y"), Opt("options"));
        Add("partialcorr", "Correlation with other variables held fixed: [rho, p] = partialcorr(X), partialcorr(X, Z), partialcorr(X, Y, Z).", P("X"), Opt("Y"), Opt("Z"), Opt("options"));
        Add("partialcorri", "Correlation between each response and each predictor, holding the other predictors fixed: partialcorri(Y, X, Z).", P("Y"), P("X"), Opt("Z"), Opt("options"));
        Add("corrcov", "The correlation matrix a covariance matrix implies: [R, sigma] = corrcov(C).", P("C"));
        Add("nearcorr", "The nearest correlation matrix to a symmetric one: nearcorr(A, 'Tolerance', t, 'MaxIterations', n).", P("A"), Opt("options"));
        Add("ecdf", "The empirical distribution function, Kaplan-Meier when censored: [f, x, flo, fup] = ecdf(y, 'Censoring', c, 'Function', 'survivor').", P("y"), Opt("options"));
        Add("ecdfhist", "Histogram heights that agree with an empirical distribution: [n, c] = ecdfhist(f, x, bins).", P("f"), P("x"), Opt("bins"));
        Add("ksdensity", "A kernel-smoothed distribution: [f, xi] = ksdensity(x, pts, 'Kernel', k, 'Bandwidth', b, 'Support', s, 'Function', 'pdf').", P("x"), Opt("points"), Opt("options"));
        Add("nanmax", "The largest value, ignoring NaN (the legacy spelling of max(x, [], 'omitnan')).", P("array"), Opt("dim"));
        Add("nanmin", "The smallest value, ignoring NaN (the legacy spelling of min(x, [], 'omitnan')).", P("array"), Opt("dim"));
        Add("nanmean", "The mean, ignoring NaN (the legacy spelling of mean(x, 'omitnan')).", P("array"), Opt("dim"));
        Add("nanmedian", "The median, ignoring NaN (the legacy spelling of median(x, 'omitnan')).", P("array"), Opt("dim"));
        Add("nanstd", "The standard deviation, ignoring NaN (the legacy spelling of std(x, 'omitnan')).", P("array"), Opt("dim"));
        Add("nansum", "The sum, ignoring NaN (the legacy spelling of sum(x, 'omitnan')).", P("array"), Opt("dim"));
        Add("nanvar", "The variance, ignoring NaN (the legacy spelling of var(x, 'omitnan')).", P("array"), Opt("dim"));
        Add("nancov", "Covariance with incomplete observations dropped (the legacy spelling of cov(..., 'omitrows')).", P("A"), Opt("B"));


        // --- Continuous probability distributions (M53) ----------------------------------------
        Add("normpdf", "The normal density at x: normpdf(x, mu, sigma).", P("x"), Opt("mu"), Opt("sigma"));
        Add("normcdf", "The normal distribution function: normcdf(x, mu, sigma), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("mu"), Opt("sigma"), Opt("upper"));
        Add("norminv", "The normal quantile: norminv(p, mu, sigma).", P("p"), Opt("mu"), Opt("sigma"));
        Add("normrnd", "Normal random numbers: normrnd(mu, sigma), normrnd(mu, sigma, m, n) or normrnd(mu, sigma, [m n]).", P("mu"), P("sigma"), Opt("m"), Opt("n"));
        Add("normstat", "The mean and variance of a normal: [m, v] = normstat(mu, sigma).", Opt("mu"), Opt("sigma"));
        Add("normfit", "Fits a normal distribution by maximum likelihood: [muhat, sigmahat, muci, sigmaci] = normfit(x, alpha, censoring, freq).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("normlike", "The negative log-likelihood of a normal, and the asymptotic covariance of its estimates: [nlogL, avar] = normlike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("exppdf", "The exponential density at x: exppdf(x, mu). The parameter is the mean, not the rate.", P("x"), Opt("mu"));
        Add("expcdf", "The exponential distribution function: expcdf(x, mu), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("mu"), Opt("upper"));
        Add("expinv", "The exponential quantile: expinv(p, mu).", P("p"), Opt("mu"));
        Add("exprnd", "Exponential random numbers: exprnd(mu), exprnd(mu, m, n) or exprnd(mu, [m n]).", P("mu"), Opt("m"), Opt("n"));
        Add("expstat", "The mean and variance of a exponential: [m, v] = expstat(mu).", Opt("mu"));
        Add("expfit", "Fits a exponential distribution by maximum likelihood: [muhat, muci] = expfit(x, alpha, censoring, freq).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("explike", "The negative log-likelihood of a exponential, and the asymptotic covariance of its estimates: [nlogL, avar] = explike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("gampdf", "The gamma density at x: gampdf(x, a, b). The second parameter is the scale, so the mean is a*b.", P("x"), P("a"), Opt("b"));
        Add("gamcdf", "The gamma distribution function: gamcdf(x, a, b), or ..., 'upper' for the probability of exceeding x.", P("x"), P("a"), Opt("b"), Opt("upper"));
        Add("gaminv", "The gamma quantile: gaminv(p, a, b).", P("p"), P("a"), Opt("b"));
        Add("gamrnd", "Gamma random numbers: gamrnd(a, b), gamrnd(a, b, m, n) or gamrnd(a, b, [m n]).", P("a"), P("b"), Opt("m"), Opt("n"));
        Add("gamstat", "The mean and variance of a gamma: [m, v] = gamstat(a, b).", P("a"), Opt("b"));
        Add("gamfit", "Fits a gamma distribution by maximum likelihood: [phat, pci] = gamfit(data, alpha, censoring, freq).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("gamlike", "The negative log-likelihood of a gamma, and the asymptotic covariance of its estimates: [nlogL, avar] = gamlike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("betapdf", "The beta density at x: betapdf(x, a, b).", P("x"), P("a"), P("b"));
        Add("betacdf", "The beta distribution function: betacdf(x, a, b), or ..., 'upper' for the probability of exceeding x.", P("x"), P("a"), P("b"), Opt("upper"));
        Add("betainv", "The beta quantile: betainv(p, a, b).", P("p"), P("a"), P("b"));
        Add("betarnd", "Beta random numbers: betarnd(a, b), betarnd(a, b, m, n) or betarnd(a, b, [m n]).", P("a"), P("b"), Opt("m"), Opt("n"));
        Add("betastat", "The mean and variance of a beta: [m, v] = betastat(a, b).", P("a"), P("b"));
        Add("betafit", "Fits a beta distribution by maximum likelihood: [phat, pci] = betafit(data, alpha).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("betalike", "The negative log-likelihood of a beta, and the asymptotic covariance of its estimates: [nlogL, avar] = betalike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("chi2pdf", "The chi-square density at x: chi2pdf(x, v).", P("x"), P("v"));
        Add("chi2cdf", "The chi-square distribution function: chi2cdf(x, v), or ..., 'upper' for the probability of exceeding x.", P("x"), P("v"), Opt("upper"));
        Add("chi2inv", "The chi-square quantile: chi2inv(p, v).", P("p"), P("v"));
        Add("chi2rnd", "Chi-square random numbers: chi2rnd(v), chi2rnd(v, m, n) or chi2rnd(v, [m n]).", P("v"), Opt("m"), Opt("n"));
        Add("chi2stat", "The mean and variance of a chi-square: [m, v] = chi2stat(v).", P("v"));
        Add("tpdf", "The student's t density at x: tpdf(x, v).", P("x"), P("v"));
        Add("tcdf", "The student's t distribution function: tcdf(x, v), or ..., 'upper' for the probability of exceeding x.", P("x"), P("v"), Opt("upper"));
        Add("tinv", "The student's t quantile: tinv(p, v).", P("p"), P("v"));
        Add("trnd", "Student's t random numbers: trnd(v), trnd(v, m, n) or trnd(v, [m n]).", P("v"), Opt("m"), Opt("n"));
        Add("tstat", "The mean and variance of a student's t: [m, v] = tstat(v).", P("v"));
        Add("fpdf", "The f density at x: fpdf(x, v1, v2).", P("x"), P("v1"), P("v2"));
        Add("fcdf", "The f distribution function: fcdf(x, v1, v2), or ..., 'upper' for the probability of exceeding x.", P("x"), P("v1"), P("v2"), Opt("upper"));
        Add("finv", "The f quantile: finv(p, v1, v2).", P("p"), P("v1"), P("v2"));
        Add("frnd", "F random numbers: frnd(v1, v2), frnd(v1, v2, m, n) or frnd(v1, v2, [m n]).", P("v1"), P("v2"), Opt("m"), Opt("n"));
        Add("fstat", "The mean and variance of a f: [m, v] = fstat(v1, v2).", P("v1"), P("v2"));
        Add("unifpdf", "The uniform density at x: unifpdf(x, a, b).", P("x"), Opt("a"), Opt("b"));
        Add("unifcdf", "The uniform distribution function: unifcdf(x, a, b), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("a"), Opt("b"), Opt("upper"));
        Add("unifinv", "The uniform quantile: unifinv(p, a, b).", P("p"), Opt("a"), Opt("b"));
        Add("unifrnd", "Uniform random numbers: unifrnd(a, b), unifrnd(a, b, m, n) or unifrnd(a, b, [m n]).", P("a"), P("b"), Opt("m"), Opt("n"));
        Add("unifstat", "The mean and variance of a uniform: [m, v] = unifstat(a, b).", Opt("a"), Opt("b"));
        Add("unifit", "Fits a uniform distribution by maximum likelihood: [ahat, bhat, aci, bci] = unifit(data, alpha).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("lognpdf", "The lognormal density at x: lognpdf(x, mu, sigma). The parameters describe the logarithm of the variable, not the variable.", P("x"), Opt("mu"), Opt("sigma"));
        Add("logncdf", "The lognormal distribution function: logncdf(x, mu, sigma), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("mu"), Opt("sigma"), Opt("upper"));
        Add("logninv", "The lognormal quantile: logninv(p, mu, sigma).", P("p"), Opt("mu"), Opt("sigma"));
        Add("lognrnd", "Lognormal random numbers: lognrnd(mu, sigma), lognrnd(mu, sigma, m, n) or lognrnd(mu, sigma, [m n]).", P("mu"), P("sigma"), Opt("m"), Opt("n"));
        Add("lognstat", "The mean and variance of a lognormal: [m, v] = lognstat(mu, sigma).", Opt("mu"), Opt("sigma"));
        Add("lognfit", "Fits a lognormal distribution by maximum likelihood: [phat, pci] = lognfit(x, alpha, censoring, freq).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("lognlike", "The negative log-likelihood of a lognormal, and the asymptotic covariance of its estimates: [nlogL, avar] = lognlike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("wblpdf", "The weibull density at x: wblpdf(x, a, b). The scale comes first and the shape second.", P("x"), Opt("a"), Opt("b"));
        Add("wblcdf", "The weibull distribution function: wblcdf(x, a, b), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("a"), Opt("b"), Opt("upper"));
        Add("wblinv", "The weibull quantile: wblinv(p, a, b).", P("p"), Opt("a"), Opt("b"));
        Add("wblrnd", "Weibull random numbers: wblrnd(a, b), wblrnd(a, b, m, n) or wblrnd(a, b, [m n]).", P("a"), P("b"), Opt("m"), Opt("n"));
        Add("wblstat", "The mean and variance of a weibull: [m, v] = wblstat(a, b).", Opt("a"), Opt("b"));
        Add("wblfit", "Fits a weibull distribution by maximum likelihood: [phat, pci] = wblfit(x, alpha, censoring, freq).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("wbllike", "The negative log-likelihood of a weibull, and the asymptotic covariance of its estimates: [nlogL, avar] = wbllike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("evpdf", "The extreme value density at x: evpdf(x, mu, sigma). This is the smallest-extreme-value distribution, whose long tail runs left.", P("x"), Opt("mu"), Opt("sigma"));
        Add("evcdf", "The extreme value distribution function: evcdf(x, mu, sigma), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("mu"), Opt("sigma"), Opt("upper"));
        Add("evinv", "The extreme value quantile: evinv(p, mu, sigma).", P("p"), Opt("mu"), Opt("sigma"));
        Add("evrnd", "Extreme value random numbers: evrnd(mu, sigma), evrnd(mu, sigma, m, n) or evrnd(mu, sigma, [m n]).", P("mu"), P("sigma"), Opt("m"), Opt("n"));
        Add("evstat", "The mean and variance of a extreme value: [m, v] = evstat(mu, sigma).", Opt("mu"), Opt("sigma"));
        Add("evfit", "Fits a extreme value distribution by maximum likelihood: [phat, pci] = evfit(x, alpha, censoring, freq).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("evlike", "The negative log-likelihood of a extreme value, and the asymptotic covariance of its estimates: [nlogL, avar] = evlike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("gevpdf", "The generalized extreme value density at x: gevpdf(x, k, sigma, mu).", P("x"), P("k"), P("sigma"), P("mu"));
        Add("gevcdf", "The generalized extreme value distribution function: gevcdf(x, k, sigma, mu), or ..., 'upper' for the probability of exceeding x.", P("x"), P("k"), P("sigma"), P("mu"), Opt("upper"));
        Add("gevinv", "The generalized extreme value quantile: gevinv(p, k, sigma, mu).", P("p"), P("k"), P("sigma"), P("mu"));
        Add("gevrnd", "Generalized extreme value random numbers: gevrnd(k, sigma, mu), gevrnd(k, sigma, mu, m, n) or gevrnd(k, sigma, mu, [m n]).", P("k"), P("sigma"), P("mu"), Opt("m"), Opt("n"));
        Add("gevstat", "The mean and variance of a generalized extreme value: [m, v] = gevstat(k, sigma, mu).", P("k"), P("sigma"), P("mu"));
        Add("gevfit", "Fits a generalized extreme value distribution by maximum likelihood: [phat, pci] = gevfit(x, alpha).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("gevlike", "The negative log-likelihood of a generalized extreme value, and the asymptotic covariance of its estimates: [nlogL, avar] = gevlike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("gppdf", "The generalized pareto density at x: gppdf(x, k, sigma, theta).", P("x"), P("k"), Opt("sigma"), Opt("theta"));
        Add("gpcdf", "The generalized pareto distribution function: gpcdf(x, k, sigma, theta), or ..., 'upper' for the probability of exceeding x.", P("x"), P("k"), Opt("sigma"), Opt("theta"), Opt("upper"));
        Add("gpinv", "The generalized pareto quantile: gpinv(p, k, sigma, theta).", P("p"), P("k"), Opt("sigma"), Opt("theta"));
        Add("gprnd", "Generalized Pareto random numbers: gprnd(k, sigma, theta), gprnd(k, sigma, theta, m, n) or gprnd(k, sigma, theta, [m n]).", P("k"), P("sigma"), P("theta"), Opt("m"), Opt("n"));
        Add("gpstat", "The mean and variance of a generalized pareto: [m, v] = gpstat(k, sigma, theta).", P("k"), Opt("sigma"), Opt("theta"));
        Add("gpfit", "Fits a generalized pareto distribution by maximum likelihood: [phat, pci] = gpfit(x, alpha).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("gplike", "The negative log-likelihood of a generalized pareto, and the asymptotic covariance of its estimates: [nlogL, avar] = gplike(params, data).", P("params"), P("data"), Opt("censoring"), Opt("freq"));
        Add("raylpdf", "The rayleigh density at x: raylpdf(x, b).", P("x"), Opt("b"));
        Add("raylcdf", "The rayleigh distribution function: raylcdf(x, b), or ..., 'upper' for the probability of exceeding x.", P("x"), Opt("b"), Opt("upper"));
        Add("raylinv", "The rayleigh quantile: raylinv(p, b).", P("p"), Opt("b"));
        Add("raylrnd", "Rayleigh random numbers: raylrnd(b), raylrnd(b, m, n) or raylrnd(b, [m n]).", P("b"), Opt("m"), Opt("n"));
        Add("raylstat", "The mean and variance of a rayleigh: [m, v] = raylstat(b).", Opt("b"));
        Add("raylfit", "Fits a rayleigh distribution by maximum likelihood: [bhat, bci] = raylfit(data, alpha).", P("x"), Opt("alpha"), Opt("censoring"), Opt("freq"));
        Add("ncx2pdf", "The noncentral chi-square density at x: ncx2pdf(x, v, delta).", P("x"), P("v"), P("delta"));
        Add("ncx2cdf", "The noncentral chi-square distribution function: ncx2cdf(x, v, delta), or ..., 'upper' for the probability of exceeding x.", P("x"), P("v"), P("delta"), Opt("upper"));
        Add("ncx2inv", "The noncentral chi-square quantile: ncx2inv(p, v, delta).", P("p"), P("v"), P("delta"));
        Add("ncx2rnd", "Noncentral chi-square random numbers: ncx2rnd(v, delta), ncx2rnd(v, delta, m, n) or ncx2rnd(v, delta, [m n]).", P("v"), P("delta"), Opt("m"), Opt("n"));
        Add("ncx2stat", "The mean and variance of a noncentral chi-square: [m, v] = ncx2stat(v, delta).", P("v"), P("delta"));
        Add("ncfpdf", "The noncentral f density at x: ncfpdf(x, v1, v2, delta).", P("x"), P("v1"), P("v2"), P("delta"));
        Add("ncfcdf", "The noncentral f distribution function: ncfcdf(x, v1, v2, delta), or ..., 'upper' for the probability of exceeding x.", P("x"), P("v1"), P("v2"), P("delta"), Opt("upper"));
        Add("ncfinv", "The noncentral f quantile: ncfinv(p, v1, v2, delta).", P("p"), P("v1"), P("v2"), P("delta"));
        Add("ncfrnd", "Noncentral F random numbers: ncfrnd(v1, v2, delta), ncfrnd(v1, v2, delta, m, n) or ncfrnd(v1, v2, delta, [m n]).", P("v1"), P("v2"), P("delta"), Opt("m"), Opt("n"));
        Add("ncfstat", "The mean and variance of a noncentral f: [m, v] = ncfstat(v1, v2, delta).", P("v1"), P("v2"), P("delta"));
        Add("nctpdf", "The noncentral t density at x: nctpdf(x, v, delta).", P("x"), P("v"), P("delta"));
        Add("nctcdf", "The noncentral t distribution function: nctcdf(x, v, delta), or ..., 'upper' for the probability of exceeding x.", P("x"), P("v"), P("delta"), Opt("upper"));
        Add("nctinv", "The noncentral t quantile: nctinv(p, v, delta).", P("p"), P("v"), P("delta"));
        Add("nctrnd", "Noncentral t random numbers: nctrnd(v, delta), nctrnd(v, delta, m, n) or nctrnd(v, delta, [m n]).", P("v"), P("delta"), Opt("m"), Opt("n"));
        Add("nctstat", "The mean and variance of a noncentral t: [m, v] = nctstat(v, delta).", P("v"), P("delta"));

        Add("pdf", "The density of a named distribution: pdf('Normal', x, mu, sigma).", P("name"), P("x"), Opt("a"), Opt("b"), Opt("c"));
        Add("cdf", "The distribution function of a named distribution: cdf('Weibull', x, a, b), or ..., 'upper'.", P("name"), P("x"), Opt("a"), Opt("b"), Opt("c"));
        Add("icdf", "The quantile of a named distribution: icdf('Gamma', p, a, b).", P("name"), P("p"), Opt("a"), Opt("b"), Opt("c"));
        Add("random", "Random numbers from a named distribution: random('Beta', a, b, m, n).", P("name"), P("a"), Opt("b"), Opt("c"), Opt("m"), Opt("n"));
        Add("mle", "Maximum likelihood estimates: mle(data), mle(data, 'distribution', name), or mle(data, 'pdf', @f, 'start', p0), with 'Alpha', 'Censoring' and 'Frequency'.", P("data"), Opt("options"));

        // --- Discrete probability distributions (M53 wave D) ---------------------------------------
        Add("binopdf", "The binomial probability of x: binopdf(x, n, p).", P("x"), P("n"), P("p"));
        Add("binocdf", "The binomial distribution function: binocdf(x, n, p), or ..., 'upper' for the probability of exceeding x.", P("x"), P("n"), P("p"), Opt("upper"));
        Add("binoinv", "The binomial quantile — the least value whose probability has reached p: binoinv(p, n, p).", P("p"), P("n"), P("p"));
        Add("binornd", "Binomial random numbers: binornd(n, p), binornd(n, p, m, n) or binornd(n, p, [m n]).", P("n"), P("p"), Opt("m"), Opt("n"));
        Add("binostat", "The mean and variance of a binomial: [m, v] = binostat(n, p).", P("n"), P("p"));

        Add("poisspdf", "The Poisson probability of x: poisspdf(x, lambda).", P("x"), P("lambda"));
        Add("poisscdf", "The Poisson distribution function: poisscdf(x, lambda), or ..., 'upper' for the probability of exceeding x.", P("x"), P("lambda"), Opt("upper"));
        Add("poissinv", "The Poisson quantile — the least value whose probability has reached p: poissinv(p, lambda).", P("p"), P("lambda"));
        Add("poissrnd", "Poisson random numbers: poissrnd(lambda), poissrnd(lambda, m, n) or poissrnd(lambda, [m n]).", P("lambda"), Opt("m"), Opt("n"));
        Add("poisstat", "The mean and variance of a Poisson: [m, v] = poisstat(lambda).", P("lambda"));

        Add("geopdf", "The geometric probability of x: geopdf(x, p).", P("x"), P("p"));
        Add("geocdf", "The geometric distribution function: geocdf(x, p), or ..., 'upper' for the probability of exceeding x.", P("x"), P("p"), Opt("upper"));
        Add("geoinv", "The geometric quantile — the least value whose probability has reached p: geoinv(p, p).", P("p"), P("p"));
        Add("geornd", "Geometric random numbers: geornd(p), geornd(p, m, n) or geornd(p, [m n]).", P("p"), Opt("m"), Opt("n"));
        Add("geostat", "The mean and variance of a geometric: [m, v] = geostat(p).", P("p"));

        Add("hygepdf", "The hypergeometric probability of x: hygepdf(x, m, k, n).", P("x"), P("m"), P("k"), P("n"));
        Add("hygecdf", "The hypergeometric distribution function: hygecdf(x, m, k, n), or ..., 'upper' for the probability of exceeding x.", P("x"), P("m"), P("k"), P("n"), Opt("upper"));
        Add("hygeinv", "The hypergeometric quantile — the least value whose probability has reached p: hygeinv(p, m, k, n).", P("p"), P("m"), P("k"), P("n"));
        Add("hygernd", "Hypergeometric random numbers: hygernd(m, k, n), hygernd(m, k, n, m, n) or hygernd(m, k, n, [m n]).", P("m"), P("k"), P("n"), Opt("m"), Opt("n"));
        Add("hygestat", "The mean and variance of a hypergeometric: [m, v] = hygestat(m, k, n).", P("m"), P("k"), P("n"));

        Add("nbinpdf", "The negative binomial probability of x: nbinpdf(x, r, p).", P("x"), P("r"), P("p"));
        Add("nbincdf", "The negative binomial distribution function: nbincdf(x, r, p), or ..., 'upper' for the probability of exceeding x.", P("x"), P("r"), P("p"), Opt("upper"));
        Add("nbininv", "The negative binomial quantile — the least value whose probability has reached p: nbininv(p, r, p).", P("p"), P("r"), P("p"));
        Add("nbinrnd", "Negative binomial random numbers: nbinrnd(r, p), nbinrnd(r, p, m, n) or nbinrnd(r, p, [m n]).", P("r"), P("p"), Opt("m"), Opt("n"));
        Add("nbinstat", "The mean and variance of a negative binomial: [m, v] = nbinstat(r, p).", P("r"), P("p"));

        Add("unidpdf", "The discrete uniform probability of x: unidpdf(x, n).", P("x"), P("n"));
        Add("unidcdf", "The discrete uniform distribution function: unidcdf(x, n), or ..., 'upper' for the probability of exceeding x.", P("x"), P("n"), Opt("upper"));
        Add("unidinv", "The discrete uniform quantile — the least value whose probability has reached p: unidinv(p, n).", P("p"), P("n"));
        Add("unidrnd", "Discrete uniform random numbers: unidrnd(n), unidrnd(n, m, n) or unidrnd(n, [m n]).", P("n"), Opt("m"), Opt("n"));
        Add("unidstat", "The mean and variance of a discrete uniform: [m, v] = unidstat(n).", P("n"));

        Add("binofit", "The probability of success and its exact confidence interval: [phat, pci] = binofit(x, n, alpha), from x successes in n trials.", P("x"), P("n"), Opt("alpha"));
        Add("poissfit", "The mean count and its exact confidence interval: [lambdahat, lambdaci] = poissfit(data, alpha).", P("data"), Opt("alpha"));
        Add("nbinfit", "The negative binomial parameters by maximum likelihood: [phat, pci] = nbinfit(data, alpha).", P("data"), Opt("alpha"), Opt("options"));
        Add("mnpdf", "The multinomial probability of each row of counts: mnpdf(x, p).", P("x"), P("p"));
        Add("mnrnd", "Multinomial random counts: mnrnd(n, p) or mnrnd(n, p, m).", P("n"), P("p"), Opt("m"));

        // --- Multivariate distributions, sampling and resampling (M53 wave E) -----------------------
        Add("cholcov", "A factor T with T'*T = SIGMA — the Cholesky factor, or a shorter one where the covariance is singular: [T, num] = cholcov(SIGMA).", P("sigma"), Opt("flag"));
        Add("mvnpdf", "The multivariate normal density of each row: mvnpdf(X, Mu, Sigma).", P("X"), Opt("Mu"), Opt("Sigma"));
        Add("mvncdf", "The multivariate normal probability below each row, or inside a box: mvncdf(X, mu, sigma) or mvncdf(xl, xu, mu, sigma).", P("X"), Opt("mu"), Opt("sigma"), Opt("more"));
        Add("mvnrnd", "Multivariate normal random rows: mvnrnd(mu, sigma) or mvnrnd(mu, sigma, n).", P("mu"), P("sigma"), Opt("n"));
        Add("mvtpdf", "The multivariate t density of each row: mvtpdf(X, C, df).", P("X"), P("C"), P("df"));
        Add("mvtcdf", "The multivariate t probability below each row, or inside a box: mvtcdf(X, C, df) or mvtcdf(xl, xu, C, df).", P("X"), P("C"), P("df"), Opt("more"));
        Add("mvtrnd", "Multivariate t random rows: mvtrnd(C, df) or mvtrnd(C, df, n).", P("C"), P("df"), Opt("n"));
        Add("wishrnd", "A Wishart random matrix with mean df*sigma: [W, D] = wishrnd(sigma, df, D).", P("sigma"), P("df"), Opt("D"));
        Add("iwishrnd", "An inverse Wishart random matrix: [W, DI] = iwishrnd(tau, df, DI).", P("tau"), P("df"), Opt("DI"));
        Add("mvksdensity", "A multivariate kernel density estimate at the given points: mvksdensity(x, pts, 'Bandwidth', bw).", P("x"), P("pts"), P("option"), P("value"));
        Add("randsample", "Values drawn from a population, or from the integers up to n: randsample(population, k, replace, w).", P("population"), P("k"), Opt("replace"), Opt("w"));
        Add("datasample", "Observations drawn from data along a dimension: [y, idx] = datasample(data, k, dim, 'Replace', false, 'Weights', w).", P("data"), P("k"), Opt("dim"), Opt("options"));
        Add("randg", "Gamma random numbers of unit scale: randg(A), randg(A, m, n) or randg(A, [m n]).", Opt("A"), Opt("m"), Opt("n"));
        Add("lhsdesign", "A Latin hypercube design of n points in p variables: lhsdesign(n, p, 'criterion', 'maximin', 'iterations', 5, 'smooth', 'off').", P("n"), P("p"), Opt("options"));
        Add("lhsnorm", "A multivariate normal sample whose every marginal is stratified: [X, Z] = lhsnorm(mu, sigma, n, 'off').", P("mu"), P("sigma"), P("n"), Opt("smooth"));
        Add("bootstrp", "A statistic recomputed on nboot resamples of the rows: [bootstat, bootsam] = bootstrp(nboot, bootfun, d1).", P("nboot"), P("bootfun"), P("d1"), Opt("more"));
        Add("bootci", "A bootstrap confidence interval, two rows: bootci(nboot, bootfun, d1) or bootci(nboot, {bootfun, d1}, 'alpha', 0.05, 'type', 'bca').", P("nboot"), P("bootfun"), P("d1"), Opt("more"));
        Add("jackknife", "A statistic recomputed with each observation left out: jackknife(jackfun, X).", P("jackfun"), P("X"), Opt("more"));
        Add("combnk", "Every way of choosing k of the values, one combination per row: combnk(v, k).", P("v"), P("k"));

        // --- Hypothesis tests and analysis of variance (M53 wave F) ---------------------------------
        Add("ttest", "Whether a normal sample's mean is m, or two paired samples differ: [h, p, ci, stats] = ttest(x, m, 'Alpha', 0.05, 'Tail', 'both', 'Dim', 1).", P("x"), Opt("m"), Opt("options"));
        Add("ttest2", "Whether two independent samples have the same mean: [h, p, ci, stats] = ttest2(x, y, 'Vartype', 'unequal') for Welch's test.", P("x"), P("y"), Opt("options"));
        Add("ztest", "The one-sample mean test with a known standard deviation: [h, p, ci, stats] = ztest(x, m, sigma, 'Tail', 'right').", P("x"), P("m"), P("sigma"), Opt("options"));
        Add("vartest", "Whether a normal sample's variance is v: [h, p, ci, stats] = vartest(x, v, 'Alpha', 0.05, 'Tail', 'both').", P("x"), P("v"), Opt("options"));
        Add("vartest2", "Whether two samples share a variance, through the ratio of the two estimates: [h, p, ci, stats] = vartest2(x, y).", P("x"), P("y"), Opt("options"));
        Add("vartestn", "Whether several groups share a variance: [p, stats] = vartestn(X, 'TestType', 'BrownForsythe') or vartestn(x, group).", P("X"), Opt("group"), Opt("options"));
        Add("kstest", "Whether a sample came from a fully specified distribution: [h, p, ksstat, cv] = kstest(x, 'CDF', cdf, 'Tail', 'unequal').", P("x"), Opt("options"));
        Add("kstest2", "Whether two samples came from the same distribution: [h, p, ks2stat] = kstest2(x1, x2, 'Alpha', 0.05, 'Tail', 'unequal').", P("x1"), P("x2"), Opt("options"));
        Add("lillietest", "The Kolmogorov-Smirnov test where the parameters were estimated from the same sample: [h, p, kstat, critval] = lillietest(x, 'Distr', 'norm').", P("x"), Opt("options"));
        Add("adtest", "Anderson and Darling's test, which weights the tails: [h, p, adstat, cv] = adtest(x, 'Distribution', 'exp').", P("x"), Opt("options"));
        Add("jbtest", "Whether a sample's skewness and kurtosis are the normal distribution's: [h, p, jbstat, critval] = jbtest(x, alpha).", P("x"), Opt("alpha"));
        Add("chi2gof", "The binned goodness-of-fit test: [h, p, stats] = chi2gof(x, 'NBins', 10, 'CDF', @fun, 'EMin', 5).", P("x"), Opt("options"));
        Add("runstest", "Whether a sequence alternates about its median the way an independent one would: [h, p, stats] = runstest(x, v, 'Method', 'exact').", P("x"), Opt("v"), Opt("options"));
        Add("ranksum", "Wilcoxon's rank sum test of two independent samples: [p, h, stats] = ranksum(x, y, 'tail', 'right', 'method', 'exact').", P("x"), P("y"), Opt("options"));
        Add("signrank", "Wilcoxon's signed rank test of matched pairs: [p, h, stats] = signrank(x, y) or signrank(x, m).", P("x"), Opt("y"), Opt("options"));
        Add("signtest", "The sign test of matched pairs: [p, h, stats] = signtest(x, y) or signtest(x, m).", P("x"), Opt("y"), Opt("options"));
        Add("ansaribradley", "Whether two samples are equally dispersed: [h, p, stats] = ansaribradley(x, y, 'tail', 'both').", P("x"), P("y"), Opt("options"));
        Add("barttest", "How many principal components a set of variables needs: [ndim, prob, chisquare] = barttest(x, alpha).", P("x"), Opt("alpha"));
        Add("fishertest", "Fisher's exact test of a two-by-two table of counts: [h, p, stats] = fishertest(x, 'Tail', 'right').", P("x"), Opt("options"));
        Add("dwtest", "Whether a model's residuals are correlated with their neighbours: [p, d] = dwtest(r, x, 'Method', 'exact', 'Tail', 'both').", P("r"), P("x"), Opt("options"));
        Add("linhyptest", "Whether a linear combination of coefficients takes a stated value: [p, F, r] = linhyptest(beta, COVB, c, H, dfe).", P("beta"), P("COVB"), Opt("c"), Opt("H"), Opt("dfe"));
        Add("sampsizepwr", "Whichever of the effect, the power and the sample size was left out as []: n = sampsizepwr('t', [mu0 sigma], mu1, 0.8).", P("testtype"), P("p0"), P("p1"), Opt("power"), Opt("n"), Opt("options"));
        Add("anova1", "One-way analysis of variance: [p, tbl, stats] = anova1(X) over a matrix's columns, or anova1(x, group).", P("X"), Opt("group"), Opt("displayopt"));
        Add("anova2", "Two-way analysis of a balanced grid: [p, tbl, stats] = anova2(X, reps), with p in the order columns, rows, interaction.", P("X"), Opt("reps"), Opt("displayopt"));
        Add("anovan", "Analysis with any number of crossed factors: [p, tbl, stats] = anovan(y, group, 'model', 'interaction', 'sstype', 3, 'varnames', names).", P("y"), P("group"), Opt("options"));
        Add("manova1", "Whether several groups share a mean vector, answered as the dimension their means span: [d, p, stats] = manova1(X, group, alpha).", P("X"), P("group"), Opt("alpha"));
        Add("kruskalwallis", "The one-way analysis asked of the ranks: [p, tbl, stats] = kruskalwallis(X) or kruskalwallis(x, group).", P("X"), Opt("group"), Opt("displayopt"));
        Add("friedman", "The two-way analysis asked of ranks taken within each block: [p, tbl, stats] = friedman(X, reps).", P("X"), Opt("reps"), Opt("displayopt"));
        Add("multcompare", "Every pair of estimates compared at a family-wide level: [c, m, h, gnames] = multcompare(stats, 'CType', 'bonferroni', 'Alpha', 0.05).", P("stats"), Opt("options"));

        // --- Regression (M53 wave G) ----------------------------------------------------------------
        Add("regress", "Multiple linear regression against a design you built, intercept column and all: [b, bint, r, rint, stats] = regress(y, X, alpha).", P("y"), P("X"), Opt("alpha"));
        Add("regstats", "One fit described every way: stats = regstats(y, X, 'quadratic') or regstats(y, X, 'linear', {'beta','cookd'}).", P("y"), P("X"), Opt("model"), Opt("whichstats"));
        Add("leverage", "How far each observation pulls its own fitted value: h = leverage(data, 'linear').", P("data"), Opt("model"));
        Add("ridge", "Least squares with the size of the coefficients penalized: b = ridge(y, X, k, scaled), one column per ridge parameter.", P("y"), P("X"), P("k"), Opt("scaled"));
        Add("x2fx", "The design matrix a model description names: D = x2fx(X, 'quadratic', categ) or a matrix of exponents.", P("X"), Opt("model"), Opt("categ"), Opt("catlevels"));
        Add("dummyvar", "An indicator column for every level of every grouping column: D = dummyvar(group).", P("group"));
        Add("polyconf", "A polynomial evaluated with an interval around it: [y, delta] = polyconf(p, x, S, 'alpha', 0.05, 'predopt', 'curve').", P("p"), P("x"), Opt("S"), Opt("options"));
        Add("invpred", "The x at which a straight-line fit would have produced y0: [x0, dxlo, dxup] = invpred(x, y, y0, 'predopt', 'curve').", P("x"), P("y"), P("y0"), Opt("options"));
        Add("robustfit", "Least squares that gives up on outliers: [b, stats] = robustfit(X, y, 'bisquare', tune, 'on').", P("X"), P("y"), Opt("wfun"), Opt("tune"), Opt("const"));
        Add("glmfit", "A linear model for a counted, proportioned or positive response: [b, dev, stats] = glmfit(X, y, 'poisson', 'link', 'log').", P("X"), P("y"), Opt("distr"), Opt("options"));
        Add("glmval", "What a generalized fit predicts, with its interval: [yhat, dlo, dhi] = glmval(b, X, 'logit', stats, 'Confidence', 0.95).", P("b"), P("X"), P("link"), Opt("stats"), Opt("options"));
        Add("stepwisefit", "Which predictors belong in the model, found by adding and removing them: [b, se, pval, inmodel, stats, nextstep, history] = stepwisefit(X, y, 'penter', 0.05).", P("X"), P("y"), Opt("options"));
        Add("nlinfit", "A model of any shape fitted by least squares: [beta, R, J, CovB, MSE] = nlinfit(X, y, @model, beta0, options).", P("X"), P("y"), P("modelfun"), P("beta0"), Opt("options"), Opt("options"));
        Add("nlparci", "An interval around each parameter of a nonlinear fit: ci = nlparci(beta, R, 'jacobian', J, 'alpha', 0.05).", P("beta"), P("resid"), Opt("options"));
        Add("nlpredci", "What a nonlinear fit predicts at new rows, with its interval: [y, delta] = nlpredci(@model, X, beta, R, 'Jacobian', J).", P("modelfun"), P("X"), P("beta"), P("R"), Opt("options"));
        Add("hougen", "The Hougen-Watson reaction rate, the documented nonlinear example: y = hougen(beta, x).", P("beta"), P("x"));
        Add("lasso", "The whole path of penalized fits, from keeping everything to keeping nothing: [B, FitInfo] = lasso(X, y, 'Alpha', 1, 'NumLambda', 100).", P("X"), P("y"), Opt("options"));
        Add("lassoglm", "The penalized path for a response the squared-error loss does not describe: [B, FitInfo] = lassoglm(X, y, 'binomial', 'Alpha', 0.5).", P("X"), P("y"), Opt("distr"), Opt("options"));
        Add("plsregress", "Regression through directions chosen to explain the response: [XL, YL, XS, YS, BETA, PCTVAR, MSE, stats] = plsregress(X, Y, ncomp).", P("X"), P("Y"), Opt("ncomp"), Opt("options"));
        Add("mnrfit", "A logistic regression for a response of several categories: [B, dev, stats] = mnrfit(X, Y, 'model', 'ordinal', 'link', 'probit').", P("X"), P("Y"), Opt("options"));
        Add("mnrval", "The probability of each category at each row: [p, dlo, dhi] = mnrval(B, X, stats, 'type', 'cumulative').", P("B"), P("X"), Opt("stats"), Opt("options"));
        Add("mvregress", "Several responses fitted together so their errors may be correlated: [beta, Sigma, E, CovB, logL] = mvregress(X, Y).", P("X"), P("Y"), Opt("options"));
        Add("mvregresslike", "How improbable the data is at stated multivariate parameters: [nlogL, COVB] = mvregresslike(X, Y, beta, Sigma).", P("X"), P("Y"), P("beta"), P("Sigma"), Opt("algorithm"), Opt("options"));
        Add("coxphfit", "How predictors multiply the rate of failure, without saying what that rate is: [b, logl, H, stats] = coxphfit(X, T, 'censoring', c, 'ties', 'efron').", P("X"), P("T"), Opt("options"));

        // --- Designs, plot verbs, files and utilities (M53 wave J) -----------------------------------
        Add("fullfact", "Every combination of the levels of each factor, one run per row: d = fullfact([2 3 3]).", P("levels"));
        Add("ff2n", "The two-level full factorial over n factors, coded zero and one: d = ff2n(4).", P("n"));
        Add("fracfact", "A two-level fraction from its generators, and what its effects are confounded with: [X, conf] = fracfact('a b c abc', 'MaxInt', 3).", P("gen"), Opt("options"));
        Add("fracfactgen", "Generators for a fraction of the named factors at a wanted resolution: g = fracfactgen('a b c d e', 4, 4).", P("terms"), Opt("k"), Opt("R"));
        Add("bbdesign", "The Box-Behnken design of n factors: [d, blocks] = bbdesign(3, 'center', 5).", P("n"), Opt("options"));
        Add("ccdesign", "The central composite design of n factors: [d, blocks] = ccdesign(3, 'type', 'faced').", P("n"), Opt("options"));
        Add("capability", "How well a process meets its specification: S = capability(data, [lower upper]).", P("data"), P("specs"));
        Add("capaplot", "The fitted distribution drawn against the specification: [p, h] = capaplot(data, specs).", P("data"), P("specs"));
        Add("normspec", "The same picture from a stated mean and deviation: [p, h] = normspec(specs, mu, sigma).", P("specs"), Opt("mu"), Opt("sigma"));
        Add("gagerr", "How much of the spread in a set of measurements is the measuring: [sd, tbl, stats] = gagerr(y, {part, op}, 'model', 'linear').", P("y"), P("group"), Opt("options"));
        Add("statset", "The settings structure the iterative names take: opts = statset('MaxIter', 500).", Opt("options"));
        Add("statget", "One setting out of that structure, or a fallback: n = statget(opts, 'MaxIter', 100).", P("options"), P("name"), Opt("default"));

        // M99: optimfun. The first names here that MATLAB documents as functions rather than
        // builtins, and the first of the toolbox-function arc.
        Add("fminsearch", "A local minimum of a function of several variables, by the Nelder-Mead simplex and no derivative: [x, fval] = fminsearch(@(v) norm(v), [1; 2]).", P("fun"), P("x0"), Opt("options"));
        Add("fminbnd", "The minimum of a function of one variable inside an interval: [x, fval] = fminbnd(@cos, 3, 4).", P("fun"), P("x1"), P("x2"), Opt("options"));
        Add("fzero", "A zero of a function of one variable, from a guess or from an interval it changes sign across: x = fzero(@cos, [1 2]).", P("fun"), P("x0"), Opt("options"));
        Add("lsqnonneg", "The least-squares solution of C*x = d with every entry of x at or above zero: [x, resnorm] = lsqnonneg(C, d).", P("C"), P("d"), Opt("options"));
        Add("optimset", "The settings structure the solvers take, or one solver's defaults: opts = optimset('TolX', 1e-8).", Opt("options"));
        Add("optimget", "One setting out of that structure, or a fallback: t = optimget(opts, 'TolX', 1e-4).", P("options"), P("name"), Opt("default"));
        Add("optimplotfval", "A solver's PlotFcns entry that draws the objective against the iteration: optimset('PlotFcns', @optimplotfval).", P("x"), P("optimValues"), P("state"));
        Add("optimplotx", "A solver's PlotFcns entry that draws the current point, one bar per unknown.", P("x"), P("optimValues"), P("state"));
        Add("optimplotfunccount", "A solver's PlotFcns entry that draws how many evaluations have been spent.", P("x"), P("optimValues"), P("state"));

        // M100: polyfun, and the 1-D signal names from datafun and elfun that go with it.
        Add("roots", "The roots of a polynomial given by its coefficients, highest power first, as a column: r = roots([1 -6 11 -6]).", P("p"));
        Add("poly", "The polynomial with the given roots, or a square matrix's characteristic polynomial, as a row: p = poly([1 2 3]).", P("rOrA"));
        Add("polyder", "The derivative of a polynomial, of a product polyder(a, b), or of a ratio [q, d] = polyder(b, a).", P("p"), Opt("b"));
        Add("polyint", "The antiderivative of a polynomial, with an optional constant of integration: q = polyint(p, k).", P("p"), Opt("k"));
        Add("polyvalm", "A polynomial evaluated at a square matrix, every power a matrix power: Y = polyvalm(p, X).", P("p"), P("X"));
        Add("conv", "The convolution of two vectors, which is also the product of two polynomials; shape 'full' (default), 'same', or 'valid'.", P("u"), P("v"), Opt("shape"));
        Add("deconv", "Long division of one vector by another: [q, r] = deconv(u, v), so that u is conv(v, q) + r.", P("u"), P("v"));
        Add("convn", "Convolution over every dimension at once; shape 'full' (default), 'same', or 'valid'.", P("A"), P("B"), Opt("shape"));
        Add("nextpow2", "The exponent of the next power of two at or above each element's magnitude.", P("A"));
        Add("unwrap", "Removes the wrap from a phase record by adding whole turns: unwrap(P), unwrap(P, tol, dim).", P("P"), Opt("tol"), Opt("dim"));
        Add("cplxpair", "Orders values so conjugate pairs sit together, negative imaginary part first, with the real ones last.", P("A"), Opt("tol"), Opt("dim"));
        Add("polyarea", "The area a closed polygon encloses, by the shoelace formula: a = polyarea(x, y).", P("x"), P("y"), Opt("dim"));
        Add("rectint", "The area each rectangle of A shares with each rectangle of B, one row per rectangle of A.", P("A"), P("B"));
        Add("inpolygon", "Which query points a polygon encloses, and which sit on its edge: [in, on] = inpolygon(xq, yq, xv, yv).", P("xq"), P("yq"), P("xv"), P("yv"));

        // M101: the interpolation half of polyfun, and the piecewise polynomial behind it.
        Add("spline", "The not-a-knot cubic spline through samples, read at points or handed back as a pp: s = spline(x, y, xq).", P("x"), P("y"), Opt("xq"));
        Add("pchip", "The shape-preserving cubic through samples, which never overshoots one: p = pchip(x, y, xq).", P("x"), P("y"), Opt("xq"));
        Add("makima", "The modified Akima cubic through samples, local enough not to ring: yq = makima(x, y, xq).", P("x"), P("y"), Opt("xq"));
        Add("ppval", "A piecewise polynomial read at points: v = ppval(pp, xq).", P("pp"), P("xq"));
        Add("mkpp", "Builds a piecewise polynomial from its breaks and coefficients: pp = mkpp(breaks, coefs, d).", P("breaks"), P("coefs"), Opt("d"));
        Add("unmkpp", "Takes a piecewise polynomial apart: [breaks, coefs, L, order, dim] = unmkpp(pp).", P("pp"));
        Add("interp1q", "Quick straight-line interpolation that checks nothing, for columns only: yi = interp1q(x, Y, xi).", P("x"), P("Y"), P("xi"));
        Add("interpft", "Resamples a record to n points through its Fourier transform: y = interpft(X, n, dim).", P("X"), P("n"), Opt("dim"));
        Add("interpn", "A grid of any number of directions read between its samples: Vq = interpn(X1, ..., V, Xq1, ..., method, extrapval).", P("V"), Opt("Xq1"), Opt("more"), Opt("method"), Opt("extrapval"));

        // M102: the matrix builders of elmat, and the shape verbs that rearrange one.
        Add("toeplitz", "The matrix that is constant down every diagonal: T = toeplitz(c, r).", P("c"), Opt("r"));
        Add("hankel", "The matrix that is constant along every anti-diagonal: H = hankel(c, r).", P("c"), Opt("r"));
        Add("blkdiag", "The blocks laid corner to corner down a matrix of zeros: B = blkdiag(A1, A2, ...).", P("A1"), Opt("A2"), Opt("more"));
        Add("compan", "The companion matrix of a polynomial, whose eigenvalues are its roots: A = compan(u).", P("u"));
        Add("vander", "The Vandermonde matrix of a vector, powers descending across each row: A = vander(v).", P("v"));
        Add("hadamard", "A matrix of plus and minus ones with orthogonal columns: H = hadamard(n).", P("n"), Opt("classname"));
        Add("pascal", "Pascal's matrix of binomial coefficients, or one of its two factors: P = pascal(n, k).", P("n"), Opt("k"), Opt("classname"));
        Add("rosser", "Rosser's 8-by-8 symmetric eigenvalue test matrix: A = rosser.", Opt("classname"));
        Add("wilkinson", "Wilkinson's tridiagonal eigenvalue test matrix: W = wilkinson(n).", P("n"), Opt("classname"));
        Add("invhilb", "The exact inverse of the Hilbert matrix, whose entries are integers: H = invhilb(n).", P("n"), Opt("classname"));
        Add("gallery", "One of the Higham test matrices, by name: A = gallery('lehmer', 5).", P("matrixname"), Opt("p1"), Opt("more"), Opt("classname"));
        Add("repelem", "Each element repeated in place, as many times as its position asks: B = repelem(A, r1, r2).", P("A"), P("r1"), Opt("r2"), Opt("more"));
        Add("shiftdim", "The dimensions rotated, or the leading singletons stripped: [B, m] = shiftdim(A, n).", P("A"), Opt("n"));
        Add("ipermute", "Undoes a permute: A = ipermute(B, dimorder).", P("B"), P("dimorder"));
        Add("flipdim", "The values reversed along one dimension; the older spelling of flip: B = flipdim(A, dim).", P("A"), P("dim"));

        // M103: data cleaning and grouping — the outlier trio, change points, groups, and the
        // trend-and-correlation leftovers of datafun.
        Add("isoutlier", "Which readings sit outside the fences: [TF, L, U, C] = isoutlier(A, method, dim, 'ThresholdFactor', t).", P("A"), Opt("method"), Opt("window"), Opt("dim"), Opt("options"));
        Add("rmoutliers", "The data with its outliers removed — elements of a vector, whole rows of a matrix: [B, TF] = rmoutliers(A, method, dim).", P("A"), Opt("method"), Opt("window"), Opt("dim"), Opt("options"));
        Add("filloutliers", "Outliers replaced — by the center, the nearer fence, a neighbour, or an interpolant: [B, TF, L, U, C] = filloutliers(A, fill, method).", P("A"), P("fillmethod"), Opt("findmethod"), Opt("window"), Opt("dim"), Opt("options"));
        Add("ischange", "Where a signal stops being one thing: [TF, S1, S2] = ischange(A, method, 'Threshold', t).", P("A"), Opt("method"), Opt("dim"), Opt("options"));
        Add("findgroups", "Category values numbered in sorted order: [G, ID] = findgroups(A1, ..., AN), or over a table's variables.", P("A"), Opt("more"));
        Add("splitapply", "A function applied to each group of the data and the answers joined: Y = splitapply(func, X, G).", P("func"), P("X"), P("G"), Opt("more"));
        Add("standardizeMissing", "Named stand-ins replaced by the missing value of the data's kind: B = standardizeMissing(A, indicator).", P("A"), P("indicator"), Opt("options"));
        Add("subspace", "The largest principal angle between the ranges of two matrices: theta = subspace(A, B).", P("A"), P("B"));
        Add("detrend", "The best-fitting polynomial trend removed, segmented at breakpoints: y = detrend(x, n, bp, 'SamplePoints', t).", P("x"), Opt("n"), Opt("bp"), Opt("options"));
        Add("del2", "The discrete Laplacian over 2·ndims, boundaries by extrapolation: L = del2(U, hx, hy).", P("U"), Opt("h"), Opt("more"));
        Add("filter2", "Two-dimensional correlation, which is conv2 with the kernel turned half a turn: Y = filter2(H, X, shape).", P("H"), P("X"), Opt("shape"));
        Add("histcounts2", "Pairs counted onto a grid: [N, Xedges, Yedges, binX, binY] = histcounts2(X, Y, nbins).", P("X"), P("Y"), Opt("bins"), Opt("more"), Opt("options"));
        Add("xcorr", "Cross- or auto-correlation at every lag: [r, lags] = xcorr(x, y, maxlag, scaleopt).", P("x"), Opt("y"), Opt("maxlag"), Opt("scaleopt"));
        Add("xcov", "Cross- or auto-covariance — xcorr after each signal loses its mean: [c, lags] = xcov(x, y, maxlag, scaleopt).", P("x"), Opt("y"), Opt("maxlag"), Opt("scaleopt"));
        Add("groupcounts", "How many of each: [GC, GR, GP] = groupcounts(A), or a summary table with counts and percentages.", P("A"), Opt("groupvars"));
        Add("grouptransform", "Each group transformed in place — zscore, rescale, a fill, or a function handle: B = grouptransform(A, G, method).", P("A"), P("G"), P("method"));
        Add("groupfilter", "The rows of every group the predicate approves: B = groupfilter(A, G, method).", P("A"), P("G"), P("method"), Opt("datavars"));
        Add("head", "The first rows of an array or a table, eight when unasked: B = head(A, k).", P("A"), Opt("k"));
        Add("tail", "The last rows of an array or a table, eight when unasked: B = tail(A, k).", P("A"), Opt("k"));
        Add("topkrows", "The top rows under a lexicographic sort, descending unless told otherwise: [B, I] = topkrows(X, k, col, direction).", P("X"), P("k"), Opt("col"), Opt("direction"));
        Add("clip", "Every reading pulled inside the bounds: y = clip(x, lower, upper).", P("x"), P("lower"), P("upper"));
        Add("isuniform", "Whether the readings are evenly spaced, and by how much: [TF, step] = isuniform(v).", P("v"));
        Add("rmse", "Root-mean-square error between forecast and actual: E = rmse(F, A, dim, 'Weights', w).", P("F"), P("A"), Opt("dim"), Opt("options"));
        Add("mape", "Mean absolute percentage error between forecast and actual: E = mape(F, A, dim).", P("F"), P("A"), Opt("dim"), Opt("options"));
        // M104: strings and validators — the between-verbs, the char-matrix builders, the two that
        // spell a number's own bits, and the four mustBe... names the validators folder still lacked.
        Add("append", "Text joined with nothing between and nothing trimmed: s = append(s1, s2, ...).", P("text1"), Opt("text..."));
        Add("eraseBetween", "The text between two markers or two positions, taken out: s = eraseBetween(str, from, to).", P("str"), P("from"), P("to"), Opt("options"));
        Add("replaceBetween", "The text between two markers or two positions, written over: s = replaceBetween(str, from, to, new).", P("str"), P("from"), P("to"), P("newText"), Opt("options"));
        Add("extract", "Every occurrence of a piece of text, or the character at a position: s = extract(str, pat).", P("str"), P("pattern"));
        Add("splitlines", "The pieces of text between line breaks, as a column.", P("str"));
        Add("strtok", "The first run of non-delimiter characters, and what follows it: [t, r] = strtok(str, delims).", P("str"), Opt("delimiters"));
        Add("strjust", "Each row's characters moved to one side of its blanks: B = strjust(A, 'left').", P("A"), Opt("side"));
        Add("strvcat", "Text stacked into a char matrix, blank arguments left out: S = strvcat(a, b, c).", Opt("text..."));
        Add("str2mat", "Text stacked into a char matrix, blank arguments kept as blank rows.", Opt("text..."));
        Add("strmatch", "Which rows of a list of text begin with the text sought: x = strmatch(str, list, 'exact').", P("str"), P("list"), Opt("exact"));
        Add("isStringScalar", "Whether the value is a one-element string array.", P("A"));
        Add("hex2num", "The double whose bits a run of hexadecimal digits spells: x = hex2num('400921fb54442d18').", P("hexStr"));
        Add("num2hex", "The hexadecimal spelling of a number's own bits: s = num2hex(pi).", P("X"));
        Add("isvarname", "Whether the text is a name a variable could have.", P("s"));
        Add("mustBeNonsparse", "Errors if the value is sparse.", P("value"));
        Add("mustBeValidVariableName", "Errors unless every name could be a variable's.", P("value"));
        Add("mustBeFile", "Errors unless every path names a file that exists.", P("path"));
        Add("mustBeFolder", "Errors unless every path names a folder that exists.", P("path"));

        // M106: coordinates and elementary special functions — the four conversions, the elliptic
        // family, the exponential integral, the Legendre functions, the two rational
        // approximations, and the assignment problem. This closes the specfun folder.
        Add("cart2pol", "Cartesian coordinates read as polar: [theta, rho] = cart2pol(x, y).", P("x"), P("y"), Opt("z"));
        Add("pol2cart", "Polar coordinates read as Cartesian: [x, y] = pol2cart(theta, rho).", P("theta"), P("rho"), Opt("z"));
        Add("cart2sph", "Cartesian coordinates read as spherical: [az, elev, r] = cart2sph(x, y, z).", P("x"), P("y"), P("z"));
        Add("sph2cart", "Spherical coordinates read as Cartesian: [x, y, z] = sph2cart(az, elev, r).", P("azimuth"), P("elevation"), P("r"));
        Add("ellipke", "The complete elliptic integrals of the first and second kind: [K, E] = ellipke(m).", P("m"), Opt("tol"));
        Add("ellipj", "The Jacobi elliptic functions: [sn, cn, dn] = ellipj(u, m).", P("u"), P("m"), Opt("tol"));
        Add("expint", "The exponential integral E1(x), which answers in complex on the negative axis.", P("X"));
        Add("legendre", "Every order of the associated Legendre functions of one degree: P = legendre(n, X).", P("n"), P("X"), Opt("normalization"));
        Add("rat", "The continued fraction of a number, spelled out or reduced: [N, D] = rat(X).", P("X"), Opt("tol"));
        Add("rats", "A matrix written as a table of fractions: S = rats(X, strlen).", P("X"), Opt("strlen"));
        Add("matchpairs", "The cheapest pairing of rows with columns: M = matchpairs(Cost, costUnmatched).", P("Cost"), P("costUnmatched"), Opt("goal"));

        // M107: the matrix-function leftovers — the elimination, the plane rotation and the two
        // factorization updates over it, the two conversions between real and complex forms, the
        // eigenvalue conditioning, the three estimators, the Sylvester equation, the two least
        // squares solvers, the polynomial eigenproblem, the general matrix function, the
        // generalized SVD, and the decomposition object. This closes the matfun folder.
        Add("rref", "Reduced row echelon form by Gauss-Jordan elimination: [R, jb] = rref(A).", P("A"), Opt("tol"));
        Add("planerot", "The Givens rotation that puts a two-element column on its first axis: [G, y] = planerot(x).", P("x"));
        Add("qrinsert", "The QR factors after one column or row is inserted: [Q, R] = qrinsert(Q, R, j, x).", P("Q"), P("R"), P("j"), P("x"), Opt("orient"));
        Add("qrdelete", "The QR factors after one column or row is removed: [Q, R] = qrdelete(Q, R, j).", P("Q"), P("R"), P("j"), Opt("orient"));
        Add("cdf2rdf", "A complex diagonal eigenform rewritten as a real block one: [V, D] = cdf2rdf(V, D).", P("V"), P("D"));
        Add("rsf2csf", "A real Schur form rewritten as a complex triangular one: [U, T] = rsf2csf(U, T).", P("U"), P("T"));
        Add("condeig", "How sensitive each eigenvalue is to perturbation: s = condeig(A), or [V, D, s].", P("A"));
        Add("normest", "The 2-norm of a matrix, estimated by power iteration: [e, cnt] = normest(S, tol).", P("S"), Opt("tol"));
        Add("normest1", "The 1-norm of a matrix or operator, estimated by the block algorithm: normest1(A, t).", P("A"), Opt("t"), Opt("X0"));
        Add("condest", "The 1-norm condition number, estimated: [c, v] = condest(A, t).", P("A"), Opt("t"));
        Add("sylvester", "The solution X of the Sylvester equation A*X + X*B = C.", P("A"), P("B"), P("C"));
        Add("lsqminnorm", "The least-squares solution of smallest length: X = lsqminnorm(A, B, tol).", P("A"), P("B"), Opt("tol"), Opt("rankWarn"));
        Add("lscov", "Least squares with a covariance or weights: [x, stdx, mse, S] = lscov(A, b, V, alg).", P("A"), P("B"), Opt("V"), Opt("alg"));
        Add("polyeig", "The eigenvalues of a matrix polynomial: [X, e, s] = polyeig(A0, A1, ..., Ap).", P("A0"), Opt("A1..."));
        Add("funm", "A general function of a matrix, by Schur-Parlett: F = funm(A, @cos).", P("A"), P("fun"), Opt("options"), Opt("args..."));
        Add("gsvd", "The generalized singular value decomposition of a pair: [U, V, X, C, S] = gsvd(A, B).", P("A"), P("B"), Opt("flag"));
        Add("decomposition", "A matrix factored once and kept, so that a solve with it costs only a solve.", P("A"), Opt("type"), Opt("options..."));
        Add("isIllConditioned", "Whether a decomposition's matrix is ill conditioned.", P("dA"));

        Add("copulacdf", "The probability a copula puts below a point of the unit cube: y = copulacdf('Clayton', u, 1.5).", P("family"), P("u"), P("param"), Opt("nu"));
        Add("copulapdf", "The density of a copula at a point of the unit cube: y = copulapdf('t', u, rho, 5).", P("family"), P("u"), P("param"), Opt("nu"));
        Add("copularnd", "Draws from a copula, one per row: u = copularnd('Gumbel', 2, 500).", P("family"), P("param"), P("n"), Opt("extra"));
        Add("copulastat", "The rank correlation a copula parameter produces: r = copulastat('Frank', 4, 'type', 'Spearman').", P("family"), P("param"), Opt("options"));
        Add("copulaparam", "The parameter that produces a wanted rank correlation: a = copulaparam('Clayton', 0.4).", P("family"), P("r"), Opt("options"));
        Add("copulafit", "The copula parameter that makes observed probabilities most likely: [p, nu] = copulafit('t', u).", P("family"), P("u"), Opt("options"));

        Add("johnsrnd", "Draws from the Johnson curve through four quantiles: [r, type, coefs] = johnsrnd(quantiles, 1000, 1).", P("quantiles"), Opt("m"), Opt("n"));
        Add("pearsrnd", "Draws from the Pearson curve with four given moments: [r, type, coefs] = pearsrnd(0, 1, 0.5, 4, 100, 1).", P("mu"), P("sigma"), P("skew"), P("kurt"), Opt("m"), Opt("n"));
        Add("mhsample", "A Metropolis-Hastings chain aimed at a density you can only write down: [x, accept] = mhsample(x0, n, 'pdf', f, 'proprnd', g, 'symmetric', true).", P("start"), P("nsamples"), Opt("options"));
        Add("slicesample", "A slice sampler, which needs no proposal at all: [x, neval] = slicesample(x0, n, 'pdf', f, 'width', 5).", P("start"), P("nsamples"), Opt("options"));
        Add("mlecov", "How precise a maximum likelihood estimate is: acov = mlecov(phat, data, 'pdf', f).", P("params"), P("data"), Opt("options"));
        Add("paretotails", "A distribution empirical in the middle and Pareto in each tail: pd = paretotails(x, 0.1, 0.9).", P("x"), P("pl"), P("pu"), Opt("cdffun"));
        Add("createns", "A set of points prepared to be asked for neighbours later: ns = createns(X, 'NSMethod', 'kdtree').", P("X"), Opt("options"));
        Add("ExhaustiveSearcher", "The same, comparing against every point: ns = ExhaustiveSearcher(X, 'Distance', 'cityblock').", P("X"), Opt("options"));
        Add("KDTreeSearcher", "The same, over a space-partitioning tree: ns = KDTreeSearcher(X, 'BucketSize', 20).", P("X"), Opt("options"));
        Add("tsne", "A low-dimensional picture in which near stays near: [Y, loss] = tsne(X, 'Perplexity', 15).", P("X"), Opt("options"));

        Add("caseread", "One name per line, read as a cell of char rows: names = caseread('cases.dat').", P("file"));
        Add("casewrite", "The same file written back: casewrite(names, 'cases.dat').", P("names"), P("file"));
        Add("tblread", "A data file with named variables and named cases: [data, vars, cases] = tblread('sat.dat').", P("file"), Opt("delimiter"));
        Add("tblwrite", "The same file written back: tblwrite(data, vars, cases, 'sat.dat').", P("data"), P("varnames"), P("casenames"), P("file"), Opt("delimiter"));
        Add("tdfread", "A tab-delimited file read into one field per column: s = tdfread('sat.dat').", P("file"), Opt("delimiter"));
        Add("xptread", "A SAS transport file read into one field per variable: s = xptread('sample.xpt').", P("file"), Opt("option"));

        Add("cdfplot", "The empirical distribution function, drawn as a staircase: [h, stats] = cdfplot(x).", P("x"));
        Add("histfit", "A histogram with a fitted density over it: h = histfit(data, 12, 'gamma').", P("data"), Opt("nbins"), Opt("dist"));
        Add("normplot", "How straight a sample looks against a normal: h = normplot(x).", P("x"));
        Add("wblplot", "The same question asked of a Weibull: h = wblplot(x).", P("x"));
        Add("probplot", "The same question asked of any family: h = probplot('exponential', x).", Opt("dist"), P("y"), Opt("option"));
        Add("qqplot", "One sample's quantiles against another's, or against a normal's: h = qqplot(x, y).", P("x"), Opt("y"), Opt("pvec"));
        Add("boxplot", "A box and whisker per group: boxplot(x, g, 'Notch', 'on', 'Whisker', 1.5).", P("x"), Opt("group"), Opt("options"));
        Add("gscatter", "A scatter with one colour per group: h = gscatter(x, y, g, 'rgb', 'os^').", P("x"), P("y"), P("group"), Opt("clr"), Opt("sym"), Opt("siz"));
        Add("lsline", "A least-squares line through every series already drawn: h = lsline.", Opt("ax"));
        Add("refline", "A straight reference line over the current axes: h = refline(2, 1).", Opt("m"), Opt("b"));
        Add("refcurve", "A polynomial reference curve over the current axes: h = refcurve([1 -2 3]).", P("p"));
        Add("gplotmatrix", "A grid of scatters, one per pair of variables: [h, ax, bigax] = gplotmatrix(x, y, g).", P("x"), Opt("y"), Opt("group"), Opt("options"));
        Add("scatterhist", "A scatter with the two marginal histograms beside it: h = scatterhist(x, y).", P("x"), P("y"));
        Add("dendrogram", "The agglomerative tree drawn as the links that made it: [H, T, perm] = dendrogram(Z, 20, 'Orientation', 'left').", P("tree"), Opt("p"), Opt("options"));
        Add("manovacluster", "The dendrogram of the group means a multivariate analysis compared: manovacluster(stats, 'average').", P("stats"), Opt("method"));
        Add("andrewsplot", "Each observation as a Fourier curve in its own variables: andrewsplot(X, 'Group', g).", P("X"), Opt("options"));
        Add("parallelcoords", "Each observation as a line across its variables: parallelcoords(X, 'Standardize', 'on').", P("X"), Opt("options"));
        Add("glyphplot", "Each observation as a star whose rays are its variables: glyphplot(X, 'ObsLabels', names).", P("X"), Opt("options"));
        Add("biplot", "The variables as arrows and the observations as points, together: biplot(coefs, 'Scores', score).", P("coefs"), Opt("options"));
        Add("hist3", "How many observations fall in each cell of a grid: [N, C] = hist3(X, [10 10]).", P("X"), Opt("nbins"), Opt("options"));
        Add("addedvarplot", "What one predictor adds to a model that already holds the others: addedvarplot(X, y, 2, inmodel).", P("X"), P("y"), P("num"), Opt("inmodel"));
        Add("rcoplot", "The residuals in case order, each with its interval: rcoplot(r, rint).", P("r"), P("rint"));
        Add("interactionplot", "The response's mean at each level of one factor, per level of another: interactionplot(y, {a, b}).", P("Y"), P("group"), Opt("options"));
        Add("maineffectsplot", "The response's mean at each level of each factor: maineffectsplot(y, {a, b}).", P("Y"), P("group"), Opt("options"));
        Add("multivarichart", "The same means read as a multi-vari chart: multivarichart(y, {a, b}).", P("y"), P("group"), Opt("options"));
        Add("lassoPlot", "How the penalized coefficients shrink as the penalty grows: lassoPlot(B, FitInfo, 'PlotType', 'Lambda').", P("B"), Opt("fitinfo"), Opt("options"));
        Add("perfcurve", "How a classifier's two error rates trade off: [X, Y, T, AUC] = perfcurve(labels, scores, 1).", P("labels"), P("scores"), P("posclass"), Opt("options"));

        // --- Distribution objects (M53 wave I) -------------------------------------------------------
        Add("makedist", "A probability distribution object built from its parameters: pd = makedist('Normal', 'mu', 10, 'sigma', 2).", Opt("name"), Opt("property"), Opt("value"));
        Add("fitdist", "A probability distribution fitted to data: pd = fitdist(x, 'Weibull', 'Censoring', c).", P("x"), P("name"), Opt("options"));
        Add("truncate", "The same distribution conditioned on an interval: t = truncate(pd, 0, 10).", P("pd"), P("lower"), P("upper"));
        Add("negloglik", "The negative log-likelihood of the data a distribution was fitted to: nll = negloglik(pd).", P("pd"));
        Add("paramci", "A confidence interval for each fitted parameter: ci = paramci(pd, 'Alpha', 0.01).", P("pd"), Opt("options"));
        Add("proflik", "The likelihood as one parameter is walked, the others re-fitted at each step: [ll, p] = proflik(pd, 1).", P("pd"), P("pnum"), Opt("options"));
        Add("BetaDistribution", "A beta distribution, built from its parameters: pd = BetaDistribution('a', 2, 'b', 5).", Opt("property"), Opt("value"));
        Add("BinomialDistribution", "A binomial distribution, built from its parameters: pd = BinomialDistribution('N', 20, 'p', 0.3).", Opt("property"), Opt("value"));
        Add("BirnbaumSaundersDistribution", "A birnbaum-saunders distribution, built from its parameters: pd = BirnbaumSaundersDistribution('beta', 2, 'gamma', 0.5).", Opt("property"), Opt("value"));
        Add("BurrDistribution", "A burr distribution, built from its parameters: pd = BurrDistribution('alpha', 1, 'c', 2, 'k', 3).", Opt("property"), Opt("value"));
        Add("ExponentialDistribution", "An exponential distribution, built from its parameters: pd = ExponentialDistribution('mu', 4).", Opt("property"), Opt("value"));
        Add("ExtremeValueDistribution", "An extreme value distribution, built from its parameters: pd = ExtremeValueDistribution('mu', 0, 'sigma', 2).", Opt("property"), Opt("value"));
        Add("GammaDistribution", "A gamma distribution, built from its parameters: pd = GammaDistribution('a', 3, 'b', 2).", Opt("property"), Opt("value"));
        Add("GeneralizedExtremeValueDistribution", "A generalized extreme value distribution, built from its parameters: pd = GeneralizedExtremeValueDistribution('k', 0.1, 'sigma', 1, 'mu', 0).", Opt("property"), Opt("value"));
        Add("GeneralizedParetoDistribution", "A generalized pareto distribution, built from its parameters: pd = GeneralizedParetoDistribution('k', 0.2, 'sigma', 1, 'theta', 0).", Opt("property"), Opt("value"));
        Add("HalfNormalDistribution", "A half-normal distribution, built from its parameters: pd = HalfNormalDistribution('mu', 0, 'sigma', 3).", Opt("property"), Opt("value"));
        Add("InverseGaussianDistribution", "An inverse gaussian distribution, built from its parameters: pd = InverseGaussianDistribution('mu', 1, 'lambda', 4).", Opt("property"), Opt("value"));
        Add("KernelDistribution", "A kernel-smoothed distribution — the object fitdist(x, 'Kernel') returns.", Opt("property"), Opt("value"));
        Add("LogisticDistribution", "A logistic distribution, built from its parameters: pd = LogisticDistribution('mu', 0, 'sigma', 2).", Opt("property"), Opt("value"));
        Add("LoglogisticDistribution", "A log-logistic distribution, built from its parameters: pd = LoglogisticDistribution('mu', 0, 'sigma', 0.5).", Opt("property"), Opt("value"));
        Add("LognormalDistribution", "A lognormal distribution, built from its parameters: pd = LognormalDistribution('mu', 0, 'sigma', 1).", Opt("property"), Opt("value"));
        Add("LoguniformDistribution", "A log-uniform distribution, built from its parameters: pd = LoguniformDistribution('Lower', 1, 'Upper', 100).", Opt("property"), Opt("value"));
        Add("MultinomialDistribution", "A multinomial distribution, built from its parameters: pd = MultinomialDistribution('probabilities', [0.2 0.5 0.3]).", Opt("property"), Opt("value"));
        Add("NakagamiDistribution", "A nakagami distribution, built from its parameters: pd = NakagamiDistribution('mu', 1.5, 'omega', 2).", Opt("property"), Opt("value"));
        Add("NegativeBinomialDistribution", "A negative binomial distribution, built from its parameters: pd = NegativeBinomialDistribution('R', 3, 'p', 0.4).", Opt("property"), Opt("value"));
        Add("NormalDistribution", "A normal distribution, built from its parameters: pd = NormalDistribution('mu', 10, 'sigma', 2).", Opt("property"), Opt("value"));
        Add("PiecewiseLinearDistribution", "A piecewise-linear distribution, built from its parameters: pd = PiecewiseLinearDistribution('x', [0 1 3], 'Fx', [0 0.5 1]).", Opt("property"), Opt("value"));
        Add("PoissonDistribution", "A poisson distribution, built from its parameters: pd = PoissonDistribution('lambda', 4).", Opt("property"), Opt("value"));
        Add("RayleighDistribution", "A rayleigh distribution, built from its parameters: pd = RayleighDistribution('B', 2).", Opt("property"), Opt("value"));
        Add("RicianDistribution", "A rician distribution, built from its parameters: pd = RicianDistribution('s', 2, 'sigma', 1).", Opt("property"), Opt("value"));
        Add("StableDistribution", "A stable distribution, built from its parameters: pd = StableDistribution('alpha', 1.5, 'beta', 0, 'gam', 1, 'delta', 0).", Opt("property"), Opt("value"));
        Add("TriangularDistribution", "A triangular distribution, built from its parameters: pd = TriangularDistribution('A', 0, 'B', 1, 'C', 3).", Opt("property"), Opt("value"));
        Add("UniformDistribution", "A uniform distribution, built from its parameters: pd = UniformDistribution('Lower', 0, 'Upper', 10).", Opt("property"), Opt("value"));
        Add("WeibullDistribution", "A weibull distribution, built from its parameters: pd = WeibullDistribution('A', 2, 'B', 1.5).", Opt("property"), Opt("value"));
        Add("tLocationScaleDistribution", "A t location-scale distribution, built from its parameters: pd = tLocationScaleDistribution('mu', 0, 'sigma', 1, 'nu', 5).", Opt("property"), Opt("value"));
        // --- Clustering, distances and multivariate analysis (M53 wave H) ---------------------------
        Add("pdist", "The distance between every pair of rows, as one long vector: D = pdist(X, 'minkowski', 3).", P("X"), Opt("metric"), Opt("arg"));
        Add("pdist2", "Every row of one set against every row of another: D = pdist2(X, Y, 'cosine').", P("X"), P("Y"), Opt("metric"), Opt("arg"));
        Add("squareform", "The pairwise distances as a square matrix, or a square matrix condensed back: Z = squareform(D, 'tomatrix').", P("D"), Opt("direction"));
        Add("mahal", "How far each point is from a sample's centre in that sample's own metric: d = mahal(Y, X).", P("Y"), P("X"));
        Add("knnsearch", "The nearest members of a set to each query point: [idx, d] = knnsearch(X, Y, 'K', 3, 'Distance', 'cityblock').", P("X"), P("Y"), Opt("options"));
        Add("rangesearch", "Every member within a radius of each query point: [idx, d] = rangesearch(X, Y, r, 'Distance', 'chebychev').", P("X"), P("Y"), P("radius"), Opt("options"));
        Add("linkage", "The agglomerative tree over the data or over distances already computed: Z = linkage(X, 'ward', 'euclidean').", P("X"), Opt("method"), Opt("metric"), Opt("arg"));
        Add("cluster", "Which cluster each observation falls in when the tree is cut: T = cluster(Z, 'maxclust', 3) or cluster(Z, 'cutoff', c).", P("Z"), P("option"), P("value"), Opt("more"), Opt("value"));
        Add("clusterdata", "Linkage and cut in one step: T = clusterdata(X, 'maxclust', 3, 'linkage', 'average').", P("X"), Opt("cutoff"), Opt("options"));
        Add("cophenet", "How faithfully the tree reproduces the distances it was built from: [c, d] = cophenet(Z, Y).", P("Z"), P("Y"));
        Add("inconsistent", "Each merge's height against the merges below it: Y = inconsistent(Z, depth).", P("Z"), Opt("depth"));
        Add("optimalleaforder", "The leaf order that keeps neighbouring leaves as close as the tree allows: order = optimalleaforder(Z, D).", P("Z"), P("D"), Opt("options"));
        Add("kmeans", "Clusters around their own means: [idx, C, sumd, D] = kmeans(X, 3, 'Replicates', 5, 'Start', 'plus').", P("X"), P("k"), Opt("options"));
        Add("kmedoids", "Clusters around members rather than means, in any metric: [idx, C, sumd, D, midx, info] = kmedoids(X, 3, 'Distance', 'correlation').", P("X"), P("k"), Opt("options"));
        Add("dbscan", "Clusters found by density, leaving sparse points in none: [idx, corepts] = dbscan(X, epsilon, minpts).", P("X"), P("epsilon"), P("minpts"), Opt("options"));
        Add("spectralcluster", "Clusters found in the eigenvectors of the affinity matrix: [idx, V, D] = spectralcluster(X, 3, 'KernelScale', 1).", P("X"), P("k"), Opt("options"));
        Add("silhouette", "How well each observation sits in its cluster, between -1 and 1: s = silhouette(X, idx, 'cityblock').", P("X"), P("clust"), Opt("metric"), Opt("arg"));
        Add("pca", "The directions the data varies in most: [coeff, score, latent, tsquared, explained, mu] = pca(X, 'NumComponents', 2).", P("X"), Opt("options"));
        Add("pcacov", "The same components from a covariance matrix already formed: [coeff, latent, explained] = pcacov(V).", P("V"));
        Add("pcares", "What the first few components fail to account for: [residuals, reconstructed] = pcares(X, ndim).", P("X"), P("ndim"));
        Add("ppca", "Components fitted by maximum likelihood, so a missing value costs one entry and not a whole row: [coeff, score, pcvar, mu, v, S] = ppca(Y, k).", P("Y"), P("k"), Opt("options"));
        Add("nnmf", "A factorization into parts that only ever add: [W, H, D] = nnmf(A, k, 'Replicates', 5, 'Algorithm', 'mult').", P("A"), P("k"), Opt("options"));
        Add("rotatefactors", "Loadings turned so each variable loads on few components: [B, T] = rotatefactors(A, 'Method', 'promax').", P("A"), Opt("options"));
        Add("cmdscale", "Coordinates whose distances reproduce the ones given: [Y, e] = cmdscale(D, p).", P("D"), Opt("p"));
        Add("procrustes", "How much of one configuration is left once it has been moved onto another: [d, Z, transform] = procrustes(X, Y, 'Scaling', false).", P("X"), P("Y"), Opt("options"));
        Add("canoncorr", "The combinations of two sets of variables that agree most: [A, B, r, U, V, stats] = canoncorr(X, Y).", P("X"), P("Y"));
        Add("robustcov", "A covariance a handful of outliers cannot move, and the outliers: [sig, mu, mah, outliers, s] = robustcov(X, 'Method', 'ogk').", P("X"), Opt("options"));
        Add("grp2idx", "A grouping variable as numbers, with the level names: [G, GN, GL] = grp2idx(S).", P("S"));
        Add("confusionmat", "How often each known class was predicted as each other: [C, order] = confusionmat(known, predicted).", P("known"), P("predicted"), Opt("options"));
        Add("onehotencode", "One indicator column per class, a single one in each row: A = onehotencode(labels, dim).", P("labels"), Opt("dim"));
        Add("onehotdecode", "The class each row's largest indicator names: labels = onehotdecode(A, classes, dim).", P("A"), P("classes"), Opt("dim"));
        Add("hmmgenerate", "A sequence drawn from a hidden Markov model: [seq, states] = hmmgenerate(len, TRANS, EMIS, 'Symbols', s).", P("len"), P("TRANS"), P("EMIS"), Opt("options"));
        Add("hmmdecode", "The probability of each state at each step of an observed sequence: [pstates, logpseq, fs, bs, s] = hmmdecode(seq, TRANS, EMIS).", P("seq"), P("TRANS"), P("EMIS"), Opt("options"));
        Add("hmmviterbi", "The single most likely path through a model: [states, logp] = hmmviterbi(seq, TRANS, EMIS).", P("seq"), P("TRANS"), P("EMIS"), Opt("options"));
        Add("hmmestimate", "The two matrices counted from a sequence whose states are known: [TRANS, EMIS] = hmmestimate(seq, states).", P("seq"), P("states"), Opt("options"));
        Add("hmmtrain", "The two matrices estimated from the observations alone: [TRANS, EMIS] = hmmtrain(seqs, guessTR, guessE, 'Tolerance', 1e-6).", P("seqs"), P("guessTR"), P("guessE"), Opt("options"));

        // --- Array operations ---------------------------------------------------------------------
        Add("sort", "A sorted copy of a numeric or string array; order \"ascend\" (default) or \"descend\", with 'MissingPlacement' and 'ComparisonMethod' for where NaN lands and how complex numbers order.", P("array"), Opt("order"), Opt("options"));
        Add("unique", "The distinct values of a numeric or string array: [c, ia, ic] = unique(x, 'rows', 'stable', 'last'), where c = x(ia) and x = c(ic).", P("array"), Opt("option"), Opt("more"));
        Add("find", "Indices of the truthy elements: volt(find(temp > 85)) gathers the matches. In a .m file find(x, k) keeps the first k ('last' for the other end); in JGS the second argument is the index base, 0 by default.", P("mask"), Opt("k"), Opt("direction"));
        Add("any", "Whether at least one element is truthy, over one dimension, several, or 'all'.", P("array"), Opt("dim"));
        Add("all", "Whether every element is truthy, over one dimension, several, or 'all'.", P("array"), Opt("dim"));
        Add("concat", "One array from arrays and scalars, in order: concat(a, b), concat(a, 5).", P("first"), P("second"));
        Add("slice", "In JGS, elements [start, stop) by 0-based index; stop defaults to the array length. In MATLAB, the volume shown where something cuts it: slice(V, sx, sy, sz) or slice(X, Y, Z, V, sx, sy, sz) for axis-aligned planes, any list [] for none, or three same-sized matrices for a slicing surface.", P("array"), P("start"), Opt("stop"));
        Add("indexof", "0-based index of the first element equal to value, or -1.", P("array"), P("value"));
        Add("reverse", "A reversed copy of an array.", P("array"));
        Add("isnan", "Whether x is NaN, element-wise over arrays.", P("x"));
        Add("isequal", "Deep equality of two values (arrays element-by-element), as one bool.", P("a"), P("b"));
        Add("isequaln", "Deep equality treating NaN as equal to NaN.", P("a"), P("b"));
        Add("isequalwithequalnans", "The pre-R2012a name for isequaln.", P("a"), P("b"));
        Add("isfinite", "Whether x is finite, element-wise over arrays.", P("x"));
        Add("isinf", "Whether x is infinite, element-wise over arrays.", P("x"));
        Add("isfloat", "True for a value stored as floating point — every JGraph number.", P("x"));
        Add("isinteger", "True for an integer class; always false, since JGraph numbers are doubles.", P("x"));
        Add("isreal", "True when a value carries no imaginary part.", P("x"));
        Add("isscalar", "True for a single value rather than an array.", P("x"));
        Add("isvector", "True for a scalar or a flat array (no nested rows).", P("x"));
        Add("ismatrix", "True for any two-dimensional value.", P("x"));
        Add("isrow", "True for a vector; JGraph vectors have no orientation and read as rows.", P("x"));
        Add("iscolumn", "True only for a single value, since vectors read as rows.", P("x"));
        Add("isstr", "True for a string (the pre-R2016 spelling of ischar).", P("x"));
        Add("isstring", "True for a string array — text written with double quotes, not single ones.", P("x"));
        Add("iscellstr", "True for a cell array whose every element is a string.", P("x"));
        Add("isletter", "Whether each character is a letter, as a mask.", P("text"));
        Add("isspace", "Whether each character is whitespace, as a mask.", P("text"));
        Add("issorted", "Whether the values are in non-decreasing order along a dimension.", P("x"), Opt("dim"));
        Add("class", "The class name of a value: double, logical, char, cell, struct, function_handle.", P("x"));
        Add("isa", "Whether a value has the named class, or is 'numeric'/'float'/'integer'.", P("x"), P("type"));
        Add("logical", "The value converted to a logical (true where non-zero).", P("x"));
        Add("cast", "The value converted to the named class, or to the class of a prototype with cast(x, 'like', p).", P("x"), P("type"), Opt("prototype"));
        Add("double", "x as a number: a logical becomes 0 or 1, and text becomes its character codes.", P("x"));
        Add("single", "x rounded to single precision (still stored as a double).", P("x"));
        foreach (string integerClass in new[] { "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64" })
        {
            Add(integerClass, $"x rounded and saturated to the {integerClass} range (stored as a double); {integerClass}.empty(m, n) builds an empty.", P("x"));
        }

        Add("and", "Element-wise logical AND, broadcasting a scalar across an array.", P("a"), P("b"));
        Add("or", "Element-wise logical OR, broadcasting a scalar across an array.", P("a"), P("b"));
        Add("not", "Element-wise logical NOT over an array, or of one value.", P("a"));

        // --- Strings ------------------------------------------------------------------------------
        Add("sprintf", "Formats values C-style: %d %i %f %e %g %s %x %% with width/precision (%.2f, %8d).", P("format"), P("values"));
        Add("fprintf", "Writes a sprintf-formatted string to the console or to an open file with no added newline; answers how many bytes went out.", P("format"), P("values"));
        Add("str", "Any value formatted as a string.", P("value"));
        Add("num", "A string parsed as a number; NaN when it does not parse (filter with isnan).", P("text"));
        Add("upper", "The string in upper case.", P("text"));
        Add("lower", "The string in lower case.", P("text"));
        Add("trim", "The string without leading/trailing whitespace.", P("text"));
        Add("split", "The pieces of text between occurrences of separator, as a string array.", P("text"), P("separator"));
        Add("join", "The array's elements joined into one string with separator between them.", P("array"), P("separator"));
        Add("startsWith", "Whether text starts with prefix.", P("text"), P("prefix"));
        Add("endsWith", "Whether text ends with suffix.", P("text"), P("suffix"));
        Add("replace", "text with every occurrence of old replaced by new.", P("text"), P("old"), P("new"));
        Add("contains", "Whether a string contains a substring, or an array contains a value.", P("value"), P("search"));

        // --- Table access -----------------------------------------------------------------------
        Add("readcsv", "Reads a delimited text file into a table, skipping skiprows leading junk lines first. Bare names resolve against the script, then the workspace root.", P("path"), Opt("skiprows"));
        Add("readxlsx", "Reads the first sheet of an .xlsx workbook into a table, skipping skiprows leading rows first.", P("path"), Opt("skiprows"));
        Add("readtable", "Reads a .csv/.tsv/.txt/.xlsx file into a table, picking the reader by extension.", P("path"), Opt("skiprows"));
        Add("writematrix", "Writes a matrix to a delimited text file: writematrix(A, 'x.csv', 'Delimiter', 'tab', 'WriteMode', 'append').", P("A"), P("path"), Opt("options"));
        Add("writecell", "Writes a cell array to a delimited text file, each element as its own field.", P("C"), P("path"), Opt("options"));
        Add("writetable", "Writes a table to a delimited text file; 'WriteVariableNames', false leaves the header off.", P("T"), P("path"), Opt("options"));
        Add("writelines", "Writes each line of text to a file, one per row.", P("lines"), P("path"));
        Add("readlines", "Reads a text file as a column of string, one element per line.", P("path"));
        Add("readmatrix", "Reads a delimited text file as a numeric matrix; anything unparseable reads as NaN.", P("path"), Opt("options"));
        Add("readcell", "Reads a delimited text file as a cell array, numbers as numbers and everything else as text.", P("path"), Opt("options"));
        Add("csvwrite", "Writes a matrix as comma-separated text (the older spelling of writematrix).", P("path"), P("A"));
        Add("dlmwrite", "Writes a matrix as delimited text with the given delimiter (the older spelling of writematrix).", P("path"), P("A"), Opt("delimiter"));
        Add("struct2table", "A struct array as a table, one row per element and one variable per field.", P("s"));
        Add("table2struct", "A table as a struct array, one element per row and one field per variable.", P("T"));
        Add("column", "A table column as a numeric array.", P("table"), P("name"));
        Add("colnames", "The table's column names as a string array.", P("table"));
        Add("rowcount", "The number of data rows in the table.", P("table"));
        Add("textcolumn", "A table column as a string array (missing cells become \"\") — for serial numbers and IDs.", P("table"), P("name"));

        // --- Composition and output ---------------------------------------------------------------
        Add("run", "Runs another JGS script into the current global scope (an include).", P("path"));
        Add("clear", "Clears the workspace (or just the named variables) and reverts any rebound built-in. Figures stay open.", Opt("names"));
        Add("clearvars", "Clears the user's variables (all, or just the named ones). Built-ins are untouched.", Opt("names"));
        Add("print", "In JGS, writes the values to the console, space-separated. In the MATLAB dialect the paper verb instead: print('plot.png'), print(gcf, 'plot', '-dpdf'), with '-dpng'/'-djpeg'/'-dpdf'/'-dsvg' for the format and '-r300' for the resolution.", P("values"));
        Add("clc", "Clears the console display. Variables and figures are untouched.");
        Add("whos", "Lists the workspace's variables with their size and class.");
        Add("save", "Writes workspace variables to a version 5 MAT-file (or text with '-ascii'); '-append' adds to one that exists.", Opt("path"), Opt("names..."));
        Add("load", "Reads variables from a version 5 or 7.3 MAT-file (or a numeric text file) into the workspace.", Opt("path"), Opt("names..."));
        Add("fopen", "Opens a file and returns its id (-1 on failure), or tells you about one already open: modes r (default), w, a, r+, w+, a+, A, W, with an optional byte order and encoding.", P("path"), Opt("mode"), Opt("machinefmt"), Opt("encoding"));
        Add("fclose", "Closes a file id, or every open file with fclose('all').", P("fid"));
        Add("fread", "Reads binary values from a file: a count or an [m n] shape, a precision that may name the class read and the class kept, bytes to skip between elements, and a byte order.", P("fid"), Opt("size"), Opt("precision"), Opt("skip"), Opt("machinefmt"));
        Add("fwrite", "Writes values to a file in binary: a precision, bytes to skip between elements, and a byte order; answers how many elements went out.", P("fid"), P("data"), Opt("precision"), Opt("skip"), Opt("machinefmt"));
        Add("frewind", "Moves an open file back to its beginning.", P("fid"));
        Add("fgetl", "The next text line of a file, without its newline; -1 (a number) at end of file.", P("fid"));
        Add("image", "Displays a matrix as a colormapped image over its cell indices (an image value shows as-is).", P("z"));
        Add("help", "Shows a builtin's signature and summary; help alone lists every function.", Opt("name"));
        Add("format", "Sets numeric display precision: short, long, shortE, longE (bare format resets).", Opt("mode"));
        Add("dir", "The files and folders in the working directory (or matching pattern) as a cell array of names; folders end with the path separator.", Opt("pattern"));
        Add("path", "The search path as one string; path(folders) replaces the added folders.", Opt("folders..."));
        Add("addpath", "Adds folders to the search path, so their .m files answer bare names; '-end' appends instead.", P("folder"), Opt("more..."));
        Add("rmpath", "Removes folders from the search path.", P("folder"), Opt("more..."));
        Add("genpath", "A folder and all of its sub-folders, joined by the path separator, ready for addpath.", P("folder"));
        Add("pathsep", "The character that separates folders in a path string.");

        // --- Errors and argument validation (M62) --------------------------------------------------
        Add("MException", "Builds an error object: an identifier, a message, and the stack it carries.", P("identifier"), P("message"), Opt("args..."));
        Add("throw", "Raises an MException.", P("exception"));
        Add("addCause", "A copy of an MException carrying one more underlying cause.", P("exception"), P("cause"));

        // --- User classes (M68) --------------------------------------------------------------------
        Add("isobject", "True for an instance of a class, including the built-in ones a value stands in for.", P("x"));
        Add("properties", "The property names of an object, or of a class named by its name, as a cell column.", P("x"));
        Add("methods", "The method names of an object, or of a class named by its name, as a cell column.", P("x"));
        Add("metaclass", "A description of a value's class: its name, its properties, and its methods.", P("x"));
        Add("throwAsCaller", "Raises an MException, reported against the caller.", P("exception"));
        Add("mustBePositive", "Errors unless every element is greater than zero.", P("value"));
        Add("mustBeNonnegative", "Errors unless every element is zero or greater.", P("value"));
        Add("mustBeNegative", "Errors unless every element is less than zero.", P("value"));
        Add("mustBeNonpositive", "Errors unless every element is zero or less.", P("value"));
        Add("mustBeNonzero", "Errors unless every element is nonzero.", P("value"));
        Add("mustBeFinite", "Errors unless every element is finite.", P("value"));
        Add("mustBeNonNan", "Errors if any element is NaN.", P("value"));
        Add("mustBeInteger", "Errors unless every element is a whole number.", P("value"));
        Add("mustBeReal", "Errors if the value is complex.", P("value"));
        Add("mustBeNumeric", "Errors unless the value is numeric.", P("value"));
        Add("mustBeNumericOrLogical", "Errors unless the value is numeric or a mask.", P("value"));
        Add("mustBeFloat", "Errors unless the value is floating-point.", P("value"));
        Add("mustBeNonempty", "Errors if the value has no elements.", P("value"));
        Add("mustBeScalarOrEmpty", "Errors unless the value is one element or none.", P("value"));
        Add("mustBeVector", "Errors unless the value is a nonempty row or column.", P("value"));
        Add("mustBeText", "Errors unless the value is text, or a cell of text.", P("value"));
        Add("mustBeTextScalar", "Errors unless the value is one piece of text.", P("value"));
        Add("mustBeMember", "Errors unless every element is one of the allowed values.", P("value"), P("allowed"));
        Add("mustBeA", "Errors unless the value's class is one of those named.", P("value"), P("classes"));
        Add("mustBeGreaterThan", "Errors unless every element is greater than the bound.", P("value"), P("bound"));
        Add("mustBeLessThan", "Errors unless every element is less than the bound.", P("value"), P("bound"));
        Add("mustBeGreaterThanOrEqual", "Errors unless every element is at least the bound.", P("value"), P("bound"));
        Add("mustBeLessThanOrEqual", "Errors unless every element is at most the bound.", P("value"), P("bound"));
        Add("mustBeInRange", "Errors unless every element lies between the bounds; 'exclude-lower'/'exclude-upper' open an end.", P("value"), P("low"), P("high"), Opt("bounds..."));
        Add("validateattributes", "Errors unless the value has one of the named classes and all of the named attributes.", P("value"), P("classes"), P("attributes"), Opt("name"));

        // --- Figure setup and plotting -------------------------------------------------------------
        Add("figure", "Starts a new figure (or selects figure n) and returns its handle (a figure number, so it starts at 1). Any figure property may be set at construction: figure('Position', [x y w h], 'Name', 'title').", Opt("n"), Opt("options"));
        Add("subplot", "Selects cell index of a rows-by-cols axes grid (a grid cell number, so 1-based, row-major) and returns a handle on it.", P("rows"), P("cols"), P("index"));
        Add("close", "Closes the current figure, figure n, or every figure with close all; a trailing 'force' skips CloseRequestFcn.", Opt("n"), Opt("force"));
        Add("closereq", "The default close a CloseRequestFcn opts back into: deletes the callback's figure without asking again.");
        Add("uicontextmenu", "A right-click menu for a figure's objects: cm = uicontextmenu; set(h, 'ContextMenu', cm).", Opt("parent"), Opt("name"), Opt("value"));
        Add("uimenu", "One entry of a context menu: m = uimenu(cm, 'Text', 'Copy', 'MenuSelectedFcn', @onCopy).", Opt("parent"), Opt("name"), Opt("value"));
        Add("clf", "Clears the current figure (or figure n), keeping its window open.", Opt("n"));
        Add("gcf", "The current figure's number.");
        Add("gca", "Selects the current axes, creating a figure and axes if there are none.");

        // --- Handle graphics (M54) -----------------------------------------------------------------
        Add("get", "Reads a figure object's properties through its handle: get(h, 'Color'), get(h, {'A','B'}), or get(h) for all of them as a struct.", P("h"), Opt("name"));
        Add("set", "Writes properties through a handle: set(h, 'LineWidth', 2, 'Color', 'r'), set(h, {'A','B'}, {1, 2}), or set(h) for the writable names.", P("h"), Opt("name"), Opt("value"));
        Add("findobj", "Finds figure objects whose properties match: findobj('Type', 'line'), findobj(ax, 'Tag', 't'), with 'flat' or '-depth' n to limit how deep it looks.", Opt("h"), Opt("name"), Opt("value"));
        Add("refreshdata", "Re-reads the workspace variables named by the XDataSource family and writes what they now hold back into the charts.", Opt("h"), Opt("workspace"));
        Add("findall", "Like findobj, but also finds objects that asked to stay hidden from a search.", Opt("h"), Opt("name"), Opt("value"));
        Add("ishandle", "Whether each number names a live figure object.", P("h"));
        Add("ishghandle", "Whether each number names a live figure object (the same question as ishandle).", P("h"));
        Add("isgraphics", "Whether each number names a live figure object, optionally of a named kind: isgraphics(h, 'axes').", P("h"), Opt("type"));
        Add("ancestor", "The nearest enclosing object of a named kind: ancestor(p, 'axes'), or 'toplevel' for the outermost one.", P("h"), P("type"), Opt("toplevel"));
        Add("copyobj", "Copies a figure object into another parent and returns a handle on the copy: copyobj(p, otherAxes).", P("h"), P("parent"));
        Add("gobjects", "A block of empty handles to fill in: gobjects(n) or gobjects(rows, cols).", Opt("rows"), Opt("cols"));
        Add("gco", "The object the user last clicked, or empty when none has been.");
        Add("gcbo", "The object whose callback is running, or empty outside a callback.");
        Add("gcbf", "The figure of the object whose callback is running, or empty outside a callback.");
        Add("cla", "Empties the current axes (or a named one); cla reset also puts its settings back.", Opt("ax"), Opt("reset"));
        Add("ishold", "Whether the current axes (or a named one) is keeping what is already drawn.", Opt("ax"));
        Add("newplot", "Readies an axes for the next drawing verb, honouring hold, and returns a handle on it.", Opt("ax"));
        Add("shg", "Shows the current figure.");

        // --- Rulers and ticks (M54) ------------------------------------------------------------------
        Add("xticks", "Where the x ticks go: xticks(0:5), xticks('auto'), xticks('manual'), or xticks to read them back.", Opt("ax"), Opt("values"));
        Add("yticks", "Where the y ticks go: yticks(0:5), yticks('auto'), yticks('manual'), or yticks to read them back.", Opt("ax"), Opt("values"));
        Add("zticks", "Where the z ticks go: zticks(0:5), zticks('auto'), zticks('manual'), or zticks to read them back.", Opt("ax"), Opt("values"));
        Add("xticklabels", "What the x ticks read: xticklabels({'low','high'}), 'auto', 'manual', or xticklabels to read them back.", Opt("ax"), Opt("labels"));
        Add("yticklabels", "What the y ticks read: yticklabels({'low','high'}), 'auto', 'manual', or yticklabels to read them back.", Opt("ax"), Opt("labels"));
        Add("zticklabels", "What the z ticks read: zticklabels({'low','high'}), 'auto', 'manual', or zticklabels to read them back.", Opt("ax"), Opt("labels"));
        Add("xtickangle", "Turns the x tick labels: xtickangle(45), or xtickangle to read the angle back.", Opt("ax"), Opt("angle"));
        Add("ytickangle", "Turns the y tick labels: ytickangle(45), or ytickangle to read the angle back.", Opt("ax"), Opt("angle"));
        Add("ztickangle", "Turns the z tick labels: ztickangle(45), or ztickangle to read the angle back.", Opt("ax"), Opt("angle"));
        Add("xtickformat", "How the x tick numbers are written: xtickformat('%.2f'), or a word such as usd, degrees, percentage, auto.", Opt("ax"), Opt("format"));
        Add("ytickformat", "How the y tick numbers are written: ytickformat('%.2f'), or a word such as usd, degrees, percentage, auto.", Opt("ax"), Opt("format"));
        Add("ztickformat", "How the z tick numbers are written: ztickformat('%.2f'), or a word such as usd, degrees, percentage, auto.", Opt("ax"), Opt("format"));
        Add("num2ruler", "A number as its ruler reads it: num2ruler(x, ax.XAxis).", P("x"), P("ruler"));
        Add("ruler2num", "A ruler value as a plain number: ruler2num(v, ax.XAxis).", P("v"), P("ruler"));
        Add("rticks", "Where the rings of a polar axes go: rticks(0:2:10), rticks('auto'), or rticks to read them back.", Opt("pax"), Opt("values"));
        Add("thetaticks", "Where the spokes stand, in the axes' angle units: thetaticks(0:45:315), or thetaticks to read them back.", Opt("pax"), Opt("values"));
        Add("rticklabels", "What the rings read: rticklabels({'near','far'}), 'auto', 'manual', or rticklabels to read them back.", Opt("pax"), Opt("labels"));
        Add("thetaticklabels", "What the spokes read: thetaticklabels({'N','E','S','W'}), 'auto', 'manual', or thetaticklabels to read them back.", Opt("pax"), Opt("labels"));
        Add("rtickformat", "How the ring numbers are written: rtickformat('%.1f'), or a word such as usd, degrees, percentage, auto.", Opt("pax"), Opt("format"));
        Add("thetatickformat", "How the spoke angles are written: thetatickformat('%g'), or a word such as degrees, auto.", Opt("pax"), Opt("format"));
        Add("rtickangle", "Turns the ring labels of a polar axes: rtickangle(45), or rtickangle to read the angle back.", Opt("pax"), Opt("angle"));
        Add("rlim", "The radial range of a polar axes: rlim([0 5]), rlim('auto'), or rlim to read it back.", Opt("pax"), Opt("limits"));
        Add("thetalim", "The visible turn, in the axes' angle units: thetalim([0 180]) cuts the circle to a half; thetalim reads it back.", Opt("pax"), Opt("limits"));
        Add("polaraxes", "Makes the current axes a circle and returns its handle: polaraxes, polaraxes(pax), polaraxes('ThetaDirection', 'clockwise').", Opt("ax"), Opt("name"), Opt("value"));
        Add("polarplot", "Line plot round a circle, angles in radians: polarplot(theta, rho, spec?), polarplot(rho), polarplot(z).", P("theta"), Opt("rho"), Opt("spec"));
        Add("polarscatter", "Markers round a circle: polarscatter(theta, rho), polarscatter(theta, rho, sz, c, 'filled').", P("theta"), P("rho"), Opt("sz"), Opt("c"));
        Add("polarhistogram", "Histogram of angles in radians: polarhistogram(theta, nbins | edges, 'Normalization', how, 'DisplayStyle', 'bar' | 'stairs').", P("theta"), Opt("bins"), Opt("options"));
        Add("rose", "Angular histogram as petal outlines over a full turn: rose(theta, nbins | centers), [tout, rout] = rose(theta).", P("theta"), Opt("bins"));
        Add("polar", "Line plot round a circle, the older name for polarplot: polar(theta, rho), polar(theta, rho, spec).", P("theta"), Opt("rho"), Opt("spec"));
        Add("polarbubblechart", "Bubbles sized by a third variable, drawn round a circle: polarbubblechart(theta, rho, sz), polarbubblechart(theta, rho, sz, c).", P("theta"), P("rho"), P("sz"), Opt("c"));
        Add("compass", "Arrows from the middle of a polar chart out to each point: compass(u, v), compass(z), with a line spec and options.", P("u"), Opt("v"), Opt("spec"));
        Add("feather", "The same arrows spread along the x axis in sample order: feather(u, v), feather(z), with a line spec and options.", P("u"), Opt("v"), Opt("spec"));
        Add("plot", "Line plot: plot(y), plot(x, y, spec?), or plot(table, xColumn, yColumn, spec?), with LineWidth, Color, LineStyle, Marker, MarkerSize, MarkerEdgeColor, MarkerFaceColor, MarkerIndices, LineJoin and AlignVertexCenters.", P("x"), P("y"), Opt("spec"), Opt("options"));
        Add("scatter", "Scatter plot: scatter(x, y), scatter(x, y, sz, c, 'filled'), or scatter(table, xColumn, yColumn).", P("x"), P("y"), Opt("sz"), Opt("c"));
        Add("bar", "Bar chart, one series per column: bar(y), bar(x, y), bar(x, y, width), bar(x, y, 'stacked').", P("x"), P("y"), Opt("width"));
        Add("barh", "Horizontal bar chart, taking everything bar takes: barh(y), barh(x, y, 'stacked').", P("x"), P("y"), Opt("width"));
        Add("stairs", "Stairstep line: stairs(y), stairs(x, y), [xb, yb] = stairs(x, y) for the path alone.", P("x"), Opt("y"));
        Add("area", "Filled band under a series, stacked one per column: area(y), area(x, y), area(x, y, baseValue).", P("x"), P("y"), Opt("baseValue"));
        Add("pie", "Pie chart on round, frameless axes: pie(x), pie(x, explode), pie(x, labels).", P("x"), Opt("explode"), Opt("labels"));
        Add("heatmap", "Labelled grid of coloured cells: heatmap(cdata), heatmap(xlabels, ylabels, cdata), heatmap(tbl, xvar, yvar).", P("cdata"), Opt("ylabels"), Opt("cdata"));
        Add("boxchart", "A box and whiskers per group: boxchart(ydata), boxchart(xgroupdata, ydata), 'GroupByColor', c.", P("ydata"), Opt("ydata"), Opt("options"));
        Add("bubblechart", "Bubbles sized by a third variable: bubblechart(x, y, sz), bubblechart(x, y, sz, c).", P("x"), P("y"), P("sz"), Opt("c"));
        Add("bubblesize", "The smallest and largest bubble diameter in points: bubblesize([4 25]), bubblesize.", Opt("sizelimits"));
        Add("bubblelim", "The values mapped onto the bubble sizes: bubblelim([0 100]), bubblelim('auto'), bubblelim.", Opt("limits"));
        Add("bubblelegend", "Legends the bubble sizes: bubblelegend, bubblelegend(title), 'Style', 'telescopic'.", Opt("title"), Opt("options"));
        Add("pareto", "Contributions ranked largest first, with the running share on a second ruler: pareto(y), pareto(y, names), pareto(y, 0.8).", P("y"), Opt("names"), Opt("threshold"));
        Add("plotmatrix", "A grid of scatter plots, one per pair of columns: plotmatrix(x), plotmatrix(x, y), [h, ax] = plotmatrix(x).", P("x"), Opt("y"), Opt("spec"));
        Add("plotyy", "Two series against two y scales on one axes: plotyy(x1, y1, x2, y2), plotyy(x1, y1, x2, y2, 'bar').", P("x1"), P("y1"), P("x2"), P("y2"), Opt("verb"), Opt("verb2"));
        Add("stackedplot", "One panel per variable, stacked over a shared x: stackedplot(tbl), stackedplot(tbl, vars), stackedplot(X, Y), 'XVariable', 'DisplayLabels'.", P("tbl"), Opt("vars"), Opt("options"));
        Add("scatterhistogram", "Points with each coordinate's distribution drawn beside them: scatterhistogram(x, y), scatterhistogram(tbl, xvar, yvar), 'GroupVariable', 'NumBins'.", P("x"), P("y"), Opt("yvar"), Opt("options"));
        Add("stem", "Stem plot: stem(y), stem(x, y), with a LineSpec, 'filled', and Color, LineStyle, LineWidth, Marker, MarkerSize, MarkerEdgeColor, MarkerFaceColor, BaseValue and ShowBaseLine.", P("x"), Opt("y"), Opt("spec"), Opt("options"));
        Add("histogram", "Counts values into bins and draws them: histogram(x), histogram(x, nbins | edges), histogram(categories), histogram('BinEdges', e, 'BinCounts', n), histogram(table, column), with BinWidth, BinLimits, BinMethod, NumBins, Normalization, DisplayStyle, Orientation, BarWidth, FaceColor, EdgeColor, FaceAlpha, EdgeAlpha, LineWidth, LineStyle, DisplayOrder, NumDisplayBins and ShowOthers.", Opt("values"), Opt("bins"), Opt("options"));
        Add("errorbar", "Line plot with error bars: errorbar(x, y, err), errorbar(x, y, neg, pos), errorbar(x, y, yneg, ypos, xneg, xpos), with 'vertical', 'horizontal' or 'both', a LineSpec, the table form, and CapSize, Color, LineStyle, LineWidth, Marker, MarkerSize, MarkerEdgeColor and MarkerFaceColor.", P("x"), P("y"), P("error"), Opt("pos"), Opt("options"));
        Add("semilogx", "Line plot with a logarithmic x axis: semilogx(x, y) or semilogx(y).", Opt("x"), P("y"), Opt("spec"));
        Add("semilogy", "Line plot with a logarithmic y axis: semilogy(x, y) or semilogy(y).", Opt("x"), P("y"), Opt("spec"));
        Add("loglog", "Line plot with logarithmic x and y axes: loglog(x, y) or loglog(y).", Opt("x"), P("y"), Opt("spec"));
        Add("title", "Sets the current axes title, with optional text properties: title('t', 'Color', 'r', 'FontSize', 14).", P("text"), Opt("name"), Opt("value"));
        Add("subtitle", "Sets a second line under the axes title, with the same text properties title takes.", P("text"), Opt("name"), Opt("value"));
        Add("sgtitle", "Sets a title over the whole figure, above every subplot in it.", P("text"), Opt("name"), Opt("value"));
        Add("xlabel", "Sets the x-axis label, with optional text properties.", P("text"), Opt("name"), Opt("value"));
        Add("ylabel", "Sets the label of the active y ruler, with optional text properties.", P("text"), Opt("name"), Opt("value"));
        Add("box", "Turns the rectangular frame around the axes on (default) or off.", Opt("on"));
        Add("xline", "Draws a vertical reference line at x, or one per value: xline(0), xline([1 2], '--r', 'limit').", P("x"), Opt("linespec"), Opt("label"));
        Add("yline", "Draws a horizontal reference line at y, or one per value: yline(mean(v), '-k', 'mean').", P("y"), Opt("linespec"), Opt("label"));
        Add("clabel", "Writes each contour level's value into its own curve: [C, h] = contour(X, Y, Z); clabel(C, h).", P("C"), Opt("h"), Opt("levels"));
        Add("texlabel", "The TeX an expression written in plain characters would have been: texlabel('lambda12^(3y)').", P("expression"), Opt("literal"));
        Add("xlim", "The x-axis range: xlim([0 10]), xlim(0, 10), xlim('auto'), xlim('manual'), or xlim to read it back.", Opt("ax"), Opt("limits"), Opt("max"));
        Add("ylim", "The range of the active y ruler: ylim([0 10]), ylim(0, 10), ylim('auto'), ylim('manual'), or ylim to read it back.", Opt("ax"), Opt("limits"), Opt("max"));
        Add("yyaxis", "Makes one side's y ruler active, so the label, limits, ticks, and the plots drawn next belong to it: yyaxis left or yyaxis right.", P("side"));
        Add("grid", "Turns grid lines on (default) or off; 'minor' toggles the minor lines instead.", Opt("on"));
        Add("hold", "Keeps existing series when plotting more (default on).", Opt("on"));
        Add("legend", "Shows the legend, named by a list of series names or built from a vector of line handles, with an optional 'Location'.", P("names"), Opt("location"));
        Add("linkaxes", "Links a vector of axes handles so they pan and zoom together along 'x', 'y', or 'xy'.", P("axes"), Opt("which"));
        Add("show", "Shows the current figure (or figure fig) in its own window.", Opt("fig"));

        // --- 3D surfaces, contours, and images -------------------------------------------------
        Add("meshgrid", "Returns [X, Y] coordinate matrices over the x and y vectors: let [X, Y] = meshgrid(x, y). One vector means the same one per output asked for, so [X, Y, Z] = meshgrid(v) is a cube.", P("x"), Opt("y"), Opt("z"));
        Add("surf", "Colormap-filled 3D surface of matrix z: surf(z) or surf(x, y, z), then any surface properties as name/value pairs — 'FaceAlpha', 'EdgeColor', 'FaceColor'. Drag to rotate.", P("x"), P("y"), P("z"), Opt("options"));
        Add("mesh", "Wireframe 3D surface of matrix z: mesh(z) or mesh(x, y, z).", P("x"), P("y"), P("z"));
        Add("meshc", "Wireframe 3D surface with contour lines projected on the floor.", P("x"), P("y"), P("z"));
        Add("contour", "Iso-line contours at auto (or explicit) levels: contour(z), contour(z, levels), or contour(x, y, z, levels).", P("x"), P("y"), P("z"), Opt("levels"));
        Add("contourf", "Filled contour bands of matrix z at auto (or explicit) levels.", P("x"), P("y"), P("z"), Opt("levels"));
        Add("imagesc", "Displays matrix z as a colormapped heatmap over its cell indices.", P("z"));
        Add("pcolor", "Displays matrix z as a colormapped heatmap over the x/y extents.", P("x"), P("y"), P("z"));
        Add("zlabel", "Sets the z-axis label of a 3D axes, with optional text properties.", P("text"), Opt("name"), Opt("value"));
        Add("zlim", "The z-axis range of a 3D axes: zlim([0 10]), zlim(0, 10), zlim('auto'), zlim('manual'), or zlim to read it back.", Opt("ax"), Opt("limits"), Opt("max"));
        Add("view", "Reads or sets the 3D camera angles in degrees: view(az, el), view([az el]), view(2), or view(3).", Opt("azimuth"), Opt("elevation"));
        Add("campos", "Reads or sets the camera position in data coordinates; only its direction from the box centre matters.", Opt("position"));
        Add("camtarget", "The point the camera looks at — always the centre of the data box.", Opt("target"));
        Add("camup", "The direction that appears as up on screen — always the +z axis.", Opt("up"));
        Add("camorbit", "Turns the camera by an azimuth and elevation increment in degrees.", P("dtheta"), P("dphi"));
        Add("camzoom", "Zooms the current axes about the centre of its limits; factors above 1 zoom in.", P("factor"));
        Add("camva", "Reads or sets the camera view angle in degrees, applied as a zoom about the default framing.", Opt("angle"));
        Add("pbaspect", "Reads or sets the relative side lengths of the 3D plot box, or 'auto' for a cube.", Opt("aspect"));
        Add("daspect", "Reads or sets how many data units one box unit is worth on each axis, or 'auto'.", Opt("aspect"));
        Add(
            "colormap",
            "Applies a colormap to the current axes' plots: a built-in name (parula, viridis, turbo, "
                + "jet, hot, cool, gray, hsv, bone, copper, pink, spring, summer, autumn, winter, "
                + "lines, flag, prism) or an m-by-3 table of RGB rows. With no argument it reads the "
                + "current map back as a table.",
            Opt("map"));
        Add("colorbar", "Shows (default) or hides the current axes' colorbar.", Opt("on"));
        Add("caxis", "Reads or sets the color limits of the current axes: caxis([lo hi]), caxis(lo, hi), or caxis('auto').", Opt("limits"), Opt("high"));
        Add("clim", "The same as caxis: reads or sets the current axes' color limits.", Opt("limits"), Opt("high"));
        Add("brighten", "Brightens (beta > 0) or darkens (beta < 0) the current colormap.", P("beta"));
        Add("colororder", "Reads or sets the colors plots cycle through in the current axes.", Opt("colors"));
        Add("plot3", "A line through points in space: plot3(x, y, z, spec?). Matrix arguments draw one line per column.", P("x"), P("y"), P("z"), Opt("spec"));
        Add("scatter3", "Markers at points in space: scatter3(x, y, z, sizes?, colors?, 'filled'?).", P("x"), P("y"), P("z"), Opt("sizes"), Opt("colors"));
        Add("fill", "A filled polygon: fill(x, y, color). A matrix fills one polygon per column.", P("x"), P("y"), P("color"));
        Add("fill3", "A filled polygon in space: fill3(x, y, z, color).", P("x"), P("y"), P("z"), P("color"));
        Add("patch", "Filled polygons: patch(x, y, color), patch(x, y, z, color), or patch('Faces', F, 'Vertices', V).", P("x"), P("y"), Opt("z"), Opt("color"));
        Add("line", "A line added to the current axes without clearing it: line(x, y) or line(x, y, z).", P("x"), P("y"), Opt("z"));
        Add("text", "A text label at a point: text(x, y, string) or text(x, y, z, string).", P("x"), P("y"), P("string"));
        Add("surface", "The same surface as surf, drawn without resetting the axes.", P("x"), Opt("y"), Opt("z"));
        Add("surfl", "A 3D surface lit from beside the camera (surf plus a light 45 degrees round from the view).", P("x"), Opt("y"), Opt("z"));
        Add("surfnorm", "The unit surface normals of a grid, as [nx, ny, nz] in data units.", P("x"), Opt("y"), Opt("z"));
        Add("surfc", "A filled 3D surface with contour lines projected on the floor.", P("x"), Opt("y"), Opt("z"));
        Add("meshz", "A wireframe 3D surface with a curtain dropped from its edges to the floor.", P("x"), Opt("y"), Opt("z"));
        Add("waterfall", "Each row of z as a curve in space, filled down to a common base.", P("x"), Opt("y"), Opt("z"));
        Add("ribbon", "The columns of y as flat strips standing side by side: ribbon(y) or ribbon(x, y, width).", P("y"), Opt("z"), Opt("width"));
        Add("contour3", "Iso-lines of z drawn in 3D at the height of their own level.", P("x"), P("y"), P("z"), Opt("levels"));
        Add("quiver", "Arrows in the plane: quiver(u, v) or quiver(x, y, u, v), with an optional scale.", P("x"), P("y"), Opt("u"), Opt("v"), Opt("scale"));
        Add("quiver3", "Arrows in space: quiver3(x, y, z, u, v, w), with an optional scale.", P("x"), P("y"), P("z"), P("u"), P("v"), P("w"));
        Add("trisurf", "A triangulated surface over a vertex list: trisurf(tri, x, y, z).", P("tri"), P("x"), P("y"), P("z"), Opt("c"));
        Add("trimesh", "The same triangulation drawn as colored edges only.", P("tri"), P("x"), P("y"), P("z"), Opt("c"));
        Add("voronoi", "The Voronoi diagram of a point set, drawn: voronoi(x, y), or [vx, vy] = voronoi(...) for the edges instead.", P("x"), P("y"), Opt("tri"), Opt("spec"));
        Add("triplot", "The edges of a triangulation: triplot(tri, x, y), or [xd, yd] = triplot(...) for the path instead.", P("tri"), P("x"), P("y"), Opt("spec"));
        Add("tetramesh", "The faces of a tetrahedral mesh: tetramesh(T, X), with an optional colour per tetrahedron.", P("T"), P("X"), Opt("c"));
        Add("stem3", "Stems rising to points in space: stem3(z), stem3(x, y, z), then 'filled', a line spec and options.", P("x"), Opt("y"), Opt("z"));
        Add("bar3", "A matrix as a field of bars: bar3(z), bar3(y, z), then a width, a layout word ('detached', 'grouped', 'stacked') and options.", P("y"), Opt("z"), Opt("width"));
        Add("bar3h", "The same chart with the bars laid along x instead of standing up.", P("y"), Opt("z"), Opt("width"));
        Add("pie3", "A raised pie chart: pie3(x), pie3(x, explode), pie3(x, labels).", P("x"), Opt("explode"), Opt("labels"));
        Add("binscatter", "The readings counted into a grid of bins and coloured by how many fell in each: binscatter(x, y), binscatter(x, y, nbins).", P("x"), P("y"), Opt("nbins"));
        Add("swarmchart", "A scatter whose crowded points are spread sideways so all of them show: swarmchart(x, y), swarmchart(x, y, sz, c).", P("x"), P("y"), Opt("sz"), Opt("c"));
        Add("swarmchart3", "The same spread in space: swarmchart3(x, y, z), swarmchart3(x, y, z, sz, c).", P("x"), P("y"), P("z"), Opt("sz"), Opt("c"));
        Add("bubblechart3", "Bubbles in space sized by a fourth variable: bubblechart3(x, y, z, sz), bubblechart3(x, y, z, sz, c).", P("x"), P("y"), P("z"), P("sz"), Opt("c"));
        // --- the function plotters -------------------------------------------------------------
        Add("fplot", "A function of x sampled where it bends: fplot(f), fplot(f, [a b]), the parametric fplot(fx, fy), a line spec, then 'MeshDensity' and 'ShowPoles'.", P("f"), Opt("interval"), Opt("spec"));
        Add("fplot3", "A curve in space from three functions of one parameter: fplot3(fx, fy, fz, [t0 t1], spec).", P("fx"), P("fy"), P("fz"), Opt("interval"), Opt("spec"));
        Add("fsurf", "A function of x and y as a filled surface: fsurf(f), fsurf(f, [xmin xmax ymin ymax]), or fsurf(fx, fy, fz) for a parametric one.", P("f"), Opt("domain"));
        Add("fmesh", "The same surface drawn as a wireframe: fmesh(f), fmesh(f, domain), fmesh(fx, fy, fz).", P("f"), Opt("domain"));
        Add("fcontour", "The iso-lines of a function of x and y: fcontour(f), fcontour(f, domain), then 'LevelList', 'LevelStep' or 'Fill'.", P("f"), Opt("domain"));
        Add("fimplicit", "The curve where a function of x and y is zero: fimplicit(f), fimplicit(f, domain).", P("f"), Opt("domain"));
        Add("fimplicit3", "The surface where a function of x, y and z is zero: fimplicit3(f), fimplicit3(f, box).", P("f"), Opt("box"));

        // The legacy spellings: the same drawings over a turn of the circle, and the function may be
        // written as text.
        Add("ezplot", "fplot over [-2*pi 2*pi], from a handle or an expression: ezplot('x*sin(x)'). A two-variable expression draws where it is zero.", P("f"), Opt("domain"));
        Add("ezplot3", "fplot3 over [0 2*pi]: ezplot3(fx, fy, fz), with an optional 'animate'.", P("fx"), P("fy"), P("fz"), Opt("domain"));
        Add("ezpolar", "A function of the angle drawn round the circle over [0 2*pi]: ezpolar(f).", P("f"), Opt("domain"));
        Add("ezsurf", "fsurf over [-2*pi 2*pi]: ezsurf(f) or ezsurf(fx, fy, fz).", P("f"), Opt("domain"));
        Add("ezmesh", "fmesh over [-2*pi 2*pi]: ezmesh(f) or ezmesh(fx, fy, fz).", P("f"), Opt("domain"));
        Add("ezsurfc", "The same surface with contour lines projected on the floor.", P("f"), Opt("domain"));
        Add("ezmeshc", "The same wireframe with contour lines projected on the floor.", P("f"), Opt("domain"));
        Add("ezcontour", "fcontour over [-2*pi 2*pi]: ezcontour(f).", P("f"), Opt("domain"));
        Add("ezcontourf", "The same contours, filled.", P("f"), Opt("domain"));

        // --- volume visualization ----------------------------------------------------------------
        // Every one of these reads the grid X, Y, Z before its readings, or leaves it out and takes
        // the readings to be on the whole numbers.
        Add("isosurface", "The surface where a volume reaches a level: fv = isosurface(X, Y, Z, V, level), or isosurface(...) on its own to draw it.", P("V"), Opt("level"), Opt("colors"));
        Add("isocaps", "The lids that close an isosurface at the sides of its box: fv = isocaps(X, Y, Z, V, level, 'above'|'below').", P("V"), Opt("level"), Opt("side"));
        Add("isonormals", "Which way each vertex of a surface faces, from the volume's own slope: n = isonormals(X, Y, Z, V, vertices) or isonormals(..., patch).", P("V"), P("vertices"));
        Add("isocolors", "The reading of a colour volume at each vertex of a surface: c = isocolors(X, Y, Z, C, vertices) or isocolors(..., patch) to paint it.", P("C"), P("vertices"));
        Add("smooth3", "A volume with each reading averaged over the block around it: smooth3(V), smooth3(V, 'gaussian'|'box', size, sd).", P("V"), Opt("filter"), Opt("size"), Opt("sd"));
        Add("subvolume", "The part of a volume inside a box: [NX, NY, NZ, NV] = subvolume(X, Y, Z, V, [xmin xmax ymin ymax zmin zmax]); NaN leaves a side alone.", P("V"), P("limits"));
        Add("reducevolume", "Every n-th reading of a volume: [NX, NY, NZ, NV] = reducevolume(X, Y, Z, V, [Rx Ry Rz]).", P("V"), P("factors"));
        Add("volumebounds", "The box a grid covers, and the range of its readings: [xmin xmax ymin ymax zmin zmax cmin cmax] = volumebounds(X, Y, Z, V).", P("V"));
        Add("reducepatch", "A surface with about the given share of its faces: [f, v] = reducepatch(fv, 0.2), or reducepatch(p, 0.2) to shrink a drawn one.", P("patch"), Opt("keep"));
        Add("shrinkfaces", "Every face pulled in towards its own centre so the faces come apart: shrinkfaces(p, 0.3).", P("patch"), Opt("factor"));
        Add("surf2patch", "A surface grid as faces and vertices: fv = surf2patch(X, Y, Z), or surf2patch(h), with 'triangles' to cut the quadrilaterals up.", P("X"), Opt("Y"), Opt("Z"), Opt("triangles"));
        Add("curl", "How much a vector field turns: [cx, cy, cz, cav] = curl(X, Y, Z, U, V, W), or curl(X, Y, U, V) for a plane.", P("U"), P("V"), Opt("W"));
        Add("divergence", "How much a vector field spreads out at each point: divergence(X, Y, Z, U, V, W), or divergence(X, Y, U, V).", P("U"), P("V"), Opt("W"));
        Add("interp3", "A volume read at points that need not be on its grid: interp3(X, Y, Z, V, xq, yq, zq).", P("V"), P("xq"), P("yq"), P("zq"));
        Add("stream2", "The traced points of streamlines through a plane field: verts = stream2(X, Y, U, V, sx, sy, [step maxverts]).", P("U"), P("V"), P("sx"), P("sy"));
        Add("stream3", "The traced points of streamlines through a field in space: verts = stream3(X, Y, Z, U, V, W, sx, sy, sz, [step maxverts]).", P("U"), P("V"), P("W"), P("sx"), P("sy"), P("sz"));
        Add("streamline", "Streamlines drawn: streamline(verts) from traced points, or a field to trace first — streamline(X, Y, U, V, sx, sy) in a plane, streamline(X, Y, Z, U, V, W, sx, sy, sz) in space, either with the grid left out.", P("verts"));
        Add("streamslice", "Streamlines started on a lattice, so no starting points need choosing: streamslice(X, Y, U, V, density) over a plane, or streamslice(X, Y, Z, U, V, W, sx, sy, sz) on axis-aligned planes through a volume, with 'arrows'/'noarrows', an interpolation method, and [verts, averts] for the vertices instead of a drawing.", P("U"), P("V"), Opt("density"));
        Add("streamribbon", "A band along each streamline, turning the way the field turns: streamribbon(X, Y, Z, U, V, W, sx, sy, sz, width).", P("U"), P("V"), P("W"), P("sx"), P("sy"), P("sz"));
        Add("streamtube", "A round tube along each streamline, widening where the field spreads: streamtube(X, Y, Z, U, V, W, sx, sy, sz, scale).", P("U"), P("V"), P("W"), P("sx"), P("sy"), P("sz"));
        Add("coneplot", "An arrowhead at each given place, pointing the way the field points: coneplot(X, Y, Z, U, V, W, Cx, Cy, Cz, scale, 'quiver').", P("U"), P("V"), P("W"), P("Cx"), P("Cy"), P("Cz"));
        Add("contourslice", "Contours on planes cut through a volume, drawn where the planes are: contourslice(X, Y, Z, V, Sx, Sy, Sz, levels).", P("V"), P("Sx"), P("Sy"), P("Sz"));

        Add("ndgrid", "Coordinate arrays over the given vectors, the first running down the first dimension: [X, Y, Z] = ndgrid(x, y, z). meshgrid swaps the first two.", P("x"), Opt("y"), Opt("z"));

        // --- figure tooling ------------------------------------------------------------------------
        Add("annotation", "Draws on the figure rather than on any axes, in coordinates normalized to it: annotation('arrow', [0.2 0.5], [0.3 0.7]) or annotation('textbox', [x y w h], 'String', 'note'). The kinds are rectangle, ellipse, textbox, line, arrow, doublearrow, and textarrow.", P("kind"), Opt("position"), Opt("name"), Opt("value"));

        // Figures as files. The extension written is .fig and what lands in it is this build's own
        // .graph document, which openfig and hgload read back.
        Add("savefig", "Saves a figure to a file: savefig('name') or savefig(h, 'name.fig').", Opt("h"), P("filename"), Opt("compact"));
        Add("hgsave", "Saves a figure to a file; the older spelling of savefig.", Opt("h"), P("filename"));
        Add("openfig", "Reads a saved figure back and returns a handle to it: h = openfig('name.fig').", P("filename"), Opt("mode"));
        Add("hgload", "Reads a saved figure back; the older spelling of openfig.", P("filename"));
        // The print and export dialogs, and uiaxes (M84) — the six names that stood on the graphics
        // exclusion list as "app building" until the figure they describe turned out to be this one.
        Add("printdlg", "Opens the print dialog and prints the figure; printdlg('-setup', fig) opens page setup.", Opt("fig"));
        Add("printpreview", "Shows the page the figure would print on, with a button to print it.", Opt("fig"));
        Add("pagesetupdlg", "Opens the page-setup dialog, writing the figure's Paper* properties.", Opt("fig"));
        Add("exportsetupdlg", "Opens the export-setup dialog, writing the settings the picture verbs fall back on.", Opt("fig"));
        Add("exportapp", "Writes the window — chrome and all — to an image file.", P("fig"), P("file"));
        Add("uiaxes", "An axes with the app-building defaults: a visible toolbar and a BackgroundColor of its own.", Opt("parent"), Opt("name"), Opt("value"));

        Add("saveas","Writes a figure to a file, format by extension or by name: saveas(gcf, 'plot.png') or saveas(gcf, 'plot', 'pdf'). A .fig writes the document rather than a picture.", Opt("h"), P("filename"), Opt("format"));
        Add("exportgraphics", "Writes a figure to an image or document, format by extension: exportgraphics(gcf, 'plot.pdf', 'ContentType', 'vector').", Opt("h"), P("filename"), Opt("name"), Opt("value"));
        Add("hgexport", "Writes a figure to a file; the older spelling of exportgraphics.", Opt("h"), P("filename"));
        Add("copygraphics", "Puts a figure on the clipboard as an image: copygraphics(gcf, 'Resolution', 300).", Opt("h"), Opt("name"), Opt("value"));
        Add("getframe", "Renders a figure to pixels and returns a frame struct holding cdata and colormap: f = getframe or f = getframe(h).", Opt("h"));
        Add("frame2im", "The picture inside a frame: im = frame2im(f).", P("frame"));
        Add("im2frame", "Turns a picture into a frame movie can play: f = im2frame(im) or im2frame(indices, map).", P("image"), Opt("map"));

        // M108: where a frame goes once getframe has made one.
        Add("VideoWriter", "Creates a video file writer: v = VideoWriter('clip.mp4', 'MPEG-4'). The profile decides the container — 'Motion JPEG AVI' (the default for .avi), 'Uncompressed AVI', 'Grayscale AVI', 'Indexed AVI' or 'MPEG-4'. VideoWriter.getProfiles lists them.", P("filename"), Opt("profile"));
        Add("open", "Opens a VideoWriter for writing: open(v). Nothing is written until the first frame, which is what fixes the frame size.", P("v"));
        Add("writeVideo", "Appends one frame to an open VideoWriter: writeVideo(v, frame). The frame is a getframe struct, an array of them, or an image — uint8 0 to 255, or double 0 to 1.", P("v"), P("frame"));

        // M67: the objects a living figure is built from.
        Add("animatedline", "Creates a line points are added to as a script runs: h = animatedline or animatedline(x, y[, z], 'MaximumNumPoints', n).", Opt("x"), Opt("y"), Opt("z"), Opt("name"), Opt("value"));
        Add("addpoints", "Adds points to an animated line: addpoints(h, x, y) or addpoints(h, x, y, z).", P("h"), P("x"), P("y"), Opt("z"));
        Add("getpoints", "The points an animated line holds: [x, y] = getpoints(h).", P("h"));
        Add("clearpoints", "Empties an animated line without removing it: clearpoints(h).", P("h"));
        Add("rectangle", "Draws a rectangle in the data's own coordinates: rectangle('Position', [x y w h], 'Curvature', [a b]).", Opt("name"), Opt("value"));
        Add("axes", "Creates an axes in the current figure and makes it current, or selects an existing one: ax = axes or axes(ax).", Opt("ax"), Opt("name"), Opt("value"));
        Add("groot", "The root every figure hangs from: get(groot, 'ScreenSize').");
        Add("reset", "Puts a figure or axes back to its default settings and clears what was drawn: reset(gca).", P("h"));
        Add("waitfor", "Waits until an object is deleted, or a property changes or takes a value, running callbacks meanwhile; with nobody to wait on (a batch), returns at once.", P("h"), Opt("property"), Opt("value"));
        Add("hggroup", "Groups drawn objects so they can be shown, hidden and found together: g = hggroup; set(h, 'Parent', g).", Opt("name"), Opt("value"));
        Add("hgtransform", "A group whose Matrix moves its members: t = hgtransform; set(t, 'Matrix', makehgtform('translate', [1 0 0])).", Opt("name"), Opt("value"));

        // What a script hangs on an object, and what it keeps in step.
        Add("setappdata", "Stores a value on a figure object under a name of your own: setappdata(gcf, 'state', s).", P("h"), P("name"), P("value"));
        Add("getappdata", "Reads a value stored with setappdata, or the whole lot as a struct: getappdata(gcf, 'state') or getappdata(gcf).", P("h"), Opt("name"));
        Add("isappdata", "Whether anything is stored under a name: isappdata(gcf, 'state').", P("h"), P("name"));
        Add("rmappdata", "Removes a value stored with setappdata.", P("h"), P("name"));
        Add("linkprop", "Keeps a property the same across several objects: linkprop([ax1 ax2], 'XLim') or linkprop(h, {'Color', 'LineWidth'}).", P("handles"), P("properties"));
        Add("refresh", "Redraws a figure now.", Opt("h"));
        Add("alpha", "Sets how transparent the filled plots in the current axes are: alpha(0.5), alpha('opaque'), alpha('clear').", P("value"));
        Add("alim", "The opacity range. This build has a transparency per object rather than a mapping, so this reads [0 1] and setting it changes nothing.", Opt("limits"));
        Add("alphamap", "The opacity ramp. As alim, this build has no mapping, so this reads the default ramp and setting it changes nothing.", Opt("map"));
        Add("rendererinfo", "What drew the figure, as a struct: r = rendererinfo or rendererinfo(h).", Opt("h"));

        // Motion. Each of these draws its finished picture and then, if a window can show it, replays
        // how it got there — so a run with no window still gets the drawing.
        Add("comet", "Draws a curve and, in a window, travels along it: comet(y), comet(x, y), or comet(x, y, p) for the tail length.", P("x"), Opt("y"), Opt("p"));
        Add("comet3", "As comet, in space: comet3(x, y, z) or comet3(x, y, z, p).", P("x"), P("y"), P("z"), Opt("p"));
        Add("movie", "Plays frames captured with getframe: movie(F), movie(F, n), or movie(F, n, fps).", P("frames"), Opt("times"), Opt("fps"));
        Add("streamparticles", "Draws markers spread along traced streamlines: streamparticles(verts, n).", P("verts"), Opt("n"), Opt("name"), Opt("value"));
        Add("interpstreamspeed", "Respaces traced streamlines so even steps cover even distance: interpstreamspeed(verts, factor).", P("verts"), Opt("factor"));

        // The verbs that would wait for a mouse.
        Add("pan", "Turns panning on or off, or reads which it is: pan on, pan off, pan(gcf).", Opt("h"), Opt("state"));
        Add("datacursormode", "Turns data tips on or off, or reads which it is: datacursormode on, datacursormode(gcf).", Opt("h"), Opt("state"));
        Add("gtext", "Places a label where you click. This needs a figure window; use text or annotation to say where instead.", P("text"));
        Add("waitforbuttonpress", "Waits for the next key or mouse button and says which it was: 1 for a key, 0 for a button. This needs a figure window.");
        Add("ginput", "Reads points off a chart by clicking them: [x, y] = ginput(n) for a fixed number, bare ginput until a key is pressed, and [x, y, button] for which button each was. This needs a figure window.", Opt("n"));
        Add("rotate", "Turns a plot's own data about a direction through an angle in degrees: rotate(h, [0 90], 45) or rotate(h, [x y z], 45, origin).", P("h"), P("direction"), P("angle"), Opt("origin"));
        Add("disableDefaultInteractivity", "Turns off the gestures an axes answers to without a tool being chosen.", Opt("ax"));
        Add("enableDefaultInteractivity", "Turns them back on, giving back whatever the axes was set to.", Opt("ax"));

        // The gestures themselves, as objects a script hands to an axes through ax.Interactions.
        Add("panInteraction", "A pan gesture, optionally held to one direction.", Opt("name"), Opt("value"));
        Add("zoomInteraction", "A wheel-zoom gesture, optionally held to one direction.", Opt("name"), Opt("value"));
        Add("rulerPanInteraction", "A pan by dragging a ruler.", Opt("name"), Opt("value"));
        Add("regionZoomInteraction", "A zoom by dragging out a region.", Opt("name"), Opt("value"));
        Add("rotateInteraction", "A rotate gesture for a three-dimensional axes.");
        Add("dataTipInteraction", "A click that pins a data tip.", Opt("name"), Opt("value"));

        // The two plot-tool verbs that describe a strip of buttons rather than open a window.
        Add("axtoolbar", "The hovering toolbar over an axes, optionally with the buttons it should have.", Opt("ax"), Opt("buttons"), Opt("name"), Opt("value"));
        Add("axtoolbarbtn", "Adds a button to an axes toolbar and answers it.", P("tb"), Opt("style"), Opt("name"), Opt("value"));
        Add("enableLegacyExplorationModes", "Accepted and changes nothing: there is no legacy exploration mode to restore.", Opt("fig"));
        Add("addToolbarExplorationButtons", "Accepted and changes nothing: the figure window has its own toolbar.", Opt("fig"));
        Add("removeToolbarExplorationButtons", "Accepted and changes nothing: the figure window has its own toolbar.", Opt("fig"));

        Add("sphere", "The unit sphere: [X, Y, Z] = sphere(n), or sphere(n) to draw one.", Opt("n"));
        Add("cylinder", "A surface of revolution: [X, Y, Z] = cylinder(r, n), or cylinder(r) to draw one.", Opt("r"), Opt("n"));
        Add("ellipsoid", "An ellipsoid grid: [X, Y, Z] = ellipsoid(xc, yc, zc, xr, yr, zr, n).", P("xc"), P("yc"), P("zc"), P("xr"), P("yr"), P("zr"), Opt("n"));

        // M54 wave F: camera extras and the legacy appearance commands.
        Add("viewmtx", "The 4-by-4 view transformation for a camera: viewmtx(az, el), or viewmtx(az, el, phi, target) for perspective.", P("az"), P("el"), Opt("phi"), Opt("target"));
        Add("makehgtform", "A 4-by-4 transform from named steps, multiplied in order: makehgtform('translate', [1 2 0], 'zrotate', pi/4). Angles in radians.", Opt("name"), Opt("value"));
        Add("camroll", "Turns the camera about the direction it is looking, by an angle in degrees.", P("degrees"));
        Add("camdolly", "Slides the view: camdolly(dx, dy, dz) in fractions of the scene, or with 'movetarget' and 'camera'/'data'.", P("dx"), P("dy"), P("dz"), Opt("targetmode"), Opt("coordsys"));
        Add("campan", "Swings the view by two angles in degrees: campan(dtheta, dphi), optionally in 'camera' or 'data' coordinates.", P("dtheta"), P("dphi"), Opt("coordsys"));
        Add("camlookat", "Frames the objects named, or everything in the axes, by fitting the limits around them.", Opt("handles"));
        Add("camproj", "Reads the projection back, always 'orthographic'; setting it accepts 'orthographic' or 'perspective'.", Opt("projection"));
        Add("colorcube", "A colormap of regularly spaced RGB-cube colours plus pure and grey ramps: colorcube(64).", Opt("m"));
        Add("rgbplot", "Plots a colormap's three columns against the row number, in red, green and blue.", P("map"));
        Add("validatecolor", "A colour as an RGB row in [0, 1]: validatecolor('r'), validatecolor({'r' '#00FF00'}, 'multiple').", P("color"), Opt("one|multiple"));
        Add("diffuse", "Diffuse reflectance of a surface with normals Nx, Ny, Nz lit from S — an [x y z] or [az el] direction.", P("Nx"), P("Ny"), P("Nz"), P("S"));
        Add("specular", "Specular reflectance toward a viewer at V of a surface lit from S; the spread exponent defaults to 10.", P("Nx"), P("Ny"), P("Nz"), P("S"), P("V"), Opt("spread"));
        Add("contrast", "A grey colormap that spreads a picture's own histogram evenly: colormap(contrast(X)).", P("X"), Opt("m"));
        Add("hidden", "Whether a mesh hides what is behind it, by painting its faces the axes background: hidden on, hidden off.", Opt("on"));
        Add("orient", "The paper orientation, always 'portrait'; 'landscape' and 'tall' are accepted and change nothing.", Opt("orientation"));
        Add("whitebg", "Puts the figure on a background colour and moves the ink to suit it; with no colour it toggles light and dark.", Opt("fig"), Opt("color"));
        Add("colordef", "The whole light or dark look by name: colordef white, colordef black, colordef none.", Opt("fig"), P("choice"));
        Add("opengl", "Accepted and does nothing — there is no renderer to select.", Opt("mode"));
        Add("cmpermute", "Shuffles a colormap and reindexes the picture to match: [Y, newmap] = cmpermute(X, map, order).", P("X"), P("map"), Opt("order"));
        Add("cmunique", "The same picture over a palette with no colour twice: [Y, newmap] = cmunique(X, map), or cmunique(RGB).", P("X"), Opt("map"));
        Add("dither", "Trades resolution for depth by error diffusion: dither(RGB, map) indexes, dither(I) gives black and white.", P("X"), Opt("map"));

        foreach (string name in JgsBuiltins.ColormapGeneratorNames)
        {
            Add(name, $"The {name} colormap as an m-by-3 table of RGB rows (default 256).", Opt("m"));
        }
        Add("savefigure", "Saves the current figure (or figure fig) as a .graph document.", P("path"), Opt("fig"));
        Add("loadfigure", "Loads a .graph document as a new figure, makes it current, and returns its handle.", P("path"));
        Add("exportfigure", "Exports the current figure (or figure fig) as an image — png/jpg/bmp/tiff/svg/pdf by extension.", P("path"), Opt("fig"));

        return infos;
    }
}
