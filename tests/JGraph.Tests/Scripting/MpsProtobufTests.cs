using System.Numerics;
using JGraph.Numerics.Sparse;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// MATLAB Production Server's protobuf wire format (compiler and SDK plan, stage K16, ADR 0272):
/// <see cref="JgsMpsProtobuf"/> writes each value as the bytes R2025b's muserve wrote for it
/// (<c>recordings/k16_server.out.txt</c>, the raw mode's <c>out.*</c> cases, hex as recorded) and reads
/// the requests MathWorks' Python client sends (its builders' bytes) and the faults a request can carry.
/// </summary>
public class MpsProtobufTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static JgsValue Row(double[] values, JgsNumericClass numericClass = JgsNumericClass.Double)
    {
        JgsValue row = JgsMatrix.FromColumnMajor(values, 1, values.Length);
        if (numericClass != JgsNumericClass.Double)
        {
            row.SetNumericClass(numericClass);
        }

        return row;
    }

    [Theory]
    [InlineData("double", "0a1708091202010130003800ca010a0a080000000000000440")]
    [InlineData("row", "0a2708091202010330003800ca011a0a18000000000000f03f00000000000000400000000000000840")]
    [InlineData("empty", "0a0d08091202000030003800ca0100")]
    [InlineData("int8s", "0a12080112020103300038008a01050a03fb007f")]
    [InlineData("int32", "0a1308051202010130003800aa01060a046079feff")]
    [InlineData("uint64", "0a1908061202010130003800b2010c0a0affffffffffffffffff01")]
    [InlineData("single", "0a1308081202010130003800c201060a040000c03f")]
    [InlineData("complex", "0a2108091202010130003801ca01140a08000000000000f03f1208000000000000f0bf")]
    [InlineData("logicals", "0a12080a1202010330003800d201050a03010001")]
    [InlineData("char", "0a14080b1202010530003800da01070a0568656c6c6f")]
    [InlineData("charmatrix", "0a15080b1202020330003800da01080a06616462656366")]
    [InlineData("emptychar", "0a0d080b1202000030003800da0100")]
    [InlineData("string", "0a1708131202010130003800b2020a0a05616c706861120105")]
    [InlineData("stringmissing", "0a1508131202010130003800b202080a03ffff07120101")]
    [InlineData("cell", "0a6b080d1202010330003800e2015e0a1708091202010130003800ca010a0a08000000000000f03f0a12080b1202010330003800da01050a0374776f0a2f08091202020230003800ca01220a200000000000000840000000000000144000000000000010400000000000001840")]
    [InlineData("emptycell", "0a0d080d1202000030003800e20100")]
    [InlineData("struct", "0a59080c1202010130003800ea014c0a046e616d650a0576616c75651214080b1202010530003800da01070a0570726f6265122708091202010330003800ca011a0a18000000000000f03f00000000000000400000000000000840")]
    [InlineData("nofields", "0a0d080c1202010130003800ea0100")]
    [InlineData("sparse", "0a2e080e1202030330003800f201210800120200021a04000102028201120a1000000000000014400000000000001840")]
    [InlineData("enum", "0a3e0812120201012a280a260a000a1c6d61746c61622e6c616e672e4f6e4f666653776974636853746174650a026f6e100430003800a00201aa02040a022802")]
    public void TheWriterWritesMuservesBytes(string kind, string expected)
    {
        JgsValue value = kind switch
        {
            "double" => JgsValue.Number(2.5),
            "row" => Row([1, 2, 3]),
            "empty" => JgsMatrix.FromColumnMajor([], 0, 0),
            "int8s" => Row([-5, 0, 127], JgsNumericClass.Int8),
            "int32" => Scalar(-100000, JgsNumericClass.Int32),
            "uint64" => Scalar(18446744073709551615.0, JgsNumericClass.UInt64),
            "single" => Scalar(1.5, JgsNumericClass.Single),
            "complex" => JgsValue.ComplexNum(new Complex(1, -1)),
            "logicals" => JgsMatrix.FromElementsDims([JgsValue.Bool(true), JgsValue.Bool(false), JgsValue.Bool(true)], [1, 3]),
            "char" => JgsValue.Str("hello"),
            "charmatrix" => JgsValue.CharMatrix(["abc", "def"]),
            "emptychar" => JgsValue.CharMatrix([]),
            "string" => JgsValue.StringArray([JgsValue.Str("alpha")], 1, 1),
            "stringmissing" => JgsValue.StringArray([JgsValue.Str(JgsBuiltins.MissingSentinel)], 1, 1),
            "cell" => Cell([JgsValue.Number(1), JgsValue.Str("two"), JgsMatrix.FromColumnMajor([3, 5, 4, 6], 2, 2)], 1, 3),
            "emptycell" => Cell([], 0, 0),
            "struct" => JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["name"] = JgsValue.Str("probe"), ["value"] = Row([1, 2, 3]) }),
            "nofields" => JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)),
            "sparse" => JgsValue.Sparse(CscMatrix.FromTriplets(3, 3, [(0, 0, 5.0), (2, 1, 6.0)])),
            _ => OnOffSwitchStateValue.Of(true),
        };
        Assert.Equal(expected, Hex(JgsMpsProtobuf.EncodeResult([value])));
    }

    private static JgsValue Scalar(double x, JgsNumericClass numericClass)
    {
        JgsValue value = JgsValue.Number(x);
        value.SetNumericClass(numericClass);
        return value;
    }

    private static JgsValue Cell(JgsValue[] items, int rows, int cols)
    {
        JgsValue cell = JgsValue.Cell(items);
        cell.Reshape(rows, cols);
        return cell;
    }

    [Fact]
    public void NoOutputsIsNoBytesAndAnErrorIsItsFields()
    {
        Assert.Empty(JgsMpsProtobuf.EncodeResult([]));
        // nargout.2: error { message: "Too many output arguments." identifier: "MATLAB:TooManyOutputs" } with no frames, 53 bytes.
        byte[] error = JgsMpsProtobuf.EncodeError("MATLAB:TooManyOutputs", "Too many output arguments.", [], withStack: false);
        Assert.Equal(53, error.Length);
        Assert.StartsWith("1233" + "0a1a" + Hex("Too many output arguments."u8.ToArray()), Hex(error), StringComparison.Ordinal);
        // A frame: file (1), name (2), line (3).
        byte[] framed = JgsMpsProtobuf.EncodeError("k14:bad", "An error.", [("k14err", "C:\\x\\k14err.m", 5)]);
        Assert.EndsWith("1a" + "19" + "0a0d" + Hex("C:\\x\\k14err.m"u8.ToArray()) + "1206" + Hex("k14err"u8.ToArray()) + "1805", Hex(framed), StringComparison.Ordinal);
    }

    private static JgsMpsProtobuf.ServerRequest Decode(string hex) => JgsMpsProtobuf.DecodeRequest(Hex(hex), static _ => null);

    [Fact]
    public void TheReaderReadsThePythonClientsRequests()
    {
        // matlab.double([[1,2],[3,4]]): a 2-by-2 column-major.
        JgsMpsProtobuf.ServerRequest matrix = Decode("0801122b080912020202ca01220a20000000000000f03f000000000000084000000000000000400000000000001040");
        Assert.Equal(1, matrix.Nargout);
        JgsValue m = Assert.Single(matrix.Arguments);
        Assert.Equal([2, 2], m.Dims);
        Assert.Equal([1.0, 3.0, 2.0, 4.0], Enumerable.Range(0, 4).Select(i => m.ElementAt(i).AsNumber));

        // matlab.int8([1, -2]) as bytes; matlab.int16([-5, 7]) as varints; a Python int as int64.
        JgsValue int8 = Assert.Single(Decode("0801120d0801120201028a01040a0201fe").Arguments);
        Assert.Equal(JgsNumericClass.Int8, int8.NumericClass);
        Assert.Equal([1.0, -2.0], [int8.ElementAt(0).AsNumber, int8.ElementAt(1).AsNumber]);
        JgsValue int16 = Assert.Single(Decode("080112160803120201029a010d0a0bfbffffffffffffffff0107").Arguments);
        Assert.Equal(JgsNumericClass.Int16, int16.NumericClass);
        Assert.Equal([-5.0, 7.0], [int16.ElementAt(0).AsNumber, int16.ElementAt(1).AsNumber]);
        JgsValue int64 = Assert.Single(Decode("0801120c080712020101ba01030a0105").Arguments);
        Assert.Equal(JgsNumericClass.Int64, int64.NumericClass);
        Assert.Equal(5.0, int64.AsNumber);

        // A complex row, a logical, an empty char (no payload), a string 'abc'.
        JgsValue complex = Assert.Single(Decode("0801122d080912020102ca01240a10000000000000f03f00000000000008401210000000000000004000000000000010c0").Arguments);
        Assert.Equal(new Complex(3, -4), complex.ElementAt(1).AsComplex);
        Assert.True(Assert.Single(Decode("0801120c080a12020101d201030a0101").Arguments).AsBool);
        JgsValue empty = Assert.Single(Decode("08011206080b12020000").Arguments);
        Assert.True(empty.IsCharMatrix);
        Assert.Equal([0, 0], empty.Dims);
        Assert.Equal("abc", Assert.Single(Decode("0801120e080b12020103da01050a03616263").Arguments).AsString);

        // A dict {'b': 1, 'a': 'x'}: a struct with its fields in the dict's order; a list [1, 'a', True]: a cell row.
        JgsValue dict = Assert.Single(Decode("0801122b080c12020101ea01220a01620a0161120c080712020101ba01030a0101120c080b12020101da01030a0178").Arguments);
        Assert.Equal(["b", "a"], dict.AsStruct.Keys);
        Assert.Equal("x", dict.AsStruct["a"].AsString);
        JgsValue list = Assert.Single(Decode("08011233080d12020103e2012a0a0c080712020101ba01030a01010a0c080b12020101da01030a01610a0c080a12020101d201030a0101").Arguments);
        Assert.Equal([1, 3], list.Dims);
        Assert.Equal("a", list.AsCell[1].AsString);

        // nargout=2; nargout absent is 0; a trailing zero byte ends the message (body.trailing).
        Assert.Equal(2, Decode("08021213080912020101ca010a0a08000000000000f03f").Nargout);
        Assert.Equal(0, Decode("1213080912020101ca010a0a08000000000000f03f").Nargout);
        Assert.Single(Decode("08011213080912020101ca010a0a08000000000000f03f0000").Arguments);
    }

    /// <summary>A request of one array built field by field: <c>type</c> (1), <c>dimension</c> (2), <paramref name="extra"/> raw, then a double payload in <paramref name="payloadField"/>.</summary>
    private static JgsMpsProtobuf.ServerRequest Request(int? type, ulong[]? dims, double[]? values, string extra = "", int payloadField = 25, int nargout = 1)
    {
        var array = new JgsProtobufWire.Writer();
        if (type is int t)
        {
            array.VarintField(1, (ulong)t);
        }

        if (dims is not null)
        {
            var packed = new JgsProtobufWire.Writer();
            foreach (ulong d in dims)
            {
                packed.WriteVarint(d);
            }

            array.PackedField(2, packed, always: true);
        }

        array.Append(Hex(extra));
        if (values is not null)
        {
            var doubles = new JgsProtobufWire.Writer();
            foreach (double v in values)
            {
                doubles.WriteDouble(v);
            }

            var payload = new JgsProtobufWire.Writer();
            payload.PackedField(1, doubles);
            array.MessageField(payloadField, payload);
        }

        var request = new JgsProtobufWire.Writer();
        request.VarintField(1, (long)nargout);
        request.MessageField(2, array);
        return JgsMpsProtobuf.DecodeRequest(request.ToArray(), static _ => null);
    }

    [Fact]
    public void WhatMuserveReadsPastOrEndsOnIsRefused()
    {
        // nargout.negative: muserve's own error; garbage and a cut message: 400 InvalidInputArgs.
        Assert.Equal("vector too long", Decode("08ffffffffffffffffff01").Fault);
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Decode("ffffffff"));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Decode("08011213080912020101ca010a0a08000000"));
        // count.short (two elements for three), payload.absent, complex.flag.only (isComplex true, no parts), type.mismatch
        // (INT32 with a double payload), an UNKNOWN layout, OBJECT, two payloads.
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(9, [1, 3], [1, 2]));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(9, [1, 1], null));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(9, [1, 2], [1, 2], extra: "3801"));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(5, [1, 2], [1, 2]));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(9, [1, 1], [1], extra: "3002"));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(15, [1, 1], [1]));
        Assert.Throws<JgsMpsJson.ServerRefusal>(() => Request(9, [1, 1], [1], extra: "c201060a040000c03f"));
        // dims.one is a column; dims.trailing.one drops the 1; type.absent takes the payload's type; an extra element is passed
        // over; no dimensions is 0-by-0; a payload may be absent for an empty array; ROW_MAJOR elements are transposed.
        Assert.Equal([3, 1], Assert.Single(Request(9, [3], [1, 2, 3]).Arguments).Dims);
        Assert.Equal([2, 2], Assert.Single(Request(9, [2, 2, 1], [1, 2, 3, 4]).Arguments).Dims);
        Assert.Equal(JgsNumericClass.Double, Assert.Single(Request(null, [1, 2], [1, 2]).Arguments).NumericClass);
        Assert.Equal(1.0, Assert.Single(Request(9, [1, 1], [1, 2]).Arguments).AsNumber);
        Assert.Equal([0, 0], Assert.Single(Request(9, null, [1, 2]).Arguments).Dims);
        Assert.Equal([0, 3], Assert.Single(Request(9, [0, 3], null).Arguments).Dims);
        JgsValue rowMajor = Assert.Single(Request(9, [2, 3], [1, 2, 3, 4, 5, 6], extra: "3001").Arguments);
        Assert.Equal([1.0, 4.0, 2.0, 5.0, 3.0, 6.0], Enumerable.Range(0, 6).Select(i => rowMajor.ElementAt(i).AsNumber));
    }

    [Fact]
    public void EveryValueGoesRoundThroughAResultAndARequest()
    {
        JgsValue[] values =
        [
            JgsValue.Number(2.5), Row([1, 2, 3]), JgsMatrix.FromColumnMajor([1, 4, 2, 5, 3, 6], 2, 3), Row([-5, 0, 127], JgsNumericClass.Int8),
            Scalar(4294967295, JgsNumericClass.UInt32), JgsValue.ComplexNum(new Complex(1, -1)), JgsValue.Bool(true), JgsValue.Str("hello"),
            JgsValue.CharMatrix(["abc", "def"]), JgsValue.StringArray([JgsValue.Str("a"), JgsValue.Str(JgsBuiltins.MissingSentinel)], 1, 2),
            Cell([JgsValue.Number(1), JgsValue.Str("two")], 2, 1), JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["name"] = JgsValue.Str("p") }),
            JgsValue.Sparse(CscMatrix.FromTriplets(3, 3, [(0, 0, 5.0), (2, 1, 6.0)])), OnOffSwitchStateValue.Of(true),
        ];
        foreach (JgsValue value in values)
        {
            // The result's lhs array, wrapped as a request's rhs: MATLAB_Params { nargout: 1, rhs: <array> }.
            byte[] result = JgsMpsProtobuf.EncodeResult([value]);
            var reader = new JgsProtobufWire.Reader(result);
            Assert.True(reader.Next(out int field, out int wireType));
            Assert.Equal((1, JgsProtobufWire.Delimited), (field, wireType));
            byte[] array = reader.ReadBytes().ToArray();
            var request = new JgsProtobufWire.Writer();
            request.VarintField(1, 1UL);
            request.BytesField(2, array);
            JgsValue back = Assert.Single(JgsMpsProtobuf.DecodeRequest(request.ToArray(), static _ => null).Arguments);
            Assert.Equal(Hex(result), Hex(JgsMpsProtobuf.EncodeResult([back])));
        }
    }
}
