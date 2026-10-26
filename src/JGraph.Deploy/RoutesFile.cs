using System.Text.Json;
using System.Text.RegularExpressions;

namespace JGraph.Deploy;

/// <summary>A routes file that is not as it should be, with R2025b's words (compiler and SDK plan, stage K14).</summary>
public sealed class RoutesException(string message) : Exception(message);

/// <summary>
/// A Production Server routes file, the <c>RoutesFile</c> of <c>compiler.build.productionServerArchive</c>
/// and the <c>ROUTES:</c> part of <c>mcc -W CTF</c> (stage K14): <c>{"version":"1.0.0","pathmap":[{"match":"&lt;regex&gt;","webhandler":{"component":"&lt;archive&gt;","function":"&lt;name&gt;"}}]}</c>,
/// checked as R2025b's mcc checks it when the archive is built (k14_build: mcc_routes_*; probe_k14_routes):
/// <c>pathmap</c> an array of entries with <c>match</c> and <c>webhandler</c> alone, <c>match</c> a
/// regular expression, <c>webhandler</c> an object with <c>function</c> and, when present, a
/// <c>component</c> naming the archive; <c>version</c> is not checked and a function need not exist.
/// A server routes a request whose path the pattern matches to the function (ADR 0270).
/// </summary>
public sealed class RoutesFile
{
    private RoutesFile(string text, IReadOnlyList<Route> routes)
    {
        Text = text;
        Routes = routes;
    }

    /// <summary>One entry of the path map.</summary>
    /// <param name="Match">The regular expression a request's path is matched against.</param>
    /// <param name="Function">The function that answers it.</param>
    /// <param name="Component">The archive named, or null when the entry names none.</param>
    public sealed record Route(string Match, string Function, string? Component);

    /// <summary>Where the archive keeps the file, as R2025b's does (probe_k14_routes: r_ok).</summary>
    public const string ArchivePath = ".META/.prodserver/routes/routes.json";

    /// <summary>The file's text as given, which the archive carries unchanged.</summary>
    public string Text { get; }

    /// <summary>The entries in order.</summary>
    public IReadOnlyList<Route> Routes { get; }

    /// <summary>Reads and checks <paramref name="path"/> for the archive <paramref name="componentName"/>.</summary>
    /// <exception cref="RoutesException">R2025b's message for what is wrong.</exception>
    public static RoutesFile Read(string path, string componentName) => Parse(File.ReadAllText(path), componentName);

    /// <summary>Reads routes text, as <see cref="Read"/> reads a file.</summary>
    public static RoutesFile Parse(string text, string componentName)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException error)
        {
            // R2025b: "JSON syntax error: status=Unexpected end of input (line: 2, column: 0)".
            bool ended = error.Message.Contains("end of", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("depth", StringComparison.OrdinalIgnoreCase);
            string status = ended ? "Unexpected end of input" : error.Message.Split(" LineNumber:")[0].Split(" Path:")[0].TrimEnd('.');
            throw new RoutesException($"JSON syntax error: status={status} (line: {(error.LineNumber ?? 0) + 1}, column: {error.BytePositionInLine ?? 0})");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new RoutesException("The top-level JSON value must be an Object.");
            }

            if (!root.TryGetProperty("pathmap", out JsonElement pathmap))
            {
                throw new RoutesException("Required property \"pathmap\" not found. (at .pathmap)");
            }

            if (pathmap.ValueKind != JsonValueKind.Array)
            {
                throw new RoutesException("Top-level property \"pathmap\" must be an array. (at .pathmap).");
            }

            // R2025b reads the map in two passes (k14_build's mcc_routes_* and probe_k14_routes' r_*): first each
            // entry's webhandler, the last entry first (two entries naming another archive report .pathmap[1]),
            // its shape and then its component, which must be this archive's; then each entry in order, its
            // keys and its match. So a missing function is reported before a wrong component (mcc_routes_nofunction),
            // and a wrong component before a missing match (mcc_routes_nomatch).
            JsonElement[] entries = [.. pathmap.EnumerateArray()];
            for (int index = entries.Length - 1; index >= 0; index--)
            {
                JsonElement entry = entries[index];
                string at = $".pathmap[{index}]";
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new RoutesException($"pathmap entry is not an Object. (at {at})");
                }

                if (!entry.TryGetProperty("webhandler", out JsonElement handler))
                {
                    throw new RoutesException($"Required property \"webhandler\" not found. (at {at}.webhandler)");
                }

                if (handler.ValueKind != JsonValueKind.Object)
                {
                    throw new RoutesException($"webhandler entry is not an Object. (at {at})");
                }

                string[] unknownHandler = [.. handler.EnumerateObject().Select(static p => p.Name).Where(static n => n is not ("component" or "function"))];
                if (unknownHandler.Length > 0)
                {
                    throw new RoutesException(
                        $"Unknown key(s) detected: [ {string.Join(", ", unknownHandler.Select(static n => "\"" + n + "\""))} ]. Must be one of: [ \"component\", \"function\" ]. (at {at})");
                }

                if (!handler.TryGetProperty("function", out JsonElement function))
                {
                    throw new RoutesException($"Required property \"function\" not found. (at {at}.function)");
                }

                if (function.ValueKind != JsonValueKind.String)
                {
                    throw new RoutesException($"Required property \"function\" must be a String. (at {at}.function)");
                }

                if (handler.TryGetProperty("component", out JsonElement named))
                {
                    string component = named.ValueKind == JsonValueKind.String ? named.GetString()! : named.GetRawText();
                    if (component != componentName)
                    {
                        throw new RoutesException($"Optional property \"component\" if present must be the same value as \"{componentName}\" . (at {at})");
                    }
                }
            }

            var routes = new List<Route>(entries.Length);
            for (int index = 0; index < entries.Length; index++)
            {
                JsonElement entry = entries[index];
                string at = $".pathmap[{index}]";
                string[] unknown = [.. entry.EnumerateObject().Select(static p => p.Name).Where(static n => n is not ("match" or "webhandler"))];
                if (unknown.Length > 0)
                {
                    throw new RoutesException(
                        $"Unknown key(s) detected: [ {string.Join(", ", unknown.Select(static n => "\"" + n + "\""))} ]. Must be one of: [ \"match\", \"webhandler\" ]. (at {at})");
                }

                if (!entry.TryGetProperty("match", out JsonElement match))
                {
                    throw new RoutesException($"Required property \"match\" not found. (at {at}.match)");
                }

                if (match.ValueKind != JsonValueKind.String)
                {
                    throw new RoutesException($"Required property \"match\" must be a String. (at {at}.match)");
                }

                string pattern = match.GetString()!;
                try
                {
                    _ = new Regex(pattern, RegexOptions.CultureInvariant);
                }
                catch (ArgumentException error)
                {
                    // R2025b names std::regex's fault; JGraph names .NET's.
                    throw new RoutesException($"\"match\" entry has invalid regular expression. (at {at}.match). reason: {error.Message}");
                }

                JsonElement handler = entry.GetProperty("webhandler");
                string? component = handler.TryGetProperty("component", out JsonElement componentElement)
                    ? componentElement.ValueKind == JsonValueKind.String ? componentElement.GetString() : componentElement.GetRawText()
                    : null;
                routes.Add(new Route(pattern, handler.GetProperty("function").GetString()!, component));
            }

            return new RoutesFile(text, routes);
        }
    }
}
