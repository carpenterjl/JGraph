% record: -noFigureWindows
% U9b of the app-building plan (ADR 0208): a uihtml's page and MATLAB talking, both ways, through the
% echo page in helpers/u9b_echo.html. Data written by MATLAB reaches the page as jsonencode writes
% it (the page echoes what it holds), and is not sent again while its JSON is unchanged; events go
% through sendEventToHTMLSource's own encoder (the page pongs what it got); Data the page sets comes
% back through jsondecode with DataChangedFcn; events the page sends come back with their vectors
% as rows. Each component loads its page once: R2025b runs a uihtml's callbacks once more for every
% further HTMLSource it is given (ADR 0208, divergences). Probes u9b_bridge, u9b_more, u9b_sends.
global LOG
uf = uifigure('Visible', 'off');

%% MATLAB -> page through Data: what the page holds, as JSON.stringify writes it
h = u9b_page(uf, []);
vals = {1.5, -0, 0.1 + 0.2, pi, 1/3, 1e21, 1e-7, 123456789012345678, 1e300, realmin, [1 NaN 3], [NaN NaN], [Inf -Inf], ...
    int8(-5), single(pi), uint8([1 2 3]), true(2), [true; false], 'a"b\c', sprintf('line1\nline2'), ['ab'; 'cd'], '', "", ...
    string(missing), ["a" missing], strings(0, 1), {}, cell(0, 3), {1, 'x'; 2, 'y'}, {[]}, {{1}}, struct(), struct('a', {}), ...
    struct('a', {1; 2}), struct('a', [1 2; 3 4]), struct('a', struct('b', [])), zeros(2, 2, 2), [], zeros(0, 3), [1; 2; 3], ...
    magic(3), 1:3, datetime(2024, 1, 15), [datetime(2024, 1, 15) datetime(2024, 1, 16)], containers.Map({'k1', 'k2'}, {1, 'v'}), ...
    @sin, sparse([1 0 2]), 1 + 2i, 5, 5, 6};
for k = 1:numel(vals)
    u9b_chk(sprintf('data_%02d', k), @() setwait(h, 'Data', vals{k}, 'event echo', 2));
end

%% sendEventToHTMLSource: what the page gets, as JSON.stringify writes it
sends = {1.5, [1 2 3], [1; 2; 3], magic(3), [], zeros(1, 0), 'text', "str", ["a" "b"], {1, 'x'}, struct('a', {1, 2}), true, ...
    NaN, [1 NaN], Inf, -Inf, int8(5), datetime(2024, 1, 15), [datetime(2024, 1, 15) NaT], NaT, {}, struct(), containers.Map({'k'}, {1}), ...
    @sin, zeros(2, 2, 2), string(missing), "", strings(0), single(pi), single(NaN), ['ab'; 'cd'], '', 1e6, 1e-5, 1/3, ...
    struct('a', [1 2; 3 4]), {[1; 2]}, uint8(200), -0, 1e21, {1, {2, 'x'}}};
for k = 1:numel(sends)
    u9b_chk(sprintf('send_%02d', k), @() sendwait(h, 'ping', sends{k}, 'event pong', 2));
end
u9b_chk('send_nodata', @() sendwait(h, 'ping', [], 'event pong', 2, true));
% An anonymous function is sent as its text, which func2str writes with spaces here (open item 54).
u9b_chk('send_anonymous', @() sendwait(h, 'ping', @(x) x + 1, 'event pong', 2));
u9b_chk('send_cellname', @() sendwait(h, {'ping'}, 7, 'event pong', 2));
u9b_chk('send_stringname', @() sendwait(h, "ping", 8, 'event pong', 2));

%% page -> MATLAB: Data the page sets, then events the page sends
texts = {'1.5', '[1,2,3]', '[[1,2],[3,4]]', '[[1,2],[3,4,5]]', '[]', '[[]]', '[[],[]]', '{}', '[{}]', '[{},{}]', ...
    '[{"a":1},{"a":2}]', '[{"a":1},{"b":2}]', '[{"a":[1,2]},{"a":[3,4]}]', '{"1a":1,"a b":2,"":3,"a-b":4}', ...
    '{"a":1,"A":2}', '[1,null,3]', '[null,null]', '[true,false]', '[true,null]', '[true,1]', '["a","b"]', '["a",null]', ...
    '[1,"a"]', '"text"', '""', '[""]', 'null', 'true', 'false', '0', '-0', '{"a":[1,2,3]}', '{"a":{"b":[]}}', '{"a":null}', ...
    '[[[1,2],[3,4]],[[5,6],[7,8]]]', '[[1],[2]]', '[["a","b"],["c","d"]]', '{"x":[{"y":1},{"y":2}]}', '123456789012345678', ...
    '1e-320', '1e308', '"2024-01-15"', '[[1,2],[]]', '[1.5]', '["x"]', '[{"a":1}]', '[[true,false],[false,true]]', ...
    '{"a":[[1,2],[3,4]]}', '[[1,2],["a","b"]]', '[[1,2],[3,null]]', '{"a":[{"b":1},{"b":2}]}', '[{"a":1,"b":2},{"b":3,"a":4}]', ...
    '{"a":1,"a":2}', '__nan__', '__inf__', '__ninf__', '__undefined__'};
for k = 1:numel(texts)
    u9b_chk(sprintf('pagedata_%02d', k), @() sendwait(h, 'set', texts{k}, 'event setdone', 3));
end
u9b_chk('pagedata_read_back', @() h.Data);
for k = 1:numel(texts)
    u9b_chk(sprintf('pageevent_%02d', k), @() sendwait(h, 'send', struct('name', 'ev', 'json', texts{k}), 'event ev:', 3));
end
u9b_chk('pageevent_nodata', @() sendwait(h, 'send', struct('name', 'ev'), 'event ev:', 3));
u9b_chk('pageevent_numbername', @() sendwait(h, 'send', struct('name', 5, 'json', '1'), 'event', 3));
u9b_chk('pageevent_emptyname', @() sendwait(h, 'send', struct('name', '', 'json', '1'), 'event', 3));
u9b_chk('pageevent_datachangedname', @() sendwait(h, 'send', struct('name', 'DataChanged', 'json', '7'), 'event', 3));
delete(h);

%% what is sent again and what is not, and in what order
h = u9b_page(uf, []);
u9b_chk('step_page_sets_row', @() sendwait(h, 'set', '[1,2,3]', 'event setdone', 3));
u9b_chk('step_same_json_column', @() setwait(h, 'Data', [1; 2; 3], 'event echo', 2));
u9b_chk('step_same_json_row', @() setwait(h, 'Data', [1 2 3], 'event echo', 2));
u9b_chk('step_seven', @() setwait(h, 'Data', 7, 'event echo', 2));
u9b_chk('step_seven_again', @() setwait(h, 'Data', 7, 'event echo', 2));
u9b_chk('step_page_seven', @() sendwait(h, 'set', '7', 'event setdone', 3));
u9b_chk('step_page_seven_again', @() sendwait(h, 'set', '7', 'event setdone', 3));
u9b_chk('step_seven_after_page', @() setwait(h, 'Data', 7, 'event echo', 2));
u9b_chk('step_page_pair', @() sendwait(h, 'set', '[4,5]', 'event setdone', 3));
u9b_chk('step_pair_the_page_holds', @() setwait(h, 'Data', [4 5], 'event echo', 2));
u9b_chk('step_three_writes', @() threewrites(h));
u9b_chk('step_write_then_ping', @() writeping(h));
u9b_chk('step_ping_then_write', @() pingwrite(h));
u9b_chk('step_two_pings', @() twopings(h));
u9b_chk('step_nan', @() setwait(h, 'Data', NaN, 'event echo', 2));
u9b_chk('step_inf_same_json', @() setwait(h, 'Data', Inf, 'event echo', 2));
u9b_chk('step_empty_cell', @() setwait(h, 'Data', {}, 'event echo', 2));
u9b_chk('step_empty_after_cell', @() setwait(h, 'Data', [], 'event echo', 2));
u9b_chk('step_complex', @() setwait(h, 'Data', 1 + 2i, 'event echo', 2));
u9b_chk('step_after_complex', @() setwait(h, 'Data', 13, 'event echo', 2));
u9b_chk('step_lastwarn_before_drawnow', @() warnatonce(h));
u9b_chk('step_invisible_ping', @() invisibleping(h));
u9b_chk('step_no_callbacks', @() nocallbacks(h));
delete(h);

%% Data written before the page loads
h = u9b_page(uf, 5);
u9b_chk('first_data_five', @() strjoin(LOG, ' // '));
delete(h);
h = u9b_page(uf, {});
u9b_chk('first_data_empty_cell', @() strjoin(LOG, ' // '));
delete(h);
h = u9b_page(uf, struct('a', {1, 2}));
u9b_chk('first_data_structs', @() strjoin(LOG, ' // '));
delete(h);

%% the event data, and callbacks of each form
h = u9b_page(uf, []);
global EVT
EVT = {};
h.HTMLEventReceivedFcn = @(s, e) keepevt(s, e);
h.DataChangedFcn = @(s, e) keepevt(s, e);
sendEventToHTMLSource(h, 'send', struct('name', 'evx', 'json', '[1,2]'));
sendEventToHTMLSource(h, 'set', '{"z":1}');
t = tic;
while toc(t) < 5 && numel(EVT) < 3
    drawnow;
    pause(0.05);
end
u9b_chk('evt_count', @() numel(EVT));
for k = 1:min(numel(EVT), 3)
    e = EVT{k}{2};
    u9b_chk(sprintf('evt_%d_class', k), @() class(e));
    u9b_chk(sprintf('evt_%d_source_is_h', k), @() isequal(EVT{k}{1}, h) && isequal(e.Source, h));
    u9b_chk(sprintf('evt_%d_properties', k), @() properties(e));
    u9b_chk(sprintf('evt_%d_eventname', k), @() e.EventName);
    u9b_chk(sprintf('evt_%d_isa', k), @() double([isa(e, 'event.EventData') isobject(e) isstruct(e)]));
    % Event data are classed structs here (ADR 0202), so a write to one is not refused.
    u9b_chkdiv(sprintf('evt_%d_readonly', k), @() setevt(e), 'ADR0208');
    if isprop(e, 'HTMLEventName')
        u9b_chk(sprintf('evt_%d_name', k), @() e.HTMLEventName);
        u9b_chk(sprintf('evt_%d_data', k), @() e.HTMLEventData);
    else
        u9b_chk(sprintf('evt_%d_data', k), @() e.Data);
        u9b_chk(sprintf('evt_%d_previous', k), @() e.PreviousData);
    end
end
h.HTMLEventReceivedFcn = {@cellcb, 'extra'};
u9b_chk('callback_cell', @() sendwait(h, 'ping', 'c', 'cell', 3));
h.HTMLEventReceivedFcn = 'global LOG; LOG{end+1} = ''text callback ran'';';
u9b_chk('callback_text', @() sendwait(h, 'ping', 't', 'text', 3));
h.HTMLEventReceivedFcn = @onevent;
h.DataChangedFcn = @ondata;
delete(h);

%% last: events no encoder can write (R2025b's bridge does not answer again after these)
h = u9b_page(uf, []);
u9b_chk('send_complex', @() sendwait(h, 'ping', 1 + 2i, 'event pong', 2));
u9b_chk('send_sparse', @() sendwait(h, 'ping', sparse(1), 'event pong', 2));
delete(uf);

function h = u9b_page(uf, data)
global LOG
LOG = {};
h = uihtml(uf, 'HTMLEventReceivedFcn', @onevent, 'DataChangedFcn', @ondata);
if ~isequal(data, [])
    h.Data = data;
end
h.HTMLSource = 'u9b_echo.html';
waitfor_('event ready', 30);
t = tic;
while toc(t) < 0.5
    drawnow;
    pause(0.05);
end
end

function onevent(~, e)
global LOG
name = e.HTMLEventName;
if ~ischar(name)
    name = ['{' u9b_text(name) '}'];
end
LOG{end+1} = sprintf('event %s: %s', name, u9b_text(e.HTMLEventData));
end

function ondata(src, e)
global LOG
LOG{end+1} = sprintf('data %s prev %s', u9b_text(src.Data), u9b_text(e.PreviousData));
end

function cellcb(~, e, extra)
global LOG
LOG{end+1} = sprintf('cell %s %s', e.HTMLEventName, extra);
end

function keepevt(s, e)
global EVT
EVT{end+1} = {s, e};
end

function r = setevt(e)
try
    e.EventName = 'x';
    r = 'set';
catch err
    r = err.identifier;
end
end

function got = waitfor_(prefix, secs)
% Waits until a LOG line starts with prefix, then answers every line so far; '(none)' if none came.
global LOG
t = tic;
while toc(t) < secs
    drawnow;
    pause(0.05);
    if any(startsWith(LOG, prefix))
        got = strjoin(LOG, ' // ');
        return;
    end
end
if isempty(LOG)
    got = '(none)';
else
    got = strjoin(LOG, ' // ');
end
end

function got = setwait(h, name, value, prefix, secs)
global LOG
LOG = {};
h.(name) = value;
got = waitfor_(prefix, secs);
end

function got = sendwait(h, name, value, prefix, secs, nodata)
global LOG
LOG = {};
if nargin > 5 && nodata
    sendEventToHTMLSource(h, name);
else
    sendEventToHTMLSource(h, name, value);
end
got = waitfor_(prefix, secs);
end

function got = threewrites(h)
global LOG
LOG = {};
h.Data = 8; h.Data = 9; h.Data = 8;
got = waitfor_('event echo', 2);
end

function got = writeping(h)
global LOG
LOG = {};
h.Data = 10; sendEventToHTMLSource(h, 'ping', 'p10');
got = waitfor_('event pong', 3);
end

function got = pingwrite(h)
global LOG
LOG = {};
sendEventToHTMLSource(h, 'ping', 'p11'); h.Data = 11;
got = waitfor_('event pong', 3);
end

function got = twopings(h)
global LOG
LOG = {};
sendEventToHTMLSource(h, 'ping', 'first'); sendEventToHTMLSource(h, 'ping', 'second');
t = tic;
while toc(t) < 3 && sum(startsWith(LOG, 'event pong')) < 2
    drawnow;
    pause(0.05);
end
got = strjoin(LOG, ' // ');
end

function got = warnatonce(h)
lastwarn('');
h.Data = 2 + 3i;
[~, id] = lastwarn;
got = ['right after the write: [' id ']'];
drawnow;
end

function got = invisibleping(h)
global LOG
h.Visible = 'off';
LOG = {};
sendEventToHTMLSource(h, 'ping', 'hidden');
got = waitfor_('event pong', 3);
h.Visible = 'on';
end

function got = nocallbacks(h)
h.HTMLEventReceivedFcn = '';
h.DataChangedFcn = '';
sendEventToHTMLSource(h, 'set', '17');
t = tic;
while toc(t) < 2
    drawnow;
    pause(0.05);
end
got = h.Data;
h.HTMLEventReceivedFcn = @onevent;
h.DataChangedFcn = @ondata;
end
