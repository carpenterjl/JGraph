function u9b_types
% U9b probe: the file types R2025b's server gives a uihtml page, and the paths it refuses.
% Headless. Run make_site.py first.
global LOG
LOG = {};
here = fileparts(mfilename('fullpath'));
files = dir(fullfile(here, 'site', 'types'));
files = files(~[files.isdir]);
urls = cellfun(@(n) ['types/' n], {files.name}, 'UniformOutput', false);
urls = [urls, {'types/sub/deep.js', 'types/sub/', 'types/sub', 'types/', 'types', 'types/missing.js', 'types/a.js?x=1', ...
    'types/a.js#frag', 'types/%61.js', 'types/sub/../a.js', 'types/..%2Fpage.html', '../outside.js', '/outside.js', ...
    'types/space%20name.js', 'types/A.JS', 'types/a.Js', 'page.html', 'echo.html', 'types//a.js', 'types/./a.js', ...
    'types/a.js/', 'types\a.js'}];
uf = uifigure('Visible', 'off');
h = uihtml(uf, 'HTMLEventReceivedFcn', @onEvent);
h.HTMLSource = fullfile(here, 'site', 'types.html');
waitFor('ready', 30);
sendEventToHTMLSource(h, 'list', urls);
waitFor('done', 120);
for k = 1:numel(LOG), fprintf('%s\n', LOG{k}); end
delete(uf);
end

function onEvent(~, evt)
global LOG
d = evt.HTMLEventData;
if ~ischar(d), d = jsonencode(d); end
LOG{end+1} = sprintf('%s: %s', evt.HTMLEventName, d);
end

function waitFor(name, secs)
global LOG
t = tic;
while toc(t) < secs
    drawnow; pause(0.05);
    if any(startsWith(LOG, [name ':'])), return; end
end
fprintf('(timed out waiting for %s)\n', name);
end
