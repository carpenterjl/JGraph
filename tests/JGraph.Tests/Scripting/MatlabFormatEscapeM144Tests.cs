using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The backslash escapes the MATLAB formatting family decodes, and what it does with one it does
/// not recognise: it warns, keeps what the format produced up to that point, and abandons the rest
/// of the format string. JGraph used to pass the backslash through and carry on formatting, so
/// <c>sprintf('B \ C')</c> answered five characters where R2025b answers two (M144, ADR 0148).
/// </summary>
/// <remarks>
/// Every expectation in this file was produced by running the call in R2025b and printing
/// <c>mat2str(double(...))</c> of the answer, not by reading the documentation or reasoning from
/// C's rules — which matters here, because MATLAB's set is not C's: it rejects <c>\%</c>, accepts
/// <c>\"</c> and <c>\'</c> that its quotes never needed, and lets both numeric forms run to as
/// many digits as follow rather than stopping at C's two and three.
/// </remarks>
[Collection("JG facade")]
public class MatlabFormatEscapeM144Tests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabFormatEscapeM144Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    /// <summary>The character codes <paramref name="expression"/> answers, spelled as R2025b spells them.</summary>
    private string Codes(string expression)
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab($"disp(mat2str(double({expression})));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    /// <summary>Everything the run wrote to the error stream — the warnings, in the order they were raised.</summary>
    private string Warnings() => _output.ErrorText.Replace("\r", string.Empty).Trim();

    /// <summary>
    /// The escapes MATLAB accepts, each decoding to one character. The two numeric forms take as
    /// many digits as follow — '\x41B' is U+041B and not 'A' then 'B' — and both stop at 0xFFFF.
    /// </summary>
    [Theory]
    [InlineData("'A\\nB'", "[65 10 66]")]
    [InlineData("'A\\tB'", "[65 9 66]")]
    [InlineData("'A\\rB'", "[65 13 66]")]
    [InlineData("'A\\aB'", "[65 7 66]")]
    [InlineData("'A\\bB'", "[65 8 66]")]
    [InlineData("'A\\fB'", "[65 12 66]")]
    [InlineData("'A\\vB'", "[65 11 66]")]
    [InlineData("'A\\\\B'", "[65 92 66]")]
    [InlineData("'A\\\"B'", "[65 34 66]")]
    [InlineData("'A\\''B'", "[65 39 66]")]
    [InlineData("'A\\x41B'", "[65 1051]")]
    [InlineData("'A\\x41g'", "[65 65 103]")]
    [InlineData("'A\\xaB'", "[65 171]")]
    [InlineData("'A\\xAB'", "[65 171]")]
    [InlineData("'A\\x0Z'", "[65 0 90]")]
    [InlineData("'A\\x4'", "[65 4]")]
    [InlineData("'A\\xFFFFZ'", "[65 65535 90]")]
    [InlineData("'A\\xFFFEZ'", "[65 65534 90]")]
    [InlineData("'A\\x000000000041Z'", "[65 65 90]")]
    [InlineData("'A\\x100B'", "[65 4107]")]
    [InlineData("'A\\101B'", "[65 65 66]")]
    [InlineData("'A\\0B'", "[65 0 66]")]
    [InlineData("'A\\1B'", "[65 1 66]")]
    [InlineData("'A\\12B'", "[65 10 66]")]
    [InlineData("'A\\1234B'", "[65 668 66]")]
    [InlineData("'A\\400B'", "[65 256 66]")]
    [InlineData("'A\\777B'", "[65 511 66]")]
    [InlineData("'A\\177777Z'", "[65 65535 90]")]
    [InlineData("'A\\177776Z'", "[65 65534 90]")]
    [InlineData("'A\\08Z'", "[65 0 56 90]")]
    [InlineData("'A\\00000000000101Z'", "[65 65 90]")]
    public void AcceptedEscapeDecodesToOneCharacter(string format, string expected)
    {
        Assert.Equal(expected, Codes($"sprintf({format})"));
        Assert.Equal(string.Empty, Warnings());
    }

    /// <summary>
    /// An escape MATLAB does not recognise: the format ends there, what came before it stands, and
    /// the warning names the fault. The four kinds are distinguished — a bad escape, a lone trailing
    /// backslash, a digit the numeric form cannot use, and a value past the end of the character set.
    /// </summary>
    [Theory]
    [InlineData("'B \\ C'", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\ B'", "65", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\%B'", "65", "Escaped character '\\%' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\qB'", "65", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\eB'", "65", "Escaped character '\\e' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\zB'", "65", "Escaped character '\\z' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'\\z'", "[]", "Escaped character '\\z' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\<B'", "65", "Escaped character '\\<' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\NB'", "65", "Escaped character '\\N' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\TB'", "65", "Escaped character '\\T' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\XB'", "65", "Escaped character '\\X' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\X41B'", "65", "Escaped character '\\X' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\'", "65", "A lone trailing backslash, '\\' , is not a valid control character. See 'doc sprintf' for control characters valid in the format string.")]
    [InlineData("'A\\xZZ'", "65", "Valid hexadecimal digits are 0-9 and A-F.")]
    [InlineData("'A\\x'", "65", "Valid hexadecimal digits are 0-9 and A-F.")]
    [InlineData("'A\\x%dZ'", "65", "Valid hexadecimal digits are 0-9 and A-F.")]
    [InlineData("'A\\8B'", "65", "Valid octal digits are 0-7.")]
    [InlineData("'A\\9B'", "65", "Valid octal digits are 0-7.")]
    [InlineData("'A\\x10000Z'", "65", "The hex value specified is outside the range of the character set.")]
    [InlineData("'A\\x1FFFFFFFFFFFFFFFFZ'", "65", "The hex value specified is outside the range of the character set.")]
    [InlineData("'A\\200000Z'", "65", "The octal value specified is outside the range of the character set.")]
    [InlineData("'A\\11111111111111111111Z'", "65", "The octal value specified is outside the range of the character set.")]
    public void UnrecognisedEscapeEndsTheFormatAndWarns(string format, string expected, string warning)
    {
        Assert.Equal(expected, Codes($"sprintf({format})"));
        Assert.Equal("Warning: " + warning, Warnings());
    }

    /// <summary>
    /// Truncation meeting the format's own repetition. MATLAB stops <em>each pass</em> at the bad
    /// escape rather than the whole call, so '[%d]\q[%d]' over four values is '[1][2][3][4]' — four
    /// short passes — and the warning is raised once for the call however many passes there were.
    /// </summary>
    [Theory]
    [InlineData("sprintf('%d\\qX', 5)", "53", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('%d\\q%d', 5, 6)", "[53 54]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('[%d]\\q[%d]', [1 2 3 4])", "[91 49 93 91 50 93 91 51 93 91 52 93]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('\\q%d', 5)", "[]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('%s\\q', 'hi')", "[104 105]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('%d\\q', [1 2 3])", "[49 50 51]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('%d%%\\q%d', 5, 6)", "[53 37 54 37]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('%5.2f\\q', pi)", "[32 51 46 49 52]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('a\\qb\\nc')", "97", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("sprintf('%s', 'a\\qb')", "[97 92 113 98]", "")]
    public void TruncationStopsEachPassAndWarnsOnce(string call, string expected, string warning)
    {
        Assert.Equal(expected, Codes(call));
        Assert.Equal(warning.Length == 0 ? string.Empty : "Warning: " + warning, Warnings());
    }

    /// <summary>
    /// <c>error</c> and <c>assert</c> read their message as a format only when data follows it or an
    /// identifier came before it. A message that is not a format is used exactly as written, which is
    /// why <c>error('B \n C')</c> keeps its backslash where <c>error('my:id', 'B \n C')</c> breaks
    /// the line. When it is a format it takes sprintf's escapes, sprintf's repetition, and
    /// sprintf's answer to a bad escape.
    /// </summary>
    [Theory]
    [InlineData("error('B \\ C')", "[66 32 92 32 67]", "")]
    [InlineData("error('B \\n C')", "[66 32 92 110 32 67]", "")]
    [InlineData("error('B \\ C %d')", "[66 32 92 32 67 32 37 100]", "")]
    [InlineData("error('B \\ C %d', 5)", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("error('B \\n C %d', 5)", "[66 32 10 32 67 32 53]", "")]
    [InlineData("error('my:id', 'B \\ C')", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("error('my:id', 'B \\n C')", "[66 32 10 32 67]", "")]
    [InlineData("error('a%d ', 1, 2)", "[97 49 32 97 50 32]", "")]
    [InlineData("error('a%d %d', 1)", "[97 49 32]", "")]
    [InlineData("error('B \\ C', 5, 6)", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("assert(false, 'B \\ C')", "[66 32 92 32 67]", "")]
    [InlineData("assert(false, 'B \\ C %d', 5)", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("assert(false, 'a%d ', 1, 2)", "[97 49 32 97 50 32]", "")]
    public void MessageIsAFormatOnlyWhenSomethingSaysSo(string call, string expected, string warning)
    {
        string code = $"try{Environment.NewLine}  {call};{Environment.NewLine}catch e{Environment.NewLine}  disp(mat2str(double(e.message)));{Environment.NewLine}end";
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(expected, _output.NormalText.Trim());
        Assert.Equal(warning.Length == 0 ? string.Empty : "Warning: " + warning, Warnings());
    }

    /// <summary>
    /// <c>MException</c> always had an identifier in front of its message, so the message is a format
    /// even with no data after it — which is the same rule <c>error</c> follows once an identifier is
    /// present, and not the one it follows without one.
    /// </summary>
    [Theory]
    [InlineData("MException('a:b', 'B \\ C %d', 5)", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("MException('a:b', 'B \\ C')", "[66 32]", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("MException('a:b', 'B \\n C')", "[66 32 10 32 67]", "")]
    public void MExceptionAlwaysReadsItsMessageAsAFormat(string call, string expected, string warning)
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab($"m = {call};" + Environment.NewLine + "disp(mat2str(double(m.message)));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(expected, _output.NormalText.Trim());
        Assert.Equal(warning.Length == 0 ? string.Empty : "Warning: " + warning, Warnings());
    }

    /// <summary>
    /// <c>warning</c> follows the same rule, and the escape complaint it raises on the way does not
    /// become the warning <c>lastwarn</c> reports: the call's own message is recorded after it is
    /// issued, so the inner one cannot displace it.
    /// </summary>
    [Theory]
    [InlineData("warning('B \\ C')", "B \\ C")]
    [InlineData("warning('B \\n C')", "B \\n C")]
    [InlineData("warning('a%d ', 1, 2)", "a1 a2 ")]
    public void WarningIsIssuedAndRecorded(string call, string expected)
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab($"{call};" + Environment.NewLine + "disp(lastwarn);");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("Warning: " + expected, _output.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>compose</c> is the one member of the family that raises MATLAB's printf <em>error</em>
    /// instead of warning and carrying on with a shortened format. That is measured, not inferred,
    /// and it is why the identifiers here are MATLAB's own rather than invented ones (ADR 0062).
    /// </summary>
    [Theory]
    [InlineData("'B \\ C'", "", "MATLAB:printf:BadEscapeSequenceInFormat", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'A\\'", "", "MATLAB:printf:NoControlCharacterInFormat", "A lone trailing backslash, '\\' , is not a valid control character. See 'doc sprintf' for control characters valid in the format string.")]
    [InlineData("'A\\xZ'", "", "MATLAB:printf:HexCharCodeInvalid", "Valid hexadecimal digits are 0-9 and A-F.")]
    [InlineData("'A\\8B'", "", "MATLAB:printf:OctalCharCodeInvalid", "Valid octal digits are 0-7.")]
    [InlineData("'A\\x10000Z'", "", "MATLAB:printf:HexCharCodeOutOfRange", "The hex value specified is outside the range of the character set.")]
    [InlineData("'A\\200000Z'", "", "MATLAB:printf:OctalCharCodeOutOfRange", "The octal value specified is outside the range of the character set.")]
    [InlineData("'A\\nB'", "[65 10 66]", "", "")]
    [InlineData("'A\\x41Z'", "[65 65 90]", "", "")]
    public void ComposeRaisesTheFaultInsteadOfWarning(string format, string codes, string identifier, string message)
    {
        _output.Normal.Clear();
        string code = "try" + Environment.NewLine
            + $"  c = compose({format});" + Environment.NewLine
            + "  disp(mat2str(double(char(c))));" + Environment.NewLine
            + "catch e" + Environment.NewLine
            + "  disp([e.identifier ' | ' e.message]);" + Environment.NewLine
            + "end";
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        string expected = identifier.Length == 0 ? codes : identifier + " | " + message;
        Assert.Equal(expected, _output.NormalText.Trim());
        Assert.Equal(string.Empty, Warnings());
    }

    /// <summary>
    /// <c>sscanf</c> shares the format reader and so shares the rule: it warns and reads on with the
    /// format cut short, which a repeating scan format survives.
    /// </summary>
    [Theory]
    [InlineData("'12 34'", "'%d\\q%d'", "[12 34]", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("'12 34'", "'%d\\n%d'", "[12 34]", "")]
    public void SscanfWarnsAndReadsOnWithTheShorterFormat(string text, string format, string expected, string warning)
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab($"v = sscanf({text}, {format});"
            + Environment.NewLine + "disp(mat2str(v(:).'));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(expected, _output.NormalText.Trim());
        Assert.Equal(warning.Length == 0 ? string.Empty : "Warning: " + warning, Warnings());
    }

    /// <summary>
    /// <c>fprintf</c> answers how many characters it actually wrote, which the truncation shortens —
    /// <c>fprintf('B \ C')</c> writes two and says two.
    /// </summary>
    [Theory]
    [InlineData("fprintf('B \\ C')", "2", "Escaped character '\\ ' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("fprintf('[%d]\\qX', [1 2 3])", "9", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    [InlineData("fprintf('%d\\q%d', 5, 6)", "2", "Escaped character '\\q' is not valid. See 'doc sprintf' for supported special characters.")]
    public void FprintfCountsWhatItWrote(string call, string expected, string warning)
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab($"n = {call};" + Environment.NewLine + "disp(num2str(n));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.EndsWith(expected, _output.NormalText.Trim(), StringComparison.Ordinal);
        Assert.Equal("Warning: " + warning, Warnings());
    }
}
