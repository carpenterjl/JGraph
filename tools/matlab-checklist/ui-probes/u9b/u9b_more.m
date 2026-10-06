function u9b_more
% U9b probe, part three: an event sent with no data, the names a cell or a string array send, an
% anonymous function's text, a datetime array with a NaT, and what a page sees of Data before its
% first DataChanged when Data was set before the source. Headless.
global LOG
here = fileparts(mfilename('fullpath'));
addpath(here);
echo = fullfile(here, 'site', 'echo.html');
uf = uifigure('Visible', 'off');
h = fresh(uf, echo);
cases = {{'ping'}, {{'ping'}, 1}, {"ping", 2}, {["ping" "x"], 3}, {'ping', @(x) x + 1}, ...
    {'ping', [datetime(2024, 1, 15) NaT]}, {'ping', -Inf}, {'ping', single(NaN)}, {'ping', int8([])}, {'ping', true}};
for k = 1:numel(cases)
    c = cases{k};
    n = numel(LOG);
    try
        sendEventToHTMLSource(h, c{:});
        r = 'ok';
    catch e
        r = ['ERR ' e.identifier];
    end
    fprintf('more %d %s | %s\n', k, r, waitNew('event pong', n, 3));
end
% Data before the source, then a DataChanged after setup?
delete(h);
LOG = {};
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent, 'Data', 5);
h.HTMLSource = echo;
t = tic; while toc(t) < 8, drawnow; pause(0.05); end
fprintf('data-first: %s\n', strjoin(LOG, ' // '));
delete(h);
LOG = {};
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent, 'Data', {});
h.HTMLSource = echo;
t = tic; while toc(t) < 8, drawnow; pause(0.05); end
fprintf('data-empty-cell-first: %s\n', strjoin(LOG, ' // '));
delete(h);
LOG = {};
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent, 'HTMLSource', echo, 'Data', 6);
t = tic; while toc(t) < 8, drawnow; pause(0.05); end
fprintf('source-then-data in one call: %s\n', strjoin(LOG, ' // '));
delete(uf);
end

function h = fresh(uf, src)
global LOG
LOG = {};
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent);
h.HTMLSource = src;
t = tic;
while toc(t) < 30
    drawnow; pause(0.05);
    if any(startsWith(LOG, 'event ready:')), break; end
end
LOG = {};
end

function onEvent(~, evt)
global LOG
LOG{end+1} = sprintf('event %s: %s', evt.HTMLEventName, deep(evt.HTMLEventData));
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
