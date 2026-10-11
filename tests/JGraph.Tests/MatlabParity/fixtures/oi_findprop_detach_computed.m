% record: -noFigureWindows
% Open items 91, 93, 94 and 95 (ADR 0224): findprop on a graphics handle, and a deleted handle's
% class members and dot; the defaults a high-level maker leaves to the axes' orders and a low-level
% one takes, and a figure made by a plot taking the root's; Parent = [] on placed components, menus
% and tools, and menus and tools made without a parent; R2025b's names whose values are objects,
% tables or worked out from the data and the layout. Probes probe_91, probe_91b, probe_93b, probe_93d,
% probe_93e, probe_94, probe_95, probe_95b and probe_95c (open-items scratch). The colours used are
% exact in 8 bits, as this build keeps colours in 8 bits.
close all force

% --- item 91: findprop, and a deleted handle
f = figure('Visible', 'off');
ln = plot(axes(f), 1:3);
p = findprop(ln, 'Color');
u9b_chk('findprop_line', @() {class(p), p.Name, p.GetAccess, p.SetAccess, p.SetObservable, p.Hidden, class(p.DefiningClass), p.DefiningClass.Name});
u9b_chk('findprop_through_dot', @() ln.findprop('LineWidth').Name);
u9b_chk('findprop_read_only', @() findprop(ln, 'Type').SetAccess);
u9b_chk('findprop_axes', @() {findprop(gca, 'XLim').Name, findprop(gca, 'XLim').SetObservable});
u9b_chk('findprop_unknown', @() {class(findprop(ln, 'Bogus')), size(findprop(ln, 'Bogus'))});
u9b_chk('findprop_case', @() isempty(findprop(ln, 'color')));
u9b_chk('findprop_root', @() findprop(groot, 'CurrentFigure').Name);
u9b_chk('findprop_two', @() as_statement(@() findprop([ln ln], 'Color')));
u9b_chk('findprop_number', @() as_statement(@() findprop(5, 'x')));
b = uibutton(uifigure('Visible', 'off'));
u9b_chk('findprop_component', @() {class(findprop(b, 'Text')), findprop(b, 'Text').Name});
delete(ln);
u9b_chk('deleted_members', @() [numel(methods(ln)), numel(properties(ln)), numel(events(ln))]);
u9b_chk('deleted_ismethod', @() [ismethod(ln, 'set'), isprop(ln, 'Color')]);
u9b_chk('deleted_dot_known', @() as_statement(@() ln.Color));
u9b_chk('deleted_dot_unknown', @() as_statement(@() ln.Bogus));
u9b_chk('deleted_dot_method', @() as_statement(@() ln.set('Color', 'r')));
u9b_chk('deleted_write', @() as_statement(@() write_color(ln)));
u9b_chk('deleted_get', @() as_statement(@() get(ln, 'Color')));
u9b_chk('deleted_findprop', @() isempty(findprop(ln, 'Color')));
close all force

% --- item 93: which makers take which defaults
f = figure('Visible', 'off');
ax = axes(f);
set(groot, 'DefaultLineMarker', 'o');
set(groot, 'DefaultLineLineStyle', ':');
h = line(ax, 1:3, 1:3);
u9b_chk('line_xy_leaves_orders', @() {h.Marker, h.LineStyle});
h2 = line('Parent', ax, 'XData', 1:3, 'YData', 1:3);
u9b_chk('line_named_takes', @() {h2.Marker, h2.LineStyle});
set(groot, 'DefaultLineMarker', 'remove');
set(groot, 'DefaultLineLineStyle', 'remove');
set(groot, 'DefaultLineColor', [1 0 0]);
u9b_chk('line_colour', @() [isequal(get(line(ax, 1:3, 1:3), 'Color'), [1 0 0]), isequal(get(line('Parent', ax, 'XData', 1:3, 'YData', 1:3), 'Color'), [1 0 0])]);
set(groot, 'DefaultLineColor', 'remove');
u9b_chk('line_named_defaults', @() {get(line('Parent', ax), 'XData'), get(line('Parent', ax, 'XData', 1:3, 'YData', 1:3, 'ZData', 1:3), 'ZData')});
u9b_chk('line_named_odd', @() as_statement(@() line('XData')));
set(groot, 'DefaultPatchFaceColor', [0 0 1]);
u9b_chk('patch_named_takes', @() get(patch('Parent', ax, 'XData', [0 1 1], 'YData', [0 0 1]), 'FaceColor'));
u9b_chk('patch_named_given', @() get(patch('Parent', ax, 'XData', [0 1 1], 'YData', [0 0 1], 'FaceColor', 'r'), 'FaceColor'));
u9b_chk('patch_row_is_rgb', @() get(patch(ax, [0 1 1], [0 0 1], [0 1 0]), 'FaceColor'));
set(groot, 'DefaultPatchFaceColor', 'remove');
set(groot, 'DefaultStemColor', [1 0 0]);
set(groot, 'DefaultStemMarker', 's');
st = stem(ax, 1:3);
u9b_chk('stem_defaults', @() {isequal(st.Color, [1 0 0]), st.Marker});
set(groot, 'DefaultStemColor', 'remove');
set(groot, 'DefaultStemMarker', 'remove');
set(groot, 'DefaultBarFaceColor', [1 0 0]);
set(groot, 'DefaultBarBarWidth', 0.4);
br = bar(ax, 1:3);
u9b_chk('bar_defaults', @() {isequal(br.FaceColor, [1 0 0]), br.BarWidth});
set(groot, 'DefaultBarFaceColor', 'remove');
set(groot, 'DefaultBarBarWidth', 'remove');
set(groot, 'DefaultScatterMarker', 's');
u9b_chk('scatter_default', @() get(scatter(ax, 1:3, 1:3), 'Marker'));
set(groot, 'DefaultScatterMarker', 'remove');
set(groot, 'DefaultTextColor', [0 0 1]);
ax2 = axes(figure('Visible', 'off'));
u9b_chk('title_takes_text_colour', @() ax2.Title.Color);
u9b_chk('xlabel_does_not', @() isequal(ax2.XLabel.Color, [0 0 1]));
set(groot, 'DefaultTextColor', 'remove');
set(groot, 'DefaultTextInterpreter', 'none');
ax3 = axes(figure('Visible', 'off'));
u9b_chk('labels_take_interpreter', @() {ax3.Title.Interpreter, ax3.XLabel.Interpreter, ax3.YLabel.Interpreter});
set(groot, 'DefaultTextInterpreter', 'remove');
close all force
set(groot, 'DefaultFigureColor', [1 0 0]);
plot(1:3);
u9b_chk('plot_figure_takes', @() get(gcf, 'Color'));
close all force
axes;
u9b_chk('axes_figure_takes', @() get(gcf, 'Color'));
close all force
u9b_chk('gcf_figure_takes', @() get(gcf, 'Color'));
close all force
u = uifigure('Visible', 'off');
u9b_chk('uifigure_takes', @() u.Color);
delete(u);
set(groot, 'DefaultFigureColor', 'remove');
set(groot, 'DefaultFigureName', 'held');
close all force
plot(1:3);
u9b_chk('plot_figure_name', @() get(gcf, 'Name'));
set(groot, 'DefaultFigureName', 'remove');
close all force

% --- item 94: Parent = [], and menus and tools made without a parent
u = uifigure('Visible', 'off');
b = uibutton(u, 'Text', 'go');
b.Parent = [];
u9b_chk('detach', @() {isempty(b.Parent), numel(u.Children), b.Text, isvalid(b)});
b.Parent = u;
u9b_chk('reattach', @() numel(u.Children));
set(b, 'Parent', []);
u9b_chk('set_detach', @() numel(u.Children));
pn = uipanel(u);
c = uibutton(pn);
pn.Parent = [];
u9b_chk('panel_detach_keeps_children', @() [numel(u.Children), numel(pn.Children), isequal(c.Parent, pn)]);
g = uigridlayout(u, [1 2]);
gb = uibutton(g);
gb.Parent = [];
u9b_chk('grid_child_detach', @() numel(g.Children));
gb.Parent = g;
u9b_chk('grid_child_back', @() gb.Layout.Column);
g.Parent = [];
ua = uiaxes(u);
ua.Parent = [];
u9b_chk('grid_and_uiaxes_detach', @() [isempty(g.Parent), isempty(ua.Parent)]);
m = uimenu('Parent', []);
u9b_chk('menu_no_parent', @() {isempty(m.Parent), m.Text});
m.Parent = u;
sub = uimenu('Parent', [], 'Text', 'sub');
sub.Parent = m;
u9b_chk('menu_placed', @() [isequal(m.Parent, u), numel(m.Children)]);
m.Parent = [];
u9b_chk('menu_detach_keeps_children', @() [isempty(m.Parent), numel(sub.Parent.Children)]);
cm = uicontextmenu('Parent', []);
u9b_chk('contextmenu_no_parent', @() isempty(cm.Parent));
cm.Parent = u;
u9b_chk('contextmenu_placed', @() isequal(cm.Parent, u));
tb = uitoolbar('Parent', []);
pt = uipushtool('Parent', []);
tt = uitoggletool('Parent', []);
u9b_chk('bars_no_parent', @() [isempty(tb.Parent), isempty(pt.Parent), isempty(tt.Parent)]);
pt.Parent = tb;
tb.Parent = u;
u9b_chk('bars_placed', @() [numel(tb.Children), isequal(tb.Parent, u)]);
fc = figure('Visible', 'off');
c2 = uicontrol(fc, 'Style', 'pushbutton');
c2.Parent = [];
u9b_chk('uicontrol_detach', @() isempty(c2.Parent));
u9b_chk('uicontrol_no_parent', @() isempty(get(uicontrol('Parent', []), 'Parent')));
u9b_chk('menu_in_panel_refused', @() as_statement(@() uimenu(uipanel('Parent', []))));
u9b_chk('detached_delete', @() delete_detached());
close all force
delete(u);

% --- item 95: objects, tables, and values worked out from the data
f = figure('Visible', 'off');
u9b_chk('theme', @() {class(f.Theme), f.Theme.Name, f.Theme.BaseColorStyle, f.ThemeMode});
f.Theme = 'dark';
u9b_chk('theme_written', @() {f.Theme.Name, f.Theme.BaseColorStyle, f.ThemeMode});
ax = axes(f);
u9b_chk('interaction_options', @() {class(ax.InteractionOptions), properties(ax.InteractionOptions)', ax.InteractionOptions.LimitsDimensions});
ax.InteractionOptions.LimitsDimensions = 'x';
u9b_chk('interaction_options_written', @() ax.InteractionOptions.LimitsDimensions);
pax = polaraxes(figure('Visible', 'off'));
u9b_chk('polar_interaction_options', @() {class(pax.InteractionOptions), properties(pax.InteractionOptions)'});
gr = hggroup(ax);
u9b_chk('group_annotation', @() gr.Annotation.LegendInformation.IconDisplayStyle);
gr.Annotation.LegendInformation.IconDisplayStyle = 'on';
u9b_chk('group_annotation_written', @() get(get(get(gr, 'Annotation'), 'LegendInformation'), 'IconDisplayStyle'));
u9b_chk('transform_annotation', @() get(hgtransform(ax), 'Annotation').LegendInformation.IconDisplayStyle);
hold(ax, 'on');
ln = plot(ax, 1:3);
st = stem(ax, 1:3);
hold(ax, 'off');
u9b_chk('source_table', @() {class(ln.SourceTable), size(ln.SourceTable), size(st.SourceTable)});
cla(ax);
xlim(ax, [0 50000]); drawnow;
u9b_chk('exponent', @() {class(ax.XAxis.Exponent), ax.XAxis.Exponent, ax.XAxis.ExponentMode});
xlim(ax, [0 0.003]); drawnow;
u9b_chk('exponent_small', @() ax.XAxis.Exponent);
xlim(ax, [0 1000]); drawnow;
u9b_chk('exponent_none', @() ax.XAxis.Exponent);
ax.XAxis.Exponent = 2;
u9b_chk('exponent_written', @() {class(ax.XAxis.Exponent), ax.XAxis.Exponent, ax.XAxis.ExponentMode});
u9b_chk('exponent_refused', @() as_statement(@() set(ax.XAxis, 'Exponent', 'a')));
ax.XAxis.ExponentMode = 'auto';
xlim(ax, [0 1e6]); drawnow;
u9b_chk('exponent_auto_again', @() ax.XAxis.Exponent);
xlim(ax, [0 5]); xticks(ax, 0:5); drawnow;
u9b_chk('minor_fifths', @() ax.XAxis.MinorTickValues);
xlim(ax, [0 10]); xticks(ax, 0:2:10); drawnow;
u9b_chk('minor_quarters', @() ax.XAxis.MinorTickValues);
set(ax, 'XScale', 'log'); xlim(ax, [1 1000]); xticks(ax, [1 10 100 1000]); drawnow;
u9b_chk('minor_log', @() ax.XAxis.MinorTickValues);
set(ax, 'XScale', 'linear'); xlim(ax, [0 5]); xticks(ax, 0:5);
ax.XAxis.MinorTickValues = [0.5 1.5];
xlim(ax, [0 10]); drawnow;
u9b_chk('minor_written', @() {ax.XAxis.MinorTickValues, ax.XAxis.MinorTickValuesMode});
xlim(ax, [0 10]); ylim(ax, [0 100]);
t = title(ax, 'Hello'); drawnow;
u9b_chkdiv('title_position', @() round(t.Position, 2), '0224');
u9b_chk('title_position_mode', @() t.PositionMode);
t.Position = [2 50 0];
u9b_chk('title_position_written', @() {t.Position, t.PositionMode});
hold(ax, 'on');
q = quiver(ax, [0 1], [0 0], [1 1], [1 2]);
q2 = quiver(ax, [0 1], [0 0], [1 1], [1 2], 0.5);
q3 = quiver(ax, [0 1], [0 0], [1 1], [1 2], 0);
[X, Y] = meshgrid(0:0.5:2, 0:0.5:1);
q4 = quiver(ax, X, Y, cos(X), sin(Y));
u9b_chk('quiver_scale', @() round([q.ScaleFactor, q2.ScaleFactor, q2.AutoScaleFactor, q3.ScaleFactor, q4.ScaleFactor], 9));
q5 = quiver(ax, [1 2 3; 4 5 6], [1 1 1; 1 1 1]);
u9b_chk('quiver_components_alone', @() {unique(q5.XData(:))', unique(q5.YData(:))', round(q5.ScaleFactor, 9)});
u9b_chkdiv('quiver_components_xdata', @() q5.XData, '0224');
close all force

function write_color(h)
h.Color = 'r';
end

function r = delete_detached()
b = uibutton('Parent', []);
delete(b);
r = isvalid(b);
end

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end
