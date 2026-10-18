% oi139_verify - open item 139: R2025b opening rt\jg_captures.fig (written by JGraph, oi139_make)
% and running each control's callback: the ones that captured literals carry the values in their
% text, the one that indexed a capture is left out. Headless.
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
set(groot, 'DefaultFigureVisible', 'off');
file = fullfile(rt, 'jg_captures.fig');
w = whos('-file', file);
for i = 1:numel(w), fprintf('var %s %s %s\n', w(i).name, w(i).class, mat2str(w(i).size)); end
lastwarn('');
[txt, h] = evalc('openfig(file, ''invisible'')');
if ~isempty(strtrim(txt)), fprintf('printed: %s\n', strrep(strtrim(txt), newline, ' / ')); end
[msg, id] = lastwarn;
if ~isempty(msg), fprintf('warning: %s | %s\n', id, strrep(msg, newline, ' / ')); end
for tag = {'number', 'many', 'matrix', 'neg', 'shadow', 'indexed', 'source'}
    c = findobj(h, 'Tag', tag{1});
    cb = c.Callback;
    if isempty(cb)
        fprintf('%s: no callback\n', tag{1});
        continue
    end
    fprintf('%s: %s\n', tag{1}, cb);
    try
        out = evalc('hgfeval(cb, c, [])');
        fprintf('  ran: %s\n', strrep(strtrim(out), newline, ' / '));
    catch e
        fprintf('  run failed: %s\n', e.message);
    end
end
L = findobj(h, 'Tag', 'left');
fprintf('axes: title=%s subtitle=%s legend title=%s\n', L.Title.String, L.Subtitle.String, findobj(h, 'Type', 'legend').Title.String);
delete(h);
fprintf('done\n');
