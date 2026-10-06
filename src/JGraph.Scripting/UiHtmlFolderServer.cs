
namespace JGraph.Scripting;

/// <summary>
/// What a uihtml page may load from its folder, as R2025b's server answers (probe <c>u9b_types</c>,
/// app-building plan section L): the page's folder and the folders under it, nothing above; a file
/// only when its type is one R2025b serves; a folder as its <c>index.html</c>; GET and HEAD only.
/// Pure: the WebView2 handler asks it, and the tests ask it directly.
/// </summary>
public static class UiHtmlFolderServer
{
    /// <summary>
    /// The endings R2025b serves, with the content type it sends (null: it sends none). Anything else
    /// - <c>.txt</c>, <c>.mjs</c>, <c>.webp</c>, <c>.mp3</c>, <c>.bmp</c>, <c>.csv</c>, ... - is a 404.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string?> Served = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".js"] = "text/javascript",
        [".json"] = "application/json",
        [".css"] = "text/css",
        [".xml"] = "text/xml",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".cur"] = "image/x-cursor",
        [".psd"] = "image/vnd.adobe.photoshop",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "application/x-font-ttf",
        [".otf"] = "application/x-font-opentype",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".ogv"] = "video/ogg",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"] = null,
        [".vtt"] = "text/vtt",
        [".pdf"] = "application/pdf",
        [".wasm"] = "application/wasm",
        [".glb"] = "model/gltf-binary",
        [".gltf"] = "model/gltf+json",
        [".md"] = "text/plain",
        [".map"] = null,
        [".zip"] = null,
        [".bin"] = "application/octet-stream",
        [".mlapp"] = null,
        [string.Empty] = null,
    };

    /// <summary>R2025b's page for a file it does not serve.</summary>
    public const string NotFoundPage =
        "<!doctype html>\n<html>\n<head>\n    <title>Error</title>\n</head>\n<body>\n    <h1>Page not found.</h1>\n</body>\n</html>\n";

    /// <summary>What a request comes to.</summary>
    /// <param name="Status">200, 404 or 501.</param>
    /// <param name="File">The file to send for a 200.</param>
    /// <param name="ContentType">The type to send with it, or null for none.</param>
    public readonly record struct Answer(int Status, string? File, string? ContentType);

    /// <summary>
    /// Answers a request for <paramref name="relative"/> (the URL's path under the page's own,
    /// unescaped) within <paramref name="folder"/>.
    /// </summary>
    public static Answer Resolve(string folder, string relative, string method)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(relative);
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return new Answer(501, null, null);
        }

        string root;
        string path;
        try
        {
            root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string tidy = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            path = Path.GetFullPath(Path.Combine(root, tidy));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Answer(404, null, null);
        }

        if (!(path + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return new Answer(404, null, null);
        }

        if (Directory.Exists(path))
        {
            path = Path.Combine(path, "index.html");
        }

        // A name ending in a separator was a folder; a trailing separator on a file is what R2025b
        // forgives (types/a.js/ is served).
        path = path.TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(path) || Path.GetFileName(path).StartsWith('.'))
        {
            return new Answer(404, null, null);
        }

        return Served.TryGetValue(Path.GetExtension(path), out string? type)
            ? new Answer(200, path, type)
            : new Answer(404, null, null);
    }
}
