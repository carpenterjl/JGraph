function u4_wait2
% U4 probe (headless), second half: u4_wait stops at a wait started inside a timer's callback, which
% no other timer can end in R2025b (timer callbacks do not nest). This is the rest of it.
f = figure('Visible', 'off');
c = uicontrol(f, 'Style', 'checkbox', 'Tag', 'c');
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
