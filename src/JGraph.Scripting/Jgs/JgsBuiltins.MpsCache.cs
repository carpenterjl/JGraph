using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB Production Server's in-MATLAB helpers (compiler and SDK plan, stage K15):
/// <c>prodserver.metrics.incrementCounter</c> and <c>setGauge</c>, which print the metric event the
/// server collects (<c>[metric:counter] archive|name value</c>, the archive being ctfroot's folder);
/// <c>mps.cache.control</c>, <c>connect</c> and <c>configure</c>, <c>mps.cache.Connection</c>'s static
/// methods, <c>mps.cache.Location</c> and <c>mps.cache.TriStateBoolean</c>; and <c>mps.sync.mutex</c>.
/// Their checks are R2025b's M-files' (toolbox/mps/metrics and toolbox/mps/persistence), measured by
/// the fixture <c>k15_mps_cache</c>; the objects are <see cref="MpsCacheObject"/>, <see cref="MpsControllerObject"/>,
/// <see cref="MpsConnectionObject"/> and <see cref="MpsMutexObject"/>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterMpsCacheBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> MpsCacheBuiltinNames { get; } =
    [
        "prodserver.metrics.incrementCounter", "prodserver.metrics.setGauge",
        "mps.cache.control", "mps.cache.connect", "mps.cache.configure", "mps.sync.mutex",
        "mps.cache.Connection.list", "mps.cache.Connection.exist", "mps.cache.Connection.clear", "mps.cache.Connection.release",
        "mps.cache.Connection.disconnect", "mps.cache.Connection.reset",
        "mps.cache.Location.Local", "mps.cache.Location.Remote",
        "mps.cache.TriStateBoolean.True", "mps.cache.TriStateBoolean.False", "mps.cache.TriStateBoolean.Unknown",
    ];

    private static readonly string[] MetricValueTypes = ["double", "single", "uint8", "uint16", "uint32", "uint64", "int8", "int16", "int32", "int64"];

    internal static void RegisterMpsCacheBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Statement(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true, BindsAnsAsStatement = false }));

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

        void Bare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true, AutoCallsBare = true }));

        // --- metrics ------------------------------------------------------------------------------------
        void Metric(string name, string type, bool positive)
        {
            JgsValue[] Body(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
            {
                if (wanted > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                if (args.Count > 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                if (args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                string metric = MetricName(args[0], line, col);
                double value = MetricValue(args[1], positive, line, col);
                string archive = Path.GetFileName(Path.TrimEndingDirectorySeparator(host.Deployment?.CtfRoot ?? AppContext.BaseDirectory));
                host.WriteOut($"[metric:{type}] {archive}|{metric} {JgsSprintf.FormatMatlab("%g", [JgsValue.Number(value)])}\n");
                return [];
            }

            // The plain delegate is the one-output call (r = f(...)), which R2025b refuses; a statement comes through MultiOutput with none wanted.
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => Body(args, 1, line, col) is [JgsValue first, ..] ? first : JgsValue.Null)
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = false,
                KnowsWhenDiscarded = true,
                MultiOutput = Body,
            }));
        }

        Metric("prodserver.metrics.incrementCounter", "counter", positive: true);
        Metric("prodserver.metrics.setGauge", "gauge", positive: false);

        // --- the cache ---------------------------------------------------------------------------------
        MpsCacheState State() => MpsCacheState.Of(interpreter);
        DeviceSession Devices() => host.Devices;

        Define("mps.cache.control", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            MpsCacheState state = State();
            MpsConnectionObject connection;
            if (args.Count == 1)
            {
                connection = ConnectionNamed(state, args[0], line, col);
            }
            else
            {
                string provider = MpsProviderName(args[1], line, col);
                connection = Configure(state, host, interpreter, args[0], provider, args.Skip(2).ToArray(), line, col);
            }

            return JgsValue.External(new MpsControllerObject(Devices(), interpreter, state, connection));
        });

        Define("mps.cache.configure", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            return JgsValue.External(Configure(State(), host, interpreter, args[0], MpsProviderName(args[1], line, col), args.Skip(2).ToArray(), line, col));
        });

        Define("mps.cache.connect", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            string name = VariableNameArgument(args[0], "name", line, col);
            (string? connectionName, List<(string Name, JgsValue Value)> unmatched) = NameValues(args.Skip(1).ToArray(), ["Connection"], line, col);
            MpsCacheState state = State();
            MpsConnectionObject connection = connectionName is null ? DefaultConnection(state, line, col) : ConnectionNamed(state, JgsValue.Str(connectionName), line, col);
            JgsValue localKeys = JgsValue.Cell([]);
            localKeys.Reshape(0, 0);
            foreach ((string option, JgsValue value) in unmatched)
            {
                if (option.Equals("LocalKeys", StringComparison.Ordinal))
                {
                    localKeys = JgsValue.Share(value);
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:UnrecognizedOption", $"Unrecognized option: \"{option}\".");
                }
            }

            if (connection.ServiceHandle is null)
            {
                if (state.ActiveServices.TryGetValue(connection.Folder, out MatFileService? service) && !service.Stopped)
                {
                    connection.ServiceHandle = service;
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:InactiveService",
                        $"{connection.ProviderName} service on connection \"{connection.ConnectionName}\" not active. Use Controller object to start or attach to service.");
                }
            }

            return JgsValue.External(new MpsCacheObject(Devices(), interpreter, state, name, connection, localKeys));
        });

        Define("mps.sync.mutex", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            string name = VariableNameArgument(args[0], "name", line, col);
            (string? connectionName, List<(string Name, JgsValue Value)> rest) = NameValues(args.Skip(1).ToArray(), ["Connection", "Expiration"], line, col);
            double expiration = 10;
            foreach ((string option, JgsValue value) in rest)
            {
                if (option == "Expiration")
                {
                    if (!IsNumericValue(value) || IsLogicalValue(value) || DeviceChecks.Count(value) != 1)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:expectedScalar", "The value of 'Expiration' is invalid. Expected input to be a scalar.");
                    }

                    expiration = DeviceChecks.Numbers(value).First();
                    if (expiration != Math.Floor(expiration) || double.IsInfinity(expiration))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:expectedInteger", "The value of 'Expiration' is invalid. Expected input to be integer-valued.");
                    }
                }
            }

            MpsCacheState state = State();
            MpsConnectionObject connection = connectionName is null ? DefaultConnection(state, line, col) : ConnectionNamed(state, JgsValue.Str(connectionName), line, col);
            if (connection.ServiceHandle is null)
            {
                if (state.ActiveServices.TryGetValue(connection.Folder, out MatFileService? service) && !service.Stopped)
                {
                    connection.ServiceHandle = service;
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:InactiveService",
                        $"{connection.ProviderName} service on connection \"{connection.ConnectionName}\" not active. Use Controller object to start or attach to service.");
                }
            }

            return JgsValue.External(new MpsMutexObject(Devices(), interpreter, name, connection, expiration));
        });

        // --- mps.cache.Connection's static methods --------------------------------------------------------
        Bare("mps.cache.Connection.list", (args, line, col) =>
        {
            Arity("mps.cache.Connection.list", args, 0, line, col);
            string[] names = [.. State().Connections.Keys.Order(StringComparer.Ordinal)];
            if (names.Length == 0)
            {
                JgsValue none = JgsValue.Cell([]);
                none.Reshape(0, 0);
                return none;
            }

            JgsValue cell = JgsValue.Cell([.. names.Select(JgsValue.Str)]);
            cell.Reshape(1, names.Length);
            return cell;
        });

        Define("mps.cache.Connection.exist", (args, line, col) =>
        {
            Arity("mps.cache.Connection.exist", args, 1, line, col);
            return JgsValue.Bool(IsTextScalar(args[0]) && State().Connections.ContainsKey(TextOf(args[0])));
        });

        Statement("mps.cache.Connection.clear", (args, line, col) =>
        {
            ArityRange("mps.cache.Connection.clear", args, 0, 1, line, col);
            MpsCacheState state = State();
            if (args.Count == 0)
            {
                foreach (MpsConnectionObject connection in state.Connections.Values)
                {
                    connection.ServiceHandle = null;
                }

                state.Connections.Clear();
            }
            else if (IsTextScalar(args[0]) && state.Connections.Remove(TextOf(args[0]), out MpsConnectionObject? removed))
            {
                removed.ServiceHandle = null;
            }

            return JgsValue.Null;
        });

        foreach (string verb in new[] { "release", "disconnect", "reset" })
        {
            Statement("mps.cache.Connection." + verb, (args, line, col) =>
            {
                Arity("mps.cache.Connection." + verb, args, 1, line, col);
                MpsCacheState state = State();
                if (IsTextScalar(args[0]) && state.Connections.TryGetValue(TextOf(args[0]), out MpsConnectionObject? connection))
                {
                    if (verb == "reset")
                    {
                        connection.ServiceHandle = state.ActiveServices.TryGetValue(connection.Folder, out MatFileService? service) && !service.Stopped ? service : null;
                    }
                    else
                    {
                        connection.ServiceHandle = null;
                    }
                }

                return JgsValue.Null;
            });
        }

        // --- the enumerations ------------------------------------------------------------------------------
        foreach (string member in new[] { "Local", "Remote" })
        {
            Bare("mps.cache.Location." + member, (args, line, col) =>
            {
                Arity("mps.cache.Location." + member, args, 0, line, col);
                return MpsEnumValue.Of(Devices(), interpreter, MpsEnumValue.Location, member);
            });
        }

        foreach (string member in new[] { "True", "False", "Unknown" })
        {
            Bare("mps.cache.TriStateBoolean." + member, (args, line, col) =>
            {
                Arity("mps.cache.TriStateBoolean." + member, args, 0, line, col);
                return MpsEnumValue.Of(Devices(), interpreter, MpsEnumValue.TriStateBoolean, member);
            });
        }
    }

    /// <summary>The metric name: mustBeNonzeroLengthText, mustBeTextScalar, then isvarname, in R2025b's words (k15_mps_cache: m_badname and its kin).</summary>
    private static string MetricName(JgsValue value, int line, int col)
    {
        bool textual = value.Type == JgsType.String || value.IsStringArray || (value.Type == JgsType.Cell && value.AsCell.All(static e => e.Type == JgsType.String));
        if (!textual)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                "The value of 'name' is invalid. Value must be a character vector, string array, or cell array of character vectors.");
        }

        bool anyText = value.Type == JgsType.String ? value.AsString.Length > 0
            : value.IsStringArray ? Enumerable.Range(0, value.ArrayLength).Any(i => value.ElementAt(i).AsString.Length > 0)
            : value.AsCell.Any(static e => e.AsString.Length > 0);
        if (!anyText)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText", "The value of 'name' is invalid. Value must be text with one or more characters.");
        }

        if (!IsTextScalar(value) || value.IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar", "The value of 'name' is invalid. Value must be a character vector or string scalar.");
        }

        string name = TextOf(value);
        if (!IsValidVariableName(name))
        {
            throw new JgsRuntimeException(line, col, "MPS:MATLAB:Metrics:IncorrectNameFormat",
                "The value of 'name' is invalid. Incorrect metric name format. Metric name must be a valid MATLAB variable name. Prometheus naming convention is lower-case letters and underscores.");
        }

        return name;
    }

    /// <summary>The metric value: validateattributes numeric, scalar, nonempty; mustBeReal; for a counter mustBePositive.</summary>
    private static double MetricValue(JgsValue value, bool positive, int line, int col)
    {
        if (!IsNumericValue(value) || IsLogicalValue(value))
        {
            string className = ClassOf(value, JgsDialect.Matlab);
            string instead = className == "char" ? "" : $"\n\nInstead its type was {className}.";
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                $"The value of 'value' is invalid. Expected input to be one of these types:\n\n{string.Join(", ", MetricValueTypes)}{instead}");
        }

        if (DeviceChecks.Count(value) != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedScalar", "The value of 'value' is invalid. Expected input to be a scalar.");
        }

        if (HasComplexElements(value) || value.Type == JgsType.Complex)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeReal", "The value of 'value' is invalid. Value must be real.");
        }

        double number = DeviceChecks.Numbers(value).First();
        if (positive && !(number > 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBePositive", "The value of 'value' is invalid. Value must be positive.");
        }

        return number;
    }

    /// <summary>A required argument validated by <c>@(p)isvarname(p)</c>, in inputParser's words.</summary>
    private static string VariableNameArgument(JgsValue value, string argument, int line, int col)
    {
        if (!IsTextScalar(value) || value.IsCharMatrix || !IsValidVariableName(TextOf(value)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ArgumentFailedValidation",
                $"The value of '{argument}' is invalid. It must satisfy the function: @(p)isvarname(p).");
        }

        return TextOf(value);
    }

    /// <summary>A required argument validated by <c>validateattributes(p, {'string','char'}, {'nonempty'})</c>.</summary>
    private static string NonEmptyTextArgument(JgsValue value, string argument, int line, int col)
    {
        if (!(value.Type == JgsType.String || value.IsStringArray))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:invalidType",
                $"The value of '{argument}' is invalid. Expected input to be one of these types:\n\nstring, char\n\nInstead its type was {ClassOf(value, JgsDialect.Matlab)}.");
        }

        if (value.Type == JgsType.String ? value.AsString.Length == 0 && !value.IsCharMatrix : value.ArrayLength == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:expectedNonempty", $"The value of '{argument}' is invalid. Expected input to be nonempty.");
        }

        return IsTextScalar(value) ? TextOf(value) : TextOf(value.ElementAt(0));
    }

    private static string MpsProviderName(JgsValue value, int line, int col)
    {
        string provider = NonEmptyTextArgument(value, "provider", line, col);
        if (provider is not ("MatlabTest" or "Redis" or "MongoDB"))
        {
            throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:UnknownProvider", $"Unknown provider \"{provider}\".");
        }

        return provider;
    }

    /// <summary>
    /// inputParser's name-value reading: a name matched in any case or by a unique start among
    /// <paramref name="known"/>; a name that is none of them is answered among the unmatched (as
    /// KeepUnmatched keeps it), so the caller refuses it in its own words.
    /// </summary>
    private static (string? Connection, List<(string Name, JgsValue Value)> Others) NameValues(JgsValue[] args, string[] known, int line, int col)
    {
        string? connection = null;
        var rest = new List<(string, JgsValue)>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!IsTextScalar(args[i]) || args[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string given = TextOf(args[i]);
            if (i + 1 >= args.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{given}'. Name-value pair arguments require a name followed by a value.");
            }

            string[] matches = [.. known.Where(k => k.StartsWith(given, StringComparison.OrdinalIgnoreCase))];
            string? name = matches.FirstOrDefault(m => m.Equals(given, StringComparison.OrdinalIgnoreCase)) ?? (matches.Length == 1 ? matches[0] : null);
            if (name == "Connection")
            {
                connection = NonEmptyTextArgument(args[i + 1], "Connection", line, col);
            }
            else
            {
                rest.Add((name ?? given, args[i + 1]));
            }
        }

        return (connection, rest);
    }

    private static MpsConnectionObject ConnectionNamed(MpsCacheState state, JgsValue name, int line, int col)
    {
        string text = NonEmptyTextArgument(name, "name", line, col);
        return state.Connections.TryGetValue(text, out MpsConnectionObject? connection)
            ? connection
            : throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:UnknownServiceConnection", $"Unknown service connection \"{text}\".");
    }

    private static MpsConnectionObject DefaultConnection(MpsCacheState state, int line, int col) =>
        state.Connections.Count == 1
            ? state.Connections.Values.First()
            : throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:AmbiguousDefaultConnection",
                $"Cannot determine the default connection: {state.Connections.Count} connections are known. Name one with 'Connection'.");

    /// <summary>mps.cache.configure: a FolderConnection for the MatlabTest provider (Redis and MongoDB have no control script here).</summary>
    private static MpsConnectionObject Configure(MpsCacheState state, JGraphScriptGlobals host, Interpreter interpreter, JgsValue nameValue, string provider, JgsValue[] options, int line, int col)
    {
        string name = NonEmptyTextArgument(nameValue, "name", line, col);
        string? folder = null;
        string operations = "read | write | create | update";
        for (int i = 0; i < options.Length; i += 2)
        {
            if (!IsTextScalar(options[i]) || options[i].IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string option = TextOf(options[i]);
            if (i + 1 >= options.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{option}'. Name-value pair arguments require a name followed by a value.");
            }

            if (option.Equals("Folder", StringComparison.OrdinalIgnoreCase))
            {
                folder = NonEmptyTextArgument(options[i + 1], "Folder", line, col);
            }
            else if (option.Equals("Operations", StringComparison.OrdinalIgnoreCase))
            {
                operations = NonEmptyTextArgument(options[i + 1], "Operations", line, col);
            }
            else if (option.Equals("Host", StringComparison.OrdinalIgnoreCase) || option.Equals("Port", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:NoControlScript",
                    $"The {provider} persistence provider's control script is not part of JGraph: only the MatlabTest (MAT-file) provider is (ADR 0271).");
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{option}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }
        }

        if (provider != "MatlabTest")
        {
            throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:NoControlScript",
                $"The {provider} persistence provider's control script is not part of JGraph: only the MatlabTest (MAT-file) provider is (ADR 0271).");
        }

        if (state.Connections.ContainsKey(name))
        {
            throw new JgsRuntimeException(line, col, "MPS:MATLAB:Cache:DuplicateServiceConnection",
                $"Connection \"{name}\" already exists. Retrieve it by calling mps.cache.control('{name}').");
        }

        folder ??= Path.Combine(Path.GetTempPath(), "tp" + Guid.NewGuid().ToString("N")[..12]);
        var connection = new MpsConnectionObject(host.Devices, interpreter, name, provider, folder, operations);
        state.Connections[name] = connection;
        return connection;
    }
}
