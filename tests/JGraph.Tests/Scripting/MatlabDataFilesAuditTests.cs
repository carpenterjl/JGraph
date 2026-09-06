using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;
namespace JGraph.Tests.Scripting;
[Collection("JG facade")]
public sealed class MatlabDataFilesAuditTests : IDisposable
{
    public MatlabDataFilesAuditTests() => JG.Reset();
    public void Dispose() => JG.Reset();
    [Theory]
    [InlineData("C=char([65 66;67 68],'abcd'); assert(isequal(C,['AB  ';'CD  ';'abcd']));")]
    [InlineData("C=char(char('a','bb'),'ccc'); assert(isequal(C,['a  ';'bb ';'ccc']));")]
    [InlineData("D=hours(23:25)+minutes(8)+seconds(1.2345); C=char(D,'hh:mm'); assert(isequal(C,['23:08';'24:08';'25:08']));")]
    [InlineData("C=char(seconds([0 65]),'mm:ss'); assert(isequal(C,['00:00';'01:05']));")]
    [InlineData("D=hours(23:25)+minutes(8)+seconds(1.2345); assert(isequal(char(D),['23.134 hr';'24.134 hr';'25.134 hr']));")]
    [InlineData("D=datetime(2024,2,3,4,5,6); assert(strcmp(char(D,'yyyy-MM-dd HH:mm:ss'),'2024-02-03 04:05:06'));")]
    [InlineData("D=datetime(2024,2,3); assert(strcmp(char(D,'eeee, MMMM d, yyyy','fr_FR'),'samedi, f\u00e9vrier 3, 2024'));")]
    [InlineData("D=datetime(2024,2,3); D.Format='yyyy-MM-dd'; assert(strcmp(char(D,[],'en_US'),'2024-02-03'));")]
    [InlineData("s=regexprep('My flowers may bloom in May','M(\\w+)y','April','preservecase'); assert(strcmp(s,'My flowers april bloom in April'));")]
    [InlineData("assert(strcmp(regexprep('MAY may May','may','aPRIL','preservecase'),'APRIL april April'));")]
    [InlineData("s=struct('x',{1 2},'label',{'same'}); assert(numel(s)==2); assert(s(2).x==2); assert(strcmp(s(2).label,'same'));")]
    [InlineData("s=struct('label',{'same'},'x',{1;2}); assert(isequal(size(s),[2 1])); assert(strcmp(s(2).label,'same'));")]
    [InlineData("s=struct('x',{},'label',{'same'}); assert(isempty(s)); assert(isfield(s,'label'));")]
    [InlineData("year=(1:3)'; pop=[2;4;8]; T=table(year,pop); assert(isequal(T.year,year)); assert(isequal(T.Properties.VariableNames,{'year','pop'}));")]
    [InlineData("x=(1:3)'; T=table(x,x+1); assert(isequal(T.Properties.VariableNames,{'x','Var2'}));")]
    [InlineData("x=(1:3)'; T=table(x,'VariableNames',{'value'},'RowNames',{'a';'b';'c'}); assert(isequal(T.value,x)); assert(isequal(T.Properties.RowNames,{'a';'b';'c'}));")]
    [InlineData("Age=[38;43;40]; BP=[124 93;109 77;117 75]; T=table(Age,BP); assert(isequal(size(T),[3 2])); assert(isequal(T.BP,BP)); S=T([3 1],:); assert(isequal(S.BP,BP([3 1],:)));")]
    [InlineData("A=[1 2 3]; T=table(A); assert(isequal(size(T),[1 1])); assert(isequal(T.A,A));")]
    [InlineData("Time=datetime(2024,1,1)+(0:2)'; y=[2;4;6]; T=table(Time,y); assert(isdatetime(T.Time)); assert(isequal(T.Time,Time));")]
    [InlineData("failed=false; try; T=table([1;2],'RowNames',{'a'}); catch; failed=true; end; assert(failed);")]
    [InlineData("A=magic(3); T=array2table(A); assert(isequal(T.A2,A(:,2))); assert(isequal(T.Properties.VariableNames,{'A1','A2','A3'}));")]
    [InlineData("T=array2table([1 2;3 4],'VariableNames',{'x','y'}); assert(isequal(T.y,[2;4]));")]
    [InlineData("A=magic(3); T=array2table(A); M=movmean(T,3); assert(isequal(M.A1,[5.5;5;3.5])); assert(isequal(M.A2,[3;5;7]));")]
    [InlineData("A=magic(3); T=array2table(A); M=movmean(T,3,DataVariables=[\"A2\" \"A3\"]); assert(isequal(M.A1,T.A1)); assert(isequal(M.A3,[6.5;5;4.5]));")]
    [InlineData("A=magic(3); T=array2table(A); M=movmean(T,3,ReplaceValues=false); assert(isequal(size(M),[3 6])); assert(isequal(M.A1_movmean,[5.5;5;3.5])); assert(isequal(M.Properties.VariableNames,{'A1','A2','A3','A1_movmean','A2_movmean','A3_movmean'}));")]
    [InlineData("x=[3;1;2]; y=[30;10;20]; T=table(x,y); S=sortrows(T); assert(isequal(S.y,[10;20;30]));")]
    [InlineData("A=[1+2i 3+4i;5+6i 7+8i]; assert(isequal(fliplr(A),A(:,[2 1]))); assert(isequal(fliplr(A(:,1)),A(:,1)));")]
    [InlineData("failed=false; try; s=struct('a',{1 2},'b',{3;4}); catch; failed=true; end; assert(failed);")]
    [InlineData("p=plot([1 2],[3+4i 5+6i]); assert(isequal(p.YData,[3 5])); close all;")]
    [InlineData("p=plot([1+2i 3+4i]); assert(isequal(p.XData,[1 3])); assert(isequal(p.YData,[2 4])); close all;")]
    public void MatchesMatlab(string code)
    {
        var result = JgsRunner.Run(code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default,
            sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
