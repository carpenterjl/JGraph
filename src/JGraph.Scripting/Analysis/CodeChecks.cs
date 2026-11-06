namespace JGraph.Scripting.Analysis;

/// <summary>
/// The Code Analyzer checks JGraph raises (App Designer window plan W10, decision WO5): each with R2025b's
/// id, severity and text, measured headless (<c>w10_analyzer</c>, <c>w10_rules</c>) over files written to
/// raise them. A check R2025b has and JGraph does not is in <see cref="KnownElsewhere"/>, so that a
/// <c>%#ok</c> naming it is neither "no longer generated" nor "no check found"; an id in neither list
/// is <c>MSNE</c>.
/// </summary>
public static class CodeChecks
{
    /// <summary>One check: its id, severity and the text it prints (a template where the text names the code).</summary>
    public sealed record Check(string Id, CodeSeverity Severity, string Text);

    private static readonly Dictionary<string, Check> ByIdMap = new(StringComparer.Ordinal);

    private static Check Add(string id, CodeSeverity severity, string text)
    {
        var check = new Check(id, severity, text);
        ByIdMap[id] = check;
        return check;
    }

    // --- the unused family ----------------------------------------------------------------------------
    public static readonly Check INUSD = Add("INUSD", CodeSeverity.Warning, "Input argument might be unused. Consider replacing the argument with ~ instead.");
    public static readonly Check MANU = Add("MANU", CodeSeverity.Warning, "Input argument might be unused. Consider replacing the argument with ~, or make this method Static instead.");
    public static readonly Check NASGU = Add("NASGU", CodeSeverity.Warning, "Value assigned to variable might be unused.");
    public static readonly Check ASGLU = Add("ASGLU", CodeSeverity.Warning, "Value assigned to variable might be unused. Consider replacing the variable with ~ instead.");
    public static readonly Check STRNU = Add("STRNU", CodeSeverity.Warning, "This variable, apparently a structure, is changed but the value might be unused.");
    public static readonly Check NUSED = Add("NUSED", CodeSeverity.Warning, "Global or persistent variable might be unused or unset in this function or script.");
    public static readonly Check PUSE = Add("PUSE", CodeSeverity.Warning, "Persistent variable might be unused.");
    public static readonly Check PSET = Add("PSET", CodeSeverity.Warning, "Persistent variable is used, but might be unset.");
    public static readonly Check STOUT = Add("STOUT", CodeSeverity.Warning, "Function return value might be unset.");
    public static readonly Check DEFNU = Add("DEFNU", CodeSeverity.Warning, "Function might be unused.");
    public static readonly Check NODEF = Add("NODEF", CodeSeverity.Warning, "Variable might be used before it is defined.");
    public static readonly Check UNRCH = Add("UNRCH", CodeSeverity.Warning, "This statement (and possibly following ones) cannot be reached.");
    public static readonly Check AGROW = Add("AGROW", CodeSeverity.Info, "Variable appears to change size on every loop iteration. Consider preallocating for speed.");
    public static readonly Check GVMIS = Add("GVMIS", CodeSeverity.Warning, "Global variables are inefficient and make errors difficult to diagnose. Use a function with input variables instead.");
    public static readonly Check REDEFGG = Add("REDEFGG", CodeSeverity.Warning, "Declaring a variable to be global more than once might not be supported in a future release.");
    public static readonly Check NOANS = Add("NOANS", CodeSeverity.Warning, "Using ANS as a variable is not recommended as ANS is frequently overwritten by MATLAB.");
    public static readonly Check LTARG = Add("LTARG", CodeSeverity.Warning, "Function might be called with too few arguments.");
    public static readonly Check MSNU = Add("MSNU", CodeSeverity.Info, "A Code Analyzer message was once suppressed here, but the message is no longer generated.");
    public static readonly Check MSNE = Add("MSNE", CodeSeverity.Info, "No Code Analyzer check is found for this check ID.");

    // --- statements without effect, and output not hidden --------------------------------------------
    public static readonly Check VUNUS = Add("VUNUS", CodeSeverity.Warning, "'{0}' produces a value that might be unused.");
    public static readonly Check NOEFF = Add("NOEFF", CodeSeverity.Warning, "The operation or expression '{0}' has no evident effect.");
    public static readonly Check EQEFF = Add("EQEFF", CodeSeverity.Warning, "To assign values to variables, use =. The == operator compares equality of values.");
    public static readonly Check NOPTS = Add("NOPTS", CodeSeverity.Info, "Add a semicolon after the statement to hide the output (in a script).");
    public static readonly Check NOPRT = Add("NOPRT", CodeSeverity.Info, "Add a semicolon after the statement to hide the output (in a function).");
    public static readonly Check NOCOMMA = Add("NOCOMMA", CodeSeverity.Info, "Extra comma is unnecessary.");
    public static readonly Check NOSEMI = Add("NOSEMI", CodeSeverity.Info, "Extra semicolon is unnecessary.");
    public static readonly Check SUBSASGN = Add("SUBSASGN", CodeSeverity.Error, "Output of SUBSASGN must be assigned to a variable.");
    public static readonly Check SYNER = Add("SYNER", CodeSeverity.Error, "Parse error at {0}: usage might be invalid MATLAB syntax.");

    // --- clear, load, eval, feval, exist ------------------------------------------------------------
    public static readonly Check CLEAR0ARGS = Add("CLEAR0ARGS", CodeSeverity.Info, "Avoid using 'clear' to clear more than necessary, this decreases code performance and is usually unnecessary.");
    public static readonly Check CLALL = Add("CLALL", CodeSeverity.Info, "Using 'clear' with the 'all' option usually decreases code performance and is often unnecessary.");
    public static readonly Check CLFUNC = Add("CLFUNC", CodeSeverity.Info, "Using 'clear' with the 'functions' option usually decreases code performance and is often unnecessary.");
    public static readonly Check LOAD = Add("LOAD", CodeSeverity.Warning, "To avoid conflicts with functions on the path, specify variables to load from file.");
    public static readonly Check EVLCS = Add("EVLCS", CodeSeverity.Warning, "'eval' is inefficient and makes code less clear. Call the statement directly.");
    public static readonly Check EVLEQ = Add("EVLEQ", CodeSeverity.Warning, "'eval' is inefficient and makes code less clear. Assign to the variable directly.");
    public static readonly Check EVLDUAL = Add("EVLDUAL", CodeSeverity.Warning, "This use of 'eval' is unnecessary and can be removed. Call the evaluated function directly using parentheses. For example, use 'load(filename)' instead of 'eval(['load ' filename])'.");
    public static readonly Check EV2IN = Add("EV2IN", CodeSeverity.Warning, "Using 'eval' with two arguments is not recommended. Use try/catch statements instead to make code more clear and efficient.");
    public static readonly Check FVAL = Add("FVAL", CodeSeverity.Warning, "Calling functions using 'feval' is usually not necessary. Call the function directly instead.");
    public static readonly Check EXIST = Add("EXIST", CodeSeverity.Info, "EXIST with two input arguments is generally faster and clearer than with one input argument.");

    // --- comparisons and sizes ----------------------------------------------------------------------
    public static readonly Check ISCL = Add("ISCL", CodeSeverity.Info, "To improve performance, use 'isscalar' instead of length comparison.");
    public static readonly Check ISMT = Add("ISMT", CodeSeverity.Info, "Using ISEMPTY is usually faster than comparing LENGTH to 0.");
    public static readonly Check ISMAT = Add("ISMAT", CodeSeverity.Info, "When checking if a variable is a matrix consider using ISMATRIX.");
    public static readonly Check ELARLOG = Add("ELARLOG", CodeSeverity.Warning, "The {0} operator in the expression {1}(A {0} B) is unexpected. Should this be {1}(A) {0} B?");
    public static readonly Check STCI = Add("STCI", CodeSeverity.Warning, "Use STRCMPI(str1,str2) instead of using UPPER/LOWER in a call to STRCMP.");
    public static readonly Check STNCI = Add("STNCI", CodeSeverity.Warning, "Use STRNCMPI(str1,str2) instead of using UPPER/LOWER in a call to STRNCMP.");
    public static readonly Check STCUL = Add("STCUL", CodeSeverity.Warning, "The comparison will likely fail due to case mismatch.");
    public static readonly Check STLOW = Add("STLOW", CodeSeverity.Info, "In this comparison the call to UPPER/LOWER is unnecessary.");

    // --- readability ----------------------------------------------------------------------------------
    public static readonly Check CHARTEN = Add("CHARTEN", CodeSeverity.Info, "For readability, consider using 'newline' instead of 'char(10)'.");
    public static readonly Check STRQUOT = Add("STRQUOT", CodeSeverity.Info, "string('...') is not recommended. Use \"...\" instead.");
    public static readonly Check LOGL = Add("LOGL", CodeSeverity.Info, "Use 'true' or 'false' instead of 'logical(1)' or 'logical(0)'.");
    public static readonly Check RPMT1 = Add("RPMT1", CodeSeverity.Info, "For readability, consider using 'ones(x,y)' instead of 'repmat(1,x,y)'.");
    public static readonly Check RPMT0 = Add("RPMT0", CodeSeverity.Info, "For readability, consider using 'zeros(x,y)' instead of 'repmat(0,x,y)'.");
    public static readonly Check RPMTT = Add("RPMTT", CodeSeverity.Info, "For readability, consider using 'true(x,y)' instead of 'repmat(true,x,y)'.");
    public static readonly Check RPMTF = Add("RPMTF", CodeSeverity.Info, "For readability, consider using 'false(x,y)' instead of 'repmat(false,x,y)'.");
    public static readonly Check NBRAK2 = Add("NBRAK2", CodeSeverity.Info, "Use of brackets [] is unnecessary.");
    public static readonly Check NO4LP = Add("NO4LP", CodeSeverity.Info, "Parentheses are not needed in a FOR statement.");
    public static readonly Check DSPSP = Add("DSPSP", CodeSeverity.Info, "'disp(sprintf(...))' can usually be replaced by 'fprintf(...\\n)'.");
    public static readonly Check GFLD = Add("GFLD", CodeSeverity.Info, "Use dynamic fieldnames with structures instead of GETFIELD.");
    public static readonly Check SFLD = Add("SFLD", CodeSeverity.Info, "Use dynamic fieldnames with structures instead of SETFIELD.");
    public static readonly Check ST2NM = Add("ST2NM", CodeSeverity.Info, "If you are operating on scalar values, consider using 'str2double' for faster performance.");
    public static readonly Check LAXES = Add("LAXES", CodeSeverity.Info, "Calling AXES(h) in a loop can be slow. Consider moving the call to AXES outside the loop.");

    // --- names against their files (packages and class folders plan, stage P5, ADR 0311) ------------
    public static readonly Check FNDEF = Add("FNDEF", CodeSeverity.Warning, "Function name '{0}' is known to MATLAB by its file name: '{1}'.");
    public static readonly Check FNDOT = Add("FNDOT", CodeSeverity.Error, "Function name can only contain dots if it is a class method.");
    public static readonly Check MCFIL = Add("MCFIL", CodeSeverity.Error, "Class name '{0}' and file name do not agree: '{1}'. Update the class name and constructor, if defined, or change the file name to match the class name.");
    public static readonly Check MCDIR = Add("MCDIR", CodeSeverity.Error, "Class name '{0}' and @directory name do not agree: '{1}'.");
    public static readonly Check NOIN = Add("NOIN", CodeSeverity.Warning, "Method '{0}' should either be a static method or have at least one input argument.");

    // --- functions not recommended or to be removed -------------------------------------------------
    public static readonly Check DATST = Add("DATST", CodeSeverity.Info, "'datestr' is not recommended. With appropriate code changes, use 'datetime' instead.");
    public static readonly Check DATNM = Add("DATNM", CodeSeverity.Info, "'datenum' is not recommended. With appropriate code changes, use 'datetime' instead.");
    public static readonly Check DATE = Add("DATE", CodeSeverity.Info, "'date' is not recommended. With appropriate code changes, use 'datetime(\"today\")' instead.");
    public static readonly Check TNOW1 = Add("TNOW1", CodeSeverity.Info, "'now' is not recommended. With appropriate code changes, use 'datetime(\"now\")' instead.");
    public static readonly Check HIST = Add("HIST", CodeSeverity.Info, "'hist' is not recommended. With appropriate code changes, use 'histogram' instead.");
    public static readonly Check DQUAD = Add("DQUAD", CodeSeverity.Info, "'quad' is not recommended. With appropriate code changes, use 'integral' instead.");
    public static readonly Check DQUADL = Add("DQUADL", CodeSeverity.Info, "'quadl' is not recommended. With appropriate code changes, use 'integral' instead.");
    public static readonly Check DQUADV = Add("DQUADV", CodeSeverity.Info, "'quadv' is not recommended. With appropriate code changes, use 'integral' instead.");
    public static readonly Check DDBLQD = Add("DDBLQD", CodeSeverity.Info, "'dblquad' is not recommended. With appropriate code changes, use 'integral2' instead.");
    public static readonly Check DTRIQD = Add("DTRIQD", CodeSeverity.Info, "'triplequad' is not recommended. With appropriate code changes, use 'integral3' instead.");
    public static readonly Check COMPASS = Add("COMPASS", CodeSeverity.Info, "'compass' is not recommended. With appropriate code changes, use 'compassplot' instead.");
    public static readonly Check DINLN = Add("DINLN", CodeSeverity.Warning, "INLINE will be removed in a future release. Use anonymous functions instead.");

    /// <summary>The functions a bare mention, a call or a handle of which raises a check, by name.</summary>
    internal static readonly IReadOnlyDictionary<string, Check> ByFunctionName = new Dictionary<string, Check>(StringComparer.Ordinal)
    {
        ["datestr"] = DATST,
        ["datenum"] = DATNM,
        ["date"] = DATE,
        ["now"] = TNOW1,
        ["hist"] = HIST,
        ["quad"] = DQUAD,
        ["quadl"] = DQUADL,
        ["quadv"] = DQUADV,
        ["dblquad"] = DDBLQD,
        ["triplequad"] = DTRIQD,
        ["compass"] = COMPASS,
        ["inline"] = DINLN,
        ["str2num"] = ST2NM,
    };

    /// <summary>The check with <paramref name="id"/>, or null when JGraph has none of that id.</summary>
    public static Check? ById(string id) => ByIdMap.TryGetValue(id, out Check? check) ? check : null;

    /// <summary>Every check JGraph raises, by id.</summary>
    public static IReadOnlyDictionary<string, Check> All => ByIdMap;

    /// <summary>
    /// Check ids R2025b has that JGraph does not raise (the rest of W0's corpus of 106 and the ids the probes met),
    /// so a <c>%#ok</c> naming one is left alone rather than called a check that is not found.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownElsewhere = new HashSet<string>(StringComparer.Ordinal)
    {
        "ATTOF", "ATUNK", "BDLGI", "BDSCI", "CABE", "CCAT1", "COMNL", "DFCNCHK", "DFIRGAUSS", "DFIRRCOS", "DGAUSSFIR", "DILEVAL",
        "FINSI", "FXUP", "GRIDD", "ISCLSTR", "LOGSUM", "MCABE", "MCSGP", "MCSNOV", "MCSOH", "MDEPIN", "MDUPC", "MGMD",
        "NUMCH", "REDEF", "RMFLD", "ROWLN", "SCABE", "SEPEX", "SETNU", "SIMPT", "SPRIX", "SPTMT", "STFLD", "STISA",
        "TCBAF", "TCBAM", "TCBAN", "TCTMT", "TLEV", "UDTMT", "USENS", "VRJOYSTK", "VSBBR", "VSBBW", "VSCRD", "VSECC",
        "VSEMD", "VSFCS", "VSFGL", "VSFGT", "VSFOP", "VSFPR", "VSFRD", "VSFSF", "VSFWR", "VSQRY", "VSRSN", "VSTMT",
    };
}
