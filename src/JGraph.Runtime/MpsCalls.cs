using JGraph.Scripting.Jgs;

namespace JGraph.Runtime;

/// <summary>A Production Server's answer to one request: the HTTP status, the reason phrase R2025b's server writes, and the JSON body for a 200.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Reason">The reason phrase: <c>OK</c>, or R2025b's code and text (<c>InvalidInput Invalid input</c>).</param>
/// <param name="Body">The JSON reply for a 200: <c>{"lhs":[…]}</c> or <c>{"error":{…}}</c>; null otherwise.</param>
public sealed record MpsReply(int Status, string Reason, string? Body);

/// <summary>A Production Server's answer to one protobuf request (stage K16): the status, the reason phrase, and the <c>MATLAB_Result</c> bytes for a 200 (empty for no outputs).</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Reason">The reason phrase: <c>OK</c>, or R2025b's code and text.</param>
/// <param name="Body">The protobuf reply for a 200; null otherwise.</param>
public sealed record MpsBinaryReply(int Status, string Reason, byte[]? Body);

/// <summary>
/// A web handler's answer (compiler and SDK plan, stage K15): the handler's status, message, headers and
/// body, or, when <paramref name="Refused"/>, the server's 500 and its reason for an answer that is not
/// as it should be or a handler that failed.
/// </summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Reason">The reason phrase.</param>
/// <param name="Headers">The response headers, in the handler's order.</param>
/// <param name="Body">The response body.</param>
/// <param name="Refused">Whether this is the server's refusal rather than the handler's answer.</param>
public sealed record MpsWebReply(int Status, string Reason, IReadOnlyList<(string Name, string Value)> Headers, byte[] Body, bool Refused);

/// <summary>
/// The call a JGraph Production Server's worker makes into a component for one HTTP request
/// (compiler and SDK plan, stage K14): the JSON body read by MATLAB Production Server's rules, the
/// function called on the engine thread, and the outputs or the error written back as R2025b's server
/// writes them (k14_server). The request's and the reply's shapes are <see cref="JgsMpsJson"/>'s.
/// Stage K15 adds <see cref="WebAsync"/>, the call a routes file's web handler gets.
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
            return new MpsReply(200, "OK", JgsMpsJson.EncodeError(error.Identifier, error.Message, Frames(error, hideStack), withStack: !hideStack));
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

    /// <summary>
    /// Answers a <c>POST /&lt;component&gt;/&lt;function&gt;</c> whose body is <c>application/x-google-protobuf</c>
    /// (stage K16, k16_server): <c>MATLAB_Params</c> read by <see cref="JgsMpsProtobuf"/>, the function
    /// called, and <c>MATLAB_Result</c> written back with the outputs or the error, as R2025b's muserve
    /// answers MathWorks' own client libraries; a body that is not a request is a 400.
    /// </summary>
    public static Task<MpsBinaryReply> CallProtobufAsync(Component component, string function, byte[] requestBody, bool hideStack = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrEmpty(function);
        ArgumentNullException.ThrowIfNull(requestBody);
        return component.RunOnEngine(session => CallProtobuf(session, function, requestBody, hideStack, cancellationToken), cancellationToken);
    }

    private static MpsBinaryReply CallProtobuf(ComponentSession session, string function, byte[] requestBody, bool hideStack, CancellationToken cancellationToken)
    {
        JgsMpsProtobuf.ServerRequest request;
        try
        {
            request = JgsMpsProtobuf.DecodeRequest(requestBody, session.Interpreter.FindClass);
        }
        catch (JgsMpsJson.ServerRefusal refusal)
        {
            return new MpsBinaryReply(refusal.Status, refusal.Code + " " + refusal.Reason, null);
        }

        if (request.Fault is not null)
        {
            // What muserve answers as a MATLAB error of its own (nargout.negative: "vector too long").
            return new MpsBinaryReply(200, "OK", JgsMpsProtobuf.EncodeError("", request.Fault, [], withStack: false));
        }

        JgsValue[] outputs;
        try
        {
            outputs = session.CallEngine(function, request.Nargout, request.Arguments, output: null, error: null, cancellationToken);
        }
        catch (DeployedException error)
        {
            return new MpsBinaryReply(200, "OK", JgsMpsProtobuf.EncodeError(error.Identifier, error.Message, Frames(error, hideStack), withStack: !hideStack));
        }

        try
        {
            return new MpsBinaryReply(200, "OK", JgsMpsProtobuf.EncodeResult([.. outputs.Take(request.Nargout)]));
        }
        catch (JgsMpsProtobuf.Refused refused)
        {
            return new MpsBinaryReply(200, "OK", JgsMpsProtobuf.EncodeError(refused.Identifier, refused.Message, [], withStack: false));
        }
    }

    /// <summary>
    /// Calls a web handler with MATLAB Production Server's request struct (k15_server: route.*):
    /// <c>ApiVersion</c> [1 0 0], <c>Method</c>, <c>Path</c> (the target as sent, query included),
    /// <c>HttpVersion</c> [1 1], <c>Headers</c> an N-by-2 cell of names and values, <c>Body</c> a 1-by-N
    /// uint8; and reads its response struct as muserve checks it (bad.*): exactly one 1-by-1 struct
    /// output, no field but <c>ApiVersion</c>, <c>HttpCode</c> (1-by-1 double, truncated), <c>HttpMessage</c>
    /// (a non-empty char row), <c>Headers</c> (an N-by-2 cell, N at least 1, a value that is not char
    /// dropped) and <c>Body</c> (a 1-by-N uint8); a field left out is 200, <c>OK</c>, no headers, no body.
    /// A handler that fails answers 500 <c>MATLAB error: message</c>.
    /// </summary>
    public static Task<MpsWebReply> WebAsync(
        Component component, string function, string method, string path, string httpVersion, IReadOnlyList<(string Name, string Value)> headers,
        byte[] body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrEmpty(function);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(body);
        return component.RunOnEngine(session => Web(session, function, method, path, httpVersion, headers, body, cancellationToken), cancellationToken);
    }

    private static MpsWebReply Web(
        ComponentSession session, string function, string method, string path, string httpVersion, IReadOnlyList<(string Name, string Value)> headers,
        byte[] body, CancellationToken cancellationToken)
    {
        static MpsWebReply Refusal(string reason) => new(500, reason, [], [], Refused: true);

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["ApiVersion"] = JgsMatrix.FromColumnMajor([1, 0, 0], 1, 3),
            ["Method"] = JgsValue.Str(method),
            ["Path"] = JgsValue.Str(path),
            ["HttpVersion"] = JgsMatrix.FromColumnMajor(httpVersion.Split('.').Select(static p => double.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0).ToArray(), 1, 2),
            ["Headers"] = HeadersCell(headers),
            ["Body"] = Bytes(body),
        };

        JgsValue[] outputs;
        try
        {
            outputs = session.CallEngine(function, 1, [JgsValue.Struct(fields)], output: null, error: null, cancellationToken);
        }
        catch (DeployedException error)
        {
            return Refusal("MATLAB error: " + error.Message.ReplaceLineEndings(" "));
        }

        if (outputs.Length == 0 || outputs[0].Type != JgsType.Struct || !IsScalar(outputs[0]))
        {
            return Refusal("WebFunctions must return exactly one output (1x1 struct)");
        }

        Dictionary<string, JgsValue> answer = outputs[0].AsStructArray.Elements[0];
        foreach (string name in answer.Keys)
        {
            if (name is not ("ApiVersion" or "HttpCode" or "HttpMessage" or "Headers" or "Body"))
            {
                return Refusal($"Invalid response struct (invalid field \"{name}\")");
            }
        }

        byte[] bytes = [];
        if (answer.TryGetValue("Body", out JgsValue? bodyValue))
        {
            if (!TryBytes(bodyValue, out bytes))
            {
                return Refusal("Invalid \"Body\" response struct field (type must be 1xN uint8)");
            }
        }

        int code = 200;
        if (answer.TryGetValue("HttpCode", out JgsValue? codeValue))
        {
            if (codeValue.Type != JgsType.Number || codeValue.NumericClass != JgsNumericClass.Double)
            {
                return Refusal("Invalid \"HttpCode\" response struct field (type must be 1x1 double)");
            }

            code = (int)Math.Truncate(codeValue.AsNumber);
        }

        string message = "OK";
        if (answer.TryGetValue("HttpMessage", out JgsValue? messageValue))
        {
            if (messageValue.Type != JgsType.String || messageValue.IsCharMatrix || messageValue.AsString.Length == 0)
            {
                return Refusal("Invalid \"HttpMessage\" response struct field (type must be 1xN char)");
            }

            message = messageValue.AsString;
        }

        var responseHeaders = new List<(string Name, string Value)>();
        if (answer.TryGetValue("Headers", out JgsValue? headersValue))
        {
            if (headersValue.Type != JgsType.Cell || headersValue.Dims is not [int rows, 2] || rows < 1)
            {
                return Refusal("Invalid \"Headers\" response struct field (type must be Nx2 cell)");
            }

            JgsValue[] cells = headersValue.AsCell;
            for (int r = 0; r < rows; r++)
            {
                JgsValue name = cells[r];
                JgsValue value = cells[rows + r];
                if (name.Type == JgsType.String && !name.IsCharMatrix && value.Type == JgsType.String && !value.IsCharMatrix)
                {
                    responseHeaders.Add((name.AsString, value.AsString));
                }
            }
        }

        return new MpsWebReply(code, message, responseHeaders, bytes, Refused: false);
    }

    /// <summary>
    /// An error's M-code frames as R2025b's server names them (k14_server, k16_server: error.nested): a local
    /// function by its own name, where JGraph's stack says <c>file&gt;local</c>; none with the stack hidden.
    /// </summary>
    private static List<(string Name, string File, int Line)> Frames(DeployedException error, bool hideStack)
    {
        var frames = new List<(string Name, string File, int Line)>();
        if (!hideStack)
        {
            frames.AddRange(error.Stack.Select(static f => (f.Name[(f.Name.LastIndexOf('>') + 1)..], f.File, f.Line)));
        }

        return frames;
    }

    private static bool IsScalar(JgsValue value) => value.Dims is [1, 1] or [];

    private static JgsValue HeadersCell(IReadOnlyList<(string Name, string Value)> headers)
    {
        var cells = new JgsValue[headers.Count * 2];
        for (int i = 0; i < headers.Count; i++)
        {
            cells[i] = JgsValue.Str(headers[i].Name);
            cells[headers.Count + i] = JgsValue.Str(headers[i].Value);
        }

        JgsValue cell = JgsValue.Cell(cells);
        cell.Reshape(headers.Count, 2);
        return cell;
    }

    private static JgsValue Bytes(byte[] body)
    {
        var values = new double[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            values[i] = body[i];
        }

        JgsValue array = JgsMatrix.FromColumnMajor(values, 1, body.Length);
        array.SetNumericClass(JgsNumericClass.UInt8);
        return array;
    }

    /// <summary>A 1-by-N uint8 as bytes (a 1-by-1 is a scalar); false for anything else, a 0-by-0 included (bad.bodyempty).</summary>
    private static bool TryBytes(JgsValue value, out byte[] bytes)
    {
        bytes = [];
        if (value.NumericClass != JgsNumericClass.UInt8)
        {
            return false;
        }

        if (value.Type == JgsType.Number)
        {
            bytes = [(byte)value.AsNumber];
            return true;
        }

        if (value.Type != JgsType.Array || value.Dims is not [1, _])
        {
            return false;
        }

        bytes = new byte[value.ArrayLength];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)value.ElementAt(i).AsNumber;
        }

        return true;
    }
}
