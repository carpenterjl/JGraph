using System.Buffers.Binary;
using System.Text;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 131 and 132 (ADR 0235), where the parity fixture cannot reach: the bytes of a version
/// 7.3 file the R2025b cross-check depends on (its HDF5 aborts on an end-of-file address counted from
/// the superblock and on a global heap under 4096 bytes), the file read back, an on/off property's
/// class where R2025b keeps char, and a member passed to <c>sprintf</c> beside a number. What a
/// script reads is R2025b's (fixtures oi_onoff_states_save_v73, oi_dot_number_duration_fill).
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch22Tests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { })));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private async Task<string> Printed(string code)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark;
    }

    // --- a version 7.3 file -----------------------------------------------------------------------

    [Fact]
    public void AVersion73FileHasR2025bsUserblockSuperblockAndHeap()
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["a"] = JgsValue.Number(1),
            ["name"] = JgsValue.Str("x"),
        };
        byte[] file = MatV73Writer.Build([("s", JgsValue.Struct(fields)), ("v", JgsValue.Number(5))]);

        // The userblock: R2025b's description and the version word 0x0200, 'IM'.
        Assert.StartsWith("MATLAB 7.3 MAT-file", Encoding.ASCII.GetString(file, 0, 116));
        Assert.Equal(new byte[] { 0x00, 0x02, (byte)'I', (byte)'M' }, file[124..128]);

        // The superblock at 512, based there, with an absolute end-of-file address.
        Assert.Equal(0x89, file[512]);
        Assert.Equal("HDF", Encoding.ASCII.GetString(file, 513, 3));
        Assert.Equal(512, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(512 + 24)));
        Assert.Equal(file.Length, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(512 + 40)));

        // The struct's field names live in a global heap of at least 4096 bytes.
        int heap = IndexOf(file, "GCOL"u8);
        Assert.True(heap > 0);
        Assert.True(BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(heap + 8)) >= 4096);
    }

    [Fact]
    public void AVersion73FileReadsBackAsWritten()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jg_b22_{Guid.NewGuid():N}.mat");
        try
        {
            JgsValue matrix = JgsMatrix.FromColumnMajor([1, 2, 3, 4, 5, 6], 2, 3);
            JgsValue cell = JgsValue.Cell([JgsValue.Number(1), JgsValue.Str("ab")]);
            cell.Reshape(1, 2);
            MatV73Writer.Write(path, [("m", matrix), ("c", cell), ("t", JgsValue.Str("hello"))]);
            IReadOnlyList<(string Name, JgsValue Value)> back = MatFileReader.Read(path);
            Assert.Equal(["c", "m", "t"], back.Select(static v => v.Name).ToArray());
            JgsValue m = back.Single(static v => v.Name == "m").Value;
            Assert.Equal(3, JgsMatrix.ColCount(m));
            Assert.Equal(6, m.ElementAt(5).AsNumber);
            Assert.Equal("ab", back.Single(static v => v.Name == "c").Value.AsCell[1].AsString);
            Assert.Equal("hello", back.Single(static v => v.Name == "t").Value.AsString);
            Assert.True(MatV73Writer.IsVersion73(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Version73RefusesWhatR2025bKeepsInItsObjectSubsystem()
    {
        Assert.Contains("version 7.3", MatV73Writer.WhyNotWritable(JgsValue.StringArray([JgsValue.Str("a")])));
        Assert.Null(MatV73Writer.WhyNotWritable(JgsValue.Number(1)));
    }

    // --- on/off as OnOffSwitchState ---------------------------------------------------------------

    [Fact]
    public async Task OnOffPropertiesAnswerAMemberButWhereR2025bKeepsChar()
    {
        string printed = await Printed(
            "f = figure('Visible', 'off'); u = uicontrol(f);\n" +
            "fprintf('%s|%s|%s|%s\\n', class(f.Visible), class(f.HandleVisibility), class(u.Enable), class(u.Visible));");
        Assert.Equal("matlab.lang.OnOffSwitchState|char|char|matlab.lang.OnOffSwitchState\n", printed);
    }

    [Fact]
    public async Task AMemberPrintsByTheConversionItMeets()
    {
        // A GUIDE file's log: a number first, the member under %s (open item 132).
        string printed = await Printed(
            "f = figure('Visible', 'off');\n" +
            "fprintf('%s\\n', sprintf('n=%d v=%s t=%d', 3, f.Visible, f.Visible));");
        Assert.Equal("n=3 v=off t=0\n", printed);
    }

    [Fact]
    public async Task WaitforComparesAMemberByItsName()
    {
        string printed = await Printed(
            "f = figure('Visible', 'off');\n" +
            "waitfor(f, 'Visible', 'off'); disp('done');");
        Assert.Equal("done\n", printed);
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle);
}
