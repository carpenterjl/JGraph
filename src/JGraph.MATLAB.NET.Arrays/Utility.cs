using System.Text;
using JGraph.Deploy;
using JGraph.MATLAB.NET.Arrays;
using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Utility;

/// <summary>
/// A component of the JGraph Runtime, as a generated class's static constructor opens it and its
/// methods call it (compiler and SDK plan, stage K8), with R2025b's <c>MWMCR</c> overloads: the
/// MWArray ones answer MWArrays, the native ones .NET values. A MATLAB error is written to standard
/// error as R2025b's runtime writes it and thrown as a plain <see cref="Exception"/> whose message is
/// R2025b's: <c>... MWMCR::EvaluateFunction error ...</c>, the message and its causes, and
/// <c>Error in =&gt;</c> with the M-code stack (<c>k8_client</c>).
/// </summary>
public class MWMCR : IDisposable
{
    /// <summary>Whether the runtime can be started in this process; always, for JGraph's.</summary>
    public static bool MCRAppInitialized = true;

    /// <summary>R2025b's switch between the .NET Framework and .NET runtimes; JGraph runs on .NET 8 only.</summary>
    public static bool? UseCore = true;

    private readonly Component _component;

    /// <summary>Opens <c>&lt;componentPath&gt;\&lt;componentName&gt;.ctf</c>, an archive written beside the assembly (<c>EmbedArchive</c> off).</summary>
    public MWMCR(string componentName, string componentPath, bool isLibrary)
    {
        ArgumentNullException.ThrowIfNull(componentName);
        ArgumentNullException.ThrowIfNull(componentPath);
        _component = Component.Open(Path.Combine(componentPath, componentName + ".ctf"));
    }

    /// <summary>Opens the archive an assembly embeds, or, with no stream, the one named beside it.</summary>
    public MWMCR(string componentId, string componentPath, Stream embeddedCtfStream, bool isLibrary)
    {
        _component = embeddedCtfStream is not null
            ? Component.Open(embeddedCtfStream)
            : Component.Open(Path.Combine(componentPath ?? string.Empty, (componentId ?? string.Empty) + ".ctf"));
    }

    /// <summary>Retires this handle; the component's session lasts until the runtime terminates, as R2025b's.</summary>
    public void Dispose()
    {
        _component.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Calls a function with native arguments, answering <paramref name="numArgsOut"/> native values.</summary>
    public object[] EvaluateFunction(int numArgsOut, string functionName, params object[] argsIn)
    {
        CheckCount(numArgsOut);
        return [.. Call(functionName, numArgsOut, NativeInputs(argsIn)).Select(NativeConversion.ToNative)];
    }

    /// <summary>Calls a function with MWArray arguments, filling <paramref name="argsOut"/> with its outputs.</summary>
    public void EvaluateFunction(string functionName, int numArgsOut, ref MWArray[] argsOut, MWArray[] argsIn) =>
        argsOut = EvaluateFunction(functionName, numArgsOut, argsIn);

    /// <summary>Calls a function with native arguments, answering its first output.</summary>
    public object EvaluateFunction(string functionName, params object[] argsIn) =>
        NativeConversion.ToNative(Call(functionName, 1, NativeInputs(argsIn))[0]);

    /// <summary>Calls a function with MWArray arguments, answering its first output.</summary>
    public MWArray EvaluateFunction(string functionName, params MWArray[] argsIn) =>
        MWArray.FromRuntime(Call(functionName, 1, Inputs(argsIn))[0]);

    /// <summary>Calls a function with MWArray arguments, answering <paramref name="numArgsOut"/> outputs.</summary>
    public MWArray[] EvaluateFunction(string functionName, int numArgsOut, MWArray[] argsIn)
    {
        CheckCount(numArgsOut);
        return [.. Call(functionName, numArgsOut, Inputs(argsIn)).Select(MWArray.FromRuntime)];
    }

    /// <summary>Calls a function with MWArray arguments, answering <paramref name="numArgsOut"/> outputs.</summary>
    public MWArray[] EvaluateFunction(int numArgsOut, string functionName, params MWArray[] argsIn) =>
        EvaluateFunction(functionName, numArgsOut, argsIn);

    /// <summary>The native class's typed call: named arguments, then the variable ones.</summary>
    public void EvaluateFunctionForTypeSafeCall(string functionName, int numArgsOut, ref object[] argsOut, object[] argsIn, params object[] varArgsIn)
    {
        CheckCount(numArgsOut);
        object[] all = [.. argsIn ?? [], .. varArgsIn ?? []];
        argsOut = [.. Call(functionName, numArgsOut, [.. all.Select(NativeConversion.FromNative)]).Select(NativeConversion.ToNative)];
    }

    /// <summary>The <c>-logfile</c> the runtime writes, or null.</summary>
    public static string GetMCRLogFileName() => JgRuntime.Options.LogFile;

    /// <summary>Whether the runtime is running.</summary>
    public static bool IsMCRInitialized() => JgRuntime.IsInitialized;

    /// <summary>False: JGraph's runtime has no Java virtual machine.</summary>
    public static bool IsMCRJVMEnabled() => false;

    /// <summary>Runs <paramref name="userFunc"/>; macOS's run loop is not JGraph's concern, which runs on Windows.</summary>
    public static void setupMacRunLoopAndRun(Func<object, int> userFunc, object args)
    {
        ArgumentNullException.ThrowIfNull(userFunc);
        _ = userFunc(args);
    }

    /// <summary>Terminates the runtime.</summary>
    public static bool TerminateApplication()
    {
        JgRuntime.Terminate();
        return true;
    }

    /// <summary>Terminates the runtime.</summary>
    public static bool TerminateApplicationEx() => TerminateApplication();

    /// <summary>Waits until none of the component's figures is on screen.</summary>
    public void WaitForFiguresToDie() => _component.WaitForFiguresToClose();

    private static void CheckCount(int numArgsOut)
    {
        if (numArgsOut < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numArgsOut), "Argument must be non-negative");
        }
    }

    private static RuntimeValue[] Inputs(MWArray[] argsIn)
    {
        var inputs = new RuntimeValue[argsIn?.Length ?? 0];
        for (int i = 0; i < inputs.Length; i++)
        {
            // R2025b's refusal of a null argument (k8_client: echo.null).
            inputs[i] = argsIn[i] is { } given ? given.ToRuntime() : throw new ArgumentNullException("Invalid Null Argument", new Exception());
        }

        return inputs;
    }

    /// <summary>
    /// A native call's arguments, an <c>object[]</c> among them spread out in its place: the native
    /// class hands varargin so, after the named arguments, and R2025b spreads any such array
    /// (<c>k8_client</c>: <c>native.echo.objects</c> is two arguments).
    /// </summary>
    private static RuntimeValue[] NativeInputs(object[] argsIn) =>
        [.. (argsIn ?? []).SelectMany(static a => a?.GetType() == typeof(object[]) ? (object[])a : [a]).Select(NativeConversion.FromNative)];

    private RuntimeValue[] Call(string functionName, int numArgsOut, RuntimeValue[] inputs)
    {
        try
        {
            return _component.Call(functionName, numArgsOut, inputs);
        }
        catch (DeployedException error)
        {
            Console.Error.Write(Reported(functionName, error));
            Console.Error.Flush();
            throw new Exception(Message(error));
        }
    }

    /// <summary>The report R2025b's runtime writes to standard error for an error a call raised: no identifier line, two blank lines after.</summary>
    private static string Reported(string functionName, DeployedException error)
    {
        // An error at the call itself (too many inputs or outputs) is the called function's (k8_client).
        string report = error.Stack.Count == 0 && !error.Report.StartsWith("Error using ", StringComparison.Ordinal)
            ? $"Error using {functionName}\n{error.Report}"
            : error.Report;
        if (error.Identifier.Length > 0 && report.EndsWith("\n" + error.Identifier, StringComparison.Ordinal))
        {
            report = report[..^(error.Identifier.Length + 1)];
        }

        return report.TrimEnd('\n') + "\n\n\n";
    }

    /// <summary>R2025b's message for an error a call raised (<c>k8_client</c>: <c>err.*</c>).</summary>
    private static string Message(DeployedException error)
    {
        var text = new StringBuilder("... MWMCR::EvaluateFunction error ... \n").Append(error.Message);
        foreach (string cause in error.Causes)
        {
            text.Append('\n').Append(cause);
        }

        if (error.Stack.Count == 0)
        {
            return text.Append('.').ToString();
        }

        DeployedStackFrame entry = error.Stack[^1];
        text.Append("\nError in => ").Append(Path.GetFileName(entry.File)).Append(" at line ").Append(entry.Line).Append(".\n\n... Matlab M-code Stack Trace ...\n");
        text.Append(string.Join("\n", error.Stack.Select(static f =>
            $"    at\nfile {f.File}, name {f.Name[(f.Name.LastIndexOf('>') + 1)..]}, line {f.Line}.")));
        return text.ToString();
    }
}

/// <summary>An error with a MATLAB stack, as R2025b's MWArray API declares one.</summary>
public class MWException : ApplicationException
{
    private readonly string[] _stack;

    /// <summary>An error with this message and stack.</summary>
    public MWException(string msg, string[] stack)
        : base(msg) => _stack = stack ?? [];

    /// <summary>The MATLAB stack, one frame per line.</summary>
    public override string StackTrace => string.Join("\n", _stack);
}

/// <summary>A generated native method's MATLAB signature: its inputs, outputs and whether it takes varargin.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MATLABSignature : Attribute
{
    /// <summary>The function.</summary>
    public string Name;

    /// <summary>Its named inputs.</summary>
    public int Inputs;

    /// <summary>Its outputs.</summary>
    public int Outputs;

    /// <summary>Whether it takes varargin.</summary>
    public bool HasVarArgIn;

    /// <summary>A signature.</summary>
    public MATLABSignature(string name, int inputs, int outputs, int hasvarargin)
    {
        Name = name;
        Inputs = inputs;
        Outputs = outputs;
        HasVarArgIn = hasvarargin != 0;
    }
}

/// <summary>The runtime version an assembly was built for.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MCRVersion : Attribute
{
    /// <summary>The major version.</summary>
    public string Major;

    /// <summary>The minor version.</summary>
    public string Minor;

    /// <summary>The update.</summary>
    public string Update;

    /// <summary>A version.</summary>
    public MCRVersion(string major, string minor, string update)
    {
        Major = major;
        Minor = minor;
        Update = update;
    }
}

/// <summary>A runtime option an assembly asks for (<c>-R</c>).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MWMCROptionAttribute : Attribute
{
    /// <summary>An option.</summary>
    public MWMCROptionAttribute(string OptVal) => MWMCROption = OptVal;

    /// <summary>The option.</summary>
    public string MWMCROption { get; }
}

/// <summary>Whether an assembly starts the runtime without Java; JGraph's has none.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NOJVMAttribute : Attribute
{
    /// <summary>A choice.</summary>
    public NOJVMAttribute(bool JVMDisable) => JVMDisabled = JVMDisable;

    /// <summary>Whether Java is off.</summary>
    public bool JVMDisabled { get; }
}

/// <summary>Whether native memory is collected under pressure; JGraph's arrays are managed.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NativeGCAttribute : Attribute
{
    /// <summary>A choice.</summary>
    public NativeGCAttribute(bool enableGCTrigger) => GCEnabled = enableGCTrigger;

    /// <summary>A choice and a block size.</summary>
    public NativeGCAttribute(bool enableGCTrigger, int GCBlockSize)
    {
        GCEnabled = enableGCTrigger;
        this.GCBlockSize = GCBlockSize;
    }

    /// <summary>Whether collection is on.</summary>
    public bool GCEnabled { get; }

    /// <summary>The block size.</summary>
    public int GCBlockSize { get; set; }
}

/// <summary>The log file an assembly's runtime writes (<c>-R -logfile</c>).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LOGFILEAttribute : Attribute
{
    /// <summary>A log file.</summary>
    public LOGFILEAttribute(string fileName) => LogfileName = fileName;

    /// <summary>The log file.</summary>
    public string LogfileName { get; }
}

/// <summary>A reader of an embedded archive; JGraph's runtime reads the stream itself.</summary>
public class MWCTFStreamReader
{
    private readonly Stream _stream;

    /// <summary>A reader of <paramref name="embeddedCtfStream"/>.</summary>
    public MWCTFStreamReader(Stream embeddedCtfStream) => _stream = embeddedCtfStream;

    /// <summary>No native reader: the runtime opens the stream as a .NET one.</summary>
    public IntPtr CtfStreamReadFcn => _stream is null ? IntPtr.Zero : IntPtr.Zero;
}
