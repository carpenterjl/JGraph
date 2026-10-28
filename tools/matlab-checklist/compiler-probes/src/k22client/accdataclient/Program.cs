// The acceptance set's client of the MATLAB Data API for .NET (compiler and SDK plan, stage K22):
// the AccData archive and its strongly typed interface built from src\k22 by R2025b or by JGraph,
// called dynamically and through the generated MATLABFunctions. One label<TAB>result line per
// question. The same source is built against JGraph's assembly with only its using lines changed
// (accept-k22.ps1), so the two transcripts compare line for line. Argument: the AccData archive.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using MathWorks.MATLAB.Runtime;
using MathWorks.MATLAB.Types;
using MathWorks.MATLAB.Exceptions;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
string ctf = args[0];

string Flat(string s) => s == null ? "null" : s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

string Show(object o)
{
    switch (o)
    {
        case null:
            return "null";
        case string s:
            return "\"" + Flat(s) + "\"";
        case double d:
            return d.ToString("R", CultureInfo.InvariantCulture);
        case double[] v:
            return "double[" + v.Length + "] {" + string.Join(", ", v.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + "}";
        case double[,] m:
        {
            var parts = new List<string>();
            for (int r = 0; r < m.GetLength(0); r++)
                for (int c = 0; c < m.GetLength(1); c++)
                    parts.Add(m[r, c].ToString("R", CultureInfo.InvariantCulture));
            return $"double[{m.GetLength(0)},{m.GetLength(1)}] {{{string.Join(", ", parts)}}}";
        }
        case MATLABStruct st:
        {
            var names = new List<string>();
            foreach (string n in st.GetFieldNames()) names.Add(n);
            names.Sort(StringComparer.Ordinal);
            return "MATLABStruct{" + string.Join(", ", names.Select(n => n + "=" + Show(st.GetField(n)))) + "}";
        }
        case bool b:
            return b ? "true" : "false";
        case MATLABArray a:
            return "MATLABArray " + Show((double)(dynamic)a);
        default:
            return o.GetType().Name + " " + Flat(o.ToString());
    }
}

string Threw(Exception e)
{
    var parts = new List<string>();
    for (Exception x = e; x != null; x = x.InnerException) parts.Add($"{x.GetType().Name}: {Flat(x.Message.Trim())}");
    return "THREW " + string.Join(" <- ", parts);
}

void Line(string label, string text)
{
    Console.WriteLine($"{label}\t{text}");
    Console.Out.Flush();
}

void Case(string label, Func<object> f)
{
    string text;
    try { text = Show(f()); }
    catch (Exception e) { text = Threw(e); }
    Line(label, text);
}

dynamic matlab = null;
Case("start", () => { matlab = MATLABRuntime.StartMATLAB(ctf); return "ok"; });
if (matlab == null) return;
// A function without outputs is called with nargout 0 said: the dynamic call's default is one
// output, which the function refuses (MATLAB:TooManyOutputs), in R2025b as here (stage K9).
Case("quiet", () => { matlab.acc_quiet(new RunOptions(nargout: 0)); return "ok"; });
Case("quiet.default", () => { matlab.acc_quiet(); return "ok"; });
Case("add", () => (double)matlab.acc_add(2.0, 3.0));
Case("add.int", () => (double)matlab.acc_add(2, 3));
Case("stats", () => (MATLABStruct)matlab.acc_stats(new double[] { 3, 1, 4, 1, 5 }));
Case("stats.fields", () =>
{
    MATLABStruct s = (MATLABStruct)matlab.acc_stats(new double[] { 3, 1, 4, 1, 5 });
    return $"n={(double)(dynamic)s.GetField("n")} mean={(double)(dynamic)s.GetField("mean")} max={(double)(dynamic)s.GetField("max")}";
});
Case("greet", () => (string)matlab.acc_greet("Ada"));
Case("scale", () => (double[,])matlab.acc_scale(new double[,] { { 1, 2 }, { 3, 4 } }, 10.0));
Case("scale.row", () => (double[])matlab.acc_scale(new double[] { 1, 2, 3 }, 2.0));
Case("count.1", () => (double)matlab.acc_count());
Case("count.2", () => (double)matlab.acc_count());
Case("fail", () => { matlab.acc_fail(new RunOptions(nargout: 0), "from .NET"); return "ok"; });
Case("add.wrongclass", () => (double)matlab.acc_add("x", 3.0));
Case("count.3", () => (double)matlab.acc_count());

// The strongly typed interface generated with the archive: a double argument without mustBeReal is
// a Complex, a char one a char[], as R2025b's generator writes them.
MATLABProvider m = (MATLABProvider)matlab;
Case("typed.add", () => { AccData.MATLABFunctions.acc_add(m, new Complex(2, 0), new Complex(3, 0), out dynamic c); return (double)c; });
Case("typed.stats", () => { AccData.MATLABFunctions.acc_stats(m, new Complex[] { 3, 1, 4, 1, 5 }, out dynamic s); return (MATLABStruct)s; });
Case("typed.greet", () => { AccData.MATLABFunctions.acc_greet(m, "Ada".ToCharArray(), out dynamic g); return (string)g; });
Case("typed.scale", () => { AccData.MATLABFunctions.acc_scale(m, new double[,] { { 1, 2 }, { 3, 4 } }, new Complex(10, 0), out dynamic y); return (double[,])y; });
Case("typed.count", () => { AccData.MATLABFunctions.acc_count(m, out dynamic n); return (double)n; });
Case("typed.quiet", () => { AccData.MATLABFunctions.acc_quiet(m); return "ok"; });
Case("typed.fail", () => { AccData.MATLABFunctions.acc_fail(m, "typed".ToCharArray()); return "ok"; });
Case("dispose", () => { ((IDisposable)matlab).Dispose(); return "ok"; });
Case("terminate", () => { MATLABRuntime.TerminateApplication(); return "ok"; });
