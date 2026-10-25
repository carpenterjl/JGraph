using JGraph.MATLAB.NET.Arrays;
using Xunit;
using NativeStruct = JGraph.MATLAB.NET.Arrays.native.MWStructArray;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The MWArray API's own rules (compiler and SDK plan, stage K8), as R2025b's MWArray.dll answered
/// <c>k8client</c> (<c>tools/matlab-checklist/compiler-probes/recordings/k8_client.out.txt</c>): flat
/// data by rows unless asked otherwise, MATLAB's subscripts, the scalar readers' classes, a struct's
/// 0-based <c>GetField</c>, R2025b's refusals in its words; and where R2025b answers wrongly, the right
/// answer (ADR 0264's divergences).
/// </summary>
public sealed class MWArrayApiTests
{
    [Fact]
    public void FlatDataIsReadByRowsUnlessAskedOtherwise()
    {
        var rows = new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 });
        Assert.Equal("2", rows[1, 2].ToString());
        Assert.Equal("5", rows[4].ToString()); // linear, down the columns
        Assert.Equal("     1     3     5\n     2     4     6", new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 }, false).ToString());

        // A logical's flag says column-major when true (logical.rows.asym.true).
        Assert.Equal("   1   1\n   0   0", new MWLogicalArray(2, 2, [true, true, false, false]).ToString());
        Assert.Equal("   1   0\n   1   0", new MWLogicalArray(2, 2, [true, true, false, false], true).ToString());
        Assert.Equal([true, false, true, false], new MWLogicalArray(new bool[,] { { true, true }, { false, false } }).ToVector());
    }

    [Fact]
    public void ScalarsAndArraysTakeTheClassesR2025bGivesThem()
    {
        Assert.Equal(MWNumericType.Double, new MWNumericArray(3).NumericType);
        Assert.Equal(MWNumericType.Int32, new MWNumericArray(3, false).NumericType);
        Assert.Equal(MWNumericType.UInt8, ((MWNumericArray)(byte)4).NumericType);
        Assert.Equal(MWNumericType.Int64, ((MWNumericArray)4L).NumericType);
        Assert.Equal(MWNumericType.Double, ((MWNumericArray)new[] { 1, 2 }).NumericType);
        Assert.Equal([0, 1], new MWNumericArray(MWNumericType.Single).Dimensions);
        Assert.Equal("0     0", new MWNumericArray(new[] { 1, 2 }).ToString()); // an int[] is the dimensions constructor
        Assert.Equal("The data array type is invalid",
            Assert.Throws<ArgumentException>(() => new MWNumericArray(new sbyte[] { 1 })).Message);
        Assert.Equal("The data array must have (row * columns) elements (Parameter 'realData')",
            Assert.Throws<ArgumentException>(() => new MWNumericArray(2, 2, new double[] { 1, 2, 3 })).Message);
    }

    [Fact]
    public void TheScalarReadersAndTheIndexesRefuseAsR2025bsDo()
    {
        var m = new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 });
        Assert.Equal("Invalid index", Assert.Throws<Exception>(() => m[7]).Message);
        Assert.Equal("Invalid index", Assert.Throws<Exception>(() => m[1, 1, 1]).Message);
        Assert.Equal("Value must be a scalar", Assert.Throws<Exception>(() => m.IsNaN).Message);
        Assert.Equal(1.0, new MWNumericArray(new double[] { 1, 2 }).ToScalarDouble()); // the first element, no scalar check
        Assert.Equal(2, new MWNumericArray(2.7).ToScalarInteger());
        Assert.Equal("Unexpected error - Invalid numeric array data type",
            Assert.Throws<ApplicationException>(() => new MWNumericArray(2.5).ToScalarFloat()).Message);
        Assert.Equal("Data type unsupported by MATLAB .NET Assembly for output",
            Assert.Throws<InvalidDataException>(() => new MWNumericArray(1.0, 2.0).ToArray()).Message);
        var disposed = new MWNumericArray(1.0);
        disposed.Dispose();
        Assert.Equal("Invalid access to disposed object\nObject name: 'JGraph.MATLAB.Arrays.MWArray'.",
            Assert.Throws<ObjectDisposedException>(() => disposed.ToString()).Message.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void StructsCellsAndStringsAreR2025bs()
    {
        var st = new MWStructArray(1, 2, ["a", "b"]);
        st["a"] = 5.0;
        st["a", 2] = new MWNumericArray(new double[] { 1, 2 });
        Assert.Equal("5", st.GetField("a", 0).ToString()); // GetField's index is 0-based
        Assert.Equal("1     2", st.GetField("a", 1).ToString());
        Assert.Equal("The specified field was not found", Assert.Throws<ArgumentException>(() => st["zz"]).Message);
        Assert.Equal("The specified dimensions are invalid", Assert.Throws<ArgumentException>(() => st["a", 3]).Message);
        Assert.Equal("  1x2 struct array with fields:\n\n    a\n    b", st.ToString());
        var scalar = new MWStructArray(1, 1, ["name", "value"]);
        scalar["name"] = "probe";
        scalar["value"] = new MWNumericArray(new double[] { 1, 2, 3 });
        Assert.Equal("     name: 'probe'\n    value: [1 2 3]", scalar.ToString());

        var cell = new MWCellArray(1, 3);
        cell[1, 1] = 5.0;
        cell[1, 2] = "two";
        cell[1, 3] = new MWNumericArray(new double[,] { { 3, 4 }, { 5, 6 } });
        Assert.Equal("[5]    'two'    [2x2 double]", cell.ToString());
        Assert.IsType<double[,]>(((object[,])cell.ToArray())[0, 0]);

        Assert.Equal("System.String[]", new MWStringArray("x", "y").ToString());
        Assert.Null(new MWStringArray([null]).ToString());
        Assert.Equal("Failed to cast to string", Assert.Throws<ArgumentException>(() => (string)new MWStringArray("x", "y")).Message);
        Assert.Equal("1x1 struct array with fields:\n\tq", new NativeStruct(1, 1, ["q"]).ToString());
    }

    [Fact]
    public void WhereR2025bAnswersWronglyTheAnswerIsRight()
    {
        // R2025b reads 0 for an element of a complex array, and for its imaginary part (num.complex.index).
        var cx = new MWNumericArray(1, 2, new double[] { 1, 2 }, new double[] { 3, 4 });
        Assert.Equal("2.0000 + 4.0000i", cx[1, 2].ToString());
        Assert.Equal("4", cx[MWArrayComponent.Imaginary, 1, 2].ToString());

        // R2025b keeps the old element when a double is written into an int32 array, and drops a complex value's imaginary part.
        var ints = new MWNumericArray(1, 2, new[] { 1, 2 }, false, true);
        ints[1, 2] = 2.7;
        Assert.Equal("1   3", ints.ToString());
        var real = new MWNumericArray(new double[] { 1, 2 });
        real[2] = new MWNumericArray(1.0, 1.0);
        Assert.True(real.IsComplex);

        // R2025b takes a field name MATLAB cannot hold, and a name twice.
        Assert.Throws<ArgumentException>(() => new MWStructArray(1, 1, ["1bad"]));
        Assert.Throws<ArgumentException>(() => new MWStructArray(1, 1, ["a", "a"]));
    }
}
