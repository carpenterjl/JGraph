using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U11 of the app-building plan (ADR 0210): MATLAB's own <c>.fig</c> files and GUIDE apps. What a
/// script sees is held by the parity fixtures (<c>u11_figfile</c>, <c>u11_guide</c>) over files
/// R2025b wrote; these hold the parts under them - the subsystem decoder, the MAT reader's empty and
/// opaque elements and MATLAB's form of a function handle - and three fixes the stage needed
/// elsewhere: <c>str2func</c> of a name nothing answers, a legend's <c>String</c> read before any
/// drawing, and the root's <c>uicontrol</c> defaults.
/// </summary>
[Collection("JG facade")]
public class FigFilesU11Tests : IAsyncLifetime
{
    private static readonly string Helpers =
        Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, Helpers)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        await _session.DisposeAsync();
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>Records what the reader asks a binder for, and makes nothing it is not asked to.</summary>
    private sealed class RecordingBinder : IMatObjectBinder
    {
        public List<(string Text, string Type, IReadOnlyDictionary<string, JgsValue>? Workspace)> Functions { get; } = [];

        public JgsValue NewObject(string className, bool deleted, string variable) => JgsValue.Number(0);

        public void SetProperties(JgsValue instance, IReadOnlyDictionary<string, JgsValue> properties)
        {
        }

        public JgsValue Function(string text, string type, IReadOnlyDictionary<string, JgsValue>? workspace, string variable)
        {
            Functions.Add((text, type, workspace));
            return JgsValue.Str(text);
        }
    }

    [Fact]
    public void SubsystemHoldsR2025bsFigureBehindItsWrapper()
    {
        // savefig in R2025b keeps the whole figure in the subsystem; hgS_080000 is an empty struct.
        (IReadOnlyList<(string Name, JgsValue Value)> variables, MatMcos? store) = MatFileReader.ReadWithSubsystem(
            Path.Combine(Helpers, "u11_plots.fig"), new HashSet<string> { "hgS_080000", "hgM_080000" }, new RecordingBinder());

        Assert.NotNull(store);
        JgsValue empty = Assert.Single(variables, v => v.Name == "hgS_080000").Value;
        Assert.True(empty.IsStructArray);
        Assert.Equal(0, empty.AsStructArray.Length);

        JgsValue wrapper = Assert.Single(variables, v => v.Name == "hgM_080000").Value.AsStruct["GraphicsObjects"];
        Assert.True(MatMcos.Refs(wrapper, out int[] ids));
        Assert.Equal("matlab.graphics.internal.figfile.GraphicsObjects", store!.ClassOf(Assert.Single(ids)));

        JgsValue format3 = Assert.Single(store.Properties(ids[0]), p => p.Name == "Format3Data").Value;
        Assert.True(MatMcos.Refs(format3, out int[] figure));
        Assert.Equal("matlab.ui.Figure", store.ClassOf(Assert.Single(figure)));
        Assert.Contains(store.Properties(figure[0]), p => p.Name == "Name_I" && p.Value.AsString == "plots");
    }

    [Fact]
    public void StructFormReadsEmptyElementsAndMatlabsFunctionHandles()
    {
        // Research C's pair: hgsave's struct tree, its childless nodes written as empty elements,
        // its callbacks as MATLAB's function elements whose workspace is a subsystem object.
        var binder = new RecordingBinder();
        (IReadOnlyList<(string Name, JgsValue Value)> variables, _) = MatFileReader.ReadWithSubsystem(
            Path.Combine(Helpers, "myguide.fig"), new HashSet<string> { "hgS_070000" }, binder);

        IReadOnlyDictionary<string, JgsValue> tree = Assert.Single(variables).Value.AsStruct;
        Assert.Equal("figure", tree["type"].AsString);
        IReadOnlyDictionary<string, JgsValue> first = tree["children"].AsStructArray.Elements[0];
        Assert.Equal(JgsType.Array, first["children"].Type);
        Assert.Equal(0, first["children"].ArrayLength);

        Assert.Contains(binder.Functions, f => f.Type == "anonymous"
            && f.Text == "@(hObject,eventdata)myguide('pushbutton1_Callback',hObject,eventdata,guidata(hObject))"
            && f.Workspace is { Count: 0 });
    }

    [Theory]
    [InlineData("sf%2@(hObject,eventdata)f(hObject)", "@(hObject,eventdata)f(hObject)")]
    [InlineData("sf%17@(x) x+1", "@(x) x+1")]
    [InlineData("legendpostdeserialize", "legendpostdeserialize")]
    [InlineData("sf%x@(x)x", "sf%x@(x)x")]
    public void AnonymousTextLosesOnlyItsSlotPrefix(string saved, string text) =>
        Assert.Equal(text, MatMcos.FunctionText(saved));

    [Fact]
    public async Task OpenfigBuildsTheModelOfAFileWithoutItsCode()
    {
        await Exec("h = openfig('u11_plots.fig', 'invisible');");
        FigureModel figure = Assert.Single(JG.FigureNumbers.Select(n => JG.TryGetFigure(n, out FigureModel f) ? f : null!));
        Assert.Equal("plots", figure.Name);
        Assert.False(figure.Visible);
        Assert.Equal(2, figure.Axes.Count);
        Assert.Equal(2, figure.Axes.Sum(static a => a.Plots.OfType<LinePlot>().Count()));
        Assert.EndsWith("u11_plots.fig", figure.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Str2funcOfANameNothingAnswersIsRefusedOnlyWhenCalled()
    {
        await Exec("""
            f = str2func('u11_nothing_here');
            k = class(f);
            try, f(1); catch e, id = e.identifier; msg = e.message; end
            """);
        await Exec("disp(k); disp(id); disp(msg)");
        Assert.Contains("function_handle", _output.NormalText);
        Assert.Contains("MATLAB:UndefinedFunction", _output.NormalText);
        Assert.Contains("Undefined function 'u11_nothing_here' for input arguments of type 'double'.", _output.NormalText);
    }

    [Fact]
    public async Task LegendStringIsCurrentBeforeAnyDrawing()
    {
        await Exec("""
            f = figure('Visible', 'off');
            plot(1:3); hold on; plot(3:-1:1);
            lg = legend({'a', 'b'});
            disp(strjoin(lg.String, '|'))
            """);
        Assert.Contains("a|b", _output.NormalText);
    }

    [Fact]
    public async Task RootAnswersTheUicontrolDefaults()
    {
        await Exec("""
            f = figure('Visible', 'off');
            c = uicontrol(f);
            disp(double(isequal(get(0, 'defaultUicontrolBackgroundColor'), c.BackgroundColor)))
            """);
        Assert.Contains("1", _output.NormalText);
    }
}
