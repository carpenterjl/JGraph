% U0: uihtml under -batch -noFigureWindows: does the page run, Data conversions both ways,
% HTMLSource forms, the file sandbox and network access.
global LOG
LOG = {};
uf = uifigure('Visible','off');
h = uihtml(uf,'Position',[10 10 300 200]);
h.HTMLEventReceivedFcn = @onEvent;
h.DataChangedFcn = @onData;
h.Data = struct('init', 1);
h.HTMLSource = fullfile(pwd,'htmlsite','page.html');
waitFor('loaded', 20);
waitFor('net', 10); waitFor('f_outside', 5);

% MATLAB -> page: set Data; the page echoes JSON.stringify of what it sees.
vals = {1.5, [1 2 3], [1;2;3], [1 2;3 4], [], zeros(1,0), 'text', "str", ["a" "b"], {1,'x'}, ...
        struct('a',1,'b','x'), struct('a',{1,2}), true, [true false], NaN, Inf, -Inf, int8(5), ...
        datetime(2026,10,3,12,0,0), single(2.5), 1+2i, {}, struct(), containers.Map({'k'},{1})};
for k = 1:numel(vals)
    n = numel(LOG);
    try
        h.Data = vals{k};
        got = waitNew('echo', n, 5);
        fprintf('M->JS %-28s Data reads back class=%s | page saw %s\n', describe(vals{k}), class(h.Data), got);
    catch e
        fprintf('M->JS %-28s set ERROR %s | %s\n', describe(vals{k}), e.identifier, e.message);
    end
end
% sendEventToHTMLSource conversions
for k = [1 2 4 7 11 12 19]
    n = numel(LOG);
    try
        sendEventToHTMLSource(h, 'ping', vals{k});
        fprintf('send  %-28s page saw %s\n', describe(vals{k}), waitNew('pong', n, 5));
    catch e
        fprintf('send  %-28s ERROR %s | %s\n', describe(vals{k}), e.identifier, e.message);
    end
end
% JS -> MATLAB: Data set from the page
sendEventToHTMLSource(h, 'jsset', []);
waitFor('jsdone', 30);
sendEventToHTMLSource(h, 'evtypes', []);
waitFor('ev_none', 10);
pause(1); drawnow;
fprintf('--- event log ---\n');
for k = 1:numel(LOG), fprintf('%s\n', LOG{k}); end

% HTMLSource forms
fprintf('--- HTMLSource forms ---\n');
cd(fullfile(pwd,'htmlsite'));
forms = {'page.html', fullfile(pwd,'page.html'), '<p>hi</p>', 'hello world', 'missing.html', 'https://www.mathworks.com', "<b>string</b>"};
for k = 1:numel(forms)
    try
        h.HTMLSource = forms{k};
        fprintf('HTMLSource=%-40s ok, reads back class=%s value=%s\n', char(forms{k}), class(h.HTMLSource), char(h.HTMLSource));
    catch e
        fprintf('HTMLSource=%-40s ERROR %s | %s\n', char(forms{k}), e.identifier, e.message);
    end
end
cd('..');
% Setting Data from code: does DataChangedFcn fire? (counted in LOG as 'DataChanged')
n = sum(startsWith(LOG,'DataChangedFcn'));
h.Data = 42; pause(1); drawnow;
fprintf('DataChangedFcn calls after a code set: %d (before %d)\n', sum(startsWith(LOG,'DataChangedFcn')), n);
delete(uf);

function onEvent(src, evt)
    global LOG
    d = evt.HTMLEventData;
    LOG{end+1} = sprintf('event %s: class=%s size=%s value=%s evt=%s', evt.HTMLEventName, class(d), mat2str(size(d)), show(d), class(evt));
end
function onData(src, evt)
    global LOG
    LOG{end+1} = sprintf('DataChangedFcn: Data class=%s size=%s value=%s | Previous=%s evt=%s', class(src.Data), mat2str(size(src.Data)), show(src.Data), show(evt.PreviousData), class(evt));
end
function s = show(d)
    try
        if ischar(d), s = ['''' d ''''];
        elseif isstruct(d) || iscell(d), s = jsonencode(d);
        elseif isnumeric(d) || islogical(d), s = mat2str(d);
        elseif isstring(d), s = ['"' char(join(d, '","')) '"'];
        else, s = ['<' class(d) '>']; end
    catch, s = ['<' class(d) '>']; end
end
function s = describe(v)
    s = sprintf('%s%s', class(v), mat2str(size(v)));
end
function waitFor(name, secs)
    global LOG
    t = tic;
    while toc(t) < secs
        drawnow; pause(0.1);
        if any(startsWith(LOG, ['event ' name ':'])), return; end
    end
    fprintf('(timed out waiting for %s)\n', name);
end
function got = waitNew(name, n, secs)
    global LOG
    t = tic; got = '(none)';
    while toc(t) < secs
        drawnow; pause(0.05);
        idx = find(startsWith(LOG(n+1:end), ['event ' name ':']), 1);
        if ~isempty(idx), got = LOG{n+idx}; return; end
    end
end
