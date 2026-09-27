using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using JGraph.NativeHost;
using JGraph.Scripting.Jgs.Native;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>jgraph.internal.nativehost</c>: the test-only door to the native host (interop plan, stage 7,
/// ADR 0180), which has no MATLAB surface until <c>loadlibrary</c> and <c>calllib</c> arrive in stages
/// 8 and 9. It is undocumented and unsupported, and exists so the JGraph-only fixtures can crash,
/// cancel and inspect the host from a script:
/// <list type="bullet">
/// <item><c>nativehost('call', lib, 'int32 jg_int32(int32)', 5)</c> loads <c>lib</c> by the library
/// search, calls the export with the signature written C-style in MATLAB's type names (<c>int8</c> …
/// <c>uint64</c>, <c>single</c>, <c>double</c>, <c>voidPtr</c>, <c>cstring</c>, <c>void</c>), and
/// answers the return as <c>calllib</c> will: numbers as double, a <c>cstring</c> as char;</item>
/// <item><c>nativehost('pid')</c>: the live host's process id, or 0;</item>
/// <item><c>nativehost('find', lib)</c>: the file the search finds, or the name for the system search;</item>
/// <item><c>nativehost('stop')</c>: ends the host.</item>
/// </list>
/// </summary>
internal static partial class JgsBuiltins
{
    private const string NativeHostName = "jgraph.internal.nativehost";

    /// <summary>The function behind <c>jgraph.internal.nativehost</c>.</summary>
    private static JgsValue NativeHostFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction(NativeHostName, (args, line, col) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:NotSupported", "Native libraries are called on Windows only.");
            }

            return NativeHostEntry(interpreter, args, line, col);
        }));

    [SupportedOSPlatform("windows")]
    private static JgsValue NativeHostEntry(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JGraphScriptGlobals host = interpreter.Host
            ?? throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:NotSupported", "This session has no native host.");
        NativeSession session = host.Native;
        string op = args.Count > 0 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
        switch (op)
        {
            case "pid":
                return JgsValue.Number(session.Current?.ProcessId ?? 0);
            case "stop":
                session.Stop();
                return JgsValue.Null;
            case "find" when args.Count == 2 && IsTextScalar(args[1]):
                return JgsValue.Str(FindLibrary(interpreter, host, TextOf(args[1])) ?? TextOf(args[1]));
            case "call" when args.Count >= 3 && IsTextScalar(args[1]) && IsTextScalar(args[2]):
                return NativeCall(interpreter, host, session, TextOf(args[1]), TextOf(args[2]), args.Skip(3).ToArray(), line, col);
            default:
                throw new JgsRuntimeException(line, col, "JGraph:nativehost:Arguments",
                    $"{NativeHostName} takes 'call', lib, signature and arguments; 'pid'; 'find', lib; or 'stop'.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? FindLibrary(Interpreter interpreter, JGraphScriptGlobals host, string name) =>
        NativeSession.FindLibrary(name, host.CurrentDirectory, interpreter.FunctionPath?.Folders ?? (IEnumerable<string>)[]);

    [SupportedOSPlatform("windows")]
    private static JgsValue NativeCall(
        Interpreter interpreter, JGraphScriptGlobals host, NativeSession session, string library, string signature,
        IReadOnlyList<JgsValue> arguments, int line, int col)
    {
        (string returns, string function, string[] parameters) = ParseNativeSignature(signature, line, col);
        if (parameters.Length != arguments.Count)
        {
            throw new JgsRuntimeException(line, col, "JGraph:nativehost:Arguments",
                $"{function} takes {parameters.Length} arguments; {arguments.Count} were given.");
        }

        string during = $"calllib('{library}', '{function}')";
        string path = FindLibrary(interpreter, host, library) ?? library;
        NativeHostProcess process;
        try
        {
            process = session.Host;
        }
        catch (NativeHostFailure failure)
        {
            throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:HostUnavailable", failure.Message);
        }

        var temporaries = new List<long>();
        try
        {
            if (!session.Modules.TryGetValue(path, out long module))
            {
                try
                {
                    module = process.Load(path);
                }
                catch (NativeHostFailure failure)
                {
                    // R2025b's sentence and the loader's own text, %1 filled as Windows fills it
                    // (probe_shrlib_search).
                    throw new JgsRuntimeException(line, col, "MATLAB:loadlibrary:LoadFailed",
                        $"There was an error loading the library \"{path}\"\n{failure.Message.Replace("%1", path, StringComparison.Ordinal)}");
                }

                session.Modules[path] = module;
            }

            if (!session.Symbols.TryGetValue((module, function), out long address))
            {
                try
                {
                    address = process.Symbol(module, function);
                }
                catch (NativeHostFailure)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:calllib:MethodNotFound", "Method was not found.");
                }

                session.Symbols[(module, function)] = address;
            }

            var slots = new Slot[parameters.Length];
            byte[] bytes = new byte[parameters.Length * 8];
            for (int i = 0; i < parameters.Length; i++)
            {
                slots[i] = NativeSlot(parameters[i], line, col);
                Span<byte> cell = bytes.AsSpan(i * 8, 8);
                JgsValue value = arguments[i];
                if (parameters[i] == "cstring")
                {
                    byte[] text = Encoding.UTF8.GetBytes((IsTextScalar(value) ? TextOf(value) : "") + "\0");
                    long buffer = process.Alloc(text.Length);
                    temporaries.Add(buffer);
                    process.Write(buffer, text);
                    BinaryPrimitives.WriteInt64LittleEndian(cell, buffer);
                    continue;
                }

                double number = NumOf(function, value, line, col);
                switch (slots[i].Kind)
                {
                    case SlotKind.Single:
                        BinaryPrimitives.WriteSingleLittleEndian(cell, (float)number);
                        break;
                    case SlotKind.Double:
                        BinaryPrimitives.WriteDoubleLittleEndian(cell, number);
                        break;
                    case SlotKind.UInt64:
                        BinaryPrimitives.WriteUInt64LittleEndian(cell, (ulong)number);
                        break;
                    default:
                        BinaryPrimitives.WriteInt64LittleEndian(cell, (long)number);
                        break;
                }
            }

            Slot result = returns == "void" ? new Slot(SlotKind.Void) : NativeSlot(returns, line, col);
            process.Sync(host.CurrentDirectory);
            byte[] answer = process.Call(address, result, slots, bytes, interpreter.Cancellation);
            return returns switch
            {
                "void" => JgsValue.Null,
                "cstring" => JgsValue.Str(ReadNativeString(process, BinaryPrimitives.ReadInt64LittleEndian(answer))),
                _ => JgsValue.Number(result.Kind switch
                {
                    SlotKind.Int8 or SlotKind.Int16 or SlotKind.Int32 or SlotKind.Int64 or SlotKind.Pointer
                        => BinaryPrimitives.ReadInt64LittleEndian(answer),
                    SlotKind.UInt8 or SlotKind.UInt16 or SlotKind.UInt32 or SlotKind.UInt64
                        => BinaryPrimitives.ReadUInt64LittleEndian(answer),
                    SlotKind.Single => BinaryPrimitives.ReadSingleLittleEndian(answer),
                    _ => BinaryPrimitives.ReadDoubleLittleEndian(answer),
                }),
            };
        }
        catch (NativeHostExitedException exited)
        {
            temporaries.Clear(); // they died with the host
            session.Modules.Clear();
            session.Symbols.Clear();
            string sentence = exited.Sentence(during);
            if (exited.Cancelled)
            {
                host.WriteErr(sentence + "\n");
                throw new OperationCanceledException(sentence);
            }

            throw new JgsRuntimeException(line, col, "JGraph:loadlibrary:HostExited", sentence);
        }
        finally
        {
            if (process.IsAlive)
            {
                foreach (long buffer in temporaries)
                {
                    process.Release(buffer);
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ReadNativeString(NativeHostProcess process, long address)
    {
        if (address == 0)
        {
            return "";
        }

        long length = process.StringLength(address);
        return Encoding.UTF8.GetString(process.Read(address, checked((int)length)));
    }

    /// <summary>Splits <c>int32 jg_int32(int32, double)</c> into its return type, name and parameter types.</summary>
    private static (string Returns, string Function, string[] Parameters) ParseNativeSignature(string signature, int line, int col)
    {
        int open = signature.IndexOf('(');
        int close = signature.LastIndexOf(')');
        string[] head = open > 0 ? signature[..open].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        if (head.Length != 2 || close < open)
        {
            throw new JgsRuntimeException(line, col, "JGraph:nativehost:Arguments",
                $"'{signature}' is not a signature like 'int32 jg_int32(int32)'.");
        }

        string[] parameters = signature[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parameters is ["void"])
        {
            parameters = [];
        }

        return (head[0], head[1], parameters);
    }

    private static Slot NativeSlot(string type, int line, int col) => type switch
    {
        "int8" => new Slot(SlotKind.Int8),
        "uint8" => new Slot(SlotKind.UInt8),
        "int16" => new Slot(SlotKind.Int16),
        "uint16" => new Slot(SlotKind.UInt16),
        "int32" => new Slot(SlotKind.Int32),
        "uint32" => new Slot(SlotKind.UInt32),
        "int64" => new Slot(SlotKind.Int64),
        "uint64" => new Slot(SlotKind.UInt64),
        "single" => new Slot(SlotKind.Single),
        "double" => new Slot(SlotKind.Double),
        "voidPtr" or "cstring" => new Slot(SlotKind.Pointer),
        _ => throw new JgsRuntimeException(line, col, "JGraph:nativehost:Arguments", $"'{type}' is not a type the native host door knows."),
    };
}
