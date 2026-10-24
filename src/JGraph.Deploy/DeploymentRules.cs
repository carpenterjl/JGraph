namespace JGraph.Deploy;

/// <summary>
/// What R2025b's dependency analysis (<c>matlab.depfun</c>, its <c>mcr.rdl</c> rules and
/// <c>el.win64.txt</c>) knows that a program's own text cannot say, as stage K2's probes recorded it
/// (<c>tools/matlab-checklist/compiler-probes</c>): which product owns a name, which files the runtime
/// leaves out, and which functions read a data file named by a literal.
/// </summary>
/// <remarks>
/// The tables come from <c>recordings/k2_names.out.txt</c>, embedded as it was recorded: one line per
/// name JGraph answers, with the products <c>matlab.codetools.requiredFilesAndProducts</c> gave for
/// it and its file under <c>matlabroot</c>; one line per product met; one line per public file mcc
/// warns it left out when code names it.
/// </remarks>
public static class DeploymentRules
{
    /// <summary>MATLAB's own product number; every answer of <c>requiredFilesAndProducts</c> holds it.</summary>
    public const int MatlabProduct = 1;

    /// <summary>
    /// The functions whose literal file-name arguments are data files a build takes along, from
    /// <c>matlab.depfun</c>'s rules: a literal, or a variable a literal was last assigned to.
    /// </summary>
    public static readonly IReadOnlySet<string> DataReaders = new HashSet<string>(StringComparer.Ordinal)
    {
        "audioinfo", "audioread", "csvread", "daqread", "dlmread", "fileread", "fopen", "imfinfo",
        "importdata", "imread", "load", "matfile", "mmfileinfo", "open", "readcell", "readmatrix",
        "readtable", "readtimetable", "readvars", "type", "VideoReader", "xlsfinfo", "xlsread",
        "xmlread", "xslt",
    };

    /// <summary>
    /// The extensions a data-file literal may not carry: a code file named in a literal is not taken
    /// as data (<c>fileread('fb.m')</c> takes nothing, probe_k2_rules).
    /// </summary>
    public static readonly IReadOnlySet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".m", ".mlx", ".mlapp", ".p", ".mexw64",
    };

    /// <summary>
    /// The reason R2025b gives for a non-deployable file, in the excluded-files log and the
    /// <c>Compiler:compiler:COM_WARN_EXCLUDED_FILE</c> warning.
    /// </summary>
    public const string NonDeployableReason =
        "The file or function is non-deployable and has been excluded from packaging. Either remove the file "
        + "or function from your code, or use the MATLAB function \"isdeployed\" to ensure the function is "
        + "not invoked in the deployed component. Then you can suppress this warning by using the %#exclude pragma.";

    /// <summary>The message identifier the excluded-files log gives a non-deployable file.</summary>
    public const string ExcludedById = "MATLAB:depfun:req:ExcludedBy";

    private static readonly Lazy<Tables> Loaded = new(Load);

    /// <summary>A product as <c>requiredFilesAndProducts</c> describes it.</summary>
    public static RuntimeProduct? Product(int number) =>
        Loaded.Value.Products.TryGetValue(number, out RuntimeProduct? product) ? product : null;

    /// <summary>
    /// The products R2025b says <paramref name="name"/> needs, MATLAB first; just MATLAB for a name
    /// of MATLAB's own, for one R2025b does not have and for one the probe could not ask about.
    /// </summary>
    public static IReadOnlyList<int> ProductsOf(string name) =>
        Loaded.Value.NameProducts.TryGetValue(name, out int[]? numbers) ? numbers : [MatlabProduct];

    /// <summary>
    /// The toolbox folder R2025b keeps <paramref name="name"/> under (<c>signal</c> for <c>butter</c>),
    /// what <c>mcc -N -p</c> names; null for a name with no file under <c>toolbox</c>.
    /// </summary>
    public static string? ToolboxOf(string name) =>
        Loaded.Value.NameToolbox.TryGetValue(name, out string? toolbox) ? toolbox : null;

    /// <summary>
    /// The file under <c>matlabroot</c> R2025b leaves out of the runtime that <paramref name="name"/>
    /// resolves to (<c>toolbox/matlab/codetools/edit.m</c>), or null when the name is deployable.
    /// </summary>
    public static string? NonDeployableFile(string name) =>
        Loaded.Value.Excluded.TryGetValue(name, out string? file) ? file : null;

    private sealed record Tables(
        Dictionary<int, RuntimeProduct> Products,
        Dictionary<string, int[]> NameProducts,
        Dictionary<string, string> NameToolbox,
        Dictionary<string, string> Excluded);

    private static Tables Load()
    {
        var products = new Dictionary<int, RuntimeProduct>();
        var nameProducts = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var nameToolbox = new Dictionary<string, string>(StringComparer.Ordinal);
        var excluded = new Dictionary<string, string>(StringComparer.Ordinal);
        using Stream stream = typeof(DeploymentRules).Assembly.GetManifestResourceStream("JGraph.Deploy.r2025b-names.tsv")
            ?? throw new InvalidOperationException("The R2025b names table is not embedded.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            string[] f = line.Split('\t');
            if (f.Length == 4 && f[0] == "product" && int.TryParse(f[1], out int number))
            {
                products[number] = new RuntimeProduct(f[2], f[3], number);
            }
            else if (f.Length == 3 && f[0] == "excluded")
            {
                // mcrcachedir.mexw64 is left out of the archive, but the runtime answers it, as
                // JGraph's deployed session does (stage K1): nothing is missing to warn about.
                if (f[1] != "mcrcachedir")
                {
                    excluded[f[1]] = f[2];
                }
            }
            else if (f.Length == 3)
            {
                if (f[2].StartsWith("toolbox/", StringComparison.Ordinal) && f[2].IndexOf('/', 8) is int slash and > 8)
                {
                    nameToolbox[f[0]] = f[2][8..slash];
                }

                if (f[1].Contains(',', StringComparison.Ordinal))
                {
                    nameProducts[f[0]] = f[1].Split(',').Select(int.Parse).ToArray();
                }
            }
        }

        return new Tables(products, nameProducts, nameToolbox, excluded);
    }
}
