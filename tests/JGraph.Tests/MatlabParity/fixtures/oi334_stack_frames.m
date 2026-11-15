% record: -noFigureWindows
% Open item 334 (ADR 0368): ME.stack holds a frame per anonymous body the error left, named by its text
% (line 0 for a handle made from text), a script named in a statement or run by run is a frame, run holds
% R2025b's run.m as a frame at its line 112, and dbstack's file for a package function or class method.
% Frames print as name@line(file), the fixture's folder as <DIR>, the made files' as <LIB>, an installation's
% toolbox as <ML>; getReport(e, 'basic') is the header and message.
global OI334LIB
lib = tempname;
mkdir(lib);
OI334LIB = lib;
put(fullfile(lib, 'oi334s.m'), {'try', '    error(''oi334:si'', ''inner'');', 'catch e', '    oi334_inner = e.stack;', 'end', ...
    'x = 1;', 'error(''oi334:s'', ''from script'');'});
put(fullfile(lib, 'oi334r.m'), {'oi334_rd = dbstack;', 'try', '    error(''oi334:ri'', ''inner'');', 'catch e', '    oi334_ri = e.stack;', 'end'});
put(fullfile(lib, 'oi334e.m'), {'x = 2;', 'error(''oi334:re'', ''from run'');'});
put(fullfile(lib, 'oi334fn.m'), {'function oi334fn()', 'assignin(''base'', ''oi334_fn'', dbstack);', 'end'});
put(fullfile(lib, 'oi334rr.m'), {'run(fullfile(fileparts(mfilename(''fullpath'')), ''oi334ri.m''));'});
put(fullfile(lib, 'oi334ri.m'), {'oi334_rr = dbstack;'});
put(fullfile(lib, 'oi334rf.m'), {'x = 1;', 'oi334fail(x);'});
put(fullfile(lib, 'oi334fail.m'), {'function oi334fail(~)', 'error(''oi334:fail'', ''deep'');', 'end'});
put(fullfile(lib, 'oi334ds.m'), {'dbstack;'});
mkdir(fullfile(lib, '+oi334pk'));
put(fullfile(lib, '+oi334pk', 'pf.m'), {'function s = pf()', 's = dbstack;', 'end'});
put(fullfile(lib, '+oi334pk', 'pf2.m'), {'function s = pf2()', 's = loc();', 'end', 'function s = loc()', 's = dbstack;', 'end'});
put(fullfile(lib, '+oi334pk', 'pferr.m'), {'function pferr()', 'error(''oi334:pk'', ''pkg boom'');', 'end'});
put(fullfile(lib, 'OI334C.m'), {'classdef OI334C', '    methods', '        function s = inst(~)', '            s = dbstack;', ...
    '        end', '    end', '    methods (Static)', '        function s = stat()', '            s = dbstack;', '        end', ...
    '        function staterr()', '            error(''oi334:cl'', ''class boom'');', '        end', '    end', 'end'});
addpath(lib);

% --- anonymous bodies in ME.stack ---------------------------------------------------------------
try
    cellfun(@(x) error('oi334:a', 'boom'), {1});
catch e
    st('cellfun', e);
end
try
    f = @() error('oi334:b', 'boom');
    f();
catch e
    st('direct', e);
end
try
    callit(@() error('oi334:c', 'boom'));
catch e
    st('callit', e);
end
try
    g = @() feval(@() error('oi334:d', 'boom'));
    g();
catch e
    st('nested', e);
end
try
    arrayfun(@(x) failing(x), 1);
catch e
    st('arrayfun_local', e);
end
try
    h = str2func('@() error(''oi334:e'', ''boom'')');
    h();
catch e
    st('str2func', e);
end
try
    eval('k = @() error(''oi334:f'', ''boom'');');
    k();
catch e
    st('eval_made', e);
end
try
    m = makefail();
    m();
catch e
    st('madeelsewhere', e);
end
try
    q = @(x) x(5);
    q([1 2]);
catch e
    st('runtime_refusal', e);
end
try
    cellfun(@(x) innercatch(x), {1});
catch e
    st('innercatch', e);
end
chk('caught_inside', caughtinside());
try
    feval(@() ...
        error('oi334:g', 'boom'));
catch e
    st('continued', e);
end
try
    z = @() error('oi334:h', 'boom');
    infn(z);
catch e
    st('throwascaller', e);
end
try
    rethrowit();
catch e
    st('rethrow_anon', e);
end

% --- scripts by name and by run -----------------------------------------------------------------
try
    oi334s;
catch e
    st('script_byname', e);
end
chk('script_inner_caught', fr(oi334_inner));
run(fullfile(lib, 'oi334r.m'));
chk('run_dbstack', fr(oi334_rd));
chk('run_inner_caught', fr(oi334_ri));
try
    run(fullfile(lib, 'oi334e.m'));
catch e
    st('run_error', e);
end
run(fullfile(lib, 'oi334fn.m'));
chk('run_function_file', fr(oi334_fn));
run(fullfile(lib, 'oi334rr.m'));
chk('run_nested', fr(oi334_rr));
try
    run(fullfile(lib, 'oi334rf.m'));
catch e
    st('run_deeper_error', e);
end
chk('printed_run', printed(evalc('run(fullfile(lib, ''oi334ds.m''))')));
chk('printed_script', printed(evalc('oi334ds')));
chk('printed_anon', printed(evalc('callp(@() dbstack)')));
chk('printed_href', sprintf('%d', contains(evalc('callp(@() dbstack)'), '<a href')), 'div=ADR0368');

% --- a package function's and a class method's frames -------------------------------------------
chk('pkg_dbstack', fr(oi334pk.pf()));
chk('pkg_local_dbstack', fr(oi334pk.pf2()));
chk('class_static_dbstack', fr(OI334C.stat()));
o = OI334C();
chk('class_inst_dbstack', fr(o.inst()));
chk('pkg_complete', sf(callcomplete(@() oi334pk.pf())));
try
    oi334pk.pferr();
catch e
    chk('pkg_error', fr(e.stack));
end
try
    OI334C.staterr();
catch e
    chk('class_error', fr(e.stack));
end
rmpath(lib);
rmdir(lib, 's');

function st(key, e)
r = getReport(e, 'basic');
r = regexprep(regexprep(r, '<a href="[^"]*"[^>]*>', ''), '</a>', '');
chk(key, [fr(e.stack) ' # ' strjoin(splitlines(strtrim(r)), ' / ')]);
end

function s = fr(s)
parts = cell(1, numel(s));
for q = 1:numel(s)
    parts{q} = sprintf('%s@%d(%s)', s(q).name, s(q).line, sf(s(q).file));
end
s = sprintf('n=%d %s', numel(s), strjoin(parts, ' ; '));
end

function f = sf(f)
global OI334LIB
f = strrep(f, OI334LIB, '<LIB>');
f = strrep(f, fileparts(mfilename('fullpath')), '<DIR>');
f = regexprep(f, '^.*\\toolbox\\', '<ML>\\toolbox\\');
end

function t = printed(t)
t = regexprep(regexprep(t, '<a href="[^"]*"[^>]*>', ''), '</a>', '');
t = strjoin(splitlines(strtrim(t)), ' / ');
end

function s = callcomplete(fn)
st = fn();
s = sprintf('%s', st(1).file);
end

function varargout = callit(fn)
[varargout{1:nargout}] = fn();
end

function callp(fn)
fn();
end

function r = failing(~)
error('oi334:loc', 'boom');
end

function m = makefail()
m = @() error('oi334:m', 'boom');
end

function r = innercatch(~)
try
    error('oi334:ic', 'boom');
catch e
    st('innercatch_inside', e);
end
r = 1;
end

function r = caughtinside()
try
    cellfun(@(x) error('oi334:ci', 'boom'), {1});
catch e
    r = fr(e.stack);
end
end

function infn(fn)
try
    fn();
catch e
    throwAsCaller(e);
end
end

function rethrowit()
try
    feval(@() error('oi334:rt', 'boom'));
catch e
    rethrow(e);
end
end

function put(path, lines)
fid = fopen(path, 'w');
for q = 1:numel(lines)
    fprintf(fid, '%s\n', lines{q});
end
fclose(fid);
end

function chk(name, v, rule)
if nargin < 3
    rule = 'exact';
end
fprintf('CHK|%s|%s|%s\n', name, v, rule);
end
