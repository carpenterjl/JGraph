// peer-sim: the scriptable far end of a com0com pair, for R2025b's device fixtures.
//
//   peer-sim COM21 [--parent PID] [--idle SECONDS] [--on MATCH=REPLY]...
//   peer-sim tcp:PORT [same options]
//   peer-sim hislip:PORT [same options]
//
// --on adds a reply rule (both sides in hex) that the engine's reset keeps.
//
// The network forms serve the same engine on 127.0.0.1: a raw TCP socket (a VISA SOCKET resource) or a
// HiSLIP 1.0 server (a TCPIP INSTR resource); see NetworkPeers.cs.
//
// Opens the port with RTS and DTR low, prints READY on its own line, then runs the peer engine until
// it is told to quit (the in-band "quit" command), the parent process ends, the port is lost, or no
// byte has arrived for the idle time (default 120 s). A port still held by the previous fixture's peer
// is retried for ten seconds.

using System.Diagnostics;
using System.Runtime.Versioning;
using JGraph.Devices;
using JGraph.Devices.Serial;
using JGraph.Devices.Simulation;

[assembly: SupportedOSPlatform("windows")]

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: peer-sim PORT [--parent PID] [--idle SECONDS]");
    return 2;
}

string portName = args[0];
int parent = 0;
int idleSeconds = 120;
var standing = new List<(byte[] Match, byte[] Reply)>();
for (int i = 1; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--on":
        {
            // --on MATCH=REPLY, both in hex: a reply rule the engine's reset keeps.
            string[] pair = args[i + 1].Split('=');
            standing.Add((Convert.FromHexString(pair[0]), Convert.FromHexString(pair[1])));
            break;
        }

        case "--parent":
            parent = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--idle":
            idleSeconds = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            break;
    }
}

if (portName.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) || portName.StartsWith("hislip:", StringComparison.OrdinalIgnoreCase))
{
    int tcpPort = int.Parse(portName[(portName.IndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    IDisposable server;
    PeerEngine peerEngine;
    Func<long> activity;
    try
    {
        if (portName.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            var tcp = new TcpPeer(tcpPort);
            (server, peerEngine, activity) = (tcp, tcp.Engine, () => Interlocked.Read(ref tcp.LastActivity));
        }
        else
        {
            var hislip = new HislipPeer(tcpPort);
            (server, peerEngine, activity) = (hislip, hislip.Engine, () => Interlocked.Read(ref hislip.LastActivity));
        }
    }
    catch (System.Net.Sockets.SocketException e)
    {
        Console.Error.WriteLine($"peer-sim: {e.Message}");
        return 1;
    }

    foreach ((byte[] match, byte[] reply) in standing)
    {
        peerEngine.AddStandingRule(match, reply);
    }

    using var quit = new ManualResetEventSlim();
    peerEngine.QuitRequested += quit.Set;
    long started = Environment.TickCount64;
    Console.Out.WriteLine("READY");
    Console.Out.Flush();
    Process? owner = null;
    if (parent > 0)
    {
        try
        {
            owner = Process.GetProcessById(parent);
        }
        catch (ArgumentException)
        {
            server.Dispose();
            return 0;
        }
    }

    while (!quit.Wait(200))
    {
        if (owner is not null && owner.HasExited)
        {
            break;
        }

        if (Environment.TickCount64 - Math.Max(started, activity()) > idleSeconds * 1000L)
        {
            break;
        }
    }

    server.Dispose();
    return 0;
}

Win32SerialPort? port = null;
var opening = Stopwatch.StartNew();
while (port is null)
{
    try
    {
        port = Win32SerialPort.Open(portName, new SerialSettings { BaudRate = 115200 }, dtr: false, rts: false);
    }
    catch (DeviceOpenException e) when (opening.Elapsed < TimeSpan.FromSeconds(10))
    {
        _ = e;
        Thread.Sleep(100);
    }
    catch (DeviceOpenException e)
    {
        Console.Error.WriteLine($"peer-sim: {e.Message}");
        return 1;
    }
}

using var done = new ManualResetEventSlim();
long lastActivity = Environment.TickCount64;
var link = new PortLink(port);
using var engine = new PeerEngine(link);
foreach ((byte[] match, byte[] reply) in standing)
{
    engine.AddStandingRule(match, reply);
}

engine.QuitRequested += done.Set;
port.Input.Appended += (_, _) =>
{
    Interlocked.Exchange(ref lastActivity, Environment.TickCount64);
    engine.Receive(port.Input.TakeAll());
};
port.BreakReceived += _ => engine.BreakReceived();
port.ConnectionLost += _ => done.Set();

Console.Out.WriteLine("READY");
Console.Out.Flush();

Process? parentProcess = null;
if (parent > 0)
{
    try
    {
        parentProcess = Process.GetProcessById(parent);
    }
    catch (ArgumentException)
    {
        return 0;
    }
}

while (!done.Wait(200))
{
    if (parentProcess is not null && parentProcess.HasExited)
    {
        break;
    }

    if (Environment.TickCount64 - Interlocked.Read(ref lastActivity) > idleSeconds * 1000L)
    {
        break;
    }
}

port.Dispose();
return 0;

/// <summary>The engine's view of the far port.</summary>
internal sealed class PortLink(Win32SerialPort port) : IPeerLink
{
    public void Send(ReadOnlySpan<byte> bytes) => port.Write(bytes, TimeSpan.FromSeconds(5), CancellationToken.None);

    public void SetRts(bool on) => port.SetRts(on);

    public void SetDtr(bool on) => port.SetDtr(on);

    public SerialPins Pins => port.GetPins();

    public void SendBreak(int milliseconds) => port.SendBreak(milliseconds, CancellationToken.None);
}
