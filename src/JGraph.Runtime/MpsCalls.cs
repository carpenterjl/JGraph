using JGraph.Scripting.Jgs;

namespace JGraph.Runtime;

/// <summary>A Production Server's answer to one request: the HTTP status, the reason phrase R2025b's server writes, and the JSON body for a 200.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Reason">The reason phrase: <c>OK</c>, or R2025b's code and text (<c>InvalidInput Invalid input</c>).</param>
/// <param name="Body">The JSON reply for a 200: <c>{"lhs":[…]}</c> or <c>{"error":{…}}</c>; null otherwise.</param>
public sealed record MpsReply(int Status, string Reason, string? Body);

/// <summary>
/// The call a JGraph Production Server's worker makes into a component for one HTTP request
/// (compiler and SDK plan, stage K14): the JSON body read by MATLAB Production Server's rules, the
/// function called on the engine thread, and the outputs or the error written back as R2025b's server
/// writes them (k14_server). The request's and the reply's shapes are <see cref="JgsMpsJson"/>'s.
/// </summary>
public static class MpsCalls
{
    /// <summary>
    /// Answers <c>POST /&lt;component&gt;/&lt;function&gt;</c> with <paramref name="requestBody"/>: a 400 for a body
    /// the server cannot read, a 200 with the outputs or the error the function raised, a 500 when an
    /// output has no JSON form (k14_server: out.handle). The function is one of the component's own.
    /// </summary>
    /// <param name="component">The component the archive was opened as.</param>
    /// <param name="function">The function named by the request's path.</param>
    /// <param name="requestBody">The request's body, UTF-8 read.</param>
    /// <param name="hideStack">Whether the error reply leaves the M-code frames out (<c>--hide-matlab-error-stack</c>).</param>
    /// <param name="cancellationToken">Stops the function at its next statement, or before it starts.</param>
    public static Task<MpsReply> CallAsync(Component component, string function, string requestBody, bool hideStack = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrEmpty(function);
        ArgumentNullException.ThrowIfNull(requestBody);
        return component.RunOnEngine(session => Call(session, function, requestBody, hideStack, cancellationToken), cancellationToken);
    }

    private static MpsReply Call(ComponentSession session, string function, string requestBody, bool hideStack, CancellationToken cancellationToken)
    {
        JgsMpsJson.ServerRequest request;
        try
        {
            request = JgsMpsJson.DecodeRequest(requestBody, session.Interpreter.FindClass);
        }
        catch (JgsMpsJson.ServerRefusal refusal)
        {
            return new MpsReply(refusal.Status, refusal.Code + " " + refusal.Reason, null);
        }

        JgsValue[] outputs;
        try
        {
            outputs = session.CallEngine(function, request.Nargout, request.Arguments, output: null, error: null, cancellationToken);
        }
        catch (DeployedException error)
        {
            // R2025b's server answers 200 with the error (k14_server: error.*); its frames name each file in full.
            var frames = new List<(string Name, string File, int Line)>();
            if (!hideStack)
            {
                frames.AddRange(error.Stack.Select(static f => (f.Name, f.File, f.Line)));
            }

            return new MpsReply(200, "OK", JgsMpsJson.EncodeError(error.Identifier, error.Message, frames));
        }

        try
        {
            return new MpsReply(200, "OK", JgsMpsJson.EncodeResponse([.. outputs.Take(request.Nargout)], request.OutputNotation, request.OutputNanType));
        }
        catch (JgsMpsJson.Refused)
        {
            return new MpsReply(500, "ReturnTypeNotSupported Function return type not supported.", null);
        }
    }
}
