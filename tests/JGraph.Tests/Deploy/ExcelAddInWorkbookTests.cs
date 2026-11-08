using System.IO.Compression;
using System.Text;
using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The Excel add-in workbook JGraph writes with no Excel (compiler and SDK plan, stage K17e, decision
/// KO12): the MS-OVBA compression and the PROJECT stream's encryption against MathWorks' own
/// <c>FunctionWizard2007.xlam</c>, the compound file written and read back, MathWorks' workbook
/// read through the same reader where MATLAB R2025b is installed, and the <c>.xlam</c>'s parts and
/// project as a build writes them. Excel's own acceptance of the file is measured by
/// <c>run-k17.ps1</c>'s cells step (the spike of 2026-11-07 opened it as an add-in and evaluated its
/// function), not here: the unit tests start no Excel.
/// </summary>
public sealed class ExcelAddInWorkbookTests
{
    private static readonly string Repo = FindRepo();
    private static readonly string MathWorksWorkbook = @"C:\Program Files\MATLAB\R2025b\toolbox\matlabxl\matlabxl\win64\FunctionWizard2007.xlam";

    [Fact]
    public void TheCompressionRoundTripsAndTheEncryptionIsOffices()
    {
        // The recorded module (12,095 bytes: three chunks of copy tokens), a run (a copy token or two
        // per chunk), noise (tokens would not fit, so a raw chunk), nothing, three bytes.
        byte[] module = Encoding.Latin1.GetBytes(RecordedModule("bas.file.k17bas.bas"));
        byte[] noise = Noise(5000);
        foreach (byte[] data in new[] { module, [.. Enumerable.Repeat((byte)'a', 10000)], noise, [], "abc"u8.ToArray() })
        {
            byte[] compressed = VbaProject.Compress(data);
            Assert.Equal(0x01, compressed[0]);
            Assert.Equal(data, VbaProject.Decompress(compressed));
        }

        Assert.True(VbaProject.Compress(module).Length < module.Length / 2, "the module compresses");
        byte[] raw = VbaProject.Compress(noise);
        Assert.Equal(0, raw[2] & 0x80);
        // The first chunk raw (4,096 bytes), the second (904 bytes) as literal tokens with a flag byte per eight.
        Assert.Equal(1 + 2 + 4096 + 2 + 904 + (904 + 7) / 8, raw.Length);

        // MS-OVBA 2.4.3's encryption, checked against MathWorks' FunctionWizard2007.xlam's PROJECT
        // stream: its CMG holds a four-byte protection state (4), its GC a one-byte visibility (0).
        Assert.Equal(new byte[] { 4, 0, 0, 0 }, VbaProject.Decrypt("989A3442384238463C463C"));
        Assert.Equal(new byte[] { 0 }, VbaProject.Decrypt("C8CA647765776577"));
        const string id = "{1F2E3D4C-5B6A-7988-0123-456789ABCDEF}";
        foreach (byte seed in new byte[] { 0x00, 0x58, 0x5A, 0x5C, 0x5E, 0xFF })
        {
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, VbaProject.Decrypt(VbaProject.Encrypt(id, [1, 2, 3, 4], seed)));
            Assert.Equal(new byte[] { 0xFF }, VbaProject.Decrypt(VbaProject.Encrypt(id, [0xFF], seed)));
        }
    }

    [Fact]
    public void TheCompoundFileRoundTrips()
    {
        // Streams in the mini stream (under 4,096 bytes), at the cutoff and above it (their own
        // sectors), an empty one, and a storage inside a storage; the names come back in the file's
        // order (by length, then case-insensitively).
        var root = new CompoundFileStorage { Name = "Root Entry" };
        var vba = new CompoundFileStorage { Name = "VBA" };
        vba.Streams.Add(("Module1", Noise(700)));
        vba.Streams.Add(("dir", Noise(4096)));
        vba.Streams.Add(("_VBA_PROJECT", [0xCC, 0x61, 0xFF, 0xFF, 0, 0, 0]));
        vba.Streams.Add(("Big", Noise(9000)));
        vba.Streams.Add(("Empty", []));
        var inner = new CompoundFileStorage { Name = "Inner" };
        inner.Streams.Add(("x", [1, 2, 3]));
        vba.Storages.Add(inner);
        root.Storages.Add(vba);
        root.Streams.Add(("PROJECT", Encoding.Latin1.GetBytes("ID=\"{0}\"\r\n")));
        root.Streams.Add(("PROJECTwm", [0, 0]));

        byte[] file = CompoundFile.Write(root);
        Assert.Equal(0, file.Length % 512);
        Assert.Equal(file, CompoundFile.Write(root));
        CompoundFileStorage back = CompoundFile.Read(file);
        Assert.Equal(["PROJECT", "PROJECTwm"], back.Streams.Select(static s => s.Name));
        Assert.Equal(["VBA"], back.Storages.Select(static s => s.Name));
        CompoundFileStorage vbaBack = back.Storage("VBA")!;
        Assert.Equal(["Big", "dir", "Empty", "Module1", "_VBA_PROJECT"], vbaBack.Streams.Select(static s => s.Name));
        foreach ((string name, byte[] data) in vba.Streams)
        {
            Assert.Equal(data, vbaBack.Stream(name));
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, vbaBack.Storage("Inner")!.Stream("x"));
        Assert.Equal(Encoding.Latin1.GetBytes("ID=\"{0}\"\r\n"), back.Stream("PROJECT"));
        Assert.Throws<InvalidDataException>(() => CompoundFile.Read(Noise(1024)));
    }

    [Fact]
    public void MathWorksAddInWorkbookReadsBackWhereInstalled()
    {
        if (!File.Exists(MathWorksWorkbook))
        {
            // No MATLAB R2025b on this machine: the reader is held to JGraph's own files alone.
            return;
        }

        ExcelAddInWorkbookContents fw = ExcelAddInWorkbook.Read(MathWorksWorkbook);
        Assert.Contains("application/vnd.ms-excel.addin.macroEnabled.main+xml", fw.ContentTypes, StringComparison.Ordinal);
        Assert.Equal("MLFProject2007", fw.Project.Name);
        Assert.Equal(["ThisWorkbook", "Sheet1", "Sheet2", "Sheet3", "Module1"], fw.Project.Modules.Select(static m => m.Name));
        Assert.Equal([true, true, true, true, false], fw.Project.Modules.Select(static m => m.IsDocument));
        Assert.Contains("Public Sub functionWizardButton_Click(control As IRibbonControl)", fw.Project.Modules[4].Source, StringComparison.Ordinal);
        Assert.StartsWith("Attribute VB_Name = \"ThisWorkbook\"\r\nAttribute VB_Base = \"0{00020819-0000-0000-C000-000000000046}\"", fw.Project.Modules[0].Source, StringComparison.Ordinal);
        Assert.Equal(2, fw.Project.References.Count);
        Assert.StartsWith(@"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#", fw.Project.References[0], StringComparison.Ordinal);
        Assert.StartsWith(@"*\G{2DF8D04C-5BFA-101B-BDE5-00AA0044DE52}#2.0#0#", fw.Project.References[1], StringComparison.Ordinal);
        Assert.Contains("Name=\"MLFProject2007\"", fw.Project.ProjectStream, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkbookCarriesTheModuleAndIsTheSameEveryTime()
    {
        string module = RecordedModule("bas.file.k17bas.bas").Replace("MWComUtil.MWUtil25.2", ExcelVisualBasic.UtilityProgId, StringComparison.Ordinal);
        byte[] bytes = ExcelAddInWorkbook.Write("k17bas", module);
        Assert.Equal(bytes, ExcelAddInWorkbook.Write("k17bas", module));
        Assert.NotEqual(bytes, ExcelAddInWorkbook.Write("k17other", module));
        Assert.NotEqual(Guid.Empty, ExcelAddInWorkbook.ProjectId("k17bas"));
        Assert.Equal(ExcelAddInWorkbook.ProjectId("k17bas"), ExcelAddInWorkbook.ProjectId("k17bas"));

        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k17e-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string path = ExcelAddInWorkbook.Write(folder, "k17bas", module);
            Assert.Equal(Path.Combine(folder, "k17bas.xlam"), path);
            ExcelAddInWorkbookContents back = ExcelAddInWorkbook.Read(path);
            Assert.Equal(["[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml", "xl/styles.xml", "xl/vbaProject.bin"], back.Parts);
            Assert.Contains("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.ms-excel.addin.macroEnabled.main+xml\"/>", back.ContentTypes, StringComparison.Ordinal);
            Assert.Contains("<Default Extension=\"bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>", back.ContentTypes, StringComparison.Ordinal);
            Assert.Contains("<workbookPr codeName=\"ThisWorkbook\"/>", back.Workbook, StringComparison.Ordinal);
            Assert.Contains("visibility=\"veryHidden\"", back.Workbook, StringComparison.Ordinal);

            VbaProjectContents project = back.Project;
            Assert.Equal(ExcelAddInWorkbook.ProjectName, project.Name);
            Assert.Equal(ExcelAddInWorkbook.ProjectId("k17bas"), project.Id);
            Assert.Equal(["ThisWorkbook", "Module1"], project.Modules.Select(static m => m.Name));
            Assert.True(project.Modules[0].IsDocument);
            Assert.Equal(VbaProject.ThisWorkbookSource, project.Modules[0].Source);
            Assert.False(project.Modules[1].IsDocument);
            Assert.Equal("Attribute VB_Name = \"Module1\"\r\n" + module, project.Modules[1].Source);
            Assert.Equal(2, project.References.Count);
            Assert.Contains("Document=ThisWorkbook/&H00000000\r\nModule=Module1\r\nName=\"VBAProject\"\r\n", project.ProjectStream, StringComparison.Ordinal);
            Assert.Contains("[Host Extender Info]\r\n&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n", project.ProjectStream, StringComparison.Ordinal);
            Assert.Contains("[Workspace]\r\nThisWorkbook=0, 0, 0, 0, C\r\nModule1=0, 0, 0, 0, C\r\n", project.ProjectStream, StringComparison.Ordinal);
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, VbaProject.Decrypt(Property(project.ProjectStream, "CMG")));
            Assert.Equal(new byte[] { 0 }, VbaProject.Decrypt(Property(project.ProjectStream, "DPB")));
            Assert.Equal(new byte[] { 0xFF }, VbaProject.Decrypt(Property(project.ProjectStream, "GC")));

            // The project's own streams: no performance cache (version 0xFFFF), the module names twice in PROJECTwm.
            using ZipArchive zip = ZipFile.OpenRead(path);
            using var bin = new MemoryStream();
            using (Stream entry = zip.GetEntry("xl/vbaProject.bin")!.Open())
            {
                entry.CopyTo(bin);
            }

            CompoundFileStorage root = CompoundFile.Read(bin.ToArray());
            Assert.Equal(new byte[] { 0xCC, 0x61, 0xFF, 0xFF, 0, 0, 0 }, root.Storage("VBA")!.Stream("_VBA_PROJECT"));
            byte[] wm =
            [
                .. Encoding.Latin1.GetBytes("ThisWorkbook\0"), .. Encoding.Unicode.GetBytes("ThisWorkbook\0"),
                .. Encoding.Latin1.GetBytes("Module1\0"), .. Encoding.Unicode.GetBytes("Module1\0"),
                0, 0,
            ];
            Assert.Equal(wm, root.Stream("PROJECTwm"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Property(string projectStream, string name)
    {
        string line = projectStream.Split("\r\n").Single(l => l.StartsWith(name + "=\"", StringComparison.Ordinal));
        return line[(name.Length + 2)..^1];
    }

    private static byte[] Noise(int count)
    {
        var bytes = new byte[count];
        uint state = 0x9E3779B9;
        for (int k = 0; k < count; k++)
        {
            state = state * 1664525u + 1013904223u;
            bytes[k] = (byte)(state >> 24);
        }

        return bytes;
    }

    /// <summary>A recorded module's text, CR LF line ends and a line end after the last line, as the recording prints it under <paramref name="key"/>.</summary>
    internal static string RecordedModule(string key)
    {
        string[] all = File.ReadAllLines(Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "recordings", "k17_excel.out.txt"));
        int at = Array.FindIndex(all, l => l.StartsWith(key + "\t", StringComparison.Ordinal));
        Assert.True(at >= 0, key + " is not in the recording");
        return string.Join("\r\n", all.Skip(at + 1).TakeWhile(static l => l.StartsWith("  |", StringComparison.Ordinal)).Select(static l => l[3..])) + "\r\n";
    }

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }
}
