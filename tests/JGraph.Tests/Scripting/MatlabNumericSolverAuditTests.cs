using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;
namespace JGraph.Tests.Scripting;
[Collection("JG facade")]
public sealed class MatlabNumericSolverAuditTests : IDisposable
{
    public MatlabNumericSolverAuditTests() => JG.Reset();
    public void Dispose() => JG.Reset();
    [Theory]
    [InlineData("A=zeros(2,3,'single'); assert(strcmp(class(A),'single')); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=zeros(2,3,'like',complex(single(2),single(3))); assert(~isreal(A)); assert(strcmp(class(A),'single')); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=zeros(2,3,'like',sparse(1)); assert(issparse(A)); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=ones(2,3,'single'); assert(strcmp(class(A),'single')); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=ones(2,3,'like',complex(single(2),single(3))); assert(~isreal(A)); assert(strcmp(class(A),'single')); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=ones(2,3,'like',sparse(1)); assert(issparse(A)); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=eye(2,3,'single'); assert(strcmp(class(A),'single')); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=eye(2,3,'like',complex(single(2),single(3))); assert(~isreal(A)); assert(strcmp(class(A),'single')); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=eye(2,3,'like',sparse(1)); assert(issparse(A)); assert(isequal(size(A),[2 3]));")]
    [InlineData("A=sum(int8([100 100]),'native'); assert(strcmp(class(A),'int8')); assert(A==127);")]
    [InlineData("A=sum(int32([1 2;3 4]),2,'native'); assert(strcmp(class(A),'int32')); assert(isequal(A,int32([3;7])));")]
    [InlineData("assert(isequal(sum([1 NaN;3 4],2,'omitmissing'),[1;7])); assert(isnan(sum([1 NaN],'includemissing')));")]
    [InlineData("A=sum(single([1 2]),'double'); assert(strcmp(class(A),'double')); assert(A==3);")]
    [InlineData("A=reshape(1:24,[2 3 4]); assert(isequal(median(A,[1 2]),reshape([3.5 9.5 15.5 21.5],[1 1 4]))); assert(median(A,'all')==12.5);")]
    [InlineData("assert(isequal(median([1 NaN;3 4],2,'omitmissing'),[1;3.5])); assert(isnan(median([NaN NaN],'omitmissing')));")]
    [InlineData("A=[1 1;7 9;1 9;1 9;6 2]; W=[1 2 1 2 3]'; assert(isequal(median(A,Weights=W),[6 9]));")]
    [InlineData("assert(median([1 3 9],Weights=[1 1 2])==6); assert(median([1 3 9],Weights=[0 1 0])==3);")]
    [InlineData("A=median(int16([1 2])); assert(strcmp(class(A),'int16')); assert(A==2);")]
    [InlineData("assert(isequaln(median(zeros(0,3)),[NaN NaN NaN])); assert(isequal(size(median(zeros(3,0))),[1 0]));")]
    [InlineData("[v,k]=max([-2+2i 4+i -1-3i]); assert(v==4+1i); assert(k==2);")]
    [InlineData("[v,k]=max([1 -1 1i -1i]); assert(v==-1); assert(k==2);")]
    [InlineData("[v,k]=max([1+2i 3+4i;3+1i 1+1i]); assert(isequal(v,[3+1i 3+4i])); assert(isequal(k,[2 1]));")]
    [InlineData("[v,k]=max([-5 3 -2],[],ComparisonMethod='abs'); assert(v==-5); assert(k==1);")]
    [InlineData("[v,k]=max([1+9i 2+1i],[],ComparisonMethod='real'); assert(v==2+1i); assert(k==2);")]
    [InlineData("assert(isnan(max([1 NaN],[],'includemissing'))); assert(max([1 NaN],[],'omitmissing')==1);")]
    [InlineData("assert(norm(linspace(1+2i,5+6i,3)-[1+2i 3+4i 5+6i])<1e-12); assert(linspace(1i,2i,1)==2i); assert(isempty(linspace(1i,2i,0)));")]
    [InlineData("assert(isequal(true,1,uint8(1))); assert(isequal('AB',[65 66])); assert(isequal(\"foo\",'foo'));")]
    [InlineData("assert(isequaln([1+NaN*1i 2],[1+NaN*1i 2],[1+NaN*1i 2])); assert(~isequal([1 NaN],[1 NaN]));")]
    [InlineData("A=zeros(2,2,2); B=A; B(2,2,2)=1; assert(~isequal(A,B)); assert(isequaln(A,A,A));")]
    [InlineData("S=sparse([1 0 3;0 2 0]); A=sum(S); assert(issparse(A)); assert(isequal(full(A),[1 2 3])); assert(isequal(full(sum(S,2)),[4;2]));")]
    [InlineData("S=sparse([1 0 3;0 2 0]); assert(issparse(diag(S))); assert(isequal(full(diag(S)),[1;2])); assert(isequal(full(diag(S,2)),3));")]
    [InlineData("A=diag(sparse([2 3]),-1); assert(issparse(A)); assert(isequal(full(A),[0 0 0;2 0 0;0 3 0]));")]
    [InlineData("S=sparse([1 0;0 2]); A=[2 3;4 5]; assert(isequal(S-A,[-1 -3;-4 -3])); assert(isequal(A-S,[1 3;4 3])); assert(isequal(S+2,[3 2;2 4]));")]
    [InlineData("x=0:4; v=sin(x)+1i*cos(x); q=.25:.5:3.75; a=interp1(x,v,q,'linear'); b=interp1(x,real(v),q,'linear')+1i*interp1(x,imag(v),q,'linear'); assert(norm(a-b)<1e-12);")]
    [InlineData("x=0:4; v=sin(x)+1i*cos(x); q=.25:.5:3.75; a=interp1(x,v,q,'nearest'); b=interp1(x,real(v),q,'nearest')+1i*interp1(x,imag(v),q,'nearest'); assert(norm(a-b)<1e-12);")]
    [InlineData("x=0:4; v=sin(x)+1i*cos(x); q=.25:.5:3.75; a=interp1(x,v,q,'pchip'); b=interp1(x,real(v),q,'pchip')+1i*interp1(x,imag(v),q,'pchip'); assert(norm(a-b)<1e-12);")]
    [InlineData("x=0:4; v=sin(x)+1i*cos(x); q=.25:.5:3.75; a=interp1(x,v,q,'spline'); b=interp1(x,real(v),q,'spline')+1i*interp1(x,imag(v),q,'spline'); assert(norm(a-b)<1e-12);")]
    [InlineData("x=0:4; v=sin(x)+1i*cos(x); q=.25:.5:3.75; a=interp1(x,v,q,'makima'); b=interp1(x,real(v),q,'makima')+1i*interp1(x,imag(v),q,'makima'); assert(norm(a-b)<1e-12);")]
    [InlineData("v=[0 1i;2+2i 4i]; a=interp1([0 2],v,[.5;1.5]); assert(norm(a-[.5+.5i 1.75i;1.5+1.5i 3.25i])<1e-12);")]
    [InlineData("assert(abs(interp1([0 1],[1i 1+2i],2,'linear','extrap')-(2+3i))<1e-12);")]
    [InlineData("q=integral(@(x)[sin(x) cos(x)],0,1,ArrayValued=true); assert(isequal(size(q),[1 2])); assert(norm(q-[1-cos(1) sin(1)])<1e-9);")]
    [InlineData("q=integral(@(x)[x x^2;exp(x) cos(x)],0,1,'ArrayValued',true); assert(isequal(size(q),[2 2])); assert(norm(q-[.5 1/3;exp(1)-1 sin(1)])<1e-9);")]
    [InlineData("q=integral(@(x)[exp(1i*x);x+1i*x^2],0,pi,ArrayValued=true); assert(norm(q-[2i;pi^2/2+1i*pi^3/3])<1e-8);")]
    [InlineData("q=integral(@(z)1./z,1,1,Waypoints=[1i -1 -1i]); assert(abs(q-2*pi*1i)<1e-8);")]
    [InlineData("q=integral(@(z)z.^2,0,1+1i); assert(abs(q-(1+1i)^3/3)<1e-9);")]
    [InlineData("q=integral(@(x)exp(1i*x),0,pi); assert(abs(q-2i)<1e-9);")]
    [InlineData("q=integral(@(x)[x x^2],1,0,ArrayValued=true); assert(norm(q-[-.5 -1/3])<1e-10);")]
    [InlineData("q=integral(@(x)[exp(-x) exp(-2*x)],0,Inf,ArrayValued=true); assert(norm(q-[1 .5])<1e-7);")]
    [InlineData("q=integral(@(x)[sin(40*x) cos(40*x)],0,1,ArrayValued=true,AbsTol=1e-11,RelTol=1e-10); assert(norm(q-[(1-cos(40))/40 sin(40)/40])<1e-9);")]
    [InlineData("A=reshape(sin(1:12),4,3); [U,s,V]=svd(A,'econ','vector'); assert(isequal(size(s),[3 1])); assert(norm(U*diag(s)*V'-A,'fro')<1e-10); assert(isequal(size(U),[4 3])); assert(isequal(size(V),[3 3]));")]
    [InlineData("A=reshape(sin(1:12),3,4); [U,s,V]=svd(A,'econ','vector'); assert(isequal(size(s),[3 1])); assert(norm(U*diag(s)*V'-A,'fro')<1e-10); assert(isequal(size(U),[3 3])); assert(isequal(size(V),[4 3]));")]
    [InlineData("A=reshape(sin(1:9),3,3); [U,s,V]=svd(A,'econ','vector'); assert(isequal(size(s),[3 1])); assert(norm(U*diag(s)*V'-A,'fro')<1e-10); assert(isequal(size(U),[3 3])); assert(isequal(size(V),[3 3]));")]
    [InlineData("A=[1 2;3 4;5 6]; S=svd(A,'matrix'); assert(isequal(size(S),[3 2])); assert(norm(diag(S)-svd(A))<1e-10);")]
    [InlineData("A=[1+1i 2;3i 4;5 6-1i]; [U,s,V]=svd(A,'econ','vector'); assert(norm(U*diag(s)*V'-A,'fro')<1e-9);")]
    [InlineData("A=sparse([1 0;2 3;0 4]); [Q,R]=qr(A,0); assert(norm(Q*R-A,'fro')<1e-10);")]
    [InlineData("o=odeset(RelTol=1e-8,AbsTol=1e-10); [t,y]=ode45(@(t,y)-2*y,[0 1],1,o); assert(abs(y(end)-exp(-2))<1e-6);")]
    [InlineData("assert(islogical(zeros(2,'like',true))); assert(islogical(ones(2,'logical'))); assert(islogical(eye(2,'like',true))); assert(isequal(eye(2,'logical'),logical([1 0;0 1])));")]
    [InlineData("A=sum([true true],'native'); assert(islogical(A)); assert(A);")]
    [InlineData("A=zeros(2,3,'like',1i); B=A; C=B'; assert(~isreal(B)); assert(~isreal(C)); assert(isequal(size(C),[3 2]));")]
    [InlineData("[v,k]=max([2+1i 2+3i],[],ComparisonMethod='real'); assert(v==2+3i); assert(k==2);")]
    [InlineData("[v,k]=min([3i 1+1i 2i]); assert(v==1+1i); assert(k==2);")]
    [InlineData("a=interp1([0 1],[0 2],[.5 2],'linear',3+4i); assert(isequal(a,[1 3+4i]));")]
    [InlineData("failed=false; try; median([1 2],Weights=[1 -1]); catch; failed=true; end; assert(failed); failed=false; try; median([1 2],'all',Weights=[1 1]); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; svd(eye(2),'econ','invalid'); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; integral(@(x)[x x^2],0,1,ArrayValued=true,AbsTol=-1); catch; failed=true; end; assert(failed);")]
    public void MatchesMatlab(string code)
    {
        var result = JgsRunner.Run(code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default,
            sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
