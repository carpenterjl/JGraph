namespace JGraph.MATLAB.Types;

/// <summary>
/// How a MATLAB function is called (R2025b's RunOptions, k9client: call.runoptions.*): how many
/// outputs it is asked for (1 when nothing says), whether the call answers a task, the token that
/// cancels it, and where its standard output and error go (the console's when nothing says).
/// </summary>
public struct RunOptions
{
    // Stored less one, so that default(RunOptions) - new RunOptions() - asks for one output as R2025b's does.
    private int _nargoutLessOne;
    private TextWriter _output;
    private TextWriter _error;

    /// <summary>Calls with <paramref name="nargout"/> outputs, synchronously or not.</summary>
    public RunOptions(int nargout = 1, bool async = false)
    {
        _nargoutLessOne = nargout - 1;
        Async = async;
        CancelToken = CancellationToken.None;
        _output = null;
        _error = null;
    }

    /// <summary>Calls asynchronously with <paramref name="nargout"/> outputs, cancelled by <paramref name="cancelToken"/>.</summary>
    public RunOptions(CancellationToken cancelToken, int nargout = 1)
        : this(nargout, async: true)
    {
        CancelToken = cancelToken;
    }

    /// <summary>Calls with <paramref name="nargout"/> outputs, its text written to <paramref name="output"/> and <paramref name="error"/>.</summary>
    public RunOptions(TextWriter output, TextWriter error, int nargout = 1, bool async = false)
        : this(nargout, async)
    {
        _output = output;
        _error = error;
    }

    /// <summary>Calls asynchronously, its text written to <paramref name="output"/> and <paramref name="error"/>, cancelled by <paramref name="cancelToken"/>.</summary>
    public RunOptions(TextWriter output, TextWriter error, CancellationToken cancelToken, int nargout = 1)
        : this(output, error, nargout, async: true)
    {
        CancelToken = cancelToken;
    }

    /// <summary>Number of values the MATLAB function is asked for; 1 by default.</summary>
    public int Nargout
    {
        readonly get => _nargoutLessOne + 1;
        set => _nargoutLessOne = value - 1;
    }

    /// <summary>Whether the call answers a task rather than its result; false by default.</summary>
    public bool Async { get; set; }

    /// <summary>The token that cancels the call; none by default.</summary>
    public CancellationToken CancelToken { get; set; }

    /// <summary>Where the function's standard output goes; <see cref="Console.Out"/> by default.</summary>
    public TextWriter Output
    {
        readonly get => _output ?? Console.Out;
        set => _output = value;
    }

    /// <summary>Where the function's error text goes; <see cref="Console.Error"/> by default.</summary>
    public TextWriter Error
    {
        readonly get => _error ?? Console.Error;
        set => _error = value;
    }
}
