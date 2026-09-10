cd(fileparts(mfilename('fullpath')));
fid = fopen('sum.m','w'); fprintf(fid, 'function y = sum(varargin)\ny = -555;\nend\n'); fclose(fid);
fid = fopen('Obj.m','w'); fprintf(fid, 'classdef Obj\n properties\n v\n end\n methods\n function o = Obj(v), o.v = v; end\n end\nend\n'); fclose(fid);
fid = fopen('Obj2.m','w'); fprintf(fid, 'classdef Obj2\n properties\n v\n end\n methods\n function o = Obj2(v), o.v = v; end\n function y = sum(varargin), y = -666; end\n end\nend\n'); fclose(fid);
rehash;
o = Obj(1); o2 = Obj2(1);
function show(label, f)
    try
        r = f();
        if isnumeric(r) || islogical(r), r = mat2str(r); elseif ischar(r), r = ['''' r '''']; else, r = class(r); end
        disp([label ' = ' r]);
    catch e
        disp([label ' err: ' e.message]);
    end
end
show('A sum(o,2)', @() sum(o, 2));
show('B sum(2,o)', @() sum(2, o));
show('C sum(o2,2)', @() sum(o2, 2));
show('D sum(2,o2)', @() sum(2, o2));
show('E sum([1 2 3])', @() sum([1 2 3]));
show('F sum({1})', @() sum({1}));
show('G sum(int8(3),2.5)', @() sum(int8(3), 2.5));
show('H max(int8(3),2.5)', @() max(int8(3), 2.5));
show('I max(1,single(2))', @() max(1, single(2)));
show('J max(1,''a'')', @() max(1, 'a'));
show('K max(''a'',1)', @() max('a', 1));
show('L max([1 5 3],[],2)', @() max([1 5 3], [], 2));
show('M max(1,@sin)', @() max(1, @sin));
show('N max(int8(3),{1})', @() max(int8(3), {1}));
show('O sum([1 2],''all'')', @() sum([1 2], 'all'));
show('P sum(int8([1 2]),''native'')', @() sum(int8([1 2]), 'native'));
show('Q max([1 2],[],"all")', @() max([1 2], [], "all"));
show('R sum(true)', @() sum(true));
show('S sum(o.v)', @() sum(o.v));
delete('sum.m'); delete('Obj.m'); delete('Obj2.m');
