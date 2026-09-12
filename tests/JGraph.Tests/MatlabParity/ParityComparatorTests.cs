using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.MatlabParity;

/// <summary>
/// The <c>bits</c> rule, proven on this side alone: a whole-array digest that rejects every
/// perturbation a permutation-blind or precision-blind check would pass, resolved from a file the
/// fixture wrote into its own <c>tempname</c> folder, and never satisfied by a file that is not
/// there. The recording of <c>p0_bits_rule</c> proves the other half — that MATLAB writes the same
/// bytes for the same array.
/// </summary>
[Collection("JG facade")]
public class ParityComparatorTests : IDisposable
{
    public ParityComparatorTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private const string Helpers = """

        function bits(name, folder, x)
        p = fullfile(folder, [name '.bits']);
        fid = fopen(p, 'w');
        writebits(fid, x);
        fclose(fid);
        fprintf('CHK|%s|file:%s|bits\n', name, p);
        end

        function writebits(fid, x)
        fprintf(fid, '%s', class(x));
        fprintf(fid, ' %d', size(x));
        fprintf(fid, '\n');
        if ~isfloat(x)
            x = double(x);
        end
        if isreal(x)
            writeplane(fid, x);
        else
            writeplane(fid, real(x));
            writeplane(fid, imag(x));
        end
        end

        function writeplane(fid, v)
        v = v(:);
        n = numel(v);
        chunk = 65536;
        for s = 1:chunk:n
            e = min(n, s + chunk - 1);
            h = num2hex(v(s:e));
            t = [h, repmat(newline, e - s + 1, 1)].';
            fprintf(fid, '%s', t);
        end
        end
        """;

    [Theory]
    [InlineData("double")]
    [InlineData("single")]
    public void BitsRejectsEveryPerturbation(string cls)
    {
        // The base and every perturbation of it that keeps a sum, a count, or a set of values.
        string script = $$"""
            d = tempname; mkdir(d);
            x = {{cls}}([1 2 2 1 0 0.5 -3 NaN]);
            bits('base', d, x);
            bits('base_again', d, x);
            y = x; y(1) = y(1) + eps(y(1)); bits('nextafter', d, y);
            y = x; y([2 5]) = y([5 2]); bits('swap', d, y);
            bits('pair_1221', d, {{cls}}([1 2 2 1]));
            bits('pair_2112', d, {{cls}}([2 1 1 2]));
            bits('reversed', d, x(end:-1:1));
            bits('rotated', d, x([2:end 1]));
            y = x; y(5) = -0; bits('negative_zero', d, y);
            y = x; y(8) = -NaN; bits('nan_payload', d, y);
            bits('reshaped', d, reshape(x, 2, 4));
            bits('transposed', d, x.');
            """ + Helpers;

        Dictionary<string, (string Value, string Rule)> lines = MatlabParityComparer.Parse(
            MatlabParityComparer.ResolveBits(RunMatlabDialect(script)));

        Assert.All(lines.Values, v => Assert.Equal("bits", v.Rule));
        Assert.All(lines.Values, v => Assert.Matches("^[0-9a-f]{64}$", v.Value));
        Assert.Equal(lines["base"].Value, lines["base_again"].Value);
        Assert.NotEqual(lines["pair_1221"].Value, lines["pair_2112"].Value);
        foreach (string name in new[] { "nextafter", "swap", "reversed", "rotated", "negative_zero", "nan_payload", "reshaped", "transposed" })
        {
            Assert.NotEqual(lines["base"].Value, lines[name].Value);
        }
    }

    [Fact]
    public void ResolveDeletesTheFileAndItsTempnameFolder()
    {
        string printed = RunMatlabDialect("d = tempname; mkdir(d); bits('a', d, [1 2 3]); bits('b', d, [4 5 6]); disp(d);" + Helpers);
        string folder = printed.Split('\n').Select(l => l.Trim()).Last(l => l.Length > 0);
        Assert.True(Directory.Exists(folder), folder);
        Assert.Equal(2, Directory.GetFiles(folder).Length);

        string resolved = MatlabParityComparer.ResolveBits(printed);
        Assert.DoesNotContain("file:", resolved);
        Assert.False(Directory.Exists(folder), "the tempname folder should be gone once both files are resolved");
    }

    [Fact]
    public void AMissingFileFailsTheLineAndNothingElseIsTried()
    {
        string missing = Path.Combine(Path.GetTempPath(), "jg_" + Guid.NewGuid().ToString("N"), "x.bits");
        string actual = MatlabParityComparer.ResolveBits($"CHK|a|file:{missing}|bits\n");
        Assert.StartsWith("CHK|a|missing:", actual);
        string problem = Assert.Single(MatlabParityComparer.Compare("CHK|a|" + new string('0', 64) + "|bits\n", actual));
        Assert.Contains("bits file missing", problem);
    }

    [Fact]
    public void ARelativeOrOutsidePathIsMissingAndIsNotDeleted()
    {
        string outside = Path.Combine(AppContext.BaseDirectory, "not_a_bits_file_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "double 1 1\n3ff0000000000000\n");
        try
        {
            string actual = MatlabParityComparer.ResolveBits($"CHK|a|file:{outside}|bits\nCHK|b|file:relative.bits|bits\n");
            Assert.Contains("CHK|a|missing:", actual);
            Assert.Contains("CHK|b|missing:relative.bits|bits", actual);
            Assert.True(File.Exists(outside), "a file outside the temp folder must be left alone");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void AnUnresolvedPathNeverPasses()
    {
        string problem = Assert.Single(MatlabParityComparer.Compare(
            "CHK|a|" + new string('a', 64) + "|bits\n", "CHK|a|file:C:\\somewhere\\a.bits|bits\n"));
        Assert.Contains("not resolved", problem);
        problem = Assert.Single(MatlabParityComparer.Compare("CHK|a|file:x|bits\n", "CHK|a|file:x|bits\n"));
        Assert.Contains("not resolved", problem);
    }

    [Fact]
    public void DigestsCompareAsTextAndOnlyAsDigests()
    {
        string a = new string('a', 64);
        string b = new string('b', 64);
        Assert.Empty(MatlabParityComparer.Compare($"CHK|x|{a}|bits\n", $"CHK|x|{a.ToUpperInvariant()}|bits\n"));
        Assert.Contains("bits", Assert.Single(MatlabParityComparer.Compare($"CHK|x|{a}|bits\n", $"CHK|x|{b}|bits\n")));
        Assert.Contains("not a SHA-256 digest", Assert.Single(MatlabParityComparer.Compare($"CHK|x|{a}|bits\n", "CHK|x|12345|bits\n")));
    }

    [Fact]
    public void TwoFixturesResolvedTogetherKeepTheirOwnFiles()
    {
        // Two runs of the same script get two tempname folders; resolving both at once must hash
        // each one's own file, and the same array must hash to the same digest.
        string first = RunMatlabDialect("d = tempname; mkdir(d); bits('same', d, mod((1:1000)*0.618033988749895, 1));" + Helpers);
        string second = RunMatlabDialect("d = tempname; mkdir(d); bits('same', d, mod((1:1000)*0.618033988749895, 1));" + Helpers);
        Assert.NotEqual(first, second);

        string[] resolved = new string[2];
        Parallel.Invoke(
            () => resolved[0] = MatlabParityComparer.ResolveBits(first),
            () => resolved[1] = MatlabParityComparer.ResolveBits(second));
        Assert.Equal(resolved[0], resolved[1]);
        Assert.Matches(@"^CHK\|same\|[0-9a-f]{64}\|bits", resolved[0].Trim());
    }

    [Fact]
    public void TheWriterProducesTheCanonicalBytes()
    {
        // A file read back before resolution, so the format itself is pinned: header, then one
        // sixteen-digit row per element in storage order, then the imaginary plane.
        string printed = RunMatlabDialect(
            "d = tempname; mkdir(d); bits('m', d, [1 3; 2 4]); bits('c', d, single(1) + 2i); bits('e', d, zeros(0, 2)); disp(d);" + Helpers);
        string folder = printed.Split('\n').Select(l => l.Trim()).Last(l => l.Length > 0);
        Assert.Equal("double 2 2\n3ff0000000000000\n4000000000000000\n4008000000000000\n4010000000000000\n",
            File.ReadAllText(Path.Combine(folder, "m.bits")));
        Assert.Equal("single 1 1\n3f800000\n40000000\n", File.ReadAllText(Path.Combine(folder, "c.bits")));
        Assert.Equal("double 0 2\n", File.ReadAllText(Path.Combine(folder, "e.bits")));
        MatlabParityComparer.ResolveBits(printed);
        Assert.False(Directory.Exists(folder));
    }

    private static string RunMatlabDialect(string code)
    {
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
