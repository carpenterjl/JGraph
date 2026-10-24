namespace JGraph.Scripting.Jgs;

/// <summary>
/// The secret functions (the compiler and SDK plan's stage K3b, ADR 0259): <c>getSecret</c>,
/// <c>isSecret</c>, <c>getSecretMetadata</c>, <c>setSecret</c>, <c>removeSecret</c>,
/// <c>setSecretMetadata</c> and <c>listSecrets</c>, as R2025b's <c>toolbox/matlab/authnz/secretapis</c>
/// declares their arguments.
/// </summary>
/// <remarks>
/// <para>JGraph has no vault. An ordinary session answers as R2025b's <c>matlab -batch</c> does,
/// where the vault is unavailable (probe_k3b_secrets): the reading functions refuse as
/// <c>MATLAB:authnz:secretapis:VaultNotFound</c>, the writing ones and <c>listSecrets</c> as
/// <c>VaultNotSupported</c>, each after its arguments are checked.</para>
/// <para>A deployed session reads a secret from the environment variable of the same name, as
/// R2025b's runtime does with no embedded secrets (k3b_run, k3b_secrets_run): <c>getSecret</c>
/// answers its value as a string and <c>isSecret</c> whether it is set; a name the environment
/// does not hold is <c>SecretValueNotFound</c>, the name's case not mattering on Windows (measured).
/// <c>listSecrets</c>, <c>setSecret</c>, <c>removeSecret</c> and <c>setSecretMetadata</c> are not
/// in a deployed session at all. Embedding secrets in an archive (<c>mcc -J</c>) is not built:
/// without encryption it would put them in the archive as plain text (decision of 2026-10-24).</para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Declares the secret functions; a deployed session is read at each call.</summary>
    internal static void RegisterSecretBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body, bool command = false) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = !command,
            }));

        env.Builtins.Register("getSecret", JgsValue.Function(new BuiltinFunction("getSecret",
            (args, line, col) => GetSecret(host, args, 1, line, col))
        {
            KeepsStringArguments = true,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => [GetSecret(host, args, wanted, line, col)],
        }));

        Define("isSecret", (args, line, col) =>
        {
            string name = SecretName(args, 1, line, col);
            if (host.Deployment is null)
            {
                throw VaultNotFound("isSecret", line, col);
            }

            return JgsValue.Bool(Environment.GetEnvironmentVariable(name) is not null);
        });

        // A secret the environment holds has no metadata: an empty dictionary, where R2025b's
        // runtime raises MATLAB:authnz:secretapis:InternalError (k3b_secrets_run, a divergence).
        Define("getSecretMetadata", (args, line, col) =>
        {
            string name = SecretName(args, 1, line, col);
            if (host.Deployment is null)
            {
                throw VaultNotFound("getSecretMetadata", line, col);
            }

            return Environment.GetEnvironmentVariable(name) is null
                ? throw SecretValueNotFound(name, line, col)
                : NewKeyed(DictionaryClassName, [], true, line, col);
        });

        Define("setSecret", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Invalid argument list. Function requires 1 more input(s).");
            }

            SecretNameAt(args[0], line, col);
            for (int i = 1; i < args.Count; i += 2)
            {
                string option = IsTextScalar(args[i]) ? TextOf(args[i]) : string.Empty;
                if (!option.Equals("Overwrite", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", $"Invalid argument name '{option}'. Use 'Overwrite'.");
                }
            }

            throw VaultNotSupported("setSecret", line, col);
        }, command: true);

        Define("removeSecret", (args, line, col) =>
        {
            SecretName(args, 1, line, col);
            throw VaultNotSupported("removeSecret", line, col);
        }, command: true);

        Define("setSecretMetadata", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", $"Invalid argument list. Function requires {2 - args.Count} more input(s).");
            }

            SecretNameAt(args[0], line, col);

            // mustBeValidMetadata: a configured dictionary of text keys and cell values.
            JgsValue metadata = args[1];
            string? problem = metadata.ClassName != DictionaryClassName ? null
                : !IsConfigured(metadata) ? "MetadataDictUnconfigured|The input metadata dictionary is unconfigured with unset key and value types. Add entries to the dictionary."
                : metadata.AsStruct["KeyType"].AsString is not ("char" or "string") ? "InvalidMetadataDictKeys|Metadata key in the dictionary must be a string scalar or char vector."
                : metadata.AsStruct["ValueType"].AsString != "cell" ? "InvalidMetadataDictValues|Metadata value in the dictionary must be a cell."
                : string.Empty;
            if (problem is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                    "Invalid argument at position 2. Value must be of type dictionary or be convertible to dictionary.");
            }

            if (problem.Length > 0)
            {
                string[] parts = problem.Split('|');
                throw new JgsRuntimeException(line, col, "MATLAB:authnz:secretapis:" + parts[0], "Invalid argument at position 2. " + parts[1]);
            }

            throw VaultNotSupported("setSecretMetadata", line, col);
        }, command: true);

        Define("listSecrets", (args, line, col) =>
        {
            if (args.Count > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            throw VaultNotSupported("listSecrets", line, col);
        });
    }

    /// <summary>
    /// <c>value = getSecret(name)</c>: in a deployed session the environment's value as a string;
    /// with no output, a row of asterisks as long as the value between blank lines, as R2025b's
    /// <c>getSecret.m</c> prints it.
    /// </summary>
    private static JgsValue GetSecret(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        string name = SecretName(args, 1, line, col);
        if (host.Deployment is null)
        {
            throw VaultNotFound("getSecret", line, col);
        }

        string value = Environment.GetEnvironmentVariable(name) ?? throw SecretValueNotFound(name, line, col);
        if (wanted == 0)
        {
            host.WriteOut("\n" + new string('*', value.Length) + "\n\n");
            return JgsValue.Null;
        }

        return JgsValue.StringScalar(value);
    }

    /// <summary>The one argument of a secret function: a nonempty text scalar, in its validators' words.</summary>
    private static string SecretName(IReadOnlyList<JgsValue> args, int count, int line, int col)
    {
        if (args.Count < count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", $"Invalid argument list. Function requires {count - args.Count} more input(s).");
        }

        if (args.Count > count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        return SecretNameAt(args[0], line, col);
    }

    private static string SecretNameAt(JgsValue value, int line, int col)
    {
        if (!IsTextScalar(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar",
                "Invalid argument at position 1. Value must be a character vector or string scalar.");
        }

        string name = TextOf(value);
        if (name.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                "Invalid argument at position 1. Value must be text with one or more characters.");
        }

        return name;
    }

    private static JgsRuntimeException SecretValueNotFound(string name, int line, int col) =>
        new(line, col, "MATLAB:authnz:secretapis:SecretValueNotFound",
            $"No secret value found for secret name '{name}' in the vault. Use setSecret to set up the secret.");

    private static JgsRuntimeException VaultNotFound(string function, int line, int col) =>
        new(line, col, "MATLAB:authnz:secretapis:VaultNotFound", $"Unable to call '{function}'. The MATLAB Vault is unavailable.");

    private static JgsRuntimeException VaultNotSupported(string function, int line, int col) =>
        new(line, col, "MATLAB:authnz:secretapis:VaultNotSupported",
            $"Unable to call '{function}'. The MATLAB Vault is unavailable in the current environment.");
}
