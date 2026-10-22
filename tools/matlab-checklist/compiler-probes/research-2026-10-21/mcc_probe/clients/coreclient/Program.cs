using System;
using MathWorks.MATLAB.NET.Arrays;

static void Show(string label, object o)
{
    if (o is MWArray a)
        Console.WriteLine($"{label}: {a.GetType().Name} dims=[{string.Join(",", a.Dimensions)}] <<{a.ToString().Replace("\n", " | ")}>>");
    else if (o is Array arr)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var x in arr) parts.Add(x?.ToString() ?? "null");
        Console.WriteLine($"{label}: {o.GetType().FullName} rank={arr.Rank} [{string.Join(", ", parts)}]");
    }
    else
        Console.WriteLine($"{label}: {o?.GetType().FullName ?? "null"} <<{o}>>");
}

static void Try(string label, Func<object> f)
{
    try { Show(label, f()); }
    catch (Exception e)
    {
        var x = e; while (x.InnerException != null) x = x.InnerException;
        Console.WriteLine($"{label}: THREW {x.GetType().FullName}: {x.Message.Trim().Replace("\n", " ")}");
    }
}

var c = new JgProbeCore.addtwoClass();
Try("getint8", () => c.getint8());
Try("getstr", () => c.getstr());
Try("getcplx", () => c.getcplx());
Try("addtwo(2,3) -> one output", () => c.addtwo((MWArray)2.0, (MWArray)3.0));
var n = new JgProbeCoreNative.addtwoClass();
Try("native getint8", () => n.getint8());
Try("native getstr", () => n.getstr());
Try("native getcplx", () => n.getcplx());
Try("native shapes(3)", () => n.shapes(3));
Try("native shapes(1) struct", () => n.shapes());
Try("native addtwo(int8 via sbyte)", () => n.addtwo((sbyte)3, (sbyte)4));
Try("native addtwo(bool,bool)", () => n.addtwo(true, true));
Try("native addtwo(double[,])", () => n.addtwo(new double[,] { { 1, 2 }, { 3, 4 } }, 1.0));
c.Dispose(); n.Dispose();
Console.WriteLine("done");
