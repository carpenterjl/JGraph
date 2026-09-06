using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;
namespace JGraph.Tests.Scripting;
[Collection("JG facade")]
public sealed class MatlabCopiedLegendTests : IDisposable
{
    public MatlabCopiedLegendTests() => JG.Reset();
    public void Dispose() => JG.Reset();
    [Theory]
    [InlineData("a", false)]
    [InlineData("[a lg]", true)]
    [InlineData("[lg a]", true)]
    public void CopyingAxesOnlyCopiesTheLegendWhenRequested(string source, bool expected)
    {
        string script="f=figure; a=axes; plot(1:3); lg=legend('Data'); g=figure; copies=copyobj("+source+",g); close(f);";
        var result=JgsRunner.Run(script,new ScriptContext(new RecordingScriptOutput(),(_,_)=>{}),default,sourceId:"",hook:null,JgsDialect.Matlab);
        Assert.True(result.Success,result.Message);
        Assert.Equal(expected,JG.CurrentFigure.Axes[0].Legend.Visible);
    }
}
