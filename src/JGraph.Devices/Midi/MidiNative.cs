using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Midi;

/// <summary>WinMM's MIDI API (mmeapi.h) and the timer resolution calls (timeapi.h).</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class MidiNative
{
    public const uint MidiMapper = 0xFFFFFFFF;
    public const uint CALLBACK_FUNCTION = 0x00030000;
    public const uint MIM_DATA = 0x3C3;
    public const uint MIM_LONGDATA = 0x3C4;
    public const uint MHDR_DONE = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    public struct MIDIINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        public fixed char szPname[32];
        public uint dwSupport;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIDIOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        public fixed char szPname[32];
        public ushort wTechnology;
        public ushort wVoices;
        public ushort wNotes;
        public ushort wChannelMask;
        public uint dwSupport;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIDIHDR
    {
        public byte* lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public nint dwUser;
        public uint dwFlags;
        public nint lpNext;
        public nint reserved;
        public uint dwOffset;
        public fixed long dwReserved[8];
    }

    [LibraryImport("winmm.dll")]
    public static partial uint midiInGetNumDevs();

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutGetNumDevs();

    [LibraryImport("winmm.dll", EntryPoint = "midiInGetDevCapsW")]
    public static partial uint midiInGetDevCaps(nuint id, MIDIINCAPS* caps, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW")]
    public static partial uint midiOutGetDevCaps(nuint id, MIDIOUTCAPS* caps, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInOpen(nint* handle, uint id, delegate* unmanaged<nint, uint, nint, nint, nint, void> callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInStart(nint handle);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInStop(nint handle);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInReset(nint handle);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInClose(nint handle);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInPrepareHeader(nint handle, MIDIHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInUnprepareHeader(nint handle, MIDIHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint midiInAddBuffer(nint handle, MIDIHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutOpen(nint* handle, uint id, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutReset(nint handle);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutClose(nint handle);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutShortMsg(nint handle, uint message);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutLongMsg(nint handle, MIDIHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutPrepareHeader(nint handle, MIDIHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint midiOutUnprepareHeader(nint handle, MIDIHDR* header, uint size);

    [LibraryImport("winmm.dll", EntryPoint = "midiOutGetErrorTextW")]
    public static partial uint midiOutGetErrorText(uint error, char* text, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint period);

    /// <summary>An input's name, or null when WinMM has no input by that ID (the MIDI Mapper usually).</summary>
    public static string? InputName(uint id)
    {
        MIDIINCAPS caps;
        return midiInGetDevCaps(id, &caps, (uint)sizeof(MIDIINCAPS)) == 0 ? new string(caps.szPname) : null;
    }

    /// <summary>An output's name, or null when WinMM has no output by that ID.</summary>
    public static string? OutputName(uint id)
    {
        MIDIOUTCAPS caps;
        return midiOutGetDevCaps(id, &caps, (uint)sizeof(MIDIOUTCAPS)) == 0 ? new string(caps.szPname) : null;
    }

    /// <summary>WinMM's sentence for an error code.</summary>
    public static string ErrorText(uint error)
    {
        char* text = stackalloc char[256];
        return midiOutGetErrorText(error, text, 256) == 0 ? new string(text) : $"MMSYSERR {error}";
    }
}

/// <summary>
/// A WinMM MIDI input. Short messages arrive through the driver's callback; system exclusive messages
/// through four 4 KiB buffers, each handed back to the driver once its message is queued.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WinMmMidiInput : IMidiInput
{
    private const int SysexBuffers = 4;
    private const int SysexSize = 4096;

    private readonly Queue<MidiEvent> _received = new();
    private readonly object _gate = new();
    private readonly List<nint> _headers = [];
    private GCHandle _self;
    private nint _handle;
    private volatile bool _closing;

    public WinMmMidiInput(MidiDeviceInfo device)
    {
        _self = GCHandle.Alloc(this);
        nint handle = 0;
        uint result = MidiNative.midiInOpen(&handle, device.NativeId, &Callback, GCHandle.ToIntPtr(_self), MidiNative.CALLBACK_FUNCTION);
        if (result != 0)
        {
            _self.Free();
            throw new DeviceOpenException(MidiNative.ErrorText(result), (int)result);
        }

        _handle = handle;
        for (int k = 0; k < SysexBuffers; k++)
        {
            var header = (MidiNative.MIDIHDR*)NativeMemory.AllocZeroed((nuint)sizeof(MidiNative.MIDIHDR));
            header->lpData = (byte*)NativeMemory.Alloc(SysexSize);
            header->dwBufferLength = SysexSize;
            MidiNative.midiInPrepareHeader(_handle, header, (uint)sizeof(MidiNative.MIDIHDR));
            MidiNative.midiInAddBuffer(_handle, header, (uint)sizeof(MidiNative.MIDIHDR));
            _headers.Add((nint)header);
        }

        MidiNative.midiInStart(_handle);
    }

    public event Action<MidiEvent>? Arrived;

    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _received.Count;
            }
        }
    }

    public IReadOnlyList<MidiEvent> Take(int max)
    {
        lock (_gate)
        {
            var taken = new List<MidiEvent>(Math.Min(max, _received.Count));
            while (taken.Count < max && _received.TryDequeue(out MidiEvent e))
            {
                taken.Add(e);
            }

            return taken;
        }
    }

    [UnmanagedCallersOnly]
    private static void Callback(nint handle, uint message, nint instance, nint param1, nint param2)
    {
        try
        {
            if (instance == 0 || GCHandle.FromIntPtr(instance).Target is not WinMmMidiInput input)
            {
                return;
            }

            double now = MidiClock.Now;
            if (message == MidiNative.MIM_DATA)
            {
                uint packed = (uint)param1;
                byte status = (byte)packed;
                int length = status < 0x80 ? 1 : MidiWire.LengthOf(status);
                byte[] bytes = new byte[length];
                for (int i = 0; i < length; i++)
                {
                    bytes[i] = (byte)(packed >> (8 * i));
                }

                input.Enqueue(new MidiEvent(bytes, now));
            }
            else if (message == MidiNative.MIM_LONGDATA)
            {
                var header = (MidiNative.MIDIHDR*)param1;
                if (header->dwBytesRecorded > 0)
                {
                    input.Enqueue(new MidiEvent(new ReadOnlySpan<byte>(header->lpData, (int)header->dwBytesRecorded).ToArray(), now));
                }

                // The buffer goes back to the driver from another thread: WinMM forbids it from its callback.
                if (!input._closing)
                {
                    nint again = (nint)header;
                    ThreadPool.UnsafeQueueUserWorkItem(static state => state.Input.Requeue(state.Header), (Input: input, Header: again), preferLocal: false);
                }
            }
        }
        catch (Exception)
        {
            // Nothing may escape into the driver.
        }
    }

    private void Requeue(nint header)
    {
        lock (_gate)
        {
            if (_closing || _handle == 0)
            {
                return;
            }

            var h = (MidiNative.MIDIHDR*)header;
            h->dwBytesRecorded = 0;
            MidiNative.midiInAddBuffer(_handle, h, (uint)sizeof(MidiNative.MIDIHDR));
        }
    }

    private void Enqueue(MidiEvent e)
    {
        lock (_gate)
        {
            // Nothing may read an input a midicontrols object alone holds: keep the newest.
            if (_received.Count >= MidiLimits.Queue)
            {
                _received.Dequeue();
            }

            _received.Enqueue(e);
        }

        Arrived?.Invoke(e);
    }

    public void Dispose()
    {
        _closing = true;
        nint handle;
        lock (_gate)
        {
            handle = _handle;
            _handle = 0;
        }

        if (handle != 0)
        {
            MidiNative.midiInStop(handle);
            MidiNative.midiInReset(handle); // returns every buffer, marked done
            foreach (nint header in _headers)
            {
                var h = (MidiNative.MIDIHDR*)header;
                MidiNative.midiInUnprepareHeader(handle, h, (uint)sizeof(MidiNative.MIDIHDR));
                NativeMemory.Free(h->lpData);
                NativeMemory.Free(h);
            }

            _headers.Clear();
            MidiNative.midiInClose(handle);
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }
}

/// <summary>
/// A WinMM MIDI output: short messages through midiOutShortMsg, a system exclusive message through
/// midiOutLongMsg, each at its time on a <see cref="MidiScheduler"/>. Closing drops what has not been
/// sent and resets the device, which turns off any note still sounding.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WinMmMidiOutput : IMidiOutput
{
    private readonly MidiScheduler _scheduler;
    private nint _handle;

    public WinMmMidiOutput(MidiDeviceInfo device)
    {
        nint handle = 0;
        uint result = MidiNative.midiOutOpen(&handle, device.NativeId, 0, 0, 0);
        if (result != 0)
        {
            throw new DeviceOpenException(MidiNative.ErrorText(result), (int)result);
        }

        _handle = handle;
        _scheduler = new MidiScheduler(Deliver, "MIDI out: " + device.Name);
    }

    public void Send(IReadOnlyList<(byte[] Bytes, double Delay)> messages) => _scheduler.Schedule(messages);

    public bool WaitSent(TimeSpan timeout) => _scheduler.WaitIdle(timeout);

    private void Deliver(byte[] bytes)
    {
        nint handle = _handle;
        if (handle == 0 || bytes.Length == 0)
        {
            return;
        }

        if (bytes[0] != 0xF0)
        {
            uint sent = MidiNative.midiOutShortMsg(handle, MidiWire.Pack(bytes));
            if (sent != 0)
            {
                throw new DeviceIOException(MidiNative.ErrorText(sent));
            }

            return;
        }

        byte* data = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        var header = (MidiNative.MIDIHDR*)NativeMemory.AllocZeroed((nuint)sizeof(MidiNative.MIDIHDR));
        try
        {
            bytes.CopyTo(new Span<byte>(data, bytes.Length));
            header->lpData = data;
            header->dwBufferLength = (uint)bytes.Length;
            MidiNative.midiOutPrepareHeader(handle, header, (uint)sizeof(MidiNative.MIDIHDR));
            uint result = MidiNative.midiOutLongMsg(handle, header, (uint)sizeof(MidiNative.MIDIHDR));
            if (result == 0)
            {
                SpinWait.SpinUntil(() => (header->dwFlags & MidiNative.MHDR_DONE) != 0, 2000);
            }

            MidiNative.midiOutUnprepareHeader(handle, header, (uint)sizeof(MidiNative.MIDIHDR));
            if (result != 0)
            {
                throw new DeviceIOException(MidiNative.ErrorText(result));
            }
        }
        finally
        {
            NativeMemory.Free(header);
            NativeMemory.Free(data);
        }
    }

    public void Dispose()
    {
        _scheduler.Dispose();
        nint handle = _handle;
        _handle = 0;
        if (handle != 0)
        {
            MidiNative.midiOutReset(handle);
            MidiNative.midiOutClose(handle);
        }
    }
}
