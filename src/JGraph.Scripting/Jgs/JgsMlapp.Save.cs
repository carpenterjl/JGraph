using System.IO.Compression;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>What saving code into an App Designer file did.</summary>
/// <param name="HasDesignCopy">Whether the file holds App Designer's own copy of the code (<c>appModel.mat</c>), so App Designer can open it.</param>
/// <param name="DesignCopyInStep">
/// Whether that copy says what the saved text says. False when the text is not laid out as App
/// Designer lays an app out, a function of its callbacks block is left open, or the copy is kept
/// in a form that cannot be rewritten: it was left as it was, and App Designer would show the code
/// it had.
/// </param>
/// <param name="GeneratedCodeChanged">
/// Whether the save changed code App Designer generates from its component tree. The edit runs;
/// App Designer writes its own version over it the next time it saves the app.
/// </param>
public sealed record MlappSaveResult(bool HasDesignCopy, bool DesignCopyInStep, bool GeneratedCodeChanged);

/// <content>Writing code back into an App Designer file (app-building plan U7b, ADR 0205).</content>
public static partial class JgsMlapp
{
    private const string ModelRelationship = "/relationships/appModel";
    private const string TextRun = "<w:t";
    private const string TextRunEnd = "</w:t>";

    /// <summary>
    /// Saves <paramref name="text"/> as the code of the App Designer file at <paramref name="path"/>.
    /// The document part gets the text; App Designer's copy of the code is re-derived from it; every
    /// other part of the package is kept as the bytes it is. The file is replaced only once the new
    /// one is complete, so a save that fails leaves the app as it was.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a package with a code document.</exception>
    /// <exception cref="IOException">The file could not be read or replaced.</exception>
    public static MlappSaveResult WriteCode(string path, string text) => WriteCode(path, text, null);

    /// <summary>
    /// <see cref="WriteCode(string, string)"/> with a hook called once the new file is written and
    /// before it replaces the old one, which is where a test makes a save fail.
    /// </summary>
    internal static MlappSaveResult WriteCode(string path, string text, Action? beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        List<Part> parts;
        string document;
        string? model;
        try
        {
            using ZipArchive package = ZipFile.OpenRead(path);
            document = TargetOf(package, DocumentRelationship) is { } target && package.GetEntry(target) is not null
                ? target
                : throw new InvalidDataException("the package names no code document");
            model = TargetOf(package, ModelRelationship) is { } named && package.GetEntry(named) is not null ? named : null;
            parts = package.Entries.Select(Part.Of).ToList();
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        Part code = parts.First(p => p.Name == document);
        string before;
        using (var stream = new MemoryStream(code.Bytes))
        {
            before = TextOf(stream);
        }

        (byte[] rewritten, string written) = WithText(code.Bytes, text);
        code.Bytes = rewritten;

        JgsMlappLayout was = JgsMlappLayout.Of(before);
        JgsMlappLayout now = JgsMlappLayout.Of(written);
        bool inStep = false;
        if (model is not null)
        {
            Part design = parts.First(p => p.Name == model);
            inStep = JgsMlappModel.Update(design.Bytes, was, now, out byte[]? updated);
            if (updated is not null)
            {
                design.Bytes = updated;
            }
        }

        Replace(path, parts, beforeReplace);
        return new MlappSaveResult(model is not null, inStep, !was.SameGeneratedCode(now));
    }

    /// <summary>
    /// The class file an <c>.mlapp</c> holds, as the <c>.m</c> file MATLAB's "Export to .m file"
    /// writes: the same text under the name <paramref name="className"/>, which is the name the
    /// exported file must carry. The class line and the constructor are the two places the name is.
    /// </summary>
    public static string ExportedCode(string text, string className)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(className);
        string old = JgsMlappLayout.Of(text).ClassName;
        if (old.Length == 0 || old == className)
        {
            return text;
        }

        string name = System.Text.RegularExpressions.Regex.Escape(old);
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"^(\s*classdef\s+(?:\(.*?\)\s*)?)" + name + @"\b", "${1}" + className,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"^(\s*function\s+(?:\w+\s*=\s*)?)" + name + @"\b", "${1}" + className,
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    /// <summary>One part of the package: its name, what the zip records about it, and its bytes.</summary>
    private sealed class Part
    {
        public required string Name { get; init; }

        public required byte[] Bytes { get; set; }

        public required DateTimeOffset Written { get; init; }

        public required bool Stored { get; init; }

        public required int Attributes { get; init; }

        public static Part Of(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return new Part
            {
                Name = entry.FullName,
                Bytes = bytes.ToArray(),
                Written = entry.LastWriteTime,
                Stored = entry.Length > 0 && entry.CompressedLength == entry.Length,
                Attributes = entry.ExternalAttributes,
            };
        }
    }

    /// <summary>
    /// The document part with <paramref name="text"/> as its code, and the text as written: in the
    /// line endings the document had, inside one CDATA run as MATLAB writes it. Everything of the
    /// part around the run - the declaration, the paragraph style - is kept.
    /// </summary>
    private static (byte[] Document, string Written) WithText(byte[] document, string text)
    {
        ReadOnlySpan<byte> mark = Encoding.UTF8.Preamble;
        bool marked = document.AsSpan().StartsWith(mark);
        string xml = new UTF8Encoding(false).GetString(document, marked ? mark.Length : 0, document.Length - (marked ? mark.Length : 0));

        int run = xml.IndexOf(TextRun, StringComparison.Ordinal);
        int open = run < 0 ? -1 : xml.IndexOf('>', run);
        int close = xml.LastIndexOf(TextRunEnd, StringComparison.Ordinal);
        if (open < 0 || close <= open || xml[open - 1] == '/')
        {
            throw new InvalidDataException("the code document has no text to replace");
        }

        string written = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (xml.AsSpan(open, close - open).Contains("\r\n", StringComparison.Ordinal))
        {
            written = written.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        // A CDATA section cannot hold its own terminator: the text is cut inside it.
        string data = "<![CDATA[" + written.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal) + "]]>";
        byte[] body = new UTF8Encoding(false).GetBytes(string.Concat(xml.AsSpan(0, open + 1), data, xml.AsSpan(close)));
        return (marked ? [.. mark, .. body] : body, written);
    }

    /// <summary>Writes the parts as a new package beside the old one, then puts it in the old one's place.</summary>
    private static void Replace(string path, List<Part> parts, Action? beforeReplace)
    {
        string temporary = path + ".saving";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var package = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (Part part in parts)
                {
                    ZipArchiveEntry entry = package.CreateEntry(
                        part.Name, part.Stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    entry.LastWriteTime = part.Written;
                    entry.ExternalAttributes = part.Attributes;
                    using Stream into = entry.Open();
                    into.Write(part.Bytes);
                }
            }

            beforeReplace?.Invoke();
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The original is intact, which is what matters; the leftover is beside it.
            }

            throw;
        }
    }
}
