using System.IO;

namespace JGraph.Application.Services;

/// <summary>
/// Replaces a small settings document without ever leaving a half-written one behind. Both of the
/// application state files are rewritten in full on every change, so a truncate-in-place write that
/// is interrupted destroys the previous good copy along with the new one. Writing beside the target,
/// flushing it to the device, and then replacing means a reader sees one whole document or the
/// other — and that the one it sees survives losing power, which a rename over unflushed data does
/// not.
/// </summary>
internal static class AtomicFile
{
    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="contents"/>.</summary>
    internal static void Write(string path, string contents)
    {
        string staged = path + ".new";
        using (var stream = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        // File.Replace carries the original's attributes and access rules over, and deletes the
        // staged file; it needs the destination to exist, which on a first write it does not.
        if (File.Exists(path))
        {
            File.Replace(staged, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(staged, path);
        }
    }
}
