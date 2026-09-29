using System.Net;
using System.Net.Sockets;

namespace JGraph.Devices.Network;

/// <summary>
/// A TCP connection to a server (<c>tcpclient</c>): one reader thread fills <see cref="Input"/>; a
/// write sends every byte or throws; the far end closing, or the connection failing, ends it once
/// (<see cref="ConnectionLost"/>).
/// </summary>
public sealed class TcpTransport : IDeviceTransport
{
    private readonly Socket _socket;
    private readonly Thread _reader;
    private volatile bool _closing;
    private int _lost;
    private long _written;

    private TcpTransport(Socket socket, string host, int port)
    {
        _socket = socket;
        RemoteHost = host;
        RemotePort = port;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"JGraph tcp reader {host}:{port}" };
        _reader.Start();
    }

    /// <summary>Connects to <paramref name="host"/>:<paramref name="port"/> within <paramref name="connectTimeout"/>.</summary>
    public static TcpTransport Connect(string host, int port, TimeSpan connectTimeout, bool transferDelay, CancellationToken cancel)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = !transferDelay };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(connectTimeout);
            try
            {
                socket.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw new DeviceOpenException($"The connection to {host}:{port} timed out.", 10060);
            }
            catch (SocketException e)
            {
                throw new DeviceOpenException(e.Message, e.ErrorCode);
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new TcpTransport(socket, host, port);
    }

    /// <summary>Wraps a connection a server accepted.</summary>
    internal static TcpTransport Accepted(Socket socket)
    {
        var remote = (IPEndPoint)socket.RemoteEndPoint!;
        return new TcpTransport(socket, remote.Address.ToString(), remote.Port);
    }

    public string RemoteHost { get; }

    public int RemotePort { get; }

    /// <summary>The local end's port.</summary>
    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    /// <summary>Whether Nagle's algorithm delays small writes (MATLAB's EnableTransferDelay).</summary>
    public bool TransferDelay => !_socket.NoDelay;

    public string Name => $"{RemoteHost}:{RemotePort}";

    public bool Connected => !_closing && Volatile.Read(ref _lost) == 0;

    public InputBuffer Input { get; } = new();

    public long BytesWritten => Interlocked.Read(ref _written);

    public event Action<DeviceConnectionLostException>? ConnectionLost;

    public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel)
    {
        if (!Connected)
        {
            throw new DeviceConnectionLostException($"The connection to {Name} is closed.");
        }

        try
        {
            _socket.SendTimeout = (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);
            int sent = 0;
            while (sent < data.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int n = _socket.Send(data[sent..]);
                sent += n;
                Interlocked.Add(ref _written, n);
            }
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
        {
            throw new TimeoutException($"The write to {Name} did not finish in time.");
        }
        catch (SocketException e)
        {
            Lose(e.Message, e.ErrorCode);
            throw new DeviceConnectionLostException(e.Message, e.ErrorCode);
        }
    }

    public void FlushInput() => Input.Clear();

    public void FlushOutput()
    {
    }

    private void ReadLoop()
    {
        var buffer = new byte[65536];
        try
        {
            while (!_closing)
            {
                int n = _socket.Receive(buffer);
                if (n == 0)
                {
                    Lose("The server closed the connection.", 0);
                    return;
                }

                Input.Append(buffer.AsSpan(0, n));
            }
        }
        catch (SocketException e)
        {
            if (!_closing)
            {
                Lose(e.Message, e.ErrorCode);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Lose(string why, int code)
    {
        if (_closing || Interlocked.Exchange(ref _lost, 1) != 0)
        {
            return;
        }

        var lost = new DeviceConnectionLostException(why, code);
        Input.SetFault(lost);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ConnectionLost?.Invoke(lost);
            }
            catch (Exception)
            {
            }
        });
    }

    public void Dispose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        _socket.Dispose();
        _reader.Join(1000);
    }
}

/// <summary>
/// A TCP server that takes one client at a time (<c>tcpserver</c>): the listener runs on its own
/// thread, a connection's bytes go to one <see cref="Input"/> that outlives connections, and a
/// connection coming or going raises <see cref="ConnectionChanged"/>. While a client is connected the
/// server does not listen, so a second client is refused as R2025b refuses it (probe_net_dgcb); it
/// listens again on the same endpoint when the client goes.
/// </summary>
public sealed class TcpServerTransport : IDeviceTransport
{
    private readonly IPEndPoint _endpoint;
    private readonly Thread _acceptor;
    private readonly object _gate = new();
    private Socket? _listener;
    private Socket? _client;
    private volatile bool _closing;
    private long _written;

    private TcpServerTransport(Socket listener)
    {
        _listener = listener;
        _endpoint = (IPEndPoint)listener.LocalEndPoint!;
        _acceptor = new Thread(AcceptLoop) { IsBackground = true, Name = "JGraph tcp server" };
        _acceptor.Start();
    }

    /// <summary>Listens on <paramref name="address"/> (every interface when null) and <paramref name="port"/>.</summary>
    public static TcpServerTransport Listen(string? address, int port)
    {
        IPAddress bind = address is null || address.Length == 0 ? IPAddress.Any
            : IPAddress.TryParse(address, out IPAddress? parsed) ? parsed
            : Dns.GetHostAddresses(address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
              ?? throw new DeviceOpenException($"The address {address} could not be resolved.");
        try
        {
            return new TcpServerTransport(OpenListener(new IPEndPoint(bind, port), reuse: false));
        }
        catch (SocketException e)
        {
            throw new DeviceOpenException(e.Message, e.ErrorCode);
        }
    }

    private static Socket OpenListener(IPEndPoint endpoint, bool reuse)
    {
        var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            if (reuse)
            {
                // The connection that just ended may hold the port in TIME_WAIT.
                listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            listener.Bind(endpoint);
            listener.Listen(1);
            return listener;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    public string ServerAddress => _endpoint.Address.ToString();

    public int ServerPort => _endpoint.Port;

    public string Name => $"{ServerAddress}:{ServerPort}";

    /// <summary>Whether a client is connected now.</summary>
    public bool Connected
    {
        get
        {
            lock (_gate)
            {
                return _client is not null;
            }
        }
    }

    /// <summary>The connected client's address and port, or ("", 0).</summary>
    public (string Address, int Port) Client
    {
        get
        {
            lock (_gate)
            {
                return _client?.RemoteEndPoint is IPEndPoint remote ? (remote.Address.ToString(), remote.Port) : ("", 0);
            }
        }
    }

    public InputBuffer Input { get; } = new();

    public long BytesWritten => Interlocked.Read(ref _written);

    /// <summary>Raised on a background thread when a client connects (true) or goes (false), with its address and port.</summary>
    public event Action<bool, string, int>? ConnectionChanged;

    public event Action<DeviceConnectionLostException>? ConnectionLost;

    public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel)
    {
        Socket client;
        lock (_gate)
        {
            client = _client ?? throw new DeviceConnectionLostException("No client is connected.");
        }

        try
        {
            client.SendTimeout = (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);
            int sent = 0;
            while (sent < data.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int n = client.Send(data[sent..]);
                sent += n;
                Interlocked.Add(ref _written, n);
            }
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
        {
            throw new TimeoutException("The write did not finish in time.");
        }
        catch (SocketException e)
        {
            throw new DeviceIOException(e.Message, e.ErrorCode);
        }
    }

    public void FlushInput() => Input.Clear();

    public void FlushOutput()
    {
    }

    /// <summary>Accepts a client, stops listening, reads it until it goes, listens again; until disposed.</summary>
    private void AcceptLoop()
    {
        while (!_closing)
        {
            Socket? listener;
            lock (_gate)
            {
                listener = _listener;
            }

            listener ??= Reopen();
            if (listener is null)
            {
                return;
            }

            Socket accepted;
            try
            {
                accepted = listener.Accept();
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_gate)
            {
                if (_closing)
                {
                    accepted.Dispose();
                    return;
                }

                _listener = null;
                _client = accepted;
            }

            listener.Dispose();
            var remote = (IPEndPoint)accepted.RemoteEndPoint!;
            Raise(true, remote);
            ReadLoop(accepted, remote);
        }
    }

    /// <summary>Listens again on the endpoint; null once the server is closing or the port cannot be had.</summary>
    private Socket? Reopen()
    {
        for (int attempt = 0; attempt < 50 && !_closing; attempt++)
        {
            try
            {
                Socket listener = OpenListener(_endpoint, reuse: true);
                lock (_gate)
                {
                    if (_closing)
                    {
                        listener.Dispose();
                        return null;
                    }

                    _listener = listener;
                    return listener;
                }
            }
            catch (SocketException)
            {
                Thread.Sleep(100);
            }
        }

        return null;
    }

    private void ReadLoop(Socket client, IPEndPoint remote)
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

                Input.Append(buffer.AsSpan(0, n));
            }
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        lock (_gate)
        {
            if (ReferenceEquals(_client, client))
            {
                _client = null;
            }
        }

        client.Dispose();
        if (!_closing)
        {
            Raise(false, remote);
        }
    }

    private void Raise(bool connected, IPEndPoint remote)
    {
        try
        {
            ConnectionChanged?.Invoke(connected, remote.Address.ToString(), remote.Port);
        }
        catch (Exception)
        {
            // A listener thread must not end the process.
        }
    }

    public void Dispose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        lock (_gate)
        {
            _listener?.Dispose();
            _listener = null;
            _client?.Dispose();
            _client = null;
        }

        _acceptor.Join(1000);
        _ = ConnectionLost;
    }
}

/// <summary>One datagram received: its bytes and who sent it.</summary>
public sealed record ReceivedDatagram(byte[] Data, string SenderAddress, int SenderPort);

/// <summary>
/// A UDP socket (<c>udpport</c>): bound to a local address and port, sending to any destination.
/// In byte mode each datagram's bytes go to <see cref="Input"/>; in datagram mode each is kept whole
/// in its queue (<see cref="TakeDatagrams"/>).
/// </summary>
public sealed class UdpTransport : IDeviceTransport
{
    private readonly Socket _socket;
    private readonly Thread _reader;
    private readonly object _gate = new();
    private readonly Queue<ReceivedDatagram> _datagrams = new();
    private volatile bool _closing;
    private long _written;

    private UdpTransport(Socket socket, bool datagramMode)
    {
        _socket = socket;
        DatagramMode = datagramMode;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "JGraph udp reader" };
        _reader.Start();
    }

    /// <summary>Opens a UDP socket on <paramref name="localHost"/> (every interface when empty) and <paramref name="localPort"/> (any when 0).</summary>
    public static UdpTransport Open(bool ipv6, string localHost, int localPort, bool portSharing, bool datagramMode)
    {
        AddressFamily family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        IPAddress bind = localHost.Length == 0 ? (ipv6 ? IPAddress.IPv6Any : IPAddress.Any)
            : IPAddress.TryParse(localHost, out IPAddress? parsed) ? parsed
            : Dns.GetHostAddresses(localHost).FirstOrDefault(a => a.AddressFamily == family)
              ?? throw new DeviceOpenException($"The address {localHost} could not be resolved.");
        var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Two sockets share a port only when both ask (probe_net_ports); a plain bind is refused
            // where another holds the port, and nothing is exclusive, so an echo server's dual-stack
            // socket and this one may hold the same port.
            if (portSharing)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            // A datagram to a port nobody listens on must not end the socket (Windows' ICMP reset).
            const int SIO_UDP_CONNRESET = -1744830452;
            socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            socket.Bind(new IPEndPoint(bind, localPort));
        }
        catch (SocketException e)
        {
            socket.Dispose();
            throw new DeviceOpenException(e.Message, e.ErrorCode);
        }

        return new UdpTransport(socket, datagramMode);
    }

    public bool DatagramMode { get; }

    public string LocalHost => ((IPEndPoint)_socket.LocalEndPoint!).Address.ToString();

    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public string Name => $"{LocalHost}:{LocalPort}";

    public bool Connected => !_closing;

    public InputBuffer Input { get; } = new();

    public long BytesWritten => Interlocked.Read(ref _written);

    /// <summary>The largest datagram a write sends; longer data goes as several.</summary>
    public int OutputDatagramSize { get; set; } = 512;

    public bool EnableBroadcast
    {
        get => _socket.EnableBroadcast;
        set => _socket.EnableBroadcast = value;
    }

    public bool MulticastLoopback
    {
        get => _socket.MulticastLoopback;
        set => _socket.MulticastLoopback = value;
    }

    /// <summary>The destination a write without one goes to, once a write has named it.</summary>
    public IPEndPoint? RemoteEndpoint { get; set; }

    /// <summary>Raised on the reader thread per datagram in datagram mode, with the count now waiting.</summary>
    public event Action<int>? DatagramReceived;

    public event Action<DeviceConnectionLostException>? ConnectionLost;

    /// <summary>How many datagrams are waiting (datagram mode).</summary>
    public int DatagramCount
    {
        get
        {
            lock (_gate)
            {
                return _datagrams.Count;
            }
        }
    }

    /// <summary>Takes up to <paramref name="count"/> datagrams.</summary>
    public IReadOnlyList<ReceivedDatagram> TakeDatagrams(int count)
    {
        lock (_gate)
        {
            var taken = new List<ReceivedDatagram>();
            while (taken.Count < count && _datagrams.TryDequeue(out ReceivedDatagram? next))
            {
                taken.Add(next);
            }

            return taken;
        }
    }

    /// <summary>Waits until <paramref name="count"/> datagrams are waiting.</summary>
    public bool WaitForDatagrams(int count, TimeSpan timeout, CancellationToken cancel) =>
        Input.WaitUntil(() => DatagramCount >= count, timeout, cancel);

    /// <summary>Joins a multicast group.</summary>
    public void JoinGroup(IPAddress group)
    {
        if (group.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(group));
        }
        else
        {
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(group));
        }
    }

    /// <summary>Leaves a multicast group.</summary>
    public void LeaveGroup(IPAddress group)
    {
        if (group.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.DropMembership, new IPv6MulticastOption(group));
        }
        else
        {
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(group));
        }
    }

    /// <summary>Sends <paramref name="data"/> to <paramref name="to"/> in datagrams of at most <see cref="OutputDatagramSize"/> bytes.</summary>
    public void SendTo(ReadOnlySpan<byte> data, IPEndPoint to)
    {
        try
        {
            int size = Math.Max(1, OutputDatagramSize);
            for (int at = 0; at < data.Length; at += size)
            {
                int n = _socket.SendTo(data.Slice(at, Math.Min(size, data.Length - at)), SocketFlags.None, to);
                Interlocked.Add(ref _written, n);
            }

            if (data.Length == 0)
            {
                _socket.SendTo(ReadOnlySpan<byte>.Empty, SocketFlags.None, to);
            }
        }
        catch (SocketException e)
        {
            throw new DeviceIOException(e.Message, e.ErrorCode);
        }
    }

    public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel) =>
        SendTo(data, RemoteEndpoint ?? throw new DeviceIOException("No destination address and port have been given."));

    public void FlushInput()
    {
        Input.Clear();
        lock (_gate)
        {
            _datagrams.Clear();
        }
    }

    public void FlushOutput()
    {
    }

    private void ReadLoop()
    {
        var buffer = new byte[65536];
        EndPoint from = new IPEndPoint(_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        try
        {
            while (!_closing)
            {
                int n = _socket.ReceiveFrom(buffer, ref from);
                var sender = (IPEndPoint)from;
                if (DatagramMode)
                {
                    int waiting;
                    lock (_gate)
                    {
                        _datagrams.Enqueue(new ReceivedDatagram(buffer.AsSpan(0, n).ToArray(), sender.Address.ToString(), sender.Port));
                        waiting = _datagrams.Count;
                    }

                    Input.Pulse();
                    try
                    {
                        DatagramReceived?.Invoke(waiting);
                    }
                    catch (Exception)
                    {
                        // The reader thread must not end the process.
                    }
                }
                else
                {
                    Input.Append(buffer.AsSpan(0, n));
                }
            }
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _socket.Dispose();
        _reader.Join(1000);
        _ = ConnectionLost;
    }
}
