using System.Globalization;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>Behavioral regressions for the first six performance recommendations.</summary>
[Collection("JG facade")]
public class FirstSixRecommendationsTests : IDisposable
{
    public FirstSixRecommendationsTests() => JG.Reset();
    public void Dispose() => JG.Reset();

    private static async Task Run(string code)
    {
        var output = new RecordingScriptOutput();
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(output, (_, _) => { }));
        var result = await session.ExecuteAsync(code, "", CancellationToken.None);
        Assert.True(result.Success, result.Message + output.ErrorText);
    }

    [Fact]
    public Task SelectionPreservesOwnershipMissingValuesAndEvenAveraging() => Run("""
        x = [100:-1:1 NaN]; original = x;
        assert(isnan(median(x)));
        assert(median(x,'omitnan') == 50.5);
        assert(isequaln(x,original));
        assert(median([realmax realmax]) == realmax);
        assert(median(ones(1,1001)) == 1);
        assert(median([101:-1:1]) == 51);
        assert(isequal(median([1 9; 3 7],1),[2 8]));
        assert(isequal(median([1 9; 3 7],2),[5;5]));
        x = [ones(1,100) 100]; saved = x;
        flags = isoutlier(x);
        assert(sum(flags) == 1 && flags(end));
        assert(isequal(x,saved));
        """);

    [Fact]
    public Task BorrowedWindowsNeverAliasInputOrOutput() => Run("""
        x = 1:1001; old = x;
        a = movmean(x,51); b = movstd(x,51);
        c = movmedian(x,21); d = movmax(x,101);
        assert(isequal(x,old));
        a(1) = -999; assert(x(1) == 1);
        x(2) = -100; assert(c(2) == 6.5 && d(2) == 52);
        q = (1:20)'; z = movsum(q,3,'Endpoints','discard');
        assert(isequal(size(z),[18 1])); assert(isequal(z,(6:3:57)'));
        assert(isequal(q,(1:20)'));
        q = reshape(1:24,[2 3 4]); old = q;
        z = movmean(q,1,3); assert(isequal(z,q));
        z(1)=999; assert(isequal(q,old));
        q=[1 NaN 3 4]; old=q;
        z=movmean(q,3,'omitnan'); assert(isequaln(q,old));
        assert(isequal(z,[1 2 3.5 3.5]));
        z=movmean(1:4,3,'SamplePoints',1:4);
        assert(isequal(z,[1.5 2 3 3.5]));
        """);

    [Fact]
    public Task ClassedReductionsPreserveClassAndSaturatingScans() => Run("""
        x=uint8([200 100; 1 2]); old=x;
        assert(isequal(sum(x,1),[201 102]));
        assert(strcmp(class(sum(x)), 'double'));
        assert(isequal(sum(x,2,'native'),uint8([255;3])));
        assert(isequal(cumsum(uint8([200 100 1])),uint8([200 255 255])));
        assert(isequal(prod(uint8([20 20]),'native'),uint8(255)));
        assert(isequal(mean(x,1),[100.5 51]));
        assert(isequal(max(x,[],2),uint8([200;2])));
        assert(isequal(x,old));
        assert(strcmp(class(sum(single([1 2 3]))),'single'));
        assert(sum(single([1 2 3])) == 6);
        """);

    [Fact]
    public Task UniformLookupHandlesKnotsExtremeScalesAndIrregularFallback() => Run("""
        x=linspace(0,1,101); y=1:101;
        assert(isequal(interp1(x,y,x,'nearest'),y));
        assert(isequal(interp1(x,y,x,'previous'),y));
        assert(isequal(interp1(x,y,x,'next'),y));
        assert(isequal(interp1([0 1e-310 2e-310],[10 20 30],[0 1e-310 2e-310],'nearest'),[10 20 30]));
        assert(isequal(interp1([-1e308 0 1e308],[10 20 30],[-1e308 0 1e308],'nearest'),[10 20 30]));
        x=[0 1 1.01 7 100]; y=[10 20 30 40 50];
        assert(isequal(interp1(x,y,x,'nearest'),y));
        assert(isequal(interp1([0 1 2],[0 2 4],[-1e100 1e100],'linear','extrap'),[-2e100 2e100]));
        assert(isnan(interp1([0 1],[0 1],NaN)));
        """);

    [Fact]
    public async Task RegexCacheSeparatesCultureAndDoesNotLoseHotPatternsAtCapacity()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            await Run("assert(isequal(regexpi('I','i'),1));");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            await Run("assert(isempty(regexpi('I','i')));");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            await Run("""
                for k=1:300
                    regexp('abc',sprintf('x%d',k));
                    assert(isequal(regexp('abc','b'),2));
                end
                assert(isequal(regexp('abc','^','emptymatch'),1));
                assert(isempty(regexp('abc','^')));
                """);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
