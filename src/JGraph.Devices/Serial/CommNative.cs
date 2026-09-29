using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.Serial;

/// <summary>
/// The Win32 communications API a serial port stands on (kernel32): about fifteen functions, the DCB,
/// the timeouts and the overlapped structure, declared as source-generated P/Invoke.
/// </summary>
internal static unsafe partial class CommNative
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_HANDLE = 6;
    public const int ERROR_BAD_COMMAND = 22;
    public const int ERROR_GEN_FAILURE = 31;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_SEM_TIMEOUT = 121;
    public const int ERROR_OPERATION_ABORTED = 995;
    public const int ERROR_IO_INCOMPLETE = 996;
    public const int ERROR_IO_PENDING = 997;
    public const int ERROR_DEVICE_REMOVED = 1617;
    public const int ERROR_NO_SUCH_DEVICE = 433;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;
    public const uint INFINITE = 0xFFFFFFFF;

    // EscapeCommFunction
    public const uint SETXOFF = 1;
    public const uint SETXON = 2;
    public const uint SETRTS = 3;
    public const uint CLRRTS = 4;
    public const uint SETDTR = 5;
    public const uint CLRDTR = 6;
    public const uint SETBREAK = 8;
    public const uint CLRBREAK = 9;

    // GetCommModemStatus
    public const uint MS_CTS_ON = 0x0010;
    public const uint MS_DSR_ON = 0x0020;
    public const uint MS_RING_ON = 0x0040;
    public const uint MS_RLSD_ON = 0x0080;

    // PurgeComm
    public const uint PURGE_TXABORT = 0x0001;
    public const uint PURGE_RXABORT = 0x0002;
    public const uint PURGE_TXCLEAR = 0x0004;
    public const uint PURGE_RXCLEAR = 0x0008;

    // SetCommMask / WaitCommEvent
    public const uint EV_RXCHAR = 0x0001;
    public const uint EV_CTS = 0x0008;
    public const uint EV_DSR = 0x0010;
    public const uint EV_RLSD = 0x0020;
    public const uint EV_BREAK = 0x0040;
    public const uint EV_ERR = 0x0080;
    public const uint EV_RING = 0x0100;

    // ClearCommError
    public const uint CE_RXOVER = 0x0001;
    public const uint CE_OVERRUN = 0x0002;
    public const uint CE_RXPARITY = 0x0004;
    public const uint CE_FRAME = 0x0008;
    public const uint CE_BREAK = 0x0010;

    public const byte NOPARITY = 0;
    public const byte ODDPARITY = 1;
    public const byte EVENPARITY = 2;
    public const byte MARKPARITY = 3;
    public const byte SPACEPARITY = 4;

    public const byte ONESTOPBIT = 0;
    public const byte ONE5STOPBITS = 1;
    public const byte TWOSTOPBITS = 2;

    public const uint DTR_CONTROL_DISABLE = 0;
    public const uint DTR_CONTROL_ENABLE = 1;
    public const uint DTR_CONTROL_HANDSHAKE = 2;
    public const uint RTS_CONTROL_DISABLE = 0;
    public const uint RTS_CONTROL_ENABLE = 1;
    public const uint RTS_CONTROL_HANDSHAKE = 2;

    /// <summary>The device-control block: a port's speed, framing and flow control.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;

        // The bit fields of Flags, in the order windows.h declares them.
        public const int fBinary = 0;
        public const int fParity = 1;
        public const int fOutxCtsFlow = 2;
        public const int fOutxDsrFlow = 3;
        public const int fDtrControl = 4; // 2 bits
        public const int fDsrSensitivity = 6;
        public const int fTXContinueOnXoff = 7;
        public const int fOutX = 8;
        public const int fInX = 9;
        public const int fErrorChar = 10;
        public const int fNull = 11;
        public const int fRtsControl = 12; // 2 bits
        public const int fAbortOnError = 14;

        public void SetBit(int bit, bool on) => Flags = on ? Flags | (1u << bit) : Flags & ~(1u << bit);

        public readonly bool GetBit(int bit) => (Flags & (1u << bit)) != 0;

        public void SetField(int bit, uint value) => Flags = (Flags & ~(3u << bit)) | ((value & 3u) << bit);

        public readonly uint GetField(int bit) => (Flags >> bit) & 3u;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COMSTAT
    {
        public uint Flags;
        public uint cbInQue;
        public uint cbOutQue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OVERLAPPED
    {
        public nuint Internal;
        public nuint InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public nint hEvent;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCommState(SafeFileHandle handle, ref DCB dcb);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCommState(SafeFileHandle handle, ref DCB dcb);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCommTimeouts(SafeFileHandle handle, ref COMMTIMEOUTS timeouts);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupComm(SafeFileHandle handle, uint inQueue, uint outQueue);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCommMask(SafeFileHandle handle, uint mask);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WaitCommEvent(SafeFileHandle handle, uint* mask, OVERLAPPED* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(SafeFileHandle handle, byte* buffer, uint count, uint* read, OVERLAPPED* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(SafeFileHandle handle, byte* buffer, uint count, uint* written, OVERLAPPED* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOverlappedResult(SafeFileHandle handle, OVERLAPPED* overlapped, uint* transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CancelIoEx(SafeFileHandle handle, OVERLAPPED* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EscapeCommFunction(SafeFileHandle handle, uint function);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCommModemStatus(SafeFileHandle handle, out uint status);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PurgeComm(SafeFileHandle handle, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClearCommError(SafeFileHandle handle, out uint errors, out COMSTAT status);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
    public static partial nint CreateEvent(nint security, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ResetEvent(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForMultipleObjects(uint count, nint* handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint QueryDosDevice(string? name, char* buffer, uint max);
}
