using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JGraph.Deploy;

/// <summary>A discovery file that does not describe the functions as it should, with R2025b's words (compiler and SDK plan, stage K14).</summary>
public sealed class SignatureException(string message) : Exception(message);

/// <summary>
/// A Production Server discovery file, the <c>FunctionSignatures</c> of
/// <c>compiler.build.productionServerArchive</c> and the <c>DISCOVERY:</c> part of <c>mcc -W CTF</c>
/// (stage K14): <c>{"_schemaVersion":"1.1.0","_typedefs":{…},"&lt;function&gt;":{"inputs":[…],"outputs":[…],"purpose":"…"}}</c>,
/// each parameter <c>{"name","type","purpose"}</c>, a type a class name, <c>struct:&lt;typedef&gt;</c>,
/// or a list of those with a <c>size=r,c</c> attribute. It is checked as R2025b's
/// <c>_validateFunctionSignatures</c> checks it, in its words (fixture <c>k14_production_server_archive</c>,
/// k14_build: mcc_disc_*): the file's shape first (<see cref="Read"/>), then its functions against the
/// archive's (<see cref="Check"/>), which R2025b does when the archive is built.
/// </summary>
public sealed class FunctionSignatures
{
    /// <summary>The schema versions R2025b accepts.</summary>
    private static readonly string[] KnownVersions = ["1.0.0", "1.1.0"];

    private static readonly string[] KnownTypes =
    [
        "double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "logical", "char", "string", "cell", "struct",
    ];

    private static readonly Regex VariableName = new("^[A-Za-z][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant);

    private FunctionSignatures(string? schemaVersion, IReadOnlyList<CtfTypedef> typedefs, IReadOnlyDictionary<string, Described> functions, IReadOnlyList<string> warnings)
    {
        SchemaVersion = schemaVersion;
        Typedefs = typedefs;
        Functions = functions;
        Warnings = warnings;
    }

    /// <summary>A function as the file describes it: its purpose and its parameters.</summary>
    /// <remarks>
    /// <paramref name="ShapeFault"/> is the message for a value that is a JSON array (R2025b reads it as a function's
    /// name, which <see cref="Check"/> looks for in the archive before anything is said about its shape: sig_routes_file).
    /// </remarks>
    public sealed record Described(string Purpose, IReadOnlyList<CtfSignatureParameter> Inputs, IReadOnlyList<CtfSignatureParameter> Outputs, string? ShapeFault = null);

    /// <summary>The file's <c>_schemaVersion</c>, or null when it has none (R2025b warns and reads on).</summary>
    public string? SchemaVersion { get; }

    /// <summary>The structs under <c>_typedefs</c>.</summary>
    public IReadOnlyList<CtfTypedef> Typedefs { get; }

    /// <summary>The functions described, by name, in the file's order.</summary>
    public IReadOnlyDictionary<string, Described> Functions { get; }

    /// <summary>What R2025b warns about while reading: a missing schema version.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Reads and checks the file's shape, as R2025b does when the option is set or the -W part read.</summary>
    /// <exception cref="SignatureException">The file is not JSON, or not a discovery file (R2025b's message).</exception>
    public static FunctionSignatures Read(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads discovery text, as <see cref="Read"/> reads a file.</summary>
    public static FunctionSignatures Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using JsonDocument document = ParseDocument(text);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new SignatureException($"invalid conversion: JSON {KindName(root.ValueKind)} to JSON object");
        }

        var warnings = new List<string>();
        string? version = null;
        if (root.TryGetProperty("_schemaVersion", out JsonElement versionElement))
        {
            version = versionElement.ValueKind == JsonValueKind.String ? versionElement.GetString() : versionElement.GetRawText();
            if (!KnownVersions.Contains(version))
            {
                throw new SignatureException($"MATLAB Production Server discovery schema version '{version}' is not supported.\n");
            }
        }
        else
        {
            warnings.Add("Schema version property '_schemaVersion' was not found in the function signatures file.");
        }

        var typedefs = new List<CtfTypedef>();
        if (root.TryGetProperty("_typedefs", out JsonElement typedefsElement))
        {
            if (typedefsElement.ValueKind != JsonValueKind.Object)
            {
                throw new SignatureException("Property \"_typedefs\" must be of type \"JSON object\".\n");
            }

            foreach (JsonProperty typedef in typedefsElement.EnumerateObject())
            {
                typedefs.Add(ReadTypedef(typedef));
            }
        }

        var functions = new Dictionary<string, Described>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name is "_schemaVersion" or "_typedefs")
            {
                continue;
            }

            // A string (a routes file's "version") is passed over; an array ("pathmap") is a function's name
            // (sig_routes_file); what another scalar is was not measured, so it is passed over too.
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    functions[property.Name] = ReadFunction(property, typedefs);
                    break;
                case JsonValueKind.Array:
                    functions[property.Name] = new Described("", [], [], $"In function signature \"{property.Name}\":\nFunction signature must be of type \"JSON object\".\n");
                    break;
                default:
                    break;
            }
        }

        return new FunctionSignatures(version, typedefs, functions, warnings);
    }

    /// <summary>
    /// Checks the described functions against the archive's (R2025b's checks at the archive build and
    /// at the option's set): each is in the archive, and its counts are the function's.
    /// </summary>
    /// <exception cref="SignatureException">R2025b's message.</exception>
    public void Check(IReadOnlyList<CtfFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        foreach (string name in Functions.Keys)
        {
            if (!functions.Any(f => f.Name == name))
            {
                throw new SignatureException($"Function signature \"{name}\" was defined but is not contained in the deployed archive.\n");
            }
        }

        foreach ((string name, Described described) in Functions)
        {
            if (described.ShapeFault is not null)
            {
                throw new SignatureException(described.ShapeFault);
            }

            CtfFunction function = functions.First(f => f.Name == name);
            CheckCount(name, "inputs", described.Inputs.Count, function.Inputs);
            CheckCount(name, "outputs", described.Outputs.Count, function.Outputs);
        }
    }

    /// <summary>The build with each described function's help and signature, and the typedefs, after <see cref="Check"/>.</summary>
    public CtfBuild ApplyTo(CtfBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        Check(build.PublicFunctions);
        return build with
        {
            PublicFunctions =
            [
                .. build.PublicFunctions.Select(f => Functions.TryGetValue(f.Name, out Described? described)
                    ? f with { Help = described.Purpose, Signature = new CtfSignature(described.Inputs, described.Outputs) }
                    : f),
            ],
            Typedefs = Typedefs,
        };
    }

    private static void CheckCount(string function, string property, int described, IReadOnlyList<string> declared)
    {
        bool variadic = declared.Count > 0 && declared[^1] is "varargin" or "varargout";
        int fixedCount = variadic ? declared.Count - 1 : declared.Count;
        if (described != declared.Count && !(variadic && described >= fixedCount))
        {
            throw new SignatureException(
                $"In function signature \"{function}\":\nProperty \"{property}\" has an invalid number of arguments: {described}. Specify {declared.Count}.\n");
        }
    }

    private static CtfTypedef ReadTypedef(JsonProperty typedef)
    {
        if (typedef.Value.ValueKind != JsonValueKind.Object)
        {
            throw new SignatureException($"In typedef \"{typedef.Name}\":\nProperty \"{typedef.Name}\" must be of type \"JSON object\".\n");
        }

        string purpose = "";
        var fields = new List<CtfSignatureParameter>();
        foreach (JsonProperty property in typedef.Value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "purpose":
                    purpose = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    break;
                case "type":
                    if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() != "struct")
                    {
                        throw new SignatureException($"In typedef \"{typedef.Name}\":\nProperty \"type\" has an invalid value: {property.Value.GetRawText()}.\n");
                    }

                    break;
                case "fields":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        throw new SignatureException($"In typedef \"{typedef.Name}\":\nProperty \"fields\" must be of type \"JSON array\".\n");
                    }

                    foreach (JsonElement field in property.Value.EnumerateArray())
                    {
                        fields.Add(ReadParameter(field, $"In typedef \"{typedef.Name}\":\n", []));
                    }

                    break;
                default:
                    throw new SignatureException($"In typedef \"{typedef.Name}\":\nProperty \"{property.Name}\" is not recognized.\n");
            }
        }

        return new CtfTypedef(typedef.Name, purpose, fields);
    }

    private static Described ReadFunction(JsonProperty function, IReadOnlyList<CtfTypedef> typedefs)
    {
        string prefix = $"In function signature \"{function.Name}\":\n";
        if (function.Value.ValueKind != JsonValueKind.Object)
        {
            throw new SignatureException($"{prefix}Function signature must be of type \"JSON object\".\n");
        }

        string purpose = "";
        List<CtfSignatureParameter>? inputs = null;
        List<CtfSignatureParameter>? outputs = null;
        foreach (JsonProperty property in function.Value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "purpose":
                    purpose = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    break;
                case "inputs":
                case "outputs":
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        throw new SignatureException($"{prefix}Property \"{property.Name}\" must be of type \"JSON array\".\n");
                    }

                    var parameters = new List<CtfSignatureParameter>();
                    foreach (JsonElement parameter in property.Value.EnumerateArray())
                    {
                        parameters.Add(ReadParameter(parameter, prefix, typedefs));
                    }

                    if (property.Name == "inputs")
                    {
                        inputs = parameters;
                    }
                    else
                    {
                        outputs = parameters;
                    }

                    break;
                }

                default:
                    throw new SignatureException($"{prefix}Property \"{property.Name}\" is not recognized.\n");
            }
        }

        if (inputs is null)
        {
            throw new SignatureException($"{prefix}Property \"inputs\" is missing.\n");
        }

        if (outputs is null)
        {
            throw new SignatureException($"{prefix}Property \"outputs\" is missing.\n");
        }

        return new Described(purpose, inputs, outputs);
    }

    private static CtfSignatureParameter ReadParameter(JsonElement parameter, string prefix, IReadOnlyList<CtfTypedef> typedefs)
    {
        if (parameter.ValueKind != JsonValueKind.Object)
        {
            throw new SignatureException($"{prefix}Parameter must be of type \"JSON object\".\n");
        }

        string? name = null;
        string type = "";
        string size = "";
        string help = "";
        foreach (JsonProperty property in parameter.EnumerateObject())
        {
            switch (property.Name)
            {
                case "name":
                    name = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    if (!VariableName.IsMatch(name))
                    {
                        throw new SignatureException($"{prefix}Property \"name\" has an invalid MATLAB variable name: \"{name}\".\n");
                    }

                    break;
                case "purpose":
                    help = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    break;
                case "type":
                {
                    IEnumerable<JsonElement> words = property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray() : [property.Value];
                    foreach (JsonElement word in words)
                    {
                        string text = word.ValueKind == JsonValueKind.String ? word.GetString()! : word.GetRawText();
                        if (text.StartsWith("size=", StringComparison.Ordinal))
                        {
                            string[] parts = text["size=".Length..].Split(',');
                            if (parts.Length < 2)
                            {
                                throw new SignatureException($"{prefix}Attribute \"size\" requires at least 2 values.\n");
                            }

                            size = "[" + string.Join(",", parts.Select(static p => p.Trim())) + "]";
                        }
                        else if (text.StartsWith("struct:", StringComparison.Ordinal))
                        {
                            string typedef = text["struct:".Length..];
                            if (!typedefs.Any(t => t.Name == typedef))
                            {
                                throw new SignatureException($"{prefix}struct \"{typedef}\" was referenced but not defined.\n");
                            }

                            if (type.Length == 0)
                            {
                                type = text;
                            }
                        }
                        else if (KnownTypes.Contains(text))
                        {
                            if (type.Length == 0)
                            {
                                type = text;
                            }
                        }
                        else
                        {
                            throw new SignatureException($"{prefix}Property \"type\" has an invalid value: \"{text}\".\n");
                        }
                    }

                    break;
                }

                default:
                    throw new SignatureException($"{prefix}Property \"{property.Name}\" is not recognized.\n");
            }
        }

        if (name is null)
        {
            throw new SignatureException($"{prefix}Property \"name\" is missing.\n");
        }

        return new CtfSignatureParameter(name, type, size, help);
    }

    /// <summary>
    /// The JSON document, comments allowed as R2025b's template writes them above the text. A
    /// syntax error is reported as R2025b's parser reports the one the fixture met (a value that is
    /// not JSON: <c>JSON ParserException (E0008): Expected leading '"' character while parsing value
    /// of type string (Line 1, Column 12)</c>, the line and a caret), and otherwise in JGraph's words.
    /// </summary>
    private static JsonDocument ParseDocument(string text)
    {
        try
        {
            return JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = false });
        }
        catch (JsonException error)
        {
            // R2025b's column is the offending token's index from 0, where its caret stands (sig_notjson: the n of
            // "not" in {"this is": not json is Column 12); the .NET reader's position is stepped back to the token's start.
            int line = (int)(error.LineNumber ?? 0);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            string lineText = line < lines.Length ? lines[line] : "";
            int column = Math.Min((int)(error.BytePositionInLine ?? 0), lineText.Length);
            while (column > 0 && char.IsAsciiLetterOrDigit(lineText[column - 1]))
            {
                column--;
            }

            char at = column < lineText.Length ? lineText[column] : '\0';
            string reason = char.IsAsciiLetter(at)
                ? "JSON ParserException (E0008): Expected leading '\"' character while parsing value of type string"
                : $"JSON ParserException: {error.Message.Split(" Path:")[0].Split(" LineNumber:")[0]}";
            throw new SignatureException($"{reason} (Line {line + 1}, Column {column})\n{lineText}\n{new string(' ', column)}^");
        }
    }

    private static string KindName(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "object",
    };

    /// <summary>R2025b's text for a missing schema version, kept beside the file's other warnings for the callers' messages.</summary>
    public static string WarningText(IEnumerable<string> warnings) => string.Join("\n", warnings);

    /// <summary>The discovery text's bytes as UTF-8, for a copy kept beside the archive's other files.</summary>
    public static byte[] Bytes(string text) => new UTF8Encoding(false).GetBytes(text);
}
