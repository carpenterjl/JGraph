using System.Globalization;
using JGraph.Runtime.Desktop;

namespace JGraph.Runtime.Launch;

/// <summary>
/// The managed entry of a standalone program (compiler and SDK plan, stage K6). Its launcher calls it
/// as <c>jglaunch.dll --program &lt;the program&gt; [--splash &lt;window&gt;] -- &lt;its arguments&gt;</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? program = null;
        nint splash = 0;
        int at = 0;
        for (; at < args.Length && args[at] != "--"; at++)
        {
            if (args[at] == "--program" && at + 1 < args.Length)
            {
                program = args[++at];
            }
            else if (args[at] == "--splash" && at + 1 < args.Length
                && long.TryParse(args[++at], NumberStyles.None, CultureInfo.InvariantCulture, out long window))
            {
                splash = (nint)window;
            }
        }

        if (program is null || at >= args.Length)
        {
            Console.Error.WriteLine("jglaunch: start a JGraph standalone program by its own name; this library is its runtime.");
            return 2;
        }

        return StandaloneProgramRun.Run(program, splash, args[(at + 1)..]);
    }
}
