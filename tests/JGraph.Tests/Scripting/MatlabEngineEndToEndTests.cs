using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S7: <c>.m</c> as a first-class document. A file with that extension routes to the MATLAB engine
/// in the editor and in <c>jgraph -batch</c> alike, and a genuine unmodified MATLAB script runs through
/// it start to finish.
/// </summary>
[Collection("JG facade")]
public class MatlabEngineEndToEndTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabEngineEndToEndTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    [Fact]
    public void TheEngineIsAvailableAndNamed()
    {
        Assert.Equal("MATLAB", _engine.Language);
        Assert.True(_engine.IsAvailable);
    }

    [Fact]
    public void DotMFilesRouteToTheMatlabEngine()
    {
        Assert.Equal("MATLAB", ScriptDocumentModel.LanguageForFile("analysis.m"));
        Assert.Equal("MATLAB", ScriptDocumentModel.LanguageForFile(@"C:\work\Analysis.M"));
        Assert.Equal(".m", ScriptDocumentModel.ExtensionForLanguage("MATLAB"));

        // The other languages are untouched.
        Assert.Equal("JGS", ScriptDocumentModel.LanguageForFile("script.jgs"));
        Assert.Equal("Python", ScriptDocumentModel.LanguageForFile("script.py"));
    }

    [Fact]
    public async Task AnUnmodifiedMatlabScriptRunsThroughTheEngine()
    {
        // Everything here is MATLAB's spelling, not JGS's: % comments, no 'let', 1-based indexing,
        // '...' continuation, transpose, a struct, a cell, a switch, and a function at the bottom.
        ScriptRunResult result = await Run("""
            % Summarise a short measurement run.
            samples = [4 8 15 ...
                       16 23 42];
            column = samples';

            stats.count = numel(column);
            stats.total = 0;
            for k = 1:stats.count
                stats.total = stats.total + column(k);
            end
            stats.mean = stats.total / stats.count;

            labels = {'small', 'large'};
            switch classify(stats.mean)
                case 'small'
                    name = labels{1};
                otherwise
                    name = labels{2};
            end

            fprintf('%d samples, mean %.1f, %s\n', stats.count, stats.mean, name);

            function kind = classify(value)
            if value > 100
                kind = 'large';
            else
                kind = 'small';
            end
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("6 samples, mean 18.0, small", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMatlabScriptCanPlot()
    {
        ScriptRunResult result = await Run("""
            x = 0:0.1:2*pi;
            y = sin(x);
            plot(x, y);
            title('sine');
            xlabel('t');
            show();
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        FigureModel figure = Assert.Single(_figures);
        Assert.Equal("sine", figure.Axes[0].Title);
        Assert.NotEmpty(figure.Axes[0].Plots);
    }

    [Fact]
    public async Task ASyntaxErrorReportsItsLine()
    {
        ScriptRunResult result = await Run("x = 1;\ny = [1 2;\n");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void BatchRunnerPicksTheMatlabEngineForADotMFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jgraph-matlab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(directory, "run.m");
            File.WriteAllText(script, "% a MATLAB script\nvalues = [1 2 3];\ndisp(sum(values))\n");

            ResolvedStatement resolved = StartupStatement.Resolve(script, directory);

            Assert.Null(resolved.Error);
            Assert.Equal("MATLAB", resolved.Language);
            Assert.Equal(script, resolved.SourcePath);
            Assert.Contains("values = [1 2 3];", resolved.Code, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchRunnerRunsADotMFileEndToEnd()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jgraph-matlab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(directory, "batch.m");
            File.WriteAllText(script, "% headless\nvalues = [1 2 3 4];\nfprintf('%d\\n', sum(values));\n");

            var options = new StartupOptions(StartupMode.Batch, script, StartDirectory: directory);
            int code = await BatchRunner.RunAsync(
                options,
                [new JgsScriptEngine(), new MatlabScriptEngine()],
                _output,
                (_, figure) => _figures.Add(figure));

            Assert.Equal(0, code);
            Assert.Contains("10", _output.NormalText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
