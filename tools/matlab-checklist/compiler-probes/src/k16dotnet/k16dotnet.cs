// A client of a Production Server's protobuf interface through MathWorks' own .NET client library
// (compiler and SDK plan, stage K16): MathWorks.MATLAB.ProductionServer.Client.dll from R2025b's
// toolbox\compiler_sdk\mps_clients\dotnet, a .NET Framework 4.5 assembly, so this program is compiled
// by the Framework's csc.exe (C# 5) in record-k16.ps1 and run-k16.ps1 and run against muserve and
// jgraph-mps alike. One line per case: label<TAB>text, with the volatile parts replaced by tokens.
//
//     k16dotnet.exe <base url> <component>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using MathWorks.MATLAB.ProductionServer.Client;
using MathWorks.MATLAB.ProductionServer.Client.REST;

public class Probe
{
    public string name;
    public double[] value;

    public override string ToString()
    {
        return "Probe name=" + K16.Show(name) + " value=" + K16.Show(value);
    }
}

public class Student
{
    public string name;
    public string grade;
    public double score;

    public override string ToString()
    {
        return "Student name=" + K16.Show(name) + " grade=" + K16.Show(grade) + " score=" + K16.Show(score);
    }
}

public class Rules : MWDefaultMarshalingRules
{
    public override IList<Type> StructTypes
    {
        get { return new List<Type> { typeof(Probe), typeof(Student) }; }
    }
}

public class KeepRows : Rules
{
    public override bool PreserveOutputVectorDimensions
    {
        get { return true; }
    }
}

public class NullAsNaN : Rules
{
    public override double? InputNullConversion
    {
        get { return double.NaN; }
    }

    public override double? OutputNaNConversion
    {
        get { return 0; }
    }

    public override bool ConvertOutputToDateTime
    {
        get { return true; }
    }
}

public static class K16
{
    private static string url;
    private static string component;

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("k16dotnet.exe <base url> <component>");
            return 2;
        }

        url = args[0].TrimEnd('/');
        component = args[1];
        Console.OutputEncoding = new UTF8Encoding(false);
        using (var client = new MWHttpClient())
        {
            MWInvokable proxy = client.CreateComponentProxy(new Uri(url + "/" + component), new Rules());
            Proxy(proxy);
            Rest();
            MWInvokable keep = client.CreateComponentProxy(new Uri(url + "/" + component), new KeepRows());
            Typed<double[,]>("rules.keep.row", keep, "k14make", "row");
            Typed<double[,]>("rules.keep.column", keep, "k14make", "column");
            Typed<double[]>("rules.keep.row.flat", keep, "k14make", "row");
            Typed<object>("rules.keep.object.row", keep, "k14make", "row");
            MWInvokable nulls = client.CreateComponentProxy(new Uri(url + "/" + component), new NullAsNaN());
            Typed<string>("rules.null.in", nulls, "k14describe", new object[] { null });
            Typed<double>("rules.nan.out", nulls, "k14make", "nan");
            Typed<object>("rules.datetime.out", nulls, "k14make", "datetime");
            Typed<string>("rules.datetime.in", nulls, "k14describe", new DateTime(2026, 10, 24, 12, 30, 0));
        }

        return 0;
    }

    private static void Proxy(MWInvokable p)
    {
        Typed<double>("basic", p, "k14add", 1.0, 2.0);
        Typed<double>("ints", p, "k14add", 1, 2);
        Typed<int>("int.out", p, "k14add", 1, 2);
        Typed<object>("object.out", p, "k14add", 1.0, 2.0);
        Typed<string>("wrong.type.out", p, "k14add", 1.0, 2.0);
        // Values in, as the client marshals them, described by the function.
        var inputs = new List<KeyValuePair<string, object>>
        {
            new KeyValuePair<string, object>("double", 5.0), new KeyValuePair<string, object>("float", 5.5f), new KeyValuePair<string, object>("int", 5),
            new KeyValuePair<string, object>("long", 5000000000L), new KeyValuePair<string, object>("short", (short)-5), new KeyValuePair<string, object>("ushort", (ushort)65535),
            new KeyValuePair<string, object>("byte", (byte)200), new KeyValuePair<string, object>("sbyte", (sbyte)-5), new KeyValuePair<string, object>("uint", 4294967295u),
            new KeyValuePair<string, object>("ulong", 18446744073709551615UL), new KeyValuePair<string, object>("bool", true), new KeyValuePair<string, object>("string", "abc"),
            new KeyValuePair<string, object>("empty.string", ""), new KeyValuePair<string, object>("unicode", "λ€A"), new KeyValuePair<string, object>("char", 'x'),
            new KeyValuePair<string, object>("doubles", new double[] { 1, 2, 3 }), new KeyValuePair<string, object>("matrix", new double[,] { { 1, 2 }, { 3, 4 } }),
            new KeyValuePair<string, object>("nd", new double[,,] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } }), new KeyValuePair<string, object>("empty.doubles", new double[0]),
            new KeyValuePair<string, object>("ints", new int[] { -1, 2 }), new KeyValuePair<string, object>("longs", new long[] { -9007199254740993L }),
            new KeyValuePair<string, object>("bytes", new byte[] { 1, 255 }), new KeyValuePair<string, object>("sbytes", new sbyte[] { -1, 2 }), new KeyValuePair<string, object>("floats", new float[] { 1.5f }),
            new KeyValuePair<string, object>("shorts", new short[] { -5, 7 }), new KeyValuePair<string, object>("bools", new bool[] { true, false }),
            new KeyValuePair<string, object>("strings", new string[] { "a", "bc" }), new KeyValuePair<string, object>("chars", new char[] { 'a', 'b' }),
            new KeyValuePair<string, object>("objects", new object[] { 1.0, "a", true }), new KeyValuePair<string, object>("objects.nested", new object[] { new object[] { 1.0 }, new object[0] }),
            new KeyValuePair<string, object>("objects.homogeneous", new object[] { 1.0, 2.0 }), new KeyValuePair<string, object>("empty.objects", new object[0]),
            new KeyValuePair<string, object>("probe", new Probe { name = "probe", value = new double[] { 1, 2, 3 } }),
            new KeyValuePair<string, object>("students", new Student[] { new Student { name = "A", grade = "B+", score = 90 }, new Student { name = "C", grade = "A", score = 85 } }),
            new KeyValuePair<string, object>("null", null), new KeyValuePair<string, object>("datetime", new DateTime(2026, 10, 24, 12, 30, 0)),
            new KeyValuePair<string, object>("decimal", 1.5m), new KeyValuePair<string, object>("dictionary", new Dictionary<string, object> { { "a", 1.0 } }),
            new KeyValuePair<string, object>("nullable", (int?)5), new KeyValuePair<string, object>("list", new List<double> { 1, 2 }),
            new KeyValuePair<string, object>("jagged", new double[][] { new double[] { 1, 2 }, new double[] { 3, 4 } }),
            new KeyValuePair<string, object>("nan", double.NaN), new KeyValuePair<string, object>("inf", double.NegativeInfinity), new KeyValuePair<string, object>("negzero", -0.0),
        };
        foreach (var input in inputs)
        {
            Typed<string>("in." + input.Key, p, "k14describe", new object[] { input.Value });
        }

        // Values out, read as object (the client's own choice of type) and as the type a client would ask for.
        string[] kinds =
        {
            "double", "negzero", "row", "column", "matrix", "nd", "empty", "empty03", "single", "int8", "int8s", "uint8", "int32", "int64", "uint64", "complex",
            "complexrow", "complexsingle", "logical", "logicals", "char", "charmatrix", "emptychar", "unicode", "quote", "controls", "string", "strings",
            "stringmissing", "cell", "cellcol", "nestedcell", "emptycell", "struct", "structnested", "structarray", "emptystruct", "nofields", "sparse",
            "sparselogical", "inf", "nan", "pi", "big", "enum", "enums", "handle", "datetime", "datetimeutc", "table", "map", "categorical", "duration", "dict",
            "large", "nosuchkind",
        };
        foreach (string kind in kinds)
        {
            Typed<object>("out." + kind, p, "k14make", kind);
        }

        Typed<double[,]>("typed.matrix", p, "k14make", "matrix");
        Typed<double[]>("typed.row", p, "k14make", "row");
        Typed<double[]>("typed.column", p, "k14make", "column");
        Typed<double[,,]>("typed.nd", p, "k14make", "nd");
        Typed<double[]>("typed.empty", p, "k14make", "empty");
        Typed<string>("typed.char", p, "k14make", "char");
        Typed<string[]>("typed.charmatrix", p, "k14make", "charmatrix");
        Typed<string>("typed.string", p, "k14make", "string");
        Typed<string[]>("typed.strings", p, "k14make", "strings");
        Typed<bool>("typed.logical", p, "k14make", "logical");
        Typed<bool[]>("typed.logicals", p, "k14make", "logicals");
        Typed<int>("typed.int32", p, "k14make", "int32");
        Typed<long>("typed.int64", p, "k14make", "int64");
        Typed<ulong>("typed.uint64", p, "k14make", "uint64");
        Typed<sbyte[]>("typed.int8s", p, "k14make", "int8s");
        Typed<byte>("typed.uint8", p, "k14make", "uint8");
        Typed<float>("typed.single", p, "k14make", "single");
        Typed<double>("typed.int8.as.double", p, "k14make", "int8");
        Typed<int>("typed.double.as.int", p, "k14make", "double");
        Typed<object[]>("typed.cell", p, "k14make", "cell");
        Typed<object[]>("typed.cellcol", p, "k14make", "cellcol");
        Typed<object[]>("typed.nestedcell", p, "k14make", "nestedcell");
        Typed<Probe>("typed.struct", p, "k14make", "struct");
        Typed<Probe[]>("typed.structarray", p, "k14make", "structarray");
        Typed<Student>("typed.struct.wrong.class", p, "k14make", "struct");
        Typed<double[,]>("typed.sparse", p, "k14make", "sparse");
        Typed<string>("typed.enum", p, "k14make", "enum");
        Typed<double>("typed.datetime", p, "k14make", "datetime");
        Typed<DateTime>("typed.datetime.as.datetime", p, "k14make", "datetime");
        // Errors.
        foreach (string kind in new[] { "plain", "id", "nested", "cause", "builtin", "undefined", "multiline", "quote", "none" })
        {
            Typed<double>("err." + kind, p, "k14err", kind);
        }

        Typed<double>("err.too.many.in", p, "k14add", 1.0, 2.0, 3.0);
        Typed<double>("err.too.few.in", p, "k14add", 1.0);
        // Output counts.
        Many("multi.3", p, "k14multi", new List<Type> { typeof(double), typeof(double), typeof(string) }, 2.0);
        Many("multi.1", p, "k14multi", new List<Type> { typeof(double) }, 2.0);
        Many("multi.0", p, "k14multi", new List<Type>(), 2.0);
        Many("multi.4", p, "k14multi", new List<Type> { typeof(double), typeof(double), typeof(string), typeof(double) }, 2.0);
        Many("multi.objects", p, "k14multi", new List<Type> { typeof(object), typeof(object), typeof(object) }, 2.0);
        Many("echo.3.3", p, "k14echo", new List<Type> { typeof(double), typeof(string), typeof(bool) }, 1.0, "a", true);
        Many("echo.0.1", p, "k14echo", new List<Type> { typeof(double) });
        Many("nargout.2.add", p, "k14add", new List<Type> { typeof(double), typeof(double) }, 1.0, 2.0);
        Void("void.noout", p, "k14noout", 5.0);
        Void("void.add", p, "k14add", 1.0, 2.0);
        Void("void.err", p, "k14err", "id");
        Typed<double>("noout.1", p, "k14noout", 5.0);
        Typed<double>("print", p, "k14print", 2.0);
        for (int nIn = 0; nIn <= 2; nIn++)
        {
            for (int nOut = 0; nOut <= 3; nOut++)
            {
                var types = new List<Type>();
                for (int i = 0; i < nOut; i++)
                {
                    types.Add(typeof(double));
                }

                var args = new object[nIn];
                for (int i = 0; i < nIn; i++)
                {
                    args[i] = (double)(i + 1);
                }

                Many("nargs." + nIn + "." + nOut, p, "k14nargs", types, args);
            }
        }

        Typed<object>("state.0", p, "k14state");
        Typed<object>("state.1", p, "k14state");
        Typed<string>("struct.in.out", p, "k14struct", new Probe { name = "n", value = new double[] { 1 } });
        Typed<double>("missing.function", p, "nosuch", 1.0);
        var large = new double[100000];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = i;
        }

        Typed<double[]>("large.in", p, "k14nargs", new object[] { large });
    }

    /// <summary>The REST classes: the request's bytes written by MATLABParams, the response read by MATLABResult, and the bytes the server wrote.</summary>
    private static void Rest()
    {
        RestCase<double>("rest.basic", "k14add", 1, new List<Type> { typeof(double) }, 1.0, 2.0);
        RestCase<double[,]>("rest.matrix", "k14make", 1, new List<Type> { typeof(double[,]) }, "matrix");
        RestCase<string>("rest.char", "k14make", 1, new List<Type> { typeof(string) }, "char");
        RestCase<object>("rest.cell", "k14make", 1, new List<Type> { typeof(object) }, "cell");
        RestCase<Probe>("rest.struct", "k14make", 1, new List<Type> { typeof(Probe) }, "struct");
        RestCase<string>("rest.describe", "k14describe", 1, new List<Type> { typeof(string) }, new object[] { new object[] { 1.0, "a", new Probe { name = "p", value = new double[] { 2 } } } });
        RestCase<double>("rest.err", "k14err", 1, new List<Type> { typeof(double) }, "id");
        RestCase<double>("rest.nargout.0", "k14add", 0, new List<Type>(), 1.0, 2.0);
        RestCase<double>("rest.nargout.2", "k14add", 2, new List<Type> { typeof(double), typeof(double) }, 1.0, 2.0);
        RestCase<double>("rest.multi", "k14multi", 3, new List<Type> { typeof(double), typeof(double), typeof(string) }, 2.0);
        RestCase<double>("rest.missing.function", "nosuch", 1, new List<Type> { typeof(double) }, 1.0);
    }

    private static void RestCase<T>(string label, string function, int nargout, IList<Type> types, params object[] args)
    {
        try
        {
            MATLABParams mlParams = MATLABParams.Create(nargout, types, new Rules(), args);
            var request = (HttpWebRequest)WebRequest.Create(url + "/" + component + "/" + function);
            request.Method = "POST";
            request.ContentType = "application/x-google-protobuf";
            request.ServicePoint.Expect100Continue = false;
            mlParams.WriteTo(request.GetRequestStream());
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                response = (HttpWebResponse)ex.Response;
                if (response == null)
                {
                    throw;
                }
            }

            byte[] body;
            using (var buffer = new MemoryStream())
            {
                response.GetResponseStream().CopyTo(buffer);
                body = buffer.ToArray();
            }

            string shown;
            try
            {
                MATLABResult result = MATLABResult.Create(mlParams, new MemoryStream(body));
                shown = Show(result.Result<T>());
            }
            catch (Exception ex)
            {
                shown = Err(ex);
            }

            bool hasFrames = body.Length > 0 && Encoding.UTF8.GetString(body).Contains(".m");
            Out(label, (int)response.StatusCode + " " + response.StatusDescription + "\tsent=" + Hex(mlParams.RequestBody) + "\t" + (hasFrames ? "<error bytes>" : "hex=" + Hex(body)) + "\t" + shown);
            response.Close();
        }
        catch (Exception ex)
        {
            Out(label, Err(ex));
        }
    }

    private static string Hex(byte[] bytes)
    {
        if (bytes.Length > 1500)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return "<" + bytes.Length + " bytes md5 " + BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() + ">";
            }
        }

        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static void Typed<T>(string label, MWInvokable p, string function, params object[] args)
    {
        try
        {
            Out(label, Show(p.Invoke<T>(function, args)));
        }
        catch (Exception ex)
        {
            Out(label, Err(ex));
        }
    }

    private static void Many(string label, MWInvokable p, string function, IList<Type> types, params object[] args)
    {
        try
        {
            object[] outputs = p.Invoke(function, types, args);
            var parts = new List<string>();
            foreach (object output in outputs)
            {
                parts.Add(Show(output));
            }

            Out(label, outputs.Length + " outputs [" + string.Join("; ", parts) + "]");
        }
        catch (Exception ex)
        {
            Out(label, Err(ex));
        }
    }

    private static void Void(string label, MWInvokable p, string function, params object[] args)
    {
        try
        {
            p.Invoke(function, args);
            Out(label, "void");
        }
        catch (Exception ex)
        {
            Out(label, Err(ex));
        }
    }

    public static string Show(object v)
    {
        if (v == null)
        {
            return "null";
        }

        if (v is string)
        {
            return "string '" + (string)v + "'";
        }

        if (v is bool || v is double || v is float || v is int || v is long || v is short || v is byte || v is sbyte || v is ushort || v is uint || v is ulong || v is char || v is decimal)
        {
            return v.GetType().Name + " " + Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        if (v is DateTime)
        {
            return "DateTime " + ((DateTime)v).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + ((DateTime)v).Kind;
        }

        if (v is Probe || v is Student)
        {
            return v.ToString();
        }

        var array = v as Array;
        if (array != null)
        {
            var dims = new List<string>();
            for (int d = 0; d < array.Rank; d++)
            {
                dims.Add(array.GetLength(d).ToString(CultureInfo.InvariantCulture));
            }

            var items = new List<string>();
            foreach (object item in array)
            {
                items.Add(Show(item));
            }

            return v.GetType().Name + " [" + string.Join(",", dims) + "] {" + string.Join("; ", items) + "}";
        }

        return v.GetType().FullName + " " + v;
    }

    private static string Err(Exception ex)
    {
        var matlab = ex as MATLABException;
        if (matlab != null)
        {
            var frames = new List<string>();
            foreach (MATLABStackFrame frame in matlab.MATLABStackTrace)
            {
                frames.Add(frame.Name + "@" + Path.GetFileName(frame.File) + ":" + frame.Line);
            }

            return "MATLABException id=" + matlab.MATLABIdentifier + " message=" + matlab.Message + " stack=[" + string.Join(";", frames) + "]";
        }

        var web = ex as WebException;
        if (web != null)
        {
            var response = web.Response as HttpWebResponse;
            if (response != null)
            {
                return "WebException " + (int)response.StatusCode + " " + response.StatusDescription;
            }

            return "WebException " + web.Status;
        }

        var inner = ex.InnerException;
        return "EXC " + ex.GetType().Name + " " + ex.Message + (inner != null ? " <- " + inner.GetType().Name + " " + inner.Message : "");
    }

    private static void Out(string label, string text)
    {
        var line = new StringBuilder();
        foreach (char c in text)
        {
            if (c == '\n')
            {
                line.Append("\\n");
            }
            else if (c == '\r')
            {
                line.Append("\\r");
            }
            else if (c == '\t' || (c >= 32 && c < 127))
            {
                line.Append(c);
            }
            else
            {
                line.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
        }

        string shown = System.Text.RegularExpressions.Regex.Replace(line.ToString(), @"\(request id=[0-9:]+\)", "(request id=<N>)");
        Console.Out.Write(label + "\t" + shown + "\n");
        Console.Out.Flush();
    }
}
