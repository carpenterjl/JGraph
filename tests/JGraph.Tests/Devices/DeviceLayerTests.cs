using System.Text;
using JGraph.Devices;
using JGraph.Devices.Serial;
using JGraph.Devices.Simulation;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Devices;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// The device layer without hardware (device classes plan, stage D1): the input buffer, the precision
/// codec against R2025b's recorded bytes, the peer engine's in-band protocol, the simulated line's
/// null-modem wiring, the natural port order, validatestring's matching, and a serialport losing its
/// device. The com0com checks run only where the pair COM20/COM21 exists.
/// </summary>
[Collection("JG facade")]
public class DeviceLayerTests
{
    // --- the input buffer -----------------------------------------------------------------------------

    [Fact]
    public void TheBufferKeepsOrderAcrossItsWrap()
    {
        var buffer = new InputBuffer();
        var all = new List<byte>();
        for (int round = 0; round < 50; round++)
        {
            byte[] chunk = Enumerable.Range(0, 1000).Select(i => (byte)((i + round) % 251)).ToArray();
            buffer.Append(chunk);
            all.AddRange(chunk);
            byte[] taken = buffer.Take(700);
            Assert.Equal(all.Take(700), taken);
            all.RemoveRange(0, 700);
        }

        Assert.Equal(all.Count, buffer.Count);
        Assert.Equal(all, buffer.TakeAll());
    }

    [Fact]
    public void TheBufferFindsAPatternAndWaits()
    {
        var buffer = new InputBuffer();
        buffer.Append("abc\r"u8);
        Assert.Equal(-1, buffer.IndexAfter("\r\n"u8));
        _ = Task.Delay(50).ContinueWith(_ => buffer.Append("\ndef"u8));
        int found = buffer.WaitForPattern("\r\n"u8, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(5, found);
        Assert.False(buffer.WaitForCount(100, TimeSpan.FromMilliseconds(30), CancellationToken.None));
    }

    [Fact]
    public void AStopEndsAWaitAtOnce()
    {
        var buffer = new InputBuffer();
        using var stop = new CancellationTokenSource(50);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => buffer.WaitForCount(1, TimeSpan.FromSeconds(30), stop.Token));
        Assert.True(watch.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void AFaultEndsAWaitAndAFlushCountsAGeneration()
    {
        var buffer = new InputBuffer();
        _ = Task.Delay(30).ContinueWith(_ => buffer.SetFault(new DeviceConnectionLostException("gone")));
        Assert.False(buffer.WaitForCount(1, TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.NotNull(buffer.Fault);
        long generation = buffer.Generation;
        buffer.Clear();
        Assert.Equal(generation + 1, buffer.Generation);
    }

    // --- the codec, against the bytes R2025b wrote (probe_sp_io) -------------------------------------

    [Theory]
    [InlineData("uint8", false, new byte[] { 1, 0, 255, 3 })]
    [InlineData("int8", false, new byte[] { 1, 254, 127, 3 })]
    [InlineData("uint16", false, new byte[] { 1, 0, 0, 0, 44, 1, 3, 0 })]
    [InlineData("int16", false, new byte[] { 1, 0, 254, 255, 44, 1, 3, 0 })]
    [InlineData("int32", false, new byte[] { 1, 0, 0, 0, 254, 255, 255, 255, 44, 1, 0, 0, 3, 0, 0, 0 })]
    [InlineData("single", false, new byte[] { 0, 0, 128, 63, 0, 0, 0, 192, 0, 0, 150, 67, 0, 0, 32, 64 })]
    [InlineData("char", false, new byte[] { 1, 0, 255, 2 })]
    [InlineData("uint16", true, new byte[] { 0, 1, 0, 0, 1, 44, 0, 3 })]
    public void TheCodecWritesWhatR2025bWrites(string precision, bool bigEndian, byte[] expected) =>
        Assert.Equal(expected, PrecisionCodec.Encode([1, -2, 300, 2.5], PrecisionCodec.Parse(precision), bigEndian));

    [Fact]
    public void TheCodecSaturatesAndZeroesNaN()
    {
        Assert.Equal(new byte[] { 0, 255, 0 }, PrecisionCodec.Encode([double.NaN, double.PositiveInfinity, double.NegativeInfinity], Precision.UInt8, false));
        Assert.Equal(new byte[] { 0, 0, 255, 127, 0, 128, 255, 127 },
            PrecisionCodec.Encode([double.NaN, double.PositiveInfinity, double.NegativeInfinity, 40000], Precision.Int16, false));
        Assert.Equal(new byte[] { 1, 2, 255, 254 }, PrecisionCodec.Encode([0.5, 1.5, -0.5, -1.5], Precision.Int8, false));
    }

    [Fact]
    public void TheCodecReadsBothByteOrders()
    {
        byte[] bytes = [0, 1, 255, 254];
        Assert.Equal(new double[] { 256, 65279 }, PrecisionCodec.Decode(bytes, Precision.UInt16, bigEndian: false));
        Assert.Equal(new double[] { 1, -2 }, PrecisionCodec.Decode(bytes, Precision.Int16, bigEndian: true));
        Assert.Equal(new double[] { 1.5 }, PrecisionCodec.Decode([0, 0, 0, 0, 0, 0, 248, 63], Precision.Double, bigEndian: false));
        Assert.Equal("héllo", PrecisionCodec.Latin1([104, 233, 108, 108, 111]));
    }

    // --- the peer engine ----------------------------------------------------------------------------

    private sealed class RecordingLink : IPeerLink
    {
        public List<byte> Sent { get; } = new();

        public bool Rts { get; private set; }

        public bool Dtr { get; private set; }

        public int Breaks { get; private set; }

        public SerialPins Pins { get; set; }

        public void Send(ReadOnlySpan<byte> bytes) => Sent.AddRange(bytes.ToArray());

        public void SetRts(bool on) => Rts = on;

        public void SetDtr(bool on) => Dtr = on;

        public void SendBreak(int milliseconds) => Breaks++;

        public string Text => Encoding.ASCII.GetString(Sent.ToArray());
    }

    [Fact]
    public void ThePeerTellsDataFromCommands()
    {
        var link = new RecordingLink();
        using var engine = new PeerEngine(link);
        engine.Receive([0x1B, 0x41, 0x1B, 0x1B, 0x42]); // ESC A, then ESC ESC B: data, the held bytes and all
        engine.Receive(PeerEngine.Frame("recv"));
        Assert.Equal("00000005" + "1B411B1B42", link.Text);
        link.Sent.Clear();
        engine.Receive(PeerEngine.Frame("send 48690A"));
        Assert.Equal("Hi\n", link.Text);
        link.Sent.Clear();
        engine.Receive(PeerEngine.Frame("bogus"));
        engine.Receive(PeerEngine.Frame("send 4"));
        Assert.Equal("??", link.Text);
    }

    [Fact]
    public void ThePeerEchoesMatchesAndReportsItsPins()
    {
        var link = new RecordingLink { Pins = new SerialPins(true, false, true, false) };
        using var engine = new PeerEngine(link);
        engine.Receive(PeerEngine.Frame("echo on"));
        engine.Receive("ab"u8);
        Assert.Equal("ab", link.Text);
        engine.Receive(PeerEngine.Frame("echo off"));
        engine.Receive(PeerEngine.Frame("on 2A49444E3F 4A470A"));
        link.Sent.Clear();
        engine.Receive("*IDN?"u8);
        Assert.Equal("JG\n", link.Text);
        link.Sent.Clear();
        engine.BreakReceived();
        engine.Receive(PeerEngine.Frame("status"));
        Assert.Equal("10100001", link.Text);
        engine.Receive(PeerEngine.Frame("pins rts=1 dtr=1"));
        Assert.True(link.Rts && link.Dtr);
        engine.Receive(PeerEngine.Frame("reset"));
        Assert.False(link.Rts || link.Dtr);
    }

    [Fact]
    public void ThePeerSendsLater()
    {
        var link = new RecordingLink();
        using var engine = new PeerEngine(link);
        engine.Receive(PeerEngine.Frame("later 50 41"));
        Assert.Empty(link.Sent);
        SpinWait.SpinUntil(() => link.Sent.Count > 0, 5000);
        Assert.Equal("A", link.Text);
    }

    /// <summary>
    /// Open item 37: one timer a chunk, and timers due close together fire in no set order, so
    /// <c>chunks</c> sent its stream shuffled on a loaded machine. Two hundred chunks a millisecond
    /// apart arrive in the order asked for.
    /// </summary>
    [Fact]
    public void ThePeerSendsChunksInOrder()
    {
        var link = new RecordingLink();
        using var engine = new PeerEngine(link);
        byte[] stream = [.. Enumerable.Range(0, 200).Select(static i => (byte)i)];
        engine.Receive(PeerEngine.Frame("chunks 1 1 " + Convert.ToHexString(stream)));
        Assert.True(SpinWait.SpinUntil(() => link.Sent.Count >= stream.Length, 10000));
        Assert.Equal(stream, link.Sent.ToArray());
    }

    // --- the simulated line ---------------------------------------------------------------------------

    [Fact]
    public void TheSimulatedLineIsWiredAsCom0comIs()
    {
        using var line = new SimulatedLine("COM20");
        using SimulatedSerialPort near = line.Open(new SerialSettings());
        Assert.Throws<DeviceOpenException>(() => line.Open(new SerialSettings()));
        near.Write(PeerEngine.Frame("status"), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal("11100000", Encoding.ASCII.GetString(near.Input.TakeAll()));
        near.SetRts(false);
        near.Write(PeerEngine.Frame("status"), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal("01100000", Encoding.ASCII.GetString(near.Input.TakeAll()));
        near.Write(PeerEngine.Frame("pins rts=1 dtr=0"), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(new SerialPins(true, false, false, false), near.GetPins());
        near.Write("xy"u8, TimeSpan.FromSeconds(1), CancellationToken.None);
        near.Dispose();

        // A reopened near end finds the device as it was left, as com0com's peer is.
        using SimulatedSerialPort again = line.Open(new SerialSettings());
        again.Write(PeerEngine.Frame("recv"), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal("000000027879", Encoding.ASCII.GetString(again.Input.TakeAll()));
    }

    [Fact]
    public void PortsSortAsTheirNumbersDo()
    {
        var names = new List<string> { "COM11", "COM3", "COM1", "COM20", "COM2" };
        names.Sort(NaturalOrder.Instance);
        Assert.Equal(["COM1", "COM2", "COM3", "COM11", "COM20"], names);
    }

    [Fact]
    public void ValidatestringMatchesAsR2025bDoes()
    {
        Assert.True(DeviceChecks.Match("C", ["LF", "CR", "CR/LF"], out string? found, out _));
        Assert.Equal("CR", found);
        Assert.False(DeviceChecks.Match("uint", PrecisionCodec.Names, out _, out bool ambiguous));
        Assert.True(ambiguous);
        Assert.True(DeviceChecks.Match("ODD", ["none", "even", "odd"], out found, out _));
        Assert.Equal("odd", found);
        Assert.False(DeviceChecks.Match("", ["input", "output"], out _, out ambiguous));
        Assert.False(ambiguous);
    }

    // --- a serialport losing its device ------------------------------------------------------------------

    [Fact]
    public void AnUnpluggedPortRunsErrorOccurredFcnAndRefusesItsCalls()
    {
        string code = """
            global LOST
            LOST = {};
            jgraph.internal.devicesim('COM20');
            s = serialport("COM20", 9600, "Timeout", 1);
            s.ErrorOccurredFcn = @(e) lostlog(e);
            jgraph.internal.devicesim('unplug', 'COM20');
            pause(0.1);
            fprintf('%d|%s|%s|', numel(LOST), LOST{1}, class(LOST{2}));
            try
                read(s, 1, "uint8");
            catch e
                fprintf('%s|', e.identifier);
            end
            try
                write(s, 1, "uint8");
            catch e
                fprintf('%s', e.identifier);
            end
            function lostlog(e)
                global LOST
                LOST{end + 1} = char(e.ID);
                LOST{end + 1} = e.Message;
            end
            """;
        Assert.Equal(
            "2|serialport:serialport:ConnectionLost|string|serialport:serialport:ConnectionLost|serialport:serialport:ConnectionLost",
            Run(code));
    }

    [Fact]
    public void AnUnpluggedPortWithNoHandlerSaysSoOnTheErrorStream()
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            """
            jgraph.internal.devicesim('COM20');
            s = serialport("COM20", 9600);
            jgraph.internal.devicesim('unplug', 'COM20');
            pause(0);
            """,
            new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
        Assert.Contains("Unable to detect connection to the serialport device. Ensure that the device is plugged in and create a new serialport object.", output.ErrorText);
    }

    [Fact]
    public void ClearingThePortClosesItBeforeTheNextStatement()
    {
        Assert.Equal("1 1 1", Run("""
            jgraph.internal.devicesim('COM20');
            s = serialport("COM20", 9600);
            c = {s};
            clear s
            try, serialport("COM20", 9600); held = 0; catch, held = 1; end
            clear c
            t = serialport("COM20", 9600);
            fprintf('%d %d %d', held, isvalid(t), numel(serialportfind));
            """));
    }

    // --- com0com --------------------------------------------------------------------------------------

    /// <summary>A fact that runs only where com0com's pair COM20/COM21 is installed.</summary>
    private sealed class Com0comFactAttribute : FactAttribute
    {
        public Com0comFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !SerialPortList.All().Contains("COM20") || !SerialPortList.All().Contains("COM21"))
            {
                Skip = "com0com's pair COM20<->COM21 is not installed (device classes plan, step 0).";
            }
        }
    }

    [Com0comFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void TheWin32PortCarriesBytesPinsAndBreaksThroughCom0com()
    {
        using Win32SerialPort near = Win32SerialPort.Open("COM20", new SerialSettings { BaudRate = 115200 });
        using Win32SerialPort far = Win32SerialPort.Open("COM21", new SerialSettings { BaudRate = 115200 }, dtr: false, rts: false);
        int breaks = 0;
        far.BreakReceived += n => breaks = n;
        near.Write("hello"u8, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(far.Input.WaitForCount(5, TimeSpan.FromSeconds(2), CancellationToken.None));
        Assert.Equal("hello", Encoding.ASCII.GetString(far.Input.TakeAll()));
        Assert.Equal(5, near.BytesWritten);
        Assert.Equal(new SerialPins(false, false, false, false), near.GetPins());
        far.SetRts(true);
        far.SetDtr(true);
        SpinWait.SpinUntil(() => near.GetPins().ClearToSend, 2000);
        Assert.Equal(new SerialPins(true, true, true, false), near.GetPins());
        near.SendBreak(100, CancellationToken.None);
        SpinWait.SpinUntil(() => breaks > 0, 2000);
        Assert.Equal(1, breaks);
        Assert.Throws<DeviceOpenException>(() => Win32SerialPort.Open("COM20", new SerialSettings()));
    }

    private static string Run(string code)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
