using System.Net;
using System.Net.Sockets;

namespace JGraph.Devices.Network;

/// <summary>
/// The loopback echo servers <c>echotcpip</c> and <c>echoudp</c> start: every byte a TCP client sends
/// comes back to it, every datagram goes back to its sender. Each is one dual-stack socket on every
/// interface, which is how R2025b's behaves (probe_net_echo2, probe_net_ports): it starts on a port an
/// IPv4 socket holds, an IPv4 udpport or tcpserver may take its port after it, and the IPv4 socket
/// then gets the port's IPv4 traffic.
/// </summary>
public sealed class EchoServer : IDisposable
{
    private readonly Socket _socket;
    private readonly Thread _thread;
    private readonly List<Socket> _clients = new();
    private volatile bool _closing;

    private EchoServer(Socket socket, bool tcp)
    {
        _socket = socket;
        Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        _thread = new Thread(tcp ? AcceptLoop : UdpLoop) { IsBackground = true, Name = tcp ? "JGraph echotcpip" : "JGraph echoudp" };
        _thread.Start();
    }

    public int Port { get; }

    /// <summary>Starts a TCP echo server on every interface at <paramref name="port"/>.</summary>
    public static EchoServer Tcp(int port)
    {
        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            socket.Listen(16);
        }
        catch (SocketException e)
        {
            socket.Dispose();
            throw new DeviceOpenException(e.Message, e.ErrorCode);
        }

        return new EchoServer(socket, tcp: true);
    }

    /// <summary>Starts a UDP echo server on every interface at <paramref name="port"/>.</summary>
    public static EchoServer Udp(int port)
    {
        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            if (OperatingSystem.IsWindows())
            {
                socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            }

            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        }
        catch (SocketException e)
        {
            socket.Dispose();
            throw new DeviceOpenException(e.Message, e.ErrorCode);
        }

        return new EchoServer(socket, tcp: false);
    }

    private void AcceptLoop()
    {
        try
        {
            while (!_closing)
            {
                Socket client = _socket.Accept();
                lock (_clients)
                {
                    _clients.Add(client);
                }

                new Thread(() => Echo(client)) { IsBackground = true, Name = "JGraph echotcpip client" }.Start();
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
    }

    private void Echo(Socket client)
    {
        var buffer = new byte[65536];
        try
        {
            while (!_closing)
            {
                int n = client.Receive(buffer);
                if (n == 0)
                {
                    break;
                }

                client.Send(buffer.AsSpan(0, n));
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }

        lock (_clients)
        {
            _clients.Remove(client);
        }

        client.Dispose();
    }

    private void UdpLoop()
    {
        var buffer = new byte[65536];
        EndPoint from = new IPEndPoint(IPAddress.IPv6Any, 0);
        try
        {
            while (!_closing)
            {
                int n = _socket.ReceiveFrom(buffer, ref from);
                _socket.SendTo(buffer.AsSpan(0, n), SocketFlags.None, from);
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _closing = true;
        _socket.Dispose();
        lock (_clients)
        {
            foreach (Socket client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
        }

        _thread.Join(1000);
    }
}
