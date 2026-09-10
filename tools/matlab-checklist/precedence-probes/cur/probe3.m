cd(fileparts(mfilename('fullpath')));
addpath(fullfile('..','lib'));
% sum.m in cur; Obj is a classdef with no sum method
fid = fopen('sum.m','w'); fprintf(fid, 'function y = sum(varargin)\ny = -555;\nend\n'); fclose(fid);
fid = fopen('Obj.m','w'); fprintf(fid, 'classdef Obj\n properties\n v\n end\n methods\n function o = Obj(v), o.v = v; end\n function y = foo(o), y = ''method foo''; end\n end\nend\n'); fclose(fid);
fid = fopen('foo.m','w'); fprintf(fid, 'function y = foo(varargin)\ny = ''file foo'';\nend\n'); fclose(fid);
fid = fopen('eps.m','w'); fprintf(fid, 'function y = eps(varargin)\ny = -777;\nend\n'); fclose(fid);
rehash;
o = Obj(1);
try; disp(['A sum(o, 2) = ' num2str(sum(o, 2))]); catch e; disp(['A sum(o,2) err: ' e.message]); end
try; disp(['B sum(2, o) = ' num2str(sum(2, o))]); catch e; disp(['B sum(2,o) err: ' e.message]); end
disp(['C sum([1 2 3]) = ' num2str(sum([1 2 3]))]);
disp(['D max(int8(3), 2.5) = ' num2str(max(int8(3), 2.5))]);
disp(['E max({1}, 2) = ' num2str(max({1}, 2))]);
disp(['F max(2, {1}) = ' num2str(max(2, {1}))]);
disp(['G max("a") = ' num2str(max("a"))]);
disp(['H max(true, false) = ' num2str(max(true, false))]);
try; disp(['I bare max = ' num2str(max)]); catch e; disp(['I bare max err: ' e.message]); end
disp(['J foo(o) = ' foo(o)]);
disp(['K foo(1) = ' foo(1)]);
disp(['L bare eps = ' num2str(eps)]);
disp(['M eps(1) = ' num2str(eps(1))]);
disp(['N eps("double") = ' num2str(eps("double"))]);
h = @mean; hm = @max;
cd(fullfile('..','lib'));
disp(['O handle mean after cd away = ' num2str(h([1 2 3]))]);
disp(['P handle max cell after cd away = ' num2str(hm({1}))]);
disp(['Q cellfun(@max, {{1},[1 5 3]}) after cd = ' mat2str(cellfun(hm, {{1},[1 5 3]}))]);
cd(fullfile('..','cur'));
disp(['R exist max file = ' num2str(exist('max','file')) ' exist eps = ' num2str(exist('eps'))]);
for k = 1:2
    if k == 1
        fid = fopen('newfn.m','w'); fprintf(fid, 'function y = newfn()\ny = 1;\nend\n'); fclose(fid);
    else
        try; disp(['S newfn same statement = ' num2str(newfn())]); catch e; disp(['S newfn same statement err: ' e.message]); end
    end
end
try; disp(['T newfn next statement = ' num2str(newfn())]); catch e; disp(['T newfn next statement err: ' e.message]); end
delete('newfn.m');
try; disp(['U newfn after delete = ' num2str(newfn())]); catch e; disp(['U newfn after delete err: ' e.message]); end
fid = fopen(fullfile('private','max.m'),'w'); fprintf(fid, 'function y = max(varargin)\ny = -888;\nend\n'); fclose(fid);
try; disp(['V private max double = ' num2str(max([1 5 3]))]); catch e; disp(['V err: ' e.message]); end
disp(['W which max: ' which('max')]);
delete(fullfile('private','max.m'));
try; disp(['X after private delete max = ' num2str(max([1 5 3]))]); catch e; disp(['X err: ' e.message]); end
delete('sum.m'); delete('Obj.m'); delete('foo.m'); delete('eps.m');
clear o;
disp(['Y after deletes sum = ' num2str(sum([1 2 3])) ' eps = ' num2str(eps)]);
