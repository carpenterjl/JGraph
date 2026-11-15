% record: -noFigureWindows
% Open item 302 (ADR 0366): dbstack inside an anonymous function, a script run by name as a frame, the
% struct's file, the printed form (the marked frame, an anonymous frame's missing line, a script's local
% functions), dbstack's arguments, and inmem listing the scripts run as programs. Frames print as
% name@line(file), the fixture's own folder as <DIR>.
f = @() dbstack;
frames('anon', f());
frames('direct', dbstack);
frames('callit', callit(@() dbstack));
g = @() feval(@() dbstack);
frames('nested', g());
h = @(n) dbstack(n);
frames('skip1', h(1));
frames('complete', feval(@() dbstack('-completenames')));
[s, i] = feval(@() dbstack);
chk('index', sprintf('%d %d', numel(s), i));
frames('cellfun', cellfun(@(x) dbstack, {1}, 'UniformOutput', false));
frames('localanon', makeanon());
frames('localdirect', localdirect());
frames('arrayfun_local', arrayfun(@(x) localdirect(), 1, 'UniformOutput', false));
k = makeanon2();
frames('madeelsewhere', callit(k));
chk('count', sprintf('%d', callit(@() numel(dbstack))));
a = @(x, y) x + numel(dbstack) + y;
chk('args_count', sprintf('%d', a(0, 0)));
d = @( v )   frame2name(  v , 'x' );
chk('spaced_name', d(1));
frames('str2func', feval(str2func('@() dbstack')));
eval('e = @() dbstack;');
frames('eval_made', e());
frames('continued', feval(@() ...
    dbstack));

% --- printed ------------------------------------------------------------------------------------
printed('printed_anon', evalc('callp(@() dbstack)'));
printed('printed_local', evalc('callp(@() innerp())'));
printed('printed_complete', evalc('callp(@() dbstack(''-completenames''))'));
printed('printed_direct', evalc('innerp()'));
printed('printed_skip', evalc('callp(@() dbstack(1))'));

% --- arguments ----------------------------------------------------------------------------------
cases = {'dbstack 1', 'dbstack -completenames', 'dbstack -foo', 'dbstack(''-completenames'', 1)', ...
    'dbstack(1, ''-completenames'')', 'dbstack(1, 1)', 'dbstack(int8(1))', 'dbstack(single(0))', ...
    'dbstack(''-COMPLETENAMES'')', 'dbstack({1})', 'dbstack([])', 'dbstack(''-completenames'', ''-completenames'')', ...
    'dbstack(2.5)', 'dbstack(-1)', 'dbstack([1 2])', 'dbstack("1")', 'dbstack(true)', 'dbstack(''1'')'};
for q = 1:numel(cases)
    try
        t = evalc(cases{q});
        chk(['arg ' cases{q}], sprintf('ok %d', numel(splitlines(strtrim(t))) * ~isempty(strtrim(t))));
    catch err
        chk(['arg ' cases{q}], [err.identifier ' / ' err.message]);
    end
end

% --- scripts as frames, and inmem ---------------------------------------------------------------
lib = tempname;
mkdir(lib);
put(fullfile(lib, 'oi302s.m'), {'oi302_ss = dbstack;', 'oi302_sa = feval(@() dbstack);', 'oi302_sm = inmem;'});
put(fullfile(lib, 'oi302r.m'), {'oi302_rm = inmem;'});
put(fullfile(lib, 'oi302f.m'), {'function oi302f()', 'end'});
put(fullfile(lib, 'oi302t.m'), {'assignin(''base'', ''oi302_ts'', dbstack);'});
addpath(lib);
mm = inmem;
chk('inmem_main', ours(mm));
oi302s;
frames('script', oi302_ss);
frames('script_anon', oi302_sa);
chk('inmem_in_script', ours(oi302_sm));
chk('inmem_after_script', ours(inmem));
oi302f;
chk('inmem_after_function', ours(inmem));
run(fullfile(lib, 'oi302r.m'));
chk('inmem_in_run', ours(oi302_rm));
chk('inmem_after_run', ours(inmem));
mm = inmem('-completenames');
chk('inmem_complete_main', sprintf('%d', any(strcmp(mm, [mfilename('fullpath') '.m']))));
clear oi302s
chk('inmem_clear_name', ours(inmem));
clear functions
chk('inmem_clear_functions', ours(inmem));
frames('script_in_anon', scriptinanon());
rmpath(lib);
rmdir(lib, 's');

function frames(key, s)
if iscell(s)
    s = s{1};
end
parts = cell(1, numel(s));
for q = 1:numel(s)
    parts{q} = sprintf('%s@%d(%s)', s(q).name, s(q).line, strrep(s(q).file, fileparts(mfilename('fullpath')), '<DIR>'));
end
chk(key, sprintf('n=%d %s', numel(s), strjoin(parts, ' ; ')));
end

function printed(key, t)
t = regexprep(regexprep(t, '<a href="[^"]*"[^>]*>', ''), '</a>', '');
t = strrep(t, fileparts(mfilename('fullpath')), '<DIR>');
chk(key, strjoin(splitlines(strtrim(t)), ' / '));
end

function s = ours(m)
s = strjoin(m(startsWith(m, 'oi302'))', ',');
end

function varargout = callit(fn)
[varargout{1:nargout}] = fn();
end

function callp(fn)
fn();
end

function innerp()
dbstack;
end

function s = makeanon()
an = @() dbstack;
s = an();
end

function k = makeanon2()
k = @() dbstack;
end

function s = localdirect()
s = dbstack;
end

function r = frame2name(~, ~)
s = dbstack;
r = s(2).name;
end

function s = scriptinanon()
fs = @() oi302t;
fs();
s = evalin('base', 'oi302_ts');
end

function put(path, lines)
fid = fopen(path, 'w');
for q = 1:numel(lines)
    fprintf(fid, '%s\n', lines{q});
end
fclose(fid);
end

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, v);
end
