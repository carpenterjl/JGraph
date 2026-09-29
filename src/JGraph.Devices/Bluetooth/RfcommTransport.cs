using System.Net;
using System.Net.Sockets;

namespace JGraph.Devices.Bluetooth;

/// <summary>
/// An RFCOMM channel to a paired classic device (<c>bluetooth</c>), over a Winsock AF_BTH stream
/// socket: the same byte stream a serial port gives, with a reader thread into <see cref="Input"/>.
/// </summary>
public sealed class RfcommTransport : IDeviceTransport
{
    /// <summary>Winsock's AF_BTH.</summary>
    private const AddressFamily Bluetooth = (AddressFamily)32;

    /// <summary>Winsock's BTHPROTO_RFCOMM.</summary>
    private const ProtocolType Rfcomm = (ProtocolType)3;

    private readonly Socket _socket;
    private readonly Thread _reader;
    private volatile bool _closing;
    private int _lost;
    private long _written;

    private RfcommTransport(Socket socket, ulong address, int channel)
    {
        _socket = socket;
        Address = address;
        Channel = channel;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"JGraph rfcomm reader {BluetoothAddress.Format(address)}" };
        _reader.Start();
    }

    /// <summary>Connects to channel <paramref name="channel"/> of <paramref name="address"/>; channel 0 asks SDP for the serial port service.</summary>
    public static RfcommTransport Connect(ulong address, int channel, CancellationToken cancel)
    {
        Socket socket;
        try
        {
            socket = new Socket(Bluetooth, SocketType.Stream, Rfcomm);
        }
        catch (SocketException e)
        {
            throw new DeviceOpenException(e.Message, e.ErrorCode);
        }

        try
        {
            socket.ConnectAsync(new RfcommEndPoint(address, channel), cancel).AsTask().GetAwaiter().GetResult();
        }
        catch (SocketException e)
        {
            socket.Dispose();
            throw new DeviceOpenException(e.Message, e.ErrorCode);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new RfcommTransport(socket, address, channel);
    }

    public ulong Address { get; }

    public int Channel { get; }

    public string Name => $"{BluetoothAddress.Format(Address)}:{Channel}";

    public bool Connected => !_closing && Volatile.Read(ref _lost) == 0;

    public InputBuffer Input { get; } = new();

    public long BytesWritten => Interlocked.Read(ref _written);

    public event Action<DeviceConnectionLostException>? ConnectionLost;

    public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel)
    {
        if (!Connected)
        {
            throw new DeviceConnectionLostException($"The channel to {Name} is closed.");
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
        var buffer = new byte[4096];
        try
        {
            while (!_closing)
            {
                int n = _socket.Receive(buffer);
                if (n == 0)
                {
                    Lose("The device closed the channel.", 0);
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

    /// <summary>
    /// Winsock's SOCKADDR_BTH, packed: the family (2 bytes), the device address (8), the service
    /// class GUID (16, zero here) and the channel (4). Channel 0 with the serial port GUID lets
    /// Winsock find the channel through SDP.
    /// </summary>
    private sealed class RfcommEndPoint : EndPoint
    {
        private static readonly Guid SerialPort = new("00001101-0000-1000-8000-00805F9B34FB");
        private readonly ulong _address;
        private readonly int _channel;

        public RfcommEndPoint(ulong address, int channel)
        {
            _address = address;
            _channel = channel;
        }

        public override AddressFamily AddressFamily => Bluetooth;

        public override SocketAddress Serialize()
        {
            var socketAddress = new SocketAddress(Bluetooth, 30);
            for (int i = 0; i < 8; i++)
            {
                socketAddress[2 + i] = (byte)(_address >> (8 * i));
            }

            byte[] service = _channel == 0 ? SerialPort.ToByteArray() : new byte[16];
            for (int i = 0; i < 16; i++)
            {
                socketAddress[10 + i] = service[i];
            }

            for (int i = 0; i < 4; i++)
            {
                socketAddress[26 + i] = (byte)(_channel >> (8 * i));
            }

            return socketAddress;
        }

        public override EndPoint Create(SocketAddress socketAddress)
        {
            ulong address = 0;
            for (int i = 0; i < 8; i++)
            {
                address |= (ulong)socketAddress[2 + i] << (8 * i);
            }

            int channel = 0;
            for (int i = 0; i < 4; i++)
            {
                channel |= socketAddress[26 + i] << (8 * i);
            }

            return new RfcommEndPoint(address, channel);
        }
    }
}
