// Stage K8's client (compiler and SDK plan): the MWArray API of a .NET assembly, as R2025b's
// MWArray.dll and its generated K8.dll / K8Native.dll answer it. Each case prints one
// label<TAB>result line: an array's type, size and what ToString says, an element, a conversion,
// or the exception thrown. The same source is built against JGraph's builds with only the three
// using lines changed (run-k8.ps1), so the two transcripts compare line for line.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using MathWorksNative = MathWorks.MATLAB.NET.Arrays.native;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

static string Flat(string s) => s == null ? "null" : s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

static string TypeLabel(Type t)
{
    if (t.Namespace != null && t.Namespace.EndsWith(".native", StringComparison.Ordinal)) return "native." + t.Name;
    if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal)) return t.FullName;
    return t.Name;
}

static string Element(object o) => o switch
{
    null => "null",
    double d => d.ToString("R", CultureInfo.InvariantCulture),
    float f => f.ToString("R", CultureInfo.InvariantCulture),
    char c => "'" + c + "'",
    string s => "\"" + Flat(s) + "\"",
    Array a => Describe(a),
    MWArray m => Describe(m),
    _ when o.GetType().IsPrimitive => Convert.ToString(o, CultureInfo.InvariantCulture),
    _ => Describe(o),
};

static string Describe(object o)
{
    switch (o)
    {
        case null:
            return "null";
        case MWNumericArray n:
            return $"MWNumericArray {string.Join("x", n.Dimensions)} {n.NumericType}{(n.IsComplex ? " complex" : "")}{(n.IsSparse ? " sparse" : "")} <<{Flat(n.ToString())}>>";
        case MWArray a:
            return $"{TypeLabel(a.GetType())} {string.Join("x", a.Dimensions)} {a.ArrayType} <<{Flat(a.ToString())}>>";
        case Array arr:
        {
            var lengths = new List<int>();
            for (int r = 0; r < arr.Rank; r++) lengths.Add(arr.GetLength(r));
            var parts = new List<string>();
            foreach (object x in arr) parts.Add(Element(x));
            return $"{TypeLabel(arr.GetType())} [{string.Join(",", lengths)}] {{{string.Join(", ", parts)}}}";
        }
        default:
            if (o.GetType().Namespace != null && o.GetType().Namespace.EndsWith(".native", StringComparison.Ordinal))
                return $"{TypeLabel(o.GetType())} <<{Flat(o.ToString())}>>";
            return $"{TypeLabel(o.GetType())} {Element(o)}";
    }
}

static string Threw(Exception e)
{
    var parts = new List<string>();
    for (Exception x = e; x != null; x = x.InnerException)
    {
        string text = $"{TypeLabel(x.GetType())}: {Flat(x.Message.Trim())}";
        if (x is MWException mw) text += $" [stack <<{Flat(mw.StackTrace)}>>]";
        parts.Add(text);
    }
    return "THREW " + string.Join(" <- ", parts);
}

static void Line(string label, string text)
{
    Console.Out.Flush();
    Console.WriteLine($"{label}\t{text}");
    Console.Out.Flush();
}

static void Case(string label, Func<object> f)
{
    string text;
    try { text = Describe(f()); }
    catch (Exception e) { text = Threw(e); }
    Line(label, text);
}

static void Do(string label, Action f)
{
    string text;
    try { f(); text = "ok"; }
    catch (Exception e) { text = Threw(e); }
    Line(label, text);
}

K8.K8Class k8 = null;
Do("construct", () => k8 = new K8.K8Class());
if (k8 == null) return;
Case("quiet", () => k8.k8quiet());
Case("mcr.initialized", () => MWMCR.MCRAppInitialized);
Case("mcr.isinitialized", () => MWMCR.IsMCRInitialized());

// --- MWNumericArray: construction ---------------------------------------------------------
Case("num.empty.ctor", () => new MWNumericArray());
Case("num.double", () => new MWNumericArray(1.0));
Case("num.double.frac", () => new MWNumericArray(2.5));
Case("num.int", () => new MWNumericArray(3));
Case("num.int.notdouble", () => new MWNumericArray(3, false));
Case("num.int.double", () => new MWNumericArray(3, true));
Case("num.byte", () => new MWNumericArray((byte)5));
Case("num.byte.notdouble", () => new MWNumericArray((byte)5, false));
Case("num.short", () => new MWNumericArray((short)7));
Case("num.short.notdouble", () => new MWNumericArray((short)7, false));
Case("num.long", () => new MWNumericArray(9L));
Case("num.long.notdouble", () => new MWNumericArray(9L, false));
Case("num.float", () => new MWNumericArray(2.5f));
Case("num.float.notdouble", () => new MWNumericArray(2.5f, false));
Case("num.complex", () => new MWNumericArray(1.0, 2.0));
Case("num.complex.int", () => new MWNumericArray(3, 4));
Case("num.complex.int.notdouble", () => new MWNumericArray(3, 4, false));
Case("num.rows.double", () => new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 }));
Case("num.rows.double.colmajor", () => new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 }, false));
Case("num.rows.double.rowmajor", () => new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 }, true));
Case("num.rows.double.complex", () => new MWNumericArray(1, 2, new double[] { 1, 2 }, new double[] { 3, 4 }));
Case("num.rows.int", () => new MWNumericArray(2, 2, new int[] { 1, 2, 3, 4 }));
Case("num.rows.int.notdouble", () => new MWNumericArray(2, 2, new int[] { 1, 2, 3, 4 }, false, true));
Case("num.rows.int.colmajor", () => new MWNumericArray(2, 2, new int[] { 1, 2, 3, 4 }, false, false));
Case("num.rows.byte", () => new MWNumericArray(1, 3, new byte[] { 1, 2, 255 }));
Case("num.rows.byte.notdouble", () => new MWNumericArray(1, 3, new byte[] { 1, 2, 255 }, false, true));
Case("num.rows.short.notdouble", () => new MWNumericArray(1, 2, new short[] { -1, 2 }, false, true));
Case("num.rows.long.notdouble", () => new MWNumericArray(1, 2, new long[] { -1, 2 }, false, true));
Case("num.rows.float.notdouble", () => new MWNumericArray(1, 2, new float[] { 1.5f, 2 }, false, true));
Case("num.rows.short", () => new MWNumericArray(1, 2, new short[] { -1, 2 }));
Case("num.rows.wrongcount", () => new MWNumericArray(2, 2, new double[] { 1, 2, 3 }));
Case("num.array.vector", () => new MWNumericArray(new double[] { 1, 2, 3 }));
Case("num.array.matrix", () => new MWNumericArray(new double[,] { { 1, 2, 3 }, { 4, 5, 6 } }));
Case("num.array.3d", () => new MWNumericArray(new double[,,] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } }));
Case("num.array.int", () => new MWNumericArray(new int[] { 1, 2 }));
Case("num.array.int.notdouble", () => new MWNumericArray(new int[] { 1, 2 }, false, true));
Case("num.array.int.matrix.notdouble.colmajor", () => new MWNumericArray(new int[,] { { 1, 2 }, { 3, 4 } }, false, false));
Case("num.array.sbyte", () => new MWNumericArray(new sbyte[] { -1, 2 }));
Case("num.array.sbyte.notdouble", () => new MWNumericArray(new sbyte[] { -1, 2 }, false, true));
Case("num.array.ushort.notdouble", () => new MWNumericArray(new ushort[] { 1, 2 }, false, true));
Case("num.array.uint.notdouble", () => new MWNumericArray(new uint[] { 1, 2 }, false, true));
Case("num.array.ulong.notdouble", () => new MWNumericArray(new ulong[] { 1, 2 }, false, true));
Case("num.array.complex", () => new MWNumericArray(new double[] { 1, 2 }, new double[] { 3, 4 }));
Case("num.array.string", () => new MWNumericArray(new string[] { "a" }));
Case("num.array.empty", () => new MWNumericArray(new double[0]));
Case("num.dims", () => new MWNumericArray(new int[] { 2, 3 }));
Case("num.dims.3d", () => new MWNumericArray(new int[] { 2, 1, 2 }));
Case("num.complexity.real", () => new MWNumericArray(MWArrayComplexity.Real, new int[] { 2, 2 }));
Case("num.complexity.complex", () => new MWNumericArray(MWArrayComplexity.Complex, new int[] { 1, 2 }));
Case("num.complexity.int16", () => new MWNumericArray(MWArrayComplexity.Real, MWNumericType.Int16, new int[] { 1, 2 }));
Case("num.complexity.int16.complex", () => new MWNumericArray(MWArrayComplexity.Complex, MWNumericType.Int16, new int[] { 1, 2 }));
Case("num.type.single", () => new MWNumericArray(MWNumericType.Single));
Case("num.type.uint64", () => new MWNumericArray(MWNumericType.UInt64));
Case("num.implicit.int", () => { MWNumericArray x = 5; return x; });
Case("num.implicit.double", () => { MWNumericArray x = 2.5; return x; });
Case("num.implicit.byte", () => { MWNumericArray x = (byte)4; return x; });
Case("num.implicit.long", () => { MWNumericArray x = 4L; return x; });
Case("num.implicit.float", () => { MWNumericArray x = 1.5f; return x; });
Case("num.implicit.short", () => { MWNumericArray x = (short)3; return x; });
Case("num.implicit.doubles", () => { MWNumericArray x = new double[] { 1, 2 }; return x; });
Case("num.implicit.ints", () => { MWNumericArray x = new int[] { 1, 2 }; return x; });
Case("num.implicit.bytes", () => { MWNumericArray x = new byte[] { 1, 2 }; return x; });
Case("num.implicit.floats", () => { MWNumericArray x = new float[] { 1, 2 }; return x; });
Case("num.implicit.array", () => { MWNumericArray x = (Array)new double[,] { { 1, 2 }, { 3, 4 } }; return x; });
Case("num.static.empty", () => MWNumericArray.Empty);
Case("num.static.inf", () => MWNumericArray.Inf);
Case("num.static.nan", () => MWNumericArray.NaN);
Case("num.static.eps", () => MWNumericArray.FloatingPointAccuracy);
Case("num.sparse", () => MWNumericArray.MakeSparse(new int[] { 1, 3 }, new int[] { 1, 2 }, new double[] { 5, 6 }));
Case("num.sparse.sized", () => MWNumericArray.MakeSparse(3, 3, new int[] { 1, 3 }, new int[] { 1, 2 }, new double[] { 5, 6 }));
Case("num.sparse.complex", () => MWNumericArray.MakeSparse(new int[] { 1 }, new int[] { 2 }, new double[] { 5 }, new double[] { 1 }));
Case("num.sparse.alloc", () => MWNumericArray.MakeSparse(2, 2, MWArrayComplexity.Real, 3));
Case("num.mixed.display", () => new MWNumericArray(new double[] { 1, 2.5, 1000 }));
Case("num.large.display", () => new MWNumericArray(123456.789));
Case("num.small.display", () => new MWNumericArray(new double[] { 1e-5, 1 }));
Case("num.inf.display", () => new MWNumericArray(new double[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN }));
Case("num.negative.display", () => new MWNumericArray(new double[] { -1, 2 }));
Case("num.column.display", () => new MWNumericArray(3, 1, new double[] { 1, 2, 3 }));
Case("num.int.matrix.display", () => new MWNumericArray(2, 2, new int[] { 1, 20, 300, 4 }, false, true));

// --- MWNumericArray: what it holds ---------------------------------------------------------
var m23 = new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 });
Case("num.flags", () => $"IsDouble={m23.IsDouble} IsFloat={m23.IsFloat} IsInteger={m23.IsInteger} IsByte={m23.IsByte} IsShort={m23.IsShort} IsLong={m23.IsLong} IsComplex={m23.IsComplex} IsSparse={m23.IsSparse} IsNaN={m23.IsNaN} IsInfinity={m23.IsInfinity}");
Case("num.flags.int32", () => { var x = new MWNumericArray(3, false); return $"IsDouble={x.IsDouble} IsFloat={x.IsFloat} IsInteger={x.IsInteger} IsByte={x.IsByte} IsShort={x.IsShort} IsLong={x.IsLong}"; });
Case("num.flags.nan", () => { var x = MWNumericArray.NaN; return $"IsNaN={x.IsNaN} IsInfinity={x.IsInfinity}"; });
Case("num.props", () => $"NumberofDimensions={m23.NumberofDimensions} NumberOfElements={m23.NumberOfElements} IsEmpty={m23.IsEmpty} IsNumericArray={m23.IsNumericArray} IsCharArray={m23.IsCharArray} IsCellArray={m23.IsCellArray} IsStructArray={m23.IsStructArray} IsLogicalArray={m23.IsLogicalArray} IsStringArray={m23.IsStringArray} ArrayType={m23.ArrayType}");
Case("num.index.1.2", () => m23[1, 2]);
Case("num.index.2.3", () => m23[2, 3]);
Case("num.index.linear.4", () => m23[4]);
Case("num.index.0", () => m23[0]);
Case("num.index.out", () => m23[7]);
Case("num.index.out.2d", () => m23[3, 1]);
Case("num.index.three", () => m23[1, 1, 1]);
Do("num.set.1.1", () => m23[1, 1] = 10);
Case("num.after.set", () => m23);
Do("num.set.grow", () => m23[3, 4] = 1);
Case("num.after.grow", () => m23);
Case("num.toarray", () => m23.ToArray());
Case("num.toarray.real", () => m23.ToArray(MWArrayComponent.Real));
Case("num.tovector", () => m23.ToVector(MWArrayComponent.Real));
var cx = new MWNumericArray(1, 2, new double[] { 1, 2 }, new double[] { 3, 4 });
Case("num.complex.toarray", () => cx.ToArray());
Case("num.complex.toarray.imag", () => cx.ToArray(MWArrayComponent.Imaginary));
Case("num.complex.tovector.imag", () => cx.ToVector(MWArrayComponent.Imaginary));
Case("num.complex.index", () => cx[1, 2]);
Case("num.complex.index.imag", () => cx[MWArrayComponent.Imaginary, 1, 2]);
Do("num.complex.set.imag", () => cx[MWArrayComponent.Imaginary, 1, 1] = 9);
Case("num.complex.after.set", () => cx);
Case("num.int8.toarray", () => new MWNumericArray(new sbyte[] { -1, 2 }, false, true).ToArray());
Case("num.int32.toarray", () => new MWNumericArray(2, 2, new int[] { 1, 2, 3, 4 }, false, true).ToArray());
Case("num.3d.toarray", () => new MWNumericArray(new double[,,] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } }).ToArray());
Case("num.empty.toarray", () => new MWNumericArray().ToArray());
Case("num.toemptyvector", () => new MWNumericArray().ToEmptyVector());
Case("num.scalar.double", () => new MWNumericArray(2.5).ToScalarDouble());
Case("num.scalar.int", () => new MWNumericArray(2.7).ToScalarInteger());
Case("num.scalar.int.neg", () => new MWNumericArray(-2.7).ToScalarInteger());
Case("num.scalar.byte", () => new MWNumericArray(300.0).ToScalarByte());
Case("num.scalar.short", () => new MWNumericArray(2.5).ToScalarShort());
Case("num.scalar.long", () => new MWNumericArray(3.5).ToScalarLong());
Case("num.scalar.float", () => new MWNumericArray(2.5).ToScalarFloat());
Case("num.scalar.of.row", () => new MWNumericArray(new double[] { 1, 2 }).ToScalarDouble());
Case("num.scalar.of.complex", () => new MWNumericArray(1.0, 2.0).ToScalarDouble());
Case("num.scalar.of.int32", () => new MWNumericArray(7, false).ToScalarDouble());
Case("num.cast.double", () => (double)new MWNumericArray(4.5));
Case("num.cast.int", () => (int)new MWNumericArray(4.5));
Case("num.cast.byte", () => (byte)new MWNumericArray(4.5));
Case("num.cast.of.row", () => (double)new MWNumericArray(new double[] { 1, 2 }));
Case("num.equals.same", () => new MWNumericArray(new double[] { 1, 2 }).Equals(new MWNumericArray(new double[] { 1, 2 })));
Case("num.equals.other", () => new MWNumericArray(new double[] { 1, 2 }).Equals(new MWNumericArray(new double[] { 1, 3 })));
Case("num.equals.class", () => new MWNumericArray(1.0).Equals(new MWNumericArray(1, false)));
Case("num.clone", () =>
{
    var a = new MWNumericArray(new double[] { 1, 2 });
    var b = (MWNumericArray)a.Clone();
    b[1] = 9;
    return Describe(a) + " / " + Describe(b);
});
Case("num.dispose", () =>
{
    var a = new MWNumericArray(1.0);
    a.Dispose();
    return a.IsDisposed;
});
Case("num.after.dispose", () =>
{
    var a = new MWNumericArray(1.0);
    a.Dispose();
    return a.ToString();
});

// --- MWCharArray ---------------------------------------------------------------------------
Case("char.empty", () => new MWCharArray());
Case("char.string", () => new MWCharArray("hello"));
Case("char.emptystring", () => new MWCharArray(""));
Case("char.char", () => new MWCharArray('x'));
Case("char.chars", () => new MWCharArray(new char[] { 'a', 'b' }));
Case("char.strings", () => new MWCharArray(new string[] { "ab", "cd" }));
Case("char.strings.ragged", () => new MWCharArray(new string[] { "a", "bcd" }));
Case("char.array", () => new MWCharArray(new char[,] { { 'a', 'b' }, { 'c', 'd' } }));
Case("char.array.strings", () => new MWCharArray((Array)new string[] { "xy", "z" }));
Case("char.dims", () => new MWCharArray(new int[] { 2, 3 }));
Case("char.implicit", () => { MWCharArray c = "text"; return c; });
Case("char.static.empty", () => MWCharArray.Empty);
var chm = new MWCharArray(new string[] { "ab", "cd" });
Case("char.index.1.2", () => chm[1, 2]);
Case("char.index.linear.2", () => chm[2]);
Case("char.toarray", () => chm.ToArray());
Case("char.tostrings", () => chm.ToStringArrayIfIs2DArray());
Case("char.row.tostrings", () => new MWCharArray("row").ToStringArrayIfIs2DArray());
Case("char.cast", () => (char)new MWCharArray("q"));
Case("char.cast.of.row", () => (char)new MWCharArray("qr"));
Do("char.set", () => chm[1, 1] = new MWCharArray("z"));
Case("char.after.set", () => chm);
Case("char.unicode", () => new MWCharArray("\u00e9\u4e2d"));
Case("char.equals", () => new MWCharArray("ab").Equals(new MWCharArray("ab")));

// --- MWLogicalArray ------------------------------------------------------------------------
Case("logical.empty", () => new MWLogicalArray());
Case("logical.true", () => new MWLogicalArray(true));
Case("logical.false", () => new MWLogicalArray(false));
Case("logical.size", () => new MWLogicalArray(2, 2));
Case("logical.dims", () => new MWLogicalArray(new int[] { 1, 3 }));
Case("logical.rows", () => new MWLogicalArray(2, 2, new bool[] { true, false, false, false }));
Case("logical.rows.colmajor", () => new MWLogicalArray(2, 2, new bool[] { true, false, false, false }, true));
Case("logical.rows.rowmajor", () => new MWLogicalArray(2, 2, new bool[] { true, false, false, false }, false));
Case("logical.array", () => new MWLogicalArray(new bool[,] { { true, false }, { false, false } }));
Case("logical.array.vector", () => new MWLogicalArray(new bool[] { true, false, true }));
Case("logical.implicit", () => { MWLogicalArray l = true; return l; });
Case("logical.static.empty", () => MWLogicalArray.Empty);
var lg = new MWLogicalArray(new bool[,] { { true, false }, { false, true } });
Case("logical.isscalar", () => lg.IsScalar + " " + new MWLogicalArray(true).IsScalar);
Case("logical.index", () => lg[1, 2]);
Case("logical.tovector", () => lg.ToVector());
Case("logical.toarray", () => lg.ToArray());
Case("logical.cast", () => { bool b = new MWLogicalArray(true); return b; });
Case("logical.cast.of.matrix", () => { bool b = lg; return b; });
Case("logical.sparse", () => MWLogicalArray.MakeSparse(new int[] { 1, 2 }, new int[] { 1, 2 }, new bool[] { true, true }));
Case("logical.sparse.alloc", () => MWLogicalArray.MakeSparse(2, 3, 2));
Do("logical.set", () => lg[1, 2] = true);
Case("logical.after.set", () => lg);

// --- MWCellArray ---------------------------------------------------------------------------
Case("cell.empty", () => new MWCellArray());
Case("cell.size", () => new MWCellArray(1, 2));
Case("cell.dims", () => new MWCellArray(new int[] { 2, 1, 2 }));
Case("cell.strings", () => new MWCellArray(new MWCharArray(new string[] { "ab", "cd" })));
Case("cell.static.empty", () => MWCellArray.Empty);
var ce = new MWCellArray(1, 3);
Do("cell.set.1", () => ce[1, 1] = 5.0);
Do("cell.set.2", () => ce[1, 2] = "two");
Do("cell.set.3", () => ce[1, 3] = new MWNumericArray(new double[,] { { 3, 4 }, { 5, 6 } }));
Case("cell.after.set", () => ce);
Case("cell.get.1", () => ce[1, 1]);
Case("cell.get.2", () => ce[1, 2]);
Case("cell.get.linear.3", () => ce[3]);
Case("cell.get.out", () => ce[1, 4]);
Case("cell.toarray", () => ce.ToArray());
Case("cell.nested", () => { var c = new MWCellArray(1, 1); var inner = new MWCellArray(1, 1); inner[1] = 1.0; c[1] = inner; return c; });
Case("cell.unset.element", () => new MWCellArray(1, 2)[1, 2]);

// --- MWStructArray -------------------------------------------------------------------------
Case("struct.empty", () => new MWStructArray());
Case("struct.fields", () => new MWStructArray(1, 1, new string[] { "a", "b" }));
Case("struct.array", () => new MWStructArray(1, 2, new string[] { "a" }));
Case("struct.dims", () => new MWStructArray(new int[] { 2, 2 }, new string[] { "x" }));
Case("struct.nofields", () => new MWStructArray(1, 1, new string[0]));
Case("struct.badname", () => new MWStructArray(1, 1, new string[] { "1bad" }));
Case("struct.dupname", () => new MWStructArray(1, 1, new string[] { "a", "a" }));
Case("struct.defs", () => new MWStructArray(new MWArray[] { "a", 1.0, "b", "text" }));
Case("struct.static.empty", () => MWStructArray.Empty);
var st = new MWStructArray(1, 2, new string[] { "a", "b" });
Do("struct.set.a", () => st["a"] = 5.0);
Do("struct.set.b.2", () => st["b", 2] = "two");
Do("struct.set.a.2", () => st["a", 2] = new MWNumericArray(new double[] { 1, 2 }));
Case("struct.after.set", () => st);
Case("struct.get.a", () => st["a"]);
Case("struct.get.a.2", () => st["a", 2]);
Case("struct.get.b.1", () => st["b", 1]);
Case("struct.get.missing", () => st["zz"]);
Case("struct.getfield.a", () => st.GetField("a"));
Case("struct.getfield.b.2", () => st.GetField("b", 2));
Case("struct.isfield", () => st.IsField("a") + " " + st.IsField("zz"));
Case("struct.fieldnames", () => st.FieldNames);
Case("struct.numberoffields", () => st.NumberOfFields);
Do("struct.setfield.new", () => st.SetField("c", 7.0));
Case("struct.after.setfield", () => st.FieldNames);
Case("struct.removefield", () => st.RemoveField("b"));
Case("struct.after.removefield", () => st.FieldNames);
Case("struct.index.element", () => st[1, 2]);
Case("struct.toarray", () => st.ToArray());
Case("struct.scalar", () => { var s = new MWStructArray(1, 1, new string[] { "name", "value" }); s["name"] = "probe"; s["value"] = new MWNumericArray(new double[] { 1, 2, 3 }); return s; });

// --- MWStringArray -------------------------------------------------------------------------
Case("string.empty", () => new MWStringArray());
Case("string.strings", () => new MWStringArray(new string[] { "x", "y" }));
Case("string.dims", () => new MWStringArray(new int[] { 1, 2 }));
Case("string.null", () => new MWStringArray(new string[] { null, "z" }));
Case("string.implicit", () => { MWStringArray s = "one"; return s; });
Case("string.implicit.array", () => { MWStringArray s = new string[] { "a", "b" }; return s; });
Case("string.static.empty", () => MWStringArray.Empty);
var ss = new MWStringArray(new string[] { "x", "y" });
Case("string.index", () => ss[1, 2]);
Case("string.toarray", () => ss.ToArray());
Case("string.cast", () => (string)new MWStringArray("solo"));
Case("string.cast.of.row", () => (string)ss);
Case("string.cast.array", () => (string[])ss);
Do("string.set", () => ss[1, 1] = "w");
Case("string.after.set", () => ss);
Do("string.setarray", () => ss.SetArray("p", "q"));
Case("string.after.setarray", () => ss);

// --- MWArray, MWIndexArray, MWObjectArray --------------------------------------------------
Case("array.implicit.string", () => { MWArray a = "s"; return a; });
Case("array.implicit.double", () => { MWArray a = 3.0; return a; });
Case("index.implicit.ints", () => { MWIndexArray a = new int[] { 1, 2 }; return a; });
Case("index.implicit.int", () => { MWIndexArray a = 4; return a; });
Case("index.implicit.longs", () => { MWIndexArray a = new long[] { 1, 2 }; return a; });
Case("index.implicit.shorts", () => { MWIndexArray a = new short[] { 1, 2 }; return a; });
Case("index.implicit.bytes", () => { MWIndexArray a = new byte[] { 1, 2 }; return a; });
Case("object.empty", () => new MWObjectArray());
Case("object.static.empty", () => MWObjectArray.Empty);
Case("array.convert.native", () => MWArray.ConvertToNativeTypes(
    new MWArray[] { new MWNumericArray(new double[] { 1, 2 }), new MWCharArray("ab"), new MWLogicalArray(true) },
    new Type[] { typeof(double[]), typeof(string), typeof(bool) }));
Case("array.convert.native.matrix", () => MWArray.ConvertToNativeTypes(
    new MWArray[] { new MWNumericArray(2, 2, new double[] { 1, 2, 3, 4 }) }, new Type[] { typeof(double[,]) }));
Case("array.convert.native.mismatch", () => MWArray.ConvertToNativeTypes(
    new MWArray[] { new MWCharArray("ab") }, new Type[] { typeof(double) }));

// --- Second round: orders, indices and displays the first left open ------------------------
Case("logical.rows.asym", () => new MWLogicalArray(2, 2, new bool[] { true, true, false, false }));
Case("logical.rows.asym.true", () => new MWLogicalArray(2, 2, new bool[] { true, true, false, false }, true));
Case("logical.rows.asym.false", () => new MWLogicalArray(2, 2, new bool[] { true, true, false, false }, false));
Case("logical.array.asym", () => new MWLogicalArray(new bool[,] { { true, true }, { false, false } }));
Case("logical.tovector.asym", () => new MWLogicalArray(new bool[,] { { true, true }, { false, false } }).ToVector());
Case("num.rows.complex.colmajor", () => new MWNumericArray(2, 2, new double[] { 1, 2, 3, 4 }, new double[] { 5, 6, 7, 8 }, false));
Case("num.rows.complex.default", () => new MWNumericArray(2, 2, new double[] { 1, 2, 3, 4 }, new double[] { 5, 6, 7, 8 }));
Case("num.array.complex.matrix", () => new MWNumericArray(new double[,] { { 1, 2 }, { 3, 4 } }, new double[,] { { 5, 6 }, { 7, 8 } }));
Case("num.array.int.matrix", () => new MWNumericArray(new int[,] { { 1, 2 }, { 3, 4 } }));
Case("num.array.float", () => new MWNumericArray(new float[] { 1.5f, 2 }));
Case("num.array.float.notdouble", () => new MWNumericArray(new float[] { 1.5f, 2 }, false, true));
Case("num.array.byte", () => new MWNumericArray(new byte[] { 1, 2 }));
Case("num.array.long", () => new MWNumericArray(new long[] { 1, 2 }));
Case("num.array.short.notdouble", () => new MWNumericArray(new short[] { 1, 2 }, false, true));
Case("num.array.bool", () => new MWNumericArray(new bool[] { true }));
Case("num.complex.toarray.real", () => cx.ToArray(MWArrayComponent.Real));
Case("num.complex.tovector.real", () => cx.ToVector(MWArrayComponent.Real));
Case("num.int32.index", () => new MWNumericArray(2, 2, new int[] { 1, 2, 3, 4 }, false, true)[2, 1]);
Case("num.int32.scalar.integer", () => new MWNumericArray(7, false).ToScalarInteger());
Case("num.long.scalar.long", () => new MWNumericArray(7L, false).ToScalarLong());
Case("num.single.scalar.float", () => new MWNumericArray(1.5f, false).ToScalarFloat());
Case("num.byte.scalar.byte", () => new MWNumericArray((byte)7, false).ToScalarByte());
Case("num.short.scalar.short", () => new MWNumericArray((short)7, false).ToScalarShort());
Case("num.cast.long", () => (long)new MWNumericArray(7L, false));
Case("num.cast.float", () => (float)new MWNumericArray(1.5f, false));
Case("num.cast.short", () => (short)new MWNumericArray((short)7, false));
Case("num.uint8.display", () => new MWNumericArray(2, 2, new byte[] { 1, 20, 200, 4 }, false, true));
Case("num.single.matrix.display", () => new MWNumericArray(2, 2, new float[] { 1.5f, 2, 3, 4 }, false, true));
Case("num.negative.matrix.display", () => new MWNumericArray(2, 2, new double[] { -1.5, 2, 3, 4 }));
Case("num.index.set.int32", () => { var a = new MWNumericArray(1, 2, new int[] { 1, 2 }, false, true); a[1, 2] = 2.7; return a; });
Case("num.index.set.mwarray", () => { var a = new MWNumericArray(new double[] { 1, 2 }); a[2] = new MWNumericArray(5.0); return a; });
Case("num.index.set.matrix", () => { var a = new MWNumericArray(new double[] { 1, 2 }); a[2] = new MWNumericArray(new double[] { 5, 6 }); return a; });
Case("num.index.set.complex", () => { var a = new MWNumericArray(new double[] { 1, 2 }); a[2] = new MWNumericArray(1.0, 1.0); return a; });
Case("struct.getfield.b.1", () => st.GetField("a", 1));
Case("struct.getfield.b.0", () => st.GetField("a", 0));
Case("struct.item.a.1.2", () => st["a", 1, 2]);
Case("struct.item.a.3", () => st["a", 3]);
Case("struct.set.out", () => { st["a", 3] = 1.0; return st; });
Case("cell.mixed", () =>
{
    var c = new MWCellArray(2, 6);
    c[1, 1] = new MWLogicalArray(true);
    c[1, 2] = new MWNumericArray(3, false);
    c[1, 3] = new MWStringArray(new string[] { "s" });
    c[1, 4] = new MWStructArray(1, 1, new string[] { "f" });
    c[1, 5] = new MWCellArray(0, 0);
    c[1, 6] = new MWNumericArray(new double[] { 1, 2, 3, 4, 5 });
    c[2, 1] = new MWCharArray("ab");
    c[2, 2] = new MWNumericArray(1.0, 2.0);
    c[2, 3] = new MWCharArray(new string[] { "ab", "cd" });
    c[2, 4] = MWNumericArray.MakeSparse(new int[] { 1 }, new int[] { 1 }, new double[] { 1 });
    c[2, 5] = new MWNumericArray(2.5);
    c[2, 6] = new MWLogicalArray(new bool[] { true, false });
    return c;
});
Case("cell.column.numbers", () => { var c = new MWCellArray(2, 1); c[1] = 1.0; c[2] = 22.5; return c; });
Case("cell.row.long", () => { var c = new MWCellArray(1, 2); c[1] = new MWNumericArray(new double[] { 1, 2 }); c[2] = new MWNumericArray(123456.0); return c; });
Case("cell.empty.strings", () => { var c = new MWCellArray(1, 2); c[1] = new MWCharArray(""); c[2] = new MWStringArray(new string[] { "" }); return c; });
Case("struct.mixed", () =>
{
    var s = new MWStructArray(1, 1, new string[] { "logic", "int", "str", "inner", "cel", "row", "chars", "cplx", "sp", "longrow", "col" });
    s["logic"] = new MWLogicalArray(true);
    s["int"] = new MWNumericArray(3, false);
    s["str"] = new MWStringArray(new string[] { "s" });
    s["inner"] = new MWStructArray(1, 1, new string[] { "f" });
    s["cel"] = ce;
    s["row"] = new MWNumericArray(new double[] { 1, 2.5 });
    s["chars"] = new MWCharArray(new string[] { "ab", "cd" });
    s["cplx"] = new MWNumericArray(1.0, 2.0);
    s["sp"] = MWNumericArray.MakeSparse(new int[] { 1 }, new int[] { 1 }, new double[] { 1 });
    s["longrow"] = new MWNumericArray(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 });
    s["col"] = new MWNumericArray(2, 1, new double[] { 1, 2 });
    return s;
});
Case("string.scalar.display", () => new MWStringArray(new string[] { "only" }));
Case("string.missing.scalar", () => new MWStringArray(new string[] { null }));
Case("string.index.missing", () => new MWStringArray(new string[] { null, "z" })[1]);
Case("char.from.null", () => new MWCharArray((string)null));
Case("num.equals.object", () => new MWNumericArray(1.0).Equals("x"));
Case("cell.equals", () => new MWCellArray(1, 1).Equals(new MWCellArray(1, 1)));
Case("num.tostring.after.clone", () => ((MWArray)m23.Clone()).ToString() == m23.ToString());
Case("num.longrow", () => new MWNumericArray(Enumerable.Range(1, 30).Select(static i => (double)i).ToArray()));
Case("num.longfrac", () => new MWNumericArray(Enumerable.Range(1, 20).Select(static i => i / 3.0).ToArray()));
Case("num.longmatrix", () => new MWNumericArray(2, 15, Enumerable.Range(1, 30).Select(static i => (double)i).ToArray()));
Case("logical.longrow", () => new MWLogicalArray(Enumerable.Repeat(true, 45).ToArray()));
Case("cell.longrow", () => { var c = new MWCellArray(1, 14); for (int i = 1; i <= 14; i++) c[i] = (double)i; return c; });

// --- Calls through the MWArray class -------------------------------------------------------
static object Echo(K8.K8Class k, MWArray x)
{
    MWArray[] o = k.k8echo(2, x);
    return $"{Describe(o[0])} as {o[1]}";
}
Case("echo.double", () => Echo(k8, new MWNumericArray(2.5)));
Case("echo.int", () => Echo(k8, new MWNumericArray(3)));
Case("echo.int32", () => Echo(k8, new MWNumericArray(3, false)));
Case("echo.byte", () => Echo(k8, new MWNumericArray((byte)5, false)));
Case("echo.byte.default", () => Echo(k8, new MWNumericArray((byte)5)));
Case("echo.int8", () => Echo(k8, new MWNumericArray(new sbyte[] { -1, 2 }, false, true)));
Case("echo.int64", () => Echo(k8, new MWNumericArray(9L, false)));
Case("echo.single", () => Echo(k8, new MWNumericArray(1.5f, false)));
Case("echo.complex", () => Echo(k8, new MWNumericArray(1.0, 2.0)));
Case("echo.matrix", () => Echo(k8, new MWNumericArray(2, 3, new double[] { 1, 2, 3, 4, 5, 6 })));
Case("echo.3d", () => Echo(k8, new MWNumericArray(new double[,,] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } })));
Case("echo.empty", () => Echo(k8, new MWNumericArray()));
Case("echo.sparse", () => Echo(k8, MWNumericArray.MakeSparse(3, 3, new int[] { 1, 3 }, new int[] { 1, 2 }, new double[] { 5, 6 })));
Case("echo.char", () => Echo(k8, new MWCharArray("hello")));
Case("echo.charmatrix", () => Echo(k8, new MWCharArray(new string[] { "ab", "cd" })));
Case("echo.emptychar", () => Echo(k8, new MWCharArray("")));
Case("echo.logical", () => Echo(k8, new MWLogicalArray(new bool[] { true, false })));
Case("echo.cell", () => Echo(k8, ce));
Case("echo.struct", () => Echo(k8, st));
Case("echo.string", () => Echo(k8, new MWStringArray(new string[] { "x", "y" })));
Case("echo.string.null", () => Echo(k8, new MWStringArray(new string[] { null, "y" })));
Case("echo.implicit.double", () => Echo(k8, 4.0));
Case("echo.implicit.string", () => Echo(k8, "abc"));
Case("echo.disposed", () => { var a = new MWNumericArray(1.0); a.Dispose(); return Echo(k8, a); });
Case("echo.null", () => Echo(k8, null));
foreach (string kind in new[] { "double", "row", "matrix", "nd", "empty", "empty03", "single", "int8", "uint8", "int16", "uint16",
    "int32", "uint32", "int64", "uint64", "complex", "complexscalar", "logical", "logicalscalar", "char", "charmatrix", "emptychar",
    "cell", "nestedcell", "struct", "structarray", "emptystruct", "string", "strings", "stringmissing", "sparse", "sparselogical",
    "inf", "pi", "large", "small", "mixed", "handle" })
{
    Case("make." + kind, () => k8.k8make(new MWCharArray(kind)));
}
Case("make.uint8.toarray", () => ((MWNumericArray)k8.k8make(new MWCharArray("uint8"))).ToArray());
Case("make.int8.toarray", () => ((MWNumericArray)k8.k8make(new MWCharArray("int8"))).ToArray());
Case("make.int64.scalar", () => ((MWNumericArray)k8.k8make(new MWCharArray("int64"))).ToScalarLong());
Case("make.complex.toarray", () => ((MWNumericArray)k8.k8make(new MWCharArray("complex"))).ToArray(MWArrayComponent.Imaginary));
Case("make.cell.element.3", () => ((MWCellArray)k8.k8make(new MWCharArray("cell")))[3]);
Case("make.struct.name", () => ((MWStructArray)k8.k8make(new MWCharArray("struct")))["name"]);
Case("make.structarray.a.2", () => ((MWStructArray)k8.k8make(new MWCharArray("structarray")))["a", 2]);
Case("make.strings.toarray", () => ((MWStringArray)k8.k8make(new MWCharArray("strings"))).ToArray());
Case("make.strings.missing.toarray", () => ((MWStringArray)k8.k8make(new MWCharArray("stringmissing"))).ToArray());
Case("make.charmatrix.toarray", () => ((MWCharArray)k8.k8make(new MWCharArray("charmatrix"))).ToArray());
Case("make.logical.toarray", () => ((MWLogicalArray)k8.k8make(new MWCharArray("logical"))).ToArray());
Case("make.unknown", () => k8.k8make(new MWCharArray("nothing")));
Case("make.nargout.0", () => k8.k8make(0, new MWCharArray("double")));
Case("make.nargout.1", () => k8.k8make(1, new MWCharArray("double")));
Case("make.nargout.2", () => k8.k8make(2, new MWCharArray("double")));
Case("make.nargout.neg", () => k8.k8make(-1, new MWCharArray("double")));
Case("make.ref", () => { MWArray[] outs = null; k8.k8make(1, ref outs, new MWArray[] { new MWCharArray("row") }); return outs; });
Case("make.ref.toomany.in", () => { MWArray[] outs = null; k8.k8make(1, ref outs, new MWArray[] { new MWCharArray("row"), 1.0 }); return outs; });
Case("make.ref.given", () => { MWArray[] outs = new MWArray[3]; k8.k8make(1, ref outs, new MWArray[] { new MWCharArray("row") }); return outs; });
Case("make.ref.null.in", () => { MWArray[] outs = null; k8.k8make(1, ref outs, null); return outs; });
Case("make.noargs", () => k8.k8make());
Case("vout.0", () => k8.k8vout(0));
Case("vout.1", () => k8.k8vout(1));
Case("vout.3", () => k8.k8vout(3, 1.0, 2.0));
Do("vout.single", () => k8.k8vout());
Do("vout.params", () => k8.k8vout(new MWArray[] { 1.0, 2.0, 3.0 }));
Case("vout.ref", () => { MWArray[] outs = null; k8.k8vout(2, ref outs, new MWArray[] { 1.0 }); return outs; });
Case("args.0", () => k8.k8args());
Case("args.1", () => k8.k8args(1.0));
Case("args.2", () => k8.k8args(1.0, 2.0));
Case("args.4", () => k8.k8args(1.0, 2.0, 3.0, 4.0));
Case("args.n2.4", () => k8.k8args(2, 1.0, 2.0, 3.0, 4.0));
Case("args.n3", () => k8.k8args(3, 1.0));
Do("show.single", () => k8.k8show(new MWNumericArray(new double[] { 1, 2 })));
Case("show.nargout.0", () => k8.k8show(0, new MWCharArray("text")));
Case("show.nargout.1", () => k8.k8show(1, new MWCharArray("text")));
foreach (string kind in new[] { "id", "noid", "lines", "nested", "undefined", "cause", "index" })
{
    Do("err." + kind, () => k8.k8err(new MWCharArray(kind)));
}
Case("err.nargout.0", () => k8.k8err(0, new MWCharArray("id")));
Case("echo.too.many.in", () => { MWArray[] outs = null; k8.k8echo(1, ref outs, new MWArray[] { 1.0, 2.0 }); return outs; });

// --- The second class ----------------------------------------------------------------------
Case("other", () => { using var o = new K8.K8Other(); return o.k8other(new MWNumericArray(21.0)); });

// --- The native class ----------------------------------------------------------------------
K8Native.K8Class n8 = null;
Do("native.construct", () => n8 = new K8Native.K8Class());
static object NEcho(K8Native.K8Class k, object x)
{
    object[] o = k.k8echo(2, x);
    return $"{Describe(o[0])} as {o[1]}";
}
if (n8 != null)
{
    Case("native.echo.double", () => NEcho(n8, 2.5));
    Case("native.echo.int", () => NEcho(n8, 3));
    Case("native.echo.byte", () => NEcho(n8, (byte)3));
    Case("native.echo.sbyte", () => NEcho(n8, (sbyte)3));
    Case("native.echo.short", () => NEcho(n8, (short)3));
    Case("native.echo.long", () => NEcho(n8, 3L));
    Case("native.echo.float", () => NEcho(n8, 1.5f));
    Case("native.echo.bool", () => NEcho(n8, true));
    Case("native.echo.char", () => NEcho(n8, 'c'));
    Case("native.echo.string", () => NEcho(n8, "text"));
    Case("native.echo.doubles", () => NEcho(n8, new double[] { 1, 2, 3 }));
    Case("native.echo.matrix", () => NEcho(n8, new double[,] { { 1, 2 }, { 3, 4 } }));
    Case("native.echo.ints", () => NEcho(n8, new int[] { 1, 2 }));
    Case("native.echo.bools", () => NEcho(n8, new bool[] { true, false }));
    Case("native.echo.strings", () => NEcho(n8, new string[] { "a", "bc" }));
    Case("native.echo.chars", () => NEcho(n8, new char[] { 'a', 'b' }));
    Case("native.echo.objects", () => NEcho(n8, new object[] { 1.0, "two" }));
    Case("native.echo.struct", () =>
    {
        var s = new MathWorksNative.MWStructArray(1, 1, new string[] { "a" });
        s["a"] = 5.0;
        return NEcho(n8, s);
    });
    Case("native.echo.cell", () =>
    {
        var c = new MathWorksNative.MWCellArray(1, 2);
        c[1, 1] = 1.0;
        c[1, 2] = "x";
        return NEcho(n8, c);
    });
    Case("native.echo.null", () => NEcho(n8, null));
    Case("native.echo.mwarray", () => NEcho(n8, new MWNumericArray(2.0)));
    foreach (string kind in new[] { "double", "row", "matrix", "nd", "empty", "single", "int8", "uint8", "int16", "int32", "int64",
        "uint64", "complex", "logical", "logicalscalar", "char", "charmatrix", "emptychar", "cell", "struct", "structarray", "string",
        "strings", "sparse", "handle" })
    {
        Case("native.make." + kind, () => n8.k8make(kind));
    }
    Case("native.struct.field", () => ((MathWorksNative.MWStructArray)n8.k8make("struct"))["name"]);
    Case("native.struct.fieldnames", () => ((MathWorksNative.MWStructArray)n8.k8make("struct")).FieldNames);
    Case("native.cell.element", () => ((MathWorksNative.MWCellArray)n8.k8make("cell"))[1, 3]);
    Case("native.vout.3", () => n8.k8vout(3, 1.0));
    Do("native.err", () => n8.k8err("id"));
    Case("native.make.nargout.2", () => n8.k8make(2, "double"));
    Case("native.struct.new", () => new MathWorksNative.MWStructArray(1, 1, new string[] { "q" }));
    Case("native.cell.new", () => new MathWorksNative.MWCellArray(1, 2));
    n8.Dispose();
}

k8.Dispose();
Case("after.dispose.call", () => k8.k8make(new MWCharArray("double")));
Line("end", "");
