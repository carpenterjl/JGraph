% oi77_shapes - open item 77: what R2025b rebuilds from a .fig holding the struct tree alone
% (hgS_070000, the form every release reads), and what it tolerates in one. Headless.
% Part A: a battery of figures written by hgsave, stripped to the struct tree, reopened - the tree
% dumped (u11dump), the reopened figure's objects and any warnings listed.
% Part B: trees built by hand, to see what the loader does with a property it does not know, a
% read-only one, a missing field, a mode after its value, a second variable in the file.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'u11'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);
fprintf('struct2handle: %s\nhgload: %s\nopenfig: %s\n', which('struct2handle'), which('hgload'), which('openfig'));

%% --- Part A -----------------------------------------------------------------------------------------
figs = {};

f = figure('Name', 'plots', 'NumberTitle', 'off', 'Position', [100 100 640 360], 'Color', [1 1 0.8], 'Tag', 'plots');
ax1 = subplot(1, 2, 1);
plot(ax1, 1:5, [1 4 2 5 3], 'r-o', 'LineWidth', 2, 'Tag', 'first');
hold(ax1, 'on');
plot(ax1, 1:5, [2 2 3 3 4], 'b--', 'Tag', 'second', 'DisplayName', 'dashed');
title(ax1, 'Top'); xlabel(ax1, 'X'); ylabel(ax1, 'Y');
legend(ax1, 'a', 'b', 'Location', 'northwest');
grid(ax1, 'on');
text(ax1, 2, 3, 'note', 'Color', [0 0.6 0], 'Tag', 'note');
set(ax1, 'Tag', 'left', 'XLim', [0 6]);
ax2 = subplot(1, 2, 2);
imagesc(ax2, magic(4));
colorbar(ax2);
set(ax2, 'Tag', 'right');
colormap(f, hot(16));
figs{end + 1} = {'plots', f};

f = figure('Name', 'charts', 'Tag', 'charts');
subplot(3, 3, 1); bar([3 1 2], 'Tag', 'bar');
subplot(3, 3, 2); scatter(1:5, [2 4 1 5 3], 36, 'filled', 'Tag', 'scatter');
subplot(3, 3, 3); stem(1:4, [1 3 2 4], 'Tag', 'stem');
subplot(3, 3, 4); stairs(1:4, [1 3 2 4], 'Tag', 'stairs');
subplot(3, 3, 5); area(1:4, [1 3 2 4], 'Tag', 'area');
subplot(3, 3, 6); errorbar(1:4, [1 3 2 4], [0.2 0.1 0.3 0.2], 'Tag', 'errorbar');
subplot(3, 3, 7); histogram([1 2 2 3 3 3 4], 4, 'Tag', 'histogram');
subplot(3, 3, 8); quiver([0 1], [0 1], [1 0.5], [0.5 1], 'Tag', 'quiver');
subplot(3, 3, 9); contour(peaks(10), 'Tag', 'contour');
figs{end + 1} = {'charts', f};

f = figure('Name', 'three', 'Tag', 'three');
ax = subplot(2, 2, 1); surf(ax, peaks(8), 'Tag', 'surf'); view(ax, 30, 40); light(ax, 'Tag', 'light');
ax = subplot(2, 2, 2); mesh(ax, peaks(8), 'Tag', 'mesh');
ax = subplot(2, 2, 3); hold(ax, 'on');
patch(ax, 'Faces', [1 2 3; 1 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceColor', 'g', 'FaceAlpha', 0.5, 'Tag', 'patch');
rectangle(ax, 'Position', [0.2 0.2 0.3 0.3], 'Curvature', 0.5, 'Tag', 'rect');
g = hggroup(ax, 'Tag', 'group'); line(g, [0 1], [1 0], 'Tag', 'inGroup');
t = hgtransform(ax, 'Tag', 'xform'); line(t, [0 1], [0.5 0.5], 'Tag', 'inXform');
ax = subplot(2, 2, 4); image(ax, uint8(magic(4) * 10), 'Tag', 'image');
figs{end + 1} = {'three', f};

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
m = uimenu(f, 'Text', 'File', 'Tag', 'mfile');
k = 4;
uimenu(m, 'Text', 'Open', 'Accelerator', 'O', 'Tag', 'mopen', 'MenuSelectedFcn', @(s, e) disp(sprintf('open %d', k)));
cm = uicontextmenu(f, 'Tag', 'cm');
uimenu(cm, 'Text', 'Ctx', 'Tag', 'mctx');
set(findobj(f, 'Tag', 'ed'), 'ContextMenu', cm);
uitable(f, 'Data', [1 2; 3 4], 'ColumnName', {'p', 'q'}, 'Position', [250 120 150 60], 'Tag', 'tbl');
tb = uitoolbar(f, 'Tag', 'tb');
uipushtool(tb, 'Tooltip', 'push', 'Tag', 'pt', 'ClickedCallback', 'disp(''pushed'')');
uitoggletool(tb, 'Tooltip', 'toggle', 'Tag', 'tt', 'State', 'on');
figs{end + 1} = {'gui', f};

f = figure('Name', 'annot', 'Tag', 'annot', 'CreateFcn', @(s, e) disp('figure created'));
ax = axes(f, 'Position', [0.1 0.55 0.35 0.35]);
plot(ax, 1:10, sin(1:10), 'Tag', 'sine');
annotation(f, 'textarrow', [0.4 0.6], [0.4 0.6], 'String', 'here', 'Tag', 'ta');
annotation(f, 'ellipse', [0.15 0.15 0.2 0.2], 'Tag', 'el');
annotation(f, 'textbox', [0.6 0.1 0.3 0.2], 'String', 'box', 'Tag', 'tbx');
pax = polaraxes(f, 'Position', [0.55 0.55 0.4 0.4]);
polarplot(pax, 0:pi/16:2*pi, 1:33, 'Tag', 'polar');
set(pax, 'ThetaZeroLocation', 'top');
figs{end + 1} = {'annot', f};

f = figure('Name', 'cb', 'Tag', 'cb');
uicontrol(f, 'Style', 'pushbutton', 'String', 'char', 'Position', [10 10 60 24], 'Tag', 'c1', 'Callback', 'disp(''char callback'')');
n = 7;
uicontrol(f, 'Style', 'pushbutton', 'String', 'anon', 'Position', [80 10 60 24], 'Tag', 'c2', 'Callback', @(s, e) disp(sprintf('anon %d', n)));
uicontrol(f, 'Style', 'pushbutton', 'String', 'named', 'Position', [150 10 60 24], 'Tag', 'c3', 'Callback', @oi77_named);
uicontrol(f, 'Style', 'pushbutton', 'String', 'cell', 'Position', [220 10 60 24], 'Tag', 'c4', 'Callback', {@oi77_named, 5});
figs{end + 1} = {'cb', f};

for i = 1:numel(figs)
    name = figs{i}{1};
    f = figs{i}{2};
    full = fullfile(rt, ['s_' name '.fig']);
    only = fullfile(rt, ['s_' name '_only.fig']);
    hgsave(f, full);
    delete(f);
    s = load(full, '-mat', 'hgS_070000');
    hgS_070000 = s.hgS_070000; %#ok<NASGU>
    save(only, 'hgS_070000', '-mat');
    fprintf('\n=== %s (hgsave tree)\n', name);
    u11dump(only);
    fprintf('--- %s reopened from the tree alone\n', name);
    reopen(only);
end

%% --- Part B -----------------------------------------------------------------------------------------
fprintf('\n=== Part B: hand-built trees\n');
node = @(type, handle, props, children, special) struct('type', type, 'handle', handle, 'properties', props, 'children', children, 'special', special);
emptyKids = struct('type', {}, 'handle', {}, 'properties', {}, 'children', {}, 'special', {});

line1 = node('line', 3, struct('XData', 1:3, 'YData', [2 4 3], 'Color', [1 0 0], 'Tag', 'L'), emptyKids, []);
axes1 = node('axes', 2, struct('Tag', 'A', 'Position', [0.2 0.2 0.6 0.6]), line1, []);
fig1 = node('figure', 1, struct('Name', 'minimal', 'Tag', 'F'), axes1, []);
try1(rt, 'B1 minimal', fig1, @(h) {h.Name, h.Children.Tag, h.Children.XLimMode, h.Children.XLim, h.Children.Position, h.Children.Children.Color});

bad = fig1; bad.children.children.properties.Bogus = 5;
try1(rt, 'B2 unknown property on the line', bad, @(h) h.Children.Children.Tag);

ro = fig1; ro.children.children.properties.Type = 'line'; ro.children.children.properties.BeingDeleted = 'off';
try1(rt, 'B3 read-only properties on the line', ro, @(h) h.Children.Children.Tag);

ns = fig1; ns.children = rmfield(ns.children, 'special');
try1(rt, 'B4a special missing on the axes', ns, @(h) h.Children.Tag);
nk = fig1; nk.children.children = rmfield(nk.children.children, 'children');
try1(rt, 'B4b children missing on the line', nk, @(h) h.Children.Children.Tag);
nh = fig1; nh.children.children = rmfield(nh.children.children, 'handle');
try1(rt, 'B4c handle missing on the line', nh, @(h) h.Children.Children.Tag);
nanh = fig1; nanh.handle = NaN; nanh.children.handle = NaN; nanh.children.children.handle = NaN;
try1(rt, 'B4d handles NaN', nanh, @(h) h.Children.Children.Tag);
dup = fig1; dup.handle = 1; dup.children.handle = 1; dup.children.children.handle = 1;
try1(rt, 'B4e handles all 1', dup, @(h) h.Children.Children.Tag);
emp = fig1; emp.children.children.children = []; emp.children.children.special = [];
try1(rt, 'B4f children [] on the line', emp, @(h) h.Children.Children.Tag);

modes = fig1; modes.children.properties.XLim = [0 10]; modes.children.properties.XLimMode = 'auto';
try1(rt, 'B5a XLim then XLimMode auto', modes, @(h) {h.Children.XLimMode, h.Children.XLim});
modes = fig1; modes.children.properties.XLimMode = 'auto'; modes.children.properties.XLim = [0 10];
try1(rt, 'B5b XLimMode auto then XLim', modes, @(h) {h.Children.XLimMode, h.Children.XLim});
modes = fig1; modes.children.properties.XLim = [0 10];
try1(rt, 'B5c XLim alone', modes, @(h) {h.Children.XLimMode, h.Children.XLim});

units = fig1; units.children.properties = struct('Position', [50 50 200 100], 'Units', 'pixels', 'Tag', 'A');
try1(rt, 'B6a Position then Units pixels', units, @(h) {h.Children.Units, h.Children.Position});
units = fig1; units.children.properties = struct('Units', 'pixels', 'Position', [50 50 200 100], 'Tag', 'A');
try1(rt, 'B6b Units pixels then Position', units, @(h) {h.Children.Units, h.Children.Position});

extra = fullfile(rt, 'b7_extra.fig');
hgS_070000 = fig1; jgraph_document = uint8('{"format":"jgraph"}')'; %#ok<NASGU>
save(extra, 'hgS_070000', 'jgraph_document', '-mat');
fprintf('--- B7 a second variable in the file\n');
reopen(extra);
w = whos('-file', extra); fprintf('vars: %s\n', strjoin({w.name}, ','));

% labels through special: title, xlabel, ylabel as text children, places 1-based among the children (oldest first)
tt = node('text', 11, struct('String', 'The title', 'Tag', 'ttl'), emptyKids, []);
tx = node('text', 12, struct('String', 'The x', 'Tag', 'xl'), emptyKids, []);
ty = node('text', 13, struct('String', 'The y', 'Tag', 'yl'), emptyKids, []);
labelled = fig1; labelled.children.children = [tt; tx; ty; line1]; labelled.children.special = [1; 2; 3; 0];
try1(rt, 'B8 labels by special', labelled, @(h) {h.Children.Title.String, h.Children.XLabel.String, h.Children.YLabel.String, get(h.Children.Children, 'Type')});

% a legend: as hgsave's tree writes it (seen in Part A), rebuilt by hand with the least that works
lgA = node('scribe.legend', 20, struct('String', {{'first', 'second'}}, 'Location', 'northwest', 'Tag', 'LG'), emptyKids, []);
withLegend = fig1; withLegend.children.children = [line1; line1]; withLegend.children.children(2).handle = 4; withLegend.children.children(2).properties.Tag = 'L2';
withLegend.children = [withLegend.children; lgA];
try1(rt, 'B9a scribe.legend child of the figure, String alone', withLegend, @(h) {get(h.Children, 'Type'), get(findobj(h, 'Type', 'legend'), 'String')});
lgB = lgA; lgB.type = 'legend';
withLegend.children(2) = lgB;
try1(rt, 'B9b type legend', withLegend, @(h) {get(h.Children, 'Type'), get(findobj(h, 'Type', 'legend'), 'String')});
lgC = lgA; lgC.properties.Axes = 2; lgC.properties.PlotChildren = [3; 4];
withLegend.children(2) = lgC;
try1(rt, 'B9c scribe.legend with Axes and PlotChildren handles', withLegend, @(h) {get(h.Children, 'Type'), get(findobj(h, 'Type', 'legend'), 'String')});

% a colour bar by hand
cbA = node('scribe.colorbar', 21, struct('Tag', 'CB', 'Location', 'eastoutside'), emptyKids, []);
withBar = fig1; withBar.children = [withBar.children; cbA];
try1(rt, 'B10a scribe.colorbar child of the figure', withBar, @(h) get(h.Children, 'Type'));
cbB = cbA; cbB.properties.Axes = 2;
withBar.children(2) = cbB;
try1(rt, 'B10b scribe.colorbar with Axes', withBar, @(h) get(h.Children, 'Type'));

% chart types by their modern words and the old ones, with data alone
for word = {'bar', 'specgraph.barseries', 'scatter', 'specgraph.scattergroup', 'stem', 'specgraph.stemseries', 'stair', 'specgraph.stairseries', 'area', 'specgraph.areaseries', 'errorbar', 'specgraph.errorbarseries', 'histogram', 'quiver', 'specgraph.quivergroup', 'contour', 'specgraph.contourgroup'}
    props = struct('XData', 1:4, 'YData', [1 3 2 4], 'Tag', 'C');
    if strcmp(word{1}, 'histogram'), props = struct('Data', [1 2 2 3 3 3], 'Tag', 'C'); end
    if any(strcmp(word{1}, {'quiver', 'specgraph.quivergroup'})), props = struct('XData', [0 1], 'YData', [0 1], 'UData', [1 0.5], 'VData', [0.5 1], 'Tag', 'C'); end
    if any(strcmp(word{1}, {'contour', 'specgraph.contourgroup'})), props = struct('ZData', peaks(6), 'Tag', 'C'); end
    if strcmp(word{1}, 'errorbar') || strcmp(word{1}, 'specgraph.errorbarseries'), props.YNegativeDelta = [0.2 0.1 0.3 0.2]; props.YPositiveDelta = [0.2 0.1 0.3 0.2]; end
    ch = node(word{1}, 3, props, emptyKids, []);
    t = fig1; t.children.children = ch;
    try1(rt, ['B11 ' word{1}], t, @(h) {class(h.Children.Children), get(h.Children.Children, 'Type')});
end

% a text with Position and String; a uicontrol with a cell String; a patch by faces and vertices
tn = node('text', 3, struct('Position', [1 2 0], 'String', {{'two'; 'rows'}}, 'Color', [0 0 1], 'Tag', 'T'), emptyKids, []);
t = fig1; t.children.children = tn;
try1(rt, 'B12a text', t, @(h) {h.Children.Children.String, h.Children.Children.Position});
pn = node('patch', 3, struct('Faces', [1 2 3; 1 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceColor', [0 1 0], 'FaceAlpha', 0.5, 'Tag', 'P'), emptyKids, []);
t = fig1; t.children.children = pn;
try1(rt, 'B12b patch by faces', t, @(h) {h.Children.Children.FaceColor, h.Children.Children.FaceAlpha, size(h.Children.Children.Vertices)});
un = node('uicontrol', 3, struct('Style', 'listbox', 'String', {{'x'; 'y'}}, 'Max', 2, 'Value', [1 2], 'Position', [10 10 80 60], 'Tag', 'U'), emptyKids, []);
t = fig1; t.children = [t.children; un];
try1(rt, 'B12c uicontrol listbox', t, @(h) {h.Children(1).Style, h.Children(1).String, h.Children(1).Value});
sn = node('surface', 3, struct('XData', 1:3, 'YData', 1:3, 'ZData', magic(3), 'FaceColor', 'interp', 'Tag', 'S'), emptyKids, []);
t = fig1; t.children.children = sn; t.children.properties.View = [30 40];
try1(rt, 'B12d surface and View', t, @(h) {h.Children.View, h.Children.Children.FaceColor});
imn = node('image', 3, struct('CData', magic(4), 'CDataMapping', 'scaled', 'Tag', 'I'), emptyKids, []);
t = fig1; t.children.children = imn; t.children.properties.CLim = [1 16]; t.children.properties.YDir = 'reverse';
try1(rt, 'B12e image, CLim, YDir', t, @(h) {h.Children.CLim, h.Children.YDir, size(h.Children.Children.CData)});

% an axes whose Position is normalized and whose Units are left to default
t = fig1; t.children.properties = struct('Tag', 'A', 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5], 'Box', 'on', 'XGrid', 'on', 'XScale', 'log', 'FontSize', 14, 'Color', [0.9 0.9 1]);
try1(rt, 'B13 axes looks', t, @(h) {h.Children.Box, h.Children.XGrid, h.Children.XScale, h.Children.FontSize, h.Children.Color, h.Children.Position});

% the figure: Position, Color, Units, NumberTitle, IntegerHandle, Visible off
t = fig1; t.properties = struct('Name', 'looks', 'Tag', 'F', 'Color', [0.2 0.3 0.4], 'Position', [50 60 300 200], 'NumberTitle', 'off', 'Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none', 'Colormap', jet(8));
try1(rt, 'B14 figure looks', t, @(h) {h.Name, h.Color, h.Position, h.NumberTitle, h.Visible, h.MenuBar, h.ToolBar, size(h.Colormap)});

% print headless: does it write a png of what was opened
h = openfig(fullfile(rt, 'b1_minimal.fig'), 'invisible');
png = fullfile(rt, 'b1_minimal.png');
try, print(h, '-dpng', '-r50', png); fprintf('print wrote %d bytes\n', dir(png).bytes); catch e, fprintf('print: %s\n', e.message); end
delete(h);
fprintf('done\n');

%% ---------------------------------------------------------------------------------------------------
function try1(rt, label, tree, look)
file = fullfile(rt, [lower(regexprep(label, '[^A-Za-z0-9]+', '_')) '.fig']);
file = regexprep(file, '_\.fig$', '.fig');
hgS_070000 = tree; %#ok<NASGU>
save(file, 'hgS_070000', '-mat');
fprintf('--- %s\n', label);
lastwarn('');
try
    [txt, h] = evalc('openfig(file, ''invisible'')');
    if ~isempty(strtrim(txt)), fprintf('  printed: %s\n', strtrim(txt)); end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('  warning: %s | %s\n', id, msg); end
    try
        v = look(h);
        fprintf('  %s\n', shortv(v));
    catch e
        fprintf('  look failed: %s\n', e.message);
    end
    delete(h);
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
end

function reopen(file)
lastwarn('');
try
    [txt, h] = evalc('openfig(file, ''invisible'')');
    if ~isempty(strtrim(txt)), fprintf('  printed: %s\n', strtrim(txt)); end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('  warning: %s | %s\n', id, msg); end
    all = findall(h);
    fprintf('  findall: %s\n', strjoin(cellfun(@(x) char(x), get(all, 'Type'), 'UniformOutput', false)', ','));
    tags = get(all, 'Tag'); tags = tags(~cellfun(@isempty, tags));
    fprintf('  tags: %s\n', strjoin(tags', ','));
    for a = findobj(h, 'Type', 'axes')'
        fprintf('  axes %s: Position=%s XLim=%s(%s) title=%s xlabel=%s children=%s\n', a.Tag, mat2str(a.Position, 4), mat2str(a.XLim, 4), a.XLimMode, a.Title.String, a.XLabel.String, strjoin(cellfun(@(x) char(x), cellstr(get(a.Children, 'Type')), 'UniformOutput', false)', ','));
    end
    for lg = findobj(h, 'Type', 'legend')'
        fprintf('  legend: String=%s Location=%s\n', strjoin(lg.String, '|'), lg.Location);
    end
    fprintf('  colorbars=%d polaraxes=%d annotations=%d\n', numel(findobj(h, 'Type', 'colorbar')), numel(findall(h, 'Type', 'polaraxes')), numel(findall(h, '-regexp', 'Type', 'textarrow|ellipse|textbox')));
    for c = findobj(h, 'Type', 'uicontrol')'
        cb = c.Callback;
        if isa(cb, 'function_handle'), cbs = ['@fh ' func2str(cb)]; elseif iscell(cb), cbs = ['cell ' func2str(cb{1})]; else, cbs = ['char ' cb]; end
        fprintf('  uicontrol %s: %s | %s\n', c.Tag, c.Style, cbs);
        if ~isempty(cb)
            try
                out = evalc('hgfeval(cb, c, [])');
                fprintf('    ran: %s\n', strtrim(out));
            catch e
                fprintf('    run failed: %s\n', e.message);
            end
        end
    end
    delete(h);
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
end

function t = shortv(v)
if iscell(v)
    t = ['{' strjoin(cellfun(@shortv, v, 'UniformOutput', false), ' ; ') '}'];
elseif ischar(v)
    t = ['''' v ''''];
elseif isstring(v)
    t = ['"' strjoin(cellstr(v), '","') '"'];
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 5);
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    t = char(v);
else
    t = ['<' class(v) '>'];
end
end
