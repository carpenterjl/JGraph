using System.Text.RegularExpressions;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Designer;

/// <summary>Where an app's thumbnail comes from (<c>MetadataModel.ScreenshotMode</c>).</summary>
public enum DesignScreenshotMode
{
    /// <summary><c>auto</c>: captured when the app runs from App Designer.</summary>
    Auto,

    /// <summary><c>manual</c>: an image the user chose.</summary>
    Manual,

    /// <summary><c>none</c>: no thumbnail.</summary>
    None,
}

/// <summary>What App Details holds for an app, as one value: what its dialog and its inspector rows edit, and what one Undo puts back.</summary>
/// <param name="Name">The app's name, which is not its class's.</param>
/// <param name="Version">The version.</param>
/// <param name="Author">The author.</param>
/// <param name="Summary">The summary.</param>
/// <param name="Description">The description.</param>
/// <param name="Mode">Where the thumbnail comes from.</param>
/// <param name="Thumbnail">The thumbnail as a data URI; empty when there is none.</param>
public sealed record DesignAppDetailsValues(string Name, string Version, string Author, string Summary, string Description, DesignScreenshotMode Mode, string Thumbnail);

/// <summary>
/// App Details' rules (App Designer window plan W8a), free of a window: <c>AppDetailsUtil</c>'s version and image checks,
/// <c>FileWriter.validateVersion</c>'s normalising of what reaches a package, the dialog's limits and words, and the thumbnail
/// as <c>AppThumbnailUtils</c> and <c>ImageUtils</c> make it.
/// </summary>
public static partial class DesignAppDetails
{
    /// <summary>The dialog's title (<c>appDetailsDialogTitle</c>).</summary>
    public const string Title = "App Details";

    /// <summary>The dialog's first section and the inspector's group (<c>appDetailsSharingDetailsTitle</c>).</summary>
    public const string SharingDetailsTitle = "Sharing Details";

    /// <summary>The dialog's second section and the inspector's group (<c>appDetailsCodeOptionsTitle</c>).</summary>
    public const string CodeOptionsTitle = "Code Options";

    /// <summary>The words over the sharing details (<c>appDetailsDialogHelpText</c>).</summary>
    public const string HelpWords = "Sharing details display in certain situations, such as when you share your app or view your app in some system file browsers.";

    /// <summary>A version that will not do (<c>appDetailsDialogInvalidVersionMessage</c>).</summary>
    public const string InvalidVersion = "Version must be in the format of Major.Minor (for example, 4.11).";

    /// <summary>An image that will not do (<c>appDetailsDialogInvalidImageMessage</c>).</summary>
    public const string InvalidImage = "Selected file must be a valid GIF, JPEG, or PNG file (*.gif, *.jpeg, *.jpg, or *.png).";

    /// <summary>The image that could not be read back (<c>appDetailsDialogErrorLoadingScreenshot</c>).</summary>
    public const string ImageLoadError = "Error loading image";

    /// <summary>The thumbnail's words while Run captures it (<c>appDetailsDialogSelectScreenshotWithAutoCaptureTooltip</c>).</summary>
    public const string AutoCaptureWords = "App thumbnail is captured whenever the app runs unless you select a custom image";

    /// <summary>The thumbnail's words when Run does not capture it (<c>appDetailsDialogSelectScreenshotWithoutAutoCaptureTooltip</c>).</summary>
    public const string NoCaptureWords = "App thumbnail";

    /// <summary>The thumbnail's button (<c>appDetailsDialogSelectScreenshot</c>).</summary>
    public const string SelectImage = "Select a custom image";

    /// <summary>The image dialog's title (<c>iconFileDialogTitle</c>).</summary>
    public const string ImageDialogTitle = "Open Image File";

    /// <summary>The image dialog's filter, in order (<c>imageFileFilter*</c>).</summary>
    public const string ImageFilter = "All Image Files|*.gif;*.jpeg;*.jpg;*.png|Graphics Interchange Format|*.gif|Joint Photographic Experts Group|*.jpg;*.jpeg|Portable Network Graphics|*.png";

    /// <summary>The limit on the name and the version, in either format's dialog.</summary>
    public const int NameLimit = 60;

    /// <summary>The limit on the author in an <c>.mlapp</c>'s dialog; a plain-text app's takes <see cref="NameLimit"/>.</summary>
    public const int MlappAuthorLimit = 42;

    /// <summary>The limit on the summary.</summary>
    public const int SummaryLimit = 140;

    /// <summary>The limit on the description.</summary>
    public const int DescriptionLimit = 5000;

    /// <summary>The width a plain-text app's thumbnail fits in (<c>createAppThumbnailDataURI</c>), which is the dialog's box.</summary>
    public const int ThumbnailWidth = 220;

    /// <summary>The height a plain-text app's thumbnail fits in.</summary>
    public const int ThumbnailHeight = 165;

    /// <summary>The extensions a thumbnail can have (<c>validateScreenshotExtension</c>).</summary>
    private static readonly string[] ImageExtensions = ["PNG", "JPG", "JPEG", "GIF"];

    /// <summary>Why <paramref name="version"/> will not do (<c>AppDetailsUtil.validateVersion</c>); null when it will.</summary>
    public static string? WhyNotVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return WholeVersion().IsMatch(version) ? null : InvalidVersion;
    }

    /// <summary>Whether <paramref name="text"/> can still become a version as it is typed (<c>isTemporaryVersionValid</c>).</summary>
    public static bool MayBecomeVersion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WholeVersion().IsMatch(text) || PartVersion().IsMatch(text);
    }

    /// <summary>What a package keeps for <paramref name="version"/> (<c>FileWriter.validateVersion</c>), which asks less than the dialog: it needs only to start with Major.Minor.</summary>
    public static string VersionForPackage(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return StartOfVersion().IsMatch(version) ? version : "1.0";
    }

    /// <summary>Why <paramref name="path"/> cannot be a thumbnail by its extension (<c>validateScreenshotExtension</c>); null when it can.</summary>
    public static string? WhyNotImage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return ImageExtensions.Contains(extension) ? null : InvalidImage;
    }

    /// <summary>A file's bytes as a data URI under <paramref name="format"/> (<c>ImageUtils.getImageDataURIFromBytes</c>); empty for no bytes.</summary>
    public static string DataUri(byte[] bytes, string format)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return bytes.Length == 0 ? string.Empty : $"data:image/{format};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>A data URI's bytes and format; null when it is not one.</summary>
    public static (byte[] Bytes, string Format)? OfDataUri(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Match match = DataUriForm().Match(uri);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            return (Convert.FromBase64String(match.Groups[2].Value), match.Groups[1].Value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The size an image of <paramref name="width"/> by <paramref name="height"/> takes in a plain-text app's file: within
    /// 220 by 165, kept when it fits. R2025b's <c>createAppThumbnailDataURI</c> looks at the longer side alone, so that a square
    /// image stays any size and a landscape one of 221 by 166 keeps its height of 166 (measured, ui-probes/w8/w8_details), where
    /// its comment says it fits; here it does.
    /// </summary>
    public static (int Width, int Height) FittedSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width <= ThumbnailWidth && height <= ThumbnailHeight)
        {
            return (width, height);
        }

        // imresize's rule for the side it works out: ceil(other * asked / side).
        return (long)width * ThumbnailHeight >= (long)height * ThumbnailWidth
            ? (ThumbnailWidth, (int)(((long)height * ThumbnailWidth + width - 1) / width))
            : ((int)(((long)width * ThumbnailHeight + height - 1) / height), ThumbnailHeight);
    }

    /// <summary>The image file at <paramref name="path"/> as an <c>.mlapp</c> keeps it (<c>ImageUtils.getImageDataURIFromFile</c>): its bytes as they are, under its extension.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static string UriForPackage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return DataUri(File.ReadAllBytes(path), Path.GetExtension(path).TrimStart('.').ToLowerInvariant());
    }

    /// <summary>The image file at <paramref name="path"/> as a plain-text app keeps it (<c>createAppThumbnailDataURI</c>): a JPEG fitted within 220 by 165, as <c>imwrite</c>'s default writes one.</summary>
    /// <exception cref="InvalidDataException">The file is no image this build reads.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static string UriForPlainText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using ImageBuffer image = ImageCodec.Read(path);
        (int width, int height) = FittedSize(image.Width, image.Height);
        string temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            if (width == image.Width && height == image.Height)
            {
                ImageCodec.Write(temporary, image, 75);
            }
            else
            {
                using ImageBuffer fitted = Geometry.Resize(image, height, width);
                ImageCodec.Write(temporary, fitted, 75);
            }

            return DataUri(File.ReadAllBytes(temporary), "jpg");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    [GeneratedRegex(@"^[0-9]+\.[0-9]+$")]
    private static partial Regex WholeVersion();

    [GeneratedRegex(@"^([0-9]?|[0-9]+\.)$")]
    private static partial Regex PartVersion();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+")]
    private static partial Regex StartOfVersion();

    [GeneratedRegex(@"^data:image/([A-Za-z0-9+.-]+);base64,(.*)$", RegexOptions.Singleline)]
    private static partial Regex DataUriForm();
}

/// <summary>
/// An app's help text (App Designer window plan W8a): <c>HelpCommentsDialogController</c>, <c>ClassBuilder._resetStartLines</c> and
/// <c>CodeFormatter.wrapComment</c> write it under the class line, and <c>HelpCommentsParser</c> reads it back.
/// </summary>
public static class DesignHelpText
{
    /// <summary>How far a line of the class goes (<c>getRightHandTextLimitLength</c>).</summary>
    public const int RightHandLimit = 75;

    /// <summary>What starts a line of the description (<c>HelpCommentDescriptionPrefix</c>).</summary>
    public const string DescriptionPrefix = "%   ";

    /// <summary>The dialog's title (<c>helpCommentsDialogTitle</c> for an app).</summary>
    public const string Title = "App Help Text";

    /// <summary>What an empty summary says (<c>helpCommentsDialogSummaryPlaceholder</c> for an app).</summary>
    public const string SummaryPlaceholder = "Summary of the app";

    /// <summary>What an empty description says (<c>helpCommentsDialogDescriptionPlaceholder</c>).</summary>
    public const string DescriptionPlaceholder = "Detailed explanation";

    /// <summary><c>CodeFormatter.wrapComment</c>: each line of <paramref name="comment"/>, broken at single blanks and tabs, under its prefix, to <paramref name="limit"/>.</summary>
    public static IReadOnlyList<string> Wrap(string comment, int limit)
    {
        ArgumentNullException.ThrowIfNull(comment);
        string text = comment.StartsWith('%') ? comment : "% " + comment;
        int prefixLength = 1;
        while (prefixLength < text.Length && text[prefixLength] is ' ' or '\t')
        {
            prefixLength++;
        }

        string prefix = text[..prefixLength];
        var wrapped = new List<string>();
        foreach (string line in text[prefixLength..].Split('\n'))
        {
            string? current = null;
            foreach (string word in line.Split(' ', '\t'))
            {
                if (current is null)
                {
                    current = prefix + word;
                }
                else if (current.Length + 1 + word.Length <= limit)
                {
                    current += " " + word;
                }
                else
                {
                    wrapped.Add(current);
                    current = prefix + word;
                }
            }

            wrapped.Add(current!);
        }

        return wrapped;
    }

    /// <summary>
    /// A description as the dialog's OK holds it (<c>_handleOkButton</c>): broken to the width the class line leaves, so that
    /// what the dialog shows next time is what the file says.
    /// </summary>
    public static string WrappedDescription(string description, int indentSize)
    {
        ArgumentNullException.ThrowIfNull(description);
        int limit = RightHandLimit - indentSize;
        return string.Join('\n', Wrap(DescriptionPrefix + description.Replace("\r\n", "\n", StringComparison.Ordinal), limit).Select(static l => l[DescriptionPrefix.Length..]));
    }

    /// <summary>The comment lines under the class line (<c>ClassBuilder._resetStartLines</c>): nothing with neither a summary nor a description.</summary>
    public static IReadOnlyList<string> Lines(string className, string summary, string description, string indent)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(indent);
        if (summary.Length == 0 && description.Length == 0)
        {
            return [];
        }

        int limit = RightHandLimit - indent.Length;
        var lines = new List<string>(Wrap("%" + className.ToUpperInvariant() + (summary.Length > 0 ? " " + summary : string.Empty), limit));
        if (description.Length > 0)
        {
            lines.AddRange(Wrap(DescriptionPrefix + description, limit));
        }

        return lines.ConvertAll(l => indent + l);
    }

    /// <summary>
    /// The summary and the description the comment lines under the class hold (<c>HelpCommentsParser</c>, over what
    /// <c>getMFileHelpText</c> gives): nothing unless the first line starts with the class's name in capitals, the summary
    /// running on until a line starts with three blanks, and the description every line from there, without them. A line of
    /// nothing but blanks is dropped, as the help text drops it.
    /// </summary>
    public static (string Summary, string Description) Parse(IReadOnlyList<string> comments, string className)
    {
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(className);
        var lines = new List<string>();
        foreach (string comment in comments)
        {
            string text = comment.TrimStart();
            if (!text.StartsWith('%'))
            {
                break;
            }

            string after = text[1..];
            if (after.Trim().Length > 0)
            {
                lines.Add(after.TrimEnd());
            }
        }

        string upper = className.ToUpperInvariant();
        if (lines.Count == 0 || !lines[0].StartsWith(upper, StringComparison.Ordinal))
        {
            return (string.Empty, string.Empty);
        }

        // strrep takes the class's name wherever the line has it, and removeFirstBlank the one blank after.
        string summary = lines[0].Replace(upper, string.Empty, StringComparison.Ordinal);
        if (summary.StartsWith(' '))
        {
            summary = summary[1..];
        }

        int index = 1;
        while (index < lines.Count && !lines[index].StartsWith("   ", StringComparison.Ordinal))
        {
            summary += " " + lines[index].Trim();
            index++;
        }

        string description = string.Join('\n', lines.Skip(index).Select(static l => l.Length >= 3 ? l[3..] : string.Empty));
        return (summary, description);
    }
}
