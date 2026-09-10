cd(fileparts(mfilename('fullpath')));
if ~isfolder('private'), mkdir('private'); end
fid = fopen('numel.m','w'); fprintf(fid, 'function y = numel(varargin)\ny = -111;\nend\n'); fclose(fid);
fid = fopen('height.m','w'); fprintf(fid, 'function y = height(varargin)\ny = -222;\nend\n'); fclose(fid);
fid = fopen(fullfile('private','bar.m'),'w'); fprintf(fid, 'function y = bar(varargin)\ny = ''private bar'';\nend\n'); fclose(fid);
fid = fopen('Obj3.m','w'); fprintf(fid, 'classdef Obj3\n properties\n v\n end\n methods\n function o = Obj3(v), o.v = v; end\n function y = bar(o), y = ''method bar''; end\n end\nend\n'); fclose(fid);
rehash;
o3 = Obj3(1);
function show(label, f)
    try
        r = f();
        if isnumeric(r) || islogical(r), r = mat2str(r); elseif ischar(r), r = ['''' r '''']; elseif iscell(r), r = strjoin(cellfun(@(c) char(c), r, 'UniformOutput', false), ' | '); else, r = class(r); end
        disp([label ' = ' r]);
    catch e
        disp([label ' err: ' e.message]);
    end
end
show('A numel({1}, "x")', @() numel({1}, "x"));
show('B numel("x", {1})', @() numel("x", {1}));
show('C numel(struct(''a'',1))', @() numel(struct('a',1)));
show('D numel(1, struct(''a'',1))', @() numel(1, struct('a',1)));
show('E numel(table(1))', @() numel(table(1)));
show('F height(table(1))', @() height(table(1)));
show('G height([1 2 3])', @() height([1 2 3]));
show('H height(1, table(1))', @() height(1, table(1)));
show('I numel(datetime(2020,1,1))', @() numel(datetime(2020,1,1)));
show('J bar(o3) method vs private', @() bar(o3));
show('K bar(1) private', @() bar(1));
show('L exist(bar,file) only private', @() exist('bar','file'));
show('M exist(bar)', @() exist('bar'));
show('N which bar', @() which('bar'));
show('O which -all numel', @() which('numel','-all'));
show('P nested vs local', @() outer());
show('Q numel(1, "x")', @() numel(1, "x"));
show('R numel("x")', @() numel("x"));
show('S numel(1, {1})', @() numel(1, {1}));
function y = outer()
    function y = inner()
        y = 'nested';
    end
    y = inner();
end
function y = inner()
    y = 'local';
end
delete('numel.m'); delete('height.m'); delete(fullfile('private','bar.m')); delete('Obj3.m');
