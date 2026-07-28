using System.IO;
using System.IO.Compression;
using System.Numerics;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The MAT-file v5 writer and reader (M36): every supported value round-trips byte-exactly, and the
/// reader also accepts what real MATLAB writes — including compressed elements and the compact
/// integer encodings — via hand-crafted fixture bytes.
/// </summary>
public class MatFileRoundTripTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-mat-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string PathFor(string name) => Path.Combine(_folder, name);

    private JgsValue RoundTrip(JgsValue value)
    {
        string path = PathFor("roundtrip.mat");
        MatFileWriter.Write(path, [("x", value)]);
        (string name, JgsValue read) = Assert.Single(MatFileReader.Read(path));
        Assert.Equal("x", name);
        return read;
    }

    [Fact]
    public void Scalar_RoundTrips()
    {
        JgsValue read = RoundTrip(JgsValue.Number(42.5));
        Assert.Equal(JgsType.Number, read.Type);
        Assert.Equal(42.5, read.AsNumber);
    }

    [Fact]
    public void Vector_RoundTrips()
    {
        JgsValue read = RoundTrip(JgsValue.Array([JgsValue.Number(1), JgsValue.Number(2), JgsValue.Number(3)]));
        Assert.True(JgsStdlib.DeepEquals(
            JgsValue.Array([JgsValue.Number(1), JgsValue.Number(2), JgsValue.Number(3)]), read));
    }

    [Fact]
    public void Matrix_RoundTrips_AcrossTheColumnMajorBoundary()
    {
        JgsValue matrix = JgsValue.Array(
        [
            JgsValue.Array([JgsValue.Number(1), JgsValue.Number(2), JgsValue.Number(3)]),
            JgsValue.Array([JgsValue.Number(4), JgsValue.Number(5), JgsValue.Number(6)]),
        ]);

        Assert.True(JgsStdlib.DeepEquals(matrix, RoundTrip(matrix)));
    }

    [Fact]
    public void Complex_RoundTrips()
    {
        JgsValue read = RoundTrip(JgsValue.ComplexNum(new Complex(1.5, -2.5)));
        Assert.Equal(JgsType.Complex, read.Type);
        Assert.Equal(new Complex(1.5, -2.5), read.AsComplex);
    }

    [Fact]
    public void LogicalMask_RoundTrips()
    {
        JgsValue mask = JgsValue.Array([JgsValue.True, JgsValue.False, JgsValue.True]);
        JgsValue read = RoundTrip(mask);
        Assert.True(JgsStdlib.DeepEquals(mask, read));
        Assert.Equal(JgsType.Bool, read.ElementAt(0).Type);
    }

    [Fact]
    public void String_RoundTrips()
    {
        JgsValue read = RoundTrip(JgsValue.Str("hello mat"));
        Assert.Equal(JgsType.String, read.Type);
        Assert.Equal("hello mat", read.AsString);
    }

    [Fact]
    public void CellAndStruct_RoundTrip()
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["freq"] = JgsValue.Number(2.4e9),
            ["name"] = JgsValue.Str("antenna"),
        };
        JgsValue cell = JgsValue.Cell([JgsValue.Number(7), JgsValue.Str("two"), JgsValue.Struct(fields)]);

        JgsValue read = RoundTrip(cell);
        Assert.Equal(JgsType.Cell, read.Type);
        Assert.Equal(7, read.AsCell[0].AsNumber);
        Assert.Equal("two", read.AsCell[1].AsString);
        Assert.Equal(2.4e9, read.AsCell[2].AsStruct["freq"].AsNumber);
        Assert.Equal("antenna", read.AsCell[2].AsStruct["name"].AsString);
    }

    [Fact]
    public void SeveralVariables_KeepTheirNamesAndOrder()
    {
        string path = PathFor("many.mat");
        MatFileWriter.Write(path,
        [
            ("a", JgsValue.Number(1)),
            ("b", JgsValue.Str("two")),
        ]);

        IReadOnlyList<(string Name, JgsValue Value)> read = MatFileReader.Read(path);
        Assert.Equal(2, read.Count);
        Assert.Equal("a", read[0].Name);
        Assert.Equal("b", read[1].Name);
    }

    [Fact]
    public void ReadsAHandCraftedFile_WithSmallElementsAndInt16Data()
    {
        // A minimal v5 file written the way MATLAB itself encodes small variables: the name in the
        // small-element format and the data as compact miINT16 — neither of which our writer emits.
        string path = PathFor("foreign.mat");
        using (var stream = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(stream))
        {
            var header = new byte[116];
            Array.Fill(header, (byte)' ');
            w.Write(header);
            w.Write(new byte[8]);
            w.Write((ushort)0x0100);
            w.Write((byte)'I');
            w.Write((byte)'M');

            using var body = new MemoryStream();
            using var bw = new BinaryWriter(body);
            bw.Write(6); // miUINT32 flags
            bw.Write(8);
            bw.Write(6); // mxDOUBLE
            bw.Write(0);
            bw.Write(5); // miINT32 dims
            bw.Write(8);
            bw.Write(1);
            bw.Write(3);
            bw.Write((1 << 16) | 1); // small element: miINT8, 1 byte — the name "y"
            bw.Write((int)(byte)'y');
            bw.Write(3); // miINT16 data
            bw.Write(6);
            bw.Write((short)10);
            bw.Write((short)-20);
            bw.Write((short)30);
            bw.Write((short)0); // pad to 8
            bw.Flush();

            w.Write(14); // miMATRIX
            w.Write((int)body.Length);
            w.Write(body.ToArray());
        }

        (string name, JgsValue value) = Assert.Single(MatFileReader.Read(path));
        Assert.Equal("y", name);
        Assert.True(JgsStdlib.DeepEquals(
            JgsValue.Array([JgsValue.Number(10), JgsValue.Number(-20), JgsValue.Number(30)]), value));
    }

    [Fact]
    public void ReadsACompressedElement_AsMatlabWritesByDefault()
    {
        // Write a plain file, then re-wrap its matrix element in a zlib stream — the layout MATLAB
        // has used by default since R2006b.
        string plain = PathFor("plain.mat");
        MatFileWriter.Write(plain, [("z", JgsValue.Number(99))]);
        byte[] bytes = File.ReadAllBytes(plain);

        byte[] element = bytes[128..];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(element);
        }

        string path = PathFor("compressed.mat");
        using (var stream = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(stream))
        {
            w.Write(bytes[..128]);
            w.Write(15); // miCOMPRESSED
            w.Write((int)compressed.Length);
            w.Write(compressed.ToArray());
        }

        (string name, JgsValue value) = Assert.Single(MatFileReader.Read(path));
        Assert.Equal("z", name);
        Assert.Equal(99, value.AsNumber);
    }

    [Fact]
    public void SomethingThatIsNotAMatFile_ReportsItPlainly()
    {
        string path = PathFor("junk.mat");
        File.WriteAllText(path, "this is not a mat file at all");
        Assert.Throws<InvalidDataException>(() => MatFileReader.Read(path));
    }
}
