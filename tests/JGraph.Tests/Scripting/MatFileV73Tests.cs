using System.IO;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Reading version 7.3 MAT-files, which are HDF5 files with MATLAB's conventions written on top:
/// the plain numeric and text kinds, chunked and filtered data, complex numbers, cells, structs and
/// struct arrays, the newer HDF5 layout, and refusal by name for the classes with no representation
/// here.
/// </summary>
/// <remarks>
/// Every fixture was written by a real HDF5 library rather than by anything in this repository (see
/// <see cref="MatV73Fixture"/>), so a test passing means the reader agreed with the format, not with
/// its own idea of the format. Nothing writes version 7.3 and nothing is planned to: version 5 stays
/// the only format JGraph produces, because a hand-rolled HDF5 writer could produce files MATLAB
/// mis-reads without saying so.
/// </remarks>
public class MatFileV73Tests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-mat73-").FullName;

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    private Dictionary<string, JgsValue> Load(string fixture, params string[] names)
    {
        string path = MatV73Fixture.Write(_folder, fixture);
        IReadOnlySet<string>? wanted = names.Length == 0 ? null : new HashSet<string>(names, StringComparer.Ordinal);
        return MatFileReader.Read(path, wanted)
            .ToDictionary(static v => v.Name, static v => v.Value, StringComparer.Ordinal);
    }

    // --- Recognising the file at all ---------------------------------------------------------------

    [Fact]
    public void AVersion73File_IsRecognisedThroughTheUserblockMatlabPutsInFrontOfIt()
    {
        // MATLAB reserves 512 bytes for a version 5 style description before the HDF5 bytes begin,
        // so the signature is what identifies the format — not the endian tag, which is not there.
        string path = MatV73Fixture.Write(_folder, "v73_plain.mat");
        byte[] bytes = File.ReadAllBytes(path);

        Assert.StartsWith("MATLAB 7.3 MAT-file", System.Text.Encoding.ASCII.GetString(bytes, 0, 19), StringComparison.Ordinal);
        Assert.Equal(0x89, bytes[512]);
        Assert.NotEmpty(MatFileReader.Read(path));
    }

    // --- The plain kinds ---------------------------------------------------------------------------

    [Fact]
    public void AMatrix_ComesBackInItsMatlabShapeRatherThanItsStoredOne()
    {
        // A two-by-three matrix is stored three-by-two, and the stored run of elements is already in
        // column-major order, so reading it right means reversing the dimensions and nothing else.
        JgsValue read = Load("v73_plain.mat")["A"];

        Assert.Equal(2, JgsMatrix.RowCount(read));
        Assert.Equal(3, JgsMatrix.ColCount(read));
        Assert.Equal(1, JgsMatrix.At(read, 0, 0).AsNumber);
        Assert.Equal(2, JgsMatrix.At(read, 0, 1).AsNumber);
        Assert.Equal(4, JgsMatrix.At(read, 1, 0).AsNumber);
        Assert.Equal(6, JgsMatrix.At(read, 1, 2).AsNumber);
    }

    [Fact]
    public void AScalar_StaysAScalar()
    {
        JgsValue read = Load("v73_plain.mat")["s"];

        Assert.Equal(JgsType.Number, read.Type);
        Assert.Equal(42.5, read.AsNumber);
    }

    [Fact]
    public void TheIntegerAndSingleClasses_SurviveTheirStorage()
    {
        Dictionary<string, JgsValue> loaded = Load("v73_plain.mat");

        Assert.Equal(JgsNumericClass.Int32, loaded["n"].NumericClass);
        Assert.Equal(3, loaded["n"].ArrayLength);
        Assert.Equal(2, loaded["n"].ElementAt(1).AsNumber);

        Assert.Equal(JgsNumericClass.Single, loaded["g"].NumericClass);
        Assert.Equal(2.5, loaded["g"].ElementAt(1).AsNumber);
    }

    [Fact]
    public void ALogicalMatrix_KeepsBothItsShapeAndItsClass()
    {
        // Logical is stored as bytes, so what says it is logical is the class attribute rather than
        // anything about the data; reading the bytes alone would give a matrix of ones and zeros.
        JgsValue read = Load("v73_plain.mat")["L"];

        Assert.Equal(2, JgsMatrix.RowCount(read));
        Assert.Equal(2, JgsMatrix.ColCount(read));
        Assert.Equal(JgsType.Bool, JgsMatrix.At(read, 0, 0).Type);
        Assert.True(JgsMatrix.At(read, 0, 0).AsNumber != 0);
        Assert.True(JgsMatrix.At(read, 0, 1).AsNumber == 0);
        Assert.True(JgsMatrix.At(read, 1, 1).AsNumber != 0);
    }

    [Fact]
    public void Text_ComesBackAsTextRatherThanAsTheNumbersItIsStoredAs()
    {
        JgsValue read = Load("v73_plain.mat")["t"];

        Assert.Equal(JgsType.String, read.Type);
        Assert.Equal("hello", read.AsString);
    }

    [Fact]
    public void ACharMatrix_KeepsItsRowsRatherThanBecomingOneLongString()
    {
        // Characters are stored down the columns, and a char matrix is kept as a column of rows,
        // so this is the one read that genuinely transposes.
        JgsValue read = Load("v73_plain.mat")["T"];

        Assert.Equal(2, read.ArrayLength);
        Assert.Equal("adx", read.ElementAt(0).AsString);
        Assert.Equal("bey", read.ElementAt(1).AsString);
    }

    [Fact]
    public void AThreeDimensionalArray_KeepsAllThreeDimensions()
    {
        JgsValue read = Load("v73_plain.mat")["N"];

        Assert.True(read.IsNd);
        Assert.Equal(3, read.DimCount);
        Assert.Equal([2, 3, 2], read.Dims);
        Assert.Equal(12, read.ArrayLength);
        Assert.Equal(1, read.ElementAt(0).AsNumber);
        Assert.Equal(2, read.ElementAt(6).AsNumber);
    }

    [Fact]
    public void AnEmpty_ComesBackEmptyRatherThanAsTheDimensionsItStores()
    {
        // An empty array holds its own dimensions as data, so reading the data without reading the
        // attribute that says it is empty would give back a two-element vector of zeros.
        JgsValue read = Load("v73_plain.mat")["E"];

        Assert.Equal(0, read.ArrayLength);
    }

    // --- Chunked and filtered data -----------------------------------------------------------------

    [Fact]
    public void ADeflatedDatasetInManyChunks_ReassemblesInOrder()
    {
        // Twelve chunks in a three-by-four grid, so both the row and the column edges are partial —
        // a chunk copied to the wrong offset shows up as a plausible matrix of the wrong numbers.
        JgsValue read = Load("v73_deflate.mat")["B"];

        Assert.Equal(24, JgsMatrix.RowCount(read));
        Assert.Equal(30, JgsMatrix.ColCount(read));
        Assert.Equal(1, JgsMatrix.At(read, 0, 0).AsNumber);
        Assert.Equal(30, JgsMatrix.At(read, 0, 29).AsNumber);
        Assert.Equal(691, JgsMatrix.At(read, 23, 0).AsNumber);
        Assert.Equal(720, JgsMatrix.At(read, 23, 29).AsNumber);

        double total = 0;
        for (int i = 0; i < read.ArrayLength; i++)
        {
            total += read.ElementAt(i).AsNumber;
        }

        Assert.Equal(720 * 721 / 2.0, total);
    }

    [Fact]
    public void AShuffledAndChecksummedDataset_RunsItsFiltersBackwards()
    {
        // Three filters stack here, and they only come out right if they are undone in the reverse
        // of the order they were applied: checksum, then deflate, then shuffle.
        JgsValue read = Load("v73_deflate.mat")["S"];

        Assert.Equal(12, JgsMatrix.RowCount(read));
        Assert.Equal(1, JgsMatrix.At(read, 0, 0).AsNumber);
        Assert.Equal(144, JgsMatrix.At(read, 11, 11).AsNumber);
        Assert.Equal(13, JgsMatrix.At(read, 1, 0).AsNumber);
    }

    // --- Complex, cells and structs ----------------------------------------------------------------

    [Fact]
    public void AComplexVector_ReadsBothOfItsParts()
    {
        JgsValue read = Load("v73_complex.mat")["z"];

        Assert.Equal(2, read.ArrayLength);
        Assert.Equal(JgsType.Complex, read.ElementAt(0).Type);
        Assert.Equal(1, read.ElementAt(0).AsComplex.Real);
        Assert.Equal(2, read.ElementAt(0).AsComplex.Imaginary);
        Assert.Equal(3, read.ElementAt(1).AsComplex.Real);
        Assert.Equal(-4, read.ElementAt(1).AsComplex.Imaginary);
    }

    [Fact]
    public void ACellArray_FollowsItsReferencesToWhateverTheyPointAt()
    {
        // A cell holds addresses rather than values, and each address leads to an object read the
        // same way a variable is — which is why a cell can hold anything a variable can.
        JgsValue read = Load("v73_nested.mat")["C"];

        Assert.Equal(JgsType.Cell, read.Type);
        IReadOnlyList<JgsValue> items = read.AsCell;
        Assert.Equal(2, items.Count);
        Assert.Equal(7, items[0].AsNumber);
        Assert.Equal("two", items[1].AsString);
    }

    [Fact]
    public void AScalarStruct_KeepsItsFieldsAndTheirOrder()
    {
        JgsValue read = Load("v73_nested.mat")["St"];

        Assert.Equal(JgsType.Struct, read.Type);
        JgsStructArray payload = read.AsStructArray;
        Assert.Equal(1, payload.Length);
        Assert.Equal(["alpha", "beta"], payload.FieldNames);
        Assert.Equal(2, payload.Elements[0]["alpha"].ArrayLength);
        Assert.Equal("hi", payload.Elements[0]["beta"].AsString);
    }

    [Fact]
    public void AStructArray_ComesBackWithOneElementPerStoredReference()
    {
        // A struct array gives each field one reference per element, where a scalar struct gives the
        // field its value directly; that difference is the only thing that distinguishes the two.
        JgsValue read = Load("v73_nested.mat")["Sa"];

        JgsStructArray payload = read.AsStructArray;
        Assert.Equal(2, payload.Length);
        Assert.Equal(10, payload.Elements[0]["v"].AsNumber);
        Assert.Equal(20, payload.Elements[1]["v"].AsNumber);
    }

    // --- The newer HDF5 layout ---------------------------------------------------------------------

    [Fact]
    public void TheNewerFileLayout_ReadsTheSameValues()
    {
        // A version 3 superblock, version 2 object headers and links kept as header messages instead
        // of in a symbol table: a different shape of file holding the same variables.
        Dictionary<string, JgsValue> loaded = Load("v73_latest.mat");

        Assert.Equal(2, JgsMatrix.RowCount(loaded["A"]));
        Assert.Equal(3, JgsMatrix.At(loaded["A"], 1, 0).AsNumber);
        Assert.Equal("new", loaded["t"].AsString);
    }

    // --- Refusing by name --------------------------------------------------------------------------

    [Fact]
    public void AClassObject_SaysWhatItIsRatherThanBeingReadAsItsStorage()
    {
        // Its bytes are an ordinary double and would have read back perfectly while meaning nothing;
        // what makes it unreadable is the class attribute, which is also what makes it an object.
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Load("v73_object.mat"));

        Assert.Contains("myclass", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be loaded", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStringArray_SaysSoByName()
    {
        InvalidDataException error =
            Assert.Throws<InvalidDataException>(() => Load("v73_object.mat", "str"));

        Assert.Contains("string array", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamingAVariable_StepsOverTheOnesItWasNotAskedFor()
    {
        // Reading only what was asked for is what keeps one unreadable variable from spoiling a load
        // that was never about it.
        Dictionary<string, JgsValue> loaded = Load("v73_object.mat", "ok");

        Assert.Equal(5, Assert.Single(loaded).Value.AsNumber);
    }
}
