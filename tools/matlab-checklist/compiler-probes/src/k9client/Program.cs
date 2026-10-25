// Stage K9's client (compiler and SDK plan): the MATLAB Data API for .NET, as R2025b's
// MathWorks.MATLAB.Runtime and MathWorks.MATLAB.Types answer it over the K9 archive and the strongly
// typed interface generated with it. Each case prints one label<TAB>result line: a value's type and
// contents, a conversion, or the exception thrown. The same source is built against JGraph's builds
// with only the using lines changed (run-k9.ps1), so the two transcripts compare line for line.
// Arguments: the K9 archive, then the K9V archive.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MathWorks.MATLAB.Runtime;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
string ctf = args[0];
string ctfV = args[1];
string vendor = typeof(MATLABArray).Namespace.Split('.')[0] + ".MATLAB.";

string Flat(string s) => s == null ? "null" : s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

string TypeLabel(Type t)
{
    if (t == null) return "null";
    if (t.IsArray) return TypeLabel(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
    if (t.IsGenericType)
    {
        string name = t.Name.Substring(0, t.Name.IndexOf('`'));
        string ns = t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal) ? t.Namespace + "." : "";
        return ns + name + "<" + string.Join(",", t.GetGenericArguments().Select(TypeLabel)) + ">";
    }
    string full = t.FullName ?? t.Name;
    if (full.StartsWith(vendor, StringComparison.Ordinal)) return full.Substring(vendor.Length);
    return full.Replace('+', '.');
}

string Number(object o) => o switch
{
    double d => d.ToString("R", CultureInfo.InvariantCulture),
    float f => f.ToString("R", CultureInfo.InvariantCulture),
    Complex c => "(" + c.Real.ToString("R", CultureInfo.InvariantCulture) + "," + c.Imaginary.ToString("R", CultureInfo.InvariantCulture) + "i)",
    _ => Convert.ToString(o, CultureInfo.InvariantCulture),
};

string Show(object o, int depth = 0)
{
    if (depth > 6) return "...";
    switch (o)
    {
        case null:
            return "null";
        case string s:
            return "\"" + Flat(s) + "\"";
        case char c:
            return "'" + c + "'";
        case bool b:
            return b ? "true" : "false";
        case Enum e:
            return TypeLabel(e.GetType()) + "." + e;
        case Complex or double or float:
            return Number(o);
        case MATLABStruct st:
        {
            string text;
            try
            {
                var names = st.GetFieldNames().ToList();
                text = "MATLABStruct{" + string.Join(", ", names.Select(n => n + "=" + Show(st.GetField(n), depth + 1))) + "}";
            }
            catch (Exception e) { text = "MATLABStruct{" + Threw(e) + "}"; }
            return text;
        }
        case MATLABArray m:
            return TypeLabel(m.GetType()) + " <<" + Flat(SafeToString(m)) + ">>";
        case Array arr:
        {
            var lengths = new List<int>();
            for (int r = 0; r < arr.Rank; r++) lengths.Add(arr.GetLength(r));
            var parts = new List<string>();
            foreach (object x in arr) parts.Add(Show(x, depth + 1));
            return TypeLabel(arr.GetType()) + " [" + string.Join(",", lengths) + "] {" + string.Join(", ", parts) + "}";
        }
        case IDictionary dict:
        {
            var parts = new List<string>();
            foreach (DictionaryEntry kv in dict) parts.Add(Show(kv.Key, depth + 1) + ": " + Show(kv.Value, depth + 1));
            return TypeLabel(o.GetType()) + " {" + string.Join(", ", parts) + "}";
        }
        case ITuple tuple:
        {
            var parts = new List<string>();
            for (int k = 0; k < tuple.Length; k++) parts.Add(Show(tuple[k], depth + 1));
            return TypeLabel(o.GetType()) + " (" + string.Join(", ", parts) + ")";
        }
        case Task task:
            return TypeLabel(o.GetType()) + " " + task.Status;
        default:
            if (o.GetType().IsPrimitive || o is decimal) return TypeLabel(o.GetType()) + " " + Number(o);
            return TypeLabel(o.GetType()) + " <<" + Flat(SafeToString(o)) + ">>";
    }
}

string SafeToString(object o)
{
    try { return o.ToString(); }
    catch (Exception e) { return Threw(e); }
}

string Threw(Exception e)
{
    var parts = new List<string>();
    for (Exception x = e; x != null; x = x.InnerException)
    {
        if (x is AggregateException agg && agg.InnerExceptions.Count == 1 && x.InnerException != null)
        {
            parts.Add(TypeLabel(x.GetType()));
            continue;
        }
        parts.Add(TypeLabel(x.GetType()) + ": " + Flat(x.Message.Trim()));
    }
    return "THREW " + string.Join(" <- ", parts);
}

void Line(string label, string text)
{
    Console.Out.Flush();
    Console.Error.Flush();
    Console.WriteLine(label + "\t" + text);
    Console.Out.Flush();
}

void Case(string label, Func<object> f)
{
    string text;
    try { text = Show(f()); }
    catch (Exception e) { text = Threw(e); }
    Line(label, text);
}

void Do(string label, Action f)
{
    string text;
    try { f(); text = "ok"; }
    catch (Exception e) { text = Threw(e); }
    Line(label, text);
}

// The process's standard error is the client's too: R2025b writes a MATLAB error's report there.
var stderrMarked = new MarkedWriter(Console.Out, "STDERR| ");
Console.SetError(stderrMarked);

// --- The public surface of the two assemblies, as reflection lists it -------------------------
foreach (Assembly asm in new[] { typeof(MATLABRuntime).Assembly, typeof(MATLABArray).Assembly })
{
    foreach (Type t in asm.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        string kind = t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "interface" : t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
        string bases = t.BaseType == null ? "" : " : " + TypeLabel(t.BaseType);
        string ifaces = string.Join(",", t.GetInterfaces().Select(TypeLabel).OrderBy(n => n, StringComparer.Ordinal));
        Line("api." + TypeLabel(t), kind + bases + (ifaces.Length > 0 ? " [" + ifaces + "]" : ""));
        var members = new List<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (MemberInfo m in t.GetMembers(flags))
        {
            switch (m)
            {
                case ConstructorInfo c:
                    members.Add("ctor(" + string.Join(", ", c.GetParameters().Select(p => TypeLabel(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? "=" + Show(p.DefaultValue) : ""))) + ")");
                    break;
                case MethodInfo mi when !mi.IsSpecialName:
                    members.Add((mi.IsStatic ? "static " : "") + TypeLabel(mi.ReturnType) + " " + mi.Name + (mi.IsGenericMethod ? "<" + string.Join(",", mi.GetGenericArguments().Select(a => a.Name)) + ">" : "") + "(" + string.Join(", ", mi.GetParameters().Select(p => TypeLabel(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? "=" + Show(p.DefaultValue) : ""))) + ")");
                    break;
                case PropertyInfo pi:
                    members.Add("prop " + TypeLabel(pi.PropertyType) + " " + pi.Name + (pi.GetIndexParameters().Length > 0 ? "[" + string.Join(",", pi.GetIndexParameters().Select(p => TypeLabel(p.ParameterType))) + "]" : "") + " {" + (pi.GetGetMethod() != null ? " get;" : "") + (pi.GetSetMethod() != null ? " set;" : "") + " }");
                    break;
                case FieldInfo fi when !t.IsEnum || fi.IsStatic:
                    members.Add("field " + TypeLabel(fi.FieldType) + " " + fi.Name + (t.IsEnum ? "=" + Convert.ToInt64(fi.GetRawConstantValue(), CultureInfo.InvariantCulture) : ""));
                    break;
            }
        }
        foreach (string m in members.OrderBy(n => n, StringComparer.Ordinal)) Line("api." + TypeLabel(t) + ".", m);
        var usage = t.GetCustomAttribute<AttributeUsageAttribute>();
        if (usage != null) Line("api." + TypeLabel(t) + ".usage", usage.ValidOn + " multiple=" + usage.AllowMultiple + " inherited=" + usage.Inherited);
    }
}

// --- Starting --------------------------------------------------------------------------------
dynamic matlab = MATLABRuntime.StartMATLAB(ctf);
Case("start.type", () => TypeLabel(((object)matlab).GetType()));
Case("start.disposed", () => matlab.Disposed);
Case("quiet", () => matlab.k9quiet());
Case("start.tostring", () => ((object)matlab).ToString() == ((object)matlab).GetType().FullName);

// --- .NET values passed to MATLAB ----------------------------------------------------------
var d234 = new double[2, 3, 4];
for (int i = 0; i < 2; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 4; k++) d234[i, j, k] = 100 * i + 10 * j + k;
var inbound = new List<(string, object)>
{
    ("double", 2.5), ("negzero", -0.0), ("nan", double.NaN), ("inf", double.NegativeInfinity),
    ("float", 1.5f), ("sbyte", (sbyte)-5), ("byte", (byte)200), ("short", (short)-7), ("ushort", (ushort)7),
    ("int", -100000), ("uint", 4000000000u), ("long", -9007199254740993L), ("ulong", ulong.MaxValue),
    ("bool", true), ("char", 'x'), ("string", "hello"), ("emptystring", ""), ("unicode", "\u03bb\u20acA"),
    ("complex", new Complex(1, -1)), ("decimal", 1.5m), ("half", (Half)1.5), ("nint", (nint)5),
    ("doubles", new double[] { 1, 2, 3 }), ("doubles0", new double[0]), ("doubles2", new double[,] { { 1, 2, 3 }, { 4, 5, 6 } }),
    ("doubles3", d234), ("doubles03", new double[0, 3]), ("doubles11", new double[,] { { 7 } }), ("doubles31", new double[,] { { 1 }, { 2 }, { 3 } }),
    ("jagged", new double[][] { new double[] { 1, 2 }, new double[] { 3 } }),
    ("floats", new float[] { 1.5f, 2.5f }), ("ints", new int[] { 1, 2, 3 }), ("sbytes", new sbyte[] { -1, 2 }),
    ("bytes2", new byte[,] { { 1, 2 }, { 3, 4 } }), ("longs", new long[] { 5, 6 }), ("ulongs", new ulong[] { 7 }),
    ("shorts", new short[] { 1 }), ("ushorts", new ushort[] { 2 }), ("uints", new uint[] { 3 }),
    ("bools", new bool[] { true, false }), ("bools2", new bool[,] { { true }, { false } }),
    ("chars", new char[] { 'a', 'b' }), ("chars2", new char[,] { { 'a', 'b' }, { 'c', 'd' } }),
    ("strings", new string[] { "a", "b" }), ("strings2", new string[,] { { "a", "b" }, { "c", "d" } }),
    ("stringsnull", new string[] { "a", null }), ("strings0", new string[0]),
    ("objects", new object[] { 1.0, "two", new double[] { 3, 4 } }), ("objects0", new object[0]),
    ("objects2", new object[,] { { 1.0, 2.0 }, { 3.0, 4.0 } }), ("objectsnull", new object[] { null }),
    ("objectsnested", new object[] { new object[] { 1.0 }, new object[0] }),
    ("complexes", new Complex[] { new Complex(1, 2), new Complex(3, -4) }),
    ("struct", new MATLABStruct(("x", 2.0), ("y", "a"))),
    ("structnested", new MATLABStruct(("inner", new MATLABStruct(("z", 1.0))), ("list", new object[] { 1.0, "a" }))),
    ("structs", new MATLABStruct[] { new MATLABStruct(("a", 1.0)), new MATLABStruct(("a", 2.0)) }),
    ("structsdiffer", new MATLABStruct[] { new MATLABStruct(("a", 1.0)), new MATLABStruct(("b", 2.0)) }),
    ("structs0", new MATLABStruct[0]),
    ("structempty", new MATLABStruct()),
    ("dict", new Dictionary<string, double> { ["b"] = 2, ["a"] = 1 }),
    ("dictnum", new Dictionary<double, string> { [1] = "x", [2] = "y" }),
    ("dictobj", new Dictionary<string, object> { ["p"] = 1.0, ["q"] = "two" }),
    ("dictempty", new Dictionary<string, double>()),
    ("dictint", new Dictionary<int, int> { [1] = 2 }),
    ("dictsorted", new SortedDictionary<string, double> { ["k"] = 3 }),
    ("dictchar", new Dictionary<char, bool> { ['c'] = true }),
    ("enum", K9.k9color.green), ("enums", new K9.k9color[] { K9.k9color.red, K9.k9color.blue }),
    ("enumplain", DayOfWeek.Monday),
    ("list", new List<double> { 1, 2 }), ("tuple", (1.0, 2.0)), ("datetime", new DateTime(2026, 10, 24)),
    ("timespan", TimeSpan.FromSeconds(90)), ("guid", Guid.Empty), ("intptr", IntPtr.Zero), ("object", new object()),
    ("null", null), ("provider", (object)matlab),
};
foreach ((string name, object value) in inbound)
{
    Case("in." + name, () => (string)matlab.k9describe(value));
}
Case("in.namedcall.null", () => (string)matlab.k9describe(null));

// --- MATLAB values passed to .NET ----------------------------------------------------------
string[] kinds =
{
    "double", "negzero", "row", "column", "matrix", "nd", "nd4", "trailing", "empty", "empty03", "single", "singles",
    "int8", "int8s", "uint8", "uint8m", "int16", "uint16", "int32", "int32s", "uint32", "int64", "uint64",
    "complex", "complexrow", "complexsingle", "complexint8", "logical", "logicals", "logicalm",
    "char", "char1", "charmatrix", "emptychar", "unicode", "string", "strings", "stringm", "stringmissing",
    "stringsmissing", "emptystring", "stringempty", "cell", "cellcol", "cellm", "cellstr", "cellstrcol",
    "nestedcell", "emptycell", "struct", "structnested", "structarray", "structm", "emptystruct", "nofields",
    "sparse", "sparselogical", "sparsecomplex", "inf", "nan", "pi", "enum", "enums", "point", "points",
    "counter", "dict", "dictnum", "dictcell", "dictempty", "dictconfigured", "handle", "datetime", "table", "map",
    "categorical", "duration",
};
var casts = new List<(string, Func<dynamic, object>)>
{
    ("object", d => (object)d), ("double", d => (double)d), ("float", d => (float)d), ("int", d => (int)d),
    ("long", d => (long)d), ("ulong", d => (ulong)d), ("sbyte", d => (sbyte)d), ("byte", d => (byte)d),
    ("short", d => (short)d), ("ushort", d => (ushort)d), ("uint", d => (uint)d), ("bool", d => (bool)d),
    ("char", d => (char)d), ("string", d => (string)d), ("Complex", d => (Complex)d), ("decimal", d => (decimal)d),
    ("double[]", d => (double[])d), ("double[,]", d => (double[,])d), ("double[,,]", d => (double[,,])d),
    ("float[]", d => (float[])d), ("int[]", d => (int[])d), ("sbyte[]", d => (sbyte[])d), ("byte[,]", d => (byte[,])d),
    ("Complex[]", d => (Complex[])d), ("bool[]", d => (bool[])d), ("bool[,]", d => (bool[,])d),
    ("char[]", d => (char[])d), ("char[,]", d => (char[,])d), ("string[]", d => (string[])d), ("string[,]", d => (string[,])d),
    ("object[]", d => (object[])d), ("object[,]", d => (object[,])d),
    ("MATLABArray", d => (MATLABArray)d), ("MATLABStruct", d => (MATLABStruct)d), ("MATLABStruct[]", d => (MATLABStruct[])d),
    ("MATLABObject", d => (MATLABObject)d), ("MATLABObject[]", d => (MATLABObject[])d),
    ("k9color", d => (K9.k9color)d), ("k9color[]", d => (K9.k9color[])d), ("DayOfWeek", d => (DayOfWeek)d),
    ("Dictionary<string,double>", d => (Dictionary<string, double>)d), ("Dictionary<double,string>", d => (Dictionary<double, string>)d),
    ("Dictionary<string,object>", d => (Dictionary<string, object>)d), ("IDictionary<string,double>", d => (IDictionary<string, double>)d),
    ("IReadOnlyDictionary<string,double>", d => (IReadOnlyDictionary<string, double>)d),
    ("Dictionary<object,object>", d => (Dictionary<object, object>)d),
    ("List<double>", d => (List<double>)d), ("IEnumerable", d => (IEnumerable)d), ("DateTime", d => (DateTime)d),
    ("ValueTuple<double>", d => (ValueTuple<double>)d),
};
foreach (string kind in kinds)
{
    object r;
    try { r = matlab.k9make(kind); }
    catch (Exception e) { Line("out." + kind, Threw(e)); continue; }
    Line("out." + kind, Show(r));
    foreach ((string name, Func<dynamic, object> cast) in casts)
    {
        Case("out." + kind + "." + name, () => cast(r));
    }
}

// Implicit conversions, IConvertible, ToString and disposal.
{
    dynamic two = matlab.k9make("double");
    Case("implicit.double", () => { double v = two; return v; });
    Case("implicit.int", () => { int v = two; return v; });
    Case("implicit.Complex", () => { Complex v = two; return v; });
    Case("implicit.string", () => { string v = matlab.k9make("char"); return v; });
    Case("implicit.doubles", () => { double[] v = matlab.k9make("row"); return v; });
    Case("convertible.ToDouble", () => Convert.ToDouble((object)two));
    Case("convertible.ToInt32", () => Convert.ToInt32((object)two));
    Case("convertible.ToString", () => Convert.ToString((object)two, CultureInfo.InvariantCulture));
    Case("convertible.ChangeType", () => Convert.ChangeType((object)two, typeof(float), CultureInfo.InvariantCulture));
    Case("convertible.TypeCode", () => ((IConvertible)two).GetTypeCode().ToString());
    Case("convertible.char.ToString", () => Convert.ToString((object)matlab.k9make("char"), CultureInfo.InvariantCulture));
    Case("convertible.row.ToDouble", () => Convert.ToDouble((object)matlab.k9make("row")));
    Case("tostring.double", () => ((object)two).ToString());
    Case("tostring.struct", () => ((object)matlab.k9make("struct")).ToString());
    Case("tostring.point", () => ((object)matlab.k9make("point")).ToString());
    Case("equals.same", () => ((object)two).Equals((object)two));
    Case("equals.other", () => ((object)two).Equals((object)matlab.k9make("double")));
    Case("member.on.array", () => two.nosuch);
    Case("method.on.array", () => two.nosuch());
    Case("set.on.array", () => { two.nosuch = 1.0; return "set"; });
    dynamic gone = matlab.k9make("row");
    Do("dispose", () => ((IDisposable)gone).Dispose());
    Case("dispose.convert", () => (double[])gone);
    // Not "dispose.pass": passing a disposed array to a function ends R2025b's process (std::terminate, a pure
    // virtual call in foundation::matlabdata::Array::getType).
    Do("dispose.twice", () => ((IDisposable)gone).Dispose());
    Case("roundtrip.array", () => (string)matlab.k9describe(matlab.k9make("matrix")));
    Case("roundtrip.struct", () => (string)matlab.k9describe(matlab.k9make("structarray")));
    Case("roundtrip.handle", () => (string)matlab.k9describe(matlab.k9make("handle")));
    Case("roundtrip.table", () => (string)matlab.k9describe(matlab.k9make("table")));
    Case("roundtrip.cell.of.arrays", () => (string)matlab.k9describe(new object[] { matlab.k9make("row"), matlab.k9make("point") }));
}

// --- MATLABStruct ----------------------------------------------------------------------------
{
    dynamic s = matlab.k9make("struct");
    MATLABStruct ms = (MATLABStruct)s;
    Case("struct.type", () => TypeLabel(((object)s).GetType()));
    Case("struct.names", () => string.Join(",", ms.GetFieldNames()));
    Case("struct.names.type", () => TypeLabel(ms.GetFieldNames().GetType()));
    Case("struct.count", () => ms.Count());
    Case("struct.isfield", () => ms.IsField("name"));
    Case("struct.isfield.case", () => ms.IsField("Name"));
    Case("struct.isfield.missing", () => ms.IsField("nosuch"));
    Case("struct.getfield", () => ms.GetField("name"));
    Case("struct.getfield.value", () => ms.GetField("value"));
    Case("struct.getfield.missing", () => ms.GetField("nosuch"));
    Case("struct.getfield.null", () => ms.GetField(null));
    Case("struct.member", () => s.name);
    Case("struct.member.value", () => s.value);
    Case("struct.member.missing", () => s.nosuch);
    Case("struct.member.set", () => { s.name = "x"; return s.name; });
    Case("struct.method", () => s.GetField("name"));
    Case("struct.equals.self", () => ms.Equals(ms));
    Case("struct.equals.again", () => ms.Equals((MATLABStruct)matlab.k9make("struct")));
    Case("struct.equals.built", () => new MATLABStruct(("a", 1.0)).Equals(new MATLABStruct(("a", 1.0))));
    Case("struct.equals.order", () => new MATLABStruct(("a", 1.0), ("b", 2.0)).Equals(new MATLABStruct(("b", 2.0), ("a", 1.0))));
    Case("struct.hash.same", () => new MATLABStruct(("a", 1.0)).GetHashCode() == new MATLABStruct(("a", 1.0)).GetHashCode());
    Case("struct.new", () => new MATLABStruct(("x", 2.0), ("y", "a")));
    Case("struct.new.empty", () => new MATLABStruct());
    Case("struct.new.duplicate", () => new MATLABStruct(("x", 1.0), ("x", 2.0)));
    Case("struct.new.invalid", () => new MATLABStruct(("1x", 1.0)));
    Case("struct.new.space", () => new MATLABStruct(("a b", 1.0)));
    Case("struct.new.nullname", () => new MATLABStruct((null, 1.0)));
    Case("struct.new.nullvalue", () => new MATLABStruct(("a", null)));
    Case("struct.new.nullvalue.pass", () => (string)matlab.k9describe(new MATLABStruct(("a", null))));
    Case("struct.new.badvalue", () => (string)matlab.k9describe(new MATLABStruct(("a", Guid.Empty))));
    Case("struct.new.count", () => new MATLABStruct(("x", 2.0), ("y", "a")).Count());
    Case("struct.new.getfield", () => new MATLABStruct(("x", 2.0)).GetField("x"));
    Case("struct.new.member", () => ((dynamic)new MATLABStruct(("x", 2.0))).x);
    Case("struct.new.set", () => { dynamic n = new MATLABStruct(("x", 2.0)); n.x = 3.0; return n.x; });
    Case("struct.new.tostring", () => new MATLABStruct(("x", 2.0)).ToString());
    Case("struct.new.convert", () => (double)(dynamic)new MATLABStruct(("x", 2.0)));
    Case("struct.array.get", () => ((MATLABStruct[])matlab.k9make("structarray"))[1].GetField("a"));
    Case("struct.nested.inner", () => ((MATLABStruct)matlab.k9make("structnested")).GetField("inner"));
    Case("struct.nested.list", () => ((MATLABStruct)matlab.k9make("structnested")).GetField("list"));
}

// --- MATLABObject ----------------------------------------------------------------------------
{
    dynamic p = matlab.k9point(3.0, 4.0);
    Case("object.type", () => TypeLabel(((object)p).GetType()));
    Case("object.X", () => p.X);
    Case("object.X.double", () => (double)p.X);
    Case("object.Tag", () => p.Tag);
    Case("object.Tag.string", () => (string)p.Tag);
    Case("object.missing", () => p.nosuch);
    Case("object.norm", () => p.norm());
    Case("object.norm.double", () => (double)p.norm());
    Case("object.norm.nargout1", () => p.norm(new RunOptions(nargout: 1)));
    Case("object.norm.nargout0", () => p.norm(new RunOptions(nargout: 0)));
    Case("object.scaled", () => p.scaled(2.0));
    Case("object.scaled.X", () => (double)p.scaled(2.0).X);
    Case("object.scaled.bad", () => p.scaled("x"));
    Case("object.parts", () => p.parts(new RunOptions(nargout: 2)));
    Case("object.parts.tuple", () => { (double a, double b) = ((double, double))p.parts(new RunOptions(nargout: 2)); return a + b; });
    Case("object.nomethod", () => p.nosuch());
    Case("object.set.value", () => { p.X = 5.0; return p.X; });
    Case("object.set.private", () => { p.Tag = "t"; return p.Tag; });
    Case("object.pass", () => (string)matlab.k9describe(p));
    Case("object.static", () => matlab.k9point.origin());
    Case("object.static.X", () => (double)matlab.k9point.origin().X);
    Case("object.static.type", () => TypeLabel(((object)matlab.k9point).GetType()));
    Case("object.noargs", () => (double)matlab.k9point().X);
    Case("object.array", () => matlab.k9make("points"));
    Case("object.array.X", () => matlab.k9make("points").X);
    Case("object.array.norm", () => matlab.k9make("points").norm());
    Case("object.array.cast", () => ((MATLABObject[])matlab.k9make("points")).Length);
    Case("object.array.cast.X", () => (double)((dynamic)((MATLABObject[])matlab.k9make("points"))[1]).X);
    Case("object.array.pass", () => (string)matlab.k9describe((MATLABObject[])matlab.k9make("points")));
    Case("object.array.mixed", () => (string)matlab.k9describe(new MATLABObject[] { (MATLABObject)matlab.k9point(1.0, 1.0), (MATLABObject)matlab.k9counter(1.0) }));
    Case("object.array.empty", () => (string)matlab.k9describe(new MATLABObject[0]));
    Case("object.private.field", () => typeof(MATLABObject).GetField("Matlab", BindingFlags.Instance | BindingFlags.NonPublic) != null);
    dynamic c = matlab.k9counter(1.0);
    Case("handle.type", () => TypeLabel(((object)c).GetType()));
    Case("handle.Count", () => (double)c.Count);
    Case("handle.increment", () => c.increment());
    Case("handle.increment.nargout0", () => c.increment(new RunOptions(nargout: 0)));
    Case("handle.Count.after", () => (double)c.Count);
    Case("handle.increment.by", () => c.increment(new RunOptions(nargout: 0), 5.0));
    Case("handle.Count.by", () => (double)c.Count);
    Case("handle.set", () => { c.Count = 10.0; return (double)c.Count; });
    Case("handle.set.bad", () => { c.Count = "x"; return (double)c.Count; });
    Case("handle.set.untyped", () => { c.Label = new double[] { 1, 2 }; return c.Label; });
    Case("handle.current", () => (double)c.current());
    Case("handle.pass", () => (string)matlab.k9describe(c));
    Case("handle.shared", () => { dynamic same = matlab.k9echo(c); same.Count = 42.0; return (double)c.Count; });
    Case("handle.delete", () => c.delete(new RunOptions(nargout: 0)));
    Case("handle.after.delete", () => (double)c.Count);
    Case("enum.type", () => TypeLabel(((object)matlab.k9make("enum")).GetType()));
    Case("enum.name", () => matlab.k9make("enum").ToString());
    Case("enum.pass", () => (string)matlab.k9describe(matlab.k9make("enum")));
    Case("enum.member", () => matlab.k9color.blue);
    Case("enum.member.cast", () => (K9.k9color)matlab.k9color.blue);
}

// --- Calls -----------------------------------------------------------------------------------
{
    Case("call.nargout0", () => matlab.k9multi(new RunOptions(nargout: 0), 1.0));
    Case("call.nargout1", () => matlab.k9multi(new RunOptions(nargout: 1), 1.0));
    Case("call.nargout2", () => matlab.k9multi(new RunOptions(nargout: 2), 1.0));
    Case("call.nargout3.tuple", () => { (double a, double b, string c3) = ((double, double, string))matlab.k9multi(new RunOptions(nargout: 3), 1.0); return a + "|" + b + "|" + c3; });
    Case("call.nargout3.tuple.dynamic", () => { (object a, object b, object c3) = ((object, object, object))matlab.k9multi(new RunOptions(nargout: 3), 1.0); return TypeLabel(((object)a).GetType()) + "|" + TypeLabel(((object)c3).GetType()); });
    Case("call.nargout3.object", () => (object)matlab.k9multi(new RunOptions(nargout: 3), 1.0));
    Case("call.nargout3.wrongtuple", () => { (double a, double b) = ((double, double))matlab.k9multi(new RunOptions(nargout: 3), 1.0); return a + b; });
    Case("call.nargout3.longtuple", () => { (double a, double b, string c3, double d) = ((double, double, string, double))matlab.k9multi(new RunOptions(nargout: 3), 1.0); return a; });
    Case("call.nargout2.double", () => (double)matlab.k9multi(new RunOptions(nargout: 2), 1.0));
    Case("call.nargout2.pass", () => (string)matlab.k9describe(matlab.k9multi(new RunOptions(nargout: 2), 1.0)));
    Case("call.nargout2.member", () => matlab.k9multi(new RunOptions(nargout: 2), 1.0).X);
    Case("call.nargout4", () => matlab.k9multi(new RunOptions(nargout: 4), 1.0));
    Case("call.nargout.negative", () => matlab.k9multi(new RunOptions(nargout: -1), 1.0));
    Case("call.nargout0.tuple", () => { (double a, double b) = ((double, double))matlab.k9multi(new RunOptions(nargout: 0), 1.0); return a; });
    Case("call.nargout1.tuple1", () => { ValueTuple<double> t1 = matlab.k9multi(new RunOptions(nargout: 1), 1.0); return t1.Item1; });
    Case("call.runoptions.default", () => matlab.k9multi(new RunOptions(), 1.0));
    Case("call.runoptions.props", () => { var o = new RunOptions(); return o.Nargout + "|" + o.Async + "|" + (o.Output == null ? "null" : o.Output == Console.Out ? "Console.Out" : TypeLabel(o.Output.GetType())) + "|" + (o.Error == null ? "null" : TypeLabel(o.Error.GetType())) + "|" + o.CancelToken.CanBeCanceled; });
    Case("call.runoptions.ctor", () => { var o = new RunOptions(nargout: 3, async: true); return o.Nargout + "|" + o.Async; });
    Case("call.runoptions.token", () => { var o = new RunOptions(CancellationToken.None, 2); return o.Nargout + "|" + o.Async; });
    Case("call.runoptions.writers", () => { var o = new RunOptions(TextWriter.Null, TextWriter.Null, 4, false); return o.Nargout + "|" + o.Async + "|" + (o.Output == TextWriter.Null); });
    Case("call.runoptions.set", () => { var o = new RunOptions(); o.Nargout = 7; o.Async = true; return o.Nargout + "|" + o.Async; });
    Case("call.runoptions.second", () => matlab.k9echo(1.0, new RunOptions(nargout: 1)));
    Case("call.noout.default", () => matlab.k9noout(1.0));
    Case("call.noout.nargout0", () => matlab.k9noout(new RunOptions(nargout: 0), 1.0));
    Case("call.none", () => matlab.k9t_none());
    Case("call.none.nargout0", () => matlab.k9t_none(new RunOptions(nargout: 0)));
    Case("call.toofew", () => matlab.k9echo());
    Case("call.toomany", () => matlab.k9echo(1.0, 2.0));
    Case("call.nv.none", () => (string)matlab.k9nv(2.0));
    Case("call.nv.named", () => (string)matlab.k9nv(2.0, Scale: 3.0));
    Case("call.nv.named2", () => (string)matlab.k9nv(2.0, Scale: 3.0, Label: "x"));
    Case("call.nv.named.case", () => (string)matlab.k9nv(2.0, scale: 3.0));
    Case("call.nv.named.bad", () => (string)matlab.k9nv(2.0, Bad: 1.0));
    Case("call.nv.pairs", () => (string)matlab.k9nv(2.0, "Scale", 4.0));
    Case("call.nv.named.first", () => (string)matlab.k9nv(Scale: 3.0));
    Case("call.nv.named.positional", () => (string)matlab.k9nv(x: 2.0));
    Case("call.nv.runoptions", () => (string)matlab.k9nv(new RunOptions(nargout: 1), 2.0, Label: "y"));
    Case("call.pkg", () => matlab.k9pkg.k9inner(1.0));
    Case("call.pkg.double", () => (double)matlab.k9pkg.k9inner(1.0));
    Case("call.pkg.object", () => TypeLabel(((object)matlab.k9pkg).GetType()));
    Case("call.pkg.tostring", () => ((object)matlab.k9pkg).ToString());
    Case("call.pkg.cast", () => (double)matlab.k9pkg);
    Case("call.pkg.pass", () => matlab.k9describe(matlab.k9pkg));
    Case("call.pkg.missing", () => matlab.k9pkg.nosuch(1.0));
    Case("call.pkg.deep", () => matlab.k9pkg.deeper.k9inner(1.0));
    Case("call.missing", () => matlab.k9nosuch(1.0));
    Case("call.builtin", () => matlab.disp("x"));
    Case("call.builtin.sqrt", () => matlab.sqrt(4.0));
    Case("call.internal.getProperty", () => matlab.matlab.@internal.engine.getProperty(matlab.k9point(3.0, 4.0), "X"));
    Case("call.internal.setProperty", () => (double)matlab.matlab.@internal.engine.setProperty(matlab.k9point(3.0, 4.0), "X", 9.0).X);
    Case("call.eq", () => matlab.eq(matlab.k9point(1.0, 1.0), matlab.k9point(1.0, 1.0)));
    Case("call.keyHash", () => TypeLabel(((object)matlab.keyHash(matlab.k9point(1.0, 1.0))).GetType()));
    Case("call.member.function", () => TypeLabel(((object)matlab.k9describe).GetType()));
    Case("call.subfunction", () => matlab.k9value(1.0));
    Case("call.disposed.prop", () => matlab.Disposed);
}

// --- Errors ----------------------------------------------------------------------------------
foreach (string kind in new[] { "plain", "id", "nested", "cause", "builtin", "undefined", "none" })
{
    Case("error." + kind, () => matlab.k9err(kind));
    var so = new StringWriter();
    var se = new StringWriter();
    Case("error." + kind + ".writers", () => matlab.k9err(new RunOptions(so, se, 1, false), kind));
    Line("error." + kind + ".stdout", "<<" + Flat(so.ToString()) + ">>");
    Line("error." + kind + ".stderr", "<<" + Flat(se.ToString()) + ">>");
}
{
    Exception caught = null;
    try { matlab.k9err("id"); } catch (Exception e) { caught = e; }
    Case("error.data", () => caught == null ? "none" : string.Join(",", caught.Data.Keys.Cast<object>().Select(k => k.ToString())));
    Case("error.source", () => caught?.Source);
    Case("error.hresult", () => caught?.HResult);
    Case("error.validation", () => matlab.k9t_real("x", new double[] { 1 }, new double[,] { { 1 } }, new double[2, 3, 4]));
    Case("error.validation.size", () => matlab.k9t_real(new double[] { 1, 2 }, new double[] { 1 }, new double[,] { { 1 } }, new double[2, 3, 4]));
    Case("error.after", () => (double)matlab.k9echo(5.0));
}

// --- Output ----------------------------------------------------------------------------------
{
    var so = new StringWriter();
    var se = new StringWriter();
    Case("print.writers", () => matlab.k9print(new RunOptions(so, se, 1, false), 2.0));
    Line("print.writers.stdout", "<<" + Flat(so.ToString()) + ">>");
    Line("print.writers.stderr", "<<" + Flat(se.ToString()) + ">>");
    Line("print.default", "begin");
    Case("print.default.result", () => matlab.k9print(1.0));
    // Not "print.null.writers": RunOptions(null, null, ...) ends R2025b's process with an access violation.
    Case("print.noout", () => matlab.k9noout(new RunOptions(so, se, 0, false), "z"));
    Line("print.noout.stdout", "<<" + Flat(so.ToString()) + ">>");
}

// --- Asynchronous calls and cancellation ---------------------------------------------------
{
    object t = null;
    Case("async.call", () => t = matlab.k9slow(new RunOptions(nargout: 1, async: true), 0.2));
    Case("async.type", () => t == null ? "null" : TypeLabel(t.GetType()));
    Case("async.result", () => t is Task task ? ((dynamic)task).Result : "none");
    Case("async.result.double", () => t is Task task ? (double)((dynamic)task).Result : -1);
    Case("async.await", () => Task.Run(async () => (double)await (dynamic)matlab.k9echo(new RunOptions(nargout: 1, async: true), 7.0)).Result);
    Case("async.nargout0", () => { object z = matlab.k9echo(new RunOptions(nargout: 0, async: true), 1.0); return z is Task tz ? (object)(TypeLabel(z.GetType()) + " " + Show(((dynamic)tz).Result)) : z; });
    Case("async.nargout2", () => { object z = matlab.k9multi(new RunOptions(nargout: 2, async: true), 1.0); return z is Task tz ? Show(((dynamic)tz).Result) : z; });
    Case("async.error", () => { object z = matlab.k9err(new RunOptions(nargout: 1, async: true), "id"); return z is Task tz ? ((dynamic)tz).Result : z; });
    Case("async.error.await", () => Task.Run(async () => (object)await (dynamic)matlab.k9err(new RunOptions(nargout: 1, async: true), "id")).GetAwaiter().GetResult());
    var cts = new CancellationTokenSource();
    object slow = null;
    Case("cancel.start", () => { slow = matlab.k9slow(new RunOptions(cts.Token, 1), 5.0); return TypeLabel(slow.GetType()); });
    Thread.Sleep(500);
    cts.Cancel();
    Case("cancel.result", () => slow is Task st ? ((dynamic)st).Result : "none");
    Case("cancel.status", () => slow is Task st2 ? st2.Status.ToString() : "none");
    Case("cancel.after", () => (double)matlab.k9echo(3.0));
    var gone = new CancellationTokenSource();
    gone.Cancel();
    Case("cancel.before", () => { object z = matlab.k9echo(new RunOptions(gone.Token, 1), 1.0); return z is Task tz ? ((dynamic)tz).Result : z; });
    Case("cancel.sync.token", () => matlab.k9echo(new RunOptions(gone.Token, 1) { Async = false }, 1.0));
    Case("async.start", () => { Task<MATLABRuntime> st = MATLABRuntime.StartMATLABAsync(ctfV); return TypeLabel(st.GetType()); });
}

// --- A second component ------------------------------------------------------------------
dynamic matlabV = MATLABRuntime.StartMATLABAsync(ctfV).Result;
{
    Case("second.type", () => TypeLabel(((object)matlabV).GetType()));
    Case("second.nargs0", () => (string)matlabV.k9nargs());
    Case("second.nargs2", () => (string)matlabV.k9nargs(1.0, "a"));
    Case("second.nargs.named", () => (string)matlabV.k9nargs(1.0, Name: 2.0));
    Case("second.nout3", () => { (double a, double b, double c3) = ((double, double, double))matlabV.k9nout(new RunOptions(nargout: 3), 2.0); return a + "|" + b + "|" + c3; });
    Case("second.nout0", () => matlabV.k9nout(new RunOptions(nargout: 0), 2.0));
    Case("second.nout1", () => matlabV.k9nout(2.0));
    Case("second.other", () => matlabV.k9make("double"));
    Case("first.other", () => matlab.k9nargs());
    Case("second.object.from.first", () => (string)matlabV.k9describe(matlab.k9point(1.0, 2.0)));
    Case("second.array.from.first", () => (string)matlabV.k9describe(matlab.k9make("row")));
    Case("second.same", () => { dynamic again = MATLABRuntime.StartMATLAB(ctfV); string r = (string)again.k9nargs(5.0); ((IDisposable)again).Dispose(); return r; });
    Do("second.dispose", () => ((IDisposable)matlabV).Dispose());
    Case("second.disposed", () => matlabV.Disposed);
    Case("second.after.dispose", () => matlabV.k9nargs());
    Do("second.dispose.twice", () => ((IDisposable)matlabV).Dispose());
    Case("first.after.second", () => (double)matlab.k9echo(4.0));
}

// --- The strongly typed interface ------------------------------------------------------------
TypedCases.Run((object)matlab, Case, Do, Line);

// --- Ending ----------------------------------------------------------------------------------
// Failed starts last: R2025b's finalizer for the runtime object a failed start leaves crashes the
// process when the garbage collector reaches it ("Failed to terminate MATLAB runtime").
Case("start.missing", () => MATLABRuntime.StartMATLAB(Path.Combine(Path.GetDirectoryName(ctf), "k9nosuch.ctf")));
Case("start.notctf", () => MATLABRuntime.StartMATLAB(Path.Combine(Path.GetDirectoryName(ctf), "readme.txt")));
Case("start.null", () => MATLABRuntime.StartMATLAB(null));
Do("figures", () => matlab.WaitForFiguresToClose());
Case("macrunloop", () => { MATLABRuntime.SetupMacRunLoopAndRun(o => 7, null); return "returned"; });
Do("dispose", () => ((IDisposable)matlab).Dispose());
Case("disposed", () => matlab.Disposed);
Case("after.dispose", () => matlab.k9echo(1.0));
Do("terminate", () => MATLABRuntime.TerminateApplication());
Case("after.terminate", () => MATLABRuntime.StartMATLAB(ctf));
Do("terminate.twice", () => MATLABRuntime.TerminateApplication());
Console.Out.Flush();

/// <summary>Prefixes each line written to it, so standard error's lines stand out in the transcript.</summary>
sealed class MarkedWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly string _mark;
    private bool _lineStart = true;

    public MarkedWriter(TextWriter inner, string mark)
    {
        _inner = inner;
        _mark = mark;
    }

    public override System.Text.Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        if (value == '\r') return;
        if (_lineStart)
        {
            _inner.Write(_mark);
            _lineStart = false;
        }

        if (value == '\n')
        {
            _inner.Write('\n');
            _lineStart = true;
            return;
        }

        _inner.Write(value);
    }

    public override void Flush() => _inner.Flush();
}
