using System.Runtime.CompilerServices;

namespace JGraph.Tests;

/// <summary>What every test in this assembly runs under, set once before the first test.</summary>
internal static class TestEnvironment
{
    /// <summary>
    /// The device classes' preferences (the settings a cleared serialport leaves for the next
    /// <c>serialport()</c>) go to a folder of this test process's own, never the person's: a run
    /// must not read what a session saved, nor leave anything behind for one (device classes plan).
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JGRAPH_PREFDIR")))
        {
            string folder = Path.Combine(Path.GetTempPath(), $"jgraph-test-prefs-{Environment.ProcessId}");
            Environment.SetEnvironmentVariable("JGRAPH_PREFDIR", folder);
        }
    }
}
