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
    /// 3 = data-tip annotations (M21).</remarks>
    public const int CurrentVersion = 3;

    /// <summary>The conventional file extension for JGraph documents.</summary>
    public const string FileExtension = ".graph";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a figure to a ".graph" JSON string.</summary>
    public static string Serialize(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        var document = new DocumentDto
        {
            Format = FormatTag,
            FormatVersion = CurrentVersion,
            Figure = FigureMapper.ToDto(figure),
        };
        return JsonSerializer.Serialize(document, Options);
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

    /// <summary>Writes a figure to a ".graph" file.</summary>
    public static void Save(FigureModel figure, string path)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentException.ThrowIfNullOrEmpty(path);
        File.WriteAllText(path, Serialize(figure));
    }

    /// <summary>Reads a figure from a ".graph" file.</summary>
    /// <exception cref="GraphFormatException">The file content is not a valid JGraph document.</exception>
    public static FigureModel Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Deserialize(File.ReadAllText(path));
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
