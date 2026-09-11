% Sweep 9: hist, the legacy histogram, registered after the M145 sweeps (ADR 0150). The same
% harness as sweep 8 — the name shadowed by a file returning a sentinel and called under the 37
% argument patterns — and its which -all classification taken with no shadow in place.
root = fileparts(mfilename('fullpath'));
work = fullfile(root, 'dispatch_work9');
if ~isfolder(work), mkdir(work); end
cd(work);
rows = {'hist'};
files = cell(size(rows));
for k = 1:numel(rows), files{k} = [work filesep rows{k} '.m']; end
outpath = [root filesep 'dispatch-probe9.csv'];
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
npat = size(patterns, 1);
out = fopen(outpath, 'w');
fprintf(out, 'name');
for p = 1:npat, fprintf(out, ',"%s"', patterns{p, 1}); end
fprintf(out, '\n');
sentinel = -12345;
for k = 1:numel(rows)
    name = rows{k};
    file = files{k};
    f = fopen(file, 'w');
    fprintf(f, 'function y = %s(varargin)\ny = %d;\nend\n', name, sentinel);
    fclose(f);
    rehash;
    fprintf(out, '%s', name);
    for p = 1:npat
        args = patterns{p, 2};
        verdict = 'builtin';
        try
            r = feval(name, args{:});
            if isequal(r, sentinel), verdict = 'file'; end
        catch e
            if strcmp(e.identifier, 'MATLAB:UndefinedFunction'), verdict = 'undefined'; else, verdict = 'builtin-err'; end
        end
        fprintf(out, ',%s', verdict);
    end
    fprintf(out, '\n');
    system(['del /q "' file '"']);
    rehash;
end
fclose(out);
cd(root);
% The classification, taken with no shadow in place: which -all for the name.
fid = fopen([root filesep 'classify-hist.csv'], 'w');
fprintf(fid, 'name,kind,method_classes\n');
for k = 1:numel(rows)
    w = which(rows{k}, '-all');
    kind = 'none';
    if ~isempty(w)
        if startsWith(w{1}, 'built-in'), kind = 'builtin'; elseif endsWith(w{1}, '.m'), kind = 'mfile'; else, kind = 'other'; end
    end
    cls = {};
    for j = 1:numel(w)
        t = regexp(w{j}, '@([A-Za-z_][A-Za-z0-9_.]*)', 'tokens', 'once');
        if ~isempty(t), cls{end+1} = t{1}; end %#ok<AGROW>
    end
    fprintf(fid, '%s,%s,%s\n', rows{k}, kind, strjoin(unique(cls), ';'));
end
fclose(fid);
disp('dispatch probe 9 done');
