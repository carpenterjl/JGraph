using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices.Usb;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.Storage;

/// <summary>A disk's SCSI identity, as STORAGE_DEVICE_DESCRIPTOR gives it.</summary>
public sealed record StorageIdentity(string Vendor, string Product, string Revision, string SerialNumber, string BusType, bool Removable);

/// <summary>A USB disk: its number, identity, size, the USB device it hangs from, and its volumes.</summary>
public sealed record UsbDisk(
    int DiskNumber,
    StorageIdentity Identity,
    long Size,
    int BytesPerSector,
    string UsbInstanceId,
    string DiskInstanceId,
    IReadOnlyList<UsbVolume> Volumes);

/// <summary>A volume on a disk: its drive letters or folders, label, file system and sizes.</summary>
public sealed record UsbVolume(int DiskNumber, string VolumeName, IReadOnlyList<string> Paths, string Label, string FileSystem, long Capacity, long Free);

/// <summary>
/// USB disks and what Windows mounted from them (device classes plan, stage D9, ADR 0192): the disk
/// interface's devices walked up to their USB device, the disk number from
/// IOCTL_STORAGE_GET_DEVICE_NUMBER, the SCSI identity from IOCTL_STORAGE_QUERY_PROPERTY, the size from
/// IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, and each volume matched to its disk through its extents. Every
/// handle is opened with no access rights, so nothing needs administrator rights and nothing is read
/// from the media; a disk that is not under USB is never opened.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class UsbStorage
{
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x700A0;
    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x560000;
    private const uint CR_REMOVE_VETOED = 0x17;

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", SetLastError = true)]
    private static partial nint FindFirstVolume(char* name, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextVolumeW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextVolume(nint find, char* name, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindVolumeClose(nint find);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNamesForVolumeNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathNamesForVolumeName(string volume, char* names, uint length, uint* returned);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(string root, char* label, uint labelLength, uint* serial, uint* maxComponent, uint* flags, char* fileSystem, uint fileSystemLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string root, ulong* freeToCaller, ulong* total, ulong* free);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadErrorMode(uint mode, uint* old);

    /// <summary>Every disk that hangs from a USB device, with its volumes, in disk-number order.</summary>
    public static IReadOnlyList<UsbDisk> Disks()
    {
        var disks = new List<UsbDisk>();
        foreach ((string instance, string path) in UsbEnumerator.InterfacePaths(UsbNative.GUID_DEVINTERFACE_DISK))
        {
            uint devInst = 0;
            if (UsbNative.CM_Locate_DevNode(&devInst, instance, 0) != UsbNative.CR_SUCCESS || UsbCrossReference.UsbAncestor(devInst) is not { } usb)
            {
                continue;
            }

            using SafeFileHandle disk = UsbNative.CreateFile(path, 0, UsbNative.FILE_SHARE_READ | UsbNative.FILE_SHARE_WRITE, 0, UsbNative.OPEN_EXISTING, 0, 0);
            if (disk.IsInvalid)
            {
                continue;
            }

            byte[] number = new byte[12];
            if (UsbNative.Ioctl(disk, IOCTL_STORAGE_GET_DEVICE_NUMBER, number) < 8)
            {
                continue;
            }

            byte[] descriptor = new byte[1024]; // in: STORAGE_PROPERTY_QUERY { StorageDeviceProperty, PropertyStandardQuery }, all zeros
            int length = UsbNative.Ioctl(disk, IOCTL_STORAGE_QUERY_PROPERTY, descriptor);
            byte[] geometry = new byte[256];
            (long size, int sector) = UsbNative.Ioctl(disk, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, geometry) >= 32 ? Geometry(geometry) : (0, 0);
            disks.Add(new UsbDisk((int)BinaryPrimitives.ReadUInt32LittleEndian(number.AsSpan(4)),
                Identity(descriptor.AsSpan(0, Math.Max(0, length))), size, sector, usb, instance, []));
        }

        if (disks.Count == 0)
        {
            return disks;
        }

        Dictionary<int, List<UsbVolume>> volumes = VolumesByDisk(disks.Select(static d => d.DiskNumber).ToHashSet());
        return disks.Select(d => d with { Volumes = volumes.TryGetValue(d.DiskNumber, out List<UsbVolume>? list) ? list : [] })
            .OrderBy(static d => d.DiskNumber).ToList();
    }

    /// <summary>STORAGE_DEVICE_DESCRIPTOR: the identity strings at their offsets, RemovableMedia, and BusType named.</summary>
    public static StorageIdentity Identity(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 32)
        {
            return new StorageIdentity("", "", "", "", "", false);
        }

        byte[] io = descriptor.ToArray();
        string At(int offsetField)
        {
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(io.AsSpan(offsetField));
            if (offset <= 0 || offset >= io.Length)
            {
                return "";
            }

            int end = Array.IndexOf(io, (byte)0, offset);
            return Encoding.ASCII.GetString(io, offset, (end < 0 ? io.Length : end) - offset).Trim();
        }

        return new StorageIdentity(At(12), At(16), At(20), At(24), BusName((int)BinaryPrimitives.ReadUInt32LittleEndian(io.AsSpan(28))), io[10] != 0);
    }

    /// <summary>STORAGE_BUS_TYPE by name.</summary>
    public static string BusName(int bus) => bus switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        0x0A => "SAS",
        0x0B => "SATA",
        0x0C => "SD",
        0x0D => "MMC",
        0x0E => "Virtual",
        0x0F => "File-backed virtual",
        0x10 => "Storage Spaces",
        0x11 => "NVMe",
        0x12 => "SCM",
        0x13 => "UFS",
        _ => $"bus {bus}",
    };

    /// <summary>DISK_GEOMETRY_EX: the disk's size and its bytes per sector.</summary>
    public static (long Size, int BytesPerSector) Geometry(ReadOnlySpan<byte> geometry) =>
        geometry.Length < 32 ? (0, 0) : (BinaryPrimitives.ReadInt64LittleEndian(geometry[24..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(geometry[20..]));

    /// <summary>Each volume on one of <paramref name="wanted"/>, filed under the disks its extents lie on.</summary>
    private static Dictionary<int, List<UsbVolume>> VolumesByDisk(HashSet<int> wanted)
    {
        var result = new Dictionary<int, List<UsbVolume>>();
        char* name = stackalloc char[260];
        uint previous = 0;
        SetThreadErrorMode(0x8001, &previous); // SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX: an empty card reader asks nothing
        nint find = FindFirstVolume(name, 260);
        if (find == -1)
        {
            SetThreadErrorMode(previous, null);
            return result;
        }

        try
        {
            do
            {
                string volume = new(name);
                int[] onDisks;
                using (SafeFileHandle handle = UsbNative.CreateFile(volume.TrimEnd('\\'), 0, UsbNative.FILE_SHARE_READ | UsbNative.FILE_SHARE_WRITE,
                    0, UsbNative.OPEN_EXISTING, 0, 0))
                {
                    if (handle.IsInvalid)
                    {
                        continue;
                    }

                    byte[] extents = new byte[8 + (24 * 16)];
                    if (UsbNative.Ioctl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, extents) < 8)
                    {
                        continue;
                    }

                    int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(extents);
                    onDisks = Enumerable.Range(0, Math.Min(count, 16))
                        .Select(i => (int)BinaryPrimitives.ReadUInt32LittleEndian(extents.AsSpan(8 + (i * 24)))).Distinct().ToArray();
                }

                foreach (int disk in onDisks.Where(wanted.Contains))
                {
                    if (!result.TryGetValue(disk, out List<UsbVolume>? list))
                    {
                        result[disk] = list = [];
                    }

                    list.Add(Describe(disk, volume));
                }
            }
            while (FindNextVolume(find, name, 260));
        }
        finally
        {
            FindVolumeClose(find);
            SetThreadErrorMode(previous, null);
        }

        return result;
    }

    private static UsbVolume Describe(int disk, string volume)
    {
        var paths = new List<string>();
        char* names = stackalloc char[1024];
        uint returned = 0;
        if (GetVolumePathNamesForVolumeName(volume, names, 1024, &returned))
        {
            paths.AddRange(new string(names, 0, (int)Math.Min(returned, 1024)).Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }

        char* label = stackalloc char[261];
        char* fs = stackalloc char[261];
        string labelText = "";
        string fsText = "";
        if (GetVolumeInformation(volume, label, 261, null, null, null, fs, 261))
        {
            labelText = new string(label);
            fsText = new string(fs);
        }

        ulong total = 0;
        ulong free = 0;
        ulong caller = 0;
        GetDiskFreeSpaceEx(volume, &caller, &total, &free);
        return new UsbVolume(disk, volume, paths, labelText, fsText, (long)total, (long)free);
    }

    /// <summary>PNP_VETO_TYPE as a reason: why Windows would not let a device go.</summary>
    public static string VetoReason(int veto) => veto switch
    {
        1 => "a legacy device holds it",
        2 => "a handle to it is still closing",
        3 => "a program is using it",
        4 => "a service is using it",
        5 => "a file or handle is open on it",
        6 => "another device refused",
        7 => "its driver refused",
        8 => "the request is not valid for it",
        9 => "there is not enough power",
        10 => "Windows cannot do without it",
        11 => "a legacy driver holds it",
        12 => "this user lacks the rights",
        13 => "it is already removed",
        _ => "for a reason it did not give",
    };

    /// <summary>
    /// Asks Windows to eject a USB device (CM_Request_Device_Eject), as "Safely Remove Hardware" does. A
    /// veto, such as a program with a file open on its volume, is a <see cref="DeviceIOException"/> with
    /// the veto's reason and, where Windows names it, what holds the device.
    /// </summary>
    public static void Eject(string usbInstanceId)
    {
        uint devInst = 0;
        if (UsbNative.CM_Locate_DevNode(&devInst, usbInstanceId, 0) != UsbNative.CR_SUCCESS)
        {
            throw new DeviceOpenException($"The device {usbInstanceId} is not present.");
        }

        int veto = 0;
        char* vetoName = stackalloc char[400];
        vetoName[0] = '\0';
        uint result = UsbNative.CM_Request_Device_Eject(devInst, &veto, vetoName, 400, 0);
        if (result != UsbNative.CR_SUCCESS || veto != 0)
        {
            string held = new(vetoName);
            string reason = result is CR_REMOVE_VETOED or UsbNative.CR_SUCCESS ? VetoReason(veto) : $"configuration manager error 0x{result:X2}";
            throw new DeviceIOException($"Windows would not eject the device: {reason}{(held.Length > 0 ? $" ({held})" : "")}.", veto);
        }
    }
}
