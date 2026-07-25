using System.Text.Json;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.PythonConsole;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The wire protocol and host-side call dispatch for the out-of-process console. Both live in net8.0
/// and need no interpreter installed, which is the whole reason they were split out from the session:
/// everything except the live child round trip is covered here.
/// </summary>
[Collection("JG facade")]
public class ConsoleProtocolTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public ConsoleProtocolTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private PythonHostBridge Bridge() =>
        new(new JGraphScriptGlobals(new ScriptContext(_output, (_, figure) => _figures.Add(figure))));

    private static PythonConsoleMessage Call(string function, string argsJson, int seq = 1) =>
        new() { Type = "call", Seq = seq, Fn = function, Args = JsonDocument.Parse(argsJson).RootElement };

    [Fact]
    public void AnExecRequest_RoundTrips()
    {
        string line = PythonConsoleCodec.Encode(PythonConsoleCodec.Exec(7, "x = 1"));

        Assert.True(PythonConsoleCodec.TryDecode(line, out PythonConsoleMessage decoded));
        Assert.Equal(7, decoded.Id);
        Assert.Equal("exec", decoded.Op);
        Assert.Equal("x = 1", decoded.Code);
    }

    [Fact]
    public void EncodedMessages_AreASingleLine()
    {
        string line = PythonConsoleCodec.Encode(PythonConsoleCodec.Exec(1, "a = 'first\nsecond'"));

        Assert.DoesNotContain('\n', line); // framing is one message per line, so this is load-bearing
        Assert.True(PythonConsoleCodec.TryDecode(line, out PythonConsoleMessage decoded));
        Assert.Equal("a = 'first\nsecond'", decoded.Code);
    }

    [Fact]
    public void AbsentFields_TakeTheirDefaults_SoTheProtocolCanGrow()
    {
        Assert.True(PythonConsoleCodec.TryDecode("""{"id":3,"type":"done","ok":true,"unknown":42}""",
            out PythonConsoleMessage decoded));

        Assert.True(decoded.Ok);
        Assert.Equal(0, decoded.Line);
        Assert.Null(decoded.Exit);
        Assert.Null(decoded.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{unterminated")]
    public void GarbageOnTheChannel_IsRejectedRatherThanThrown(string line) =>
        Assert.False(PythonConsoleCodec.TryDecode(line, out _));

    [Fact]
    public void AVariableSnapshot_RoundTripsItsPayload()
    {
        var snapshot = new PythonConsoleMessage
        {
            Type = "vars",
            Items = new[] { new PythonVariablePayload("xs", "array", "[1.0, 2.0]", new[] { 1.0, 2.0 }) },
        };

        Assert.True(PythonConsoleCodec.TryDecode(PythonConsoleCodec.Encode(snapshot), out PythonConsoleMessage decoded));
        PythonVariablePayload item = Assert.Single(decoded.Items!);
        Assert.Equal("xs", item.Name);
        Assert.Equal(new[] { 1.0, 2.0 }, item.Data);
    }

    [Fact]
    public void AnOversizeArray_SendsNoData_ButKeepsItsShape()
    {
        var payload = new PythonVariablePayload("big", "array", "ndarray(10000000,)", Data: null);

        Assert.True(PythonConsoleCodec.TryDecode(
            PythonConsoleCodec.Encode(new PythonConsoleMessage { Type = "vars", Items = new[] { payload } }),
            out PythonConsoleMessage decoded));
        Assert.Null(Assert.Single(decoded.Items!).Data);
    }

    [Fact]
    public void APlotCall_BuildsAPlotInTheHostsFigure()
    {
        PythonConsoleMessage reply = Bridge().Invoke(Call("plot", "[[0, 1, 2], [0, 1, 4], \"r-\"]"));

        Assert.Equal("return", reply.Type);
        Assert.Null(reply.Message);
        Assert.Single(JG.CurrentFigure.Axes[0].Plots);
    }

    [Fact]
    public void ASingleSequence_MeansPlotOfY()
    {
        Bridge().Invoke(Call("plot", "[[3, 1, 4]]"));

        Assert.Single(JG.CurrentFigure.Axes[0].Plots);
    }

    [Fact]
    public void TitleAndLabels_ReachTheAxes()
    {
        PythonHostBridge bridge = Bridge();
        bridge.Invoke(Call("plot", "[[0, 1], [0, 1]]"));
        bridge.Invoke(Call("title", "[\"Measured\"]"));
        bridge.Invoke(Call("xlabel", "[\"Time\"]"));

        AxesModel axes = JG.CurrentFigure.Axes[0];
        Assert.Equal("Measured", axes.Title);
        Assert.Equal("Time", axes.PrimaryXAxis.Label);
    }

    [Fact]
    public void Figure_ReturnsTheNumberItSelected()
    {
        PythonConsoleMessage reply = Bridge().Invoke(Call("figure", "[4]"));

        Assert.Equal(4, reply.Value!.Value.GetInt32());
        Assert.Equal(4, JG.CurrentFigureNumber);
    }

    [Fact]
    public void ABareFigureCall_TakesTheNextFreeNumber()
    {
        PythonHostBridge bridge = Bridge();
        bridge.Invoke(Call("figure", "[1]"));

        Assert.Equal(2, bridge.Invoke(Call("figure", "[]")).Value!.Value.GetInt32());
    }

    [Fact]
    public void ShowDisplaysTheFigure_ThroughTheHostsCallback()
    {
        PythonHostBridge bridge = Bridge();
        bridge.Invoke(Call("plot", "[[0, 1], [0, 1]]"));

        bridge.Invoke(Call("show", "[]"));

        Assert.Single(_figures);
    }

    [Fact]
    public void AnUnknownFunction_ComesBackAsAnError_NotASilentNoOp()
    {
        PythonConsoleMessage reply = Bridge().Invoke(Call("teleport", "[]"));

        Assert.Equal("return", reply.Type);
        Assert.Contains("teleport", reply.Message);
    }

    [Fact]
    public void AWronglyTypedArgument_ComesBackAsAnError()
    {
        PythonConsoleMessage reply = Bridge().Invoke(Call("plot", "[{\"a\": 1}]"));

        Assert.Contains("plot", reply.Message);
    }

    [Fact]
    public void TooFewArguments_ComeBackAsAnError()
    {
        PythonConsoleMessage reply = Bridge().Invoke(Call("xlim", "[0]"));

        Assert.Contains("xlim", reply.Message);
    }

    [Fact]
    public void EveryAdvertisedFunction_IsActuallyDispatched()
    {
        // The child builds its module from this list, so a name here with no case in the switch would
        // be a proxy that always raises.
        PythonHostBridge bridge = Bridge();
        foreach (string name in PythonHostBridge.FunctionNames)
        {
            PythonConsoleMessage reply = bridge.Invoke(Call(name, "[]"));
            Assert.DoesNotContain("is not a JGraph console function", reply.Message ?? string.Empty);
        }
    }
}
