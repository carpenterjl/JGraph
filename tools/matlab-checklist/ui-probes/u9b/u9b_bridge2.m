function u9b_bridge2
% U9b probe, part two: page -> MATLAB conversions, the dedupe between the two sides, loading,
% the event data objects; then each odd sendEventToHTMLSource value on a page of its own, with a
% ping after it to see whether the page still answers. Headless.
global LOG
LOG = {};
here = fileparts(mfilename('fullpath'));
addpath(here);
site = fullfile(here, 'site');
echo = fullfile(site, 'echo.html');
uf = uifigure('Visible', 'off');
h = fresh(uf, echo);

%% page -> MATLAB: Data set by the page (set), then events sent by the page (send)
texts = {'1.5', '[1,2,3]', '[[1,2],[3,4]]', '[[1,2],[3,4,5]]', '[]', '[[]]', '[[],[]]', '{}', '[{}]', '[{},{}]', ...
    '[{"a":1},{"a":2}]', '[{"a":1},{"b":2}]', '[{"a":[1,2]},{"a":[3,4]}]', '{"1a":1,"a b":2,"":3,"a-b":4}', ...
    '{"a":1,"A":2}', '[1,null,3]', '[null,null]', '[true,false]', '[true,null]', '[true,1]', '["a","b"]', '["a",null]', ...
    '[1,"a"]', '"text"', '""', '[""]', 'null', 'true', 'false', '0', '-0', '{"a":[1,2,3]}', '{"a":{"b":[]}}', '{"a":null}', ...
    '[[[1,2],[3,4]],[[5,6],[7,8]]]', '[[1],[2]]', '[["a","b"],["c","d"]]', '{"x":[{"y":1},{"y":2}]}', '123456789012345678', ...
    '1e-320', '1e308', '"2024-01-15"', '[[1,2],[]]', '[1.5]', '["x"]', '[{"a":1}]', '[[true,false],[false,true]]', ...
    '{"a":[[1,2],[3,4]]}', '[[1,2],["a","b"]]', '[[1,2],[3,null]]', '{"a":[{"b":1},{"b":2}]}', '[{"a":1,"b":2},{"b":3,"a":4}]', ...
    '__nan__', '__inf__', '__ninf__', '__undefined__'};
for k = 1:numel(texts)
    n = numel(LOG);
    sendEventToHTMLSource(h, 'set', texts{k});
    got = waitNew('event setdone', n, 3);
    pause(0.2); drawnow;
    dc = LOG(n+1:end);
    dc = dc(startsWith(dc, 'DataChangedFcn'));
    if isempty(dc), dc = {'(no DataChangedFcn)'}; end
    fprintf('pageData %-36s %s | page %s\n', texts{k}, strjoin(dc, ' // '), got);
end
h = fresh(uf, echo);
for k = 1:numel(texts)
    n = numel(LOG);
    sendEventToHTMLSource(h, 'send', struct('name', 'ev', 'json', texts{k}));
    got = waitNew('event ev:', n, 3);
    fprintf('pageEvent %-36s %s\n', texts{k}, got);
end
n = numel(LOG);
sendEventToHTMLSource(h, 'send', struct('name', 'ev'));
fprintf('pageEvent (no data)                           %s\n', waitNew('event ev:', n, 3));

%% dedupe between the two sides
h = fresh(uf, echo);
step(h, 'page sets [1,2,3]', @() sendEventToHTMLSource(h, 'set', '[1,2,3]'));
step(h, 'MATLAB sets [1;2;3] (same JSON)', @() set(h, 'Data', [1; 2; 3]));
step(h, 'MATLAB sets [1 2 3] (same JSON, other shape)', @() set(h, 'Data', [1 2 3]));
step(h, 'MATLAB sets 7', @() set(h, 'Data', 7));
step(h, 'MATLAB sets 7 again', @() set(h, 'Data', 7));
step(h, 'page sets 7', @() sendEventToHTMLSource(h, 'set', '7'));
step(h, 'page sets 7 again', @() sendEventToHTMLSource(h, 'set', '7'));
step(h, 'MATLAB sets 7 after the page did', @() set(h, 'Data', 7));
step(h, 'page sets [4,5]', @() sendEventToHTMLSource(h, 'set', '[4,5]'));
step(h, 'MATLAB sets [4 5] (JSON the page holds)', @() set(h, 'Data', [4 5]));
step(h, 'MATLAB sets 8 then 9 then 8, no drawnow between', @() threeSets(h));
step(h, 'MATLAB sets 10 then pings, no drawnow between', @() setThenPing(h));
step(h, 'MATLAB pings then sets 11, no drawnow between', @() pingThenSet(h));
step(h, 'two pings, no drawnow between', @() twoPings(h));
step(h, 'set(h,Data,12)', @() set(h, 'Data', 12));
step(h, 'MATLAB sets NaN', @() set(h, 'Data', NaN));
step(h, 'MATLAB sets Inf (same JSON null)', @() set(h, 'Data', Inf));
step(h, 'MATLAB sets 1+2i (fails to encode)', @() set(h, 'Data', 1 + 2i));
step(h, 'lastwarn right after a failing set, before drawnow', @() warnAtOnce(h));
step(h, 'MATLAB sets 13 after the failure', @() set(h, 'Data', 13));
step(h, 'MATLAB sets {} (JSON [])', @() set(h, 'Data', {}));
step(h, 'MATLAB sets [] (JSON [] again)', @() set(h, 'Data', []));

%% loading: events sent before the page is ready, a change of source, markup
step(h, 'new source then ping at once', @() sourceThenPing(h, echo));
step(h, 'Data 14 then new source', @() dataThenSource(h, echo));
step(h, 'markup source', @() set(h, 'HTMLSource', '<p>markup</p>'));
step(h, 'ping to markup', @() sendEventToHTMLSource(h, 'ping', 'm'));
step(h, 'Data 15 on markup', @() set(h, 'Data', 15));
step(h, 'back to echo', @() set(h, 'HTMLSource', echo));
step(h, 'same source again', @() set(h, 'HTMLSource', echo));
markup = fileread(echo);
step(h, 'echo as markup', @() set(h, 'HTMLSource', markup));
step(h, 'invisible', @() set(h, 'Visible', 'off'));
step(h, 'ping while invisible', @() sendEventToHTMLSource(h, 'ping', 1));
step(h, 'Data 16 while invisible', @() set(h, 'Data', 16));
step(h, 'visible again', @() set(h, 'Visible', 'on'));
step(h, 'Parent [] then ping', @() unparent(h));
step(h, 'back in the figure', @() set(h, 'Parent', uf));
step(h, 'ping after reparenting', @() sendEventToHTMLSource(h, 'ping', 3));
step(h, 'no callbacks set, page sets 17', @() noCallbacks(h));

%% event data objects
h = fresh(uf, echo);
global EVT
EVT = {};
h.HTMLEventReceivedFcn = @(s, e) keepEvt(s, e);
h.DataChangedFcn = @(s, e) keepEvt(s, e);
sendEventToHTMLSource(h, 'send', struct('name', 'evx', 'json', '[1,2]'));
sendEventToHTMLSource(h, 'set', '{"z":1}');
t = tic; while toc(t) < 5 && numel(EVT) < 3, drawnow; pause(0.05); end
for k = 1:numel(EVT)
    e = EVT{k}{2};
    fprintf('evt class=%s srcIsH=%d\n', class(e), isequal(EVT{k}{1}, h));
    fprintf('evt properties: %s\n', strjoin(properties(e)', ' '));
    fprintf('evt methods: %s\n', strjoin(methods(e)', ' '));
    fprintf('evt EventName=%s Source=%s\n', e.EventName, class(e.Source));
    fprintf('evt display:\n%s\n', evalc('disp(e)'));
    fprintf('evt named display:\n%s\n', evalc('e'));
    try, e.EventName = 'x'; fprintf('evt set EventName ok\n'); catch ex, fprintf('evt set EventName ERR %s | %s\n', ex.identifier, oneline(ex.message)); end
    try, fprintf('evt isa event.EventData %d\n', isa(e, 'event.EventData')); catch ex, fprintf('evt isa ERR %s\n', ex.identifier); end
end

%% callbacks of the other forms, and a callback that errors
h = fresh(uf, echo);
h.HTMLEventReceivedFcn = {@cellCb, 'extra'};
step(h, 'cell callback', @() sendEventToHTMLSource(h, 'ping', 'c'));
h.HTMLEventReceivedFcn = 'disp(''text callback ran'')';
step(h, 'text callback', @() sendEventToHTMLSource(h, 'ping', 't'));
h.HTMLEventReceivedFcn = @(s, e) error('my:id', 'boom');
step(h, 'erroring callback', @() sendEventToHTMLSource(h, 'ping', 'e'));
h.HTMLEventReceivedFcn = @onEvent;
h.DataChangedFcn = @(s, e) error('my:id2', 'boom2');
step(h, 'erroring DataChangedFcn', @() sendEventToHTMLSource(h, 'set', '99'));
h.DataChangedFcn = @onData;
step(h, 'after the errors', @() sendEventToHTMLSource(h, 'ping', 'after'));

%% odd sendEventToHTMLSource values, one page each
odd = {1 + 2i, @sin, categorical({'a'}), zeros(2, 2, 2), string(missing), uf, NaN, [1 NaN], Inf, int64(9007199254740993), ...
    single(pi), seconds(90), containers.Map({'k'}, {1}), {}, struct('a', {}), "str", ["a" "b"], 'x', true(2), table(1), ...
    [datetime(2024, 1, 15) datetime(2024, 1, 16)], datetime(2024, 1, 15, 10, 30, 0), NaT, struct('a', {1, 2}), {1, {2, 'x'}}, ...
    sparse(1), matlab.lang.OnOffSwitchState.on, ['ab'; 'cd'], uint8(200), -0, 1e21, 0.1};
for k = 1:numel(odd)
    h = fresh(uf, echo);
    n = numel(LOG);
    lastwarn('');
    try
        sendEventToHTMLSource(h, 'ping', odd{k});
        r = 'ok';
    catch e
        r = sprintf('ERR %s | %s', e.identifier, oneline(e.message));
    end
    got = waitNew('event pong', n, 3);
    [wm, wid] = lastwarn;
    if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
    n = numel(LOG);
    sendEventToHTMLSource(h, 'ping', 'alive?');
    alive = waitNew('event pong', n, 3);
    fprintf('oddsend %-40s %s | page %s | then %s\n', deep(odd{k}), r, got, alive);
end
delete(uf);
end

function h = fresh(uf, src)
global LOG
delete(findall(uf, 'Type', 'uihtml'));
LOG = {};
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent, 'DataChangedFcn', @onData);
h.HTMLSource = src;
t = tic;
while toc(t) < 30
    drawnow; pause(0.05);
    if any(startsWith(LOG, 'event ready:')), break; end
end
LOG = {};
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
while toc(t) < 2, drawnow; pause(0.05); end
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
function twoPings(h)
sendEventToHTMLSource(h, 'ping', 'first'); sendEventToHTMLSource(h, 'ping', 'second');
end
function warnAtOnce(h)
lastwarn('');
h.Data = 2 + 3i;
[wm, wid] = lastwarn;
fprintf('   (right after the set: lastwarn id=%s msg=%s)\n', wid, wm);
end
function sourceThenPing(h, src)
h.HTMLSource = '<p>x</p>'; drawnow; pause(0.5);
h.HTMLSource = src; sendEventToHTMLSource(h, 'ping', 'early');
end
function dataThenSource(h, src)
h.HTMLSource = '<p>x</p>'; drawnow; pause(0.5);
h.Data = 14; h.HTMLSource = src;
end
function unparent(h)
h.Parent = [];
sendEventToHTMLSource(h, 'ping', 2);
end
function noCallbacks(h)
global LOG
h.HTMLEventReceivedFcn = '';
h.DataChangedFcn = '';
sendEventToHTMLSource(h, 'set', '17');
t = tic; while toc(t) < 1.5, drawnow; pause(0.05); end
LOG{end+1} = sprintf('(Data after the page set it with no callbacks: %s)', deep(h.Data));
h.HTMLEventReceivedFcn = @onEvent;
h.DataChangedFcn = @onData;
end
function cellCb(~, e, extra)
global LOG
LOG{end+1} = sprintf('cellCb %s %s', e.HTMLEventName, extra);
end

function keepEvt(s, e)
global EVT
EVT{end+1} = {s, e};
end

function onEvent(~, evt)
global LOG
name = evt.HTMLEventName;
if ~ischar(name) || size(name, 1) > 1, name = ['{' deep(name) '}']; end
LOG{end+1} = sprintf('event %s: %s', name, deep(evt.HTMLEventData));
end

function onData(src, evt)
global LOG
LOG{end+1} = sprintf('DataChangedFcn: Data=%s Previous=%s', deep(src.Data), deep(evt.PreviousData));
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
