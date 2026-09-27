using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// The process's environment as Windows keeps it: one block of <c>NAME=VALUE</c> strings, each ended
/// by a NUL, the whole ended by another. The native host is sent what changed since the block it last
/// saw. Reading the block costs microseconds, so it is read only when something that can change it
/// has run since the last call: <c>setenv</c>, or a .NET member (which may call
/// <c>SetEnvironmentVariable</c>); an unchanged call costs one compare of a counter.
/// </summary>
internal static unsafe partial class EnvironmentBlock
{
    private static int _version;

    /// <summary>Counts the moments the environment may have changed; see <see cref="NoteChange"/>.</summary>
    public static int Version => Volatile.Read(ref _version);

    /// <summary>Says the environment may have changed: <c>setenv</c> ran, or a .NET member is about to.</summary>
    public static void NoteChange() => Interlocked.Increment(ref _version);

    /// <summary>A copy of the block as it is now.</summary>
    [SupportedOSPlatform("windows")]
    public static char[] Current()
    {
        char* block = GetEnvironmentStringsW();
        try
        {
            return new ReadOnlySpan<char>(block, Length(block)).ToArray();
        }
        finally
        {
            FreeEnvironmentStringsW(block);
        }
    }

    /// <summary>
    /// Compares the block now with <paramref name="last"/>. When they differ,
    /// <paramref name="current"/> is the block now and <paramref name="changes"/> lists each variable
    /// set (with its value) or removed (with null); when they match, both stay null.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void Compare(char[] last, ref char[]? current, ref List<(string Name, string? Value)>? changes)
    {
        char* block = GetEnvironmentStringsW();
        try
        {
            var now = new ReadOnlySpan<char>(block, Length(block));
            if (now.SequenceEqual(last))
            {
                return;
            }

            current = now.ToArray();
        }
        finally
        {
            FreeEnvironmentStringsW(block);
        }

        Dictionary<string, string> before = Parse(last), after = Parse(current);
        changes = [];
        foreach ((string name, string value) in after)
        {
            if (!before.TryGetValue(name, out string? old) || !string.Equals(old, value, StringComparison.Ordinal))
            {
                changes.Add((name, value));
            }
        }

        foreach (string name in before.Keys)
        {
            if (!after.ContainsKey(name))
            {
                changes.Add((name, null));
            }
        }
    }

    /// <summary>
    /// The variables in a block. The <c>=C:=C:\…</c> entries, which hold each drive's folder for the
    /// command shell, are not variables and are left out; the host's folder is sent on its own.
    /// </summary>
    private static Dictionary<string, string> Parse(ReadOnlySpan<char> block)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (block.Length > 0)
        {
            int end = block.IndexOf('\0');
            ReadOnlySpan<char> entry = end < 0 ? block : block[..end];
            block = end < 0 ? [] : block[(end + 1)..];
            int equals = entry.IndexOf('=');
            if (equals > 0)
            {
                variables[entry[..equals].ToString()] = entry[(equals + 1)..].ToString();
            }
        }

        return variables;
    }

    /// <summary>The block's length in characters, up to and including the last entry's NUL.</summary>
    private static int Length(char* block)
    {
        int at = 0;
        while (block[at] != '\0' || block[at + 1] != '\0')
        {
            at++;
        }

        return at + 1;
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll")]
    private static partial char* GetEnvironmentStringsW();

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeEnvironmentStringsW(char* block);
}
