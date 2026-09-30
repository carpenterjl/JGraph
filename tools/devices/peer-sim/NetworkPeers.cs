// The peer engine on a network endpoint, for R2025b's visadev fixtures (device classes plan, stage D4):
// a raw TCP socket (a VISA SOCKET resource) and a HiSLIP 1.0 server (a TCPIP INSTR resource,
// TCPIP0::127.0.0.1::hislip0::INSTR). Both serve one engine for the life of the process, so its log and
// rules outlive a connection, as they outlive a port reopened on the com0com pair.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using JGraph.Devices;
using JGraph.Devices.Simulation;

/// <summary>The engine behind a TCP listener: bytes from the connected client are data, and what it sends goes to that client.</summary>
internal sealed class TcpPeer : IPeerLink, IDisposable
{
    private readonly object _gate = new();
    private readonly TcpListener _listener;
    private Socket? _client;

    public TcpPeer(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Engine = new PeerEngine(this);
        new Thread(AcceptLoop) { IsBackground = true, Name = "peer-sim tcp" }.Start();
    }

    public PeerEngine Engine { get; }

    public long LastActivity;

    private void AcceptLoop()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = _listener.AcceptSocket();
            }
            catch (Exception)
            {
                return;
            }

            client.NoDelay = true;
            lock (_gate)
            {
                _client?.Dispose();
                _client = client;
            }

            Interlocked.Exchange(ref LastActivity, Environment.TickCount64);
            new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "peer-sim tcp client" }.Start();
        }
    }

    private void ReadLoop(Socket client)
    {
        var buffer = new byte[65536];
        while (true)
        {
            int n;
            try
            {
                n = client.Receive(buffer);
            }
            catch (Exception)
            {
                n = 0;
            }

            if (n <= 0)
            {
                lock (_gate)
                {
                    if (_client == client)
                    {
                        _client = null;
                    }
                }

                client.Dispose();
                return;
            }

            Interlocked.Exchange(ref LastActivity, Environment.TickCount64);
            Engine.Receive(buffer.AsSpan(0, n));
        }
    }

    public void Send(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            try
            {
                _client?.Send(bytes);
            }
            catch (Exception)
            {
                // The client went away; the engine carries on for the next one.
            }
        }
    }

    public void SetRts(bool on)
    {
    }

    public void SetDtr(bool on)
    {
    }

    public SerialPins Pins => default;

    public void SendBreak(int milliseconds)
    {
    }

    public void Dispose()
    {
        _listener.Stop();
        lock (_gate)
        {
            _client?.Dispose();
        }

        Engine.Dispose();
    }
}

/// <summary>
/// A HiSLIP 1.0 server (IVI-6.1) in synchronized mode with the engine behind it: Data and DataEND
/// messages on the synchronous channel are data, and each batch the engine sends is one DataEND tagged
/// with the MessageID of the last message received. The asynchronous channel answers the maximum
/// message size, locks, remote/local control, device clear (counted by the engine), and status queries
/// with the status byte the engine's <c>stb</c> command sets. A Trigger message is counted.
/// </summary>
internal sealed class HislipPeer : IPeerLink, IInstrumentLink, IDisposable
{
    private const byte Initialize = 0;
    private const byte InitializeResponse = 1;
    private const byte FatalError = 2;
    private const byte AsyncLock = 4;
    private const byte AsyncLockResponse = 5;
    private const byte Data = 6;
    private const byte DataEnd = 7;
    private const byte DeviceClearComplete = 8;
    private const byte DeviceClearAcknowledge = 9;
    private const byte AsyncRemoteLocalControl = 10;
    private const byte AsyncRemoteLocalResponse = 11;
    private const byte Trigger = 12;
    private const byte AsyncMaximumMessageSize = 15;
    private const byte AsyncMaximumMessageSizeResponse = 16;
    private const byte AsyncInitialize = 17;
    private const byte AsyncInitializeResponse = 18;
    private const byte AsyncDeviceClear = 19;
    private const byte AsyncServiceRequest = 20;
    private const byte AsyncStatusQuery = 21;
    private const byte AsyncStatusResponse = 22;
    private const byte AsyncDeviceClearAcknowledge = 23;
    private const byte AsyncLockInfo = 24;
    private const byte AsyncLockInfoResponse = 25;

    private const uint InitialMessageId = 0xFFFFFF00;

    private readonly object _gate = new();
    private readonly TcpListener _listener;
    private readonly List<byte> _batch = new();
    private bool _batching;
    private Stream? _sync;
    private Stream? _async;
    private uint _lastMessageId = InitialMessageId;
    private ushort _nextSession = 1;

    public HislipPeer(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Engine = new PeerEngine(this);
        new Thread(AcceptLoop) { IsBackground = true, Name = "peer-sim hislip" }.Start();
    }

    public PeerEngine Engine { get; }

    public long LastActivity;

    public byte StatusByte { get; set; }

    /// <summary>Sends AsyncServiceRequest on the asynchronous channel, with the status byte as its control code.</summary>
    public void RequestService()
    {
        Stream? async;
        lock (_gate)
        {
            async = _async;
        }

        if (async is not null)
        {
            try
            {
                WriteMessage(async, AsyncServiceRequest, (byte)(StatusByte | 0x40), 0, []);
            }
            catch (Exception)
            {
                // The session went away.
            }
        }
    }

    private void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            client.NoDelay = true;
            Interlocked.Exchange(ref LastActivity, Environment.TickCount64);
            new Thread(() => Serve(client)) { IsBackground = true, Name = "peer-sim hislip channel" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        using (client)
        {
            Stream stream = client.GetStream();
            try
            {
                (byte type, _, uint parameter, byte[] _) = ReadMessage(stream);
                if (type == Initialize)
                {
                    ushort session;
                    lock (_gate)
                    {
                        session = _nextSession++;
                        _sync = stream;
                        _lastMessageId = InitialMessageId;
                    }

                    // Server protocol 1.0, synchronized mode (control code 0), the new session's id.
                    WriteMessage(stream, InitializeResponse, 0, (0x0100u << 16) | session, []);
                    SyncLoop(stream);
                }
                else if (type == AsyncInitialize)
                {
                    WriteMessage(stream, AsyncInitializeResponse, 0, ('J' << 8) | 'G', []);
                    lock (_gate)
                    {
                        _async = stream;
                    }

                    AsyncLoop(stream);
                }
                else
                {
                    WriteMessage(stream, FatalError, 1, 0, "Expected Initialize or AsyncInitialize"u8.ToArray());
                }

                _ = parameter;
            }
            catch (Exception)
            {
                // The client closed the channel.
            }
            finally
            {
                lock (_gate)
                {
                    if (_sync == stream)
                    {
                        _sync = null;
                    }

                    if (_async == stream)
                    {
                        _async = null;
                    }
                }
            }
        }
    }

    private void SyncLoop(Stream stream)
    {
        while (true)
        {
            (byte type, byte control, uint parameter, byte[] payload) = ReadMessage(stream);
            Interlocked.Exchange(ref LastActivity, Environment.TickCount64);
            switch (type)
            {
                case Data:
                case DataEnd:
                    lock (_gate)
                    {
                        _lastMessageId = parameter;
                    }

                    BeginBatch();
                    try
                    {
                        Engine.Receive(payload);
                    }
                    finally
                    {
                        EndBatch();
                    }

                    break;
                case Trigger:
                    lock (_gate)
                    {
                        _lastMessageId = parameter;
                    }

                    Engine.TriggerReceived();
                    break;
                case DeviceClearComplete:
                    lock (_gate)
                    {
                        _lastMessageId = InitialMessageId;
                        WriteMessage(stream, DeviceClearAcknowledge, 0, 0, []);
                    }

                    break;
                default:
                    _ = control;
                    break;
            }
        }
    }

    private void AsyncLoop(Stream stream)
    {
        while (true)
        {
            (byte type, byte control, uint parameter, byte[] payload) = ReadMessage(stream);
            Interlocked.Exchange(ref LastActivity, Environment.TickCount64);
            switch (type)
            {
                case AsyncMaximumMessageSize:
                {
                    var size = new byte[8];
                    BinaryPrimitives.WriteUInt64BigEndian(size, 1 << 20);
                    WriteMessage(stream, AsyncMaximumMessageSizeResponse, 0, 0, size);
                    break;
                }

                case AsyncLock:
                    WriteMessage(stream, AsyncLockResponse, 1, 0, []);
                    break;
                case AsyncLockInfo:
                    WriteMessage(stream, AsyncLockInfoResponse, 0, 0, []);
                    break;
                case AsyncRemoteLocalControl:
                    WriteMessage(stream, AsyncRemoteLocalResponse, 0, 0, []);
                    break;
                case AsyncStatusQuery:
                    WriteMessage(stream, AsyncStatusResponse, StatusByte, 0, []);
                    break;
                case AsyncDeviceClear:
                    Engine.ClearReceived();
                    WriteMessage(stream, AsyncDeviceClearAcknowledge, 0, 0, []);
                    break;
                default:
                    _ = control;
                    _ = parameter;
                    _ = payload;
                    break;
            }
        }
    }

    private void BeginBatch()
    {
        lock (_gate)
        {
            _batching = true;
        }
    }

    private void EndBatch()
    {
        lock (_gate)
        {
            _batching = false;
            if (_batch.Count > 0)
            {
                byte[] bytes = _batch.ToArray();
                _batch.Clear();
                SendLocked(bytes);
            }
        }
    }

    public void Send(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_batching)
            {
                _batch.AddRange(bytes.ToArray());
                return;
            }

            SendLocked(bytes.ToArray());
        }
    }

    private void SendLocked(byte[] bytes)
    {
        if (_sync is { } sync)
        {
            try
            {
                WriteMessage(sync, DataEnd, 0, _lastMessageId, bytes);
            }
            catch (Exception)
            {
                // The session went away.
            }
        }
    }

    private static (byte Type, byte Control, uint Parameter, byte[] Payload) ReadMessage(Stream stream)
    {
        byte[] header = ReadExactly(stream, 16);
        if (header[0] != (byte)'H' || header[1] != (byte)'S')
        {
            throw new IOException("not a HiSLIP message");
        }

        uint parameter = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        ulong length = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
        if (length > 1 << 26)
        {
            throw new IOException("HiSLIP payload too long");
        }

        return (header[2], header[3], parameter, ReadExactly(stream, (int)length));
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        int at = 0;
        while (at < count)
        {
            int n = stream.Read(buffer, at, count - at);
            if (n <= 0)
            {
                throw new EndOfStreamException();
            }

            at += n;
        }

        return buffer;
    }

    private static void WriteMessage(Stream stream, byte type, byte control, uint parameter, byte[] payload)
    {
        var message = new byte[16 + payload.Length];
        message[0] = (byte)'H';
        message[1] = (byte)'S';
        message[2] = type;
        message[3] = control;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), parameter);
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(8), (ulong)payload.Length);
        payload.CopyTo(message, 16);
        lock (stream)
        {
            stream.Write(message);
            stream.Flush();
        }
    }

    public void SetRts(bool on)
    {
    }

    public void SetDtr(bool on)
    {
    }

    public SerialPins Pins => default;

    public void SendBreak(int milliseconds)
    {
    }

    public void Dispose()
    {
        _listener.Stop();
        Engine.Dispose();
    }
}
