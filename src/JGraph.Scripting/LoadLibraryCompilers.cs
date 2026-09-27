using JGraph.Scripting.Jgs.Native;

namespace JGraph.Scripting;

/// <summary>A C compiler <c>loadlibrary</c> can use: the id the settings store and the name Options shows.</summary>
/// <param name="Id">The discovery id, stored in the settings.</param>
/// <param name="Display">The compiler's name and where it is.</param>
public sealed record LoadLibraryCompiler(string Id, string Display);

/// <summary>
/// The hosts' view of the compilers <c>loadlibrary(lib, header)</c> preprocesses with (ADR 0181): what
/// discovery finds, for the Options list, and which one the user chose. The choice is process-wide,
/// as MATLAB's <c>mex -setup</c> choice is per user; a choice naming a compiler no longer found falls
/// back to the automatic order.
/// </summary>
public static class LoadLibraryCompilers
{
    /// <summary>The compilers found on this machine, in the automatic order (empty off Windows).</summary>
    public static IReadOnlyList<LoadLibraryCompiler> Available() =>
        OperatingSystem.IsWindows()
            ? [.. CCompilers.Discover().Select(c => new LoadLibraryCompiler(c.Id, c.Display))]
            : [];

    /// <summary>The chosen compiler's id, or null for automatic.</summary>
    public static string? Preferred
    {
        get => OperatingSystem.IsWindows() ? CCompilers.Preferred : null;
        set
        {
            if (OperatingSystem.IsWindows())
            {
                CCompilers.Preferred = value;
            }
        }
    }
}
