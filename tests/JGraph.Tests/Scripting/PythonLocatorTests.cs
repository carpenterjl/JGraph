using JGraph.Scripting;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public class PythonLocatorTests
{
    [Fact]
    public void FindPythonDll_HonorsEnvironmentOverride_WhenFileExists()
    {
        string temp = Path.GetTempFileName();
        string? previous = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        try
        {
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", temp);
            Assert.Equal(temp, PythonLocator.FindPythonDll());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", previous);
            File.Delete(temp);
        }
    }

    [Fact]
    public void FindPythonDll_IgnoresEnvironmentOverride_WhenFileMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"no_such_python_{Guid.NewGuid():N}.dll");
        string? previous = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        try
        {
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", missing);
            // Falls through to probing; the result must never be the non-existent override path.
            Assert.NotEqual(missing, PythonLocator.FindPythonDll());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", previous);
        }
    }
}
