using JGraph.Devices.Dfu;
using JGraph.Devices.Usb;

namespace JGraph.Devices.Simulation;

/// <summary>What a <see cref="SimulatedDfu"/> is: a plain DFU 1.1 loader, a DfuSe loader, or an application with a DFU run-time interface.</summary>
public enum SimulatedDfuKind
{
    Dfu,
    DfuSe,
    Runtime,
}

/// <summary>
/// A DFU device for the tests and <c>jgraph.internal.dfusim</c> (device classes plan, stage D8, ADR 0191),
/// keeping the DFU 1.1 state machine (DFU 1.1, appendix A) as the requests arrive. Three kinds:
/// <list type="bullet">
/// <item>a DfuSe loader shaped like ST's ROM one: 128 KiB of flash at 0x08000000 in 16 and 64 KiB pages,
/// erased to 0xFF and programmed only from 1 to 0 as flash is, plus 16 option bytes as alternate 1;
/// commands execute on the GETSTATUS after them;</item>
/// <item>a plain DFU 1.1 loader holding one firmware image, manifestation tolerant;</item>
/// <item>an application whose run-time interface detaches by itself into the DfuSe loader on the same port.</item>
/// </list>
/// A refused request stalls, and the device goes to dfuERROR with errSTALLEDPKT, as the specification says.
/// </summary>
public sealed class SimulatedDfu : IDfuTransport
{
    public const uint FlashStart = 0x08000000;
    public const uint OptionStart = 0x1FFFC000;
    private const string FlashLayout = "@Internal Flash  /0x08000000/04*016Kg,01*064Kg";
    private const string OptionLayout = "@Option Bytes  /0x1FFFC000/01*016 e";

    private readonly SimulatedDfuKind _kind;
    private readonly byte[] _flash = new byte[128 * 1024];
    private readonly byte[] _options = [0xAA, 0xEC, 0x55, 0x13, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];
    private byte[] _image;
    private readonly List<byte> _incoming = [];
    private int _state;
    private int _status;
    private int _alternate;
    private uint _pointer = FlashStart;
    private Func<int>? _pending;
    private int _writes;

    public SimulatedDfu(SimulatedDfuKind kind)
    {
        _kind = kind;
        _state = kind == SimulatedDfuKind.Runtime ? 0 : 2;
        Array.Fill(_flash, (byte)0xFF);
        for (int i = 0; i < 1024; i++)
        {
            _flash[i] = (byte)(i * 7); // the application already there
        }

        _image = Enumerable.Range(0, 3000).Select(static i => (byte)(i * 13)).ToArray();
        Functional = kind switch
        {
            SimulatedDfuKind.DfuSe => new DfuFunctional(true, true, false, true, 255, 2048, 0x011A),
            SimulatedDfuKind.Dfu => new DfuFunctional(true, true, true, false, 1000, 256, 0x0110),
            _ => new DfuFunctional(true, true, false, true, 1000, 256, 0x0110),
        };
        AlternateNames = kind == SimulatedDfuKind.DfuSe ? [FlashLayout, OptionLayout] : [kind == SimulatedDfuKind.Dfu ? "Firmware" : "DFU run-time"];
        Info = Describe();
    }

    public SimulatedDfuKind Kind => _kind;

    public DfuFunctional Functional { get; }

    public IReadOnlyList<string> AlternateNames { get; }

    /// <summary>The device as jgraph.usb.devices would list it, with a configuration descriptor that carries its DFU interface.</summary>
    public UsbDeviceInfo Info { get; }

    /// <summary>A write, counted from 1 across the device's life, that reports errWRITE; 0 for none.</summary>
    public int FailAtWrite { get; set; }

    /// <summary>Whether the device has reset: left DFU mode, detached, or unprotected its flash.</summary>
    public bool Gone { get; private set; }

    /// <summary>The DfuSe loader a run-time device became when it detached.</summary>
    public SimulatedDfu? Detached { get; private set; }

    public int State => _state;

    /// <summary>The plain loader's firmware image.</summary>
    public byte[] Image => _image;

    /// <summary>Memory as it is, read past the loader: the flash or the option bytes.</summary>
    public byte[] Peek(uint address, int count)
    {
        (byte[] memory, uint start) = address >= OptionStart ? (_options, OptionStart) : (_flash, FlashStart);
        int at = (int)(address - start);
        if (at < 0 || at + count > memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The simulated device has no memory there.");
        }

        return memory.AsSpan(at, count).ToArray();
    }

    private UsbDeviceInfo Describe()
    {
        (int vid, int pid, string product, string serial) = _kind switch
        {
            SimulatedDfuKind.DfuSe => (0x0483, 0xDF11, "DFU in FS Mode (simulated)", "SIM-DFUSE"),
            SimulatedDfuKind.Dfu => (0x1209, 0x2002, "Simulated DFU loader", "SIM-DFU"),
            _ => (0x1209, 0x2003, "Simulated application", "SIM-APP"),
        };
        var configuration = new List<byte> { 9, 2, 0, 0, 1, 1, 0, 0x80, 50 };
        for (int alt = 0; alt < AlternateNames.Count; alt++)
        {
            configuration.AddRange([9, 4, 0, (byte)alt, 0, 0xFE, 1, (byte)(_kind == SimulatedDfuKind.Runtime ? 1 : 2), (byte)(4 + alt)]);
        }

        configuration.AddRange(Functional.ToBytes());
        configuration[2] = (byte)configuration.Count;
        var strings = new Dictionary<int, string> { [1] = "JGraph", [2] = product, [3] = serial };
        for (int alt = 0; alt < AlternateNames.Count; alt++)
        {
            strings[4 + alt] = AlternateNames[alt];
        }

        return new UsbDeviceInfo
        {
            InstanceId = $"SIM\\VID_{vid:X4}&PID_{pid:X4}\\{serial}",
            VendorId = vid,
            ProductId = pid,
            Release = 0x0200,
            Manufacturer = "JGraph",
            Product = product,
            SerialNumber = serial,
            Description = product,
            Speed = UsbSpeed.Full,
            Bus = 99,
            Ports = [1],
            Driver = "WINUSB",
            DeviceDescriptor = [18, 1, 0x00, 0x02, 0, 0, 0, 64, (byte)vid, (byte)(vid >> 8), (byte)pid, (byte)(pid >> 8), 0x00, 0x02, 1, 2, 3, 1],
            ConfigurationDescriptor = configuration.ToArray(),
            Languages = [0x0409],
            Strings = strings,
        };
    }

    private DfuSeLayout Layout => DfuSeLayout.Parse(AlternateNames[_alternate])!;

    private Exception Stall()
    {
        if (_state >= 2)
        {
            _state = 10;
            _status = 15;
        }

        return new DeviceIOException("The request failed: the device stalled the request.");
    }

    public byte[] Control(byte requestType, byte request, ushort value, ushort index, ReadOnlySpan<byte> data, int readLength, TimeSpan timeout)
    {
        if (Gone)
        {
            throw new DeviceConnectionLostException("The device is no longer connected.");
        }

        if ((requestType & 0x7F) != 0x21 || index != 0)
        {
            throw Stall();
        }

        if (_kind == SimulatedDfuKind.Runtime)
        {
            switch (request)
            {
                case 0:
                    _state = 1;
                    Gone = true;
                    Detached = new SimulatedDfu(SimulatedDfuKind.DfuSe);
                    return [];
                case 3:
                    return [0, 0, 0, 0, (byte)_state, 0];
                case 5:
                    return [(byte)_state];
                default:
                    throw Stall();
            }
        }

        switch (request)
        {
            case 1:
                return Download(value, data);
            case 2:
                return Upload(value, readLength);
            case 3:
                return GetStatus();
            case 4:
                if (_state != 10)
                {
                    throw Stall();
                }

                _state = 2;
                _status = 0;
                return [];
            case 5:
                return [(byte)_state];
            case 6:
                if (_state is not (2 or 5 or 9))
                {
                    throw Stall();
                }

                _state = 2;
                return [];
            default:
                throw Stall();
        }
    }

    private byte[] Download(ushort block, ReadOnlySpan<byte> data)
    {
        if (_state is not (2 or 5) || data.Length > Functional.TransferSize || (_state == 2 && data.Length == 0))
        {
            throw Stall();
        }

        if (data.Length == 0)
        {
            _state = 6;
            return [];
        }

        if (_kind == SimulatedDfuKind.Dfu)
        {
            if (_state == 2)
            {
                _incoming.Clear();
            }

            byte[] bytes = data.ToArray();
            _pending = () => Count() ?? Place(block, bytes);
        }
        else if (block == 0)
        {
            byte[] command = data.ToArray();
            _pending = () => Command(command);
        }
        else if (block >= 2)
        {
            byte[] bytes = data.ToArray();
            uint at = _pointer + (uint)((block - 2) * Functional.TransferSize);
            _pending = () => Count() ?? Program(at, bytes);
        }
        else
        {
            throw Stall();
        }

        _state = 3;
        return [];
    }

    private int? Count() => ++_writes == FailAtWrite ? 3 : null;

    private int Place(int block, byte[] bytes)
    {
        int at = block * Functional.TransferSize;
        if (at + bytes.Length > 32 * 1024)
        {
            return 8;
        }

        while (_incoming.Count < at + bytes.Length)
        {
            _incoming.Add(0);
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            _incoming[at + i] = bytes[i];
        }

        return 0;
    }

    private int Command(byte[] command)
    {
        uint? address = command.Length >= 5 ? (uint)(command[1] | (command[2] << 8) | (command[3] << 16) | (command[4] << 24)) : null;
        switch (command[0])
        {
            case 0x21 when address is { } a:
                if (Layout.SegmentAt(a) is null)
                {
                    return 8;
                }

                _pointer = a;
                return 0;
            case 0x41 when address is { } a:
                if (Layout.SegmentAt(a) is not { Erasable: true } segment)
                {
                    return 8;
                }

                uint page = segment.Start + (a - segment.Start) / (uint)segment.PageSize * (uint)segment.PageSize;
                Array.Fill(_flash, (byte)0xFF, (int)(page - FlashStart), segment.PageSize);
                return 0;
            case 0x41:
                Array.Fill(_flash, (byte)0xFF);
                return 0;
            case 0x92:
                Array.Fill(_flash, (byte)0xFF);
                Gone = true;
                return 0;
            default:
                return 1;
        }
    }

    private int Program(uint address, byte[] bytes)
    {
        DfuSeLayout layout = Layout;
        if (layout.Unwriteable(address, bytes.Length) is not null)
        {
            return 8;
        }

        if (address >= OptionStart)
        {
            bytes.CopyTo(_options, (int)(address - OptionStart));
            return 0;
        }

        int at = (int)(address - FlashStart);
        for (int i = 0; i < bytes.Length; i++)
        {
            if ((_flash[at + i] & bytes[i]) != bytes[i])
            {
                return 6; // a bit that must go from 0 to 1: the page was not erased
            }
        }

        bytes.CopyTo(_flash, at);
        return 0;
    }

    private byte[] GetStatus()
    {
        int poll = 0;
        switch (_state)
        {
            case 3:
                int result = _pending?.Invoke() ?? 0;
                _pending = null;
                _status = result;
                _state = 4;
                poll = 1;
                break;
            case 4:
                _state = _status == 0 ? 5 : 10;
                break;
            case 6:
                _state = 7;
                poll = 1;
                if (_kind == SimulatedDfuKind.DfuSe)
                {
                    Gone = true; // leaving: the loader jumps to the address pointer
                }
                else
                {
                    _image = _incoming.ToArray();
                }

                break;
            case 7:
                _state = Functional.ManifestationTolerant ? 2 : 8;
                break;
        }

        // A DNBUSY answer reports OK; the result shows on the GETSTATUS after it.
        int status = _state == 4 ? 0 : _status;
        return [(byte)status, (byte)poll, 0, 0, (byte)_state, 0];
    }

    private byte[] Upload(ushort block, int length)
    {
        if (_state is not (2 or 9) || length > Functional.TransferSize)
        {
            throw Stall();
        }

        if (_kind == SimulatedDfuKind.Dfu)
        {
            int at = block * Functional.TransferSize;
            int count = Math.Clamp(_image.Length - at, 0, length);
            _state = count < length ? 2 : 9;
            return _image.AsSpan(at, count).ToArray();
        }

        if (block == 0)
        {
            _state = 9;
            return new byte[] { 0x00, 0x21, 0x41, 0x92 }[..Math.Min(4, length)];
        }

        uint address = _pointer + (uint)((block - 2) * Functional.TransferSize);
        if (block < 2 || Layout.SegmentAt(address) is not { Readable: true } || Layout.SegmentAt(address + (uint)length - 1) is null)
        {
            throw Stall();
        }

        _state = 9;
        return Peek(address, length);
    }

    public void SetAlternate(int interfaceNumber, int setting)
    {
        if (Gone)
        {
            throw new DeviceConnectionLostException("The device is no longer connected.");
        }

        if (interfaceNumber != 0 || setting < 0 || setting >= AlternateNames.Count)
        {
            throw new DeviceIOException($"The device has no alternate setting {setting} of interface {interfaceNumber}.");
        }

        _alternate = setting;
        _pointer = DfuSeLayout.Parse(AlternateNames[setting]) is { Segments.Count: > 0 } layout ? layout.Segments[0].Start : 0;
    }

    public void Dispose()
    {
    }
}
