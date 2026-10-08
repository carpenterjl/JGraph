% u11_makefix - writes the MATLAB figure files the U11 parity fixtures open, with R2025b's own
% savefig (the subsystem form, hgS_080000 empty) and hgsave (the struct form, hgS_070000), into
% tests/JGraph.Tests/MatlabParity/fixtures/helpers. Run headless (-noFigureWindows) through
% run-probe.ps1; rerun only to change the files, then re-record u11_figfile and u11_guide.
here = fileparts(mfilename('fullpath'));
root = fileparts(fileparts(fileparts(fileparts(here))));
hp = fullfile(root, 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'helpers');
addpath(hp);
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
set(groot, 'DefaultFigureVisible', 'off');
log = @(s, e) u11_log(['create ' get(s, 'Tag')]);

%% plots: two axes, lines, title and labels, legend, text, an image and a colour bar (savefig)
f = figure('Name', 'plots', 'NumberTitle', 'off', 'Position', [100 100 640 360], 'Color', [1 1 0.8], 'Tag', 'plots');
ax1 = subplot(1, 2, 1);
plot(ax1, 1:5, [1 4 2 5 3], 'r-o', 'LineWidth', 2, 'Tag', 'first');
hold(ax1, 'on');
plot(ax1, 1:5, [2 2 3 3 4], 'b--', 'Tag', 'second');
title(ax1, 'Top'); xlabel(ax1, 'X'); ylabel(ax1, 'Y');
legend(ax1, 'a', 'b');
grid(ax1, 'on');
text(ax1, 2, 3, 'note', 'Color', [0 0.6 0], 'Tag', 'note');
set(ax1, 'Tag', 'left');
ax2 = subplot(1, 2, 2);
imagesc(ax2, magic(4));
colorbar(ax2);
set(ax2, 'Tag', 'right');
colormap(f, hot(16));
savefig(f, fullfile(hp, 'u11_plots.fig'));
delete(f);

%% gui: a panel, a button group, the uicontrol styles, a menu, a context menu and a table (savefig)
f = figure('Name', 'gui', 'NumberTitle', 'off', 'MenuBar', 'none', 'Position', [100 100 420 300], 'Tag', 'guifig');
p = uipanel(f, 'Title', 'Panel', 'Position', [0.05 0.45 0.5 0.5], 'Tag', 'panel1');
uicontrol(p, 'Style', 'pushbutton', 'String', 'Go', 'Position', [10 10 60 24], 'Tag', 'go', ...
    'Callback', 'u11_log(''go pressed'')');
uicontrol(p, 'Style', 'edit', 'String', 'abc', 'Position', [80 10 80 24], 'Tag', 'ed');
uicontrol(p, 'Style', 'popupmenu', 'String', {'one', 'two', 'three'}, 'Value', 2, ...
    'Position', [10 50 100 24], 'Tag', 'pop');
uicontrol(p, 'Style', 'checkbox', 'String', 'Check', 'Value', 1, 'Position', [120 50 80 24], 'Tag', 'chk');
bg = uibuttongroup(f, 'Title', 'Pick', 'Position', [0.6 0.45 0.35 0.5], 'Tag', 'bg');
uicontrol(bg, 'Style', 'radiobutton', 'String', 'A', 'Position', [10 60 60 20], 'Tag', 'ra');
r2 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'B', 'Position', [10 30 60 20], 'Tag', 'rb');
bg.SelectedObject = r2;
uicontrol(f, 'Style', 'slider', 'Min', 0, 'Max', 10, 'Value', 3, 'Units', 'normalized', ...
    'Position', [0.05 0.3 0.5 0.06], 'Tag', 'sl');
uicontrol(f, 'Style', 'listbox', 'String', {'x', 'y', 'z'}, 'Max', 2, 'Value', [1 3], ...
    'Position', [250 20 100 80], 'Tag', 'lb');
uicontrol(f, 'Style', 'text', 'String', {'two', 'lines'}, 'Position', [20 20 100 40], 'Tag', 'txt', ...
    'HorizontalAlignment', 'left', 'FontWeight', 'bold');
m = uimenu(f, 'Text', 'File', 'Tag', 'mfile');
k = 4;
uimenu(m, 'Text', 'Open', 'Accelerator', 'O', 'Tag', 'mopen', 'MenuSelectedFcn', @(s, e) u11_log(sprintf('open %d', k)));
cm = uicontextmenu(f);
uimenu(cm, 'Text', 'Ctx', 'Tag', 'mctx');
set(findobj(f, 'Tag', 'ed'), 'ContextMenu', cm);
uitable(f, 'Data', [1 2; 3 4], 'ColumnName', {'p', 'q'}, 'Position', [250 120 150 60], 'Tag', 'tbl');
savefig(f, fullfile(hp, 'u11_gui.fig'));
delete(f);

%% surf: a surface, a patch and a line in a 3-D view, with CreateFcns, saved invisible (savefig)
f = figure('Name', 'surf', 'Tag', 'F', 'CreateFcn', log);
ax = axes(f);
surf(ax, peaks(8));
hold(ax, 'on');
patch(ax, [1 4 4], [1 1 4], [0 0 0], 'g', 'FaceAlpha', 0.5, 'Tag', 'tri');
line(ax, [1 8], [1 8], [5 5], 'Tag', 'L', 'CreateFcn', log);
view(ax, 30, 40);
set(ax, 'Tag', 'A');
uipanel(f, 'Position', [0 0 0.2 0.2], 'Tag', 'P', 'CreateFcn', log);
savefig(f, fullfile(hp, 'u11_surf.fig'));
delete(f);

%% labels: a log axis, a set limit, grid, title and labels and a legend, in both forms
f = figure('Name', 'labels');
ax = axes(f);
plot(ax, 1:3, [3 1 2]);
title(ax, 'T'); xlabel(ax, 'XL'); ylabel(ax, 'YL');
xlim(ax, [0.5 4]); grid(ax, 'on'); set(ax, 'XScale', 'log');
legend(ax, 'only');
hgsave(f, fullfile(hp, 'u11_labels7.fig'));
savefig(f, fullfile(hp, 'u11_labels.fig'));
delete(f);

%% order: CreateFcns on every kind, in both forms, and the struct form alone
f = figure('CreateFcn', log, 'Tag', 'F');
p1 = uipanel(f, 'Tag', 'P1', 'CreateFcn', log);
uicontrol(p1, 'Tag', 'C1', 'CreateFcn', log);
uicontrol(f, 'Tag', 'C2', 'CreateFcn', log);
a1 = axes(f, 'Tag', 'A1', 'NextPlot', 'add', 'CreateFcn', log);
line(a1, 1:2, 1:2, 'Tag', 'L1', 'CreateFcn', log);
line(a1, 1:2, 2:3, 'Tag', 'L2', 'CreateFcn', log);
a2 = axes(f, 'Tag', 'A2', 'NextPlot', 'add', 'Position', [0.6 0.6 0.3 0.3], 'CreateFcn', log);
text(a2, 0.5, 0.5, 'hi', 'Tag', 'T3', 'CreateFcn', log);
uimenu(f, 'Tag', 'M1', 'CreateFcn', log);
savefig(f, fullfile(hp, 'u11_order.fig'));
hgsave(f, fullfile(hp, 'u11_order7.fig'));
delete(f);
s = load(fullfile(hp, 'u11_order7.fig'), '-mat', 'hgS_070000');
hgS_070000 = s.hgS_070000; %#ok<NASGU>
save(fullfile(hp, 'u11_order7s.fig'), 'hgS_070000', '-mat');
u11_log();

%% the GUIDE pairs: the struct form for u11_guide1, the subsystem form for the rest
makeGuide(hp, 'u11_guide1', true, 'off');
src = fileread(fullfile(hp, 'u11_guide1.m'));
for name = {'u11_guide2', 'u11_guidev', 'u11_guide0'}
    text = strrep(src, 'u11_guide1', name{1});
    if strcmp(name{1}, 'u11_guide0')
        text = strrep(text, 'gui_Singleton = 1;', 'gui_Singleton = 0;');
    end
    fid = fopen(fullfile(hp, [name{1} '.m']), 'w');
    fwrite(fid, text);
    fclose(fid);
end
makeGuide(hp, 'u11_guide2', false, 'off');
makeGuide(hp, 'u11_guidev', false, 'on');
makeGuide(hp, 'u11_guide0', false, 'off');
fprintf('written to %s\n', hp);

function makeGuide(hp, name, legacy, vis)
f = figure('Visible', vis, 'Tag', 'figure1', 'Name', name, 'NumberTitle', 'off', ...
    'MenuBar', 'none', 'IntegerHandle', 'off', 'HandleVisibility', 'callback', 'Units', 'characters', ...
    'Position', [100 30 60 12]);
cb = @(tag) str2func(sprintf('@(hObject,eventdata)%s(''%s'',hObject,eventdata,guidata(hObject))', name, tag));
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'pushbutton1', 'String', 'Add', 'Units', 'characters', ...
    'Position', [40 2 15 2], 'Callback', cb('pushbutton1_Callback'));
e = uicontrol(f, 'Style', 'edit', 'Tag', 'edit1', 'String', '2', 'Units', 'characters', ...
    'Position', [5 2 30 2], 'Callback', cb('edit1_Callback'));
e.CreateFcn = cb('edit1_CreateFcn');
uicontrol(f, 'Style', 'text', 'Tag', 'text1', 'String', 'Count: 0', 'Units', 'characters', ...
    'Position', [5 7 50 2]);
setappdata(f, 'GUIDEOptions', struct('active_h', [], 'taginfo', [], 'override', 0, 'release', 13, ...
    'resize', 'none', 'accessibility', 'callback', 'mfile', 1, 'callbacks', 1, 'singleton', 1, ...
    'syscolorfig', 1, 'blocking', 0, 'lastSavedFile', ''));
if legacy
    hgsave(f, fullfile(hp, [name '.fig']));
else
    savefig(f, fullfile(hp, [name '.fig']));
end
delete(f);
end
