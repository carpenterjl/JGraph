% oi77_make - open item 77: the figures JGraph writes as MATLAB .fig files for R2025b to open
% (oi77_verify). Run in JGraph (jgraph.exe -batch oi77_make, from this folder); writes rt\jg_*.fig
% and a PNG of each figure as JGraph draws it, for the eye to set beside MATLAB's print of the file.
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end

%% plots: lines, labels, a legend, a text, an image with a colour bar, a surface, charts
f = figure('Name', 'plots', 'NumberTitle', 'off', 'Position', [100 100 720 480], 'Color', [1 1 0.9], 'Tag', 'plots');
ax1 = subplot(2, 2, 1);
l1 = plot(ax1, 1:5, [1 4 2 5 3], 'r-o', 'LineWidth', 2);
set(l1, 'Tag', 'first'); % plot refuses 'Tag' among its options here (filed), where R2025b takes any line property
hold(ax1, 'on');
l2 = plot(ax1, 1:5, [2 2 3 3 4], 'b--', 'DisplayName', 'dashed');
set(l2, 'Tag', 'second');
title(ax1, 'Top'); xlabel(ax1, 'X'); ylabel(ax1, 'Y');
legend(ax1, 'a', 'b', 'Location', 'northwest');
grid(ax1, 'on');
text(ax1, 2, 3, 'note', 'Color', [0 0.6 0], 'Tag', 'note');
set(ax1, 'Tag', 'left', 'XLim', [0 6]);
ax2 = subplot(2, 2, 2);
imagesc(ax2, magic(4));
colorbar(ax2);
set(ax2, 'Tag', 'right');
colormap(f, hot(16));
ax3 = subplot(2, 2, 3);
s3 = surf(ax3, peaks(8));
set(s3, 'Tag', 'surf');
view(ax3, 30, 40);
colorbar(ax3, 'Location', 'southoutside');
set(ax3, 'Tag', 'three');
ax4 = subplot(2, 2, 4);
hold(ax4, 'on');
b4 = bar(ax4, [3 1 2], 'FaceColor', [0.2 0.6 0.2]);
set(b4, 'Tag', 'bar');
s4 = scatter(ax4, 1:3, [2 3 1], 60, 'filled', 'MarkerFaceColor', [0.8 0 0]);
set(s4, 'Tag', 'scatter');
st4 = stairs(ax4, 1:4, [1 3 2 4], 'Color', [0 0 0.8]);
set(st4, 'Tag', 'stairs');
set(ax4, 'Tag', 'charts', 'Box', 'on');
savefig(f, fullfile(rt, 'jg_plots.fig'));
exportgraphics(f, fullfile(rt, 'jg_plots_jgraph.png'), 'Resolution', 72);
close(f);

%% gui: a panel, a button group, controls with callbacks of every kind, menus, a context menu, a table, a toolbar
f = figure('Name', 'gui', 'NumberTitle', 'off', 'MenuBar', 'none', 'Position', [100 100 420 300], 'Tag', 'guifig');
p = uipanel(f, 'Title', 'Panel', 'Position', [0.05 0.45 0.5 0.5], 'Tag', 'panel1');
uicontrol(p, 'Style', 'pushbutton', 'String', 'Go', 'Position', [10 10 60 24], 'Tag', 'go', 'Callback', 'disp(''go pressed'')');
uicontrol(p, 'Style', 'edit', 'String', 'abc', 'Position', [80 10 80 24], 'Tag', 'ed');
uicontrol(p, 'Style', 'popupmenu', 'String', {'one', 'two', 'three'}, 'Value', 2, 'Position', [10 50 100 24], 'Tag', 'pop');
uicontrol(p, 'Style', 'checkbox', 'String', 'Check', 'Value', 1, 'Position', [120 50 80 24], 'Tag', 'chk');
bg = uibuttongroup(f, 'Title', 'Pick', 'Position', [0.6 0.45 0.35 0.5], 'Tag', 'bg');
uicontrol(bg, 'Style', 'radiobutton', 'String', 'A', 'Position', [10 60 60 20], 'Tag', 'ra');
r2 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'B', 'Position', [10 30 60 20], 'Tag', 'rb');
bg.SelectedObject = r2;
uicontrol(f, 'Style', 'slider', 'Min', 0, 'Max', 10, 'Value', 3, 'Units', 'normalized', 'Position', [0.05 0.3 0.5 0.06], 'Tag', 'sl');
uicontrol(f, 'Style', 'listbox', 'String', {'x', 'y', 'z'}, 'Max', 2, 'Value', [1 3], 'Position', [250 20 100 80], 'Tag', 'lb');
uicontrol(f, 'Style', 'text', 'String', {'two', 'lines'}, 'Position', [20 20 100 40], 'Tag', 'txt', 'HorizontalAlignment', 'left', 'FontWeight', 'bold');
n = 7;
uicontrol(f, 'Style', 'pushbutton', 'String', 'anon', 'Position', [130 20 50 24], 'Tag', 'anon', 'Callback', @(s, e) disp(sprintf('anon %d', n)));
uicontrol(f, 'Style', 'pushbutton', 'String', 'named', 'Position', [185 20 50 24], 'Tag', 'named', 'Callback', @oi77_named);
m = uimenu(f, 'Text', 'File', 'Tag', 'mfile');
uimenu(m, 'Text', 'Open', 'Accelerator', 'O', 'Tag', 'mopen', 'MenuSelectedFcn', 'disp(''open'')');
cm = uicontextmenu(f, 'Tag', 'cm');
uimenu(cm, 'Text', 'Ctx', 'Tag', 'mctx');
set(findobj(f, 'Tag', 'ed'), 'ContextMenu', cm);
uitable(f, 'Data', [1 2; 3 4], 'ColumnName', {'p', 'q'}, 'Position', [250 120 150 60], 'Tag', 'tbl');
tb = uitoolbar(f, 'Tag', 'tb');
uipushtool(tb, 'Tooltip', 'push', 'Tag', 'pt', 'ClickedCallback', 'disp(''pushed'')');
uitoggletool(tb, 'Tooltip', 'toggle', 'Tag', 'tt', 'State', 'on');
savefig(f, fullfile(rt, 'jg_gui.fig'));
close(f);

%% annot: annotations, a polar axes, a patch, a rectangle, a group, a light
f = figure('Name', 'annot', 'Tag', 'annot', 'Position', [100 100 600 400]);
ax = axes(f, 'Position', [0.1 0.55 0.35 0.35]);
sn = plot(ax, 1:10, sin(1:10));
set(sn, 'Tag', 'sine');
set(ax, 'Tag', 'sines');
annotation(f, 'textarrow', [0.4 0.6], [0.4 0.6], 'String', 'here', 'Tag', 'ta');
annotation(f, 'ellipse', [0.15 0.15 0.2 0.2], 'Tag', 'el', 'Color', [1 0 0]);
annotation(f, 'textbox', [0.6 0.1 0.3 0.2], 'String', 'box', 'Tag', 'tbx', 'BackgroundColor', [1 1 0.8]);
pax = polaraxes(f, 'Position', [0.55 0.55 0.4 0.4]);
pp = polarplot(pax, 0:pi/16:2*pi, 1:33);
set(pp, 'Tag', 'polar');
set(pax, 'ThetaZeroLocation', 'top', 'Tag', 'pax');
ax2 = axes(f, 'Position', [0.1 0.1 0.35 0.3]);
hold(ax2, 'on');
pt = patch(ax2, 'Faces', [1 2 3; 1 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceColor', 'g', 'FaceAlpha', 0.5);
set(pt, 'Tag', 'patch');
rc = rectangle(ax2, 'Position', [0.2 0.2 0.3 0.3], 'Curvature', 0.5, 'FaceColor', [1 1 0]);
set(rc, 'Tag', 'rect');
g = hggroup(ax2, 'Tag', 'group');
lg = line([0 1], [1 0], 'Parent', g, 'Color', [0 0 1]); % line(g, x, y) reads the group as data here (filed)
set(lg, 'Tag', 'inGroup');
set(ax2, 'Tag', 'shapes');
savefig(f, fullfile(rt, 'jg_annot.fig'));
exportgraphics(f, fullfile(rt, 'jg_annot_jgraph.png'), 'Resolution', 72);
close(f);

%% callbacks alone, each kind in a file of its own: a file MATLAB cannot read names the kind
f = figure('Name', 'fh_anon', 'Tag', 'fh_anon');
n = 7;
uicontrol(f, 'Style', 'pushbutton', 'String', 'anon', 'Position', [10 10 60 24], 'Tag', 'anon', 'Callback', @(s, e) disp(sprintf('anon %d', n)));
savefig(f, fullfile(rt, 'jg_fh_anon.fig'));
close(f);
f = figure('Name', 'fh_anon0', 'Tag', 'fh_anon0');
uicontrol(f, 'Style', 'pushbutton', 'String', 'anon0', 'Position', [10 10 60 24], 'Tag', 'anon0', 'Callback', @(s, e) disp('anon without a capture'));
savefig(f, fullfile(rt, 'jg_fh_anon0.fig'));
close(f);
f = figure('Name', 'fh_named', 'Tag', 'fh_named');
uicontrol(f, 'Style', 'pushbutton', 'String', 'named', 'Position', [10 10 60 24], 'Tag', 'named', 'Callback', @oi77_named);
savefig(f, fullfile(rt, 'jg_fh_named.fig'));
close(f);
f = figure('Name', 'fh_cell', 'Tag', 'fh_cell');
uicontrol(f, 'Style', 'pushbutton', 'String', 'cell', 'Position', [10 10 60 24], 'Tag', 'cell', 'Callback', {@oi77_named, 5});
savefig(f, fullfile(rt, 'jg_fh_cell.fig'));
close(f);

%% kinds the tree has no node for: left out of MATLAB's copy, named in a warning
f = figure('Name', 'odd', 'Tag', 'odd');
ax = axes(f);
heatmap(f, [1 2; 3 4]);
savefig(f, fullfile(rt, 'jg_odd.fig'));
close(f);
disp('written');
