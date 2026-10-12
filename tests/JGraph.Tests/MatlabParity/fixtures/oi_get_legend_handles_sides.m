% record: -noFigureWindows
% Open items 109 to 111 (ADR 0228): get(h)'s order, UserData's empty and get of several objects;
% R2025b's legend - data1, data2, ..., its commands, options and String; patch, surface and
% rectangle adding to the axes, a 2-D view, and the handle of what a replacing plot deleted; and the
% yyaxis and region remainders - each side's colour order, a ruler's colour mode, the line-style
% order as written, the read-only refusals, scatter3's AlphaData, and a band's Value, colour and
% times. Probes probe_109 to probe_111b (open-items scratch). The colour order's values are open
% item 105, so a colour is compared with the order's entry.
close all force

% --- item 109: get(h) and UserData
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
ln = plot(ax, 1:3);
tx = text(ax, 1, 1, 'a');
pa = patch(ax, [0 1 1], [0 0 1], 'r');
sc = scatter(ax, 1:3, 1:3);
rg = xregion(ax, 1, 2);
lg = legend(ax);
u9b_chk('get_order_line', @() isequal(fieldnames(get(ln)), properties(ln)));
u9b_chk('get_order_axes', @() isequal(fieldnames(get(ax)), properties(ax)));
u9b_chk('get_order_text', @() isequal(fieldnames(get(tx)), properties(tx)));
u9b_chk('get_order_patch', @() isequal(fieldnames(get(pa)), properties(pa)));
u9b_chk('get_order_scatter', @() isequal(fieldnames(get(sc)), properties(sc)));
u9b_chk('get_order_region', @() isequal(fieldnames(get(rg)), properties(rg)));
u9b_chk('get_order_legend', @() isequal(fieldnames(get(lg)), properties(lg)));
u9b_chk('get_order_figure', @() isequal(fieldnames(get(f)), properties(f)));
u9b_chk('get_root', @() fieldnames(get(groot))');
u9b_chk('props_root', @() numel(properties(groot)));
u9b_chk('get_first_line', @() first_names(fieldnames(get(ln)), 8));
u9b_chk('set_first_line', @() first_names(fieldnames(set(ln)), 6));
u9b_chk('userdata_line', @() size(ln.UserData));
u9b_chk('userdata_axes', @() size(ax.UserData));
u9b_chk('userdata_figure', @() size(f.UserData));
u9b_chk('userdata_root', @() size(get(groot, 'UserData')));
u9b_chk('userdata_class', @() class(rg.UserData));
ln.UserData = 5;
ln.UserData = [];
u9b_chk('userdata_reset', @() size(ln.UserData));
two = plot(ax, [1:3; 2:4]');
u9b_chk('get_several', @() {class(get(two)), size(get(two))});
u9b_chk('get_several_one', @() {class(get(two, 'UserData')), size(get(two, 'UserData'))});

% --- item 109: the legend's names
u9b_chk('legend_two', @() legend_two());
u9b_chk('legend_third', @() legend_third());
u9b_chk('legend_named_first', @() legend_named(1));
u9b_chk('legend_named_second', @() legend_named(2));
u9b_chk('legend_rename', @() legend_rename(''));
u9b_chk('legend_rename_x', @() legend_rename('x'));
u9b_chk('legend_one_name', @() legend_one_name());
u9b_chk('legend_handle', @() legend_handle());
u9b_chk('legend_handles_cell', @() legend_handles_cell());
u9b_chk('legend_off', @() legend_off());
u9b_chk('legend_show', @() legend_command('show'));
u9b_chk('legend_toggle', @() legend_command('toggle'));
u9b_chk('legend_boxoff', @() legend_command('boxoff'));
u9b_chk('legend_hide', @() legend_hide());
u9b_chk('legend_options', @() legend_options());
u9b_chk('legend_location_only', @() legend_location_only());
u9b_chk('legend_autoupdate_off', @() legend_autoupdate_off());
u9b_chk('legend_more_names', @() legend_more_names());
u9b_chk('legend_string_set', @() legend_string_set({'u', 'v'}));
u9b_chk('legend_string_fewer', @() legend_string_set({'u'}));
u9b_chk('legend_string_char', @() legend_string_set('u'));
u9b_chk('legend_kinds', @() legend_kinds());
u9b_chk('legend_delete_renumbers', @() legend_delete_renumbers());
u9b_chk('legend_replot', @() legend_replot());
u9b_chk('legend_again', @() legend_again());
u9b_chk('legend_cla', @() legend_cla());
u9b_chk('legend_plotchildren', @() legend_plotchildren());
u9b_chk('legend_none_property', @() legend_none_property());

% --- item 110: low-level verbs, the view, a replaced handle
u9b_chk('patch_adds', @() children_after(@() patch([0 1 1], [0 0 1], 'r')));
u9b_chk('patch_twice', @() children_after(@() patch_twice()));
u9b_chk('patch_by_name', @() children_after(@() patch('XData', [0 1 1], 'YData', [0 0 1])));
u9b_chk('patch_faces', @() children_after(@() patch('Faces', [1 2 3], 'Vertices', [0 0; 1 0; 1 1])));
u9b_chk('surface_adds', @() children_after(@() surface(magic(3))));
u9b_chk('rectangle_adds', @() children_after(@() rectangle()));
u9b_chk('fill_replaces', @() children_after(@() fill([0 1 1], [0 0 1], 'r')));
u9b_chk('patch3_view', @() view_after(@() patch([0 1 1], [0 0 1], [0 0 1], 'r')));
u9b_chk('surface_view', @() view_after(@() surface(magic(3))));
u9b_chk('plot_view', @() view_after(@() []));
u9b_chk('patch_keeps_title', @() patch_keeps());
u9b_chk('view_written', @() view_written());
u9b_chk('replaced_valid', @() replaced_valid());
u9b_chk('replaced_dot_set', @() replaced_write(1));
u9b_chk('replaced_set', @() replaced_write(2));
u9b_chk('replaced_dot_get', @() replaced_write(3));
u9b_chk('replaced_get', @() replaced_write(4));
u9b_chk('replaced_delete', @() replaced_write(5));
u9b_chk('cleared_set', @() cleared_write());
u9b_chk('replaced_in_list', @() replaced_in_list());
u9b_chk('replaced_axes_kept', @() replaced_axes_kept());

% --- item 111: yyaxis sides
u9b_chk('side_orders', @() side_orders());
u9b_chk('side_orders_after_plots', @() side_orders_after_plots());
u9b_chk('side_order_set', @() side_order_set(true));
u9b_chk('side_order_set_fn', @() side_order_set(false));
u9b_chk('side_order_cycle', @() side_order_cycle());
u9b_chk('side_order_before', @() side_order_before());
u9b_chk('side_order_cla', @() side_order_cla());
u9b_chk('ruler_modes', @() ruler_modes());
u9b_chk('ruler_modes_set', @() ruler_modes_set());
u9b_chk('style_order_fresh', @() style_order_fresh());
u9b_chk('style_order_yyaxis', @() style_order_yyaxis());
u9b_chk('style_order_written', @() style_order_written());
u9b_chk('yaxis_set', @() as_statement(@() set_yaxis()));
u9b_chk('xaxis_same', @() as_statement(@() set_xaxis(false)));
u9b_chk('xaxis_other', @() as_statement(@() set_xaxis(true)));
u9b_chk('type_line', @() as_statement(@() set_type('line')));
u9b_chk('type_text', @() as_statement(@() set_type('text')));
u9b_chk('type_axes', @() as_statement(@() set_type('axes')));
u9b_chk('beingdeleted_set', @() as_statement(@() set_type('beingdeleted')));
u9b_chk('series_index_zero', @() series_index_zero());

% --- item 111: scatter3's transparency
u9b_chk('s3_alpha_default', @() s3_alpha(@(s) {size(s.AlphaData), s.AlphaDataMapping}));
u9b_chk('s3_alpha_column', @() s3_alpha(@(s) set_and_read(s, 'AlphaData', [0.1; 0.5; 1])));
u9b_chk('s3_alpha_scalar', @() s3_alpha(@(s) set_and_read(s, 'AlphaData', 0.5)));
u9b_chk('s3_alpha_matrix', @() s3_alpha(@(s) set_and_read(s, 'AlphaData', [1 2; 3 4])));
u9b_chk('s3_alpha_text', @() s3_alpha(@(s) set_and_read(s, 'AlphaData', 'x')));
u9b_chk('s3_mapping', @() s3_alpha(@(s) set_and_read(s, 'AlphaDataMapping', 'none')));
u9b_chk('s3_mapping_bad', @() s3_alpha(@(s) set_and_read(s, 'AlphaDataMapping', 'bogus')));
u9b_chk('s3_alpha_options', @() s3_alpha_options());
u9b_chk('b3_alpha', @() b3_alpha());
u9b_chk('s2_alpha_matrix', @() s2_alpha_matrix());

% --- item 111: a band's values, colour and times
u9b_chk('band_value_column', @() band(@(q) set_and_read(q, 'Value', [5; 6])));
u9b_chk('band_value_back', @() band(@(q) band_value_back(q)));
u9b_chk('band_auto_keeps', @() band(@(q) band_auto_keeps(q)));
u9b_chk('band_auto_series', @() band(@(q) band_auto_series(q)));
u9b_chk('band_series_zero', @() band(@(q) band_series_zero(q)));
u9b_chk('band_datetime', @() band_times(1));
u9b_chk('band_duration', @() band_times(2));
u9b_chk('band_datetime_format', @() band_times(3));
u9b_chk('band_duration_format', @() band_times(4));
u9b_chk('band_datetime_hours', @() band_times(5));
u9b_chk('band_datetime_pairs', @() band_times(6));
u9b_chk('band_datetime_on_dates', @() band_times(7));
u9b_chk('band_numbers_on_dates', @() band_times(8));
u9b_chk('band_dates_on_numbers', @() band_times(9));
u9b_chk('band_dates_on_spans', @() band_times(10));
u9b_chk('band_spans_on_dates', @() band_times(11));
u9b_chk('band_dates_and_number', @() band_times(12));
u9b_chk('band_set_numbers', @() band_times(13));
u9b_chk('band_set_spans', @() band_times(14));
u9b_chk('band_set_dates', @() band_times(15));
u9b_chk('band_dates_column', @() band_times(16));

close all force

function r = first_names(names, n)
r = strjoin(names(1:n)', ',');
end

function a = fresh_axes()
close all force
a = axes(figure('Visible', 'off'));
end

function r = legend_two()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
before = {p.DisplayName, q.DisplayName};
l = legend(a);
r = {before, l.String, p.DisplayName, q.DisplayName, class(l.String)};
end

function r = legend_third()
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
l = legend(a);
q = plot(a, 3:5);
r = {l.String, q.DisplayName};
end

function r = legend_named(which)
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
if which == 1, p.DisplayName = 'n'; else, q.DisplayName = 'n'; end
l = legend(a);
r = l.String;
end

function r = legend_rename(name)
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(a);
p.DisplayName = name;
r = {l.String, p.DisplayName, q.DisplayName};
end

function r = legend_one_name()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(a, 'a');
w = plot(a, 3:5);
r = {l.String, p.DisplayName, q.DisplayName, w.DisplayName};
end

function r = legend_handle()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(q);
r = {l.String, p.DisplayName, q.DisplayName};
end

function r = legend_handles_cell()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend([q p], {'x', 'y'});
r = {l.String, p.DisplayName, q.DisplayName};
end

function r = legend_off()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(a);
legend(a, 'off');
r = {isvalid(l), isempty(a.Legend), p.DisplayName, q.DisplayName};
l2 = legend(a);
r = [r, {isvalid(l2), l2.String}];
end

function r = legend_command(word)
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
legend(a, word);
l = a.Legend;
r = {l.String, char(l.Visible), char(l.Box)};
end

function r = legend_hide()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3);
l = legend(a);
legend(a, 'hide');
r = {isvalid(l), char(l.Visible), p.DisplayName};
legend(a, 'show');
r = [r, {char(l.Visible)}];
legend(a, 'toggle');
r = [r, {char(l.Visible)}];
end

function r = legend_options()
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
l = legend(a, 'a', 'b', 'Location', 'northwest', 'FontSize', 14);
r = {l.String, l.Location, l.FontSize};
l = legend(a, {'c', 'd'}, 'Orientation', 'horizontal');
r = [r, {l.String, l.Orientation}];
end

function r = legend_location_only()
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
l = legend(a, 'Location', 'south');
r = {l.String, l.Location};
end

function r = legend_autoupdate_off()
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
l = legend(a, 'AutoUpdate', 'off');
q = plot(a, 3:5);
r = {l.String, q.DisplayName};
end

function r = legend_more_names()
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
l = legend(a, 'a', 'b', 'c');
r = l.String;
end

function r = legend_string_set(names)
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(a);
l.String = names;
r = {l.String, p.DisplayName, q.DisplayName};
end

function r = legend_kinds()
a = fresh_axes(); hold(a, 'on');
image(a, magic(3));
scatter(a, 1:3, 1:3);
bar(a, 1:3);
xline(a, 2);
text(a, 1, 1, 'a');
l = legend(a);
r = l.String;
end

function r = legend_delete_renumbers()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(a);
delete(p);
w = plot(a, 3:5);
r = {l.String, q.DisplayName, w.DisplayName};
end

function r = legend_replot()
a = fresh_axes();
plot(a, 1:3);
l = legend(a);
q = plot(a, 2:4);
r = {isvalid(l), isempty(a.Legend), q.DisplayName};
l2 = legend(a);
r = [r, {l2.String}];
end

function r = legend_again()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
legend(a);
l = legend(a, 'x');
r = {l.String, p.DisplayName, q.DisplayName};
legend(a, 'a', 'b');
l = legend(a);
r = [r, {l.String}];
end

function r = legend_cla()
a = fresh_axes(); hold(a, 'on');
plot(a, 1:3);
l = legend(a);
cla(a);
r = {isvalid(l), numel(l.String)};
end

function r = legend_plotchildren()
a = fresh_axes(); hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4);
l = legend(q);
one = l.PlotChildren;
l = legend(a);
r = {numel(one), one == q, numel(l.PlotChildren)};
end

function r = legend_none_property()
a = fresh_axes();
r = {isempty(a.Legend)};
plot(a, 1:3);
l = legend(a);
r = [r, {isequal(a.Legend, l)}];
end

function r = children_after(fn)
a = fresh_axes();
plot(a, 1:3);
axes(a);
fn();
r = numel(a.Children);
end

function patch_twice()
patch([0 1 1], [0 0 1], 'r');
patch([0 1 1], [0 0 1], 'g');
end

function r = view_after(fn)
a = fresh_axes();
plot(a, 1:3);
axes(a);
fn();
[az, el] = view(a);
r = {a.View, [az el]};
end

function r = patch_keeps()
a = fresh_axes();
plot(a, 1:3);
title(a, 'T');
xlim(a, [0 9]);
axes(a);
patch([0 1 1], [0 0 1], 'r');
r = {a.Title.String, xlim(a), ishold(a)};
end

function r = view_written()
a = fresh_axes();
plot(a, 1:3);
a.View = [30 40];
[az, el] = view(a);
r = {a.View, [az el]};
view(a, 2);
r = [r, {a.View}];
end

function r = replaced_valid()
a = fresh_axes();
h = plot(a, 1:3);
plot(a, 2:4);
r = {ishghandle(h), isvalid(h), isgraphics(h)};
end

function r = replaced_write(how)
a = fresh_axes();
h = plot(a, 1:3);
plot(a, 2:4);
switch how
    case 1, r = as_statement(@() set_line_width(h));
    case 2, r = as_statement(@() set(h, 'Visible', 'off'));
    case 3, r = as_statement(@() h.Visible);
    case 4, r = as_statement(@() get(h, 'Visible'));
    case 5, r = as_statement(@() delete(h));
end
end

function set_line_width(h)
h.LineWidth = 2;
end

function r = cleared_write()
a = fresh_axes();
h = scatter(a, 1:3, 1:3);
cla(a);
r = as_statement(@() set_line_width(h));
end

function r = replaced_in_list()
a = fresh_axes();
h = plot(a, 1:3);
hold(a, 'on');
g = plot(a, 2:4);
hold(a, 'off');
k = plot(a, 3:5);
r = {isvalid([h g k]), as_statement(@() set([h k], 'LineWidth', 2))};
end

function r = replaced_axes_kept()
a = fresh_axes();
plot(a, 1:3);
plot(a, 2:4);
r = isvalid(a);
end

function r = side_orders()
a = fresh_axes();
co = a.ColorOrder;
yyaxis(a, 'left');
left = a.ColorOrder;
yyaxis(a, 'right');
right = a.ColorOrder;
r = {size(left), same_colour(left, co(1, :)), size(right), same_colour(right, co(2, :)), size(colororder(a))};
end

function r = side_orders_after_plots()
a = fresh_axes();
co = a.ColorOrder;
hold(a, 'on');
plot(a, 1:3); plot(a, 2:4);
yyaxis(a, 'right');
right = a.ColorOrder;
ruler = a.YAxis(2).Color;
yyaxis(a, 'left');
left = a.ColorOrder;
r = {same_colour(right, co(3, :)), same_colour(ruler, co(3, :)), same_colour(left, a.YAxis(1).Color)};
end

function r = side_order_set(property)
a = fresh_axes();
co = a.ColorOrder;
yyaxis(a, 'left'); p = plot(a, 1:3);
yyaxis(a, 'right'); q = plot(a, 1:3);
if property, a.ColorOrder = [1 0 0; 0 1 0]; else, colororder(a, [1 0 0; 0 1 0]); end
r = {same_colour(p.Color, co(1, :)), q.Color, a.ColorOrder, same_colour(a.YAxis(1).Color, co(1, :)), same_colour(a.YAxis(2).Color, co(2, :))};
yyaxis(a, 'left');
r = [r, {same_colour(a.ColorOrder, co(1, :))}];
end

function r = side_order_cycle()
a = fresh_axes();
yyaxis(a, 'right');
a.ColorOrder = [1 0 0; 0 1 0];
hold(a, 'on');
p = plot(a, 1:3); q = plot(a, 2:4); w = plot(a, 3:5);
r = {[p.Color; q.Color; w.Color], {p.LineStyle, q.LineStyle, w.LineStyle}, a.LineStyleOrderIndex, a.ColorOrderIndex};
end

function r = side_order_before()
a = fresh_axes();
colororder(a, [1 0 0; 0 1 0; 0 0 1]);
yyaxis(a, 'left'); p = plot(a, 1:3);
yyaxis(a, 'right'); q = plot(a, 1:3);
r = {p.Color, q.Color, a.ColorOrder};
end

function r = side_order_cla()
a = fresh_axes();
co = a.ColorOrder;
yyaxis(a, 'right');
plot(a, 1:3);
cla(a);
r = same_colour(a.ColorOrder, co(2, :));
cla(a, 'reset');
r = {r, size(a.ColorOrder), size(a.YAxis)};
end

function r = ruler_modes()
a = fresh_axes();
yyaxis(a, 'left');
r = {a.YAxis(1).ColorMode, a.YAxis(2).ColorMode, a.YColorMode};
yyaxis(a, 'right');
r = [r, {a.YColorMode}];
end

function r = ruler_modes_set()
a = fresh_axes();
yyaxis(a, 'left');
a.YColor = 'r';
r = {a.YAxis(1).ColorMode, a.YAxis(2).ColorMode, a.YColorMode};
a.YAxis(2).Color = 'g';
r = [r, {a.YAxis(2).ColorMode}];
end

function r = style_order_fresh()
a = fresh_axes();
r = {class(a.LineStyleOrder), a.LineStyleOrder};
end

function r = style_order_yyaxis()
a = fresh_axes();
yyaxis(a, 'left');
L = a.LineStyleOrder;
r = {class(L), size(L), L(5, :), L(1, :), numel(L)};
end

function r = style_order_written()
a = fresh_axes();
a.LineStyleOrder = '--';
r = {class(a.LineStyleOrder)};
a.LineStyleOrder = ['- '; '--'];
r = [r, {class(a.LineStyleOrder), size(a.LineStyleOrder)}];
a.LineStyleOrder = ["-" "o-"];
r = [r, {class(a.LineStyleOrder), a.LineStyleOrder}];
a.LineStyleOrder = {'--', ':'};
r = [r, {a.LineStyleOrder}];
yyaxis(a, 'left');
r = [r, {a.LineStyleOrder}];
end

function set_yaxis()
a = fresh_axes();
yyaxis(a, 'left');
a.YAxis = a.YAxis;
end

function set_xaxis(other)
a = fresh_axes();
if other, a.XAxis = a.YAxis; else, a.XAxis = a.XAxis; a.ZAxis = a.ZAxis; end
end

function set_type(what)
a = fresh_axes();
switch what
    case 'line', h = plot(a, 1:3); h.Type = 'x';
    case 'text', h = text(a, 1, 1, 'a'); h.Type = 'x';
    case 'axes', a.Type = 'x';
    case 'beingdeleted', h = plot(a, 1:3); h.BeingDeleted = 'on';
end
end

function r = series_index_zero()
a = fresh_axes();
hold(a, 'on');
p = plot(a, 1:3);
p.SeriesIndex = 0;
s = scatter(a, 1:3, 1:3);
s.SeriesIndex = 0;
r = {p.SeriesIndex, s.SeriesIndex};
end

function r = s3_alpha(fn)
a = fresh_axes();
s = scatter3(a, 1:3, 1:3, 1:3);
r = fn(s);
end

function r = set_and_read(h, name, value)
try
    set(h, name, value);
    r = get(h, name);
catch err
    r = [err.identifier ' | ' strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/')];
end
end

function r = s3_alpha_options()
a = fresh_axes();
s = scatter3(a, 1:3, 1:3, 1:3, 'filled', 'AlphaData', [0.1 0.5 1], 'MarkerFaceAlpha', 'flat');
r = {s.AlphaData, s.MarkerFaceAlpha, s.AlphaDataMapping};
end

function r = b3_alpha()
a = fresh_axes();
b = bubblechart3(a, 1:3, 1:3, 1:3, 1:3);
r = {size(b.AlphaData), b.AlphaDataMapping};
b.AlphaData = [0.2 0.4 0.6];
r = [r, {b.AlphaData}];
end

function r = s2_alpha_matrix()
a = fresh_axes();
s = scatter(a, 1:3, 1:3);
r = set_and_read(s, 'AlphaData', [1 2; 3 4]);
end

function r = band(fn)
a = fresh_axes();
q = xregion(a, 1, 2);
r = fn(q);
end

function r = band_value_back(q)
q.Value = [5; 6];
q.Value = [8 7];
r = {q.Value, size(q.Value)};
end

function r = band_auto_keeps(q)
q.FaceColor = 'r';
q.FaceColorMode = 'auto';
r = {q.FaceColor, q.FaceColorMode};
q.SeriesIndex = 'none';
r = [r, {q.FaceColor}];
end

function r = band_auto_series(q)
co = colororder(q.Parent);
q.SeriesIndex = 2;
q.FaceColor = 'r';
q.FaceColorMode = 'auto';
r = same_colour(q.FaceColor, co(2, :));
end

function r = band_series_zero(q)
q.SeriesIndex = 2;
q.SeriesIndex = 0;
r = {q.SeriesIndex, q.FaceColor};
end

function r = band_times(which)
a = fresh_axes();
try
    switch which
        case 1
            q = xregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            r = {class(q.Value), char(q.Value), size(q.Value), class(get(q, 'Value'))};
        case 2
            q = yregion(a, seconds(1), seconds(2));
            r = {class(q.Value), char(q.Value), q.Value.Format};
        case 3
            q = xregion(a, datetime(2020, 1, 2, 'Format', 'yyyy-MM-dd'), datetime(2020, 1, 3));
            r = {q.Value.Format, char(q.Value)};
        case 4
            q = yregion(a, minutes(1), minutes(2));
            r = q.Value.Format;
        case 5
            q = xregion(a, datetime(2020, 1, 2, 6, 0, 0), datetime(2020, 1, 2, 18, 0, 0));
            r = char(q.Value);
        case 6
            q = xregion(a, datetime(2020, 1, [1 5]), datetime(2020, 1, [2 6]));
            r = {size(q), char(q(2).Value)};
        case 7
            plot(a, datetime(2020, 1, 1:5), 1:5);
            hold(a, 'on');
            q = xregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            r = {class(q.Value), numel(a.Children)};
        case 8
            plot(a, datetime(2020, 1, 1:5), 1:5);
            hold(a, 'on');
            q = xregion(a, 1, 2);
            r = q.Value;
        case 9
            plot(a, 1:5);
            hold(a, 'on');
            xregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            r = 'ran';
        case 10
            yregion(a, seconds(1), seconds(2));
            hold(a, 'on');
            yregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            r = 'ran';
        case 11
            yregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            hold(a, 'on');
            yregion(a, seconds(1), seconds(2));
            r = 'ran';
        case 12
            xregion(a, datetime(2020, 1, 2), 3);
            r = 'ran';
        case 13
            q = xregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            q.Value = [1 2];
            r = {class(q.Value), q.Value};
        case 14
            q = xregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            q.Value = seconds([1 2]);
            r = {class(q.Value), char(q.Value)};
        case 15
            q = xregion(a, 1, 2);
            q.Value = datetime(2020, 1, [2 3]);
            r = {class(q.Value), char(q.Value)};
        case 16
            q = xregion(a, datetime(2020, 1, 2), datetime(2020, 1, 3));
            q.Value = datetime(2020, 1, [4; 5]);
            r = size(q.Value);
    end
catch err
    r = [err.identifier ' | ' strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/')];
end
end

function r = same_colour(a, b)
% Whether two colours are the same to the byte: R2025b's colour order is written in decimals and
% what it inks rulers and yyaxis lines with is rounded to bytes.
r = isequal(size(a), size(b)) && isequal(round(a * 255), round(b * 255));
end

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/')];
end
end
