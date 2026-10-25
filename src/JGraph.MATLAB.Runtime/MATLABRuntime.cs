using JGraph.MATLAB.Exceptions;
using JGraph.MATLAB.Types;
using JGraph.Runtime;

namespace JGraph.MATLAB.Runtime;

/// <summary>
/// A deployable archive running on the JGraph Runtime, called through the MATLAB Data API for .NET
/// (compiler and SDK plan, stage K9): R2025b's MathWorks.MATLAB.Runtime.MATLABRuntime under JGraph's
/// namespace. <see cref="StartMATLAB"/> opens the archive; a member called on the instance - held as
/// <c>dynamic</c> - calls the archive's function of that name.
/// </summary>
public class MATLABRuntime : MATLABProvider
{
    private static bool _terminated;
    private readonly Component _component;

    private MATLABRuntime(Component component)
    {
        _component = component;
    }

    /// <inheritdoc/>
    internal override Component Component => _component;

    /// <summary>Opens the archive <paramref name="ctfFile"/> and answers the instance that calls it.</summary>
    /// <exception cref="MATLABException">The file is missing or holds no archive.</exception>
    /// <exception cref="MATLABNotAvailableException">The runtime has been terminated in this process.</exception>
    public static MATLABRuntime StartMATLAB(string ctfFile)
    {
        if (_terminated)
        {
            throw new MATLABNotAvailableException("The MATLAB session has been terminated.");
        }

        if (string.IsNullOrEmpty(ctfFile))
        {
            throw new MATLABException("Code archive (.ctf file) not specified.");
        }

        string full = Path.GetFullPath(ctfFile);
        if (!File.Exists(full))
        {
            throw new MATLABException($"{full} file not found.");
        }

        try
        {
            return new MATLABRuntime(Component.Open(full));
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException)
        {
            // R2025b's words, with no inner exception (k9client: start.notctf).
            throw new MATLABException(
                $"Failed to initialize MATLAB runtime: CTF file '{full}' failed to open for 'Read' access. Error message: 'Could not open source package'");
        }
    }

    /// <summary>Opens the archive on another thread and answers a task of the instance.</summary>
    public static Task<MATLABRuntime> StartMATLABAsync(string ctfFile) => StartMATLABAsync(ctfFile, CancellationToken.None);

    /// <summary>Opens the archive on another thread, cancelled by <paramref name="cancelToken"/>.</summary>
    public static async Task<MATLABRuntime> StartMATLABAsync(string ctfFile, CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();
        return await Task.Run(() => StartMATLAB(ctfFile), cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends the runtime for the rest of the process: every instance stops answering and no new one
    /// starts, as R2025b's does.
    /// </summary>
    public static void TerminateApplication()
    {
        if (!_terminated)
        {
            _terminated = true;
            JgRuntime.Terminate();
        }
    }

    /// <summary>
    /// Runs <paramref name="userFunc"/> with <paramref name="args"/>. On macOS R2025b's runtime needs
    /// the main thread's run loop for its windows; the JGraph Runtime does not, so this is a plain call.
    /// </summary>
    public static void SetupMacRunLoopAndRun(Func<object, int> userFunc, object args)
    {
        ArgumentNullException.ThrowIfNull(userFunc);
        userFunc(args);
    }

    /// <summary>Waits until none of the component's figures is on screen, as a deployed program's end waits.</summary>
    public void WaitForFiguresToClose() => _component.WaitForFiguresToClose();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!Disposed)
        {
            // The archive's session lasts until the runtime ends; this handle only retires.
            _component.Dispose();
        }

        base.Dispose(disposing);
    }
}
