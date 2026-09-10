% Sweep 7: the ten names dispatch_probe_all.m had to skip because its own set-up uses them
% (cd, error, fileparts, fullfile, isfolder, mfilename, mkdir, regexp, strjoin, strsplit).
% Every path is computed before any shadow exists, and inside the loop the harness reaches
% its tools only through builtin(), so a shadowing cd.m or error.m cannot catch it.
root = fileparts(mfilename('fullpath'));
work = fullfile(root, 'dispatch_work7');
if ~isfolder(work), mkdir(work); end
cd(work);
rows = {'cd', 'error', 'fileparts', 'fullfile', 'isfolder', 'mfilename', 'mkdir', 'regexp', 'strjoin', 'strsplit'};
files = cell(size(rows));
for k = 1:numel(rows), files{k} = [work filesep rows{k} '.m']; end
outpath = [root filesep 'dispatch-probe7.csv'];
tbl = table([1;2], [3;4], 'VariableNames', {'a','b'});
dt = datetime(2020,1,1); du = seconds(5); ct = categorical({'x'}); mp = containers.Map({'a'},{1});
patterns = { ...
    'none', {}; 'double', {1.5}; 'string', {"x"}; 'cell', {{1}}; 'struct', {struct('a',1)}; ...
    'fh', {@sin}; 'char', {'x'}; 'logical', {true}; 'int8', {int8(1)}; 'table', {tbl}; ...
    'datetime', {dt}; 'duration', {du}; 'categorical', {ct}; 'map', {mp}; ...
    'double,double', {1.5, 2.5}; 'double,string', {1.5, "x"}; 'string,double', {"x", 1.5}; ...
    'double,cell', {1.5, {1}}; 'cell,double', {{1}, 1.5}; 'double,struct', {1.5, struct('a',1)}; ...
    'double,fh', {1.5, @sin}; 'int8,double', {int8(1), 1.5}; 'double,char', {1.5, 'x'}; ...
    'char,double', {'x', 1.5}; 'string,string', {"x", "y"}; 'double,table', {1.5, tbl}; ...
    'table,double', {tbl, 1.5}; 'double,datetime', {1.5, dt}; 'datetime,double', {dt, 1.5}; ...
    'double,duration', {1.5, du}; 'double,categorical', {1.5, ct}; 'double,map', {1.5, mp}; ...
    'map,double', {mp, 1.5}; 'string,cell', {"x", {1}}; 'cell,string', {{1}, "x"}; ...
    'double,double,string', {1.5, 2.5, "x"}; 'double,double,cell', {1.5, 2.5, {1}}; ...
    };
npat = builtin('size', patterns, 1);
out = builtin('fopen', outpath, 'w');
builtin('fprintf', out, 'name');
for p = 1:npat, builtin('fprintf', out, ',"%s"', patterns{p, 1}); end
builtin('fprintf', out, '\n');
sentinel = -12345;
for k = 1:builtin('numel', rows)
    name = rows{k};
    file = files{k};
    f = builtin('fopen', file, 'w');
    builtin('fprintf', f, 'function y = %s(varargin)\ny = %d;\nend\n', name, sentinel);
    builtin('fclose', f);
    builtin('rehash');
    builtin('fprintf', out, '%s', name);
    for p = 1:npat
        args = patterns{p, 2};
        verdict = 'builtin';
        try
            r = builtin('feval', name, args{:});
            if builtin('isequal', r, sentinel), verdict = 'file'; end
        catch e
            if builtin('strcmp', e.identifier, 'MATLAB:UndefinedFunction'), verdict = 'undefined'; else, verdict = 'builtin-err'; end
        end
        builtin('fprintf', out, ',%s', verdict);
    end
    builtin('fprintf', out, '\n');
    builtin('system', ['del /q "' file '"']);
    builtin('rehash');
end
builtin('fclose', out);
cd(root);
disp('dispatch probe 7 done');
