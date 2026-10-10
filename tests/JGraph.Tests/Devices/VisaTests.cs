using JGraph.Devices;
using JGraph.Devices.Simulation;
using JGraph.Devices.Visa;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// visadev without an instrument (device classes plan, stage D4): the simulated VISA's resource names
/// and the read rules NI-VISA was measured to keep for each kind of session, and the scripting object
/// against it (<c>jgraph.internal.visasim</c>): the list, identification, synchronous reads, the
/// terminator a failed read restores (div=ADR0187), status, device clear, and the enumerations.
/// </summary>
[Collection("JG facade")]
public class VisaTests
{
    private static SimulatedVisa Visa(SimulatedLine line) =>
        new(name => name.Equals("COM20", StringComparison.OrdinalIgnoreCase) ? line : null, () => ["COM20", "COM21"], name => name == "COM21");

    [Theory]
    [InlineData("ASRL20::INSTR", 4, 20, "INSTR", "ASRL20::INSTR", "COM20")]
    [InlineData("asrl20::instr", 4, 20, "INSTR", "ASRL20::INSTR", "COM20")]
    [InlineData("COM20", 4, 20, "INSTR", "ASRL20::INSTR", "COM20")]
    [InlineData("TCPIP::127.0.0.1::hislip0::INSTR", 6, 0, "INSTR", "TCPIP0::127.0.0.1::hislip0::INSTR", "")]
    [InlineData("TCPIP0::127.0.0.1::5025::SOCKET", 6, 0, "SOCKET", "TCPIP0::127.0.0.1::5025::SOCKET", "")]
    [InlineData("TCPIP1::10.0.0.5", 6, 1, "INSTR", "TCPIP1::10.0.0.5::inst0::INSTR", "")]
    [InlineData("GPIB0::5::INSTR", 1, 0, "INSTR", "GPIB0::5::INSTR", "")]
    public void TheSimulatedVisaParsesResourceNames(string name, int type, int board, string cls, string expanded, string alias)
    {
        using var line = new SimulatedLine("COM20");
        VisaParsedName parsed = Visa(line).Parse(name);
        Assert.Equal((ushort)type, parsed.InterfaceType);
        Assert.Equal((ushort)board, parsed.InterfaceNumber);
        Assert.Equal(cls, parsed.ResourceClass);
        Assert.Equal(expanded, parsed.ExpandedName);
        Assert.Equal(alias, parsed.Alias);
    }

    [Fact]
    public void BadNamesMissingResourcesAndThePeersPortAreRefused()
    {
        using var line = new SimulatedLine("COM20");
        SimulatedVisa visa = Visa(line);
        Assert.Equal(VisaStatus.ErrorInvalidResourceName, Assert.Throws<VisaException>(() => visa.Parse("NOPE")).Status);
        Assert.Equal(VisaStatus.ErrorResourceNotFound, Assert.Throws<VisaException>(() => visa.Open("ASRL99::INSTR", 0)).Status);
        Assert.Equal(VisaStatus.ErrorResourceBusy, Assert.Throws<VisaException>(() => visa.Open("ASRL21::INSTR", 0)).Status);
        Assert.Equal(VisaStatus.ErrorResourceNotFound, Assert.Throws<VisaException>(() => visa.Open("TCPIP0::127.0.0.1::1::SOCKET", 0)).Status);
        Assert.Equal(["ASRL20::INSTR", "ASRL21::INSTR"], visa.Find("?*::INSTR"));
    }

    [Fact]
    public void ASerialReadEndsAtItsCountItsTerminatorOrItsTimeout()
    {
        using var line = new SimulatedLine("COM20");
        using IVisaSession session = Visa(line).Open("ASRL20::INSTR", 0);
        session.SetAttribute(VisaAttribute.TimeoutValue, 200);
        session.SetAttribute(VisaAttribute.TermCharEnabled, 1);
        line.Engine.Receive(PeerEngine.Frame("send 68690A6162"));

        VisaReadResult lineRead = session.Read(1024, default);
        Assert.Equal("hi\n"u8.ToArray(), lineRead.Data);
        Assert.Equal(VisaStatus.SuccessTermChar, lineRead.Status);

        // With END_IN off the character is only data: the read waits for its count and times out with what came.
        session.SetAttribute(VisaAttribute.AsrlEndIn, 0);
        VisaReadResult partial = session.Read(5, default);
        Assert.True(partial.TimedOut);
        Assert.Equal("ab"u8.ToArray(), partial.Data);

        line.Engine.Receive(PeerEngine.Frame("send 0102030405"));
        VisaReadResult counted = session.Read(3, default);
        Assert.Equal(VisaStatus.SuccessMaxCount, counted.Status);
        Assert.Equal(new byte[] { 1, 2, 3 }, counted.Data);
    }

    [Fact]
    public void SerialSettingsFollowNiVisasStopBitRules()
    {
        using var line = new SimulatedLine("COM20");
        using IVisaSession session = Visa(line).Open("ASRL20::INSTR", 0);
        Assert.Equal(VisaStatus.ErrorInvalidSetup, Assert.Throws<VisaException>(() => session.SetAttribute(VisaAttribute.AsrlStopBits, 15)).Status);
        session.SetAttribute(VisaAttribute.AsrlStopBits, 20);
        Assert.Equal(VisaStatus.ErrorInvalidSetup, Assert.Throws<VisaException>(() => session.SetAttribute(VisaAttribute.AsrlDataBits, 5)).Status);
        session.SetAttribute(VisaAttribute.AsrlStopBits, 10);
        session.SetAttribute(VisaAttribute.AsrlDataBits, 5);
        session.SetAttribute(VisaAttribute.AsrlStopBits, 15);
        Assert.Equal(15UL, session.GetAttribute(VisaAttribute.AsrlStopBits));
    }

    [Fact]
    public void AMessageReadEndsAtEndAndANewMessageDropsOldAnswers()
    {
        using var line = new SimulatedLine("COM20");
        SimulatedVisa visa = Visa(line);
        using IVisaSession session = visa.Open(SimulatedVisa.HislipName, 0);
        session.SetAttribute(VisaAttribute.TimeoutValue, 200);
        session.Write(PeerEngine.Frame("send 61620A6364"), default);
        VisaReadResult whole = session.Read(1024, default);
        Assert.Equal("ab\ncd"u8.ToArray(), whole.Data);
        Assert.Equal(VisaStatus.Success, whole.Status);

        // An answer left unread is dropped when the next message goes out, as HiSLIP's synchronized mode does.
        session.Write(PeerEngine.Frame("send 0102"), default);
        session.Write(PeerEngine.Frame("later 50 0304"), default);

        // The read ends at the message's end, so a long timeout costs nothing unless the machine is
        // loaded enough to hold the peer's 50 ms timer past 200 ms, which the lanes have seen.
        session.SetAttribute(VisaAttribute.TimeoutValue, 5000);
        VisaReadResult after = session.Read(10, default);
        Assert.Equal(new byte[] { 3, 4 }, after.Data);

        session.Write("*IDN?\n"u8, default);
        Assert.Equal(SimulatedVisa.IdnReply, session.Read(1024, default).Data);
        visa.Hislip.StatusByte = 0x41;
        Assert.Equal(0x41, session.ReadStatusByte());
    }

    [Fact]
    public void ASocketReadEndsWhenNothingMoreIsWaiting()
    {
        using var line = new SimulatedLine("COM20");
        using IVisaSession session = Visa(line).Open(SimulatedVisa.SocketName, 0);
        session.SetAttribute(VisaAttribute.TimeoutValue, 200);
        session.SetAttribute(VisaAttribute.TermCharEnabled, 1);
        session.Write(PeerEngine.Frame("send 61620A6364"), default);
        Assert.Equal("ab\n"u8.ToArray(), session.Read(1024, default).Data);
        VisaReadResult rest = session.Read(1024, default);
        Assert.Equal("cd"u8.ToArray(), rest.Data);
        Assert.False(rest.TimedOut);
        Assert.True(session.Read(1024, default).TimedOut);
        Assert.Equal(VisaStatus.ErrorInvalidSetup, Assert.Throws<VisaException>(() => session.ReadStatusByte()).Status);
    }

    [Fact]
    public void TheListAndTheObjectsSayWhatR2025bSays()
    {
        Assert.Equal("ASRL20::INSTR|COM20|serial|visalib.Serial|JGraph PeerSim SN0001|visalib.TCPIP|{off,LF}|visalib.Socket|", Run("""
            jgraph.internal.devicesim('COM20');
            jgraph.internal.visasim('on');
            L = visadevlist("Timeout", 2);
            fprintf('%s|%s|%s|', L.ResourceName, L.Alias, L.Type);
            v = visadev("COM20");
            fprintf('%s|', class(v));
            clear v
            h = visadev("TCPIP0::127.0.0.1::hislip0::INSTR");
            fprintf('%s %s %s|%s|', h.Vendor, h.Model, h.SerialNumber, class(h));
            t = h.Terminator;
            fprintf('{%s,%s}|', t{1}, t{2});
            clear h
            s = visadev("TCPIP0::127.0.0.1::5025::SOCKET");
            fprintf('%s|', class(s));
            """));
    }

    [Fact]
    public void SynchronousReadsWarnAfterTwiceTheTimeoutOrFail()
    {
        Assert.Equal("hi|0|transportlib:client:ReadWarning|instrument:interface:visa:operationTimedOut|ok|", Run("""
            jgraph.internal.devicesim('COM20');
            jgraph.internal.visasim('on');
            v = visadev("ASRL20::INSTR");
            v.Timeout = 0.2;
            dpw = @(cmd) write(v, [27 27 double('{') double(cmd) double('}')], "uint8");
            dpw('send 68690A');
            pause(0.1);
            fprintf('%s|', readline(v));
            lastwarn('');
            t0 = tic;
            x = read(v, 3);
            [~, id] = lastwarn;
            fprintf('%d|%s|', numel(x), id);
            dpw('send 0102');
            pause(0.1);
            try, read(v, 5); catch e, fprintf('%s|', e.identifier); end
            % the termination character comes back after the failed read (div=ADR0187)
            dpw('send 6F6B0A');
            pause(0.1);
            fprintf('%s|', readline(v));
            """));
    }

    [Fact]
    public void StatusClearAndTheEnumerationsWork()
    {
        Assert.Equal("65|0|00000001|1|1|tcpip|on|1|Undefined function 'visatrigger' for input arguments of type 'visalib.TCPIP'.|", Run("""
            jgraph.internal.devicesim('COM20');
            jgraph.internal.visasim('on');
            h = visadev("TCPIP0::127.0.0.1::hislip0::INSTR");
            h.Timeout = 1;
            dpw = @(cmd) write(h, [27 27 double('{') double(cmd) double('}')], "uint8");
            dpw('stb 41');
            [ready, status] = visastatus(h);
            fprintf('%d|%d|', status, ready);
            dpw('reset');
            flush(h);
            dpw('inst');
            fprintf('%s|', read(h, 8, "char"));
            fprintf('%d|%d|%s|%s|', h.Type == "tcpip", isequal(h.EOIMode, "on"), char(h.Type), char(h.EOIMode));
            fprintf('%d|', logical(h.EOIMode));
            try, visatrigger(h); catch e, fprintf('%s|', e.message); end
            """));
    }

    [Fact]
    public void TheInstalledVisaListsAndParsesWhenThereIsOne()
    {
        IVisaBackend? visa = VisaBackends.Installed(out _);
        if (visa is null)
        {
            return;
        }

        Assert.False(string.IsNullOrEmpty(visa.PreferredVisa));
        Assert.NotNull(visa.Find("?*::INSTR"));
        VisaParsedName parsed = visa.Parse("TCPIP::127.0.0.1::5025::SOCKET");
        Assert.Equal("SOCKET", parsed.ResourceClass);
        // NI-VISA takes a name it cannot parse as an alias it has not got.
        Assert.Contains(Assert.Throws<VisaException>(() => visa.Parse("NOPE")).Status,
            new[] { VisaStatus.ErrorInvalidResourceName, VisaStatus.ErrorResourceNotFound });
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
