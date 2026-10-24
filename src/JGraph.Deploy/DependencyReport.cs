namespace JGraph.Deploy;

/// <summary>A product a set of files needs, as <c>requiredFilesAndProducts</c> lists it.</summary>
/// <param name="Name">The product's name, <c>Signal Processing Toolbox</c>.</param>
/// <param name="Version">The release's version of it, <c>25.2</c>.</param>
/// <param name="Number">MathWorks' product number, 1 for MATLAB.</param>
public sealed record RuntimeProduct(string Name, string Version, int Number);

/// <summary>Why a file is in a build.</summary>
public enum DependencyReason
{
    /// <summary>An entry point, or a file a caller named among the roots.</summary>
    Root,

    /// <summary>Code a file calls, names by a handle, or inherits from.</summary>
    Code,

    /// <summary>A <c>%#function</c> pragma names it.</summary>
    Pragma,

    /// <summary>The <c>.fig</c> beside a code file in the build.</summary>
    Figure,

    /// <summary>A data file a literal names in a call to one of <see cref="DeploymentRules.DataReaders"/>.</summary>
    Data,

    /// <summary>Added by <c>-a</c> (<c>AdditionalFiles</c>).</summary>
    Additional,
}

/// <summary>A file a build takes along, and why.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Reason">Why it is in the build.</param>
/// <param name="NamedBy">The file whose text named it, or null for a root or an added file.</param>
public sealed record DependencyFile(string Path, DependencyReason Reason, string? NamedBy);

/// <summary>
/// A non-deployable function the code names, as the excluded-files log lists it. (A file a
/// <c>%#exclude</c> drops is simply not taken; R2025b's log does not list it.)
/// </summary>
/// <param name="Name">The function's name as the code named it.</param>
/// <param name="R2025bFile">The file R2025b leaves out for it, under <c>matlabroot</c>.</param>
/// <param name="MessageId">The log's message identifier.</param>
/// <param name="Reason">The log's reason text.</param>
/// <param name="NamedBy">The first file that named it.</param>
public sealed record ExcludedDependency(string Name, string R2025bFile, string MessageId, string Reason, string NamedBy);

/// <summary>A name the analysis could not take along, as <c>unresolvedSymbols.txt</c> lists it.</summary>
/// <param name="File">The file that names it.</param>
/// <param name="Symbol">The name.</param>
/// <param name="Reason">R2025b's reason word: <c>mcc_flag</c> when <c>-N</c> took its toolbox off the path.</param>
public sealed record UnresolvedSymbol(string File, string Symbol, string Reason);

/// <summary>
/// What a dependency analysis found: every file a build takes along, the products it needs, what it
/// left out and why, and what it could not resolve. Files are in the order of their full paths, as
/// <c>requiredFilesAndProducts</c> answers them.
/// </summary>
public sealed class DependencyReport
{
    /// <summary>Creates a report.</summary>
    public DependencyReport(
        IReadOnlyList<DependencyFile> files,
        IReadOnlyList<RuntimeProduct> products,
        IReadOnlyList<ExcludedDependency> excluded,
        IReadOnlyList<UnresolvedSymbol> unresolved)
    {
        Files = files;
        Products = products;
        Excluded = excluded;
        Unresolved = unresolved;
    }

    /// <summary>The files, ordered by full path.</summary>
    public IReadOnlyList<DependencyFile> Files { get; }

    /// <summary>The products, MATLAB first and the rest as the analysis met them.</summary>
    public IReadOnlyList<RuntimeProduct> Products { get; }

    /// <summary>The non-deployable functions the code names, in the order it named them.</summary>
    public IReadOnlyList<ExcludedDependency> Excluded { get; }

    /// <summary>The names <c>-N</c> left without a file.</summary>
    public IReadOnlyList<UnresolvedSymbol> Unresolved { get; }
}

/// <summary>
/// How a dependency analysis is asked: where names are looked for, and the <c>mcc</c> flags and
/// options properties that change what it takes (<c>-a</c>, <c>-X</c>, <c>-I</c>, <c>-N</c>, <c>-p</c>).
/// </summary>
public sealed class DependencyOptions
{
    /// <summary>The folder names are looked for in first, as <c>which</c> looks in the current folder.</summary>
    public string CurrentFolder { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>The search path after the current folder, in order: the session's path.</summary>
    public IReadOnlyList<string> SearchPath { get; init; } = [];

    /// <summary>
    /// Whether the roots' own folders are searched after the current folder. <c>requiredFilesAndProducts</c>
    /// does not search them: a file off the path, analysed from another folder, finds its
    /// <c>private/</c> files and nothing beside it, not even its <c>.fig</c> (measured).
    /// </summary>
    public bool SearchRootFolders { get; init; }

    /// <summary><c>-I</c>: folders searched after the current folder and before <see cref="SearchPath"/>.</summary>
    public IReadOnlyList<string> IncludeFolders { get; init; } = [];

    /// <summary>
    /// <c>-a</c> (<c>AdditionalFiles</c>): files, folders (taken whole, with every folder under them)
    /// and wildcards (<c>pack\*.txt</c>, the one folder's matches). They are taken whatever a
    /// <c>%#exclude</c> says, and their code is analysed as a root's is.
    /// </summary>
    public IReadOnlyList<string> AdditionalFiles { get; init; } = [];

    /// <summary>False for <c>-X</c> (<c>AutoDetectDataFiles='off'</c>): no data file is taken for a literal.</summary>
    public bool DetectDataFiles { get; init; } = true;

    /// <summary>
    /// <c>requiredFilesAndProducts(..., 'toponly')</c>: only what the roots themselves name, not
    /// what those files name in turn.
    /// </summary>
    public bool TopOnly { get; init; }

    /// <summary>
    /// <c>-N</c>: the toolbox folders come off the path, so a function of a product other than MATLAB
    /// is unresolved (reason <c>mcc_flag</c>) unless <see cref="ToolboxFolders"/> puts its folder back.
    /// </summary>
    public bool ClearToolboxPath { get; init; }

    /// <summary><c>-p</c>: the toolbox folders (<c>signal</c>) put back after <see cref="ClearToolboxPath"/>.</summary>
    public IReadOnlyList<string> ToolboxFolders { get; init; } = [];
}
