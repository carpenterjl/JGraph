using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices.Storage;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// USB mass storage (device classes plan, stage D9, ADR 0192): the storage stack's descriptors decoded
/// from built buffers, the veto reasons, and a walk of this machine's USB disks, which opens nothing
/// with access rights and reads nothing from the media.
/// </summary>
[SupportedOSPlatform("windows")]
public class StorageTests
{
    [Fact]
    public void AStorageDeviceDescriptorDecodes()
    {
        // STORAGE_DEVICE_DESCRIPTOR: Version, Size, DeviceType, Modifier, RemovableMedia, CommandQueueing,
        // then the vendor, product, revision and serial offsets, BusType, and the strings after it.
        byte[] io = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(io, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(io.AsSpan(4), 128);
        io[10] = 1;
        int at = 40;
        int Put(string text)
        {
            int start = at;
            Encoding.ASCII.GetBytes(text).CopyTo(io, at);
            at += text.Length + 1;
            return start;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(io.AsSpan(12), (uint)Put("SanDisk "));
        BinaryPrimitives.WriteUInt32LittleEndian(io.AsSpan(16), (uint)Put("Ultra           "));
        BinaryPrimitives.WriteUInt32LittleEndian(io.AsSpan(20), (uint)Put("1.00"));
        BinaryPrimitives.WriteUInt32LittleEndian(io.AsSpan(24), 0); // no serial number
        BinaryPrimitives.WriteUInt32LittleEndian(io.AsSpan(28), 7);
        Assert.Equal(new StorageIdentity("SanDisk", "Ultra", "1.00", "", "USB", true), UsbStorage.Identity(io));
        Assert.Equal(new StorageIdentity("", "", "", "", "", false), UsbStorage.Identity(io.AsSpan(0, 16)));
        Assert.Equal("NVMe", UsbStorage.BusName(0x11));
        Assert.Equal("bus 99", UsbStorage.BusName(99));
    }

    [Fact]
    public void DriveGeometryGivesTheSizeAndSector()
    {
        byte[] geometry = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(geometry.AsSpan(20), 512);
        BinaryPrimitives.WriteInt64LittleEndian(geometry.AsSpan(24), 31_914_983_424);
        Assert.Equal((31_914_983_424L, 512), UsbStorage.Geometry(geometry));
        Assert.Equal((0L, 0), UsbStorage.Geometry(new byte[8]));
    }

    [Fact]
    public void AVetoIsNamed()
    {
        Assert.Equal("a file or handle is open on it", UsbStorage.VetoReason(5));
        Assert.Equal("a program is using it", UsbStorage.VetoReason(3));
        Assert.Equal("for a reason it did not give", UsbStorage.VetoReason(0));
    }

    [Fact]
    public void ThisMachinesUsbDisksAreDescribedWithoutOpeningTheirMedia()
    {
        IReadOnlyList<UsbDisk> disks = UsbStorage.Disks();
        Assert.Equal(disks.Count, disks.Select(static d => d.DiskNumber).Distinct().Count());
        foreach (UsbDisk disk in disks)
        {
            Assert.StartsWith("USB", disk.UsbInstanceId, StringComparison.OrdinalIgnoreCase);
            Assert.All(disk.Volumes, v => Assert.Equal(disk.DiskNumber, v.DiskNumber));
        }
    }

    [Fact]
    public void EjectingADeviceThatIsNotThereIsRefused() =>
        Assert.Throws<JGraph.Devices.DeviceOpenException>(() => UsbStorage.Eject("USB\\VID_FFFF&PID_FFFF\\NONE"));
}
