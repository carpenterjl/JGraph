using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// A C compiler <c>loadlibrary</c> can preprocess a header with (ADR 0181): MSVC found through
/// vswhere, MinGW-w64 named by MATLAB's <c>MW_MINGW64_LOC</c>, or a <c>cl.exe</c> or <c>gcc.exe</c> on
/// <c>PATH</c>. The descriptive fields are the ones <c>mex.getCompilerConfigurations</c> answers.
/// </summary>
/// <param name="Id">How the Options setting names it: <c>msvc:&lt;folder&gt;</c>, <c>mingw:&lt;folder&gt;</c>, <c>cl:&lt;file&gt;</c> or <c>gcc:&lt;file&gt;</c>.</param>
/// <param name="IsMsvc">Whether it takes MSVC's options (<c>cl</c>) rather than GCC's.</param>
/// <param name="Name">R2025b's name for it: <c>Microsoft Visual C++ 2022 (C)</c>.</param>
/// <param name="ShortName">R2025b's short name: <c>MSVC170</c>.</param>
/// <param name="Manufacturer"><c>Microsoft</c> or <c>GNU</c>.</param>
/// <param name="Version">The product's version as R2025b writes it: <c>17.0</c>.</param>
/// <param name="Location">The installation folder, with a trailing separator.</param>
/// <param name="Executable">The compiler, or for MSVC the <c>vcvars64.bat</c> that sets it up.</param>
internal sealed record CCompiler(
    string Id, bool IsMsvc, string Name, string ShortName, string Manufacturer, string Version, string Location, string Executable)
{
    /// <summary>The name the Options list shows.</summary>
    public string Display => IsMsvc && Executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
        ? $"{Name} — {Location.TrimEnd('\\')}"
        : $"{Name} — {Executable}";
}

/// <summary>
/// Finds C compilers and runs their preprocessors for <c>loadlibrary(lib, header)</c> (ADR 0181).
/// </summary>
/// <remarks>
/// The order, first found wins, is the plan's: the compiler the Options setting names; MATLAB's own
/// <c>MW_MINGW64_LOC</c>; MSVC through vswhere, newest first, any Visual Studio from 2019 on (R2025b's
/// own list stops before Visual Studio 2026; JGraph's does not); <c>cl.exe</c> on <c>PATH</c>;
/// <c>gcc.exe</c> on <c>PATH</c>. An MSVC environment is <c>vcvars64.bat</c>'s, captured once per
/// process. Discovery runs once per process too; <see cref="Refresh"/> starts it again.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class CCompilers
{
    private static readonly object Gate = new();
    private static IReadOnlyList<CCompiler>? _found;
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Environments = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<IReadOnlyList<CCompiler>?> Substitute = new();

    /// <summary>The Options setting: null or empty for automatic, else a compiler's <see cref="CCompiler.Id"/>.</summary>
    public static string? Preferred { get; set; }

    /// <summary>
    /// For tests: makes discovery answer <paramref name="compilers"/> on this flow of control, so a
    /// test can point <c>loadlibrary</c> at no compiler without touching the machine. Null restores it.
    /// </summary>
    internal static void SubstituteForTests(IReadOnlyList<CCompiler>? compilers) => Substitute.Value = compilers;

    /// <summary>Every compiler found, in the automatic order.</summary>
    public static IReadOnlyList<CCompiler> Discover()
    {
        if (Substitute.Value is { } substitute)
        {
            return substitute;
        }

        lock (Gate)
        {
            return _found ??= Search();
        }
    }

    /// <summary>Forgets what discovery found, so the next <see cref="Discover"/> looks again.</summary>
    public static void Refresh()
    {
        lock (Gate)
        {
            _found = null;
        }
    }

    /// <summary>The compiler <c>loadlibrary</c> uses: the preferred one when it is found, else the first found, else null.</summary>
    public static CCompiler? Selected()
    {
        IReadOnlyList<CCompiler> found = Discover();
        string? preferred = Preferred;
        if (!string.IsNullOrEmpty(preferred) && found.FirstOrDefault(c => string.Equals(c.Id, preferred, StringComparison.OrdinalIgnoreCase)) is { } chosen)
        {
            return chosen;
        }

        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>R2025b's refusal when no compiler is found, its hyperlinks reduced to their text.</summary>
    public const string NoCompilerIdentifier = "MATLAB:mex:NoCompilerFound_link_Win64";

    /// <inheritdoc cref="NoCompilerIdentifier"/>
    public const string NoCompilerMessage =
        "Supported compiler not detected. You can install the freely available MinGW-w64 C/C++ compiler; see Install MinGW-w64 Compiler. "
        + "For more options, visit https://www.mathworks.com/support/compilers.";

    private static List<CCompiler> Search()
    {
        var found = new List<CCompiler>();
        if (Environment.GetEnvironmentVariable("MW_MINGW64_LOC") is { Length: > 0 } mingw
            && File.Exists(Path.Combine(mingw, "bin", "gcc.exe")))
        {
            found.Add(Gcc("mingw:" + mingw, Path.Combine(mingw, "bin", "gcc.exe"), mingw));
        }

        found.AddRange(VisualStudios());
        if (OnPath("cl.exe") is { } cl && !found.Any(c => c.IsMsvc && cl.StartsWith(c.Location, StringComparison.OrdinalIgnoreCase)))
        {
            found.Add(new CCompiler("cl:" + cl, true, "Microsoft Visual C++ (C)", "MSVC", "Microsoft", "", Path.GetDirectoryName(cl)! + "\\", cl));
        }

        if (OnPath("gcc.exe") is { } gcc && !found.Any(c => string.Equals(c.Executable, gcc, StringComparison.OrdinalIgnoreCase)))
        {
            found.Add(Gcc("gcc:" + gcc, gcc, Path.GetDirectoryName(Path.GetDirectoryName(gcc)!)!));
        }

        return found;
    }

    private static CCompiler Gcc(string id, string gcc, string location)
    {
        string version = "";
        try
        {
            version = Run(gcc, "-dumpversion", null, null, TimeSpan.FromSeconds(10)).Output.Trim();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
        }

        return new CCompiler(id, false, "MinGW64 Compiler (C)", "mingw64", "GNU", version, location.TrimEnd('\\') + "\\", gcc);
    }

    private static IEnumerable<CCompiler> VisualStudios()
    {
        string vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            yield break;
        }

        string json;
        try
        {
            json = Run(vswhere, "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json -utf8", null, null, TimeSpan.FromSeconds(30)).Output;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            yield break;
        }

        var instances = new List<(Version Version, string Path, string Year)>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            foreach (JsonElement instance in document.RootElement.EnumerateArray())
            {
                if (instance.TryGetProperty("installationPath", out JsonElement path)
                    && instance.TryGetProperty("installationVersion", out JsonElement version)
                    && Version.TryParse(version.GetString(), out Version? parsed)
                    && parsed.Major >= 16)
                {
                    string year = instance.TryGetProperty("catalog", out JsonElement catalog)
                        && catalog.TryGetProperty("productLineVersion", out JsonElement line) ? line.GetString() ?? "" : "";
                    instances.Add((parsed, path.GetString()!, year.Length > 0 ? year : parsed.Major switch { 16 => "2019", 17 => "2022", 18 => "2026", _ => "" }));
                }
            }
        }
        catch (JsonException)
        {
            yield break;
        }

        foreach ((Version version, string path, string year) in instances.OrderByDescending(i => i.Version))
        {
            string vcvars = Path.Combine(path, "VC", "Auxiliary", "Build", "vcvars64.bat");
            if (File.Exists(vcvars))
            {
                yield return new CCompiler(
                    "msvc:" + path, true, $"Microsoft Visual C++ {year} (C)", $"MSVC{version.Major}0", "Microsoft",
                    $"{version.Major}.0", path.TrimEnd('\\') + "\\", vcvars);
            }
        }
    }

    private static string? OnPath(string file)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim('"'), file);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    /// <summary>What <see cref="Preprocess"/> produced: the text, or the compiler's complaint.</summary>
    public sealed record Preprocessed(bool Succeeded, string Text, string Diagnostics);

    /// <summary>
    /// Runs <paramref name="compiler"/>'s preprocessor over <paramref name="header"/> as C, from
    /// <paramref name="folder"/>, with the header's folder, <paramref name="folder"/> and each of
    /// <paramref name="includes"/> searched for includes, keeping the line markers the parser follows.
    /// MSVC is also given R2025b's <c>/Zp8</c>.
    /// </summary>
    public static Preprocessed Preprocess(CCompiler compiler, string header, IEnumerable<string> includes, string folder, CancellationToken cancel = default)
    {
        var arguments = new StringBuilder();
        var searched = new List<string> { Path.GetDirectoryName(header)!, folder };
        searched.AddRange(includes);
        IReadOnlyDictionary<string, string>? environment = null;
        string executable;
        if (compiler.IsMsvc)
        {
            arguments.Append("/nologo /E /TC /Zp8");
            foreach (string include in searched.Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                arguments.Append(" /I").Append(Quote(include));
            }

            if (compiler.Executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                environment = MsvcEnvironment(compiler);
                executable = FindIn(environment, "cl.exe") ?? "cl.exe";
            }
            else
            {
                executable = compiler.Executable;
            }
        }
        else
        {
            arguments.Append("-E -x c");
            foreach (string include in searched.Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                arguments.Append(" -I").Append(Quote(include));
            }

            executable = compiler.Executable;
            string bin = Path.GetDirectoryName(executable)!;
            environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = bin + ";" + Environment.GetEnvironmentVariable("PATH"),
            };
        }

        arguments.Append(' ').Append(Quote(header));
        (int exit, string output, string errors) = Run(executable, arguments.ToString(), folder, environment, TimeSpan.FromMinutes(5), cancel);
        return exit == 0 ? new Preprocessed(true, output, errors) : new Preprocessed(false, "", errors.Trim());
    }

    /// <summary><c>vcvars64.bat</c>'s environment, captured once per process and compiler.</summary>
    private static IReadOnlyDictionary<string, string> MsvcEnvironment(CCompiler compiler) =>
        Environments.GetOrAdd(compiler.Id, _ =>
        {
            string command = $"/d /s /c \"\"{compiler.Executable}\" >nul 2>&1 && set\"";
            string output = Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), command, null, null, TimeSpan.FromMinutes(2)).Output;
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in output.Split('\n'))
            {
                int equals = line.Length > 1 ? line.IndexOf('=', 1) : -1;
                if (equals > 0)
                {
                    variables[line[..equals]] = line[(equals + 1)..].TrimEnd('\r');
                }
            }

            return variables;
        });

    private static string? FindIn(IReadOnlyDictionary<string, string> environment, string file)
    {
        if (!environment.TryGetValue("PATH", out string? path))
        {
            return null;
        }

        foreach (string folder in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Quote(string text) => "\"" + text.TrimEnd('\\') + "\"";

    private static (int Exit, string Output, string Errors) Run(
        string executable, string arguments, string? folder, IReadOnlyDictionary<string, string>? environment, TimeSpan limit, CancellationToken cancel = default)
    {
        var start = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (folder is not null)
        {
            start.WorkingDirectory = folder;
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var clock = Stopwatch.StartNew();
        while (!process.WaitForExit(100))
        {
            if (cancel.IsCancellationRequested || clock.Elapsed > limit)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                cancel.ThrowIfCancellationRequested();
                throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(executable)} ran past {limit.TotalSeconds} s."));
            }
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
    }
}
