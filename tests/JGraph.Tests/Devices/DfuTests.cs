using System.Text;
using JGraph.Devices.Dfu;
using JGraph.Devices.Simulation;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// DFU (device classes plan, stage D8, ADR 0191): the firmware file formats, DfuSe memory layouts, and
/// the protocol against the simulated loaders — a DfuSe one with flash that must be erased before it
/// is programmed, a plain DFU 1.1 one, and a run-time interface that detaches.
/// </summary>
public class DfuTests
{
    private static DfuDevice Open(SimulatedDfu sim) =>
        new(sim, 0, sim.Functional, sim.Kind != SimulatedDfuKind.Runtime, sim.AlternateNames) { Timeout = TimeSpan.FromSeconds(1) };

    [Fact]
    public void TheSuffixCrcIsDfuUtils()
    {
        // CRC-32 from 0xFFFFFFFF with no final inversion: the check value's complement.
        Assert.Equal(0x340BC6D9u, DfuFiles.Crc("123456789"u8));
    }

    [Fact]
    public void ASuffixRoundTripsAndADamagedFileFailsItsCrc()
    {
        byte[] payload = Enumerable.Range(0, 300).Select(static i => (byte)i).ToArray();
        byte[] file = DfuFiles.WithSuffix(payload, 0x0483, 0xDF11, 0x2200);
        DfuImage image = DfuFiles.Parse(file, ".dfu");
        Assert.Equal("DFU", image.Format);
        Assert.True(image.HasSuffix);
        Assert.True(image.CrcOk);
        Assert.Equal((0x0483, 0xDF11, 0x2200, 0x0100), (image.VendorId, image.ProductId, image.DeviceRelease, image.DfuVersion));
        Assert.Equal(payload, Assert.Single(Assert.Single(image.Targets).Elements).Data);

        file[10] ^= 0x01;
        Assert.False(DfuFiles.Parse(file, ".dfu").CrcOk);

        DfuImage raw = DfuFiles.Parse(payload, ".bin");
        Assert.Equal("binary", raw.Format);
        Assert.False(raw.HasSuffix);
    }

    [Fact]
    public void ADfuSeContainerRoundTrips()
    {
        DfuTarget flash = new(0, "Internal Flash", [new DfuElement(0x08000000, [1, 2, 3, 4]), new DfuElement(0x08004000, [5, 6])]);
        DfuTarget options = new(1, "", [new DfuElement(0x1FFFC000, [0xAA, 0xEC])]);
        DfuImage image = DfuFiles.Parse(DfuFiles.DfuSeFile([flash, options], 0x0483, 0xDF11, 0x0200), ".dfu");
        Assert.Equal("DfuSe", image.Format);
        Assert.True(image.CrcOk);
        Assert.Equal(0x011A, image.DfuVersion);
        Assert.Equal(2, image.Targets.Count);
        Assert.Equal("Internal Flash", image.Targets[0].Name);
        Assert.Equal([0x08000000u, 0x08004000u], image.Targets[0].Elements.Select(static e => e.Address));
        Assert.Equal(new byte[] { 5, 6 }, image.Targets[0].Elements[1].Data);
        Assert.Equal(1, image.Targets[1].AlternateSetting);
    }

    [Fact]
    public void IntelHexGathersRunsAtTheirAddresses()
    {
        // An upper address of 0x0800, four bytes at 0x08000000 and two at 0x08000010, then the end record.
        string hex = ":020000040800F2\n:0400000001020304F2\n:020010000506E3\n:00000001FF\n";
        DfuImage image = DfuFiles.Parse(Encoding.ASCII.GetBytes(hex), ".hex");
        Assert.Equal("Intel HEX", image.Format);
        IReadOnlyList<DfuElement> elements = Assert.Single(image.Targets).Elements;
        Assert.Equal(2, elements.Count);
        Assert.Equal(0x08000000u, elements[0].Address);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, elements[0].Data);
        Assert.Equal(0x08000010u, elements[1].Address);
        Assert.Equal(new byte[] { 5, 6 }, elements[1].Data);

        Assert.Throws<InvalidDataException>(() => DfuFiles.Parse(":0400000001020304F3\n"u8.ToArray(), ".hex"));
    }

    [Fact]
    public void ADfuSeLayoutParsesAsDfuUtilParsesIt()
    {
        DfuSeLayout layout = DfuSeLayout.Parse("@Internal Flash  /0x08000000/04*016Kg,01*064Kg,07*128Kg")!;
        Assert.Equal("Internal Flash", layout.Name);
        Assert.Equal(3, layout.Segments.Count);
        Assert.Equal((0x08000000u, 0x08010000u, 16384, 4), (layout.Segments[0].Start, layout.Segments[0].End, layout.Segments[0].PageSize, layout.Segments[0].Pages));
        Assert.Equal(0x08100000u, layout.Segments[2].End);
        Assert.True(layout.Segments[0] is { Readable: true, Erasable: true, Writeable: true });
        Assert.Equal([0x08000000u, 0x08004000u], layout.PagesTouched(0x08003F00, 0x200));
        Assert.Null(layout.Unwriteable(0x08000000, 0x100000));
        Assert.NotNull(layout.Unwriteable(0x080FFFFF, 2));

        DfuSeSegment options = Assert.Single(DfuSeLayout.Parse("@Option Bytes  /0x1FFFC000/01*016 e")!.Segments);
        Assert.True(options is { PageSize: 16, Readable: true, Erasable: false, Writeable: true });
        Assert.Null(DfuSeLayout.Parse("Firmware"));
    }

    [Fact]
    public void ADfuSeDownloadErasesThePagesItTouchesThenUploadsTheSame()
    {
        var sim = new SimulatedDfu(SimulatedDfuKind.DfuSe);
        using DfuDevice dfu = Open(sim);
        Assert.True(dfu.IsDfuSe);
        byte[] image = Enumerable.Range(0, 5000).Select(static i => (byte)(i % 251)).ToArray();
        var progress = new List<long>();
        dfu.DownloadImage(image, 0x08003F00, progress.Add);
        Assert.Equal(image, sim.Peek(0x08003F00, image.Length));
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 16), sim.Peek(0x08000000, 16)); // the old application's page was erased
        Assert.Equal(new long[] { 2048, 4096, 5000 }, progress);
        Assert.Equal(image, dfu.UploadImage(image.Length, 0x08003F00));

        dfu.Alternate = 1;
        dfu.DownloadImage([0x12, 0x34], SimulatedDfu.OptionStart);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x55 }, dfu.UploadImage(3, SimulatedDfu.OptionStart));

        dfu.Alternate = 0;
        dfu.Leave(0x08000000);
        Assert.True(sim.Gone);
    }

    [Fact]
    public void ProgrammingUnerasedFlashAndAFailedWriteReportTheDevicesStatus()
    {
        var sim = new SimulatedDfu(SimulatedDfuKind.DfuSe);
        using DfuDevice dfu = Open(sim);
        dfu.SetAddress(0x08000000);
        DfuException unerased = Assert.Throws<DfuException>(() => dfu.Download(2, [0xFF, 0xFF]));
        Assert.Equal("errPROG", unerased.Status!.Value.StatusName);
        Assert.Equal("dfuERROR", unerased.Status.Value.StateName);

        sim.FailAtWrite = 3; // the second write from here (the first failed above)
        DfuException failed = Assert.Throws<DfuException>(() => dfu.DownloadImage(new byte[4096], 0x08004000));
        Assert.Contains("errWRITE", failed.Message, StringComparison.Ordinal);

        dfu.Idle(); // clears the error
        Assert.Equal(2, dfu.GetState());
        dfu.MassErase();
        Assert.Equal(Enumerable.Repeat((byte)0xFF, 64), sim.Peek(0x08000000, 64));
    }

    [Fact]
    public void APlainDfuDownloadManifestsAndUploadsBack()
    {
        var sim = new SimulatedDfu(SimulatedDfuKind.Dfu);
        using DfuDevice dfu = Open(sim);
        Assert.False(dfu.IsDfuSe);
        Assert.Equal(3000, dfu.UploadImage(1 << 20, 0).Length); // the firmware already there, ended by a short block

        byte[] image = Enumerable.Range(0, 1000).Select(static i => (byte)(255 - (i % 256))).ToArray();
        dfu.DownloadImage(image, 0);
        Assert.Equal(image, sim.Image);
        Assert.Equal(2, dfu.GetState()); // manifestation tolerant: back to dfuIDLE
        Assert.Equal(image, dfu.UploadImage(1 << 20, 0));
        Assert.Throws<DfuException>(() => dfu.MassErase());
    }

    [Fact]
    public void ARunTimeInterfaceDetachesIntoTheLoader()
    {
        var sim = new SimulatedDfu(SimulatedDfuKind.Runtime);
        using DfuDevice dfu = Open(sim);
        Assert.False(dfu.DfuMode);
        Assert.Contains("application", Assert.Throws<DfuException>(dfu.Idle).Message, StringComparison.Ordinal);
        dfu.Detach(1000);
        Assert.True(sim.Gone);
        Assert.Equal(SimulatedDfuKind.DfuSe, sim.Detached!.Kind);
        Assert.Equal(sim.Info.Location, sim.Detached.Info.Location);
    }
}
