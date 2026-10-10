using System.Net;
using System.Net.Sockets;
using JGraph.Devices.Network;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// The network transports and their script objects on the loopback (device classes plan, stage D2):
/// the echo servers, a TCP server that stops listening while it has a client, datagrams kept whole
/// and split by OutputDatagramSize, the echo servers ending with the run, a cleared tcpclient closing
/// its connection before the next statement, and the datagram callback's count. Every port is one
/// the OS hands out, so these run beside the lanes' other processes.
/// </summary>
[Collection("JG facade")]
public class NetworkLayerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static int FreePort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    [Fact]
    public void TheTcpEchoServerSendsBackWhatItGets()
    {
        int port = FreePort();
        using EchoServer echo = EchoServer.Tcp(port);
        using TcpTransport client = TcpTransport.Connect("127.0.0.1", port, Wait, transferDelay: true, default);
        client.Write([1, 2, 3, 250], Wait, default);
        Assert.True(client.Input.WaitForCount(4, Wait, default));
        Assert.Equal(new byte[] { 1, 2, 3, 250 }, client.Input.TakeAll());
    }

    [Fact]
    public void TheServerRefusesASecondClientUntilTheFirstGoes()
    {
        int port = FreePort();
        using TcpServerTransport server = TcpServerTransport.Listen("127.0.0.1", port);
        var changes = new List<bool>();
        server.ConnectionChanged += (connected, _, _) =>
        {
            lock (changes)
            {
                changes.Add(connected);
            }
        };

        TcpTransport first = TcpTransport.Connect("127.0.0.1", port, Wait, transferDelay: true, default);
        Assert.True(SpinWait.SpinUntil(() => server.Connected, Wait));
        Assert.ThrowsAny<Exception>(() => TcpTransport.Connect("127.0.0.1", port, TimeSpan.FromSeconds(3), transferDelay: true, default).Dispose());

        first.Dispose();
        Assert.True(SpinWait.SpinUntil(() => !server.Connected, Wait));
        using TcpTransport second = TcpTransport.Connect("127.0.0.1", port, Wait, transferDelay: true, default);
        Assert.True(SpinWait.SpinUntil(() => server.Connected, Wait));
        second.Write([7], Wait, default);
        Assert.True(server.Input.WaitForCount(1, Wait, default));
        Assert.Equal(new byte[] { 7 }, server.Input.TakeAll());
        lock (changes)
        {
            Assert.Equal(new[] { true, false, true }, changes);
        }
    }

    [Fact]
    public void DatagramsStayWholeAndAWriteSplitsByTheOutputSize()
    {
        using UdpTransport receiver = UdpTransport.Open(false, "127.0.0.1", 0, portSharing: false, datagramMode: true);
        using UdpTransport sender = UdpTransport.Open(false, "127.0.0.1", 0, portSharing: false, datagramMode: false);
        sender.OutputDatagramSize = 2;
        sender.SendTo([1, 2, 3, 4, 5], new IPEndPoint(IPAddress.Loopback, receiver.LocalPort));
        Assert.True(receiver.WaitForDatagrams(3, Wait, default));
        IReadOnlyList<ReceivedDatagram> got = receiver.TakeDatagrams(3);
        Assert.Equal(new[] { 2, 2, 1 }, got.Select(static d => d.Data.Length));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, got.SelectMany(static d => d.Data));
        Assert.All(got, d => Assert.Equal(sender.LocalPort, d.SenderPort));
        Assert.All(got, static d => Assert.Equal("127.0.0.1", d.SenderAddress));
    }

    [Fact]
    public void TwoPlainSocketsDoNotShareAPortButTwoSharingOnesDo()
    {
        using UdpTransport first = UdpTransport.Open(false, "", 0, portSharing: false, datagramMode: false);
        Assert.ThrowsAny<Exception>(() => UdpTransport.Open(false, "", first.LocalPort, portSharing: false, datagramMode: false).Dispose());

        using UdpTransport shared = UdpTransport.Open(false, "", 0, portSharing: true, datagramMode: false);
        using UdpTransport alsoShared = UdpTransport.Open(false, "", shared.LocalPort, portSharing: true, datagramMode: false);
        Assert.Equal(shared.LocalPort, alsoShared.LocalPort);
    }

    [Fact]
    public void TheEchoServersEndWithTheRun()
    {
        int port = FreePort();
        Run($"echotcpip(\"on\", {port});");
        Assert.ThrowsAny<Exception>(() => TcpTransport.Connect("127.0.0.1", port, TimeSpan.FromSeconds(3), transferDelay: true, default).Dispose());
    }

    [Fact]
    public void ClearingATcpclientClosesItBeforeTheNextStatement()
    {
        int port = FreePort();
        Assert.Equal("1 0", Run($"""
            srv = tcpserver("127.0.0.1", {port});
            t = tcpclient("127.0.0.1", {port});
            pause(0.3);
            before = srv.Connected;
            clear t
            pause(0.3);
            fprintf('%d %d', before, srv.Connected);
            """));
    }

    [Fact]
    public void TheDatagramCallbackCountsWhatWasWaiting()
    {
        int port = FreePort();
        Assert.Equal("0 2", Run($"""
            global N
            N = 0;
            echoudp("on", {port});
            d = udpport("datagram");
            for k = 1:3, write(d, k, "uint8", "127.0.0.1", {port}); end
            pause(0.5);
            configureCallback(d, "datagram", 2, @(s, e) bump());
            pause(0.3);
            atSet = N;
            write(d, 9, "uint8");
            pause(0.5);
            fprintf('%d %d', atSet, N);
            function bump()
                global N
                N = N + 1;
            end
            """));
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
