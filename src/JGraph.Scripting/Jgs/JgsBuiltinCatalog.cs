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
        Add("fft2", "The two-dimensional discrete Fourier transform of a matrix.", P("a"));
        Add("ifft2", "The inverse two-dimensional discrete Fourier transform.", P("a"));
        Add("fftn", "The Fourier transform along every dimension — fft2 for a matrix.", P("a"));
        Add("ifftn", "The inverse transform along every dimension.", P("a"));
        Add("convhull", "The indices of the points on the convex hull, closed and counter-clockwise.", P("x"), P("y"));

        // --- Evaluating text and asking about the workspace -----------------------------------------
        Add("eval", "Runs a string as code in the current scope; a second string runs if the first fails.", P("code"), Opt("onError"));
        Add("evalc", "Runs a string as code and returns everything it printed.", P("code"));
        Add("evalin", "Runs a string as code in the 'base' or 'caller' workspace.", P("workspace"), P("code"));
        Add("assignin", "Creates a variable in the 'base' or 'caller' workspace.", P("workspace"), P("name"), P("value"));
        Add("str2func", "A function handle from its name, or from an @(x) … expression.", P("text"));
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
        Add("rehash", "A no-op: JGraph looks for files when it needs them rather than caching them.", Opt("scope"));
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
        Add("delete", "Deletes the named files.", P("path"));
        Add("fileattrib", "A struct of a file's attributes, or false when it does not exist.", P("path"));
        Add("filesep", "The character that separates folders on this system.");
        Add("filemarker", "The character that separates a file from a function inside it.");
        Add("isfile", "Whether the path names a file that exists.", P("path"));
        Add("isfolder", "Whether the path names a folder that exists.", P("path"));
        Add("fullfile", "Path pieces joined with the right separator.", P("part"), Opt("more"));
        Add("fileparts", "A path split into {folder, name, extension}.", P("path"));
        Add("feof", "Whether an open file is at its end.", P("fid"));
        Add("ferror", "The last error on an open file — empty, since failures are raised instead.", P("fid"), Opt("clear"));
        Add("ftell", "The current byte position in an open file.", P("fid"));
        Add("fseek", "Moves the position in an open file; 0 on success, -1 on failure.", P("fid"), P("offset"), Opt("origin"));
        Add("fgets", "The next line of an open file, keeping its newline; -1 at the end.", P("fid"));
        Add("fscanf", "Numbers or text read from an open file under a scanf format.", P("fid"), P("format"), Opt("count"));
        Add("textscan", "The rest of an open file read under a format, wrapped in a cell.", P("fid"), P("format"));
        Add("type", "Prints a file's contents to the console.", P("path"));
        Add("getenv", "The value of an environment variable, or '' when it is not set.", P("name"));
        Add("setenv", "Sets an environment variable for this process.", P("name"), Opt("value"));
        Add("ispc", "Whether this machine runs Windows.");
        Add("isunix", "Whether this machine runs Linux or macOS.");
        Add("ismac", "Whether this machine runs macOS.");
        Add("namelengthmax", "The longest name a variable may have.");
        Add("cputime", "Seconds of processor time used, for timing a long computation.");
        Add("drawnow", "Flushes pending graphics — nothing to do in JGraph, which draws as it goes.", Opt("mode"));
        Add("jsonencode", "A value written as JSON text.", P("x"), Opt("option"), Opt("value"));
        Add("jsondecode", "JSON text read back as numbers, cells, and structs.", P("text"));

        // --- Array statistics and rearrangement -----------------------------------------------------
        Add("arrayfun", "Applies a function to each element; 'UniformOutput', false gives a cell.", P("f"), P("a"), Opt("option"));
        Add("bsxfun", "Applies a function pairwise, expanding a scalar across the other array.", P("f"), P("a"), P("b"));
        Add("structfun", "Applies a function to each field of a struct.", P("f"), P("s"), Opt("option"));
        Add("struct2cell", "A struct's field values as a cell array.", P("s"));
        Add("cell2struct", "A struct built from a cell of values and a cell of field names.", P("values"), P("names"), Opt("dim"));
        Add("accumarray", "Sums values into bins their subscripts name; a function handle reduces differently.", P("subs"), P("values"), Opt("size"), Opt("f"), Opt("fill"));
        Add("cummax", "The running maximum so far at each position.", P("x"));
        Add("cummin", "The running minimum so far at each position.", P("x"));
        Add("maxk", "The k largest values of each slice, largest first: [b, i] = maxk(x, k, dim).", P("x"), P("k"), Opt("dim"));
        Add("mink", "The k smallest values of each slice, smallest first: [b, i] = mink(x, k, dim).", P("x"), P("k"), Opt("dim"));
        Add("histc", "How many values fall in each bin the edges define, per slice along dim.", P("x"), P("edges"), Opt("dim"));
        Add("uniquetol", "The unique values, treating any two within a tolerance as one: [c, ia, ic] = uniquetol(x, tol, 'ByRows', true, 'DataScale', s, 'OutputAllIndices', true).", P("x"), Opt("tol"), Opt("option"), Opt("value"));
        Add("ismembertol", "Whether each value is within a tolerance of something in the set: [lia, locb] = ismembertol(x, set, tol, 'ByRows', true).", P("x"), P("set"), Opt("tol"), Opt("option"), Opt("value"));
        Add("issortedrows", "Whether a matrix's rows are in lexicographic order.", P("a"));
        Add("randi", "Uniform whole numbers from 1 to imax, or from the range [low high]; a trailing class name (or 'like', x) says what they come back as.", P("imax"), Opt("rows"), Opt("cols"), Opt("class"));
        Add("randperm", "A random permutation of 1..n, or k values drawn from it.", P("n"), Opt("k"));
        Add("rng", "Seeds the random stream, or reports its state: rng(seed), rng('default'), rng('shuffle'), s = rng.", Opt("seed"), Opt("generator"));
        Add("circshift", "The values moved along by k places, wrapping around: circshift(x, k, dim), or a k per dimension.", P("x"), P("k"), Opt("dim"));
        Add("rot90", "A matrix turned a quarter turn counter-clockwise, k times.", P("a"), Opt("k"));
        Add("movmean", "The mean over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movmedian", "The median over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movsum", "The sum over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movprod", "The product over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movmax", "The maximum over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movmin", "The minimum over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movstd", "The standard deviation over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movvar", "The variance over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));
        Add("movmad", "The mean absolute deviation over a sliding window of width k, or a [before after] reach; 'Endpoints' says what an incomplete window at the ends means.", P("x"), P("k"), Opt("dim"), Opt("option"), Opt("value"));

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
        Add("vecnorm", "The p-norm of a vector, or of each column of a matrix (p = 2 by default).", P("a"), Opt("p"));
        Add("schur", "The real Schur form T, or [U, T] with U orthogonal and U*T*U' equal to a.", P("a"), Opt("kind"));
        Add("ordeig", "The eigenvalues of a quasi-triangular matrix, in the order its blocks appear.", P("t"));
        Add("ordschur", "Reorders a Schur form so the selected eigenvalues come first.", P("u"), P("t"), P("select"));
        Add("cholupdate", "The Cholesky factor of r'*r + x*x', or of r'*r - x*x' with '-'.", P("r"), P("x"), Opt("sign"));
        Add("qrupdate", "The QR factors of a + u*v', from the factors of a.", P("q"), P("r"), P("u"), P("v"));
        Add("delaunay", "The Delaunay triangulation of a set of points, as triangle vertex indices.", P("x"), Opt("y"));
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
        Add("full", "x itself — dense storage is the only storage there is.", P("x"));
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
        Add("range", "Values from start (inclusive) to stop (exclusive) in steps of step (default 1).", P("start"), P("stop"), Opt("step"));
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
        Add("prod", "The product of a numeric array (column-wise over matrices in MATLAB).", P("array"));
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
        Add("sparse", "Converts to sparse storage: sparse(A), sparse(m, n), or sparse(i, j, v, m, n).", P("A"));
        Add("sprand", "A sparse random matrix with roughly m*n*density uniform nonzeros.", P("m"), P("n"), P("density"));
        Add("eigs", "The k eigenvalues of largest magnitude (Arnoldi); [V, D] = eigs(A, k) adds Ritz vectors.", P("A"), P("k"));
        Add("spy", "Plots the nonzero pattern of a matrix, row 1 at the top.", P("A"));

        // --- Data types and conversions (M43) ---------------------------------------------------
        Add("table", "Builds a table from column variables; a trailing 'VariableNames', {…} names them (default Var1…VarN).", P("var1"), Opt("var2"));
        Add("timetable", "A table whose first variable is the row times: timetable(rowTimes, var1, …).", P("rowTimes"), P("var1"));
        Add("seconds", "A duration of x seconds (stored as its number, so it transposes and plots).", P("x"));
        Add("categorical", "Category labels from a cell or array (represented as the cell of names).", P("x"));
        Add("summary", "Per-variable statistics of a table, or category counts of a categorical, as a struct.", P("x"));
        Add("string", "The value as strings: cells and arrays convert per element.", P("x"));
        Add("cellstr", "A string array as a cell of character rows.", P("x"));
        Add("compose", "Formats each element through the format string, one output string per element.", P("format"), P("values"));
        Add("missing", "The missing value: a string slot with nothing in it (displays as <missing>).");
        Add("ismissing", "Whether each element is missing (the missing string, or NaN).", P("x"));
        Add("tiledlayout", "Starts an r-by-c tile grid on the current figure; nexttile advances through it.", P("rows"), P("cols"));
        Add("nexttile", "Moves to the next tile of the tiledlayout grid (or tile n).", Opt("n"));
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
        Add("strsplit", "Splits text into a cell of pieces, on a delimiter (or a cell of them) or on whitespace; [C, matches] also reports the delimiters cut on.", P("text"), Opt("delimiter"), Opt("option"), Opt("value"));
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
        Add("struct", "Builds a struct from name/value pairs.", Opt("name"), Opt("value"));
        Add("fieldnames", "The names of a struct's fields, as a cell.", P("s"));
        Add("isfield", "True when the struct has the named field.", P("s"), P("name"));
        Add("rmfield", "A copy of the struct without the named field.", P("s"), P("name"));
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
        Add("datestr", "Formats a serial date number (default: now) as text; format uses .NET date tokens.", Opt("serial"), Opt("format"));
        Add("datetime", "The current local date and time as a 'dd-MMM-yyyy HH:mm:ss' string.");
        Add("date", "The current local date as a 'dd-MMM-yyyy' string.");
        Add("time", "The current time as Unix epoch seconds (UTC), including a fractional part.");

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
        Add("imwrite", "Writes an image to a file; the extension (.png/.jpg/.bmp/.webp) selects the format. Options: 'Quality', 'BitDepth', 'Alpha'.", P("image"), P("path"), Opt("options"));
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
        Add("sum", "The sum of a numeric array, or of every sample in an image.", P("array"));
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
        Add("corrcoef", "Correlation between columns: [r, p, rl, ru] = corrcoef(A, 'Alpha', 0.05, 'Rows', 'complete').", P("A"), Opt("B"), Opt("option"), Opt("value"));
        Add("histcounts", "Values per bin: [n, edges, bin] = histcounts(x, nbins | edges, 'BinWidth', w, 'BinLimits', [a b], 'BinMethod', m, 'Normalization', how).", P("x"), Opt("bins"), Opt("option"), Opt("value"));
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

        // --- Array operations ---------------------------------------------------------------------
        Add("sort", "A sorted copy of a numeric or string array; order \"ascend\" (default) or \"descend\", with 'MissingPlacement' and 'ComparisonMethod' for where NaN lands and how complex numbers order.", P("array"), Opt("order"), Opt("option"), Opt("value"));
        Add("unique", "The distinct values of a numeric or string array: [c, ia, ic] = unique(x, 'rows', 'stable', 'last'), where c = x(ia) and x = c(ic).", P("array"), Opt("option"), Opt("more"));
        Add("find", "Indices of the truthy elements: volt(find(temp > 85)) gathers the matches. In a .m file find(x, k) keeps the first k ('last' for the other end); in JGS the second argument is the index base, 0 by default.", P("mask"), Opt("k"), Opt("direction"));
        Add("any", "Whether at least one element is truthy.", P("array"));
        Add("all", "Whether every element is truthy.", P("array"));
        Add("concat", "One array from arrays and scalars, in order: concat(a, b), concat(a, 5).", P("first"), P("second"));
        Add("slice", "Elements [start, stop) by 0-based index; stop defaults to the array length.", P("array"), P("start"), Opt("stop"));
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
        Add("isstring", "Always false — JGraph has char text, not MATLAB string arrays.", P("x"));
        Add("iscellstr", "True for a cell array whose every element is a string.", P("x"));
        Add("isletter", "Whether each character is a letter, as a mask.", P("text"));
        Add("isspace", "Whether each character is whitespace, as a mask.", P("text"));
        Add("issorted", "Whether the values are in non-decreasing order.", P("x"));
        Add("class", "The class name of a value: double, logical, char, cell, struct, function_handle.", P("x"));
        Add("isa", "Whether a value has the named class, or is 'numeric'/'float'/'integer'.", P("x"), P("type"));
        Add("logical", "The value converted to a logical (true where non-zero).", P("x"));
        Add("cast", "The value converted to the named class.", P("x"), P("type"));
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
        Add("fprintf", "Writes a sprintf-formatted string to the console with no added newline (use \\n in the format).", P("format"), P("values"));
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
        Add("column", "A table column as a numeric array.", P("table"), P("name"));
        Add("colnames", "The table's column names as a string array.", P("table"));
        Add("rowcount", "The number of data rows in the table.", P("table"));
        Add("textcolumn", "A table column as a string array (missing cells become \"\") — for serial numbers and IDs.", P("table"), P("name"));

        // --- Composition and output ---------------------------------------------------------------
        Add("run", "Runs another JGS script into the current global scope (an include).", P("path"));
        Add("clear", "Clears the workspace (or just the named variables) and reverts any rebound built-in. Figures stay open.", Opt("names"));
        Add("clearvars", "Clears the user's variables (all, or just the named ones). Built-ins are untouched.", Opt("names"));
        Add("print", "Writes the values to the console, space-separated.", P("values"));
        Add("clc", "Clears the console display. Variables and figures are untouched.");
        Add("whos", "Lists the workspace's variables with their size and class.");
        Add("save", "Writes workspace variables to a MAT-file (or text with '-ascii'): save file, save('f.mat', 'x').", Opt("path"), Opt("names..."));
        Add("load", "Reads a MAT-file's (or numeric text file's) variables into the workspace.", Opt("path"), Opt("names..."));
        Add("fopen", "Opens a file and returns its id (-1 on failure); modes r (default), w, a, r+.", P("path"), Opt("mode"));
        Add("fclose", "Closes a file id, or every open file with fclose('all').", P("fid"));
        Add("fread", "Reads binary values from a file: fread(fid, count?, precision?) — uint8 by default.", P("fid"), Opt("count"), Opt("precision"));
        Add("fwrite", "Writes values to a file in binary: fwrite(fid, data, precision?) — uint8 by default.", P("fid"), P("data"), Opt("precision"));
        Add("fgetl", "The next text line of a file, without its newline; -1 (a number) at end of file.", P("fid"));
        Add("image", "Displays a matrix as a colormapped image over its cell indices (an image value shows as-is).", P("z"));
        Add("help", "Shows a builtin's signature and summary; help alone lists every function.", Opt("name"));
        Add("format", "Sets numeric display precision: short, long, shortE, longE (bare format resets).", Opt("mode"));
        Add("dir", "The files and folders in the working directory (or matching pattern) as a cell array of names; folders end with the path separator.", Opt("pattern"));
        Add("path", "The folder that bare file names resolve against (the workspace root, or the batch start folder).");

        // --- Figure setup and plotting -------------------------------------------------------------
        Add("figure", "Starts a new figure (or selects figure n) and returns its handle (a figure number, so it starts at 1).", Opt("n"));
        Add("subplot", "Selects cell index of a rows-by-cols axes grid (a grid cell number, so 1-based, row-major) and returns a handle on it.", P("rows"), P("cols"), P("index"));
        Add("close", "Closes the current figure, figure n, or every figure with close all.", Opt("n"));
        Add("clf", "Clears the current figure (or figure n), keeping its window open.", Opt("n"));
        Add("gcf", "The current figure's number.");
        Add("gca", "Selects the current axes, creating a figure and axes if there are none.");
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
        Add("legend", "Shows the legend, named by a list of series names or built from a vector of line handles, with an optional 'Location'.", P("names"), Opt("location"));
        Add("linkaxes", "Links a vector of axes handles so they pan and zoom together along 'x', 'y', or 'xy'.", P("axes"), Opt("which"));
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
                + "lines) or an m-by-3 table of RGB rows.",
            P("map"));
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
        Add("sphere", "The unit sphere: [X, Y, Z] = sphere(n), or sphere(n) to draw one.", Opt("n"));
        Add("cylinder", "A surface of revolution: [X, Y, Z] = cylinder(r, n), or cylinder(r) to draw one.", Opt("r"), Opt("n"));
        Add("ellipsoid", "An ellipsoid grid: [X, Y, Z] = ellipsoid(xc, yc, zc, xr, yr, zr, n).", P("xc"), P("yc"), P("zc"), P("xr"), P("yr"), P("zr"), Opt("n"));

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
