function u7_classes
% Records, for each kind of graphics object JGraph makes, R2025b's class and superclasses.
f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
uf = uifigure('Visible', 'off'); g = uigridlayout(uf);
bg = uibuttongroup(uf);
t = tiledlayout(figure('Visible', 'off'), 1, 1); tax = nexttile(t);
[X, Y, Z] = peaks(8);
tb = axtoolbar(ax);
dl = plot(ax, 1:3);
m = {
 'root', @() groot
 'figure', @() f
 'axes', @() ax
 'uiaxes', @() uiaxes(uf)
 'polaraxes', @() polaraxes(figure('Visible', 'off'))
 'numericruler', @() ax.XAxis
 'tiledlayout', @() t
 'tiledlayoutoptions', @() tax.Layout
 'legend', @() legend(ax)
 'colorbar', @() colorbar(ax)
 'light', @() light(ax)
 'axestoolbar', @() tb
 'toolbarstatebutton', @() axtoolbarbtn(tb, 'state')
 'toolbarpushbutton', @() axtoolbarbtn(tb, 'push')
 'text', @() text(ax, 0, 0, 'a')
 'hggroup', @() hggroup(ax)
 'hgtransform', @() hgtransform(ax)
 'line', @() dl
 'stair', @() stairs(ax, 1:3)
 'scatter', @() scatter(ax, 1:3, 1:3)
 'bar', @() bar(ax, 1:3)
 'area', @() area(ax, 1:3)
 'pie', @() pie(axes(figure('Visible', 'off')), [1 2 3])
 'piechart', @() piechart(figure('Visible', 'off'), [1 2 3])
 'heatmap', @() heatmap(figure('Visible', 'off'), magic(3))
 'boxchart', @() boxchart(axes(figure('Visible', 'off')), [1; 2; 3; 4])
 'stem', @() stem(ax, 1:3)
 'histogram', @() histogram(ax, [1 2 2 3])
 'errorbar', @() errorbar(ax, 1:3, 1:3)
 'surface', @() surf(ax, X, Y, Z)
 'contour', @() u7_second(@() contour(ax, X, Y, Z))
 'patch', @() patch(ax, [0 1 1], [0 0 1], 'r')
 'quiver', @() quiver(ax, 1:3, 1:3, 1:3, 1:3)
 'image', @() image(ax, magic(3))
 'textbox', @() annotation(f, 'textbox')
 'textarrow', @() annotation(f, 'textarrow')
 'doublearrow', @() annotation(f, 'doublearrow')
 'arrow', @() annotation(f, 'arrow')
 'annotationline', @() annotation(f, 'line')
 'ellipse', @() annotation(f, 'ellipse')
 'rectangle', @() annotation(f, 'rectangle')
 'datatip', @() datatip(dl, 1, 1)
 'datatiptemplate', @() dl.DataTipTemplate
 'datatiptextrow', @() dl.DataTipTemplate.DataTipRows(1)
 'uicontextmenu', @() uicontextmenu(f)
 'uimenu', @() uimenu(f)
 'uicontrol', @() uicontrol(f)
 'uipanel', @() uipanel(uf)
 'uibuttongroup', @() bg
 'uigridlayout', @() g
 'uilabel', @() uilabel(uf)
 'uibutton', @() uibutton(uf)
 'uistatebutton', @() uibutton(uf, 'state')
 'uieditfield', @() uieditfield(uf)
 'uinumericeditfield', @() uieditfield(uf, 'numeric')
 'uitextarea', @() uitextarea(uf)
 'uidropdown', @() uidropdown(uf)
 'uilistbox', @() uilistbox(uf)
 'uicheckbox', @() uicheckbox(uf)
 'uiradiobutton', @() uiradiobutton(bg)
 'uitogglebutton', @() uitogglebutton(uibuttongroup(uf))
 'uislider', @() uislider(uf)
 'uirangeslider', @() uislider(uf, 'range')
 'uispinner', @() uispinner(uf)
 'uiimage', @() uiimage(uf)
 'uihyperlink', @() uihyperlink(uf)
 'uiprogressindicator', @() matlab.ui.control.internal.ProgressIndicator('Parent', uf)
 'animatedline', @() animatedline(ax)
 'bubblechart', @() bubblechart(ax, 1:3, 1:3, 1:3)
 'rectangleprim', @() rectangle(ax)
 'constantline', @() xline(ax, 1)
 'fplot', @() fplot(ax, @sin)
 'gobjects', @() gobjects(1)
 'buttonlayout', @() u7_layout(g)
 };
% superclasses leaves out the bases MATLAB builds in (isa(ax, 'matlab.graphics.axis.AbstractAxes') is
% true and the name is not listed), so the known ones are asked about one by one.
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
for k = 1:size(m, 1)
    try
        h = m{k, 2}();
        h = h(1);
        s = superclasses(h);
        fprintf('K|%s|%s|%s\n', m{k, 1}, class(h), strjoin(s', ','));
        fprintf('I|%s|%s\n', m{k, 1}, strjoin(extra(cellfun(@(n) isa(h, n), extra)), ','));
    catch e
        fprintf('K|%s|ERR %s\n', m{k, 1}, e.message);
    end
end
delete(findall(groot, 'Type', 'figure'));
end

function h = u7_second(fn)
[~, h] = fn();
end

function h = u7_layout(g)
b = uibutton(g);
h = b.Layout;
end
