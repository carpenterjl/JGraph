% record: -noFigureWindows
% Open item 336 (ADR 0378): getReport's default ('extended') form - the header (the frame error() raised in, named
% as R2025b names a class method, a package function, a nested function and a property's set method; none for a
% refusal of the runtime's), the message, an "Error in" block per frame with its line of code and the carets under
% the expression it ran, and the causes - and the anonymous frames R2025b leaves out (a text holding >%).
% A report prints as its lines joined by ' / '.
global OI336LIB
lib = tempname;
mkdir(lib);
OI336LIB = lib;
mkdir(fullfile(lib, '+oi336pk'));
put(fullfile(lib, '+oi336pk', 'pferr.m'), {'function pferr()', 'error(''oi336:pk'', ''pkg boom'');', 'end'});
put(fullfile(lib, '+oi336pk', 'pfloc.m'), {'function pfloc()', 'inner();', 'end', 'function inner()', 'error(''oi336:pkl'', ''pkg local boom'');', 'end'});
put(fullfile(lib, '+oi336pk', 'PC.m'), {'classdef PC', '    methods', '        function insterr(~)', '            error(''oi336:pci'', ''pkg inst boom'');', ...
    '        end', '        function obj = PC(v)', '            if nargin, error(''oi336:ctor'', ''ctor boom''); end', '        end', '    end', ...
    '    methods (Static)', '        function staterr()', '            error(''oi336:pcs'', ''pkg stat boom'');', '        end', ...
    '        function statloc()', '            pcloc();', '        end', '    end', 'end', 'function pcloc()', 'error(''oi336:pcl'', ''pkg class local boom'');', 'end'});
put(fullfile(lib, 'OI336C.m'), {'classdef OI336C', '    methods', '        function insterr(~)', '            error(''oi336:ci'', ''inst boom'');', ...
    '        end', '    end', '    methods (Static)', '        function staterr()', '            error(''oi336:cs'', ''stat boom'');', '        end', ...
    '        function statloc()', '            cloc();', '        end', '    end', 'end', 'function cloc()', 'error(''oi336:cl'', ''class local boom'');', 'end'});
put(fullfile(lib, 'OI336H.m'), {'classdef OI336H < handle', '    properties', '        V = 1', '    end', '    methods', '        function insterr(obj)', ...
    '            error(''oi336:h'', ''handle boom'');', '        end', '        function set.V(obj, v)', '            if v < 0, error(''oi336:set'', ''set boom''); end', ...
    '            obj.V = v;', '        end', '    end', 'end'});
put(fullfile(lib, 'oi336nest.m'), {'function oi336nest()', 'inner();', '    function inner()', '        error(''oi336:n'', ''nested boom'');', '    end', 'end'});
put(fullfile(lib, 'oi336deep.m'), {'function oi336deep()', 'mid();', 'end', 'function mid()', 'error(''oi336:deep'', ''deep boom %d'', 3);', 'end'});
put(fullfile(lib, 'oi336fn.m'), {'function oi336fn()', 'x = [1 2];', 'y = x(5);', 'end'});
addpath(lib);

% --- headers: a class method, a package function, a nested function, a set method --------------
try
    OI336C.staterr();
catch e
    rep('static', e);
end
try
    o = OI336C();
    o.insterr();
catch e
    rep('instance', e);
end
try
    OI336C.statloc();
catch e
    rep('class_local', e);
end
try
    oi336pk.PC.staterr();
catch e
    rep('pkg_static', e);
end
try
    q = oi336pk.PC();
    q.insterr();
catch e
    rep('pkg_instance', e);
end
try
    oi336pk.PC.statloc();
catch e
    rep('pkg_class_local', e);
end
try
    oi336pk.PC(1);
catch e
    rep('pkg_ctor', e);
end
try
    oi336pk.pferr();
catch e
    rep('pkg_fn', e);
end
try
    oi336pk.pfloc();
catch e
    rep('pkg_fn_local', e);
end
try
    oi336nest();
catch e
    rep('nested', e);
end
try
    h = OI336H();
    h.V = -1;
catch e
    rep('set_method', e);
end
try
    h = OI336H();
    h.insterr();
catch e
    rep('handle_instance', e);
end
try
    OI336C.staterr();
catch e
    chk('static_basic', flat(getReport(e, 'basic')));
end
try
    oi336pk.pferr();
catch e
    chk('pkg_fn_basic', flat(getReport(e, 'basic')));
end

% --- the "Error in" blocks and their carets -----------------------------------------------------
try
    error('oi336:top', 'top boom');
catch e
    rep('top_error', e);
end
try
    x = [1 2]; y = x(5);
catch e
    rep('top_refusal', e);
end
try
    q = 1;   oi336deep();   % trailing
catch e
    rep('deep', e);
end
try
    y = 3 + localerr2(4) * 2;
catch e
    rep('in_expr', e);
end
try
    y = localwrap(localerr2(4));
catch e
    rep('nested_arg', e);
end
try
    y = localwrap( ...
        localerr2(4));
catch e
    rep('continued', e);
end
try
    f = @(x) localerr2(x);
    f(1);
catch e
    rep('anon_line', e);
end
try
    cellfun(@(x) localerr2(x), {1});
catch e
    rep('anon_cellfun', e);
end
try
    g = @(x) x(5);
    g([1 2]);
catch e
    rep('anon_refusal', e);
end
try
    if localerr2(1) > 0, end
catch e
    rep('if_cond', e);
end
try
    for k = localerr2(1), end
catch e
    rep('for_head', e);
end
try
    s.a = 1; s.b.c(3);
catch e
    rep('field_refusal', e);
end
try
    z = undefinedthing336 + 1;
catch e
    rep('undefined', e);
end
try
    z = localwrap(undefinedthing336);
catch e
    rep('undefined_arg', e);
end
try
    localrefuse2();
catch e
    rep('local_refusal', e);
end
try
    oi336fn();
catch e
    rep('file_refusal', e);
end
try
	z = localerr2(1);	% tab indented
catch e
    rep('tab', e);
end
try
    z = localerr2(1); w = localerr2(2);
catch e
    rep('two_calls', e);
end
try
    z = [1 2 3]; w = {z(1), z(9)};
catch e
    rep('refusal_in_list', e);
end
try
    localthrow();
catch e
    rep('throw', e);
end
try
    localrethrow();
catch e
    rep('rethrow', e);
end
try
    localasc();
catch e
    rep('throwascaller', e);
end
try
    localcause();
catch e
    rep('cause', e);
end
try
    warning('error', 'oi336:w'); %#ok<*WNTAG>
    localwarn();
catch e
    rep('warn_err', e);
end
warning('on', 'oi336:w');
try
    z = str2func('@(x) error(''oi336:s'', ''strf'')'); z(1);
catch e
    rep('str2func', e);
end
try
    localnargs(1, 2, 3);
catch e
    rep('too_many_in', e);
end
try
    [a, b] = localone();
catch e
    rep('too_many_out', e);
end
try
    cellfun(@(x) localerr2(x), {1}, 'UniformOutput', false);
catch e
    rep('cellfun_options', e);
end
chk('inner_catch', innercatch());
cellfun(@(x) innercatchanon(x), {1});

% --- eval'd text ----------------------------------------------------------------------------
try
    eval('error(''oi336:ev'', ''in eval'');');
catch e
    rep('eval_error', e);
end
try
    eval('localerr2(3);');
catch e
    rep('eval_local', e);
end
try
    eval('x = [1 2]; x(5);');
catch e
    rep('eval_refusal', e);
end
try
    evalthere();
catch e
    rep('eval_in_function', e);
end

% --- causes, and an exception never thrown ----------------------------------------------------
me = MException('oi336:o', 'outer'); me = addCause(me, MException('oi336:i', 'inner'));
chk('unthrown_cause', flat(getReport(me)));
chk('unthrown_cause_basic', flat(getReport(me, 'basic')));
try
    throw(me);
catch e
    rep('thrown_cause', e);
    chk('thrown_cause_basic', flat(getReport(e, 'basic')));
end
chk('unthrown', flat(getReport(MException('oi336:nt', 'not thrown %d', 4))));
try
    x = [1 2]; x(5);
catch e
    chk('extended_hyperlinks_off', flat(getReport(e, 'extended', 'hyperlinks', 'off')));
end

% --- anonymous frames R2025b leaves out: a text holding >% ------------------------------------
try
    cellfun(@(x) error('oi336:a', 'x>%d', x > 1), {2});
catch e
    rep('cf_gt_fmt', e);
end
try
    cellfun(@(x) error('oi336:a', 'x %d', x > 1), {2});
catch e
    rep('cf_sp_fmt_gtarg', e);
end
try
    cellfun(@(x) error('oi336:a', '%d>x', 1), {2});
catch e
    rep('cf_fmt_first', e);
end
try
    cellfun(@(x) error('oi336:a', 'x>a%d', 1), {2});
catch e
    rep('cf_gt_a_pct', e);
end
try
    cellfun(@(x) error('x>%d'), {2});
catch e
    rep('cf_noid_pct', e);
end
try
    cellfun(@(x) error('x>'), {2});
catch e
    rep('cf_noid_gt', e);
end
try
    cellfun(@(x) localerrfmt('x>%d', 1), {2});
catch e
    rep('cf_viafn', e);
end
try
    cellfun(@(x) [x(3) '%>'], {2});
catch e
    rep('cf_refusal_pct_gt', e);
end
try
    cellfun(@(x) [x(3) '>%'], {2});
catch e
    rep('cf_refusal_gt_pct', e);
end
try
    h = @() error('oi336:a', 'x>%d', 1); h();
catch e
    rep('direct_gt_fmt', e);
end
try
    h = @() error('oi336:a', 'x %d', 1); h();
catch e
    rep('direct_sp_fmt', e);
end
chk('dbstack_gt_pct', fr(callp(@() dbstack(numel('>%') * 0))));
chk('dbstack_pct_gt', fr(callp(@() dbstack(numel('%>') * 0))));

% --- built-in refusals R2025b heads with the built-in's name ----------------------------------
try
    z = zeros('a');
catch e
    chk('builtin_header', sprintf('%d', startsWith(getReport(e), 'Error using zeros')), 'div=ADR0378');
end
rmpath(lib);
rmdir(lib, 's');

function rep(key, e)
chk(key, [fr(e.stack) ' # ' flat(getReport(e))]);
end

function t = flat(t)
t = strjoin(splitlines(t), ' / ');
end

function s = fr(s)
parts = cell(1, numel(s));
for q = 1:numel(s)
    parts{q} = sprintf('%s@%d', s(q).name, s(q).line);
end
s = sprintf('n=%d %s', numel(s), strjoin(parts, ' ; '));
end

function s = callp(fn)
s = fn();
end

function r = localerr2(v)
error('oi336:l2', 'l2 boom %d', v);
r = v;
end

function r = localerrfmt(varargin)
error('oi336:lf', varargin{:});
r = 1;
end

function r = localwrap(v)
r = v;
end

function localrefuse2()
x = [1 2];
y = x(7);
end

function localthrow()
me = MException('oi336:th', 'thrown');
throw(me);
end

function localrethrow()
try
    error('oi336:rt', 'rethrown');
catch me
    rethrow(me);
end
end

function localasc()
me = MException('oi336:asc', 'as caller');
throwAsCaller(me);
end

function localcause()
try
    error('oi336:inner', 'inner one');
catch me
    outer = MException('oi336:outer', 'outer one');
    outer = addCause(outer, me);
    throw(outer);
end
end

function localwarn()
warning('oi336:w', 'warned');
end

function localnargs(a)
end

function r = localone()
r = 1;
end

function r = innercatch()
try
    localerr2(5);
catch e
    r = [fr(e.stack) ' # ' flat(getReport(e))];
end
end

function r = innercatchanon(~)
try
    localerr2(6);
catch e
    chk('inner_catch_anon', [fr(e.stack) ' # ' flat(getReport(e))]);
end
r = 1;
end

function evalthere()
eval('error(''oi336:evf'', ''eval in function'');');
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
