namespace JGraph.Scripting;

/// <summary>
/// The host's Comparison Tool window, behind <c>visdiff</c> without an output (App Designer window plan W14). A host
/// without windows (the CLI, a batch run, tests) passes none, and the builtin refuses in R2025b's words
/// (<c>comparisons:mldesktop:UnableToCompareWithNoDisplay</c>). Invoked on the engine's background thread, so a UI
/// host marshals it.
/// </summary>
public interface IScriptComparisonTool
{
    /// <summary>Opens the comparison of the files <paramref name="leftPath"/> and <paramref name="rightPath"/>, both full paths of files that exist.</summary>
    /// <returns>Null when it opened, else why not.</returns>
    string? Compare(string leftPath, string rightPath);
}
