function u9b_bridge
% U9b probe: the uihtml bridge in R2025b, headless. Conversions both ways through Data and
% through events, what the page side looks like, the order of callbacks, and the sendEventToHTMLSource
% forms. Uses site/echo.html (the fixtures' protocol) and site/shape.html.
global LOG
LOG = {};
here = fileparts(mfilename('fullpath'));
addpath(here);
site = fullfile(here, 'site');
uf = uifigure('Visible', 'off');

%% the page side (shape.html)
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent, 'DataChangedFcn', @onData);
h.HTMLSource = fullfile(site, 'shape.html');
waitFor('laterOrder', 30);
h.Data = struct('a', 1);
waitFor('dcEvent', 5);
sendEventToHTMLSource(h, 'probe', struct('x', 1));
waitFor('mEvent', 5);
sendEventToHTMLSource(h, 'twice');
waitFor('twiceCount', 5);
sendEventToHTMLSource(h, 'removed');
waitFor('removedDone', 5);
sendEventToHTMLSource(h, 'order3');
waitFor('order3', 5);
pause(0.5); drawnow;
sendEventToHTMLSource(h, 'odd');
waitFor('oddDone', 15);
sendEventToHTMLSource(h, 'oddsend');
waitFor('oddsendDone', 15);
sendEventToHTMLSource(h, 'argc');
waitFor('argc3', 5);
sendEventToHTMLSource(h, 'selfset');
waitFor('selfsetDone', 5);
sendEventToHTMLSource(h, 'setthensend');
waitFor('afterSet', 5);
pause(0.5); drawnow;
dump('shape');

%% MATLAB -> page through Data (echo.html)
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent, 'DataChangedFcn', @onData);
h.HTMLSource = fullfile(site, 'echo.html');
waitFor('ready', 30);
dump('echo ready');
vals = {1.5, -0, 0.1 + 0.2, pi, 1/3, 1e21, 1e-7, 123456789012345678, 1e300, realmin, [1 NaN 3], [NaN NaN], [Inf -Inf], ...
    int64(9007199254740993), intmax('uint64'), int8(-5), single(pi), uint8([1 2 3]), true(2), [true; false], ...
    'a"b\c', sprintf('line1\nline2'), char(233), char([55357 56832]), ['ab'; 'cd'], '', "", string(missing), ["a" missing], strings(0, 1), ...
    {}, cell(0, 3), {1, 'x'; 2, 'y'}, {[]}, {{1}}, struct(), struct('a', {}), struct('a', {1; 2}), struct('a', [1 2; 3 4]), ...
    struct('a', struct('b', [])), zeros(2, 2, 2), [], zeros(0, 3), [1; 2; 3], magic(3), 1:3, ...
    datetime(2024, 1, 15), [datetime(2024, 1, 15) datetime(2024, 1, 16)], seconds(90), categorical({'a', 'b'}), ...
    table([1; 2], {'x'; 'y'}), containers.Map({'k1', 'k2'}, {1, 'v'}), @sin, matlab.lang.OnOffSwitchState.on, sparse([1 0 2]), ...
    1 + 2i, uf, 5, 5, 6};
for k = 1:numel(vals)
    n = numel(LOG);
    lastwarn('');
    try
        h.Data = vals{k};
        got = waitNew('event echo', n, 2);
        [wm, wid] = lastwarn;
        w = '';
        if ~isempty(wm), w = sprintf('  WARN %s | %s', wid, oneline(wm)); end
        fprintf('Data<- %-40s reads %s | page %s%s\n', deep(vals{k}), deep(h.Data), got, w);
    catch e
        fprintf('Data<- %-40s ERR %s | %s\n', deep(vals{k}), e.identifier, oneline(e.message));
    end
end

%% sendEventToHTMLSource conversions (ping -> pong)
sends = {1.5, [1 2 3], [1; 2; 3], magic(3), [], zeros(1, 0), 'text', "str", ["a" "b"], {1, 'x'}, struct('a', {1, 2}), true, ...
    NaN, int8(5), datetime(2024, 1, 15), seconds(90), {}, struct(), containers.Map({'k'}, {1}), 1 + 2i, @sin, ...
    categorical({'a'}), zeros(2, 2, 2), string(missing), uf};
for k = 1:numel(sends)
    n = numel(LOG);
    lastwarn('');
    try
        sendEventToHTMLSource(h, 'ping', sends{k});
        got = waitNew('event pong', n, 2);
        [wm, wid] = lastwarn;
        w = '';
        if ~isempty(wm), w = sprintf('  WARN %s | %s', wid, oneline(wm)); end
        fprintf('send  %-40s page %s%s\n', deep(sends{k}), got, w);
    catch e
        fprintf('send  %-40s ERR %s | %s\n', deep(sends{k}), e.identifier, oneline(e.message));
    end
end
n = numel(LOG);
sendEventToHTMLSource(h, 'ping');
fprintf('send  (no data)                              page %s\n', waitNew('event pong', n, 2));

%% page -> MATLAB: Data set by the page (set), then events sent by the page (send)
texts = {'1.5', '[1,2,3]', '[[1,2],[3,4]]', '[[1,2],[3,4,5]]', '[]', '[[]]', '[[],[]]', '{}', '[{}]', '[{},{}]', ...
    '[{"a":1},{"a":2}]', '[{"a":1},{"b":2}]', '[{"a":[1,2]},{"a":[3,4]}]', '{"1a":1,"a b":2,"":3,"a-b":4}', ...
    '{"a":1,"A":2}', '[1,null,3]', '[null,null]', '[true,false]', '[true,null]', '[true,1]', '["a","b"]', '["a",null]', ...
    '[1,"a"]', '"text"', '""', '[""]', 'null', 'true', 'false', '0', '-0', '{"a":[1,2,3]}', '{"a":{"b":[]}}', '{"a":null}', ...
    '[[[1,2],[3,4]],[[5,6],[7,8]]]', '[[1],[2]]', '[["a","b"],["c","d"]]', '{"x":[{"y":1},{"y":2}]}', '123456789012345678', ...
    '1e-320', '1e308', '"2024-01-15"', '[[1,2],[]]', '[1.5]', '["x"]', '[{"a":1}]', '[[true,false],[false,true]]', ...
    '{"a":[[1,2],[3,4]]}', '[[1,2],["a","b"]]', '__undefined__', '__nan__', '__inf__', '__ninf__'};
for k = 1:numel(texts)
    n = numel(LOG);
    sendEventToHTMLSource(h, 'set', texts{k});
    got = waitNew('event setdone', n, 2);
    dc = LOG(n+1:end);
    dc = dc(startsWith(dc, 'DataChangedFcn'));
    if isempty(dc), dc = {'(no DataChangedFcn)'}; end
    fprintf('pageData %-36s %s | page %s\n', texts{k}, strjoin(dc, ' // '), got);
end
for k = 1:numel(texts)
    n = numel(LOG);
    sendEventToHTMLSource(h, 'send', struct('name', 'ev', 'json', texts{k}));
    got = waitNew('event ev', n, 2);
    fprintf('pageEvent %-36s %s\n', texts{k}, got);
end
n = numel(LOG);
sendEventToHTMLSource(h, 'send', struct('name', 'ev'));
fprintf('pageEvent (no data)                           %s\n', waitNew('event ev', n, 2));

%% dedupe between the two sides
step(h, 'page sets [1,2,3]', @() sendEventToHTMLSource(h, 'set', '[1,2,3]'));
step(h, 'MATLAB sets [1;2;3] (same JSON)', @() set(h, 'Data', [1; 2; 3]));
step(h, 'MATLAB sets [1 2 3] (same JSON, other shape)', @() set(h, 'Data', [1 2 3]));
step(h, 'MATLAB sets 7', @() set(h, 'Data', 7));
step(h, 'MATLAB sets 7 again', @() set(h, 'Data', 7));
step(h, 'page sets 7', @() sendEventToHTMLSource(h, 'set', '7'));
step(h, 'page sets 7 again', @() sendEventToHTMLSource(h, 'set', '7'));
step(h, 'MATLAB sets 8 then 9 then 8, no drawnow between', @() threeSets(h));
step(h, 'MATLAB sets 10 then pings, no drawnow between', @() setThenPing(h));
step(h, 'MATLAB pings then sets 11, no drawnow between', @() pingThenSet(h));
step(h, 'set(h,Data,12)', @() set(h, 'Data', 12));
step(h, 'MATLAB sets NaN', @() set(h, 'Data', NaN));
step(h, 'MATLAB sets Inf (same JSON null)', @() set(h, 'Data', Inf));
step(h, 'MATLAB sets 1+2i (fails to encode)', @() set(h, 'Data', 1 + 2i));
step(h, 'MATLAB sets 1+2i again', @() set(h, 'Data', 1 + 2i));
step(h, 'MATLAB sets 13 after the failure', @() set(h, 'Data', 13));

%% loading: events sent before the page is ready, a change of source, markup
step(h, 'new source then ping at once', @() sourceThenPing(h, fullfile(site, 'echo.html')));
step(h, 'Data 14 then new source', @() dataThenSource(h, fullfile(site, 'echo.html')));
step(h, 'markup source', @() set(h, 'HTMLSource', '<p>markup</p>'));
step(h, 'back to echo', @() set(h, 'HTMLSource', fullfile(site, 'echo.html')));
step(h, 'same source again', @() set(h, 'HTMLSource', fullfile(site, 'echo.html')));
markup = fileread(fullfile(site, 'echo.html'));
step(h, 'echo as markup', @() set(h, 'HTMLSource', markup));
step(h, 'invisible', @() set(h, 'Visible', 'off'));
step(h, 'ping while invisible', @() sendEventToHTMLSource(h, 'ping', 1));
step(h, 'visible again', @() set(h, 'Visible', 'on'));
step(h, 'figure invisible ping', @() sendEventToHTMLSource(h, 'ping', 2));

%% event data objects
n = numel(LOG);
global EVT
EVT = {};
h.HTMLEventReceivedFcn = @(s, e) keepEvt(s, e);
h.DataChangedFcn = @(s, e) keepEvt(s, e);
sendEventToHTMLSource(h, 'send', struct('name', 'evx', 'json', '[1,2]'));
sendEventToHTMLSource(h, 'set', '{"z":1}');
t = tic; while toc(t) < 3 && numel(EVT) < 2, drawnow; pause(0.05); end
for k = 1:numel(EVT)
    e = EVT{k}{2};
    fprintf('evt class=%s srcIsH=%d\n', class(e), isequal(EVT{k}{1}, h));
    fprintf('evt properties: %s\n', strjoin(properties(e)', ' '));
    fprintf('evt methods: %s\n', strjoin(methods(e)', ' '));
    fprintf('evt EventName=%s Source=%s\n', e.EventName, class(e.Source));
    fprintf('evt display:\n%s\n', evalc('disp(e)'));
    fprintf('evt named display:\n%s\n', evalc('e'));
    try, fprintf('evt struct: %s\n', deep(struct(e))); catch ex, fprintf('evt struct ERR %s\n', ex.identifier); end
    try, e.EventName = 'x'; fprintf('evt set EventName ok\n'); catch ex, fprintf('evt set EventName ERR %s | %s\n', ex.identifier, oneline(ex.message)); end
    try, fprintf('evt isa event.EventData %d\n', isa(e, 'event.EventData')); catch ex, fprintf('evt isa ERR %s\n', ex.identifier); end
end

%% sendEventToHTMLSource forms
h2 = uihtml(uf);
forms = {@() sendEventToHTMLSource(h), @() sendEventToHTMLSource(h, 'n'), @() sendEventToHTMLSource(h, 'n', 1, 2), ...
    @() sendEventToHTMLSource(5, 'n'), @() sendEventToHTMLSource('n'), @() sendEventToHTMLSource(h, 5), @() sendEventToHTMLSource(h, ''), ...
    @() sendEventToHTMLSource(h, "n"), @() sendEventToHTMLSource(h, {'n'}), @() sendEventToHTMLSource(h, 'a b'), ...
    @() sendEventToHTMLSource(h, 'DataChanged'), @() sendEventToHTMLSource(h, ['ab'; 'cd']), @() sendEventToHTMLSource(h, ["a" "b"]), ...
    @() sendEventToHTMLSource(h, string(missing)), @() sendEventToHTMLSource(h, 'n', @sin), @() sendEventToHTMLSource([h h2], 'n'), ...
    @() sendEventToHTMLSource(uf, 'n'), @() sendEventToHTMLSource(uilabel(uf), 'n'), @() h.sendEventToHTMLSource('n'), ...
    @() h.sendEventToHTMLSource('n', 3), @() sendEventToHTMLSource(h, 'n', 'x', 'y'), @() sendEventToHTMLSource(), ...
    @() sendEventToHTMLSource(h, 1:3), @() sendEventToHTMLSource(h, true), @() sendEventToHTMLSource(h, 'HTMLEventReceived')};
for k = 1:numel(forms)
    lastwarn('');
    try
        forms{k}();
        r = 'ok';
    catch e
        r = sprintf('ERR %s | %s', e.identifier, oneline(e.message));
    end
    [wm, wid] = lastwarn;
    if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
    fprintf('form %s : %s\n', func2str(forms{k}), r);
end
try, x = sendEventToHTMLSource(h, 'n'); fprintf('form x = send : %s\n', deep(x)); catch e, fprintf('form x = send : ERR %s | %s\n', e.identifier, oneline(e.message)); end
delete(h2);
hd = uihtml(uf); delete(hd);
try, sendEventToHTMLSource(hd, 'n'); fprintf('form deleted : ok\n'); catch e, fprintf('form deleted : ERR %s | %s\n', e.identifier, oneline(e.message)); end
delete(uf);
end

function step(h, label, fn)
global LOG
n = numel(LOG);
lastwarn('');
try
    fn();
    r = '';
catch e
    r = sprintf(' ERR %s | %s', e.identifier, oneline(e.message));
end
t = tic;
while toc(t) < 1.5, drawnow; pause(0.05); end
[wm, wid] = lastwarn;
if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
got = LOG(n+1:end);
if isempty(got), got = {'(nothing)'}; end
fprintf('step %s :%s Data=%s => %s\n', label, r, deep(h.Data), strjoin(got, ' // '));
end

function threeSets(h)
h.Data = 8; h.Data = 9; h.Data = 8;
end
function setThenPing(h)
h.Data = 10; sendEventToHTMLSource(h, 'ping', 'p10');
end
function pingThenSet(h)
sendEventToHTMLSource(h, 'ping', 'p11'); h.Data = 11;
end
function sourceThenPing(h, src)
h.HTMLSource = '<p>x</p>'; drawnow; pause(0.5);
h.HTMLSource = src; sendEventToHTMLSource(h, 'ping', 'early');
end
function dataThenSource(h, src)
h.HTMLSource = '<p>x</p>'; drawnow; pause(0.5);
h.Data = 14; h.HTMLSource = src;
end

function keepEvt(s, e)
global EVT
EVT{end+1} = {s, e};
end

function onEvent(~, evt)
global LOG
name = evt.HTMLEventName; if ~ischar(name) || size(name, 1) > 1, name = ['{' deep(name) '}']; end
LOG{end+1} = sprintf('event %s: %s', name, deep(evt.HTMLEventData));
end

function onData(src, evt)
global LOG
LOG{end+1} = sprintf('DataChangedFcn: Data=%s Previous=%s', deep(src.Data), deep(evt.PreviousData));
end

function dump(label)
global LOG
fprintf('--- %s ---\n', label);
for k = 1:numel(LOG), fprintf('%s\n', LOG{k}); end
LOG = {};
end

function waitFor(name, secs)
global LOG
t = tic;
while toc(t) < secs
    drawnow; pause(0.05);
    if any(startsWith(LOG, ['event ' name ':'])), return; end
end
fprintf('(timed out waiting for %s)\n', name);
end

function got = waitNew(name, n, secs)
global LOG
t = tic; got = '(none)';
while toc(t) < secs
    drawnow; pause(0.05);
    idx = find(startsWith(LOG(n+1:end), name), 1);
    if ~isempty(idx), got = LOG{n+idx}; return; end
end
end
