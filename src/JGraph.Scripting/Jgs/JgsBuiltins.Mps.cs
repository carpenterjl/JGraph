namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB Production Server's JSON functions (compiler and SDK plan, stage K14): <c>mps.json.encode</c>,
/// <c>mps.json.decode</c>, <c>mps.json.encoderequest</c> and <c>mps.json.decoderesponse</c>, their
/// arguments checked as R2025b's M-files check them (toolbox/mps/json/+mps/+json, an inputParser
/// with validatestring choices) and their work <see cref="JgsMpsJson"/>'s (fixture <c>k14_mps_json</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly string[] EncodeOptionNames = ["Format", "NanInfType", "PrettyPrint"];

    private static readonly string[] RequestOptionNames = ["Nargout", "Format", "NanInfType", "OutputFormat", "OutputNanInfType", "PrettyPrint"];

    private static void RegisterMpsJsonBuiltins(Interpreter interpreter, Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("mps.json.encode", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            var options = new MpsOptions();
            ReadMpsOptions(args, 1, EncodeOptionNames, options, line, col);
            return JgsValue.Str(MpsGuarded(() => JgsMpsJson.Encode(args[0], options.Format, options.NanInfType, options.PrettyPrint), line, col));
        });

        Define("mps.json.decode", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args.Count > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
            }

            string text = MpsText(args[0], line, col);
            return MpsGuarded(() => JgsMpsJson.Decode(text, interpreter.FindClass), line, col);
        });

        Define("mps.json.encoderequest", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args[0].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ArgumentFailedValidation",
                    "The value of 'rhs' is invalid. It must satisfy the function: iscell.");
            }

            var options = new MpsOptions();
            ReadMpsOptions(args, 1, RequestOptionNames, options, line, col);
            // nargout is num2str of whatever numeric was given (r_req_nargout_*: 2.5, '1  2', nothing for []).
            string nargout = options.Nargout is { } given ? TextOf(NumberText([given], line, col)) : "1";
            JgsValue[] rhs = args[0].AsCell;
            // R2025b reads the cell's first length(rhs) elements in storage order: a 2-by-2 cell gives two (r_req_cell_2x2).
            rhs = rhs.Take(args[0].Dims.Length == 0 ? rhs.Length : args[0].Dims.Max()).ToArray();
            return JgsValue.Str(MpsGuarded(
                () => JgsMpsJson.EncodeRequest(rhs, nargout, options.Format, options.NanInfType, options.OutputFormat, options.OutputNanInfType, options.PrettyPrint),
                line, col));
        });

        Define("mps.json.decoderesponse", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args.Count > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            string text = MpsText(args[0], line, col);
            return MpsGuarded(() => JgsMpsJson.DecodeResponse(text, interpreter.FindClass), line, col);
        });
    }

    private sealed class MpsOptions
    {
        public JgsMpsJson.Notation Format { get; set; } = JgsMpsJson.Notation.Small;

        public JgsMpsJson.NanInfType NanInfType { get; set; } = JgsMpsJson.NanInfType.String;

        public JgsMpsJson.Notation OutputFormat { get; set; } = JgsMpsJson.Notation.Small;

        public JgsMpsJson.NanInfType OutputNanInfType { get; set; } = JgsMpsJson.NanInfType.String;

        public bool PrettyPrint { get; set; }

        public JgsValue? Nargout { get; set; }
    }

    private static T MpsGuarded<T>(Func<T> work, int line, int col)
    {
        try
        {
            return work();
        }
        catch (JgsMpsJson.Refused refused)
        {
            throw new JgsRuntimeException(line, col, refused.Identifier, refused.Message);
        }
    }

    /// <summary>The text mps.json.decode and decoderesponse read: a char array by its column-major characters, or a string scalar that is not missing (o_decode_*).</summary>
    private static string MpsText(JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix)
        {
            string[] rows = value.CharMatrixRows();
            var text = new System.Text.StringBuilder();
            int width = rows.Length == 0 ? 0 : rows[0].Length;
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < rows.Length; r++)
                {
                    text.Append(rows[r][c]);
                }
            }

            return text.ToString();
        }

        if (value.Type == JgsType.String || (value.IsStringArray && value.ArrayLength == 1 && !IsMissingText(value.ElementAt(0).AsString)))
        {
            return TextOf(value);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:checkSyntacticWarnings:MustBeStringScalarOrCharacterVector",
            "All input arguments must be string scalars or character vectors.");
    }

    /// <summary>
    /// The name-value pairs from <paramref name="from"/> on, as R2025b's inputParser reads them: names by
    /// a unique start in either case, the last value of a name winning, each value checked as its
    /// addParameter does (o_*: validatestring for the choices, a logical scalar for PrettyPrint, isnumeric for Nargout).
    /// </summary>
    private static void ReadMpsOptions(IReadOnlyList<JgsValue> args, int from, string[] names, MpsOptions options, int line, int col)
    {
        for (int i = from; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]) || args[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string name = ParameterName(TextOf(args[i]), names, line, col);
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{name}'. Name-value pair arguments require a name followed by a value.");
            }

            JgsValue value = args[i + 1];
            switch (name)
            {
                case "Format":
                    options.Format = Choice(value, name, ["small", "large"], line, col) == "large" ? JgsMpsJson.Notation.Large : JgsMpsJson.Notation.Small;
                    break;
                case "OutputFormat":
                    options.OutputFormat = Choice(value, name, ["small", "large"], line, col) == "large" ? JgsMpsJson.Notation.Large : JgsMpsJson.Notation.Small;
                    break;
                case "NanInfType":
                    options.NanInfType = Choice(value, name, ["string", "object"], line, col) == "object" ? JgsMpsJson.NanInfType.Object : JgsMpsJson.NanInfType.String;
                    break;
                case "OutputNanInfType":
                    options.OutputNanInfType = Choice(value, name, ["string", "object"], line, col) == "object" ? JgsMpsJson.NanInfType.Object : JgsMpsJson.NanInfType.String;
                    break;
                case "PrettyPrint":
                    if (!(value.Type == JgsType.Bool || (IsLogicalValue(value) && value.ArrayLength == 1)) || value.AsExternalOrNull() is not null)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:assertion:failed", $"The value of '{name}' is invalid. Assertion failed.");
                    }

                    options.PrettyPrint = (value.Type == JgsType.Bool ? value : value.ElementAt(0)).AsNumber != 0;
                    break;
                case "Nargout":
                    if (!IsNumericValue(value) || IsLogicalValue(value))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ArgumentFailedValidation",
                            $"The value of '{name}' is invalid. It must satisfy the function: isnumeric.");
                    }

                    options.Nargout = value;
                    break;
            }
        }
    }

    /// <summary>validatestring's answer for an option (o_format_*): a text that starts one choice, in either case; anything else refused in its words.</summary>
    private static string Choice(JgsValue value, string name, string[] choices, int line, int col)
    {
        string listed = string.Join(", ", choices.Select(static c => "'" + c + "'"));
        if (!IsTextScalar(value) || value.IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unrecognizedStringChoice",
                $"The value of '{name}' is invalid. Expected input to match one of these values:\n\n{listed}\n\nThe input did not match any of the valid values.");
        }

        string text = TextOf(value);
        string[] matches = [.. choices.Where(c => text.Length > 0 && c.StartsWith(text, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length == 1 || (matches.Length > 1 && matches.Any(m => m.Equals(text, StringComparison.OrdinalIgnoreCase))))
        {
            return matches.Length == 1 ? matches[0] : matches.First(m => m.Equals(text, StringComparison.OrdinalIgnoreCase));
        }

        throw new JgsRuntimeException(line, col, "MATLAB:unrecognizedStringChoice",
            $"The value of '{name}' is invalid. Expected input to match one of these values:\n\n{listed}\n\nThe input, '{text}', did not match any of the valid values.");
    }
}
