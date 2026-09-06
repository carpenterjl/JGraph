using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;
namespace JGraph.Tests.Scripting;
[Collection("JG facade")]
public sealed class MatlabArrayImageAuditTests : IDisposable
{
    public MatlabArrayImageAuditTests() => JG.Reset();
    public void Dispose() => JG.Reset();
    [Theory]
    [InlineData("A(:,:,1)=[1 2;3 4]; A(:,:,3)=5; assert(isequal(size(A),[2 2 3])); assert(isequal(A(:,:,1),[1 2;3 4])); assert(isequal(A(:,:,2),zeros(2)));")]
    [InlineData("A=reshape(1:8,2,2,2); A(3,4,3)=9; assert(isequal(size(A),[3 4 3])); assert(isequal(A(1:2,1:2,1:2),reshape(1:8,2,2,2))); assert(A(3,3,2)==0);")]
    [InlineData("A=[]; A(:,1)=[4;5]; assert(isequal(A,[4;5]));")]
    [InlineData("x([3 1 2],:)=[8;9;10]; assert(isequal(x,[9;10;8]));")]
    [InlineData("A=ones(2,2,2); A(:,:,end+1)=3; assert(isequal(A(:,:,3),3*ones(2)));")]
    [InlineData("S=sparse([1 2 2],[2 1 1],3,3,3); assert(isequal(full(S),[0 3 0;6 0 0;0 0 0]));")]
    [InlineData("S=sparse(1,2,4,3,3,20); assert(nzmax(S)>=20); assert(nnz(S)==1);")]
    [InlineData("S=sparse([1 0 3;0 2 0]); assert(isequal(full(S(:,[3 1])),[3 1;0 0])); assert(issparse(S(:,1)));")]
    [InlineData("S=sparse([1 0 3;0 2 0]); assert(isequal(full(S(:,end)),[3;0]));")]
    [InlineData("S=sparse([1 0;0 2]); assert(isequal(full(S([2 2 1],[2 1])),[2 0;2 0;0 1]));")]
    [InlineData("S=sparse([],[],[],3,4,10); assert(isequal(size(S),[3 4])); assert(nnz(S)==0);")]
    [InlineData("im=imagesc([1 2;3 4]); im.AlphaData=.5; assert(isequal(im.AlphaData,.5));")]
    [InlineData("im=imagesc([1 2;3 4],'AlphaData',.5); assert(isequal(im.AlphaData,.5));")]
    [InlineData("im=imagesc([1 2;3 4],AlphaData=.5); assert(isequal(im.AlphaData,.5));")]
    [InlineData("im=imagesc([1 2;3 4]); im.AlphaData=0; assert(isequal(im.AlphaData,0));")]
    [InlineData("im=imagesc([1 2;3 4]); im.AlphaData=2; assert(isequal(im.AlphaData,2));")]
    [InlineData("A=[0 .25;.75 1]; im=imagesc([1 2;3 4]); im.AlphaData=A; assert(isequal(im.AlphaData,A));")]
    [InlineData("im=imagesc([1 2;3 4]); im.AlphaData=zeros(2); im.AlphaData=1; assert(isequal(im.AlphaData,1));")]
    [InlineData("im=imagesc([1 2;3 4],'AlphaData',[0 .5;1 2]); im.AlphaDataMapping='scaled'; assert(strcmp(im.AlphaDataMapping,'scaled')); im.AlphaDataMapping='direct'; assert(strcmp(im.AlphaDataMapping,'direct'));")]
    [InlineData("im=imagesc([1 2 3;4 5 6]); assert(isequal(im.XData,[1 3])); assert(isequal(im.YData,[1 2]));")]
    [InlineData("im=imagesc(5,7,[1 2 3;4 5 6]); assert(isequal(im.XData,5)); assert(isequal(im.YData,7));")]
    [InlineData("im=imagesc([5 99 8],[3 99 6],[1 2 3;4 5 6]); assert(isequal(im.XData,[5 99 8])); assert(isequal(im.YData,[3 99 6]));")]
    [InlineData("im=imagesc([8 5],[6 3],[1 2 3;4 5 6]); assert(isequal(im.XData,[8 5])); assert(isequal(im.YData,[6 3]));")]
    [InlineData("im=imagesc([1 2;3 4],'AlphaData',ones(2),[2 3]); assert(isequal(get(gca,'CLim'),[2 3]));")]
    [InlineData("plot([1 2]); ax=gca; imagesc('CData',[1 2;3 4]); assert(numel(ax.Children)==2);")]
    [InlineData("imagesc([1 2;3 4]); assert(strcmp(get(gca,'YDir'),'reverse')); assert(strcmp(get(gca,'Layer'),'top')); assert(isequal(get(gca,'View'),[0 90]));")]
    [InlineData("C=reshape(linspace(0,1,12),2,2,3); im=imagesc(C); assert(isequal(im.CData,C));")]
    [InlineData("im=imagesc([1 2;3 4]); im.CData=[5 6 7;8 9 10]; assert(isequal(im.CData,[5 6 7;8 9 10]));")]
    [InlineData("Z=10+peaks(7); surf(Z); old=get(gca,'View'); hold on; imagesc(Z); assert(isequal(get(gca,'View'),old)); assert(numel(get(gca,'Children'))==2);  lim=get(gca,'ZLim'); assert(lim(1)<=0);")]
    [InlineData("A(1,:,:)=ones(2,3); assert(isequal(size(A),[1 2 3])); assert(nnz(A)==6);")]
    [InlineData("A=4; A(2,2,2)=8; assert(isequal(size(A),[2 2 2])); assert(A(1)==4); assert(A(2,1,1)==0);")]
    [InlineData("A=uint8(ones(2,2,2)); A(3,3,3)=9; assert(isa(A,'uint8')); assert(A(2,3,2)==0);")]
    [InlineData("S=sparse([1 0;0 2]); assert(isequal(size(S(:)),[4 1])); assert(isequal(full(S(:)),[1;0;0;2]));")]
    [InlineData("S=sparse([1 0;0 2]); assert(isequal(full(S([false true],:)),[0 2]));")]
    [InlineData("S=sparse(eye(2)); failed=false; try; x=S(1.5,1); catch; failed=true; end; assert(failed);")]
    [InlineData("C=uint8([0 100;200 255]); im=imagesc(C); assert(isa(im.CData,'uint8')); C(1)=5; assert(im.CData(1)==0);")]
    [InlineData("C=uint8(reshape(1:12,2,2,3)); im=imagesc(C); assert(isequal(im.CData,C));")]
    [InlineData("im=imagesc(ones(2)); assert(strcmp(im.AlphaDataMapping,'none'));")]
    [InlineData("plot(1:3); ax=gca; ax.CLim=[-5 5]; imagesc('CData',[1 2;3 4]); assert(isequal(ax.CLim,[1 4]));")]
    [InlineData("im=imagesc([1 2;3 4]); im.XData=[5 3]; im.YData=7; assert(isequal(im.XData,[5 3])); assert(isequal(im.YData,7));")]
    [InlineData("C=logical([0 1;1 0]); im=imagesc(C); assert(islogical(im.CData)); assert(isequal(im.CData,C));")]
    [InlineData("im=imagesc(ones(2)); im.AlphaData=uint8(2); assert(isa(im.AlphaData,'uint8')); im.AlphaData=uint8([0 1;2 3]); assert(isa(im.AlphaData,'uint8'));")]
    [InlineData("im=imagesc(ones(2)); im.CData(:,3)=[4;5]; assert(isequal(im.CData,[1 1 4;1 1 5]));")]
    [InlineData("im=imagesc(ones(2)); im.AlphaData=ones(2); im.AlphaData(1,2)=.25; assert(isequal(im.AlphaData,[1 .25;1 1]));")]
    public void MatchesMatlab(string code)
    {
        var result = JgsRunner.Run(code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default,
            sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
