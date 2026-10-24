// Stage K5's client (compiler and SDK plan): one instance of R2025b's K5 component, running k5.m.
// Prints key<TAB>value lines around the runtime's own output, so where k5's "tick" lines land shows
// whether the runtime ran the component's timer while this client was between calls (sleeping), only
// inside calls that pause, or also inside a call that computes without pausing. Then what
// WaitForFiguresToDie does with only an invisible figure open. No window is shown.
using System;
using System.Diagnostics;
using System.Threading;
using MathWorks.MATLAB.NET.Arrays;

static void Line(string key, string value)
{
    Console.Out.Flush();
    Console.WriteLine($"{key}\t{value}");
    Console.Out.Flush();
}

static string Flat(string s) => s.Replace("\r", "").Replace("\n", " / ").Trim();

MWArray none = new MWNumericArray();
var k5 = new K5.k5Class();
string Run(string op, MWArray arg)
{
    try
    {
        return k5.k5(new MWCharArray(op), arg).ToString();
    }
    catch (Exception e)
    {
        return "THREW " + e.GetType().FullName + ": " + Flat(e.Message);
    }
}

Line("quiet", Run("quiet", none));

// Five ticks, 0.1 s apart, started by one call; the client then makes no call for 1.5 s.
Line("timer.idle", Run("timer", new MWNumericArray(5.0)));
Line("sleep.begin", "1.5 s with no call");
Thread.Sleep(1500);
Line("sleep.end", "");
Line("ticks.after.sleep", Run("ticks", none));
Thread.Sleep(500);

// Five more, then at once a call that pauses for 1 s.
Line("timer.pause", Run("timer", new MWNumericArray(5.0)));
Line("ticks.before.pause", Run("ticks", none));
Line("wait", Run("wait", new MWNumericArray(1.0)));
Line("ticks.after.pause", Run("ticks", none));
Thread.Sleep(500);

// Five more, then at once a call that computes for 1 s without pausing.
Line("timer.spin", Run("timer", new MWNumericArray(5.0)));
Line("spin", Run("spin", new MWNumericArray(1.0)));
Line("ticks.after.spin", Run("ticks", none));
Thread.Sleep(1500);
Line("ticks.after.spin.sleep", Run("ticks", none));

// WaitForFiguresToDie with nothing open, then with one invisible figure open.
var clock = Stopwatch.StartNew();
k5.WaitForFiguresToDie();
Line("wait.none.seconds", $"{clock.Elapsed.TotalSeconds:F1}");
Line("figure", Run("figure", none));
Line("figures", Run("figures", none));
clock.Restart();
k5.WaitForFiguresToDie();
Line("wait.invisible.seconds", $"{clock.Elapsed.TotalSeconds:F1}");
Line("figures.after.wait", Run("figures", none));
Line("closeall", Run("closeall", none));
Line("figures.after.closeall", Run("figures", none));
Line("deleteall", Run("deleteall", none));
Line("figures.after.deleteall", Run("figures", none));

// One invisible figure left open, for what the runtime says of it when the process ends.
Line("figure.left", Run("figure", none));
k5.Dispose();
Line("end", "");
