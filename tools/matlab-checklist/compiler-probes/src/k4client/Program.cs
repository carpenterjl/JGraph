// Stage K4's client (compiler and SDK plan): two instances of R2025b's K4A component and one of
// K4B, in one process, each running k4.m. Prints key<TAB>value lines: what one instance's change
// does to the others, whether calls from two threads overlap, what an error looks like from .NET,
// and what disposing an instance leaves. The runtime's own output (disp, warnings) goes to the
// process's stdout and stderr between these lines.
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;

static void Line(string key, string value)
{
    Console.Out.Flush();
    Console.WriteLine($"{key}\t{value}");
    Console.Out.Flush();
}

static string Flat(string s) => s.Replace("\r", "").Replace("\n", " / ").Trim();

static string Describe(Exception e)
{
    var parts = new System.Collections.Generic.List<string>();
    for (Exception x = e; x != null; x = x.InnerException)
    {
        parts.Add($"{x.GetType().FullName}: {Flat(x.Message)}");
    }

    return string.Join(" <- ", parts);
}

static string Run(Func<MWArray, MWArray, MWArray> k4, string op, MWArray arg)
{
    try
    {
        return k4(new MWCharArray(op), arg).ToString();
    }
    catch (Exception e)
    {
        return "THREW " + Describe(e);
    }
}

MWArray none = new MWNumericArray();
var clock = Stopwatch.StartNew();
var a1 = new K4A.k4Class();
Line("time.a1", $"{clock.Elapsed.TotalSeconds:F1}");
clock.Restart();
var a2 = new K4A.k4Class();
Line("time.a2", $"{clock.Elapsed.TotalSeconds:F1}");
clock.Restart();
var b = new K4B.k4Class();
Line("time.b", $"{clock.Elapsed.TotalSeconds:F1}");

Func<MWArray, MWArray, MWArray> A1 = (op, arg) => a1.k4(op, arg);
Func<MWArray, MWArray, MWArray> A2 = (op, arg) => a2.k4(op, arg);
Func<MWArray, MWArray, MWArray> B = (op, arg) => b.k4(op, arg);
var who = new[] { ("a1", A1), ("a2", A2), ("b", B) };

foreach (var (name, k4) in who)
{
    Line($"quiet.{name}", Run(k4, "quiet", none));
}

Line("deployed.a1", Run(A1, "deployed", none));
foreach (var (name, k4) in who)
{
    Line($"ctfroot.{name}", Run(k4, "ctfroot", none));
}

Line("setg.a1", Run(A1, "setg", new MWNumericArray(7.0)));
foreach (var (name, k4) in who)
{
    Line($"getg.{name}", Run(k4, "getg", none));
}

foreach (var (name, k4) in who)
{
    Line($"count.{name}", Run(k4, "count", none));
}

string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "k4client_cd");
System.IO.Directory.CreateDirectory(folder);
Line("pwd.before.a1", Run(A1, "pwd", none));
Line("dotnet.cwd.before", Environment.CurrentDirectory);
Line("cd.a1", Run(A1, "cd", new MWCharArray(folder)));
Line("pwd.a2", Run(A2, "pwd", none));
Line("pwd.b", Run(B, "pwd", none));
Line("dotnet.cwd.after", Environment.CurrentDirectory);

Line("rng.a1", Run(A1, "rng", new MWNumericArray(1.0)));
Line("rand.b", Run(B, "rand", none));
Line("rand.a2", Run(A2, "rand", none));
Line("rand.a1", Run(A1, "rand", none));

Line("format.a1", Run(A1, "format", new MWCharArray("long")));
foreach (var (name, k4) in who)
{
    Line($"show.{name}", Run(k4, "show", none));
}

Line("warnoff.a1", Run(A1, "warnoff", new MWCharArray("k4:w")));
Line("warn.b", Run(B, "warn", none));
Line("warn.a2", Run(A2, "warn", none));
Line("warn.a1", Run(A1, "warn", none));

Line("setud.a1", Run(A1, "setud", new MWCharArray("v1")));
Line("getud.a2", Run(A2, "getud", none));
Line("getud.b", Run(B, "getud", none));

Line("base.a1", Run(A1, "base", new MWNumericArray(5.0)));
Line("getbase.a2", Run(A2, "getbase", none));
Line("getbase.b", Run(B, "getbase", none));

Line("threads.a1", Run(A1, "threads", none));
Line("say.a1", Run(A1, "say", none));

try
{
    a1.k4(new MWCharArray("fail"), new MWNumericArray(3.0));
    Line("fail.a1", "returned");
}
catch (Exception e)
{
    Line("fail.a1", Describe(e));
    Line("fail.a1.stack", e is MWException mw ? Flat(mw.StackTrace ?? "") : "(not an MWException)");
    Line("fail.a1.data", string.Join(", ", e.Data.Cast<System.Collections.DictionaryEntry>().Select(d => $"{d.Key}={d.Value}")));
}

Line("other.thread.a1", await Task.Run(() => Run(A1, "deployed", none)));
foreach (var (label, first, second) in new[] { ("a1+b", A1, B), ("a1+a2", A1, A2) })
{
    clock.Restart();
    var one = Task.Run(() => Run(first, "wait", new MWNumericArray(1.0)));
    var two = Task.Run(() => Run(second, "wait", new MWNumericArray(1.0)));
    await Task.WhenAll(one, two);
    Line($"overlap.{label}", $"{one.Result} {two.Result} total {clock.Elapsed.TotalSeconds:F1}");
}

Line("figure.a1", Run(A1, "figure", none));
Line("figures.a2", Run(A2, "figures", none));
Line("figures.b", Run(B, "figures", none));

a1.Dispose();
Line("disposed.a1", Run(A1, "getg", none));
Line("after.a2", Run(A2, "getg", none));
var a3 = new K4A.k4Class();
Line("new.a3", Run((op, arg) => a3.k4(op, arg), "getg", none));
Line("count.a3", Run((op, arg) => a3.k4(op, arg), "count", none));
a2.Dispose();
a3.Dispose();
Line("after.b", Run(B, "getg", none));
b.Dispose();
Line("mcr.initialized", MWMCR.IsMCRInitialized().ToString());
Line("done", "ok");
