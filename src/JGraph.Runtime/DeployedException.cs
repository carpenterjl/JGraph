namespace JGraph.Runtime;

/// <summary>A frame of a deployed function's error: the function, its file and the line it was on.</summary>
/// <param name="Name">The function, a local function as <c>file&gt;local</c>.</param>
/// <param name="File">The file it is in, in the component's extracted archive.</param>
/// <param name="Line">The line, 0 in a P-file.</param>
public sealed record DeployedStackFrame(string Name, string File, int Line);

/// <summary>
/// An error a deployed function raised and did not catch: the MException's identifier, message and
/// stack, and the report R2025b's runtime would print for it.
/// </summary>
public sealed class DeployedException : Exception
{
    internal DeployedException(string identifier, string message, IReadOnlyList<DeployedStackFrame> stack, string report, Exception? inner = null)
        : base(message, inner)
    {
        Identifier = identifier;
        Stack = stack;
        Report = report;
    }

    /// <summary>The MException's identifier, empty when it had none.</summary>
    public string Identifier { get; }

    /// <summary>The frames the error unwound through, innermost first.</summary>
    public IReadOnlyList<DeployedStackFrame> Stack { get; }

    /// <summary>
    /// The error as R2025b's runtime reports one: <c>Error using</c>, the message, the frames as
    /// <c>Error in</c> lines, and the identifier last.
    /// </summary>
    public string Report { get; }
}
