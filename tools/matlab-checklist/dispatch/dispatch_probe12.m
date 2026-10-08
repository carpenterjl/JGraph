% Sweep 12: vrjoystick, one argument pattern per MATLAB process. Sweep 10 crashed R2025b inside
% vrjoystick's built-in (an access violation in a MEX file) on its third pattern, so each pattern
% runs alone: run-probe12.ps1 starts MATLAB once per pattern with P set, and writes builtin-err for
% a pattern whose process dies, since the built-in was reached (the same fact for resolution).
root = fileparts(mfilename('fullpath'));
work = fullfile(root, 'dispatch_work12');
if ~isfolder(work), mkdir(work); end
cd(work);
name = 'vrjoystick';
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
sentinel = -12345;
file = [work filesep name '.m'];
f = fopen(file, 'w');
fprintf(f, 'function y = %s(varargin)\ny = %d;\nend\n', name, sentinel);
fclose(f);
rehash;
args = patterns{P, 2};
verdict = 'builtin';
try
    r = feval(name, args{:});
    if isequal(r, sentinel), verdict = 'file'; end
catch e
    if strcmp(e.identifier, 'MATLAB:UndefinedFunction'), verdict = 'undefined'; else, verdict = 'builtin-err'; end
end
v = fopen([root filesep 'probe12-verdict.txt'], 'w');
fprintf(v, '%s', verdict);
fclose(v);
delete(file);
