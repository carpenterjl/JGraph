using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Item 09 of the head2head_v3 gap-closure plan (ADR 0156): text conversions that were paid for one
/// boxed element at a time. 09a writes one number through one scalar formatter, byte for byte what
/// num2str's aligned-row road wrote for it, and reads a packed array's doubles where they lie.
/// </summary>
[Collection("JG facade")]
public class ScalarTextM156Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public ScalarTextM156Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    // ----- 09a: one scalar formatter ------------------------------------------------------------

    /// <summary>The road <c>string(x)</c> took for one number before 09a: num2str of that number.</summary>
    private static string Oracle(double value) =>
        JgsBuiltins.NumberText([JgsValue.Number(value)], 0, 0).AsString;

    /// <summary>
    /// Every integer decade and power of two to 2^64 with its neighbours, every power of ten a double
    /// holds with its neighbours, the places where <c>%g</c> rolls over into the next decade at each
    /// precision, 0.1-step decimals, and a few hundred thousand pseudo-random doubles — by bit pattern,
    /// by log-uniform magnitude and as whole numbers — each with both signs.
    /// </summary>
    private static IEnumerable<double> Sweep()
    {
        double[] specials =
        [
            0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon,
            double.MaxValue, 2.2250738585072014E-308, 1e5, 1e-5, 99999.5, 99999.49999999999, 100000.5,
            0.1 + 0.2, 1.0 / 3.0, Math.PI, Math.E, 9007199254740992.0, 9007199254740993.0,
        ];

        IEnumerable<double> Signed(double value)
        {
            yield return value;
            yield return -value;
            yield return Math.BitIncrement(value);
            yield return Math.BitDecrement(value);
        }

        foreach (double value in specials)
        {
            foreach (double s in Signed(value))
            {
                yield return s;
            }
        }

        for (int k = 0; k <= 22; k++)
        {
            double decade = double.Parse("1e" + k, System.Globalization.CultureInfo.InvariantCulture);
            foreach (double v in new[] { decade - 1, decade, decade + 1, decade * 5, decade * 9.5 })
            {
                foreach (double s in Signed(v))
                {
                    yield return s;
                }
            }
        }

        for (int k = 0; k <= 64; k++)
        {
            double power = Math.ScaleB(1.0, k);
            foreach (double v in new[] { power - 1, power, power + 1, power + 2 })
            {
                foreach (double s in Signed(v))
                {
                    yield return s;
                }
            }
        }

        for (int k = -324; k <= 308; k++)
        {
            double decade = double.Parse("1e" + k, System.Globalization.CultureInfo.InvariantCulture);
            foreach (double v in new[] { decade, decade * 1.5, decade * 9.99995, decade * 9.999949 })
            {
                if (double.IsFinite(v) && v != 0)
                {
                    foreach (double s in Signed(v))
                    {
                        yield return s;
                    }
                }
            }
        }

        // Where %g at each precision rolls over into the next decade: 10^(e+1) * (1 - 5 * 10^-(p+1)).
        for (int e = -12; e <= 17; e++)
        {
            for (int p = 1; p <= 16; p++)
            {
                double boundary = Math.Pow(10, e + 1) * (1 - (5 * Math.Pow(10, -(p + 1))));
                foreach (double s in Signed(boundary))
                {
                    yield return s;
                }
            }
        }

        for (int k = -100_000; k <= 100_000; k++)
        {
            yield return k / 10.0;
        }

        for (int k = -3000; k <= 3000; k++)
        {
            yield return k * 0.1;
        }

        ulong state = 0x9E3779B97F4A7C15;
        ulong Next()
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            return state;
        }

        for (int i = 0; i < 200_000; i++)
        {
            double bits = BitConverter.Int64BitsToDouble((long)Next());
            if (double.IsFinite(bits))
            {
                yield return bits;
            }
        }

        for (int i = 0; i < 200_000; i++)
        {
            double mantissa = 1 + (9.0 * (Next() >> 11) / (1UL << 53));
            int exponent = (int)(Next() % 61) - 30;
            double value = mantissa * Math.Pow(10, exponent);
            yield return (i & 1) == 0 ? value : -value;
        }

        for (int i = 0; i < 100_000; i++)
        {
            yield return (double)(Next() % 10_000_000);
            yield return (double)(long)(Next() % 200_000_000_000_000_000UL) - 1e17;
        }
    }

    [Fact]
    public void TheScalarFormatterWritesWhatTheAlignedRowRoadWrote()
    {
        int count = 0;
        foreach (double value in Sweep())
        {
            string expected = Oracle(value);
            string actual = JgsSprintf.FormatScalarGeneral(value);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Assert.Fail($"{value:R} (0x{BitConverter.DoubleToInt64Bits(value):X16}): '{actual}' is not '{expected}'");
            }

            count++;
        }

        Assert.True(count > 800_000, $"only {count} values were swept");
    }

    [Fact]
    public void NaNIsSpeltTheWayNum2strSpellsIt() =>
        Assert.Equal(Oracle(double.NaN), JgsSprintf.FormatScalarGeneral(double.NaN));

    private static JgsEnvironment Globals() => JgsBuiltins.CreateGlobals(
        new JGraphScriptGlobals(new ScriptContext(new RecordingScriptOutput(), static (_, _) => { })),
        default,
        JgsDialect.Matlab);

    private static JgsValue Call(JgsEnvironment env, string name, params JgsValue[] args)
    {
        Assert.True(env.TryGet(name, out JgsValue function), name);
        return function.AsCallable.Call(args, 1, 1);
    }

    private static JgsValue Packed(double[] values, int rows, int cols, JgsPackedKind kind = JgsPackedKind.Number)
    {
        JGraph.Numerics.NumericBuffer buffer = JgsPacking.Allocate(values.Length);
        values.CopyTo(buffer.AsSpan(0, values.Length));
        return JgsValue.Shaped(buffer, rows, cols, kind);
    }

    private static JgsValue Boxed(double[] values, int rows, int cols, bool logical = false) =>
        JgsValue.Shaped(
            Array.ConvertAll(values, v => logical ? JgsValue.Bool(v != 0) : JgsValue.Number(v)), rows, cols);

    public static TheoryData<int, int> StringShapes() => new()
    {
        { 1, 12 },
        { 12, 1 },
        { 3, 4 },
        { 2, 6 },
    };

    [Theory]
    [MemberData(nameof(StringShapes))]
    public void StringOfAPackedArrayAnswersTheBoxedRoadsElementsAndShape(int rows, int cols)
    {
        double[] values = [1, double.NaN, -0.0, 2.5, double.PositiveInfinity, 1e5, 0.1, 9007199254740992.0, -3, 1e-5, double.NegativeInfinity, 12345.678];
        JgsEnvironment env = Globals();
        JgsValue packed = Call(env, "string", Packed(values, rows, cols));
        JgsValue boxed = Call(env, "string", Boxed(values, rows, cols));

        Assert.True(packed.IsStringArray);
        Assert.Equal(boxed.Rows, packed.Rows);
        Assert.Equal(boxed.Cols, packed.Cols);
        Assert.Equal(values.Length, packed.ArrayLength);
        for (int i = 0; i < values.Length; i++)
        {
            string expected = double.IsNaN(values[i]) ? JgsBuiltins.MissingSentinel : Oracle(values[i]);
            Assert.Equal(expected, packed.ElementAt(i).AsString);
            Assert.Equal(boxed.ElementAt(i).AsString, packed.ElementAt(i).AsString);
        }
    }

    [Fact]
    public void StringOfAPackedLogicalArrayIsItsWords()
    {
        double[] values = [1, 0, 0, 1];
        JgsEnvironment env = Globals();
        JgsValue packed = Call(env, "string", Packed(values, 2, 2, JgsPackedKind.Bool));
        JgsValue boxed = Call(env, "string", Boxed(values, 2, 2, logical: true));
        Assert.Equal(2, packed.Rows);
        Assert.Equal(2, packed.Cols);
        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(boxed.ElementAt(i).AsString, packed.ElementAt(i).AsString);
            Assert.Equal(values[i] != 0 ? "true" : "false", packed.ElementAt(i).AsString);
        }
    }

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task NaNIsTheMissingStringAndTheWordNaNIsNot() => RunAsserting("""
        assert(isequal(ismissing(string([1 NaN])), [false true]));
        assert(ismissing("a" + string(NaN)));
        assert(~ismissing(string("NaN")));
        assert(strcmp(char(string("NaN")), 'NaN'));
        s = string([1 NaN; -0 2.5]);
        assert(isequal(size(s), [2 2]));
        assert(s(1, 1) == "1" && ismissing(s(1, 2)) && s(2, 1) == "0" && s(2, 2) == "2.5");
        assert(isequal(size(string((1:3)')), [3 1]));
        assert(string(int8(-5)) == "-5" && string(single(0.1)) == "0.1" && string(uint16(65535)) == "65535");
        assert(string(1e5) == "100000" && string(123456.78) == "123456.78" && string(1.5e-5) == "1.5e-05");
        t = string([true false]);
        assert(t(1) == "true" && t(2) == "false");
        """);
}
