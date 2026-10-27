using System.Security.Cryptography;
using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The JGraph Runtime's installer, <c>JGraph-Runtime-&lt;version&gt;.msi</c> (compiler and SDK plan,
/// stage K18): the counterpart of the MATLAB Runtime installer <c>mcrinstaller</c> finds and
/// <c>compiler.runtime.download</c> fetches. Each version of the runtime is a product of its own,
/// known by an upgrade code derived from the version, which is how a file is told to be the runtime's
/// installer and not another MSI.
/// </summary>
public static class RuntimeInstaller
{
    /// <summary>The environment variable naming the runtime installer to use, which wins over every other place.</summary>
    public const string LocationVariable = "JGRAPH_RUNTIME_INSTALLER";

    /// <summary>The environment variable naming where the installer is downloaded from, in place of the release (for a test's own server).</summary>
    public const string ReleaseUrlVariable = "JGRAPH_RUNTIME_RELEASE_URL";

    /// <summary>The namespace the upgrade codes are derived in (a version 5 UUID of the version's name).</summary>
    public static readonly Guid Namespace = Guid.Parse("7C0E6B2A-4F1D-4E3B-9A7E-2D5F8C1B6A90");

    /// <summary>The installer's file name for a runtime of <paramref name="version"/>.</summary>
    public static string FileName(string version) => $"JGraph-Runtime-{version}.msi";

    /// <summary>
    /// The upgrade code of the runtime MSI of <paramref name="version"/>: the name-based (SHA-1)
    /// UUID of <c>JGraph Runtime &lt;version&gt;</c> in <see cref="Namespace"/>, upper case without
    /// braces, as <c>installer/build-runtime-installer.ps1</c> derives it.
    /// </summary>
    public static string UpgradeCode(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        return NameBasedGuid(Namespace, "JGraph Runtime " + version).ToString("D").ToUpperInvariant();
    }

    /// <summary>A version 5 UUID (RFC 4122) of <paramref name="name"/> in <paramref name="namespace"/>.</summary>
    public static Guid NameBasedGuid(Guid @namespace, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        byte[] space = @namespace.ToByteArray();
        SwapOrder(space);
        byte[] hash = SHA1.HashData([.. space, .. Encoding.UTF8.GetBytes(name)]);
        byte[] bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        SwapOrder(bytes);
        return new Guid(bytes);
    }

    private static void SwapOrder(byte[] guid)
    {
        Array.Reverse(guid, 0, 4);
        Array.Reverse(guid, 4, 2);
        Array.Reverse(guid, 6, 2);
    }

    /// <summary>
    /// Where the installer of <paramref name="version"/> is downloaded from (decision KO8): the GitHub
    /// release of that JGraph version, unless <see cref="ReleaseUrlVariable"/> names another base.
    /// </summary>
    public static string ReleaseUrl(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        string root = Environment.GetEnvironmentVariable(ReleaseUrlVariable) is { Length: > 0 } named
            ? named.TrimEnd('/')
            : $"https://github.com/jgraph/jgraph/releases/download/v{version}";
        return root + "/" + FileName(version);
    }

    /// <summary>The folder a downloaded installer is kept in: the user's local application data, under JGraph.</summary>
    public static string DownloadFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JGraph", "runtime", RuntimeLayout.Platform);

    /// <summary>
    /// The runtime installer of <paramref name="version"/> on this machine, or null: the file
    /// <see cref="LocationVariable"/> names, the downloaded one, the one JGraph's own
    /// <c>toolbox\compiler\deploy\win64</c> carries, then a source checkout's build of it; each
    /// counts only when it is a Windows Installer package with the runtime's upgrade code and version.
    /// </summary>
    public static string? Find(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        foreach (string candidate in Candidates(version))
        {
            if (IsValid(candidate, version))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>Every place <see cref="Find"/> looks, in its order, whether or not the file is there.</summary>
    public static IEnumerable<string> Candidates(string version)
    {
        if (Environment.GetEnvironmentVariable(LocationVariable) is { Length: > 0 } named)
        {
            yield return named;
        }

        string file = FileName(version);
        yield return Path.Combine(DownloadFolder, file);
        yield return Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "toolbox", "compiler", "deploy", RuntimeLayout.Platform, file);
        string? folder = AppContext.BaseDirectory;
        for (int up = 0; up < 8 && folder is not null; up++)
        {
            string checkout = Path.Combine(folder, "installer", "JGraph.Runtime.Installer", "bin");
            if (Directory.Exists(checkout))
            {
                yield return Path.Combine(checkout, "Release", file);
                yield return Path.Combine(checkout, "Debug", file);
                yield break;
            }

            folder = Path.GetDirectoryName(folder);
        }
    }

    /// <summary>Whether <paramref name="path"/> is the runtime installer of <paramref name="version"/>: a package whose upgrade code and product version are the runtime's.</summary>
    public static bool IsValid(string path, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using MsiDatabase package = MsiDatabase.Open(path);
            string? code = package.Property("UpgradeCode")?.Trim('{', '}');
            return string.Equals(code, UpgradeCode(version), StringComparison.OrdinalIgnoreCase)
                && package.Property("ProductVersion") == version;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads the installer of <paramref name="version"/> from <see cref="ReleaseUrl"/> into
    /// <see cref="DownloadFolder"/> and answers its path; the download is checked to be the runtime's
    /// installer before it is kept.
    /// </summary>
    /// <exception cref="HttpRequestException">The release could not be reached or has no such file.</exception>
    /// <exception cref="InvalidDataException">What was downloaded is not the runtime installer.</exception>
    public static async Task<string> DownloadAsync(string version, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        string url = ReleaseUrl(version);
        Directory.CreateDirectory(DownloadFolder);
        string target = Path.Combine(DownloadFolder, FileName(version));
        string partial = target + ".download";
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(30);
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (FileStream file = File.Create(partial))
        {
            await response.Content.CopyToAsync(file, token).ConfigureAwait(false);
        }

        if (!IsValid(partial, version))
        {
            File.Delete(partial);
            throw new InvalidDataException($"What {url} answered is not the JGraph Runtime {version} installer.");
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }
}
