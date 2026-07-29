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
        Add("round", "x rounded to the nearest whole number (halves away from zero), element-wise.", P("x"));
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
        Add("maxk", "The k largest values, largest first.", P("x"), P("k"));
        Add("mink", "The k smallest values, smallest first.", P("x"), P("k"));
        Add("histc", "How many values fall in each bin the edges define.", P("x"), P("edges"));
        Add("uniquetol", "The unique values, treating any two within a tolerance as one.", P("x"), Opt("tol"));
        Add("ismembertol", "Whether each value is within a tolerance of something in the set.", P("x"), P("set"), Opt("tol"));
        Add("issortedrows", "Whether a matrix's rows are in lexicographic order.", P("a"));
        Add("randi", "Uniform whole numbers from 1 to imax, or from the range [low high].", P("imax"), Opt("rows"), Opt("cols"));
        Add("randperm", "A random permutation of 1..n, or k values drawn from it.", P("n"), Opt("k"));
        Add("circshift", "The values moved along by k places, wrapping around.", P("x"), P("k"));
        Add("rot90", "A matrix turned a quarter turn counter-clockwise, k times.", P("a"), Opt("k"));
        Add("movmean", "The mean over a sliding window of width k.", P("x"), P("k"));
        Add("movmedian", "The median over a sliding window of width k.", P("x"), P("k"));
        Add("movsum", "The sum over a sliding window of width k.", P("x"), P("k"));
        Add("movprod", "The product over a sliding window of width k.", P("x"), P("k"));
        Add("movmax", "The maximum over a sliding window of width k.", P("x"), P("k"));
        Add("movmin", "The minimum over a sliding window of width k.", P("x"), P("k"));
        Add("movstd", "The standard deviation over a sliding window of width k.", P("x"), P("k"));
        Add("movvar", "The variance over a sliding window of width k.", P("x"), P("k"));
        Add("movmad", "The mean absolute deviation over a sliding window of width k.", P("x"), P("k"));

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
        Add("regexprep", "Every match of a regular expression replaced; $1 refers to a captured group.", P("text"), P("expr"), P("replacement"), Opt("option"));
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
        Add("linspace", "count evenly spaced values from start to stop, inclusive.", P("start"), P("stop"), P("count"));
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
        Add("ismember", "Whether each element of x is in the set — a mask the shape of x.", P("x"), P("set"));
        Add("dot", "The inner product of two equal-length vectors (conjugating the first when complex).", P("a"), P("b"));
        Add("inv", "The inverse of a square matrix (errors when singular).", P("A"));
        Add("det", "The determinant of a square matrix.", P("A"));
        Add("rank", "The number of linearly independent rows/columns, by singular values above tol.", P("A"), Opt("tol"));
        Add("trace", "The sum of a square matrix's diagonal (complex-aware).", P("A"));
        Add("hilb", "The n-by-n Hilbert matrix, H(i,j) = 1/(i+j-1) — the classic ill-conditioned test matrix.", P("n"));
        Add("polyval", "Evaluates polynomial p (highest power first) at x, element-wise.", P("p"), P("x"));
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
        Add("shading", "Accepted for compatibility: surfaces are always smoothly shaded.", Opt("mode"));
        Add("lighting", "Accepted for compatibility: surface lighting is built in.", Opt("mode"));
        Add("camlight", "Accepted for compatibility: surface lighting is built in.", Opt("position"));
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
        Add("strsplit", "Splits text into a cell of pieces, on a delimiter or on whitespace.", P("text"), Opt("delimiter"));
        Add("strjoin", "Joins a cell (or array) of pieces into one string.", P("parts"), Opt("separator"));
        Add("num2str", "Formats a number as text, optionally to a given number of significant digits.", P("x"), Opt("digits"));
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
        Add("cellfun", "Applies a function to every cell; add 'UniformOutput', false to collect a cell.", P("f"), P("c"), Opt("options..."));
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
        Add("subplot", "Selects cell index of a rows-by-cols axes grid (a grid cell number, so 1-based, row-major).", P("rows"), P("cols"), P("index"));
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
