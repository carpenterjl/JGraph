using System.Text.Json;
using System.Text.Json.Serialization;
using JGraph.Core.Model;
using JGraph.Serialization.Dto;
using JGraph.Serialization.Json;
using JGraph.Serialization.Mapping;

namespace JGraph.Serialization;

/// <summary>
/// Reads and writes the versioned JGraph ".graph" document format: indented JSON carrying a
/// <c>format</c> tag, a <c>formatVersion</c>, and the figure object graph. This is the single entry
/// point for persistence and clipboard interchange; the model itself stays free of serialization
/// concerns (an explicit DTO layer mirrors it).
/// </summary>
public static class GraphFormat
{
    /// <summary>The format tag stored in every document.</summary>
    public const string FormatTag = "jgraph";

    /// <summary>The current schema version. Documents with a newer version are rejected.</summary>
    /// <remarks>Version history: 1 = initial (M8); 2 = 3D axes, surface/contour plots, colorbar (M20);
    /// 3 = data-tip annotations (M21); 4 = packed base64 storage for large series (M22);
    /// 5 = true-colour RGB image plot (M24).</remarks>
    public const int CurrentVersion = 5;

    /// <summary>The conventional file extension for JGraph documents.</summary>
    public const string FileExtension = ".graph";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a figure to a ".graph" JSON string (clipboard interchange).</summary>
    public static string Serialize(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return JsonSerializer.Serialize(BuildDocument(figure), Options);
    }

    /// <summary>Parses a figure from a ".graph" JSON string.</summary>
    /// <exception cref="GraphFormatException">The JSON is malformed, mistagged, or a newer version.</exception>
    public static FigureModel Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        DocumentDto? document;
        try
        {
            document = JsonSerializer.Deserialize<DocumentDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new GraphFormatException("The document is not valid JGraph JSON.", ex);
        }

        return ValidateAndMap(document);
    }

    /// <summary>
    /// Writes a figure to a ".graph" file, streaming JSON straight to the stream — a million-point
    /// figure never materializes as one giant string.
    /// </summary>
    public static void Save(FigureModel figure, string path)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentException.ThrowIfNullOrEmpty(path);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, BuildDocument(figure), Options);
    }

    /// <summary>Reads a figure from a ".graph" file (streamed, no intermediate string).</summary>
    /// <exception cref="GraphFormatException">The file content is not a valid JGraph document.</exception>
    public static FigureModel Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        DocumentDto? document;
        try
        {
            using FileStream stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<DocumentDto>(stream, Options);
        }
        catch (JsonException ex)
        {
            throw new GraphFormatException("The document is not valid JGraph JSON.", ex);
        }

        return ValidateAndMap(document);
    }

    private static DocumentDto BuildDocument(FigureModel figure) => new()
    {
        Format = FormatTag,
        FormatVersion = CurrentVersion,
        Figure = FigureMapper.ToDto(figure),
    };

    private static FigureModel ValidateAndMap(DocumentDto? document)
    {
        if (document is null)
        {
            throw new GraphFormatException("The document is empty.");
        }

        if (!string.Equals(document.Format, FormatTag, StringComparison.Ordinal))
        {
            throw new GraphFormatException($"Not a JGraph document (format tag was '{document.Format}').");
        }

        if (document.FormatVersion > CurrentVersion)
        {
            throw new GraphFormatException(
                $"This document was written by a newer version of JGraph (format version {document.FormatVersion}; this build supports {CurrentVersion}).");
        }

        try
        {
            return FigureMapper.ToModel(document.Figure);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            throw new GraphFormatException("The document's contents are inconsistent and could not be loaded.", ex);
        }
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Data may contain NaN/Infinity (e.g. gaps); persist them as named literals rather than failing.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        // A local document format: keep non-ASCII text (labels, units like "²") readable, not escaped.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(),
            new ColorJsonConverter(),
        },
    };
}
