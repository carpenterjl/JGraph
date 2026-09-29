// peer-sim: the scriptable far end of a com0com pair, for R2025b's device fixtures.
//
//   peer-sim COM21 [--parent PID] [--idle SECONDS]
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
for (int i = 1; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--parent":
            parent = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--idle":
            idleSeconds = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            break;
    }
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
