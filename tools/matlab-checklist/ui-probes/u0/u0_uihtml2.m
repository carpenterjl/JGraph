% U0 follow-up: what a uihtml page may load relative to its own folder.
global LOG
LOG = {};
uf = uifigure('Visible','off');
h = uihtml(uf);
h.HTMLEventReceivedFcn = @(s,e) logit(e);
h.HTMLSource = fullfile(pwd,'htmlsite','page2.html');
t = tic;
while toc(t) < 25 && ~any(startsWith(LOG,'done')), drawnow; pause(0.1); end
for k = 1:numel(LOG), fprintf('%s\n', LOG{k}); end
delete(uf);
function logit(e)
    global LOG
    d = e.HTMLEventData; if ~ischar(d), d = jsonencode(d); end
    LOG{end+1} = sprintf('%s: %s', e.HTMLEventName, d);
end
