using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;
namespace JGraph.Tests.Scripting;
[Collection("JG facade")]
public sealed class MatlabHandlesLifecycleAuditTests : IDisposable
{
    public MatlabHandlesLifecycleAuditTests() => JG.Reset();
    public void Dispose() => JG.Reset();
    [Theory]
    [InlineData("t=title('Response'); assert(isgraphics(t,'text')); t.Color='red'; assert(isequal(t.Color,[1 0 0])); assert(strcmp(t.String,'Response'));")]
    [InlineData("t=title('First'); u=title('Second'); assert(isequal(t,u)); a=gca; assert(isequal(a.Title,t)); assert(strcmp(t.String,'Second'));")]
    [InlineData("[t,s]=title('Response','Load case 2','Color','blue'); t.FontSize=16; s.FontAngle='italic'; assert(isequal(s.Color,[0 0 1])); assert(strcmp(s.String,'Load case 2')); assert(strcmp(s.FontAngle,'italic'));")]
    [InlineData("s=subtitle('Case'); a=gca; assert(isequal(s,a.Subtitle)); s.String='Other'; assert(strcmp(a.Subtitle.String,'Other'));")]
    [InlineData("t=xlabel('Time'); t.FontWeight='bold'; assert(strcmp(t.FontWeight,'bold')); a=gca; assert(isequal(a.XLabel,t));")]
    [InlineData("t=ylabel(123); assert(strcmp(t.String,'123'));")]
    [InlineData("surf(peaks(5)); t=zlabel(123); t.Color='red'; assert(strcmp(t.String,'123'));")]
    [InlineData("t=ylabel({2010;'Population';'in Years'}); assert(isequal(t.String,{'2010';'Population';'in Years'}));")]
    [InlineData("t=ylabel('Amplitude','Rotation',0); assert(t.Rotation==0); t.Rotation=45; assert(t.Rotation==45);")]
    [InlineData("a=subplot(2,1,1); b=subplot(2,1,2); t=xlabel(a,'Time'); assert(isequal(gca,b)); assert(isequal(a.XLabel,t));")]
    [InlineData("a=axes; a.Title.String='Nested'; a.Title.Color='red'; assert(strcmp(a.Title.String,'Nested')); assert(isequal(a.Title.Color,[1 0 0]));")]
    [InlineData("imagesc(magic(3)); c=colorbar; c.Label.String='Temperature'; c.Label.Color='red'; assert(isgraphics(c.Label,'text')); assert(strcmp(c.Label.String,'Temperature')); assert(isequal(c.Label.Color,[1 0 0]));")]
    [InlineData("f=figure; t=title('A'); close(f); assert(~isgraphics(t));")]
    [InlineData("f=figure; t=title('A'); g=figure; close(g); assert(isgraphics(t)); t.String='B'; assert(strcmp(t.String,'B'));")]
    [InlineData("d=get(groot,'default'); assert(isstruct(d));")]
    [InlineData("assert(get(groot,'factoryLineMarkerSize')==6); p=plot(1:3,'Color','red'); assert(isequal(get(p,'defaultLineColor'),get(groot,'factoryLineColor')));")]
    [InlineData("p=plot(magic(4)); set(p,{'LineStyle'},{'-';'--';':';'-.'}); assert(strcmp(p(2).LineStyle,'--')); assert(strcmp(p(4).LineStyle,'-.'));")]
    [InlineData("p=stem([1 2;3 4]); set(p,{'Marker','Tag'},{'o','one';'square','two'}); assert(strcmp(p(1).Tag,'one')); assert(strcmp(p(2).Marker,'square')); assert(strcmp(p(2).Tag,'two'));")]
    [InlineData("p=plot([1 2;3 4]); set(p,{'Tag'},{'a';'b'}); v=get(p,{'Tag','Type'}); assert(isequal(size(v),[2 2])); assert(strcmp(v{2,1},'b')); assert(strcmp(v{1,2},'line'));")]
    [InlineData("p=plot([1 2;3 4]); v=get(p,'Type'); assert(isequal(size(v),[2 1]));")]
    [InlineData("p=plot(1:3); s.Color='red'; s.LineWidth=2; set(p,s); assert(p.LineWidth==2); assert(isequal(p.Color,[1 0 0]));")]
    [InlineData("f=figure('Color','red'); plot(1:3); clf(f); assert(isempty(f.Children)); assert(isequal(f.Color,[1 0 0]));")]
    [InlineData("f=figure('Color','red','Name','Test','Tag','tag'); f.Position=[100 100 500 400]; p=f.Position; plot(1:3); g=clf(f,'reset'); assert(isequal(g,f)); assert(isempty(f.Children)); assert(isequal(f.Position,p)); assert(isempty(f.Tag));")]
    [InlineData("f=figure; plot(1:3); g=figure; plot(1:5); clf(f); assert(isequal(gcf,g)); assert(isempty(f.Children)); assert(~isempty(g.Children));")]
    [InlineData("f=figure; a=axes('HandleVisibility','off'); clf(f); assert(isgraphics(a)); clf(f,'reset'); assert(~isgraphics(a));")]
    [InlineData("f=figure; g=figure; h=figure; s=close([f g]); assert(s==1); assert(~isgraphics(f)); assert(~isgraphics(g)); assert(isgraphics(h));")]
    [InlineData("f=figure('Name','Data'); g=figure('Name','Data'); h=figure('Name','Other'); assert(close('Data')==1); assert(~isgraphics(f)); assert(~isgraphics(g)); assert(isgraphics(h));")]
    [InlineData("f=figure('CloseRequestFcn',''); assert(close(f)==0); assert(isgraphics(f)); assert(close(f,'force')==1); assert(~isgraphics(f));")]
    [InlineData("f=figure('CloseRequestFcn',@(src,event)delete(src)); assert(close(f)==1); assert(~isgraphics(f));")]
    [InlineData("f=figure('CloseRequestFcn',@(src,event)disp('retained')); assert(close(f)==0); assert(isgraphics(f)); close(f,'force');")]
    [InlineData("f=figure('CloseRequestFcn','closereq'); assert(close(f)==1); assert(~isgraphics(f));")]
    [InlineData("f=figure('HandleVisibility','off'); g=figure; close all; assert(isgraphics(f)); assert(~isgraphics(g)); close all hidden; assert(~isgraphics(f));")]
    [InlineData("a=subplot(2,1,1); b=subplot(2,1,2); hold([a b],'on'); assert(ishold(a)&&ishold(b)); hold([a b],'off'); assert(~ishold(a)&&~ishold(b)); assert(isequal(gca,b));")]
    [InlineData("a=subplot(2,1,1); b=subplot(2,1,2); axis([a b],[0 10 -1 1]); assert(isequal(a.XLim,[0 10])); assert(isequal(b.YLim,[-1 1])); assert(isequal(gca,b));")]
    [InlineData("a=subplot(2,1,1); surf(a,peaks(5)); b=subplot(2,1,2); surf(b,peaks(5)); clim(a,[-2 3]); assert(isequal(a.CLim,[-2 3])); assert(isequal(gca,b));")]
    [InlineData("a=subplot(2,1,1); b=subplot(2,1,2); clim([a b],[0 5]); assert(isequal(a.CLim,[0 5])); assert(isequal(b.CLim,[0 5]));")]
    [InlineData("plot(1:3,[2 3 4]); ylim padded; v=ylim; assert(v(1)<2&&v(2)>4); ylim tight; assert(isequal(ylim,[2 4]));")]
    [InlineData("a=subplot(2,2,[3 4]); p=a.Position; assert(abs(p(1)-.13)<1e-8); assert(abs(p(2)-.11)<1e-8); assert(abs(p(3)-.775)<1e-8);")]
    [InlineData("a=subplot('Position',[.1 .3 .3 .3]); assert(max(abs(a.Position-[.1 .3 .3 .3]))<1e-10);")]
    [InlineData("a=subplot(2,1,1); p=plot(1:3); b=subplot(2,1,2); subplot(a); assert(isequal(gca,a)); assert(isgraphics(p));")]
    [InlineData("a=subplot(2,1,1); p=plot(1:3); b=subplot(2,1,1); assert(isequal(a,b)); assert(isgraphics(p));")]
    [InlineData("a=subplot(2,1,1); p=plot(1:3); b=subplot(2,1,1,'replace'); assert(~isgraphics(a)); assert(~isgraphics(p)); assert(isgraphics(b));")]
    [InlineData("a=subplot(2,2,3); b=subplot(2,2,4); c=subplot(2,2,[3 4]); assert(~isgraphics(a)); assert(~isgraphics(b)); assert(isgraphics(c));")]
    [InlineData("a=axes; p=plot(1:3); subplot(2,1,2,a); assert(isgraphics(p)); assert(isequal(gca,a)); assert(abs(a.Position(2)-.11)<1e-8);")]
    [InlineData("a=subplot(2,1,1,polaraxes); polarplot(a,0:.1:1,0:.1:1); assert(isgraphics(a,'polaraxes'));")]
    [InlineData("f=figure; a=axes; p=plot(1:3); g=figure; subplot(2,1,1,a,'Parent',g); assert(isequal(a.Parent,g)); assert(isgraphics(p)); assert(isempty(f.Children));")]
    [InlineData("f=figure; a=axes; plot(1:3); title('Original'); g=figure; b=copyobj(a,g); subplot(2,1,1,b); assert(isequal(b.Parent,g)); assert(strcmp(b.Title.String,'Original')); assert(isgraphics(a));")]
    [InlineData("a=subplot(2,1,1,'Tag','top'); assert(strcmp(a.Tag,'top'));")]
    [InlineData("f=figure; a=axes; p=plot(1:3); g=figure; subplot(2,1,1,a); assert(isequal(a.Parent,f)); assert(isequal(gcf,g)); assert(isgraphics(p));")]
    [InlineData("f=figure; a=axes; plot(1:3); clf; p=plot(1:4); assert(isgraphics(p)); assert(~isgraphics(a)); assert(numel(f.Children)==1);")]
    [InlineData("s=get(groot,'factory'); assert(isstruct(s)); assert(s.factoryLineMarkerSize==6);")]
    [InlineData("f=figure; plot(1:3); g=figure; plot(1:4); clf([f g]); assert(isempty(f.Children)); assert(isempty(g.Children)); assert(isequal(gcf,g));")]
    [InlineData("a=axes; t=xlabel('Time'); assert(isequal(t.Parent,a));")]
    [InlineData("f=figure; a=axes; g=figure; title(a,'Named'); assert(isequal(gcf,g)); assert(isempty(g.Children));")]
    [InlineData("axis([0 5 -2 2]); assert(isequal(axis,[0 5 -2 2]));")]
    [InlineData("plot(1:3); axis manual; a=gca; assert(strcmp(a.XLimMode,'manual')); axis auto; assert(strcmp(a.XLimMode,'auto')); axis ij; assert(strcmp(a.YDir,'reverse')); axis xy; assert(strcmp(a.YDir,'normal'));")]
    public void MatchesMatlab(string code)
    {
        var result = JgsRunner.Run(code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default,
            sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
