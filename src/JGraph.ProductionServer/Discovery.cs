using System.Text;
using System.Text.Json;
using JGraph.Deploy;

namespace JGraph.ProductionServer;

/// <summary>
/// <c>GET /api/discovery</c> (compiler and SDK plan, stage K14; k14_server: get.discovery): the deployed
/// archives' function signatures as R2025b's server writes them, from the manifests' discovery
/// elements. An archive built without a discovery file is not listed, as R2025b lists none.
/// </summary>
internal static class Discovery
{
    public static string Json(IEnumerable<(string Name, CtfManifest Manifest)> archives)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("discoverySchemaVersion", "1.0.0");
            writer.WriteStartObject("archives");
            foreach ((string name, CtfManifest manifest) in archives.OrderBy(static a => a.Name, StringComparer.Ordinal))
            {
                var described = manifest.PublicFunctions.Where(static f => f.Signature is not null).ToList();
                if (described.Count == 0)
                {
                    continue;
                }

                writer.WriteStartObject(name);
                writer.WriteString("archiveSchemaVersion", "1.2.0");
                // The archive's own uuid, <name>_<guid>, as muserve writes it (k15_server: discovery); the in-MATLAB test server wrote none.
                writer.WriteString("archiveUuid", manifest.Uuid);
                writer.WriteStartObject("functions");
                foreach (CtfFunction function in described)
                {
                    writer.WriteStartObject(function.Name);
                    writer.WriteStartArray("signatures");
                    writer.WriteStartObject();
                    writer.WriteString("help", function.Help ?? "");
                    Parameters(writer, "inputs", function.Signature!.Inputs);
                    Parameters(writer, "outputs", function.Signature!.Outputs);
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                // The release the runtime follows, named as R2025b's server names its own (get.discovery).
                writer.WriteString("matlabRuntimeRelease", "R2025b");
                writer.WriteString("matlabRuntimeVersion", "25.2.0");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Parameters(Utf8JsonWriter writer, string name, IReadOnlyList<CtfSignatureParameter> parameters)
    {
        writer.WriteStartArray(name);
        foreach (CtfSignatureParameter parameter in parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("help", parameter.Help);
            writer.WriteStartArray("mwsize");
            // "[1,1]" as numbers; a dimension given as ':' is written as the string ":" (R2025b's
            // discovery writes "mwsize":[1,":"] for size=1,: - the compiler plan's acceptance set, stage K22).
            if (parameter.Size.Length > 2)
            {
                foreach (string part in parameter.Size.Trim('[', ']').Split(','))
                {
                    string trimmed = part.Trim();
                    if (int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number))
                    {
                        writer.WriteNumberValue(number);
                    }
                    else
                    {
                        writer.WriteStringValue(trimmed);
                    }
                }
            }

            writer.WriteEndArray();
            writer.WriteString("mwtype", parameter.Type);
            writer.WriteString("name", parameter.Name);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
