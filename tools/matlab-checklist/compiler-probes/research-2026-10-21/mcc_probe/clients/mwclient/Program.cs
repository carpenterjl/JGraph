using System;
using System.Diagnostics;
using MathWorks.MATLAB.NET.Arrays;

static void Show(string label, object o)
{
    if (o is MWArray a)
        Console.WriteLine($"{label}: {a.GetType().Name} dims=[{string.Join(",", a.Dimensions)}] ToString=<<{a}>>");
    else
        Console.WriteLine($"{label}: {o?.GetType().FullName ?? "null"} value=<<{Format(o)}>>");
}

static string Format(object o)
{
    if (o is Array arr)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var x in arr) parts.Add(Format(x));
        return $"{o.GetType().Name}[{string.Join(", ", parts)}]";
    }
    return o?.ToString() ?? "null";
}

var sw = Stopwatch.StartNew();
var mode = args.Length > 0 ? args[0] : "mwarray";
if (mode == "mwarray")
{
    var c = new JgProbe.addtwoClass();
    Console.WriteLine($"construct (runtime start): {sw.ElapsedMilliseconds} ms");
    sw.Restart();
    MWArray r = c.addtwo(new MWNumericArray(1.0), new MWNumericArray(2.0));
    Console.WriteLine($"first call: {sw.ElapsedMilliseconds} ms");
    Show("addtwo(1.0,2.0)", r);
    Console.WriteLine("  ToScalarDouble=" + ((MWNumericArray)r).ToScalarDouble());
    Show("addtwo((MWArray)1, (MWArray)2) via implicit int", c.addtwo((MWArray)1, (MWArray)2));
    Show("addtwo(\"3\",\"4\") MWCharArray", c.addtwo(new MWCharArray("3"), new MWCharArray("4")));
    Show("addtwo(row, 10)", c.addtwo(new MWNumericArray(new double[] { 1, 2, 3 }), 10.0));
    MWArray[] m = c.multi(3, 1.0, 2.0, 3.0);
    for (int i = 0; i < m.Length; i++) Show($"multi out {i}", m[i]);
    MWArray[] s = c.shapes(6);
    for (int i = 0; i < s.Length; i++) Show($"shapes out {i}", s[i]);
    var st = (MWStructArray)s[0];
    Console.WriteLine("  struct fields=" + string.Join(",", st.FieldNames) + " name=" + st["name"]);
    try { c.addtwo(3, 1.0, 2.0); Console.WriteLine("3 outputs from addtwo returned?"); }
    catch (Exception e) { Console.WriteLine($"addtwo nargout=3: {e.GetType().FullName}: {e.Message.Trim()}"); }
    c.Dispose();
}
else if (mode == "native")
{
    var c = new JgProbeNative.addtwoClass();
    Console.WriteLine($"construct (runtime start): {sw.ElapsedMilliseconds} ms");
    Show("native addtwo(1.0,2.0)", c.addtwo(1.0, 2.0));
    Show("native addtwo((object)1,(object)2)", c.addtwo((object)1, (object)2));
    Show("native addtwo(double[] {1,2,3}, 10.0)", c.addtwo(new double[] { 1, 2, 3 }, 10.0));
    Show("native addtwo(\"3\",\"4\")", c.addtwo("3", "4"));
    object[] m = c.multi(3, 1.0, 2.0, 3.0);
    for (int i = 0; i < m.Length; i++) Show($"native multi out {i}", m[i]);
    try
    {
        object[] s = c.shapes(6);
        for (int i = 0; i < s.Length; i++) Show($"native shapes out {i}", s[i]);
    }
    catch (Exception e) { Console.WriteLine($"native shapes: {e.GetType().FullName}: {e.Message.Trim()}"); }
    c.Dispose();
}
Console.WriteLine($"total {sw.ElapsedMilliseconds} ms");

