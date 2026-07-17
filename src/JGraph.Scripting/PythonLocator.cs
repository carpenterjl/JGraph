using System.Diagnostics;
using System.IO;
using System.Linq;

namespace JGraph.Scripting;

/// <summary>
/// Locates a CPython runtime (<c>pythonXY.dll</c> / <c>libpythonX.Y.so</c>) for pythonnet to load,
/// together with the interpreter's home prefix and module search paths so packages installed for that
/// interpreter (numpy, scipy, …) import inside the embedded runtime. It honours the
/// <c>PYTHONNET_PYDLL</c> environment variable first, then probes the <c>python</c> and (on Windows)
/// <c>py -3</c> launchers. Returns null when no CPython runtime can be found, which is how the Python
/// engine degrades gracefully.
/// </summary>
public static class PythonLocator
{
    /// <summary>Finds an installed CPython runtime, or null if none is available.</summary>
    public static PythonRuntimeInfo? Find()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
        {
            // Honour the override, and enrich it with home/paths only when a probed interpreter
            // corroborates the same DLL — a mismatched PythonHome is worse than none.
            PythonRuntimeInfo? probed = Probe();
            return probed is not null && PathsEqual(probed.Dll, fromEnvironment)
                ? probed
                : new PythonRuntimeInfo(fromEnvironment, Home: null, SearchPaths: []);
        }

        return Probe();
    }

    /// <summary>Finds a CPython shared library to load, or null if none is available.</summary>
    public static string? FindPythonDll() => Find()?.Dll;

    private static PythonRuntimeInfo? Probe()
    {
        foreach ((string exe, string? prefixArg) in Launchers())
        {
            if (TryProbe(exe, prefixArg, out PythonRuntimeInfo? info))
            {
                return info;
            }
        }

        return null;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IEnumerable<(string Exe, string? PrefixArg)> Launchers()
    {
        if (OperatingSystem.IsWindows())
        {
            // "python" first: it is what the user's PATH (and an activated venv) selects, so its
            // installed packages are the ones the user expects scripts to import. "py -3" may pick a
            // different install entirely.
            yield return ("python", null);
            yield return ("py", "-3");
        }
        else
        {
            yield return ("python3", null);
            yield return ("python", null);
        }
    }

    private static bool TryProbe(string exe, string? prefixArg, out PythonRuntimeInfo? info)
    {
        info = null;
        try
        {
            var startInfo = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(prefixArg))
            {
                startInfo.ArgumentList.Add(prefixArg);
            }

            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                "import sys,os;print(sys.version_info[0]);print(sys.version_info[1]);" +
                "print(sys.base_prefix);print(sys.prefix);print(os.pathsep.join(sys.path))");

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                TryKill(process);
                return false;
            }

            if (process.ExitCode != 0)
            {
                return false;
            }

            string[] lines = output.Split('\n', StringSplitOptions.TrimEntries)
                .Where(static line => line.Length > 0)
                .ToArray();
            if (lines.Length < 4
                || !int.TryParse(lines[0], out int major)
                || !int.TryParse(lines[1], out int minor))
            {
                return false;
            }

            // The DLL lives under the base prefix (a venv does not copy it); the home is the
            // environment's own prefix so its site-packages resolves.
            string basePrefix = lines[2];
            string home = lines[3];
            string[] searchPaths = lines.Length >= 5
                ? lines[4].Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];

            // Microsoft Store Python lives under WindowsApps and cannot be embedded (pythonnet fails
            // to bind its exports) — skip it so a regular install found by the next launcher wins.
            if (basePrefix.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string candidate in Candidates(basePrefix, major, minor))
            {
                if (File.Exists(candidate))
                {
                    info = new PythonRuntimeInfo(candidate, home, searchPaths);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No such launcher on PATH, or it could not be started — treat as "not found".
            return false;
        }
    }

    private static IEnumerable<string> Candidates(string prefix, int major, int minor)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(prefix, $"python{major}{minor}.dll");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(prefix, "lib", $"libpython{major}.{minor}.dylib");
            yield return Path.Combine(prefix, "lib", $"libpython{major}.{minor}m.dylib");
        }
        else
        {
            yield return Path.Combine(prefix, "lib", $"libpython{major}.{minor}.so");
            yield return Path.Combine(prefix, "lib", $"libpython{major}.{minor}m.so");
            yield return Path.Combine(prefix, "lib", $"libpython{major}.{minor}.so.1.0");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Process already gone.
        }
    }
}
