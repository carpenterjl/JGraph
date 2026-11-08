using System.IO.Compression;
using System.Text;

namespace JGraph.Deploy;

/// <summary>What <see cref="ExcelAddInWorkbook.Read"/> finds in an add-in workbook: its parts' names, the content types part, the workbook part and the VBA project.</summary>
public sealed record ExcelAddInWorkbookContents(IReadOnlyList<string> Parts, string ContentTypes, string Workbook, VbaProjectContents Project);

/// <summary>
/// The Excel add-in workbook (compiler and SDK plan, stage K17e; decision KO12): where R2025b's mcc
/// drives Excel to import the <c>.bas</c> into a new workbook saved as <c>&lt;name&gt;.xla</c> (and
/// fails, with COM_WARN_FAILED_CREATE_EXCEL_ADDIN, wherever Excel does not trust programmatic access
/// to its VBA projects, as it did not on the recording machine), JGraph writes <c>&lt;name&gt;.xlam</c>
/// as a file with no Excel: an Office Open XML package of the add-in kind (the workbook part's
/// content type <c>application/vnd.ms-excel.addin.macroEnabled.main+xml</c>, one empty sheet, the
/// workbook window very hidden as an add-in's is) carrying a <c>vbaProject.bin</c> that
/// <see cref="VbaProject"/> writes with the module as <c>Module1</c>, the name Excel's import gives
/// a module file with no <c>Attribute VB_Name</c> line. Excel opens the file as an add-in, compiles
/// the module and serves its functions to any workbook's cells. The entries' times are fixed, so
/// one add-in's workbook is the same bytes build after build.
/// </summary>
public static class ExcelAddInWorkbook
{
    /// <summary>The module's name in the project.</summary>
    public const string ModuleName = "Module1";

    /// <summary>The VBA project's name, Excel's default.</summary>
    public const string ProjectName = "VBAProject";

    private const string ContentTypesPart = "[Content_Types].xml";
    private const string WorkbookPart = "xl/workbook.xml";
    private const string ProjectPart = "xl/vbaProject.bin";
    private static readonly DateTimeOffset FixedTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>The project's id, derived from the add-in's name as the add-in's other ids are.</summary>
    public static Guid ProjectId(string addInName) => ExcelComGuids.Derive("vbaproject:" + addInName);

    /// <summary>Writes <c>&lt;name&gt;.xlam</c> into <paramref name="folder"/> holding <paramref name="moduleText"/>, and answers its path.</summary>
    public static string Write(string folder, string addInName, string moduleText)
    {
        ArgumentNullException.ThrowIfNull(addInName);
        string path = Path.Combine(folder, addInName + ".xlam");
        File.WriteAllBytes(path, Write(addInName, moduleText));
        return path;
    }

    /// <summary>The workbook's bytes for the add-in <paramref name="addInName"/> with <paramref name="moduleText"/> as its module.</summary>
    public static byte[] Write(string addInName, string moduleText)
    {
        ArgumentNullException.ThrowIfNull(addInName);
        ArgumentNullException.ThrowIfNull(moduleText);
        byte[] project = VbaProject.Write(ProjectName, ProjectId(addInName), [VbaProject.ThisWorkbook, VbaProject.StandardModule(ModuleName, moduleText)]);
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, ContentTypesPart, ContentTypes);
            Add(zip, "_rels/.rels", PackageRelationships);
            Add(zip, WorkbookPart, Workbook);
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
            Add(zip, "xl/worksheets/sheet1.xml", Sheet);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, ProjectPart, project);
        }

        return stream.ToArray();
    }

    /// <summary>Reads an add-in workbook back: its parts, the content types and workbook parts' text and its VBA project.</summary>
    /// <exception cref="InvalidDataException">The file is not an add-in workbook this reader takes.</exception>
    public static ExcelAddInWorkbookContents Read(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        List<string> parts = [.. zip.Entries.Select(static e => e.FullName)];
        string contentTypes = Text(zip, ContentTypesPart);
        string workbook = Text(zip, WorkbookPart);
        ZipArchiveEntry project = zip.GetEntry(ProjectPart) ?? throw new InvalidDataException("The workbook has no vbaProject.bin part.");
        using var bytes = new MemoryStream();
        using (Stream entry = project.Open())
        {
            entry.CopyTo(bytes);
        }

        return new ExcelAddInWorkbookContents(parts, contentTypes, workbook, VbaProject.Read(bytes.ToArray()));
    }

    private static string Text(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new InvalidDataException($"The workbook has no {name} part.");
        using var reader = new StreamReader(entry.Open(), Utf8);
        return reader.ReadToEnd();
    }

    private static void Add(ZipArchive zip, string name, string text) => Add(zip, name, Utf8.GetBytes(text));

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Default Extension=\"bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.ms-excel.addin.macroEnabled.main+xml\"/>"
        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
        + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
        + "</Types>";

    private const string PackageRelationships =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
        + "</Relationships>";

    private const string Workbook =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
        + "<fileVersion appName=\"xl\" lastEdited=\"7\" lowestEdited=\"7\" rupBuild=\"26730\"/>"
        + "<workbookPr codeName=\"ThisWorkbook\"/>"
        + "<bookViews><workbookView visibility=\"veryHidden\" xWindow=\"0\" yWindow=\"0\" windowWidth=\"20000\" windowHeight=\"10000\"/></bookViews>"
        + "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
        + "<calcPr calcId=\"191029\"/>"
        + "</workbook>";

    private const string WorkbookRelationships =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
        + "<Relationship Id=\"rId3\" Type=\"http://schemas.microsoft.com/office/2006/relationships/vbaProject\" Target=\"vbaProject.bin\"/>"
        + "</Relationships>";

    private const string Sheet =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
        + "<dimension ref=\"A1\"/><sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>"
        + "<sheetFormatPr defaultRowHeight=\"15\"/><sheetData/>"
        + "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>"
        + "</worksheet>";

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"
        + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
        + "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
        + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>"
        + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
        + "</styleSheet>";
}
