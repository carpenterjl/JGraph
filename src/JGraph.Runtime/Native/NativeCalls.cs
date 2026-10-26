using System.Runtime.InteropServices;
using System.Text;
using JGraph.Deploy;
using JGraph.Scripting.Jgs;

namespace JGraph.Runtime.Native;

/// <summary>
/// What jgrt.dll's mcl functions do in the engine (compiler and SDK plan, stage K10), as
/// record-k10.ps1 measured R2025b's runtime doing it for a C program (recordings k10_app and
/// k10_feval): starting and ending the runtime, opening a component from an archive, calling its
/// functions with mxArrays in and out, the last error's message and stack, and the properties of
/// an object a call returned.
/// </summary>
/// <remarks>
/// Nothing thrown may leave an entry point into C, so each one ends what fails in an answer C can
/// read: 0, NULL, or false with the reason sent to the instance's error handler. Where R2025b crashes
/// on what a client did wrong (a NULL input, a negative output count, a property the object does not
/// have) the call is refused instead, a divergence (ADR 0266).
/// </remarks>
internal static unsafe class NativeCalls
{
    private const int LastMessage = 0;

    private static readonly object Gate = new();
    private static string _lastMessage = string.Empty;
    private static string _lastStack = string.Empty;
    private static string? _logFile;

    /// <summary>
    /// mclInitializeApplication: the runtime started with the options as a C program gives them,
    /// each alone and -logfile's file as the next one. R2025b takes an option it does not know, and
    /// -startmsg and -completemsg, without a word (k10_app unknown, startmsg); so does this. Answers
    /// 0 when the runtime is already running.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static int InitializeApplication(char** options, int count)
    {
        try
        {
            RuntimeOptions chosen = RuntimeOptions.None;
            for (int i = 0; i < count; i++)
            {
                string option = options[i] == null ? string.Empty : new string(options[i]);
                chosen = option switch
                {
                    "-nojvm" => chosen with { NoJvm = true },
                    "-nodisplay" => chosen with { NoDisplay = true },
                    "-singleCompThread" => chosen with { SingleCompThread = true },
                    "-logfile" when i + 1 < count && options[i + 1] != null => chosen with { LogFile = new string(options[++i]) },
                    _ => chosen,
                };
            }

            if (!JgRuntime.Initialize(chosen))
            {
                return 0;
            }

            NoteLogFile();
            return 1;
        }
        catch (Exception failure)
        {
            // The boundary with C: a runtime that cannot start is a false answer, said on standard error.
            NativeArrays.CallHandler(0, 2, failure.Message + "\n");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    internal static int TerminateApplication()
    {
        JgRuntime.Terminate();
        return 1;
    }

    /// <summary>
    /// mclGetLogFileName: the log file's full name, empty when there is none. R2025b answers it with
    /// a '&gt;' in front (k10_app logfile), which no file name has; JGraph answers the name (ADR 0266).
    /// </summary>
    [UnmanagedCallersOnly]
    internal static int LogFileName(char* buffer, int capacity) => Answer(_logFile ?? string.Empty, buffer, capacity);

    /// <summary>mclInitializeComponentInstance...: an instance of the component the archive holds, or 0 with the reason told.</summary>
    [UnmanagedCallersOnly]
    internal static nint OpenInstance(char* archive, nint errorHandler, nint printHandler)
    {
        try
        {
            Component component = Component.Open(new string(archive));
            NoteLogFile();
            return GCHandle.ToIntPtr(GCHandle.Alloc(new NativeInstance(component, errorHandler, printHandler)));
        }
        catch (Exception failure)
        {
            // The boundary with C: an archive that cannot be opened is a false answer, said to the error handler.
            NativeArrays.CallHandler(errorHandler, 2, failure.Message + "\n");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    internal static int CloseInstance(nint instance)
    {
        if (instance == 0)
        {
            return 0;
        }

        GCHandle handle = GCHandle.FromIntPtr(instance);
        (handle.Target as NativeInstance)?.Component.Dispose();
        handle.Free();
        return 1;
    }

    /// <summary>
    /// mclFeval: the call, the inputs read from their mxArrays and each output made into a new one.
    /// An instance whose component has ended answers 0 and says nothing, as R2025b's does after its
    /// library terminated (k10_feval risky 2). With no outputs asked for, none is made, whatever
    /// the function leaves in ans (two.nlhs0).
    /// </summary>
    [UnmanagedCallersOnly]
    internal static int Feval(nint instance, char* name, int nlhs, nint* plhs, int nrhs, nint* prhs)
    {
        if (instance == 0 || GCHandle.FromIntPtr(instance).Target is not NativeInstance call || call.Component.IsDisposed)
        {
            return 0;
        }

        string function = new(name);
        try
        {
            if (nlhs < 0)
            {
                // R2025b reports a C++ "vector too long" here (k10_feval risky 5).
                return Refuse(call, function, $"The number of outputs asked for, {nlhs}, cannot be negative.");
            }

            for (int i = 0; i < nrhs; i++)
            {
                if (prhs[i] == 0)
                {
                    // R2025b's runtime crashes on a NULL input (k10_feval risky 1).
                    return Refuse(call, function, $"Input argument {i + 1} is NULL; pass an mxArray, an empty one for [].");
                }
            }

            if (call.Component.Functions.Count > 0 && !call.Component.Functions.Contains(function, StringComparer.Ordinal))
            {
                return Refuse(call, function, $"Unrecognized function or variable '{function}'.");
            }

            var inputs = new RuntimeValue[Math.Max(nrhs, 0)];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = NativeArrays.Read(prhs[i]);
            }

            RuntimeValue[] outputs = call.Component.RunOnEngine(session =>
            {
                JgsValue[] arguments = [.. inputs.Select(v => RuntimeValueConversion.ToEngine(v, session))];
                JgsValue[] results = session.CallEngine(function, nlhs, arguments, call.Output, call.Error, CancellationToken.None, rawErrors: true);
                return results.Take(nlhs).Select(o => RuntimeValueConversion.FromEngine(o, session)).ToArray();
            }).GetAwaiter().GetResult();

            for (int k = 0; k < outputs.Length; k++)
            {
                plhs[k] = NativeArrays.Make(outputs[k]);
            }

            return 1;
        }
        catch (DeployedException failure)
        {
            Remember(failure);
            string report = failure.InnerException is JgsException raised ? JgsDeployedMain.Report(raised, handler: true) : failure.Message + "\n\n";
            NativeArrays.CallHandler(call.ErrorHandler, 2, report);
            return 0;
        }
        catch (Exception failure)
        {
            // The boundary with C: whatever failed is the call's error, never the process's end.
            return Refuse(call, function, failure.Message);
        }
    }

    /// <summary>mclGetLastErrorMessage (0) and mclGetStackTrace (1): the last failed call's message, or its frames one per line.</summary>
    [UnmanagedCallersOnly]
    internal static int LastError(int what, char* buffer, int capacity)
    {
        lock (Gate)
        {
            return Answer(what == LastMessage ? _lastMessage : _lastStack, buffer, capacity);
        }
    }

    [UnmanagedCallersOnly]
    internal static void ReleaseValue(nint value)
    {
        if (value != 0)
        {
            GCHandle.FromIntPtr(value).Free();
        }
    }

    /// <summary>mxDuplicateArray of a reference: another handle on the same value, which a value object's later mxSetProperty then leaves alone.</summary>
    [UnmanagedCallersOnly]
    internal static nint CopyValue(nint value) =>
        value == 0 ? 0 : GCHandle.ToIntPtr(GCHandle.Alloc(GCHandle.FromIntPtr(value).Target));

    /// <summary>
    /// mxGetProperty: the property of element <paramref name="index"/> of the object a reference
    /// holds, or NULL when it has none of that name or will not show it. R2025b's runtime crashes on
    /// either (k10_feval risky 9, 10).
    /// </summary>
    [UnmanagedCallersOnly]
    internal static nint GetProperty(nint value, ulong index, char* name)
    {
        if (Held(value) is not { } held)
        {
            return 0;
        }

        string property = new(name);
        try
        {
            RuntimeValue answer = JgRuntime.RunIn(held.Owner, () =>
            {
                JgsValue got = held.Owner.Interpreter.ReadProperty(Element(held.Value, index), property, 0, 0);
                return RuntimeValueConversion.FromEngine(got, held.Owner);
            });
            return NativeArrays.Make(answer);
        }
        catch (Exception)
        {
            // The boundary with C: no such property, or none it may read, is NULL.
            return 0;
        }
    }

    /// <summary>
    /// mxSetProperty: a handle object's property set on the object, which every reference to it
    /// sees; a value object's set on a copy, which becomes this reference's alone. Answers the
    /// reference's value after (the same handle for a handle object), or 0 when nothing was set.
    /// R2025b's sets a value object's copy in every duplicate of it too (k10_feval value.dup.set.original).
    /// </summary>
    [UnmanagedCallersOnly]
    internal static nint SetProperty(nint value, ulong index, char* name, nint property)
    {
        if (Held(value) is not { } held)
        {
            return 0;
        }

        string field = new(name);
        try
        {
            RuntimeValue given = NativeArrays.Read(property);
            JgsValue changed = JgRuntime.RunIn(held.Owner, () =>
            {
                JgsValue target = held.Value;
                bool copied = target.Type == JgsType.Object && !target.ObjectClass.IsHandle;
                if (copied)
                {
                    target = held.Owner.Interpreter.CopyForBinding(target);
                }

                held.Owner.Interpreter.WriteProperty(Element(target, index), field, RuntimeValueConversion.ToEngine(given, held.Owner), 0, 0);
                return copied ? target : held.Value;
            });
            if (ReferenceEquals(changed, held.Value))
            {
                return value;
            }

            RuntimeValue replaced = RuntimeValueConversion.FromEngine(changed, held.Owner);
            return GCHandle.ToIntPtr(GCHandle.Alloc(replaced));
        }
        catch (Exception)
        {
            // The boundary with C: a property that cannot be set leaves the object as it was.
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    internal static void WaitForFigures(nint instance)
    {
        if (instance != 0 && GCHandle.FromIntPtr(instance).Target is NativeInstance call && !call.Component.IsDisposed)
        {
            try
            {
                call.Component.WaitForFiguresToClose();
            }
            catch (InvalidOperationException)
            {
                // On the windows' own thread there is nothing to wait for that could ever close.
            }
        }
    }

    private static int Refuse(NativeInstance call, string function, string message)
    {
        lock (Gate)
        {
            _lastMessage = message;
            _lastStack = string.Empty;
        }

        NativeArrays.CallHandler(call.ErrorHandler, 2, $"Error using {function}\n{message}\n\n");
        return 0;
    }

    /// <summary>
    /// Keeps a failed call's error for mclGetLastErrorMessage and mclGetStackTrace, in R2025b's
    /// words (k10_feval err.*): the message with its causes', then <c>Error in =&gt; file.m at line n</c>
    /// for the outermost frame; and a line per frame, <c>file F, name N, line L.</c>.
    /// </summary>
    private static void Remember(DeployedException failure)
    {
        IReadOnlyList<(string Name, string File, int Line)> frames = (failure.InnerException as JgsRuntimeException)?.Frames ?? [];
        var message = new StringBuilder(failure.Message);
        foreach (string cause in failure.Causes)
        {
            message.Append('\n').Append(cause);
        }

        if (frames.Count > 0 && Path.GetFileName(frames[^1].File) is { Length: > 0 } file)
        {
            message.Append("\nError in => ").Append(file).Append(" at line ").Append(frames[^1].Line);
        }

        lock (Gate)
        {
            _lastMessage = message.ToString();
            _lastStack = string.Join("\n", frames.Select(static f => $"file {f.File}, name {f.Name}, line {f.Line}."));
        }
    }

    private static void NoteLogFile()
    {
        if (JgRuntime.Options.LogFile is { Length: > 0 } log)
        {
            _logFile ??= Path.GetFullPath(log);
        }
    }

    private static int Answer(string text, char* buffer, int capacity)
    {
        if (buffer == null || capacity <= 0)
        {
            return text.Length;
        }

        int n = Math.Min(text.Length, capacity);
        text.AsSpan(0, n).CopyTo(new Span<char>(buffer, capacity));
        return n;
    }

    /// <summary>The object an mxArray reference holds, with the session that made it.</summary>
    private static (JgsValue Value, ComponentSession Owner)? Held(nint value) =>
        value != 0 && GCHandle.FromIntPtr(value).Target is RuntimeOpaque { Owner: ComponentSession owner, Held: JgsValue held }
            ? (held, owner)
            : null;

    private static JgsValue Element(JgsValue value, ulong index)
    {
        if (index == 0 && value.Type == JgsType.Object)
        {
            return value;
        }

        return value.Type is JgsType.Array && index < (ulong)value.ArrayLength
            ? value.ElementAt((int)index)
            : index == 0 ? value : throw new ArgumentException($"The object has no element {index + 1}.");
    }
}

/// <summary>A component instance a C program holds: its component, and where the instance's text goes.</summary>
internal sealed class NativeInstance(Component component, nint errorHandler, nint printHandler)
{
    public Component Component { get; } = component;

    /// <summary>A Python package's instance (stage K13): datetimes, durations and dictionaries cross its calls as values.</summary>
    public bool Python { get; init; }

    public nint ErrorHandler { get; } = errorHandler;

    /// <summary>Standard output, as R2025b hands it to the print handler: a piece per write (k10_feval print).</summary>
    public TextWriter Output { get; } = new HandlerWriter(printHandler, 1);

    /// <summary>Standard error, to the error handler (k10_feval: fprintf(2, ...)).</summary>
    public TextWriter Error { get; } = new HandlerWriter(errorHandler, 2);

    private sealed class HandlerWriter(nint handler, int stream) : TextWriter
    {
        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(char value) => NativeArrays.CallHandler(handler, stream, value.ToString());

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                NativeArrays.CallHandler(handler, stream, value);
            }
        }
    }
}
