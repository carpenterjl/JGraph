function u9b_sendfrom
% U9b probe: sendEventToHTMLSource of one value after another, each on a fresh echo page, each
% followed by a ping to see whether the bridge still answers. A value that leaves it dead ends the
% session (R2025b's bridge does not recover); the driver starts the next session after it. The
% first index to try is read from sendfrom.txt. Headless.
global LOG
here = fileparts(mfilename('fullpath'));
addpath(here);
echo = fullfile(here, 'site', 'echo.html');
start = str2double(strtrim(fileread(fullfile(here, 'sendfrom.txt'))));
odd = {1 + 2i, @sin, categorical({'a'}), zeros(2, 2, 2), string(missing), NaN, [1 NaN], Inf, int64(9007199254740993), ...
    single(pi), seconds(90), containers.Map({'k'}, {1}), {}, struct('a', {}), "str", ["a" "b"], 'x', true(2), table(1), ...
    [datetime(2024, 1, 15) datetime(2024, 1, 16)], datetime(2024, 1, 15, 10, 30, 0), NaT, struct('a', {1, 2}), {1, {2, 'x'}}, ...
    sparse(1), matlab.lang.OnOffSwitchState.on, ['ab'; 'cd'], uint8(200), -0, 1e21, 0.1, 1e6, 123456789, 1e-5, 1/3, ...
    struct('a', [1 2; 3 4]), {[1; 2]}, [1; 2; 3], zeros(3, 0), '', "", strings(0), {'a'; 'b'}, struct('a', {1; 2}), ...
    int8([1 2 3]), [true; false], categorical({'a', 'b'}), 'é', 5, groot};
uf = uifigure('Visible', 'off');
for k = start:numel(odd)
    LOG = {};
    delete(findall(uf, 'Type', 'uihtml'));
    h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent);
    h.HTMLSource = echo;
    if ~waitFor('event ready', 30)
        fprintf('oddsend %d page never ready\n', k);
        break;
    end
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
    fprintf('oddsend %d %-40s %s | page %s | then %s\n', k, deep(odd{k}), r, got, alive);
    if strcmp(alive, '(none)')
        fprintf('dead after %d\n', k);
        break;
    end
end
delete(uf);
end

function ok = waitFor(prefix, secs)
global LOG
t = tic; ok = false;
while toc(t) < secs
    drawnow; pause(0.05);
    if any(startsWith(LOG, prefix)), ok = true; return; end
end
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
