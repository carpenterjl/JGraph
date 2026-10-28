// The acceptance set's client of the MWArray API (compiler and SDK plan, stage K22): the AccNet
// assembly built from src\k22 by R2025b or by JGraph, called as a client of MathWorks' MWArray API
// calls it. One label<TAB>result line per question. The same source is built against JGraph's
// assembly with only its using lines changed (accept-k22.ps1), so the two transcripts compare line
// for line.
using System;
using System.Collections.Generic;
using System.Globalization;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

static string Flat(string s) => s == null ? "null" : s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

static string Show(object o)
{
    switch (o)
    {
        case null:
            return "null";
        case MWNumericArray n:
            return $"MWNumericArray {string.Join("x", n.Dimensions)} {n.NumericType} <<{Flat(n.ToString().Trim())}>>";
        case MWCharArray c:
            return $"MWCharArray {string.Join("x", c.Dimensions)} <<{Flat(c.ToString())}>>";
        case MWStructArray s:
        {
            var parts = new List<string>();
            foreach (string name in s.FieldNames) parts.Add(name + "=" + Show(s[name]));
            return $"MWStructArray {string.Join("x", s.Dimensions)} {{{string.Join(", ", parts)}}}";
        }
        case MWArray a:
            return $"{a.GetType().Name} {string.Join("x", a.Dimensions)} <<{Flat(a.ToString())}>>";
        case MWArray[] list:
        {
            var parts = new List<string>();
            foreach (MWArray x in list) parts.Add(Show(x));
            return $"MWArray[{list.Length}] {{{string.Join(", ", parts)}}}";
        }
        case double d:
            return d.ToString("R", CultureInfo.InvariantCulture);
        case string s:
            return "\"" + Flat(s) + "\"";
        default:
            return o.ToString();
    }
}

static string Threw(Exception e)
{
    var parts = new List<string>();
    for (Exception x = e; x != null; x = x.InnerException) parts.Add($"{x.GetType().Name}: {Flat(x.Message.Trim())}");
    return "THREW " + string.Join(" <- ", parts);
}

static void Line(string label, string text)
{
    Console.WriteLine($"{label}\t{text}");
    Console.Out.Flush();
}

static void Case(string label, Func<object> f)
{
    string text;
    try { text = Show(f()); }
    catch (Exception e) { text = Threw(e); }
    Line(label, text);
}

AccNet.AccClass acc = null;
Case("construct", () => { acc = new AccNet.AccClass(); return "ok"; });
if (acc == null) return;
Case("quiet", () => { acc.acc_quiet(); return "ok"; });
Case("add", () => acc.acc_add(new MWNumericArray(2.0), new MWNumericArray(3.0)));
Case("add.outs", () => acc.acc_add(1, new MWNumericArray(2.0), new MWNumericArray(3.0)));
Case("add.double", () => (double)(MWNumericArray)acc.acc_add(new MWNumericArray(2.0), new MWNumericArray(3.0)));
Case("add.int", () => acc.acc_add(new MWNumericArray(2), new MWNumericArray(3)));
Case("stats", () => acc.acc_stats(new MWNumericArray(new double[] { 3, 1, 4, 1, 5 })));
Case("stats.mean", () => { var s = (MWStructArray)acc.acc_stats(new MWNumericArray(new double[] { 3, 1, 4, 1, 5 })); return (double)(MWNumericArray)s["mean"]; });
Case("greet", () => acc.acc_greet(new MWCharArray("Ada")));
Case("greet.string", () => acc.acc_greet(new MWCharArray("Ada")).ToString());
Case("greet.implicit", () => acc.acc_greet("Ada"));
Case("scale", () => acc.acc_scale(new MWNumericArray(new double[,] { { 1, 2 }, { 3, 4 } }), new MWNumericArray(10.0)));
Case("scale.array", () =>
{
    var y = (MWNumericArray)acc.acc_scale(new MWNumericArray(new double[,] { { 1, 2 }, { 3, 4 } }), new MWNumericArray(10.0));
    var d = (double[,])y.ToArray(MWArrayComponent.Real);
    return $"{d.GetLength(0)}x{d.GetLength(1)} {d[0, 0]} {d[0, 1]} {d[1, 0]} {d[1, 1]}";
});
Case("count.1", () => acc.acc_count());
Case("count.2", () => acc.acc_count());
Case("fail", () => { acc.acc_fail(new MWCharArray("from .NET")); return "ok"; });
Case("add.wrongclass", () => acc.acc_add(new MWCharArray("x"), new MWNumericArray(3.0)));
Case("count.3", () => acc.acc_count());
Case("dispose", () => { acc.Dispose(); return "ok"; });
