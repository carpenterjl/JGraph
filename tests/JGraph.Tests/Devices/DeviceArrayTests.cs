using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// An array of device objects as R2025b's handle-object array (open item 36, ADR 0237), on the
/// simulated ports: the brackets and rearranging verbs, subscripts and growth, eq's broadcast, the
/// generic verbs, what the lifetime count holds open, the display, and the two voices a conversion
/// refuses in. The recorded rows are probe_dev_arrays and probe_dev_arrays2 on the com0com pair;
/// fixture dev_arrays runs the same rows against R2025b's recording.
/// </summary>
[Collection("JG facade")]
public class DeviceArrayTests
{
    private const string TwoPorts = """
        jgraph.internal.devicesim('COM981');
        jgraph.internal.devicesim('COM985');
        internal.Serialport.clearPreferences();
        a = serialport("COM981", 9600);
        b = serialport("COM985", 9600);
        f = [a b];
        """;

    [Fact]
    public void BracketsBuildArraysOfSharedHandles()
    {
        Assert.Equal(
            "internal.Serialport [1 2]|internal.Serialport [2 1]|internal.Serialport [1 3]|internal.Serialport [2 2]|internal.Serialport [2 1]|7 7",
            Run(TwoPorts + """
                d = @(v) sprintf('%s %s', class(v), mat2str(size(v)));
                g = f';
                f(2).Timeout = 7;
                fprintf('%s|%s|%s|%s|%s|%g %g', d(f), d([a; b]), d([f a]), d([f; f]), d(g), b.Timeout, g(2).Timeout);
                """));
    }

    [Fact]
    public void SubscriptsPickElementsAndRefuseAsR2025bDoes()
    {
        Assert.Equal(
            "COM985 COM985 COM981|internal.Serialport [1 2] 1 1|internal.Serialport [0 0]|" +
            "MATLAB:badsubscript ## Index exceeds the number of array elements. Index must not exceed 2.|" +
            "MATLAB:badsubscript ## Array indices must be positive integers or logical values.",
            Run(TwoPorts + """
                d = @(v) sprintf('%s %s', class(v), mat2str(size(v)));
                g = a([1 1]);
                fprintf('%s %s %s|%s %d %d|%s|%s|%s', f(2).Port, f(end).Port, f(1).Port, ...
                    d(g), g(1) == a, g(2) == a, d(f([])), oops(@() f(3)), oops(@() f(0)));
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    [Fact]
    public void EqBroadcastsIdentityElementByElement()
    {
        Assert.Equal(
            "[true false]|[true true]|[false true]|[false false]|logical [0 0]|[true true;true true]|" +
            "MATLAB:dimagree ## Matrix dimensions must agree.",
            Run(TwoPorts + """
                d = @(v) sprintf('%s %s', class(v), mat2str(size(v)));
                fprintf('%s|%s|%s|%s|%s|%s|%s', mat2str(f == a), mat2str(f == f), mat2str(f ~= a), ...
                    mat2str(f == 1), d(f([]) == a), mat2str([f; f] == [f; f]), oops(@() f(:) == f));
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    [Fact]
    public void TheArrayHoldsItsPortsOpen()
    {
        // f = serialportfind; clear a b leaves both ports open in R2025b (the chip's own finding,
        // stess_89 section 9); releasing the array closes them before the next statement.
        Assert.Equal("1 2 [true true]|0 1", Run(TwoPorts + """
            held = serialportfind;
            clear a b f
            try, serialport("COM981", 9600); taken = 0; catch, taken = 1; end
            fprintf('%d %d %s|', taken, numel(serialportfind), mat2str(isvalid(held)));
            clear held
            left = numel(serialportfind);
            t = serialport("COM981", 9600);
            fprintf('%d %d', left, numel(serialportfind));
            """));
    }

    [Fact]
    public void ACellHoldingTheArrayKeepsThePortOpen()
    {
        Assert.Equal("1|double [0 0]", Run(TwoPorts + """
            clear f b
            c = {[a a]};
            clear a
            fprintf('%d|', numel(serialportfind));
            clear c
            v = serialportfind;
            fprintf('%s %s', class(v), mat2str(size(v)));
            """));
    }

    [Fact]
    public void GrowthAsksForTheDefaultObject()
    {
        Assert.Equal(
            "serialport:serialport:NoSavedPreferences ## Unable to create the default serialport object. Specify the 'Port' and 'BaudRate' input arguments.|" +
            "serialport:serialport:NoSavedPreferences ## Unable to create the default serialport object. Specify the 'Port' and 'BaudRate' input arguments.|" +
            "MATLAB:class:MissingGetDefaultScalarElement|internal.Serialport [1 3]",
            Run(TwoPorts + """
                u1 = udpport;
                u2 = udpport;
                fprintf('%s|%s|%s|%s', oops(@() grow(f, a)), oops(@() member_grow(f)), ...
                    just_id(@() grow([u1 u2], u2)), grow_ok(f, a));
                function grow(f, a)
                g = f;
                g(5) = a;
                end
                function member_grow(f)
                g = f;
                g(3).Timeout = 5;
                end
                function s = grow_ok(f, a)
                g = f;
                g(3) = a;
                s = sprintf('%s %s', class(g), mat2str(size(g)));
                end
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                function s = just_id(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = e.identifier;
                end
                end
                """));
    }

    [Fact]
    public void AConversionRefusesInTwoVoices()
    {
        // A join converts through the constructor and quotes its refusal; an indexed assignment
        // refuses flat (probe_dev_arrays2, join_number_first and foreign_write).
        Assert.Equal(
            "MATLAB:UnableToConvert ## The following error occurred converting from double to internal.Serialport:\n" +
            "Invalid number of input arguments for 'serialport'. Valid syntaxes are\ns = serialport()\n" +
            "s = serialport(PORT,BAUDRATE)\ns = serialport(PORT,BAUDRATE,NAME,VALUE)|" +
            "MATLAB:UnableToConvert ## Unable to perform assignment because value of type 'double' is not convertible to 'internal.Serialport'.",
            Run(TwoPorts + """
                fprintf('%s|%s', oops(@() [a 1]), oops(@() store(f)));
                function store(f)
                g = f;
                g(1) = 1;
                end
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    [Fact]
    public void TheGenericVerbsAnswerTheRow()
    {
        Assert.Equal(
            "struct [2 1]|cell [2 1]|cell [2 2]|5 5|[true true]|cell [17 1]|cell [30 1]|[true true]|" +
            "MATLAB:maxlhs ## Too many output arguments.|0 0",
            Run(TwoPorts + """
                d = @(v) sprintf('%s %s', class(v), mat2str(size(v)));
                set(f, 'Timeout', 5);
                fprintf('%s|%s|%s|%g %g|%s|%s|%s|%s|%s|', d(get(f)), d(get(f, 'Port')), ...
                    d(get(f, {'Port', 'Timeout'})), a.Timeout, b.Timeout, mat2str(isvalid(f)), ...
                    d(properties(f)), d(methods(f)), mat2str(isprop(f, 'Port')), oops(@() bad_delete(f)));
                delete(f);
                fprintf('%d %d', isvalid(a), isvalid(b));
                function x = bad_delete(f)
                x = delete(f);
                end
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    [Fact]
    public void TheRearrangersRunOverScalarsAndRows()
    {
        Assert.Equal(
            "internal.Serialport [1 3]|internal.Serialport [1 2]|internal.Serialport [2 1]|COM985|COM985|" +
            "cell [1 2] internal.Serialport 1|COM981,COM985|" +
            "MATLAB:invalidConversion ## Conversion to char from internal.Serialport is not possible.",
            Run(TwoPorts + """
                d = @(v) sprintf('%s %s', class(v), mat2str(size(v)));
                nc = num2cell(f);
                fprintf('%s|%s|%s|%s|%s|%s %s %d|%s|%s', d(repmat(a, 1, 3)), d(cat(2, a, b)), ...
                    d(reshape(f, 2, 1)), first(fliplr(f)), first(flip(f)), d(nc), class(nc{1}), nc{1} == a, ...
                    strjoin(arrayfun(@(p) p.Port, f), ','), oops(@() char(f)));
                function s = first(g)
                s = char(g(1).Port);
                end
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    [Fact]
    public void AMethodOnTheRowRefusesInOneVoice()
    {
        // R2025b errors through each method's own validation (probe_dev_arrays2: flush_row,
        // cfg_row); JGraph refuses in one stable voice (divergence, ADR 0237).
        Assert.Equal(
            "JGraph:deviceArray:ScalarMethod ## 'flush' runs on one internal.Serialport at a time; index the array.",
            Run(TwoPorts + """
                fprintf('%s', oops(@() flush(f)));
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    [Fact]
    public void TheDisplayShowsTheArrayAndItsDead()
    {
        Assert.Equal(
            "1×2 Serialport array with properties:|Invalid or deleted object.|1x2 Invalid or deleted object",
            Run(TwoPorts + """
                shown = evalc('disp(f)');
                lines = strsplit(shown, newline);
                delete(b);
                mixed = evalc('disp(f)');
                parts = strsplit(mixed, newline);
                delete(a);
                dead = evalc('disp(f)');
                fprintf('%s|%s|%s', strtrim(lines{1}), strtrim(parts{end - 1}), strtrim(dead));
                """));
    }

    [Fact]
    public void AudioplayerKeepsItsRefusalYetFormsArrays()
    {
        Assert.Equal(
            "MATLAB:audiovideo:audioplayer:noconcatenation ## Audioplayer objects cannot be concatenated.|" +
            "MATLAB:audiovideo:audioplayer:noconcatenation ## Audioplayer objects cannot be concatenated.|" +
            "audioplayer [1 2]|audioplayer [1 2]|audioplayer [1 2]",
            Run("""
                p = audioplayer(zeros(100, 1), 8000);
                q = audioplayer(zeros(100, 1), 8000);
                d = @(v) sprintf('%s %s', class(v), mat2str(size(v)));
                fprintf('%s|%s|%s|%s|%s', oops(@() [p q]), oops(@() horzcat(p, q)), ...
                    d(repmat(p, 1, 2)), d(cat(2, p, q)), d(p([1 1])));
                function s = oops(body)
                try
                    body();
                    s = 'none';
                catch e
                    s = [e.identifier ' ## ' e.message];
                end
                end
                """));
    }

    private static string Run(string code)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
