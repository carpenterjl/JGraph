using JGraph.Deploy;

namespace JGraph.Runtime;

/// <summary>
/// A deployable archive opened in this process's JGraph Runtime: a library component whose public
/// functions a host calls (compiler and SDK plan, stage K4).
/// </summary>
/// <remarks>
/// Opening an archive extracts it into the runtime cache and starts a deployed session over it, in
/// which <c>isdeployed</c> is true, the path is the archive's and frozen, and <c>ctfroot</c> is the
/// extracted copy. Every <see cref="Component"/> opened on the same archive (the same build) shares
/// that session, as two instances of one R2025b .NET component do, and the session lasts until
/// <see cref="JgRuntime.Terminate"/>; disposing such a component only retires this handle. A
/// component opened with a session of its own, as each C++ library and Python package instance is in
/// R2025b (k12_client lib.two.of.one, k13_client life), ends its session when it is disposed.
/// </remarks>
public sealed class Component : IDisposable
{
    private readonly ComponentSession _session;
    private readonly EngineThread _engine;
    private readonly bool _ownSession;
    private bool _disposed;

    private Component(ComponentSession session, EngineThread engine, CtfManifest manifest, bool ownSession)
    {
        _session = session;
        _engine = engine;
        _ownSession = ownSession;
        Name = manifest.ComponentName;
        Uuid = manifest.Uuid;
        Functions = [.. manifest.PublicFunctions.Select(static f => f.Name)];
        Signatures = manifest.PublicFunctions;
    }

    /// <summary>The public functions' inputs and outputs, as the manifest records them (stage K8: the MWArray API's varargin).</summary>
    internal IReadOnlyList<CtfFunction> Signatures { get; }

    /// <summary>The component's name.</summary>
    public string Name { get; }

    /// <summary>The archive's identity, which the runtime cache keys on.</summary>
    public string Uuid { get; }

    /// <summary>The functions a host may call.</summary>
    public IReadOnlyList<string> Functions { get; }

    /// <summary>The folder the archive was extracted to, which <c>ctfroot</c> answers.</summary>
    public string CtfRoot => _session.Deployment.CtfRoot;

    /// <summary>
    /// Opens the archive at <paramref name="archivePath"/>, a <c>.ctf</c> or a file that carries one.
    /// The runtime is started with the archive's <c>-R</c> options if it is not running.
    /// </summary>
    /// <param name="archivePath">The archive.</param>
    /// <param name="ownSession">Whether the component gets a session of its own, ended with it, rather than the archive's shared one.</param>
    /// <param name="cacheFolderName">
    /// The name the runtime cache's folder gets when the archive is extracted now, which <c>ctfroot</c>
    /// ends with: a Production Server's <c>&lt;archive&gt;_&lt;n&gt;</c> (stage K15); the cache's own name when null.
    /// </param>
    /// <exception cref="InvalidDataException">The file holds no JGraph archive, or a damaged one.</exception>
    /// <param name="cacheDirectory">The runtime cache the archive is extracted into; the user's runtime cache when null.</param>
    public static Component Open(string archivePath, bool ownSession = false, string? cacheFolderName = null, string? cacheDirectory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        string full = Path.GetFullPath(archivePath);
        CtfManifest manifest;
        using (CtfArchiveReader archive = CtfArchiveReader.Open(full))
        {
            manifest = archive.Manifest;
        }

        EngineThread engine = JgRuntime.Engine(RuntimeOptions.Parse(manifest.RuntimeOptions));
        ComponentSession session = engine.Invoke(() => JgRuntime.Open(full, manifest.Uuid, ownSession, cacheFolderName, cacheDirectory));
        return new Component(session, engine, manifest, ownSession);
    }

    /// <summary>
    /// Opens an archive read from <paramref name="archive"/>, as a .NET assembly opens the archive it
    /// embeds. The stream is copied to a temporary file, extracted, and the file deleted.
    /// </summary>
    public static Component Open(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        string copy = Path.Combine(Path.GetTempPath(), "jgraph-" + Guid.NewGuid().ToString("N") + ".ctf");
        try
        {
            using (FileStream file = File.Create(copy))
            {
                archive.CopyTo(file);
            }

            return Open(copy);
        }
        finally
        {
            try
            {
                File.Delete(copy);
            }
            catch (IOException)
            {
                // The cache holds the extracted copy; a temporary file left behind harms nothing.
            }
        }
    }

    /// <summary>
    /// Calls <paramref name="function"/> and waits for it, asking for <paramref name="nargout"/>
    /// outputs, as <c>[o1, ..., on] = function(arguments{:})</c> would.
    /// </summary>
    /// <exception cref="DeployedException">The function raised an error it did not catch.</exception>
    /// <exception cref="ArgumentException">The component has no public function of that name, or an argument cannot be passed.</exception>
    public RuntimeValue[] Call(string function, int nargout, params RuntimeValue[] arguments)
    {
        try
        {
            return CallAsync(function, nargout, arguments).GetAwaiter().GetResult();
        }
        catch (AggregateException wrapped) when (wrapped.InnerExceptions.Count == 1)
        {
            throw wrapped.InnerExceptions[0];
        }
    }

    /// <summary>
    /// Calls <paramref name="function"/> on the engine thread, after the calls queued before it.
    /// Cancelling the token stops the function at its next statement, or before it starts.
    /// </summary>
    public Task<RuntimeValue[]> CallAsync(
        string function, int nargout, IReadOnlyList<RuntimeValue> arguments, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(function);
        ArgumentOutOfRangeException.ThrowIfNegative(nargout);
        ArgumentNullException.ThrowIfNull(arguments);
        if (Functions.Count > 0 && !Functions.Contains(function, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The component '{Name}' has no public function '{function}'.", nameof(function));
        }

        RuntimeValue[] held = [.. arguments];
        if (Array.Exists(held, static a => a is null))
        {
            throw new ArgumentException("An argument cannot be null; pass RuntimeValue.Empty() for [].", nameof(arguments));
        }

        return _engine.Run(
            () =>
            {
                JgRuntime.Activate(_session);
                return _session.Call(function, nargout, held, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// Waits until none of the component's figures is on screen, as R2025b's
    /// <c>WaitForFiguresToDie</c> does: at once when the component has shown none, or only invisible
    /// ones (<c>k5_client</c>). Its windows keep answering their user meanwhile, between calls.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called on the thread the windows run on, where they could never close.</exception>
    public void WaitForFiguresToClose(CancellationToken cancellationToken = default)
    {
        if (JgRuntime.LoadedDesktop is not { } desktop)
        {
            return;
        }

        if (desktop.IsWindowThread)
        {
            throw new InvalidOperationException(
                "WaitForFiguresToClose cannot be called on the thread that runs the figure windows: they could never close.");
        }

        while (desktop.AnyVisible(_session))
        {
            if (cancellationToken.WaitHandle.WaitOne(50))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the engine thread with this component's session made live, after
    /// the calls queued before it (the MATLAB Data API, stage K9: its conversions and calls).
    /// </summary>
    internal Task<T> RunOnEngine<T>(Func<ComponentSession, T> work, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.Run(
            () =>
            {
                JgRuntime.Activate(_session);
                return work(_session);
            },
            cancellationToken);
    }

    /// <summary>Whether this handle has been retired.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Retires this handle: the archive's shared session goes on for any other handle on it, and a
    /// session of the component's own ends once the work queued before it is done.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownSession)
        {
            ComponentSession session = _session;
            _engine.Post(() => JgRuntime.Close(session));
        }
    }
}
