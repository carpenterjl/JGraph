namespace JGraph.Scripting.Jgs;

/// <summary>
/// The files <c>mlock</c> holds in memory (open item 169): a locked file is spared by <c>clear name</c>,
/// <c>clear functions</c> and <c>clear all</c>, so its persistents keep their values, until
/// <c>munlock</c> lets it go — R2025b, measured: a locked counter counts on through all three clears and
/// starts again after the unlock and a clear.
/// </summary>
internal sealed partial class Interpreter
{
    private readonly HashSet<string> _lockedFiles = new(JgsFunctionPath.PathComparer);

    /// <summary>
    /// The file whose code is running (<see cref="CurrentFile"/>): a function's, a local function's being
    /// its file's, an anonymous function's the file that wrote it (R2025b, measured: <c>@() mlock</c>
    /// written in a script and called from a helper locks the script), a script's own; null for code with
    /// no file behind it, the prompt's, where <c>mlock</c> and <c>munlock</c> do nothing and
    /// <c>mislocked</c> is false.
    /// </summary>
    internal string? RunningCodeFile =>
        _currentFile.Length > 0 ? _currentFile
        : _activeFunctions.Count > 0 && FileOfFrame(_activeFunctions[^1].SourceId) is { Length: > 0 } file ? file
        : null;

    /// <summary>Holds <paramref name="file"/> in memory, or lets it go.</summary>
    internal void SetFileLocked(string file, bool locked)
    {
        if (locked)
        {
            _lockedFiles.Add(file);
        }
        else
        {
            _lockedFiles.Remove(file);
        }
    }

    /// <summary>Whether <c>mlock</c> holds <paramref name="file"/>.</summary>
    internal bool IsFileLocked(string file) => _lockedFiles.Count > 0 && _lockedFiles.Contains(file);

    /// <summary>
    /// <see cref="ForgetPersistents"/> sparing the functions of a locked file as well as the running
    /// ones: what <c>clear all</c> and <c>clear functions</c> do.
    /// </summary>
    internal void ForgetUnlockedPersistents()
    {
        if (_lockedFiles.Count == 0)
        {
            ForgetPersistents();
            return;
        }

        foreach (FnStmt owner in _persistents.Keys.ToList())
        {
            if (!IsFileActive(owner.SourceId) && !IsFileLocked(FileOfFrame(owner.SourceId)))
            {
                _persistents.Remove(owner);
            }
        }
    }
}
