% u11_figs - MATLAB .fig files: what R2025b's savefig writes, and what openfig/hgload do with it.
% Headless (-noFigureWindows). Writes the files it measures into figs\ beside this probe.
here = fileparts(mfilename('fullpath'));
out = fullfile(here, 'figs');
if ~isfolder(out), mkdir(out); end
global U11LOG %#ok<GVMIS>
U11LOG = {};

fprintf('--- guide\n');
try, guide; catch e, fprintf('%s | %s\n', e.identifier, e.message); end
try, guide('x.fig'); catch e, fprintf('%s | %s\n', e.identifier, e.message); end

%% 1. a plot figure: two axes, lines, labels, legend, text, an image and a colorbar
f = figure('Name', 'plots', 'NumberTitle', 'off', 'Position', [100 100 640 360], 'Color', [1 1 0.9]);
ax1 = subplot(1, 2, 1);
plot(ax1, 1:5, [1 4 2 5 3], 'r-o', 'LineWidth', 2, 'Tag', 'first');
hold(ax1, 'on');
plot(ax1, 1:5, [2 2 3 3 4], 'b--', 'DisplayName', 'second');
title(ax1, 'Top'); xlabel(ax1, 'X'); ylabel(ax1, 'Y');
legend(ax1, 'a', 'b');
grid(ax1, 'on');
text(ax1, 2, 3, 'note', 'Color', [0 0.5 0]);
ax2 = subplot(1, 2, 2);
imagesc(ax2, magic(4));
colorbar(ax2);
colormap(f, hot(16));
savefig(f, fullfile(out, 'u11_plots.fig'));
delete(f);

%% 2. a classic GUI: panel, button group, every style, a menu, a context menu, a table
f = figure('Name', 'gui', 'NumberTitle', 'off', 'MenuBar', 'none', 'Position', [100 100 420 300], ...
    'Tag', 'guifig');
p = uipanel(f, 'Title', 'Panel', 'Position', [0.05 0.45 0.5 0.5], 'Tag', 'panel1');
uicontrol(p, 'Style', 'pushbutton', 'String', 'Go', 'Position', [10 10 60 24], 'Tag', 'go', ...
    'Callback', 'disp(''go pressed'')');
uicontrol(p, 'Style', 'edit', 'String', 'abc', 'Position', [80 10 80 24], 'Tag', 'ed');
uicontrol(p, 'Style', 'popupmenu', 'String', {'one', 'two', 'three'}, 'Value', 2, ...
    'Position', [10 50 100 24], 'Tag', 'pop');
uicontrol(p, 'Style', 'checkbox', 'String', 'Check', 'Value', 1, 'Position', [120 50 80 24], 'Tag', 'chk');
bg = uibuttongroup(f, 'Title', 'Pick', 'Position', [0.6 0.45 0.35 0.5], 'Tag', 'bg');
r1 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'A', 'Position', [10 60 60 20], 'Tag', 'ra');
r2 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'B', 'Position', [10 30 60 20], 'Tag', 'rb');
bg.SelectedObject = r2;
uicontrol(f, 'Style', 'slider', 'Min', 0, 'Max', 10, 'Value', 3, 'Units', 'normalized', ...
    'Position', [0.05 0.3 0.5 0.06], 'Tag', 'sl');
uicontrol(f, 'Style', 'listbox', 'String', {'x', 'y', 'z'}, 'Max', 2, 'Value', [1 3], ...
    'Position', [250 20 100 80], 'Tag', 'lb');
uicontrol(f, 'Style', 'text', 'String', {'two', 'lines'}, 'Position', [20 20 100 40], 'Tag', 'txt', ...
    'HorizontalAlignment', 'left', 'FontWeight', 'bold');
m = uimenu(f, 'Text', 'File', 'Tag', 'mfile');
uimenu(m, 'Text', 'Open', 'Accelerator', 'O', 'Tag', 'mopen', 'MenuSelectedFcn', @(s, e) disp('open'));
cm = uicontextmenu(f);
uimenu(cm, 'Text', 'Ctx', 'Tag', 'mctx');
set(findobj(f, 'Tag', 'ed'), 'ContextMenu', cm);
uitable(f, 'Data', [1 2; 3 4], 'ColumnName', {'p', 'q'}, 'Position', [250 120 150 60], 'Tag', 'tbl');
savefig(f, fullfile(out, 'u11_gui.fig'));
delete(f);

%% 3. surfaces and patches, saved invisible, with a CreateFcn on figure, axes, line and panel
f = figure('Visible', 'off', 'Name', 'surf', 'CreateFcn', @(s, e) u11log(['create ' get(s, 'Type')]));
ax = axes(f, 'CreateFcn', @(s, e) u11log(['create ' get(s, 'Type')]));
surf(ax, peaks(8));
hold(ax, 'on');
patch(ax, [1 4 4], [1 1 4], [0 0 0], 'g', 'FaceAlpha', 0.5);
line(ax, [1 8], [1 8], [5 5], 'CreateFcn', @(s, e) u11log(['create ' get(s, 'Type')]));
view(ax, 30, 40);
uipanel(f, 'Position', [0 0 0.2 0.2], 'CreateFcn', @(s, e) u11log(['create ' get(s, 'Type')]));
savefig(f, fullfile(out, 'u11_surf.fig'));
delete(f);

%% 4. a figure saved by hgsave (format 070000), and one with only the hgS variable
f = figure('Visible', 'off', 'Name', 'old');
plot(1:3, 'k');
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
hgsave(f, fullfile(out, 'u11_hgsave.fig'));
delete(f);
s = load(fullfile(out, 'u11_hgsave.fig'), '-mat', 'hgS_070000');
hgS_070000 = s.hgS_070000; %#ok<NASGU>
save(fullfile(out, 'u11_hgsonly.fig'), 'hgS_070000', '-mat');

%% the trees
for name = {'u11_plots', 'u11_gui', 'u11_surf', 'u11_hgsave', 'u11_hgsonly'}
    fprintf('=== %s\n', name{1});
    u11dump(fullfile(out, [name{1} '.fig']));
end

%% openfig and hgload
fprintf('--- openfig\n');
pf = fullfile(out, 'u11_plots.fig');
before = numel(findall(groot, 'Type', 'figure'));
h = openfig(pf);
fprintf('class=%s Number=%s Visible=%s Name=%s figures +%d\n', class(h), mat2str(h.Number), h.Visible, h.Name, ...
    numel(findall(groot, 'Type', 'figure')) - before);
fprintf('FileName=%s\n', strrep(h.FileName, here, '<here>'));
fprintf('gcf is it=%d CurrentAxes title=%s\n', isequal(gcf, h), h.CurrentAxes.Title.String);
fprintf('Children types: %s\n', strjoin(cellstr(get(h.Children, 'Type'))', ','));
fprintf('findall types: %s\n', strjoin(get(findall(h), 'Type')', ','));
axs = findobj(h, 'Type', 'axes');
for i = 1:numel(axs)
    a = axs(i);
    fprintf('axes %d: Position=%s title=%s xlabel=%s children=%s\n', i, mat2str(a.Position, 4), ...
        a.Title.String, a.XLabel.String, strjoin(cellstr(get(a.Children, 'Type'))', ','));
end
ln = findobj(h, 'Tag', 'first');
fprintf('first line: Color=%s Marker=%s LineWidth=%g YData=%s\n', mat2str(ln.Color), ln.Marker, ln.LineWidth, mat2str(ln.YData));
lg = findobj(h, 'Type', 'legend');
fprintf('legend: n=%d String=%s Axes ok=%d\n', numel(lg), strjoin(lg.String, '|'), isequal(lg.Axes, axs(end)) || isequal(lg.Axes, axs(1)));
cb = findobj(h, 'Type', 'colorbar');
fprintf('colorbar: n=%d Limits=%s\n', numel(cb), mat2str(cb.Limits));
fprintf('Colormap rows=%d\n', size(h.Colormap, 1));
h2 = openfig(pf);
fprintf('second openfig new figure=%d\n', ~isequal(h2, h));
h3 = openfig(pf, 'reuse');
fprintf('reuse gives an open one=%d (h=%d h2=%d)\n', isequal(h3, h) || isequal(h3, h2), isequal(h3, h), isequal(h3, h2));
delete([h h2]);
h = openfig(pf, 'reuse');
fprintf('reuse with none open: new=%d\n', isgraphics(h));
delete(h);

fprintf('--- visibility\n');
sf = fullfile(out, 'u11_surf.fig');
U11LOG = {};
h = openfig(sf);
fprintf('saved invisible, openfig(f): Visible=%s\n', h.Visible);
fprintf('create order: %s\n', strjoin(U11LOG, ', '));
delete(h);
h = openfig(sf, 'visible'); fprintf('openfig visible: %s\n', h.Visible); delete(h);
h = openfig(pf, 'invisible'); fprintf('openfig invisible: %s\n', h.Visible); delete(h);
h = openfig(pf, 'new', 'invisible'); fprintf('new invisible: %s\n', h.Visible); delete(h);
try, h = openfig(pf, 'bogus'); delete(h); catch e, fprintf('bad option: %s | %s\n', e.identifier, e.message); end
try, openfig(fullfile(out, 'nope.fig')); catch e, fprintf('missing: %s | %s\n', e.identifier, strrep(e.message, here, '<here>')); end
try, openfig(fullfile(out, 'nope')); catch e, fprintf('missing no ext: %s | %s\n', e.identifier, strrep(e.message, here, '<here>')); end
x = 1; save(fullfile(out, 'notfig.mat'), 'x');
try, openfig(fullfile(out, 'notfig.mat')); catch e, fprintf('not a fig: %s | %s\n', e.identifier, strrep(e.message, here, '<here>')); end
delete(fullfile(out, 'notfig.mat'));

fprintf('--- hgload\n');
[h, old] = hgload(pf);
fprintf('hgload: class=%s Visible=%s old=%s %s\n', class(h), h.Visible, class(old), mat2str(size(old)));
delete(h);
[h, old] = hgload(pf, struct('Visible', 'off', 'Name', 'renamed'));
fprintf('hgload props: Visible=%s Name=%s old=%s\n', h.Visible, h.Name, strjoin(cellfun(@(x) class(x), old, 'UniformOutput', false), ','));
delete(h);
h = hgload(fullfile(out, 'u11_hgsonly.fig'));
fprintf('hgS only: %s lines=%d\n', class(h), numel(findobj(h, 'Type', 'line')));
delete(h);
h = hgload(fullfile(out, 'u11_hgsave.fig'));
fprintf('hgsave file: lines=%d\n', numel(findobj(h, 'Type', 'line')));
delete(h);

fprintf('--- gui figure back\n');
h = openfig(fullfile(out, 'u11_gui.fig'), 'invisible');
fprintf('findall tags: %s\n', strjoin(get(findall(h), 'Tag')', ','));
fprintf('panel children: %s\n', strjoin(get(get(findobj(h, 'Tag', 'panel1'), 'Children'), 'Tag')', ','));
bg = findobj(h, 'Tag', 'bg');
fprintf('bg SelectedObject=%s\n', bg.SelectedObject.Tag);
fprintf('pop Value=%d String=%s\n', get(findobj(h, 'Tag', 'pop'), 'Value'), strjoin(get(findobj(h, 'Tag', 'pop'), 'String')', '|'));
fprintf('lb Value=%s\n', mat2str(get(findobj(h, 'Tag', 'lb'), 'Value')));
fprintf('sl Value=%g Position=%s Units=%s\n', get(findobj(h, 'Tag', 'sl'), 'Value'), mat2str(get(findobj(h, 'Tag', 'sl'), 'Position'), 4), get(findobj(h, 'Tag', 'sl'), 'Units'));
fprintf('go Callback=%s\n', get(findobj(h, 'Tag', 'go'), 'Callback'));
mo = findobj(h, 'Tag', 'mopen');
fprintf('menu Open: Accelerator=%s MenuSelectedFcn=%s\n', mo.Accelerator, func2str(mo.MenuSelectedFcn));
ed = findobj(h, 'Tag', 'ed');
fprintf('ed ContextMenu items=%s\n', strjoin(cellstr(get(ed.ContextMenu.Children, 'Tag'))', ','));
t = findobj(h, 'Tag', 'tbl');
fprintf('table Data=%s ColumnName=%s\n', mat2str(t.Data), strjoin(t.ColumnName', ','));
txt = findobj(h, 'Tag', 'txt');
fprintf('text String class=%s FontWeight=%s\n', class(txt.String), txt.FontWeight);
fprintf('Position fig=%s\n', mat2str(h.Position));
delete(h);
