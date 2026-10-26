using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using JGraph.Deploy;
using JGraph.Scripting.Jgs;

namespace JGraph.Runtime.Native;

/// <summary>
/// What jgrt.dll's C++ entry points (jgrtCpp*, native\jgrt\include\jgraph\jgrt_cpp.h) do in the
/// engine (compiler and SDK plan, stage K12), as record-k12.ps1 measured R2025b's runtime doing it
/// for a C++ client (recordings\k12_client.out.txt, calls): the application started, a library
/// opened from its archive, and calls run on the engine thread after the calls started before them,
/// their text sent where the call asked, each ended through its completion function with the
/// outputs or the error in the binary form (<see cref="NativeWire"/>). A call not yet started can be
/// cancelled; a running one interrupted at its next statement.
/// </summary>
/// <remarks>
/// A library calls its component's functions, its classes' methods on their objects, and the few
/// functions the strongly typed interface calls on an object (subsref, subsasgn, isvalid and the
/// comparisons); any other name is refused before anything runs, in R2025b's words (calls:
/// builtin.sqrt, unknown). Nothing thrown may leave an entry point into C.
/// </remarks>
internal static unsafe class NativeCppCalls
{
    private const int Done = 0;
    private const int Cancelled = 1;
    private const int Interrupted = 2;
    private const int MatlabError = 3;
    private const int Other = 5;
    private const int Stopped = 6;
    private const int Refused = 7;

    private const string CancelledText = "The MATLAB command was cancelled.";
    private const string InterruptedText = "The execution of the MATLAB command was interrupted.";

    /// <summary>What a library calls on an object besides its classes' methods: what a generated interface calls (typed: c.*).</summary>
    private static readonly HashSet<string> ObjectFunctions = new(StringComparer.Ordinal)
    {
        "subsref", "subsasgn", "isvalid", "eq", "ne", "lt", "gt", "le", "ge",
    };

    private static readonly ConcurrentDictionary<ulong, CppTask> Tasks = new();
    private static long _lastTask;

    /// <summary>
    /// jgrtCppStart: the runtime started with the options as MATLAB's C++ API takes them, each
    /// alone and -logfile's file the next; an application started out of process runs in this one
    /// all the same (JGraph's runtime is in process). One already running is joined.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static int Start(int outOfProcess, char** options, int count, char** error)
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

            JgRuntime.Initialize(chosen);
            return 0;
        }
        catch (Exception failure)
        {
            // The boundary with C: a runtime that cannot start is refused, with the reason.
            *error = Copy(failure.Message);
            return 3;
        }
    }

    /// <summary>
    /// jgrtCppOpen: a library over the component the archive holds, in a session of its own as each
    /// of R2025b's libraries is (k12_client lib.two.of.one: an object of one is refused by another);
    /// or 0 with the reason.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static nint Open(char* archive, char** error) => OpenLibrary(archive, error, python: false);

    /// <summary>
    /// jgrtPyOpen (stage K13): a Python package's instance, opened as a library is, whose calls take and
    /// give datetimes, durations and dictionaries as values (<see cref="NativeWire"/>), as R2025b's
    /// Python runtime converts them (k13_client: out.datetime, out.duration, out.dict).
    /// </summary>
    [UnmanagedCallersOnly]
    internal static nint PyOpen(char* archive, char** error) => OpenLibrary(archive, error, python: true);

    private static nint OpenLibrary(char* archive, char** error, bool python)
    {
        try
        {
            Component component = Component.Open(new string(archive), ownSession: true);
            return GCHandle.ToIntPtr(GCHandle.Alloc(new NativeInstance(component, 0, 0) { Python = python }));
        }
        catch (Exception failure)
        {
            // The boundary with C: an archive that cannot be opened is a refusal with its reason.
            *error = Copy(failure.Message);
            return 0;
        }
    }

    /// <summary>
    /// jgrtCppCall: the call queued on the engine thread, its task's handle answered at once; or 0,
    /// with the reason, for a name the library cannot call.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static ulong Call(
        nint library, char* function, nuint nlhs, byte* inputs, nuint length, nint context, nint write, int output, int error, nint done, char** refused)
    {
        try
        {
            if (library == 0 || GCHandle.FromIntPtr(library).Target is not NativeInstance instance || instance.Component.IsDisposed)
            {
                *refused = Copy("The library has been closed.");
                return 0;
            }

            string name = new(function);
            byte[] bytes = length == 0 ? [] : new ReadOnlySpan<byte>(inputs, checked((int)length)).ToArray();
            if (!instance.Component.Functions.Contains(name, StringComparer.Ordinal) && !NativeWire.StartsWithObject(bytes))
            {
                *refused = Copy($"Function {name} not found in the component.");
                return 0;
            }

            ulong id = (ulong)Interlocked.Increment(ref _lastTask);
            var task = new CppTask(name, checked((int)nlhs), bytes, context, write, output != 0, error != 0, done);
            Tasks[id] = task;
            Task<(int Status, byte[] Data)> run;
            try
            {
                run = instance.Component.RunOnEngine(session => Execute(task, session, instance), task.Token);
            }
            catch (ObjectDisposedException)
            {
                run = Task.FromResult((Stopped, NativeWire.Text("The JGraph Runtime has ended.")));
            }

            run.ContinueWith(
                static (t, state) =>
                {
                    var finished = (CppTask)state!;
                    (int status, byte[] data) = t.IsCanceled ? (Cancelled, NativeWire.Text(CancelledText))
                        : t.IsFaulted ? (Other, NativeWire.Text(t.Exception!.InnerException?.Message ?? t.Exception.Message))
                        : t.Result;
                    finished.Complete(status, data);
                },
                task,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return id;
        }
        catch (Exception failure)
        {
            // The boundary with C: a call that cannot be started is a refusal with its reason.
            *refused = Copy(failure.Message);
            return 0;
        }
    }

    /// <summary>
    /// jgrtCppCancel: a task not yet started never runs (1); a running one is interrupted when
    /// allowed (1) and left to finish when not (0), as R2025b's are (calls: async.cancel.*); a
    /// finished one answers 0.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static int Cancel(ulong id, int allowInterrupt) =>
        Tasks.TryGetValue(id, out CppTask? task) && task.Cancel(allowInterrupt != 0) ? 1 : 0;

    [UnmanagedCallersOnly]
    internal static void Forget(ulong id) => Tasks.TryRemove(id, out _);

    /// <summary>
    /// A call on the engine thread: the inputs read, the name checked against the component and
    /// the first input's class, the function run with the call's text going where it asked; an
    /// error ending it written as R2025b writes one (to the call's error text, or standard error:
    /// calls, STDERR and print.error.buffers) and answered as its record. Text the call keeps no
    /// buffer for goes to the process's standard output and error as it is written, as R2025b's does.
    /// </summary>
    private static (int Status, byte[] Data) Execute(CppTask task, ComponentSession session, NativeInstance instance)
    {
        if (!task.TryStart())
        {
            return (Cancelled, NativeWire.Text(CancelledText));
        }

        JgsValue[] arguments;
        try
        {
            arguments = NativeWire.ReadAll(task.Inputs, session, instance.Python);
        }
        catch (ArgumentException failure)
        {
            return (MatlabError, NativeWire.Error(string.Empty, failure.Message, [], null));
        }

        // A plain std::exception in the program, as R2025b's refusal is (calls: obj.counter.isvalid, obj.handle.call).
        if (!Callable(session, task.Function, arguments))
        {
            return (Refused, NativeWire.Text($"Function {task.Function} not found in the component."));
        }

        TextWriter output = task.Output ? new CallbackWriter(task, 1) : instance.Output;
        TextWriter error = task.Error ? new CallbackWriter(task, 2) : instance.Error;
        JgsValue[] results;
        try
        {
            results = session.CallEngine(task.Function, task.Nlhs, arguments, output, error, task.Token, rawErrors: true);
        }
        catch (DeployedException failure)
        {
            var runtime = failure.InnerException as JgsRuntimeException;
            string report = failure.InnerException is JgsException raised ? JgsDeployedMain.Report(raised, handler: true) : failure.Message + "\n\n";
            error.Write(report);

            return (MatlabError, NativeWire.Error(failure.Identifier, failure.Message, runtime?.Frames ?? [], runtime?.Carried as JgsValue));
        }
        catch (OperationCanceledException)
        {
            return (Interrupted, NativeWire.Text(InterruptedText));
        }

        // An output the binary form cannot hold exactly is refused as an input is (calls: make.uint64).
        try
        {
            return (Done, NativeWire.WriteAll(results.Take(task.Nlhs), session, instance.Python));
        }
        catch (ArgumentException failure)
        {
            return (MatlabError, NativeWire.Error(string.Empty, failure.Message, [], null));
        }
    }

    private static bool Callable(ComponentSession session, string name, JgsValue[] arguments)
    {
        if (session.Deployment.Manifest?.PublicFunctions.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)) == true)
        {
            return true;
        }

        if (arguments.Length == 0 || arguments[0].Type != JgsType.Object)
        {
            return false;
        }

        return session.Interpreter.TryUserMethod(name, arguments[0], out _) || ObjectFunctions.Contains(name);
    }

    private static char* Copy(string text) => (char*)Marshal.StringToCoTaskMemUni(text);

    /// <summary>A call on its way: what it calls with, where its text goes, how it is told it ended.</summary>
    private sealed class CppTask(string function, int nlhs, byte[] inputs, nint context, nint write, bool output, bool error, nint done)
    {
        private readonly CancellationTokenSource _cancel = new();
        private readonly object _gate = new();
        private int _state; // 0 queued, 1 running, 2 cancelled before it started, 3 finished

        public string Function { get; } = function;

        public int Nlhs { get; } = nlhs;

        public byte[] Inputs { get; } = inputs;

        public bool Output { get; } = output;

        public bool Error { get; } = error;

        public CancellationToken Token => _cancel.Token;

        public bool TryStart()
        {
            lock (_gate)
            {
                if (_state != 0)
                {
                    return false;
                }

                _state = 1;
                return true;
            }
        }

        public bool Cancel(bool allowInterrupt)
        {
            bool queued;
            lock (_gate)
            {
                if (_state == 0)
                {
                    _state = 2;
                    queued = true;
                }
                else if (_state == 1 && allowInterrupt)
                {
                    queued = false;
                }
                else
                {
                    return false;
                }

                _cancel.Cancel();
            }

            // A call that never started ends now rather than when the engine reaches it, as R2025b's
            // does (k13_client: queued.done answered true at once); the engine passes it over later.
            if (queued)
            {
                Complete(Cancelled, NativeWire.Text(CancelledText));
            }

            return true;
        }

        public void Write(int stream, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            fixed (char* chars = text)
            {
                ((delegate* unmanaged<nint, int, char*, nuint, void>)write)(context, stream, chars, (nuint)text.Length);
            }
        }

        public void Complete(int status, byte[] data)
        {
            lock (_gate)
            {
                if (_state == 3)
                {
                    return;
                }

                _state = 3;
            }

            fixed (byte* bytes = data)
            {
                ((delegate* unmanaged<nint, int, byte*, nuint, void>)done)(context, status, bytes, (nuint)data.Length);
            }

            _cancel.Dispose();
        }
    }

    /// <summary>A call's output or error text, handed to its write function as it is written.</summary>
    private sealed class CallbackWriter(CppTask task, int stream) : TextWriter
    {
        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(char value) => task.Write(stream, value.ToString());

        public override void Write(string? value) => task.Write(stream, value ?? string.Empty);
    }
}
