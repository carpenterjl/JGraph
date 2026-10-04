function u4_wait
% U4 probe (headless): uiwait, uiresume, WaitStatus and waitfor in R2025b.
f = figure('Visible', 'off');
c = uicontrol(f, 'Style', 'checkbox', 'Tag', 'c');
say('WaitStatus default=[%s] class=%s', get(f, 'WaitStatus'), class(get(f, 'WaitStatus')));
say('WaitStatus listed by get=%d set=%d isprop=%d', isfield(get(f), 'WaitStatus'), isfield(set(f), 'WaitStatus'), isprop(f, 'WaitStatus'));
attempt('set WaitStatus waiting', @() set(f, 'WaitStatus', 'waiting'));
say('  -> [%s]', get(f, 'WaitStatus'));
attempt('set WaitStatus inactive', @() set(f, 'WaitStatus', 'inactive'));
attempt('set WaitStatus bad', @() set(f, 'WaitStatus', 'bad'));
attempt('set WaitStatus 1', @() set(f, 'WaitStatus', 1));
attempt('set WaitStatus WAITING', @() set(f, 'WaitStatus', 'WAITING'));
say('  -> [%s]', get(f, 'WaitStatus'));
set(f, 'WaitStatus', 'inactive');
attempt('set WaitStatus wait (abbrev)', @() set(f, 'WaitStatus', 'wait'));
say('  -> [%s]', get(f, 'WaitStatus'));
set(f, 'WaitStatus', 'inactive');
attempt('uicontrol WaitStatus', @() get(c, 'WaitStatus'));

% --- argument checks
attempt('uiwait(5.5)', @() uiwait(5.5));
attempt('uiwait(c)', @() uiwait(c));
attempt('uiwait([f f])', @() uiwait([f f]));
attempt('uiwait(''a'')', @() uiwait('a'));
attempt('uiwait(f, ''a'')', @() uiwait(f, 'a'));
attempt('uiwait(f, 1, 2)', @() uiwait(f, 1, 2));
attempt('uiresume(5.5)', @() uiresume(5.5));
attempt('uiresume(c)', @() uiresume(c));
attempt('uiresume(f) when not waiting', @() uiresume(f));
attempt('uiresume(f, 1)', @() uiresume(f, 1));
attempt('uiresume([f f])', @() uiresume([f f]));
attempt('x = uiwait(f, 1)', @() nout1(@() uiwait(f, 1)));
attempt('x = uiresume(f)', @() nout1(@() uiresume(f)));

% --- timeouts
lastwarn('');
t = tic; uiwait(f, 1); e = toc(t);
[msg, id] = lastwarn;
say('uiwait(f,1): %.1fs status=[%s] Visible=%s lastwarn=[%s] [%s]', e, f.WaitStatus, char(f.Visible), id, msg);
lastwarn('');
t = tic; uiwait(f, 0.2); e = toc(t);
[msg, id] = lastwarn;
say('uiwait(f,0.2): %.1fs lastwarn=[%s] [%s]', e, id, msg);
t = tic; uiwait(f, 1.5); e = toc(t);
say('uiwait(f,1.5): %.1fs', e);
t = tic; uiwait(f, single(1)); e = toc(t);
say('uiwait(f,single(1)): %.1fs', e);
attempt('uiwait(f, [1 2])', @() uiwait(f, [1 2]));
attempt('uiwait(f, [])', @() uiwait(f, []));
attempt('uiwait(f, NaN)', @() uiwait(f, NaN));
attempt('uiwait(f, true)', @() uiwait(f, true));

% --- what ends it
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume(f)); start(tm);
t = tic; uiwait(f); say('timer uiresume: %.1fs status=[%s]', toc(t), f.WaitStatus); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'WaitStatus', 'inactive')); start(tm);
t = tic; uiwait(f); say('timer set inactive: %.1fs', toc(t)); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume(f)); start(tm);
t = tic; uiwait(f, 5); say('timer uiresume before timeout: %.1fs timers left=%d', toc(t), numel(timerfindall) - 1); delete(tm);
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(g)); start(tm);
t = tic; uiwait(g); say('timer delete: %.1fs valid=%d', toc(t), isvalid(g)); delete(tm);
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) close(g)); start(tm);
t = tic; uiwait(g); say('timer close: %.1fs valid=%d', toc(t), isvalid(g)); delete(tm);

% --- the status while waiting, and a wait inside a wait
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.3, 'TimerFcn', @(~, ~) inner(f, g)); start(tm);
t = tic; uiwait(f, 4); say('outer wait: %.1fs', toc(t)); delete(tm);
delete(g);

% --- uiwait with no argument uses gcf
h = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume(h)); start(tm);
t = tic; uiwait; say('uiwait bare: %.1fs', toc(t)); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume); start(tm);
t = tic; uiwait(h); say('uiresume bare from a timer: %.1fs', toc(t)); stop(tm); delete(tm);
delete(h);

% --- waitfor
attempt('waitfor()', @() waitfor());
attempt('waitfor(5.5)', @() waitfor(5.5));
attempt('waitfor(''a'')', @() waitfor('a'));
attempt('waitfor(f, ''NoSuch'')', @() waitfor(f, 'NoSuch'));
attempt('waitfor(f, 5)', @() waitfor(f, 5));
attempt('waitfor(f, ''Name'', ''x'', 4)', @() waitfor(f, 'Name', 'x', 4));
attempt('x = waitfor(f, ''Name'', '''')', @() nout1(@() waitfor(f, 'Name', '')));
d = figure('Visible', 'off'); delete(d);
t = tic; attempt('waitfor(deleted)', @() waitfor(d)); say('  %.1fs', toc(t));
t = tic; attempt('waitfor(deleted, ''Name'')', @() waitfor(d, 'Name')); say('  %.1fs', toc(t));
t = tic; waitfor(f, 'Name', ''); say('waitfor already equal: %.1fs', toc(t));
t = tic; waitfor(c, 'Value', 0); say('waitfor Value already 0: %.1fs', toc(t));
t = tic; waitfor(c, 'Value', false); say('waitfor Value false (logical): %.1fs', toc(t));
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(c, 'Value', 1)); start(tm);
t = tic; waitfor(c, 'Value', 1); say('waitfor Value 1: %.1fs', toc(t)); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(c, 'Value', 0)); start(tm);
t = tic; waitfor(c, 'Value'); say('waitfor Value change: %.1fs', toc(t)); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(c, 'Value', 0)); start(tm);
tm2 = timer('StartDelay', 1.2, 'TimerFcn', @(~, ~) set(c, 'Value', 1)); start(tm2);
t = tic; waitfor(c, 'Value'); say('waitfor Value set to the same, then changed: %.1fs', toc(t)); delete([tm tm2]);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'Name', 'abc')); start(tm);
t = tic; waitfor(f, 'name', 'abc'); say('waitfor name (lower case): %.1fs', toc(t)); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'Name', 'ABCD')); start(tm);
tm2 = timer('StartDelay', 1.2, 'TimerFcn', @(~, ~) set(f, 'Name', 'abcd')); start(tm2);
t = tic; waitfor(f, 'Name', 'abcd'); say('waitfor Name is case sensitive in the value: %.1fs', toc(t)); delete([tm tm2]);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(c)); start(tm);
t = tic; waitfor(c, 'Value', 5); say('waitfor ended by delete: %.1fs', toc(t)); delete(tm);
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(g)); start(tm);
t = tic; waitfor(g); say('waitfor(fig) ended by delete: %.1fs', toc(t)); delete(tm);
% a UserData change
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'UserData', struct('a', 1))); start(tm);
t = tic; waitfor(f, 'UserData'); say('waitfor UserData change: %.1fs', toc(t)); delete(tm);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'UserData', [1 2 3])); start(tm);
t = tic; waitfor(f, 'UserData', [1 2 3]); say('waitfor UserData value: %.1fs', toc(t)); delete(tm);
% waitfor on a timer object / other
delete(f);
delete(timerfindall);
end

function inner(f, g)
say('  in timer: f status=[%s]', f.WaitStatus);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume(g)); start(tm);
t = tic; uiwait(g); say('  inner wait: %.1fs; f status=[%s] g status=[%s]', toc(t), f.WaitStatus, g.WaitStatus);
delete(tm);
uiresume(f);
end

function x = nout1(fn)
x = fn();
end

function attempt(label, fn)
lastwarn('');
try
    fn();
    [msg, id] = lastwarn;
    if isempty(msg)
        say('%s: ok', label);
    else
        say('%s: ok, warned [%s] %s', label, id, msg);
    end
catch e
    say('%s: ERR [%s] %s', label, e.identifier, strrep(e.message, newline, ' / '));
end
end

function say(fmt, varargin)
fprintf([fmt '\n'], varargin{:});
end
