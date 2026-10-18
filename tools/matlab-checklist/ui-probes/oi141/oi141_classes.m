% oi141_classes - open item 141: the legend title's class rows in the three recordings the MATLAB
% dialect reads classes from, in their own formats, for this one class. Headless. The K| and I|
% lines go into u7/u7_classes.out.txt (u7/gen_classes.py: isa and the class chain), the
% class|Type|name|hidden|settable and SET| lines into graphics-probes/recorded/graphics_class_names.txt,
% the METHODS| ... OBSERVABLE| lines into graphics-probes/recorded/graphics_class_members.txt
% (gen_graphics_class_names.ps1). The three source probes list the legend title too, so a full
% re-recording makes the same rows.
set(groot, 'DefaultFigureVisible', 'off');
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, 1:3);
lg = legend(ax);
obj = lg.Title;

% u7_classes.m's two lines
extra = {'matlab.graphics.axis.AbstractAxes', 'matlab.graphics.chart.Chart', 'matlab.graphics.primitive.Data', ...
    'matlab.graphics.mixin.Legendable', 'matlab.graphics.mixin.AxesParentable', 'matlab.graphics.mixin.UIParentable', ...
    'matlab.graphics.mixin.Selectable', 'matlab.graphics.mixin.SceneNodeGroup', 'matlab.graphics.mixin.Background', ...
    'matlab.graphics.mixin.ColorOrderUser', 'matlab.graphics.mixin.DataProperties', 'matlab.graphics.mixin.Pickable', ...
    'matlab.graphics.mixin.GraphicsPickable', 'matlab.graphics.mixin.ChartLayoutable', 'matlab.graphics.mixin.Themeable', ...
    'matlab.graphics.primitive.world.Group', 'matlab.graphics.primitive.world.SceneNode', ...
    'matlab.graphics.primitive.world.CompositeMarker', 'matlab.graphics.primitive.canvas.Canvas', ...
    'matlab.ui.container.Container', 'matlab.ui.container.CanvasContainer', 'matlab.ui.control.Component', ...
    'matlab.ui.control.WebComponent', 'matlab.ui.componentcontainer.ComponentContainer', ...
    'matlab.graphics.layout.Layout', 'matlab.graphics.layout.Layoutable', 'matlab.graphics.layout.LayoutOptions', ...
    'matlab.graphics.datatip.DataTipRow', 'matlab.graphics.axis.decorator.Ruler', ...
    'matlab.graphics.axis.decorator.ScalableAxisRuler', 'matlab.graphics.axis.decorator.DecorationContainer', ...
    'matlab.graphics.shape.Shape', 'matlab.graphics.shape.Annotation', 'matlab.graphics.illustration.ColorBar', ...
    'matlab.graphics.chart.primitive.Data', 'matlab.graphics.chart.decoration.Decoration', ...
    'matlab.graphics.primitive.Line', 'matlab.graphics.primitive.Surface', 'matlab.graphics.primitive.Rectangle', ...
    'matlab.graphics.function.FunctionLine', 'matlab.graphics.Group', 'matlab.ui.control.StateButton', ...
    'matlab.ui.control.Button', 'matlab.ui.control.EditField', 'matlab.ui.control.Slider', ...
    'matlab.ui.container.Panel', 'matlab.ui.container.Menu', 'matlab.graphics.GraphicsPlaceholder', ...
    'matlab.ui.control.internal.model.ComponentModel', 'matlab.graphics.internal.GraphicsBaseFunctions', ...
    'matlab.graphics.internal.GraphicsCoreProperties', 'matlab.graphics.internal.GraphicsJavaVisible', ...
    'matlab.graphics.internal.Legacy', 'matlab.ui.container.internal.UIContainer', 'JavaVisible', 'hgsetget'};
s = superclasses(obj);
fprintf('K|legendtitle|%s|%s\n', class(obj), strjoin(s', ','));
fprintf('I|legendtitle|%s\n', strjoin(extra(cellfun(@(n) isa(obj, n), extra)), ','));

% graphics_class_names.m's lines
mc = metaclass(obj);
if isprop(obj, 'Type'), ty = get(obj, 'Type'); else, ty = '-'; end
for p = mc.PropertyList'
    if ischar(p.GetAccess) && strcmp(p.GetAccess, 'public')
        fprintf('%s|%s|%s|%d|%d\n', mc.Name, ty, p.Name, p.Hidden, ischar(p.SetAccess) && strcmp(p.SetAccess, 'public'));
    end
end
name = class(obj);
sv = set(obj);
for p = fieldnames(sv)'
    v = sv.(p{1});
    if iscell(v) && isequal(size(v), [0 1])
        fprintf('SET|%s|%s|\n', name, p{1});
        continue
    end
    if isempty(v) || ~iscell(v), continue, end
    words = cellfun(@char, v(:)', 'UniformOutput', false);
    fprintf('SET|%s|%s|%s\n', name, p{1}, strjoin(words, char(9)));
end

% graphics_class_members.m's lines
fprintf('METHODS|%s|%s\n', name, strjoin(methods(obj)', ' '));
fprintf('PROPS|%s|%s\n', name, strjoin(properties(obj)', ' '));
listing = splitlines(evalc('methods(obj)'));
part = '';
shown = struct('LISTED', {{}}, 'STATIC', {{}});
for j = 1:numel(listing)
    row = strtrim(listing{j});
    if startsWith(row, 'Methods for class'), part = 'LISTED'; continue, end
    if strcmp(row, 'Static methods:'), part = 'STATIC'; continue, end
    if isempty(row) || isempty(part) || contains(row, '<a href'), continue, end
    shown.(part) = [shown.(part), strsplit(row)];
end
fprintf('LISTED|%s|%s\n', name, strjoin(shown.LISTED, ' '));
fprintf('STATIC|%s|%s\n', name, strjoin(shown.STATIC, ' '));
fprintf('EVENTS|%s|%s\n', name, strjoin(events(obj)', ' '));
fprintf('LISTENS|%s|%s\n', name, strjoin(sort({mc.EventList.Name}), ' '));
props = mc.PropertyList;
keep = arrayfun(@(p) p.SetObservable && ischar(p.GetAccess) && strcmp(p.GetAccess, 'public') ...
    && ~endsWith(p.Name, '_I'), props);
fprintf('OBSERVABLE|%s|%s\n', name, strjoin(sort({props(keep).Name}), ' '));
fprintf('isa Text: %d  isa Graphics: %d\n', isa(obj, 'matlab.graphics.primitive.Text'), isa(obj, 'matlab.graphics.Graphics'));
fprintf('methods listing:\n%s', evalc('methods(obj)'));
fprintf('properties listing:\n%s', evalc('properties(obj)'));
close all force
