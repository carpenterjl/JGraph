using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 336 and 318 (ADR 0378), measured in R2025b: getReport's "Error in" blocks with their carets,
/// a nested function's and a class method's report names, the anonymous frames R2025b leaves out (a text
/// holding <c>&gt;%</c>), eval binding a name its function reads as a function, fopen's readers detecting
/// the encoding, fwrite to the console and its rounding, and the metaclass of a built-in class.
/// </summary>
[Collection("JG facade")]
public class ReportFramesAndFileEncodingTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-oi336-" + Guid.NewGuid().ToString("N"));

    public ReportFramesAndFileEncodingTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task<string> RunMatlabFile(string name, string source)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, source);
        ScriptContext context = new(_output, static (_, _) => { }, _folder) { ScriptPath = path };
        ScriptRunResult result = await new MatlabScriptEngine().RunAsync(source, context, default);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private void WriteFile(string name, string source) => File.WriteAllText(Path.Combine(_folder, name), source);

    /// <summary>The default report lists the frames below the header with the line and carets under the call.</summary>
    [Fact]
    public async Task DefaultReport_HasAnErrorInBlockWithCarets()
    {
        string text = await RunMatlabFile("rep1.m",
            "try\n    y = 3 + boom(4) * 2;\ncatch e\n    fprintf('%s', getReport(e));\nend\nfunction r = boom(v)\nerror('t:b', 'boom %d', v);\nr = v;\nend\n");

        Assert.Equal(
            "Error using rep1>boom (line 7)\nboom 4\n\nError in rep1 (line 2)\n    y = 3 + boom(4) * 2;\n            ^^^^^^^",
            text);
    }

    /// <summary>A refusal of the runtime's has no header; 'basic' is the message alone.</summary>
    [Fact]
    public async Task RuntimeRefusal_HasNoHeader()
    {
        string text = await RunMatlabFile("rep2.m",
            "try\n    x = [1 2]; y = x(5);\ncatch e\n    fprintf('%s#%s', getReport(e), getReport(e, 'basic'));\nend\n");

        Assert.Equal(
            "Index exceeds the number of array elements. Index must not exceed 2.\n\nError in rep2 (line 2)\n    x = [1 2]; y = x(5);\n                   ^^^^"
            + "#Index exceeds the number of array elements. Index must not exceed 2.",
            text);
    }

    /// <summary>A nested function's frame is outer/inner, and the report heads it so.</summary>
    [Fact]
    public async Task NestedFunction_FrameIsOuterSlashInner()
    {
        WriteFile("nestr.m", "function nestr()\ninner();\n    function inner()\n        error('t:n', 'nested');\n    end\nend\n");
        string text = await RunMatlabFile("rep3.m",
            "try\n    nestr();\ncatch e\n    fprintf('%s#%s', e.stack(1).name, getReport(e, 'basic'));\nend\n");

        Assert.Equal("nestr/inner#Error using nestr/inner (line 4)\nnested", text);
    }

    /// <summary>An anonymous body whose text holds &gt;% is no frame: the error is headed by the script.</summary>
    [Fact]
    public async Task AnonymousBodyHoldingGreaterPercent_IsNoFrame()
    {
        string text = await RunMatlabFile("rep4.m",
            "try\n    cellfun(@(x) error('t:a', 'x>%d', x), {2});\ncatch e\n    fprintf('%d#%s', numel(e.stack), getReport(e));\nend\n");

        Assert.Equal("1#Error using rep4 (line 2)\nx>2", text);
    }

    /// <summary>eval binding pi in a function that reads pi leaves pi the built-in, and warns once for each text.</summary>
    [Fact]
    public async Task EvalBindingPi_TheFunctionReadsTheBuiltIn()
    {
        WriteFile("shp.m", "function r = shp(t)\nlastwarn('');\neval(t);\n[~, id] = lastwarn;\nr = sprintf('%s %g %g', id, pi, eval('pi'));\nend\n");
        string text = await RunMatlabFile("rep5.m",
            "a = shp('pi = 7;'); b = shp('pi = 7;'); c = shp('pi = 8;');\nfprintf('%s#%s#%s', a, b, c);\n");

        Assert.Equal(
            "MATLAB:lang:ExternalShadowedInEval 3.14159 7# 3.14159 7#MATLAB:lang:ExternalShadowedInEval 3.14159 8",
            text);
    }

    /// <summary>fopen in MATLAB code reads a Windows-1252 file in Windows-1252 and names it.</summary>
    [Fact]
    public async Task Fopen_DetectsWindows1252()
    {
        File.WriteAllBytes(Path.Combine(_folder, "w.txt"), [72, 233, 108, 10]);
        string text = await RunMatlabFile("rep6.m",
            "fid = fopen('w.txt'); [~, ~, ~, enc] = fopen(fid); t = fgetl(fid); fclose(fid);\nfprintf('%s#%s', enc, mat2str(double(t)));\n");

        Assert.Equal("windows-1252#[72 233 108]", text);
    }

    /// <summary>fwrite to a file rounds and saturates; fwrite(1, ...) writes the bytes to the console, NUL dropped.</summary>
    [Fact]
    public async Task Fwrite_RoundsSaturatesAndWritesTheConsole()
    {
        string text = await RunMatlabFile("rep7.m",
            "fid = fopen('b.bin', 'w'); fwrite(fid, [300 -5 2.5], 'uint8'); fclose(fid);\n"
            + "fid = fopen('b.bin'); b = fread(fid, inf, '*uint8')'; fclose(fid);\n"
            + "n = fwrite(1, [65 0 66]);\nfprintf('#%s#%d', mat2str(b), n);\n");

        Assert.Equal("AB#[255 0 3]#3", text);
    }

    /// <summary>?MException is a metaclass, and its properties cannot be written.</summary>
    [Fact]
    public async Task MetaclassOfMException_AndItsReadOnlyProperties()
    {
        string text = await RunMatlabFile("rep8.m",
            "mc = ?MException; me = MException('t:a', 'b');\ntry\n    me.Correction = 1;\ncatch e\n    id = e.identifier;\nend\nfprintf('%s#%s#%s', class(mc), mc.Name, id);\n");

        Assert.Equal("matlab.metadata.Class#MException#MATLAB:class:SetProhibited", text);
    }
}
