using System.Text.Json;
using System.Text.Json.Serialization;
using JGraph.Core.Drawing;

namespace JGraph.Serialization.Json;

/// <summary>
/// Serializes a <see cref="Color"/> as a compact hex string ("#RRGGBB" or "#AARRGGBB"), the natural
/// human-readable form for the document format. A registered converter also covers <see cref="Color"/>?
/// automatically.
/// </summary>
public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? hex = reader.GetString();
        if (hex is null || !Color.TryParse(hex, out Color color))
        {
            throw new JsonException($"'{hex}' is not a valid color.");
        }

        return color;
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}
