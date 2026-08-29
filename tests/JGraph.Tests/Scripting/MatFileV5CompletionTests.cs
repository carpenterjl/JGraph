using System.Buffers.Binary;
using System.IO;
using JGraph.Numerics.Sparse;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// What M65 wave C added to the level-5 MAT-file surface: the byte-swapping read path, logical and
/// sparse elements in both directions, class tags, char matrices, N-D and 2-D shapes, native struct
/// arrays, <c>save -append</c>, and clean refusals for the types version 5 has no room for.
/// </summary>
/// <remarks>
/// Nearly every case here began as a silent wrong answer rather than an error: a logical matrix came
/// back as doubles, a 2-by-2 cell came back 1-by-4, an <c>int8</c> came back double, a two-element
/// struct array came back as one element, a char matrix came back as numbers, and a string array
/// wrote its characters out as if they had been numbers all along. A format that quietly rounds a
/// value to the nearest thing it can represent is worse than one that refuses it.
/// </remarks>
public class MatFileV5CompletionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-mat5-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string PathFor(string name) => Path.Combine(_folder, name);

    private JgsValue RoundTrip(JgsValue value)
    {
        string path = PathFor("roundtrip.mat");
        MatFileWriter.Write(path, [("x", value)]);
        return Assert.Single(MatFileReader.Read(path)).Value;
    }

    // --- The byte-swapping read path ---------------------------------------------------------------

    [Fact]
    public void ABigEndianFile_ReadsTheSameNumbersAsALittleEndianOne()
    {
        // Hand-built rather than round-tripped: the writer only emits little-endian, so the only way
        // to exercise the swap is to lay out a file the way a big-endian machine's MATLAB would.
        string path = PathFor("big-endian.mat");
        using (var stream = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(stream))
        {
            var header = new byte[116];
            Array.Fill(header, (byte)' ');
            w.Write(header);
            w.Write(new byte[8]);
            w.Write((byte)0x01);
            w.Write((byte)0x00);
            w.Write((byte)'M');
            w.Write((byte)'I'); // 'MI' — the file is big-endian

            using var body = new MemoryStream();
            using var bw = new BinaryWriter(body);
            WriteBig(bw, 6);      // miUINT32 array flags
            WriteBig(bw, 8);
            WriteBig(bw, 6);      // mxDOUBLE
            WriteBig(bw, 0);
            WriteBig(bw, 5);      // miINT32 dimensions
            WriteBig(bw, 8);
            WriteBig(bw, 2);
            WriteBig(bw, 3);
            WriteBig(bw, 1);      // miINT8 name
            WriteBig(bw, 1);
            bw.Write((byte)'A');
            bw.Write(new byte[7]);
            WriteBig(bw, 9);      // miDOUBLE data, column-major
            WriteBig(bw, 48);
            foreach (double element in new double[] { 1, 4, 2, 5, 3, 6 })
            {
                WriteBig(bw, element);
            }

            bw.Flush();

            WriteBig(w, 14); // miMATRIX
            WriteBig(w, (int)body.Length);
            w.Write(body.ToArray());
        }

        (string name, JgsValue value) = Assert.Single(MatFileReader.Read(path));
        Assert.Equal("A", name);
        Assert.Equal(2, JgsMatrix.RowCount(value));
        Assert.Equal(3, JgsMatrix.ColCount(value));
        Assert.Equal(1, JgsMatrix.At(value, 0, 0).AsNumber);
        Assert.Equal(6, JgsMatrix.At(value, 1, 2).AsNumber);
        Assert.Equal(5, JgsMatrix.At(value, 1, 1).AsNumber);
    }

    [Fact]
    public void ABigEndianFile_ReadsItsSmallElementsToo()
    {
        // The small-element form packs size and type into one word, so reversing that word has to put
        // both halves back in the right place — the case a naive per-field swap gets wrong.
        string path = PathFor("big-endian-small.mat");
        using (var stream = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(stream))
        {
            var header = new byte[116];
            Array.Fill(header, (byte)' ');
            w.Write(header);
            w.Write(new byte[8]);
            w.Write((byte)0x01);
            w.Write((byte)0x00);
            w.Write((byte)'M');
            w.Write((byte)'I');

            using var body = new MemoryStream();
            using var bw = new BinaryWriter(body);
            WriteBig(bw, 6);
            WriteBig(bw, 8);
            WriteBig(bw, 6);
            WriteBig(bw, 0);
            WriteBig(bw, 5);
            WriteBig(bw, 8);
            WriteBig(bw, 1);
            WriteBig(bw, 1);
            WriteBig(bw, (1 << 16) | 1); // small element: miINT8, one byte
            bw.Write((byte)'q');
            bw.Write(new byte[3]);
            WriteBig(bw, 9);
            WriteBig(bw, 8);
            WriteBig(bw, 2.5);
            bw.Flush();

            WriteBig(w, 14);
            WriteBig(w, (int)body.Length);
            w.Write(body.ToArray());
        }

        (string name, JgsValue value) = Assert.Single(MatFileReader.Read(path));
        Assert.Equal("q", name);
        Assert.Equal(2.5, value.AsNumber);
    }

    private static void WriteBig(BinaryWriter w, int value) =>
        w.Write(BinaryPrimitives.ReverseEndianness(value));

    private static void WriteBig(BinaryWriter w, double value) =>
        w.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToInt64Bits(value)));

    // --- Logicals, classes and shapes --------------------------------------------------------------

    [Fact]
    public void ALogicalMatrix_KeepsBothItsShapeAndItsClass()
    {
        JgsValue mask = JgsMatrix.FromElements(
            [JgsValue.True, JgsValue.False, JgsValue.False, JgsValue.True], 2, 2);

        JgsValue read = RoundTrip(mask);
        Assert.Equal(2, JgsMatrix.RowCount(read));
        Assert.Equal(2, JgsMatrix.ColCount(read));
        Assert.Equal(JgsType.Bool, JgsMatrix.At(read, 0, 0).Type);
        Assert.True(JgsMatrix.At(read, 1, 1).AsNumber != 0);
        Assert.True(JgsMatrix.At(read, 0, 1).AsNumber == 0);
    }

    [Fact]
    public void EveryNumericClassTag_SurvivesTheRoundTrip()
    {
        // One Fact rather than a Theory because the enum is internal, and a Theory's parameter would
        // have to be public to reach it.
        JgsNumericClass[] classes =
        [
            JgsNumericClass.Int8, JgsNumericClass.UInt8, JgsNumericClass.Int16, JgsNumericClass.UInt16,
            JgsNumericClass.Int32, JgsNumericClass.UInt32, JgsNumericClass.Int64, JgsNumericClass.UInt64,
            JgsNumericClass.Single,
        ];

        foreach (JgsNumericClass numericClass in classes)
        {
            JgsValue value = JgsMatrix.FromColumnMajor([1, 2, 3], 1, 3);
            value.SetNumericClass(numericClass);

            JgsValue read = RoundTrip(value);
            Assert.Equal(numericClass, read.NumericClass);
            Assert.Equal(3, read.ArrayLength);
            Assert.Equal(2, read.ElementAt(1).AsNumber);
        }
    }

    [Fact]
    public void ADoubleThatWasNeverTagged_ComesBackADouble()
    {
        Assert.Equal(JgsNumericClass.Double, RoundTrip(JgsValue.Number(7)).NumericClass);
    }

    [Fact]
    public void ACharMatrix_KeepsItsRowsRatherThanBecomingNumbers()
    {
        // A char matrix is a 2-D array of characters here and in the MAT-file both, so the round trip
        // has to keep its shape as well as its text (M105) — it used to be a column of char rows,
        // and the read that rebuilt it answered a 3-by-1.
        JgsValue matrix = JgsValue.CharMatrix(["ab", "cd", "ef"]);

        JgsValue read = RoundTrip(matrix);
        Assert.True(read.IsCharMatrix);
        Assert.Equal(6, read.ArrayLength);
        Assert.Equal(3, read.Rows);
        Assert.Equal(2, read.Cols);
        Assert.Equal(["ab", "cd", "ef"], read.CharMatrixRows());
    }

    [Fact]
    public void AnNdArray_KeepsAllThreeOfItsDimensions()
    {
        JgsValue volume = JgsMatrix.FromColumnMajor([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], 2, 6);
        volume.ReshapeDims([2, 3, 2]);

        JgsValue read = RoundTrip(volume);
        Assert.Equal([2, 3, 2], read.Dims);
        Assert.Equal(12, read.ElementAt(11).AsNumber);
    }

    [Fact]
    public void ATwoDimensionalCell_DoesNotFlattenIntoARow()
    {
        JgsValue cell = JgsValue.Cell(
            [JgsValue.Number(1), JgsValue.Number(2), JgsValue.Str("three"), JgsValue.Number(4)]);
        cell.Reshape(2, 2);

        JgsValue read = RoundTrip(cell);
        Assert.Equal(JgsType.Cell, read.Type);
        Assert.Equal([2, 2], read.Dims);
        Assert.Equal("three", read.AsCell[2].AsString);
    }

    [Fact]
    public void AComplexMatrix_KeepsItsShape()
    {
        JgsValue matrix = JgsMatrix.FromElements(
        [
            JgsValue.ComplexNum(new System.Numerics.Complex(1, 2)),
            JgsValue.Number(3),
            JgsValue.Number(4),
            JgsValue.ComplexNum(new System.Numerics.Complex(5, -1)),
        ], 2, 2);

        JgsValue read = RoundTrip(matrix);
        Assert.Equal(2, JgsMatrix.RowCount(read));
        Assert.Equal(2, JgsMatrix.ColCount(read));
        Assert.Equal(new System.Numerics.Complex(5, -1), JgsMatrix.At(read, 1, 1).AsComplex);
    }

    // --- Sparse ------------------------------------------------------------------------------------

    [Fact]
    public void ASparseMatrix_RoundTripsAsSparse()
    {
        var matrix = CscMatrix.FromTriplets(3, 3, [(0, 0, 1.5), (2, 1, -4), (1, 2, 7)]);

        JgsValue read = RoundTrip(JgsValue.Sparse(matrix));
        Assert.Equal(JgsType.Sparse, read.Type);
        CscMatrix back = read.AsSparse;
        Assert.Equal(3, back.Rows);
        Assert.Equal(3, back.Cols);
        Assert.Equal(3, back.NonZeroCount);
        Assert.Equal(1.5, back.Values[0]);
    }

    [Fact]
    public void AnAllZeroSparseMatrix_StillKeepsItsSize()
    {
        JgsValue read = RoundTrip(JgsValue.Sparse(CscMatrix.FromTriplets(4, 2, [])));
        Assert.Equal(4, read.AsSparse.Rows);
        Assert.Equal(2, read.AsSparse.Cols);
        Assert.Equal(0, read.AsSparse.NonZeroCount);
    }

    // --- Struct arrays -----------------------------------------------------------------------------

    [Fact]
    public void AStructArray_KeepsEveryElement()
    {
        JgsValue array = JgsValue.StructArray(
        [
            new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str("first"),
                ["size"] = JgsValue.Number(1),
            },
            new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str("second"),
                ["size"] = JgsValue.Number(2),
            },
        ]);

        JgsValue read = RoundTrip(array);
        Assert.Equal(JgsType.Struct, read.Type);
        Assert.Equal(2, read.AsStructArray.Length);
        Assert.Equal("second", read.AsStructArray.Elements[1]["name"].AsString);
        Assert.Equal(1, read.AsStructArray.Elements[0]["size"].AsNumber);
    }

    [Fact]
    public void AScalarStruct_IsStillAScalarStructAfterwards()
    {
        JgsValue read = RoundTrip(JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["only"] = JgsValue.Number(5),
        }));

        Assert.False(read.IsStructArray);
        Assert.Equal(5, read.AsStruct["only"].AsNumber);
    }

    [Fact]
    public void AnEmptyStructArray_StillDeclaresItsFields()
    {
        JgsValue read = RoundTrip(JgsValue.StructArray(new JgsStructArray([], ["alpha", "beta"]), 0, 0));

        Assert.Equal(0, read.AsStructArray.Length);
        Assert.Equal(["alpha", "beta"], read.AsStructArray.FieldNames);
    }

    [Fact]
    public void AStructArrayHoldingCellsAndLogicals_KeepsThemAllTheWayDown()
    {
        JgsValue flags = JgsValue.Array([JgsValue.True, JgsValue.False]);
        JgsValue array = JgsValue.StructArray(
        [
            new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["flags"] = flags,
                ["items"] = JgsValue.Cell([JgsValue.Number(1), JgsValue.Str("two")]),
            },
            new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["flags"] = JgsValue.Array([JgsValue.False, JgsValue.True]),
                ["items"] = JgsValue.Cell([JgsValue.Number(3)]),
            },
        ]);

        JgsValue read = RoundTrip(array);
        Assert.Equal(JgsType.Bool, read.AsStructArray.Elements[0]["flags"].ElementAt(0).Type);
        Assert.Equal("two", read.AsStructArray.Elements[0]["items"].AsCell[1].AsString);
        Assert.Equal(3, read.AsStructArray.Elements[1]["items"].AsCell[0].AsNumber);
    }

    // --- Appending ---------------------------------------------------------------------------------

    [Fact]
    public void Appending_KeepsTheNamesItDoesNotMention()
    {
        string path = PathFor("append.mat");
        MatFileWriter.Write(path, [("a", JgsValue.Number(1)), ("b", JgsValue.Number(2))]);
        MatFileWriter.Append(path, [("c", JgsValue.Number(3))]);

        Dictionary<string, double> read = MatFileReader.Read(path)
            .ToDictionary(v => v.Name, v => v.Value.AsNumber, StringComparer.Ordinal);
        Assert.Equal(3, read.Count);
        Assert.Equal(1, read["a"]);
        Assert.Equal(3, read["c"]);
    }

    [Fact]
    public void AppendingANameThatIsAlreadyThere_ReplacesIt()
    {
        string path = PathFor("append-replace.mat");
        MatFileWriter.Write(path, [("a", JgsValue.Number(1)), ("b", JgsValue.Number(2))]);
        MatFileWriter.Append(path, [("a", JgsValue.Number(99))]);

        IReadOnlyList<(string Name, JgsValue Value)> read = MatFileReader.Read(path);
        Assert.Equal(2, read.Count);
        Assert.Equal(99, read.Single(v => v.Name == "a").Value.AsNumber);
    }

    [Fact]
    public void AppendingToNothing_WritesTheFile()
    {
        string path = PathFor("append-new.mat");
        MatFileWriter.Append(path, [("only", JgsValue.Number(8))]);
        Assert.Equal(8, Assert.Single(MatFileReader.Read(path)).Value.AsNumber);
    }

    // --- Refusals ----------------------------------------------------------------------------------

    [Fact]
    public void AStringArray_IsRefusedByNameRatherThanWrittenAsNumbers()
    {
        string? why = MatFileWriter.WhyNotWritable(
            JgsValue.StringArray([JgsValue.Str("a"), JgsValue.Str("b")]));

        Assert.NotNull(why);
        Assert.Contains("string array", why);
        Assert.Contains("7.3", why);
    }

    [Fact]
    public void ADatetimeAndADuration_AreBothRefusedByName()
    {
        JgsValue moment = JgsValue.Array([JgsValue.Number(0)])
            .MarkTime(new JgsTimeTag(JgsTimeKind.Datetime, "yyyy-MM-dd"));
        JgsValue span = JgsValue.Array([JgsValue.Number(1000)])
            .MarkTime(new JgsTimeTag(JgsTimeKind.Duration, "hh:mm:ss"));

        Assert.Contains("datetime", MatFileWriter.WhyNotWritable(moment)!);
        Assert.Contains("duration", MatFileWriter.WhyNotWritable(span)!);
    }

    [Fact]
    public void AValueStandingInForAnObject_IsRefusedUnderItsClassName()
    {
        JgsValue map = JgsValue.Number(3);
        map.SetClassName("containers.Map");

        Assert.Contains("containers.Map", MatFileWriter.WhyNotWritable(map)!);
    }

    [Fact]
    public void ARefusalReachesInsideACellOrAStruct()
    {
        JgsValue nested = JgsValue.Cell([JgsValue.Number(1), JgsValue.StringScalar("text")]);
        Assert.Contains("string array", MatFileWriter.WhyNotWritable(nested)!);

        JgsValue holder = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["when"] = JgsValue.Array([JgsValue.Number(0)])
                .MarkTime(new JgsTimeTag(JgsTimeKind.Datetime, "yyyy-MM-dd")),
        });
        Assert.Contains("datetime", MatFileWriter.WhyNotWritable(holder)!);
    }

    [Fact]
    public void EverythingVersionFiveCanHold_ReportsNoReason()
    {
        Assert.Null(MatFileWriter.WhyNotWritable(JgsValue.Number(1)));
        Assert.Null(MatFileWriter.WhyNotWritable(JgsValue.Str("text")));
        Assert.Null(MatFileWriter.WhyNotWritable(JgsValue.True));
        Assert.Null(MatFileWriter.WhyNotWritable(JgsValue.Sparse(CscMatrix.FromTriplets(1, 1, []))));
        Assert.Null(MatFileWriter.WhyNotWritable(JgsValue.Cell([JgsValue.Number(1)])));
    }

    [Fact]
    public void AVersion73File_SaysWhichVersionItIs()
    {
        // The HDF5 signature plus the version text MATLAB stamps into the first 116 bytes.
        string path = PathFor("v73.mat");
        var bytes = new byte[256];
        byte[] signature = [0x89, (byte)'H', (byte)'D', (byte)'F', (byte)'\r', (byte)'\n', 0x1A, (byte)'\n'];
        Array.Copy(signature, bytes, signature.Length);
        byte[] text = System.Text.Encoding.ASCII.GetBytes("MATLAB 7.3 MAT-file");
        Array.Copy(text, 0, bytes, 8, text.Length);
        File.WriteAllBytes(path, bytes);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => MatFileReader.Read(path));
        Assert.Contains("7.3", error.Message);
    }
}
