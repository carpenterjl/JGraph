using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;
namespace JGraph.Tests.Scripting;
[Collection("JG facade")]
public sealed class MatlabPeaksSurfaceAuditTests : IDisposable
{
    public MatlabPeaksSurfaceAuditTests() => JG.Reset();
    public void Dispose() => JG.Reset();
    [Fact]
    public void DurationRangeReachesTheRenderedRuler()
    {
        MatchesMatlab("t=0:seconds(30):minutes(3); plot(t,1:7,DurationTickFormat='mm:ss');");
        var axis = JG.Gca().PrimaryXAxis;
        Assert.Equal("mm:ss", axis.DurationFormat);
        var ticks = JGraph.Maths.Ticks.TickGenerators.For(axis).Generate(new JGraph.Core.Primitives.DataRange(0,180.0/86400),6);
        Assert.Contains(ticks.MajorTicks,t => t.Label == "01:40");
    }
    [Theory]
    [InlineData("Z=peaks; assert(isequal(size(Z),[49 49]));")]
    [InlineData("v=-1:.25:1; [X,Y]=meshgrid(v); assert(norm(peaks(v)-peaks(X,Y),'fro')<1e-12);")]
    [InlineData("v=(-2:2)'; assert(isequal(peaks(v),peaks(v')));")]
    [InlineData("x=-2:2; y=(-1:1)'; [X,Y]=meshgrid(x,y); assert(norm(peaks(x,y)-peaks(X,Y),'fro')<1e-12);")]
    [InlineData("x=[-1 0 1]; y=[2 1 0]; expected=3*(1-x).^2.*exp(-x.^2-(y+1).^2)-10*(x/5-x.^3-y.^5).*exp(-x.^2-y.^2)-exp(-(x+1).^2-y.^2)/3; assert(norm(peaks(x,y)-expected)<1e-12);")]
    [InlineData("assert(abs(peaks(0,0)-(3-1/3)*exp(-1))<1e-12);")]
    [InlineData("x=-2:2; y=(-1:1)'; [X,Y,Z]=peaks(x,y); assert(isequal(X,x)); assert(isequal(Y,y)); assert(isequal(size(Z),[3 5]));")]
    [InlineData("peaks(5); ax=gca; assert(numel(ax.Children)==1); assert(strcmp(ax.Children(1).Type,'surface'));")]
    [InlineData("peaks(-2:2); ax=gca; assert(isequal(size(ax.Children(1).ZData),[5 5]));")]
    [InlineData("Z=peaks(5); ax=gca; assert(isempty(ax.Children));")]
    [InlineData("f=@peaks; Z=f(5); assert(isequal(Z,peaks(5)));")]
    [InlineData("C=zeros(3,4,3); C(:,:,2)=.5; s=surf(ones(3,4),C); assert(isequal(s.CData,C));")]
    [InlineData("C=uint8(zeros(3,4,3)); C(:,:,1)=255; s=surf(ones(3,4),C); assert(isequal(s.CData,C)); assert(isa(s.CData,'uint8'));")]
    [InlineData("s=surf(ones(3)); C=zeros(3,3,3); C(:,:,3)=1; s.CData=C; assert(isequal(s.CData,C)); s.CData=2*ones(3); assert(isequal(s.CData,2*ones(3)));")]
    [InlineData("s=surf(peaks(5),'FaceAlpha',.5); assert(s.FaceAlpha==.5);")]
    [InlineData("s=surf(peaks(5)); s.EdgeColor='none'; assert(strcmp(s.EdgeColor,'none'));")]
    [InlineData("s=surf(peaks(5),FaceAlpha=.25,EdgeColor='none'); assert(s.FaceAlpha==.25); assert(strcmp(s.EdgeColor,'none'));")]
    [InlineData("[X,Y,Z]=peaks(5); C=zeros(5,5,3); C(:,:,1)=1; s=surf(X,Y,Z,C); assert(isequal(s.CData,C));")]
    [InlineData("[C,h]=contour(peaks(7),'--'); assert(strcmp(h.LineStyle,'--')); assert(size(C,1)==2);")]
    [InlineData("[C,h]=contour(peaks(7),'ShowText','on'); assert(strcmp(h.ShowText,'on'));")]
    [InlineData("[C,h]=contour(peaks(7),[-4 0 2],ShowText=true,LabelFormat='%0.1f m'); assert(strcmp(h.LabelFormat,'%0.1f m'));")]
    [InlineData("[C,h]=contour(peaks(7),[-4 0 2],ShowText=true,LabelFormat=@(v) string(v)+\" m\");")]
    [InlineData("C=contour(peaks(7)); assert(isnumeric(C)); assert(size(C,1)==2);")]
    [InlineData("Y=[1 2;3 4;5 6]; h=stem(Y); assert(numel(h)==2); assert(isequal(h(1).YData,Y(:,1)')); assert(isequal(h(2).YData,Y(:,2)'));")]
    [InlineData("Y=[1 2;3 4;5 6]; h=stem([2 4 8],Y,'filled','LineWidth',2); assert(numel(h)==2); assert(isequal(h(2).XData,[2 4 8])); assert(h(2).LineWidth==2);")]
    [InlineData("X=[1 2;3 4;5 6]; Y=2*X; h=stem(X,Y); assert(numel(h)==2); assert(isequal(h(2).XData,X(:,2)'));")]
    [InlineData("hold on; stem([1 2;3 4]); assert(ishold); assert(numel(get(gca,'Children'))==2);")]
    [InlineData("t=0:seconds(30):minutes(3); plot(t,1:7,'DurationTickFormat','mm:ss'); drawnow;")]
    [InlineData("plot(1:3,seconds([0 30 60]),DurationTickFormat='mm:ss'); drawnow;")]
    [InlineData("peaks; assert(numel(get(gca,'Children'))==1);")]
    [InlineData("[X,Y]=meshgrid(-2:2,-1:1); peaks(X,Y); ax=gca; assert(isequal(size(ax.Children(1).ZData),[3 5]));")]
    [InlineData("peaks(-2:2,(-1:1)'); ax=gca; assert(isequal(size(ax.Children(1).ZData),[3 5]));")]
    [InlineData("assert(isequal(peaks(ones(2)),peaks(ones(1,4))));")]
    [InlineData("failed=false; try; Z=peaks(ones(2,3),ones(4,2)); catch; failed=true; end; assert(failed);")]
    [InlineData("s=surf(ones(3),zeros(3,3,3)); s.CData(:,:,2)=1; C=zeros(3,3,3); C(:,:,2)=1; assert(isequal(s.CData,C));")]
    [InlineData("C=uint16(zeros(3,3,3)); C(:,:,3)=65535; s=surf(ones(3),C); assert(isequal(s.CData,C));")]
    [InlineData("s=surf(ones(3),FaceAlpha=0); assert(s.FaceAlpha==0); s.FaceAlpha=1; assert(s.FaceAlpha==1);")]
    [InlineData("[C,h]=contour(peaks(7),[-2 0 2],'r--',LineWidth=2); assert(strcmp(h.LineStyle,'--')); assert(h.LineWidth==2); assert(isequal(h.LineColor,[1 0 0]));")]
    [InlineData("stem([1 2;3 4]); assert(~ishold); assert(numel(get(gca,'Children'))==2);")]
    [InlineData("plot(seconds([-90 0 90]),[1 2 3],DurationTickFormat='mm:ss'); drawnow;")]
    [InlineData("plot(seconds([0 .1 .2]),[1 2 3],DurationTickFormat='hh:mm:ss.SSS'); drawnow;")]
    [InlineData("t=0:seconds(30):minutes(3); assert(isduration(t)); assert(isequal(seconds(t),0:30:180));")]
    public void MatchesMatlab(string code)
    {
        var result = JgsRunner.Run(code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default,
            sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
