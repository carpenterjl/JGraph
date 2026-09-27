using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// The Windows job object every native host joins (ADR 0180). It is made with
/// <c>KILL_ON_JOB_CLOSE</c>, and JGraph holds its only handle until the process ends, so when JGraph
/// ends — normally, killed, or crashed — Windows ends every host with it. <c>DIE_ON_UNHANDLED_EXCEPTION</c>
/// keeps Windows Error Reporting from holding a crashed host open behind a dialog.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private const uint DieOnUnhandledException = 0x0400;

    private static readonly Lazy<nint> Job = new(Create);

    /// <summary>Puts <paramref name="process"/> in the job; false when Windows refused.</summary>
    public static bool Adopt(Process process)
    {
        nint job = Job.Value;
        return job != 0 && AssignProcessToJobObject(job, process.Handle);
    }

    private static nint Create()
    {
        nint job = CreateJobObjectW(0, null);
        if (job == 0)
        {
            return 0;
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose | DieOnUnhandledException } };
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The native host's job object could not be configured.");
        }

        return job; // never closed: closing it is what kills the hosts
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimits info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);
}
