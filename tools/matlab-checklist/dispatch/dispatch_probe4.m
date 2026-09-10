% Sweep 4: the six names the ordinary harness cannot measure, because it uses them itself.
% Every harness operation here goes through builtin(), which reaches the built-in past any
% shadow; the call under test goes through builtin('feval', name, ...), which resolves NAME the
% ordinary way and so sees the shadow.
root = fileparts(mfilename('fullpath'));
work = fullfile(root, 'dispatch_work4');
if ~isfolder(work), mkdir(work); end
cd(work);
rows = {'fopen', 'fprintf', 'fclose', 'feval', 'rehash', 'size'};
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
out = builtin('fopen', fullfile(root, 'dispatch-probe4.csv'), 'w');
builtin('fprintf', out, 'name');
for p = 1:size(patterns, 1), builtin('fprintf', out, ',"%s"', patterns{p, 1}); end
builtin('fprintf', out, '\n');
sentinel = -12345;
for k = 1:numel(rows)
    name = rows{k};
    file = fullfile(work, [name '.m']);
    f = builtin('fopen', file, 'w');
    builtin('fprintf', f, 'function y = %s(varargin)\ny = %d;\nend\n', name, sentinel);
    builtin('fclose', f);
    builtin('rehash');
    builtin('fprintf', out, '%s', name);
    for p = 1:size(patterns, 1)
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
    system(['del /q "' file '"']);
    builtin('rehash');
end
builtin('fclose', out);
cd(root);
disp('dispatch probe 4 done');
