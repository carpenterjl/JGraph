using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 300, 303, 306 and 308 (ADR 0352), every expectation measured against R2025b: dir and
/// delete in the folder cd moved to, delete's patterns and warnings, fileread's choice of UTF-8 or
/// Windows-1252, a string under a printf conversion, an identifier with no message, the warning
/// table's layout, a warning raised as an error in reports and from the engine, the reports that
/// cannot raise, evalc's NUL and U+FFFF, and MException's Correction.
/// </summary>
[Collection("JG facade")]
public class FilesPrintfEvalcLeftoversTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jg_oi300_" + Guid.NewGuid().ToString("N"));

    public FilesPrintfEvalcLeftoversTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ScriptRunResult RunMatlab(string code)
    {
        _output.Normal.Clear();
        _output.Errors.Clear();
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string Run(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Quoted => _root.Replace("'", "''", StringComparison.Ordinal);

    // --- 300 -----------------------------------------------------------------------------------

    [Fact]
    public void Dir_ListsRelativeToTheFolderCdMovedTo()
    {
        Directory.CreateDirectory(Path.Combine(_root, "rel", "sub", "deep"));
        File.WriteAllText(Path.Combine(_root, "rel", "a.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "rel", "sub", "deep", "c.txt"), "x");
        string text = Run(
            $"old = cd('{Quoted}');\n"
            + "fprintf('%d;', numel(dir('rel')), numel(dir(fullfile('rel', '**'))), numel(dir(fullfile('rel', '**', '*.txt'))), numel(dir('**/*.txt')));\n"
            + "d = dir(fullfile('rel', '**', '*.txt')); fprintf('%s;', d.name);\n"
            + "cd(old);");
        Assert.Equal("4;10;2;2;a.txt;c.txt;", text);
    }

    [Fact]
    public void Delete_TakesPatternsAndWarnsAsR2025b()
    {
        Directory.CreateDirectory(Path.Combine(_root, "fold.tmp"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        foreach (string name in (string[])["x1.tmp", "x2.tmp", "y.txt", "q1.dat", "q22.dat", "ro.tmp", "Up.TMP", Path.Combine("sub", "a1.txt"), Path.Combine("sub", "b.txt")])
        {
            File.WriteAllText(Path.Combine(_root, name), "x");
        }

        File.SetAttributes(Path.Combine(_root, "ro.tmp"), FileAttributes.ReadOnly);
        string text = Run(
            $"old = cd('{Quoted}');\n"
            + "lastwarn('', ''); delete('*.tmp'); [m, i] = lastwarn; fprintf('%s;%d;', i, contains(m, 'fold.tmp'' is a directory.  Use rmdir'));\n"
            + "lastwarn('', ''); delete('sub/a*.txt'); delete('q?.dat'); delete('nomatch*.x'); delete(fullfile('s*', 'b.txt')); [m, i] = lastwarn; fprintf('[%s];', i);\n"
            + "lastwarn('', ''); delete('missing.txt'); [m, i] = lastwarn; fprintf('%s|%s;', i, m);\n"
            + "lastwarn('', ''); delete('sub'); [m, i] = lastwarn; fprintf('%s|%s;', i, m);\n"
            + "lastwarn('', ''); delete(''); [m, i] = lastwarn; fprintf('%s;', m);\n"
            + "try, delete({'y.txt'}); catch e, fprintf('%s|%s;', e.identifier, e.message); end\n"
            + "try, delete(); catch e, fprintf('%s;', e.identifier); end\n"
            + "warning('error', 'MATLAB:DELETE:FileNotFound'); try, delete('gone.txt'); catch e, fprintf('%s;', e.identifier); end\n"
            + "d = dir; d = d(~[d.isdir]); fprintf('%s,', d.name); d = dir('sub'); d = d(~[d.isdir]); fprintf('/%s', d.name);\n"
            + "cd(old);");
        Assert.Equal(
            "MATLAB:DELETE:DirectoryDeletion;1;[];"
            + "MATLAB:DELETE:FileNotFound|File 'missing.txt' not found.;"
            + "MATLAB:DELETE:DirectoryDeletion|'sub' is a directory.  Use rmdir to delete directories.;"
            + "File '' not found.;"
            + "MATLAB:string|Name must be a text scalar.;MATLAB:minrhs;MATLAB:DELETE:FileNotFound;"
            + "q22.dat,y.txt,/b.txt",
            text);
    }

    // --- 303 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0xC3, 0xA9, 0xE2, 0x82, 0xAC }, new[] { 233, 8364 })]
    [InlineData(new byte[] { 0xE9, 0x80 }, new[] { 233, 8364 })]
    [InlineData(new byte[] { 0xC3, 0xA9, 0xE9 }, new[] { 233, 65533 })]
    [InlineData(new byte[] { 0xC3, 0xA9, 0xE9, 0x41 }, new[] { 195, 169, 233, 65 })]
    [InlineData(new byte[] { 0x81, 0x8D, 0x8F, 0x90, 0x9D }, new[] { 129, 141, 143, 144, 157 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }, new[] { 65279, 97 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xE9, 0x61 }, new[] { 239, 187, 191, 233, 97 })]
    [InlineData(new byte[] { 0x61, 0x80, 0x62 }, new[] { 97, 8364, 98 })]
    [InlineData(new byte[] { 0xC0, 0xAF }, new[] { 65533, 65533 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, new[] { 65533, 65533, 65533 })]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80, 0x61 }, new[] { 55357, 56832, 97 })] // R2025b: 26 97, a divergence
    [InlineData(new byte[] { 0x61, 0xE2, 0x82 }, new[] { 97, 226, 8218 })]
    [InlineData(new byte[] { 0xC3, 0xC3, 0xA9 }, new[] { 195, 195, 169 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00 }, new[] { 65279, 97 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x61 }, new[] { 65279, 97 })]
    [InlineData(new byte[] { 0xA0, 0xFF }, new[] { 160, 255 })]
    public void DecodeDetected_FollowsR2025bsFileread(byte[] bytes, int[] codes) =>
        Assert.Equal(codes, JgsSourceText.DecodeDetected(bytes).Select(static c => (int)c).ToArray());

    [Fact]
    public void DecodeDetected_WeighsValidAgainstBrokenOverTheWholeFile()
    {
        byte[] Repeat(byte[] lead, int times, params byte[] tail) =>
            [.. lead, .. Enumerable.Range(0, times).SelectMany(static _ => new byte[] { 0xC3, 0xA9 }), .. tail];

        Assert.Equal(23, JgsSourceText.DecodeDetected(Repeat([], 11, 0x80)).Length); // Windows-1252
        Assert.Equal(12, JgsSourceText.DecodeDetected(Repeat([0xEF, 0xBB, 0xBF], 10, 0x80)).Length); // UTF-8
        Assert.Equal(22, JgsSourceText.DecodeDetected(Repeat([0xEF, 0xBB, 0xBF], 9, 0x80)).Length); // Windows-1252
        Assert.Equal(9001, JgsSourceText.DecodeDetected([.. Enumerable.Repeat((byte)0x61, 9000), 0xC3, 0xA9]).Length);
    }

    [Fact]
    public void Fileread_DetectsAndDecodesNamedEncodingsAsR2025b()
    {
        File.WriteAllBytes(Path.Combine(_root, "cp.bin"), [0xE9, 0x80]);
        File.WriteAllBytes(Path.Combine(_root, "high.bin"), [0x80, 0x81, 0x9F]);
        File.WriteAllBytes(Path.Combine(_root, "u8.bin"), [0xC3, 0xA9]);
        string text = Run(
            $"old = cd('{Quoted}');\n"
            + "fprintf('%s;', mat2str(double(fileread('cp.bin'))), mat2str(double(fileread('cp.bin', 'Encoding', 'UTF-8'))), "
            + "mat2str(double(fileread('high.bin', 'Encoding', 'windows-1252'))), mat2str(double(fileread('high.bin', 'Encoding', 'ISO-8859-1'))), "
            + "mat2str(double(fileread('u8.bin', 'Encoding', 'US-ASCII'))));\n"
            + "cd(old);");
        Assert.Equal("[233 8364];65533;[8364 129 376];[128 129 159];[65533 65533];", text);
    }

    // --- 306 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("sprintf('%d', \"5\")", "ERR MATLAB:invalidConversion Conversion to int64 from string is not possible.")]
    [InlineData("sprintf('%x', \"a\")", "ERR MATLAB:invalidConversion Conversion to uint64 from string is not possible.")]
    [InlineData("sprintf('%d;', 5, \"x\")", "ERR MATLAB:invalidConversion Conversion to int64 from string is not possible.")]
    [InlineData("sprintf('%c;', \"ab\")", "ERR MATLAB:printf:UnableToConvertToOneElement Unable to convert 'string' element to exactly one element of type 'char'.")]
    [InlineData("sprintf('%c;', \"\")", "ERR MATLAB:printf:UnableToConvertToOneElement Unable to convert 'string' element to exactly one element of type 'char'.")]
    [InlineData("sprintf('%s;', string(missing))", "ERR MATLAB:string:MissingNotSupported <missing> string element not supported.")]
    [InlineData("sprintf('%c;', string(missing))", "ERR MATLAB:invalidConversion Conversion to char from string is not possible.")]
    [InlineData("sprintf('%f;', \"ab\")", "NaN;")]
    [InlineData("sprintf('%g;', \"1.5\")", "1.5;")]
    [InlineData("sprintf('%g;', \" 2 \")", "2;")]
    [InlineData("sprintf('%g;', \"1+2i\")", "1;")]
    [InlineData("sprintf('%f;', string(missing))", "NaN;")]
    [InlineData("sprintf('%c;', [\"a\" \"b\"])", "a;b;")]
    [InlineData("sprintf('%c%c;', \"a\", 66)", "aB;")]
    [InlineData("sprintf('%s %s;', [\"a\" \"b\"])", "a b;")]
    [InlineData("sprintf('%d %s;', 1, \"ab\", 2, \"c\")", "1 ab;2 c;")]
    [InlineData("sprintf('%*d;', \"3\", 5)", "  5;")]
    [InlineData("sprintf('%*d;', \"x\", 5)", "")]
    [InlineData("sprintf('abc', \"x\")", "abc")]
    [InlineData("sprintf(\"%d;\", 5)", "5;")]
    [InlineData("sprintf(\"a\\tb\")", "a\tb")]
    [InlineData("sprintf('%d', 'ab')", "9798")]
    public void Sprintf_TellsAStringFromText(string call, string expected) =>
        Assert.Equal(expected, Run($"try, t = {call}; fprintf('%s', t); catch e, fprintf('ERR %s %s', e.identifier, e.message); end"));

    [Fact]
    public void Fprintf_RefusesAStringUnderANumericConversion()
    {
        Assert.Equal(
            "MATLAB:invalidConversion;ab;",
            Run("try, fprintf('%d\\n', \"5\"); catch e, fprintf('%s;', e.identifier); end\nfprintf(\"%s;\", \"ab\");"));
    }

    [Theory]
    [InlineData("pkg:id", "ERR MATLAB:warning:missingMessageArgument|ERR MATLAB:error:missingMessageArgument")]
    [InlineData("a_b:c_d:e1", "ERR MATLAB:warning:missingMessageArgument|ERR MATLAB:error:missingMessageArgument")]
    [InlineData("pkg:", "pkg:|pkg:")]
    [InlineData("a:1b", "a:1b|a:1b")]
    [InlineData("a-b:c", "a-b:c|a-b:c")]
    [InlineData("a::b", "a::b|a::b")]
    [InlineData("plain text", "plain text|plain text")]
    public void LoneIdentifier_IsRefused(string text, string expected)
    {
        string code =
            $"lastwarn('', ''); try, evalc('warning(''{text}'')'); [m, ~] = lastwarn; fprintf('%s|', m); catch e, fprintf('ERR %s|', e.identifier); end\n"
            + $"try, error('{text}'); catch e, if isempty(e.identifier), fprintf('%s', e.message); else, fprintf('ERR %s', e.identifier); end, end";
        Assert.Equal(expected, Run(code));
    }

    [Fact]
    public void Warning_RefusesAMessageThatIsNotTextAndAStructWithoutState()
    {
        Assert.Equal(
            "MATLAB:warning:badMessageArgument;MATLAB:warning:missingStructureField;",
            Run("try, warning('a:b', 5); catch e, fprintf('%s;', e.identifier); end\n"
                + "try, warning(struct('message', 'm', 'identifier', 'a:b')); catch e, fprintf('%s;', e.identifier); end"));
    }

    [Fact]
    public void WarningTable_PrintsR2025bsLayout()
    {
        string text = Run(
            "warning('on', 'all'); warning('off', 'backtrace'); warning('on', 'oi306:on'); t1 = evalc('warning');\n"
            + "warning('on', 'backtrace'); warning('off', 'oi306:a'); warning('error', 'oi306:b'); t2 = evalc('warning');\n"
            + "warning('off', 'all'); t3 = evalc('warning'); warning('on', 'oi306:c'); t4 = evalc('warning'); warning('on', 'all');\n"
            + "fprintf('%s#', t1, t2, t3, t4);");
        Assert.Equal(
            "All warnings have the state 'on'.\n#"
            + "The default warning state is 'on'. Warnings not set to the default are\n\nState  Warning Identifier\n\n    off  oi306:a\n  error  oi306:b\n\n#"
            + "All warnings have the state 'off'.\n#"
            + "The default warning state is 'off'. Warnings not set to the default are\n\nState  Warning Identifier\n\n     on  oi306:c\n\n#",
            text);
    }

    [Fact]
    public void WarningRaisedAsAnError_ReportsErrorUsingWarning()
    {
        Assert.Equal(
            "Error using warning\nraised 4;Error using warning\nraised 4;",
            Run("warning('error', 'oi306:r'); try, warning('oi306:r', 'raised %d', 4); catch e, fprintf('%s;%s;', getReport(e, 'basic'), getReport(e)); end"));
    }

    [Fact]
    public void EngineWarningRaisedAsAnError_TakesItsStatementsLine()
    {
        string text = Run(
            "warning('error', 'MATLAB:colon:nonIntegerIndex');\n"
            + "try\n"
            + "    oi306_colon(1:10);\n"
            + "catch e\n"
            + "    fprintf('%s %d %s', e.identifier, e.stack(1).line, e.stack(1).name);\n"
            + "end\n"
            + "function y = oi306_colon(x)\n"
            + "y = 0;\n"
            + "y = x(1.5:3);\n"
            + "end\n");
        Assert.Equal("MATLAB:colon:nonIntegerIndex 9 oi306_colon", text);
    }

    [Fact]
    public void ListenerAndDestructorReports_AreSilentInTheErrorState()
    {
        File.WriteAllText(Path.Combine(_root, "Oi306Src.m"), "classdef Oi306Src < handle\n    events\n        Ping\n    end\nend\n");
        File.WriteAllText(Path.Combine(_root, "Oi306Del.m"),
            "classdef Oi306Del < handle\n    methods\n        function delete(~)\n            error('oi306:dtor', 'destructor failed');\n        end\n    end\nend\n");
        string text = Run(
            $"old = cd('{Quoted}');\n"
            + "src = Oi306Src; addlistener(src, 'Ping', @(~, ~) error('oi306:cb', 'callback failed'));\n"
            + "warning('error', 'MATLAB:callback:error'); lastwarn('', ''); out = evalc('notify(src, ''Ping'')'); [m, i] = lastwarn; fprintf('%d[%s];', numel(out), i);\n"
            + "warning('on', 'MATLAB:callback:error'); lastwarn('', ''); out = evalc('notify(src, ''Ping'')'); [m, i] = lastwarn; fprintf('%d[%s];', numel(out) > 0, i);\n"
            + "warning('error', 'MATLAB:class:DestructorError'); lastwarn('', ''); out = evalc('d = Oi306Del; clear d'); [m, i] = lastwarn; fprintf('%d[%s];', numel(out), i);\n"
            + "warning('on', 'MATLAB:class:DestructorError');\n"
            + "cd(old);");
        Assert.Equal("0[];1[MATLAB:callback:error];0[];", text);
    }

    // --- 308 -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("fprintf('a%sb', char(0))", "[97 32 98]")]
    [InlineData("disp(char([97 0 98]))", "[97 32 98 10]")]
    [InlineData("fprintf('%s', char(65535))", "[]")]
    [InlineData("fprintf('a%sb', char(65535))", "[97 65535 98]")]
    [InlineData("fprintf('%sa', char(65535))", "97")]
    [InlineData("fprintf('a'); fprintf('%s', char(65535))", "[97 65535]")]
    [InlineData("fprintf('%s', char([65535 0]))", "32")]
    [InlineData("fprintf('%s', char([0 65535]))", "[32 65535]")]
    [InlineData("disp(char(65535))", "10")]
    public void Evalc_ShowsNulAsABlankAndDropsLeadingFfff(string code, string expected)
    {
        string text = Run($"t = evalc('{code.Replace("'", "''", StringComparison.Ordinal)}'); fprintf('%s', mat2str(double(t)));");
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Console_ShowsNulAsABlankAndAFileKeepsIt()
    {
        string file = Path.Combine(_root, "nul.txt").Replace("'", "''", StringComparison.Ordinal);
        string text = Run($"fprintf('a%sb;', char(0)); f = fopen('{file}', 'w'); fprintf(f, 'a%sb', char(0)); fclose(f);");
        Assert.Equal("a b;", text);
        Assert.Equal(new byte[] { 0x61, 0x00, 0x62 }, File.ReadAllBytes(Path.Combine(_root, "nul.txt")));
    }

    [Fact]
    public void MException_HasAnEmptyCorrection()
    {
        Assert.Equal(
            "double [0 0];double [0 0];double [0 0];identifier,message,cause,stack,Correction;1;",
            Run("try, error('a:b', 'x'); catch e, fprintf('%s %s;', class(e.Correction), mat2str(size(e.Correction))); end\n"
                + "me = MException('c:d', 'built'); fprintf('%s %s;', class(me.Correction), mat2str(size(me.Correction)));\n"
                + "try, throw(me); catch e, fprintf('%s %s;', class(e.Correction), mat2str(size(e.Correction))); end\n"
                + "fprintf('%s;%d;', strjoin(properties(me)', ','), isprop(me, 'Correction'));"));
    }
}
