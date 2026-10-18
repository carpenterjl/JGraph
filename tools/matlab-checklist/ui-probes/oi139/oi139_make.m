% oi139_make - open item 139: the figure JGraph writes as a MATLAB .fig for R2025b to open
% (oi139_verify): callbacks that are anonymous functions capturing variables, each written as the
% command that re-makes it with the captured values in place, and the furniture whose words
% hgsave's tree has no place for. Run in JGraph: jgraph.exe -batch oi139_make.m -sd <this folder>.
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end

f = figure('Name', 'captures', 'NumberTitle', 'off', 'Position', [100 100 420 300], 'Tag', 'captures', 'Visible', 'off');
k = 4;
name = 'it''s';
m = [1 2; 3 4];
on = true;
word = "two";
neg = -3;
v = [5 6 7];
uicontrol(f, 'Style', 'pushbutton', 'String', 'number', 'Position', [10 10 80 24], 'Tag', 'number', 'Callback', @(s, e) disp(k));
uicontrol(f, 'Style', 'pushbutton', 'String', 'many', 'Position', [100 10 80 24], 'Tag', 'many', 'Callback', @(s, e) fprintf('%s %d %s\n', name, on, word));
uicontrol(f, 'Style', 'pushbutton', 'String', 'matrix', 'Position', [190 10 80 24], 'Tag', 'matrix', 'Callback', @(s, e) disp(m));
uicontrol(f, 'Style', 'pushbutton', 'String', 'neg', 'Position', [280 10 80 24], 'Tag', 'neg', 'Callback', @(s, e) disp(2 ^ neg));
uicontrol(f, 'Style', 'pushbutton', 'String', 'shadow', 'Position', [10 50 80 24], 'Tag', 'shadow', 'Callback', @(s, e) arrayfun(@(k) disp(k), 1:k));
uicontrol(f, 'Style', 'pushbutton', 'String', 'indexed', 'Position', [100 50 80 24], 'Tag', 'indexed', 'Callback', @(s, e) disp(v(2)));
uicontrol(f, 'Style', 'pushbutton', 'String', 'source', 'Position', [190 50 80 24], 'Tag', 'source', 'Callback', @(s, e) disp(get(s, 'Tag')));
ax = axes(f, 'Units', 'pixels', 'Position', [40 100 200 150]);
plot(ax, 1:5, [1 4 2 5 3]);
hold(ax, 'on');
plot(ax, 1:5, [2 2 3 3 4]);
title(ax, 'Main');
subtitle(ax, 'Below the main');
lg = legend(ax, 'a', 'b');
title(lg, 'Series');
set(ax, 'Tag', 'left');
savefig(f, fullfile(rt, 'jg_captures.fig'));
fprintf('wrote jg_captures.fig\n');
