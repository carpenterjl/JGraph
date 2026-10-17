% oi77_hand - open item 77, part B: struct trees built by hand in hgsave's exact shapes (a leaf's
% children and special are 0-by-0 doubles, children stand in a column, oldest first), each saved
% alone as hgS_070000 and opened, to learn what R2025b's loader needs and what it tolerates. The
% first run of these crashed MATLAB on a tree whose leaves held an empty struct array for children,
% so the cases run safest first and the run can start from a case: set OI77_FROM to its number.
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end
set(groot, 'DefaultFigureVisible', 'off');
from = str2double(getenv('OI77_FROM'));
if isnan(from), from = 1; end

leaf = @(type, handle, props) struct('type', type, 'handle', handle, 'properties', props, 'children', [], 'special', []);
node = @(type, handle, props, kids, special) struct('type', type, 'handle', handle, 'properties', props, 'children', kids, 'special', special);

line1 = leaf('line', 3, struct('XData', 1:3, 'YData', [2 4 3], 'Color', [1 0 0], 'Tag', 'L'));
axes1 = node('axes', 2, struct('Tag', 'A', 'Position', [0.2 0.2 0.6 0.6]), line1, zeros(4, 1));
fig1 = node('figure', 1, struct('Name', 'minimal', 'Tag', 'F'), axes1, []);

cases = {};
add = @(varargin) varargin; % {label, tree, look} and, for one case, a fourth word

cases{end + 1} = add('B1 minimal', fig1, @(h) {h.Name, h.Children.Tag, h.Children.XLimMode, h.Children.XLim, h.Children.Position, h.Children.Children.Color, class(h.Children.Children)});
t = fig1; t.children.children.type = 'graph2d.lineseries'; t.children.children.properties.DisplayName = 'dn';
cases{end + 1} = add('B1b graph2d.lineseries', t, @(h) {class(h.Children.Children), h.Children.Children.DisplayName});

t = fig1; t.children.properties.XLim = [0 10]; t.children.properties.XLimMode = 'auto';
cases{end + 1} = add('B5a XLim then XLimMode auto', t, @(h) {h.Children.XLimMode, h.Children.XLim});
t = fig1; t.children.properties.XLimMode = 'auto'; t.children.properties.XLim = [0 10];
cases{end + 1} = add('B5b XLimMode auto then XLim', t, @(h) {h.Children.XLimMode, h.Children.XLim});
t = fig1; t.children.properties.XLim = [0 10];
cases{end + 1} = add('B5c XLim alone', t, @(h) {h.Children.XLimMode, h.Children.XLim});
t = fig1; t.children.properties.XTick = [1 2 3]; t.children.properties.XTickLabel = {'a'; 'b'; 'c'};
cases{end + 1} = add('B5d XTick and XTickLabel', t, @(h) {h.Children.XTickMode, h.Children.XTick, h.Children.XTickLabelMode, h.Children.XTickLabel'});

t = fig1; t.children.properties = struct('Position', [50 50 200 100], 'Units', 'pixels', 'Tag', 'A');
cases{end + 1} = add('B6a Position then Units pixels', t, @(h) {h.Children.Units, h.Children.Position});
t = fig1; t.children.properties = struct('Units', 'pixels', 'Position', [50 50 200 100], 'Tag', 'A');
cases{end + 1} = add('B6b Units pixels then Position', t, @(h) {h.Children.Units, h.Children.Position});
t = fig1; t.children.properties = struct('OuterPosition', [0 0 0.5 0.5], 'Tag', 'A');
cases{end + 1} = add('B6c OuterPosition alone', t, @(h) {h.Children.OuterPosition, h.Children.PositionConstraint});

cases{end + 1} = add('B7 a second variable in the file (uint8 column)', fig1, @(h) h.Name, 'extra');

tt = leaf('text', 11, struct('String', 'The title', 'Tag', 'ttl'));
tx = leaf('text', 12, struct('String', 'The x', 'Tag', 'xl'));
ty = leaf('text', 13, struct('String', 'The y', 'Tag', 'yl'));
t = fig1; t.children.children = [tt; tx; ty; line1]; t.children.special = [1; 2; 3; 0];
cases{end + 1} = add('B8a labels by special [1;2;3;0]', t, @(h) {numel(h.Children), get(h.Children(1), 'Type'), get(get(h.Children(1), 'Title'), 'String'), get(get(h.Children(1), 'XLabel'), 'String'), get(get(h.Children(1), 'YLabel'), 'String'), numel(get(h.Children(1), 'Children'))});
tt2 = tt; tt2.properties.FontSize = 20; tt2.properties.Color = [1 0 0]; tt2.properties.FontWeight = 'bold';
t = fig1; t.children.children = [line1; tt2]; t.children.special = [2; 0; 0; 0];
cases{end + 1} = add('B8b title last with font', t, @(h) {h.Children.Title.String, h.Children.Title.FontSize, h.Children.Title.Color, h.Children.Title.FontWeight, numel(h.Children.Children)});

line2 = line1; line2.handle = 4; line2.properties.Tag = 'L2'; line2.properties.Color = [0 0 1];
twoLines = fig1; twoLines.children.children = [line1; line2];
% A scribe.legend with String and Location alone - no Axes, no PlotChildren - is an access
% violation in R2025b's loader (measured 2026-10-17, the run's .err); the case is not run.
lgA = leaf('scribe.legend', 20, struct('String', {{'first', 'second'}}, 'Location', 'northwest', 'Tag', 'LG'));
lgB = lgA; lgB.properties.Axes = 2; lgB.properties.PlotChildren = [3; 4];
t = twoLines; t.children = [t.children; lgB];
cases{end + 1} = add('B9b scribe.legend with Axes and PlotChildren', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String'), get(findobj(h, 'Type', 'legend'), 'Location')});
lgC = lgB; lgC.properties.ApplicationData = struct('NonDataObject', [], 'PostDeserializeFcn', @legendpostdeserialize, 'PlotChildren', [3; 4], 'PeerAxes', 2);
t = twoLines; t.children = [t.children; lgC];
cases{end + 1} = add('B9c scribe.legend with the appdata too', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
lgD = lgB; lgD.type = 'legend';
t = twoLines; t.children = [t.children; lgD];
cases{end + 1} = add('B9d type legend', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
lgE = lgB; lgE.properties = rmfield(lgE.properties, 'PlotChildren');
t = twoLines; t.children = [t.children; lgE];
cases{end + 1} = add('B9e scribe.legend with Axes, no PlotChildren', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
lgF = lgB; lgF.properties.String = {'only'}; lgF.properties.PlotChildren = 3;
t = twoLines; t.children = [t.children; lgF];
cases{end + 1} = add('B9f scribe.legend naming one of two lines', t, @(h) {get(findobj(h, 'Type', 'legend'), 'String')});

imn = leaf('image', 3, struct('CData', magic(4), 'CDataMapping', 'scaled', 'Tag', 'I'));
withImage = fig1; withImage.children.children = imn;
cbA = leaf('scribe.colorbar', 21, struct('Tag', 'CB', 'Location', 'eastoutside'));
t = withImage; t.children = [t.children; cbA];
cases{end + 1} = add('B10a scribe.colorbar: Location alone', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
cbB = cbA; cbB.properties.Axes = 2;
t = withImage; t.children = [t.children; cbB];
cases{end + 1} = add('B10b scribe.colorbar with Axes', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits'), h.Children(end).Position});
cbC = cbB; cbC.properties.ApplicationData = struct('NonDataObject', [], 'PostDeserializeFcn', @colorbarpostdeserialize);
t = withImage; t.children = [t.children; cbC];
cases{end + 1} = add('B10c scribe.colorbar with the appdata', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
cbD = cbB; cbD.type = 'colorbar';
t = withImage; t.children = [t.children; cbD];
cases{end + 1} = add('B10d type colorbar', t, @(h) {get(h.Children, 'Type')'});

words = {'bar', 'specgraph.barseries', 'scatter', 'specgraph.scattergroup', 'stem', 'specgraph.stemseries', 'stair', 'stairs', 'specgraph.stairseries', 'area', 'specgraph.areaseries', 'errorbar', 'specgraph.errorbarseries', 'histogram', 'quiver', 'specgraph.quivergroup', 'contour', 'specgraph.contourgroup', 'surface', 'graph3d.surfaceplot'};
for w = words
    props = struct('XData', 1:4, 'YData', [1 3 2 4], 'Tag', 'C');
    switch w{1}
        case 'histogram', props = struct('Data', [1 2 2 3 3 3], 'Tag', 'C');
        case {'quiver', 'specgraph.quivergroup'}, props = struct('XData', [0 1], 'YData', [0 1], 'UData', [1 0.5], 'VData', [0.5 1], 'Tag', 'C');
        case {'contour', 'specgraph.contourgroup'}, props = struct('ZData', peaks(6), 'Tag', 'C');
        case {'errorbar', 'specgraph.errorbarseries'}, props.YNegativeDelta = [0.2 0.1 0.3 0.2]; props.YPositiveDelta = [0.2 0.1 0.3 0.2];
        case {'surface', 'graph3d.surfaceplot'}, props = struct('XData', 1:3, 'YData', 1:3, 'ZData', magic(3), 'Tag', 'C');
    end
    t = fig1; t.children.children = leaf(w{1}, 3, props);
    cases{end + 1} = add(['B11 ' w{1}], t, @(h) {class(h.Children.Children), get(h.Children.Children, 'Type')}); %#ok<AGROW>
end

t = fig1; t.children.children = leaf('text', 3, struct('Position', [1 2 0], 'String', {{'two'; 'rows'}}, 'Color', [0 0 1], 'Tag', 'T'));
cases{end + 1} = add('B12a text', t, @(h) {h.Children.Children.String', h.Children.Children.Position});
t = fig1; t.children.children = leaf('patch', 3, struct('Faces', [1 2 3; 1 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceColor', [0 1 0], 'FaceAlpha', 0.5, 'Tag', 'P'));
cases{end + 1} = add('B12b patch by faces', t, @(h) {h.Children.Children.FaceColor, h.Children.Children.FaceAlpha, size(h.Children.Children.Vertices)});
t = fig1; t.children.children = leaf('patch', 3, struct('XData', [0 1 1]', 'YData', [0 0 1]', 'FaceColor', [0 1 0], 'Tag', 'P'));
cases{end + 1} = add('B12c patch by coordinates', t, @(h) {h.Children.Children.FaceColor, size(h.Children.Children.Vertices)});
t = fig1; t.children = [t.children; leaf('uicontrol', 5, struct('Style', 'listbox', 'String', {{'x'; 'y'}}, 'Max', 2, 'Value', [1 2], 'Position', [10 10 80 60], 'Tag', 'U'))];
cases{end + 1} = add('B12d uicontrol listbox', t, @(h) {h.Children(1).Style, h.Children(1).String', h.Children(1).Value});
t = fig1; t.children.children = leaf('surface', 3, struct('XData', 1:3, 'YData', 1:3, 'ZData', magic(3), 'FaceColor', 'interp', 'Tag', 'S')); t.children.properties.View = [30 40];
cases{end + 1} = add('B12e surface and View', t, @(h) {h.Children.View, h.Children.Children.FaceColor});
t = fig1; t.children.children = imn; t.children.properties.CLim = [1 16]; t.children.properties.YDir = 'reverse';
cases{end + 1} = add('B12f image, CLim, YDir', t, @(h) {h.Children.CLim, h.Children.CLimMode, h.Children.YDir, size(h.Children.Children.CData)});
t = fig1; t.children.children = leaf('rectangle', 3, struct('Position', [0.2 0.2 0.3 0.3], 'Curvature', [0.5 0.5], 'FaceColor', [1 1 0], 'Tag', 'R'));
cases{end + 1} = add('B12g rectangle', t, @(h) {h.Children.Children.Curvature, h.Children.Children.FaceColor});
t = fig1; t.children.children = node('hggroup', 3, struct('Tag', 'G'), line1, []); t.children.children.children.handle = 6;
cases{end + 1} = add('B12h hggroup with a line', t, @(h) {get(h.Children.Children, 'Type'), get(h.Children.Children.Children, 'Type')});
t = fig1; t.children.children = node('hgtransform', 3, struct('Tag', 'X', 'Matrix', makehgtform('translate', [1 0 0])), line1, []); t.children.children.children.handle = 6;
cases{end + 1} = add('B12i hgtransform with a line', t, @(h) {get(h.Children.Children, 'Type'), h.Children.Children.Matrix(1, 4)});
t = fig1; t.children.children = [line1; leaf('light', 6, struct('Position', [1 1 1], 'Style', 'local', 'Color', [1 0 0], 'Tag', 'LT'))];
cases{end + 1} = add('B12j light', t, @(h) {get(h.Children.Children, 'Type')', h.Children.Children(1).Style});

t = fig1; t.children.properties = struct('Tag', 'A', 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5], 'Box', 'on', 'XGrid', 'on', 'XScale', 'log', 'FontSize', 14, 'Color', [0.9 0.9 1], 'XColor', [1 0 0], 'LineWidth', 2, 'TickDir', 'out', 'YAxisLocation', 'right', 'Layer', 'top', 'XMinorTick', 'on', 'GridAlpha', 0.5, 'FontName', 'Courier New', 'FontWeight', 'bold', 'NextPlot', 'add', 'Visible', 'on');
cases{end + 1} = add('B13 axes looks', t, @(h) {h.Children.Box, h.Children.XGrid, h.Children.XScale, h.Children.FontSize, h.Children.Color, h.Children.Position, h.Children.XColor, h.Children.LineWidth, h.Children.TickDir, h.Children.YAxisLocation, h.Children.FontName});

t = fig1; t.properties = struct('Name', 'looks', 'Tag', 'F', 'Color', [0.2 0.3 0.4], 'Position', [50 60 300 200], 'NumberTitle', 'off', 'Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none', 'Colormap', jet(8), 'Units', 'pixels', 'Resize', 'off', 'IntegerHandle', 'off', 'HandleVisibility', 'callback', 'PaperPositionMode', 'auto', 'PaperUnits', 'inches', 'PaperOrientation', 'landscape', 'Alphamap', linspace(0, 1, 16), 'Pointer', 'crosshair');
cases{end + 1} = add('B14 figure looks', t, @(h) {h.Name, h.Color, h.Position, h.NumberTitle, h.Visible, h.MenuBar, h.ToolBar, size(h.Colormap), h.Resize, h.IntegerHandle, h.HandleVisibility, h.PaperOrientation, numel(h.Alphamap), h.Pointer});
t = fig1; t.properties = struct('Name', 'px', 'Units', 'normalized', 'Position', [0.1 0.1 0.4 0.4]);
cases{end + 1} = add('B14b figure Units normalized then Position', t, @(h) {h.Units, h.Position});

cm = node('uicontextmenu', 30, struct('Tag', 'CM'), leaf('uimenu', 31, struct('Text', 'Ctx', 'Tag', 'ctx')), []);
ed = leaf('uicontrol', 5, struct('Style', 'edit', 'String', 'abc', 'Position', [10 10 80 24], 'Tag', 'ED', 'UIContextMenu', 30));
t = fig1; t.children = [t.children; cm; ed];
cases{end + 1} = add('B15a UIContextMenu by handle', t, @(h) {get(h.Children, 'Type')', get(get(findobj(h, 'Tag', 'ED'), 'ContextMenu'), 'Tag')});
ed2 = ed; ed2.properties = rmfield(ed2.properties, 'UIContextMenu'); ed2.properties.ContextMenu = 30;
t = fig1; t.children = [t.children; cm; ed2];
cases{end + 1} = add('B15b ContextMenu by handle', t, @(h) {get(get(findobj(h, 'Tag', 'ED'), 'ContextMenu'), 'Tag')});
ln = fig1.children.children; ln.properties.UIContextMenu = 30;
t = fig1; t.children.children = ln; t.children = [t.children; cm];
cases{end + 1} = add('B15c UIContextMenu on a line', t, @(h) get(get(findobj(h, 'Tag', 'L'), 'ContextMenu'), 'Tag'));
ra = leaf('uicontrol', 41, struct('Style', 'radiobutton', 'String', 'A', 'Position', [10 60 60 20], 'Tag', 'ra'));
rb = leaf('uicontrol', 42, struct('Style', 'radiobutton', 'String', 'B', 'Position', [10 30 60 20], 'Tag', 'rb'));
bg = node('uitools.uibuttongroup', 40, struct('Title', 'Pick', 'Position', [0.6 0.45 0.35 0.5], 'Tag', 'bg', 'SelectedObject', 42), [ra; rb], []);
t = fig1; t.children = [t.children; bg];
cases{end + 1} = add('B15d uitools.uibuttongroup with SelectedObject', t, @(h) {get(h.Children(1), 'Type'), get(h.Children(1).SelectedObject, 'Tag')});
bg2 = bg; bg2.type = 'uibuttongroup';
t = fig1; t.children = [t.children; bg2];
cases{end + 1} = add('B15e type uibuttongroup', t, @(h) {get(h.Children(1), 'Type'), get(h.Children(1).SelectedObject, 'Tag')});
pn = node('uipanel', 50, struct('Title', 'P', 'Position', [0.1 0.1 0.4 0.4], 'Tag', 'pn', 'Units', 'normalized'), leaf('uicontrol', 51, struct('Style', 'pushbutton', 'String', 'Go', 'Position', [10 10 60 24], 'Tag', 'go')), []);
t = fig1; t.children = [t.children; pn];
cases{end + 1} = add('B15f uipanel with a button', t, @(h) {h.Children(1).Title, h.Children(1).Children.String});
tb = node('uitoolbar', 60, struct('Tag', 'tb'), [leaf('uipushtool', 61, struct('Tooltip', 'push', 'Tag', 'pt', 'ClickedCallback', 'disp(''pushed'')')); leaf('uitoggletool', 62, struct('Tooltip', 'toggle', 'Tag', 'tt', 'State', 'on'))], []);
t = fig1; t.children = [t.children; tb];
cases{end + 1} = add('B15g uitoolbar with tools', t, @(h) {get(h.Children(1).Children, 'Type')', h.Children(1).Children(1).State});
mn = node('uimenu', 70, struct('Text', 'File', 'Tag', 'mf'), leaf('uimenu', 71, struct('Text', 'Open', 'Accelerator', 'O', 'Tag', 'mo', 'MenuSelectedFcn', 'disp(''open'')')), []);
t = fig1; t.children = [t.children; mn];
cases{end + 1} = add('B15h uimenu with an item', t, @(h) {h.Children(1).Text, h.Children(1).Children.Text, h.Children(1).Children.Accelerator});
tbl = leaf('uitable', 80, struct('Data', [1 2; 3 4], 'ColumnName', {{'p'; 'q'}}, 'Position', [250 120 150 60], 'Tag', 'tbl', 'ColumnEditable', [true false], 'RowName', {{'r1'; 'r2'}}));
t = fig1; t.children = [t.children; tbl];
cases{end + 1} = add('B15i uitable', t, @(h) {h.Children(1).Data, h.Children(1).ColumnName', h.Children(1).ColumnEditable, h.Children(1).RowName'});

t = fig1; t.properties.CreateFcn = 'disp(''fig create'')'; t.children.properties.CreateFcn = 'disp(''axes create'')'; t.children.children.properties.CreateFcn = 'disp(''line create'')';
cases{end + 1} = add('B16 char CreateFcns', t, @(h) {h.CreateFcn, h.Children.CreateFcn});
t = fig1; t.properties.Visible = 'on';
cases{end + 1} = add('B17 Visible on in the tree, openfig invisible', t, @(h) h.Visible);

pa = node('matlab.graphics.axis.PolarAxes', 2, struct('Tag', 'PA', 'Position', [0.2 0.2 0.6 0.6], 'ThetaZeroLocation', 'top'), leaf('graph2d.lineseries', 3, struct('ThetaData', 0:0.5:3, 'RData', 1:7, 'Tag', 'PL')), zeros(4, 1));
t = fig1; t.children = pa;
cases{end + 1} = add('B18a polar axes by the class name', t, @(h) {get(h.Children, 'Type'), h.Children.ThetaZeroLocation, get(h.Children.Children, 'Type'), h.Children.Children.RData});
pb = pa; pb.type = 'polaraxes';
t = fig1; t.children = pb;
cases{end + 1} = add('B18b type polaraxes', t, @(h) {get(h.Children, 'Type')});

sa = node('scribe.scribeaxes', 90, struct('Tag', 'SA'), [leaf('scribe.textbox', 91, struct('Position', [0.6 0.1 0.3 0.2], 'String', 'box', 'Tag', 'tbx')); leaf('scribe.scribeellipse', 92, struct('Position', [0.15 0.15 0.2 0.2], 'Tag', 'el')); leaf('scribe.textarrow', 93, struct('X', [0.4 0.6], 'Y', [0.4 0.6], 'String', 'here', 'Tag', 'ta'))], []);
t = fig1; t.children = [t.children; sa];
cases{end + 1} = add('B19a annotations under scribe.scribeaxes', t, @(h) {get(findall(h, '-regexp', 'Type', 'textarrow|ellipse|textbox'), 'Type')', get(findall(h, 'Tag', 'tbx'), 'String')});
t = fig1; t.children = [t.children; leaf('scribe.textbox', 91, struct('Position', [0.6 0.1 0.3 0.2], 'String', 'box', 'Tag', 'tbx'))];
cases{end + 1} = add('B19b scribe.textbox straight under the figure', t, @(h) get(findall(h, 'Tag', 'tbx'), 'Type'));

% the risky ones last
t = fig1; t.children.children.properties.Bogus = 5;
cases{end + 1} = add('B2 unknown property on the line', t, @(h) h.Children.Children.Tag);
t = fig1; t.children.children.properties.Type = 'line'; t.children.children.properties.BeingDeleted = 'off';
cases{end + 1} = add('B3 read-only properties on the line', t, @(h) h.Children.Children.Tag);
t = fig1; t.children.children.properties.Parent = 2;
cases{end + 1} = add('B3b Parent in the properties', t, @(h) h.Children.Children.Tag);
t = fig1; t.handle = NaN; t.children.handle = NaN; t.children.children.handle = NaN;
cases{end + 1} = add('B4d handles NaN', t, @(h) h.Children.Children.Tag);
t = fig1; t.handle = 1; t.children.handle = 1; t.children.children.handle = 1;
cases{end + 1} = add('B4e handles all 1', t, @(h) h.Children.Children.Tag);
t = fig1; t.children.special = [];
cases{end + 1} = add('B4g axes special []', t, @(h) h.Children.Children.Tag);
t = fig1; t.children = rmfield(t.children, 'special');
cases{end + 1} = add('B4a special missing on the axes', t, @(h) h.Children.Tag);
t = fig1; t.children.children = rmfield(t.children.children, 'children');
cases{end + 1} = add('B4b children missing on the line', t, @(h) h.Children.Children.Tag);
t = fig1; t.children.children = rmfield(t.children.children, 'handle');
cases{end + 1} = add('B4c handle missing on the line', t, @(h) h.Children.Children.Tag);
t = fig1; t.children.children.children = struct('type', {}, 'handle', {}, 'properties', {}, 'children', {}, 'special', {});
cases{end + 1} = add('B4f children as an empty struct array (the crash?)', t, @(h) h.Children.Children.Tag);

% hgsave's own legend node (from the shapes probe's plots tree), grafted onto two lines, whole and
% with fields taken away, to find what the loader needs of it (the hand-built ones crashed).
s = load(fullfile(rt, 's_plots_only.fig'), '-mat');
kids = s.hgS_070000.children;
own = kids(arrayfun(@(c) strcmp(c.type, 'scribe.legend'), kids));
own.handle = 20;
own.properties.Axes = 2; own.properties.PlotChildren = [3; 4];
own.properties.ApplicationData.PlotChildren = [3; 4]; own.properties.ApplicationData.PeerAxes = 2;
t = twoLines; t.children = [t.children; own];
cases{end + 1} = add('B9z1 hgsave legend node whole', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
z = own; z.properties = rmfield(z.properties, 'ApplicationData');
t = twoLines; t.children = [t.children; z];
cases{end + 1} = add('B9z2 without ApplicationData', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
keep = {'String', 'Location', 'Axes', 'PlotChildren', 'ApplicationData'};
z = own; z.properties = rmfield(z.properties, setdiff(fieldnames(z.properties), keep));
t = twoLines; t.children = [t.children; z];
cases{end + 1} = add('B9z3 String Location Axes PlotChildren ApplicationData', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
z4 = z; z4.properties.ApplicationData = struct('PostDeserializeFcn', @legendpostdeserialize);
t = twoLines; t.children = [t.children; z4];
cases{end + 1} = add('B9z4 the same with PostDeserializeFcn alone in the appdata', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
keep5 = {'String', 'Location', 'Axes', 'PlotChildren', 'Position', 'XLim', 'YLim', 'XTick', 'YTick', 'XTickLabel', 'YTickLabel'};
z5 = own; z5.properties = rmfield(z5.properties, setdiff(fieldnames(z5.properties), keep5));
t = twoLines; t.children = [t.children; z5];
cases{end + 1} = add('B9z5 no appdata, the axes-like fields kept', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
z6 = own; z6.properties.ApplicationData = rmfield(z6.properties.ApplicationData, 'PostDeserializeFcn');
t = twoLines; t.children = [t.children; z6];
cases{end + 1} = add('B9z6 whole but no PostDeserializeFcn', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'legend'), 'String')});
% hgsave's own tree of the gui figure: whether the struct form carries a control's context menu
% and a button group's selected object at all (hgsave wrote neither field).
try
    hg = openfig(fullfile(rt, 's_gui_only.fig'), 'invisible');
    edc = get(findobj(hg, 'Tag', 'ed'), 'ContextMenu');
    fprintf('hgsave gui: ed.ContextMenu=%s bg.SelectedObject=%s\n', class(edc), get(get(findobj(hg, 'Tag', 'bg'), 'SelectedObject'), 'Tag'));
    delete(hg);
catch e
    fprintf('hgsave gui: %s\n', e.message);
end

% the colour bar: hgsave's own node grafted onto an image axes, whole and cut down, and by hand
imageAxes = fig1; imageAxes.children.children = imn; imageAxes.children.properties.CLim = [1 16];
cbOwn = kids(arrayfun(@(c) strcmp(c.type, 'scribe.colorbar'), kids));
cbOwn.handle = 21; cbOwn.properties.Axes = 2;
t = imageAxes; t.children = [t.children; cbOwn];
cases{end + 1} = add('C1 hgsave colorbar node whole', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
z = cbOwn; z.properties = rmfield(z.properties, 'ApplicationData');
t = imageAxes; t.children = [t.children; z];
cases{end + 1} = add('C2 colorbar without ApplicationData', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
z = cbOwn; z.children = []; z.properties = rmfield(z.properties, 'Image');
t = imageAxes; t.children = [t.children; z];
cases{end + 1} = add('C3 colorbar without its image child', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
appcb = struct('NonDataObject', [], 'PostDeserializeFcn', @colorbarpostdeserialize);
z = leaf('scribe.colorbar', 21, struct('Location', 'eastoutside', 'Axes', 2, 'Limits', [1 16], 'XLim', [-1 2], 'YLim', [1 16], 'Tag', 'CB', 'ApplicationData', appcb));
t = imageAxes; t.children = [t.children; z];
cases{end + 1} = add('C4 hand colorbar: Location Axes Limits XLim YLim appdata', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits'), get(findobj(h, 'Type', 'colorbar'), 'Location')});
z = leaf('scribe.colorbar', 21, struct('Location', 'eastoutside', 'Axes', 2, 'Limits', [1 16], 'Tag', 'CB', 'ApplicationData', appcb));
t = imageAxes; t.children = [t.children; z];
cases{end + 1} = add('C5 hand colorbar: Location Axes Limits appdata', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
z = leaf('scribe.colorbar', 21, struct('Location', 'eastoutside', 'Axes', 2, 'Limits', [1 16], 'Tag', 'CB'));
t = imageAxes; t.children = [t.children; z];
cases{end + 1} = add('C6 hand colorbar: Location Axes Limits, no appdata', t, @(h) {get(h.Children, 'Type')', get(findobj(h, 'Type', 'colorbar'), 'Limits')});
z = leaf('scribe.colorbar', 21, struct('Location', 'southoutside', 'Axes', 2, 'Limits', [1 16], 'Ticks', [2 8 14], 'TickLabels', {{'lo'; 'mid'; 'hi'}}, 'FontSize', 7, 'Tag', 'CB', 'ApplicationData', appcb));
t = imageAxes; t.children = [t.children; z];
cases{end + 1} = add('C7 hand colorbar south with ticks and labels', t, @(h) {get(findobj(h, 'Type', 'colorbar'), 'Location'), get(findobj(h, 'Type', 'colorbar'), 'Ticks'), get(findobj(h, 'Type', 'colorbar'), 'TickLabels')', get(findobj(h, 'Type', 'colorbar'), 'FontSize')});

% bars and areas: hgsave's own nodes (with the baseline sibling a bar has), whole and cut down, and by hand
sc = load(fullfile(rt, 's_charts_only.fig'), '-mat');
caxes = sc.hgS_070000.children;
barAxes = caxes(arrayfun(@(a) any(arrayfun(@(c) strcmp(c.type, 'specgraph.barseries'), a.children)), caxes));
barKids = barAxes.children;
t = fig1; t.children.children = barKids;
cases{end + 1} = add('C8 hgsave bar node with its baseline sibling', t, @(h) {get(h.Children.Children, 'Type')', class(findobj(h, 'Type', 'bar'))});
t = fig1; t.children.children = barKids(arrayfun(@(c) strcmp(c.type, 'specgraph.barseries'), barKids));
cases{end + 1} = add('C9 hgsave bar node alone', t, @(h) {get(h.Children.Children, 'Type')', class(findobj(h, 'Type', 'bar'))});
t = fig1; t.children.children = leaf('specgraph.barseries', 3, struct('XData', 1:3, 'YData', [3 1 2], 'BarPeers', 0, 'Tag', 'C'));
cases{end + 1} = add('C10 hand bar: XData YData BarPeers 0', t, @(h) {class(h.Children.Children), get(h.Children.Children, 'Type')});
t = fig1; t.children.children = leaf('specgraph.barseries', 3, struct('XData', 1:3, 'YData', [3 1 2], 'BaseValue', 0, 'BarPeers', 0, 'FaceColor', [0.2 0.6 0.2], 'Tag', 'C'));
cases{end + 1} = add('C11 hand bar: + BaseValue FaceColor', t, @(h) {class(h.Children.Children), get(h.Children.Children, 'Type')});
bl = leaf('specgraph.baseline', 4, struct('Axis', int32(1), 'Description', 'Axes YBaseline', 'HandleVisibility', 'off', 'Internal', true, 'Orientation', 'X'));
t = fig1; t.children.children = [leaf('specgraph.barseries', 3, struct('XData', 1:3, 'YData', [3 1 2], 'BaseLine', 4, 'BarPeers', 0, 'Tag', 'C')); bl];
cases{end + 1} = add('C12 hand bar with BaseLine naming a hand baseline', t, @(h) {get(h.Children.Children, 'Type')', class(findobj(h, 'Type', 'bar'))});
t = fig1; t.children.children = leaf('specgraph.barseries', 3, struct('XData', 1:3, 'YData', [3 1 2], 'Tag', 'C', 'ApplicationData', struct('SerializedAnnotationV7', struct('LegendInformation', struct('IconDisplayStyle', 'on')))));
cases{end + 1} = add('C13 hand bar with the legend appdata', t, @(h) {class(h.Children.Children), get(h.Children.Children, 'Type')});
areaAxes = caxes(arrayfun(@(a) any(arrayfun(@(c) strcmp(c.type, 'specgraph.areaseries'), a.children)), caxes));
t = fig1; t.children.children = areaAxes.children;
cases{end + 1} = add('C14 hgsave area node whole', t, @(h) {get(h.Children.Children, 'Type')', class(findobj(h, 'Type', 'area'))});
t = fig1; t.children.children = leaf('specgraph.areaseries', 3, struct('XData', 1:4, 'YData', [1 3 2 4], 'AreaPeers', 0, 'Tag', 'C'));
cases{end + 1} = add('C15 hand area: XData YData AreaPeers 0', t, @(h) {class(h.Children.Children), get(h.Children.Children, 'Type')});

% scatter: what keeps its data lengths matched
t = fig1; t.children.children = leaf('specgraph.scattergroup', 3, struct('XData', 1:4, 'YData', [1 3 2 4], 'SizeData', 36, 'Tag', 'C'));
cases{end + 1} = add('C16 hand scatter with SizeData', t, @(h) {class(h.Children.Children), h.Children.Children.SizeData});
t = fig1; t.children.children = leaf('specgraph.scattergroup', 3, struct('XData', 1:4, 'YData', [1 3 2 4], 'SizeData', 36, 'CData', [0.8 0 0], 'MarkerFaceColor', 'flat', 'MarkerEdgeColor', 'none', 'Tag', 'C'));
cases{end + 1} = add('C17 hand scatter with SizeData CData flat', t, @(h) {class(h.Children.Children), h.Children.Children.CData, h.Children.Children.MarkerFaceColor});
t = fig1; t.children.children = leaf('specgraph.scattergroup', 3, struct('XData', 1:4, 'YData', [1 3 2 4], 'SizeData', [10 20 30 40], 'CData', [0.8 0 0], 'MarkerFaceColor', [0 0 1], 'Tag', 'C'));
cases{end + 1} = add('C18 hand scatter with sizes per point and a face colour', t, @(h) {class(h.Children.Children), h.Children.Children.SizeData, h.Children.Children.MarkerFaceColor});
t = fig1; t.children.children = leaf('histogram', 3, struct('Data', [1 2 2 3 3 3 4], 'BinEdges', [0.5 1.5 2.5 3.5 4.5], 'FaceColor', [0.2 0.2 0.8], 'Tag', 'C'));
cases{end + 1} = add('C19 hand histogram with BinEdges', t, @(h) {class(h.Children.Children), h.Children.Children.BinEdges, h.Children.Children.FaceColor});
% values the loader may refuse: does one bad value abort the figure, or is it passed over?
t = fig1; t.children.children.properties.Color = 'nonsense'; t.children.properties.XScale = 'bogus'; t.properties.Renderer = 'opengl'; t.properties.Theme = 'light';
cases{end + 1} = add('C20 bad values among good ones', t, @(h) {h.Children.Children.Tag, h.Children.XScale, h.Renderer, class(h.Theme)});
t = fig1; t.children.children.properties.XData = 'abc'; t.children.children.properties.LineWidth = -1;
cases{end + 1} = add('C21 wrong types', t, @(h) {h.Children.Children.Tag, h.Children.Children.LineWidth, class(h.Children.Children.XData)});
t = fig1; t.children.children.properties.NextSeriesIndex = 2; t.children.children.properties.XVariable = ''; t.children.properties.NextSeriesIndex = 3; t.children.properties.XLimitMethod = 'tickaligned'; t.children.properties.GridLineWidth = 0.5;
cases{end + 1} = add('C22 the newer names', t, @(h) {h.Children.Children.Tag, h.Children.NextSeriesIndex, h.Children.XLimitMethod});
fprintf('cases: %d\n', numel(cases));

% OI77_ONLY names cases to run (comma-separated numbers); OI77_INT keeps the integer handles the
% trees are built with, where the default makes every handle but the figure's fractional, as
% hgsave's are (a legend tree with integer handles was an access violation).
only = str2double(strsplit(getenv('OI77_ONLY'), ','));
if ~isempty(getenv('OI77_ONLY')), picks = only; else, picks = from:numel(cases); end
integers = ~isempty(getenv('OI77_INT'));
fprintf('handles: %s\n', char("fractional" + strings(integers == 0) + "integer" + strings(integers == 1)));
for i = picks
    c = cases{i};
    fprintf('--- [%d] %s\n', i, c{1});
    if numel(c) > 3, extra = c{4}; else, extra = ''; end
    tree = c{2};
    if ~integers, tree = frac(tree); end
    try1(rt, i, tree, c{3}, extra);
end

h = openfig(fullfile(rt, 'h01.fig'), 'invisible');
png = fullfile(rt, 'h01.png');
try, print(h, '-dpng', '-r50', png); d = dir(png); fprintf('print wrote %d bytes\n', d.bytes); catch e, fprintf('print: %s\n', e.message); end
delete(h);
fprintf('done\n');

function try1(rt, i, tree, look, extra)
file = fullfile(rt, sprintf('h%02d.fig', i));
hgS_070000 = tree; %#ok<NASGU>
if strcmp(extra, 'extra')
    jgraph_document = uint8('{"format":"jgraph"}')'; %#ok<NASGU>
    save(file, 'hgS_070000', 'jgraph_document', '-mat');
else
    save(file, 'hgS_070000', '-mat');
end
lastwarn('');
try
    [txt, h] = evalc('openfig(file, ''invisible'')');
    if ~isempty(strtrim(txt)), fprintf('  printed: %s\n', strrep(strtrim(txt), newline, ' / ')); end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('  warning: %s | %s\n', id, strrep(msg, newline, ' / ')); end
    try
        v = look(h);
        fprintf('  %s\n', shortv(v));
    catch e
        fprintf('  look failed: %s\n', e.message);
    end
    delete(h);
catch e
    fprintf('  error: %s | %s\n', e.identifier, strrep(e.message, newline, ' / '));
end
end

function t = frac(t)
% Every handle but a figure's, and every property that names one, made fractional (hgsave's own
% handles are: 0.00012207, 13.00012207 ...), so that an integer is never read as a figure's number.
d = 0.0001220703125;
for i = 1:numel(t)
    if ~strcmp(t(i).type, 'figure') && ~isnan(t(i).handle)
        t(i).handle = t(i).handle + d;
    end
    p = t(i).properties;
    for f = {'Axes', 'PlotChildren', 'UIContextMenu', 'ContextMenu', 'SelectedObject', 'Image', 'BaseLine'}
        if isfield(p, f{1}) && isnumeric(p.(f{1}))
            p.(f{1}) = p.(f{1}) + d;
        end
    end
    if isfield(p, 'ApplicationData') && isstruct(p.ApplicationData)
        for f = {'PlotChildren', 'PeerAxes'}
            if isfield(p.ApplicationData, f{1})
                p.ApplicationData.(f{1}) = p.ApplicationData.(f{1}) + d;
            end
        end
    end
    t(i).properties = p;
    if isstruct(t(i).children)
        t(i).children = frac(t(i).children);
    end
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
