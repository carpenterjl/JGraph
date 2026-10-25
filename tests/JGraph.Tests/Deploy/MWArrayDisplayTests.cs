using JGraph.MATLAB.NET.Arrays;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// An MWArray's <c>ToString</c> as MATLAB's <c>format short</c> display (compiler and SDK plan, stage
/// K8), against what R2025b's <c>disp</c> printed for the same values
/// (<c>tools/matlab-checklist/compiler-probes/recordings/k8_disp.out.txt</c>, <c>probe_k8_disp</c>): its
/// lines without the blank ones around them, a display of one line trimmed, as R2025b's MWArray API
/// answers it (<c>k8_client</c>). Rows too wide for a window are left to the client's comparison,
/// since the batch window that recorded the corpus is wider than ToString's.
/// </summary>
public sealed class MWArrayDisplayTests
{
    private static readonly Lazy<Dictionary<string, List<string>>> Recorded = new(Read);

    private static Dictionary<string, List<string>> Read()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        Assert.NotNull(folder);
        var cases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (string line in File.ReadAllLines(Path.Combine(folder, "tools", "matlab-checklist", "compiler-probes", "recordings", "k8_disp.out.txt")))
        {
            if (line.StartsWith("<<", StringComparison.Ordinal))
            {
                cases[line[2..^2]] = current = [];
            }
            else if (current is not null && line.Length >= 2 && line[0] == '|' && line[^1] == '|')
            {
                current.Add(line[1..^1]);
            }
        }

        return cases;
    }

    /// <summary>A numeric array of a class, its elements column-major.</summary>
    private static MWNumericArray Of(MWNumericType type, int[] dims, double[] re, double[]? im = null)
    {
        var made = new MWNumericArray(im is null ? MWArrayComplexity.Real : MWArrayComplexity.Complex, type, dims);
        for (int i = 0; i < re.Length; i++)
        {
            made[i + 1] = new MWNumericArray(re[i]);
            if (im is not null)
            {
                made[MWArrayComponent.Imaginary, i + 1] = new MWNumericArray(im[i]);
            }
        }

        return made;
    }

    private static MWNumericArray Row(params double[] values) => new(values);

    private static MWNumericArray Rows(int rows, int cols, params double[] rowMajor) => new(rows, cols, rowMajor);

    private static readonly Dictionary<string, Func<MWArray>> Values = new(StringComparer.Ordinal)
    {
        ["int1"] = () => new MWNumericArray(1.0),
        ["int2"] = () => new MWNumericArray(22.0),
        ["int3"] = () => new MWNumericArray(333.0),
        ["int4"] = () => new MWNumericArray(4444.0),
        ["int5"] = () => new MWNumericArray(55555.0),
        ["int6"] = () => new MWNumericArray(666666.0),
        ["int9"] = () => new MWNumericArray(123456789.0),
        ["int10"] = () => new MWNumericArray(1234567890.0),
        ["row1"] = () => Row(1, 2, 3),
        ["row3"] = () => Row(1, 22, 333),
        ["row4"] = () => Row(1, 22, 333, 4444),
        ["row5"] = () => Row(1, 55555),
        ["row6"] = () => Row(1, 666666),
        ["row9"] = () => Row(1, 123456789),
        ["row10"] = () => Row(1, 1234567890),
        ["rowneg"] = () => Row(-1, 2),
        ["rowneg3"] = () => Row(-100, 2),
        ["rowneg4"] = () => Row(-1000, 2),
        ["col"] = () => Rows(3, 1, 1, 2, 3),
        ["mat"] = () => Rows(2, 3, 1, 2, 3, 4, 5, 6),
        ["matneg"] = () => Rows(2, 2, -1, 20, 300, -4),
        ["zero"] = () => new MWNumericArray(0.0),
        ["zeros"] = () => Row(0, 0),
        ["negzero"] = () => new MWNumericArray(-0.0),
        ["frac"] = () => new MWNumericArray(2.5),
        ["pi"] = () => new MWNumericArray(Math.PI),
        ["negpi"] = () => new MWNumericArray(-Math.PI),
        ["small"] = () => new MWNumericArray(0.001),
        ["smaller"] = () => new MWNumericArray(0.0001),
        ["tiny"] = () => new MWNumericArray(1e-10),
        ["big"] = () => new MWNumericArray(99999.5),
        ["bigger"] = () => new MWNumericArray(123456.789),
        ["huge"] = () => new MWNumericArray(1e20),
        ["fracrow"] = () => Row(1.5, 2),
        ["fracneg"] = () => Rows(2, 2, -1.5, 2, 3, 4),
        ["fracrow100"] = () => Row(1, 2.5, 100),
        ["fracrow999"] = () => Row(1, 2.5, 999),
        ["scale3"] = () => Row(1, 2.5, 1000),
        ["scale4"] = () => Row(1, 2.5, 12345),
        ["scale5"] = () => Row(1.5, 123456),
        ["scalesmall"] = () => Row(0.001, 0.0025),
        ["scalesmaller"] = () => Row(0.0001, 0.00025),
        ["mixsmall"] = () => Row(1e-5, 1),
        ["mixbig"] = () => Row(1e5, 1.5),
        ["mixhuge"] = () => Row(1e10, 1.5),
        ["fraccol"] = () => Rows(2, 1, 0.5, 1.25),
        ["fracmat"] = () => Rows(2, 2, 1.5, 2.25, 3, 4),
        ["inf"] = () => new MWNumericArray(double.PositiveInfinity),
        ["nan"] = () => new MWNumericArray(double.NaN),
        ["neginf"] = () => new MWNumericArray(double.NegativeInfinity),
        ["infrow"] = () => Row(double.PositiveInfinity, double.NegativeInfinity, double.NaN),
        ["infint"] = () => Row(1, double.PositiveInfinity),
        ["inffrac"] = () => Row(1.5, double.PositiveInfinity, double.NaN),
        ["nanint"] = () => Row(double.NaN, 2, 3),
        ["infscale"] = () => Row(double.PositiveInfinity, 1, 2.5, 1000),
        ["cplx"] = () => new MWNumericArray(1.0, 2.0),
        ["cplxneg"] = () => new MWNumericArray(3.0, -4.0),
        ["cplxrow"] = () => new MWNumericArray(1, 2, new double[] { 1, 3 }, new double[] { 2, -4 }),
        ["cplxmat"] = () => new MWNumericArray(2, 2, new double[] { 1, 2, 3, 4 }, new double[] { 5, 6, 7, 8 }),
        ["cplxint"] = () => new MWNumericArray(3.0, 4.0),
        ["cplxzero"] = () => new MWNumericArray(0.0, 0.0),
        ["cplxfrac"] = () => new MWNumericArray(1.5, 0.25),
        ["cplxbig"] = () => new MWNumericArray(1000.0, 1.0),
        ["cplxrowbig"] = () => new MWNumericArray(1, 2, new double[] { 1, 1000 }, new double[] { 1, 2 }),
        ["cplxneg2"] = () => new MWNumericArray(-1.0, -2.0),
        ["cplxinf"] = () => new MWNumericArray(double.PositiveInfinity, 1.0),
        ["single"] = () => new MWNumericArray(2.5f, false),
        ["singlerow"] = () => new MWNumericArray(1, 2, [1.5f, 2f], false, true),
        ["singlemat"] = () => new MWNumericArray(2, 2, [1.5f, 2f, 3f, 4f], false, true),
        ["singleint"] = () => new MWNumericArray(1, 3, [1f, 2f, 3f], false, true),
        ["int8"] = () => Of(MWNumericType.Int8, [1, 3], [-5, 0, 127]),
        ["uint8"] = () => new MWNumericArray(2, 2, new byte[] { 1, 2, 3, 4 }, false, true),
        ["uint8big"] = () => new MWNumericArray(2, 2, new byte[] { 1, 20, 200, 4 }, false, true),
        ["int16"] = () => new MWNumericArray((short)-7, false),
        ["int16row"] = () => new MWNumericArray(1, 2, new short[] { -1, 2 }, false, true),
        ["int32"] = () => new MWNumericArray(1, 3, new[] { 1, 2, 3 }, false, true),
        ["int32mat"] = () => new MWNumericArray(2, 2, new[] { 1, 20, 300, 4 }, false, true),
        ["int64"] = () => new MWNumericArray(-9L, false),
        ["int64big"] = () => new MWNumericArray(123456789012L, false),
        ["uint64"] = () => Of(MWNumericType.UInt64, [1, 2], [1, 2]),
        ["int32cplx"] = () => new MWNumericArray(3, 4, false),
        ["int8cplxrow"] = () => Of(MWNumericType.Int8, [1, 2], [1, 2], [3, -4]),
        ["logical"] = () => new MWLogicalArray(true),
        ["logicalrow"] = () => new MWLogicalArray(new[] { true, false, true }),
        ["logicalmat"] = () => new MWLogicalArray(2, 2, [true, false, false, false]),
        ["char"] = () => new MWCharArray("hello"),
        ["charmat"] = () => new MWCharArray(["abc", "def"]),
        ["charspace"] = () => new MWCharArray(" x "),
        ["charpad"] = () => new MWCharArray(["a", "bcd"]),
        ["nd"] = () => new MWNumericArray(new double[,,] { { { 1, 3 }, { 2, 4 } }, { { 5, 7 }, { 6, 8 } } }),
        ["ndfrac"] = () => new MWNumericArray(new double[,,] { { { 1.5, 3 }, { 2, 4 } }, { { 5, 7 }, { 6, 8 } } }),
        ["nd4"] = () => new MWNumericArray(new double[,,,]
        {
            { { { 1, 3 }, { 2, 4 } }, { { 5, 7 }, { 6, 8 } } },
            { { { 9, 11 }, { 10, 12 } }, { { 13, 15 }, { 14, 16 } } },
        }),
        ["sparse"] = () => MWNumericArray.MakeSparse(3, 3, [1, 3], [1, 2], [5, 6]),
        ["sparsefrac"] = () => MWNumericArray.MakeSparse([1, 2], [1, 2], [1.5, 2]),
        ["sparsebig"] = () => MWNumericArray.MakeSparse(10, 12, [10], [12], [7.0]),
        ["sparsecplx"] = () => MWNumericArray.MakeSparse(1, 2, [1], [2], [5.0], [1.0]),
        ["sparsezero"] = () => MWNumericArray.MakeSparse(2, 2, MWArrayComplexity.Real, 0),
        ["sparselogical"] = () => MWLogicalArray.MakeSparse([1, 2], [1, 2], [true, true]),
        ["sparsescale"] = () => MWNumericArray.MakeSparse(2, 1, [1, 2], [1, 1], [1.5, 1000]),
        ["sparseneg"] = () => MWNumericArray.MakeSparse(2, 2, [1, 2], [1, 2], [-1.0, 2]),
    };

    public static TheoryData<string> Keys()
    {
        var keys = new TheoryData<string>();
        foreach (string key in Values.Keys)
        {
            keys.Add(key);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void ToStringIsR2025bsDisplay(string key)
    {
        List<string> lines = [.. Recorded.Value[key]];
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        bool text = key.StartsWith("char", StringComparison.Ordinal);
        string expected = lines.Count == 1 && !text ? lines[0].Trim() : string.Join("\n", lines);
        Assert.Equal(expected, Values[key]().ToString());
    }

    [Fact]
    public void EveryRecordedValueOfAWindowsWidthIsChecked()
    {
        // The rows wider than a window split at a width the batch session sets; the client checks those at ToString's.
        string[] left = [.. Recorded.Value.Keys.Where(k => !Values.ContainsKey(k) && !k.StartsWith("long", StringComparison.Ordinal) && k is not ("widerow" or "end" or "ndlogical"))];
        Assert.Empty(left);
    }
}
