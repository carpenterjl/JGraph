using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public sealed class MatlabCallSyntaxAuditTests : IDisposable
{
    public MatlabCallSyntaxAuditTests() => JG.Reset();
    public void Dispose() => JG.Reset();

    private static ScriptRunResult Run(string code, JgsDialect? dialect = null)
    {
        var output = new RecordingScriptOutput();
        return JgsRunner.Run(code, new ScriptContext(output, (_, _) => { }), default,
            sourceId: "", hook: null, dialect ?? JgsDialect.Matlab);
    }

    [Theory]
    [InlineData("Z=10+peaks; assert(isequal(Z,10+peaks())); s=surf(peaks); assert(isequal(s.ZData,peaks()));")]
    [InlineData("[X,Y,Z]=peaks; [A,B,C]=peaks(); assert(isequal(X,A)&&isequal(Y,B)&&isequal(Z,C));")]
    [InlineData("f=figure; assert(ishandle(f)); assert(isequal(f,gcf)); close(f);")]
    [InlineData("f=@peaks; g=f; assert(isa(g,'function_handle')); assert(isequal(g(),peaks())); f; assert(isa(f,'function_handle'));")]
    [InlineData("peaks=@peaks; f=peaks; assert(isa(f,'function_handle')); assert(isequal(size(f()),[49 49]));")]
    [InlineData("peaks=7; assert(10+peaks==17); clearvars peaks; assert(isequal(size(peaks),[49 49]));")]
    [InlineData("eps=@eps; f=eps; assert(isa(f,'function_handle'));")]
    [InlineData("peaks=7; f=@peaks; assert(isequal(size(f()),[49 49]));")]
    [InlineData("peaks=@peaks; clearvars peaks; assert(isequal(size(peaks),[49 49]));")]
    [InlineData("f=@()size(peaks); assert(isequal(f(),[49 49]));")]
    public void BareCallsPreserveVariablesAndHandles(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void LocalFunctionsReceiveRequestedOutputsAndHandleParametersStayValues()
    {
        ScriptRunResult result = Run("""
            assert(answer==42);
            [a,b]=pair; assert(a==1&&b==2);
            f=copy(@peaks); assert(isa(f,'function_handle'));
            checkDiscarded;
            function y=answer
                y=42;
            end
            function [a,b]=pair
                a=1; b=2;
            end
            function y=copy(peaks)
                y=peaks;
            end
            function checkDiscarded
                assert(nargout==0);
            end
            """);
        Assert.True(result.Success, result.Message);
    }

    [Theory]
    [InlineData("LineWidth=99; p=plot(1:3,Color='red',LineWidth=2); assert(p.LineWidth==2); assert(LineWidth==99);")]
    [InlineData("p=plot(1:3,'Color','red',LineWidth=2); assert(p.LineWidth==2);")]
    [InlineData("o=odeset(RelTol=1e-4,AbsTol=1e-7); assert(o.RelTol==1e-4&&o.AbsTol==1e-7);")]
    [InlineData("assert(abs(integral(@sin,0,pi,AbsTol=1e-8)-2)<1e-7);")]
    [InlineData("assert(isequal(ones(2,like=single(1)),ones(2,'like',single(1))));")]
    [InlineData("c=collect(12,Label='hello',Value=3); assert(isequal(c,{12,'Label','hello','Value',3})); function y=collect(varargin); y=varargin; end")]
    public void NamedArgumentsReachTheCalleeAsPairsWithoutAssignment(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message);
    }

    [Theory]
    [InlineData("plot(1:3,Color='red','LineWidth',2);")]
    [InlineData("plot(1:3,Color='red',:);")]
    public void PositionalArgumentsCannotFollowModernNamedArguments(string code) =>
        Assert.False(Run(code).Success);

    [Theory]
    [InlineData("keep=3; drop=4; clearvars -except keep; assert(keep==3); assert(exist('drop','var')==0);")]
    [InlineData("A1=1; A2=2; B=3; clearvars('A*','-except','A2'); assert(A2==2&&B==3); assert(exist('A1','var')==0);")]
    [InlineData("b105=1; b106=2; a=3; clearvars('-regexp','^b[0-9]{3}$','-except','b106'); assert(b106==2&&a==3); assert(exist('b105','var')==0);")]
    [InlineData("keep=3; drop=4; clearvars('-except','-regexp','^keep$'); assert(keep==3); assert(exist('drop','var')==0);")]
    [InlineData("base=7; helper(); assert(base==7); function helper(); keep=3; drop=4; clearvars -except keep; assert(keep==3); assert(exist('drop','var')==0); end")]
    public void ClearvarsUsesCurrentWorkspaceAndPreservesExceptions(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void JgsStillAllowsFunctionValuesAndAssignmentArguments()
    {
        ScriptRunResult result = Run("let f = sin; let x = 0; assert(f(x = 0) == 0);", JgsDialect.Jgs);
        Assert.True(result.Success, result.Message);
    }
}
