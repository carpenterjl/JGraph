% composite_write_back.m -- V6 of the value-ownership plan: a write whose path passes through a
% computed level stores back. A graphics property (p.YData(2) = 9, ax.XLim(2) = 5, appendix A #95-#99,
% #132, #133, #150), a datetime component (e.Day(2) = 15, #126, #135), a handle object held in a struct
% or a cell (#137-#140), a value object's property reached through a container, and a dictionary's
% cell value under braces (#136). Each level is get, modify, set: the setter runs once a statement,
% after the subscripts and the right-hand side; a refused write leaves the holder as it was.

run_case('g_line_ydata_indexed_write', @g_line_ydata_indexed_write);
run_case('g_line_ydata_two_subscripts', @g_line_ydata_two_subscripts);
run_case('g_line_ydata_range_write', @g_line_ydata_range_write);
run_case('g_line_ydata_mask_write', @g_line_ydata_mask_write);
run_case('g_line_ydata_end_write', @g_line_ydata_end_write);
run_case('g_line_ydata_read_modify_write', @g_line_ydata_read_modify_write);
run_case('g_line_ydata_growth', @g_line_ydata_growth);
run_case('g_line_ydata_delete', @g_line_ydata_delete);
run_case('g_line_manual_x_growth', @g_line_manual_x_growth, 'div=ADR0167');
run_case('g_line_ydata_self_overlap', @g_line_ydata_self_overlap);
run_case('g_line_ydata_through_callee', @g_line_ydata_through_callee);
run_case('g_line_ydata_cell_held', @g_line_ydata_cell_held);
run_case('g_line_ydata_struct_held', @g_line_ydata_struct_held);
run_case('g_line_ydata_children_elem', @g_line_ydata_children_elem);
run_case('g_line_ydata_handle_array_elem', @g_line_ydata_handle_array_elem);
run_case('g_axes_xlim_indexed', @g_axes_xlim_indexed);
run_case('g_figure_position_indexed', @g_figure_position_indexed);
run_case('g_line_color_indexed', @g_line_color_indexed);
run_case('g_line_linewidth_indexed', @g_line_linewidth_indexed);
run_case('g_line_ydata_refused_atomic', @g_line_ydata_refused_atomic);
run_case('g_line_ydata_global', @g_line_ydata_global);
run_case('g_line_ydata_order_end_vs_rhs', @g_line_ydata_order_end_vs_rhs);
run_case('g_line_ydata_subscript_once', @g_line_ydata_subscript_once);
run_case('g_line_ydata_read_before_write', @g_line_ydata_read_before_write);
run_case('g_text_string_indexed', @g_text_string_indexed);
run_case('g_line_ydata_dynamic_prop', @g_line_ydata_dynamic_prop);
run_case('g_image_cdata_indexed', @g_image_cdata_indexed);
run_case('d_day_indexed_write', @d_day_indexed_write);
run_case('d_year_whole_write', @d_year_whole_write);
run_case('d_year_each_element', @d_year_each_element);
run_case('d_month_rollover', @d_month_rollover);
run_case('d_hour_minute_second', @d_hour_minute_second);
run_case('d_second_fraction', @d_second_fraction);
run_case('d_component_read_after_write', @d_component_read_after_write);
run_case('d_component_count_mismatch', @d_component_count_mismatch);
run_case('d_component_growth', @d_component_growth);
run_case('d_struct_held_component', @d_struct_held_component);
run_case('d_cell_held_format', @d_cell_held_format);
run_case('d_cell_held_day_indexed', @d_cell_held_day_indexed);
run_case('d_format_alias', @d_format_alias);
run_case('d_component_order', @d_component_order);
run_case('d_nat_stays_nat', @d_nat_stays_nat);
run_case('d_zoned_hour', @d_zoned_hour);
run_case('d_duration_component_refused', @d_duration_component_refused);
run_case('o_struct_held_handle_nested_write', @o_struct_held_handle_nested_write);
run_case('o_struct_copy_keeps_handle_class', @o_struct_copy_keeps_handle_class);
run_case('o_struct_copy_whole_prop_write', @o_struct_copy_whole_prop_write);
run_case('o_cell_held_handle_nested_write', @o_cell_held_handle_nested_write);
run_case('o_struct_held_method_call', @o_struct_held_method_call);
run_case('o_handle_eq_identity', @o_handle_eq_identity);
run_case('o_value_in_cell_prop_write', @o_value_in_cell_prop_write);
run_case('o_value_in_cell_prop_indexed', @o_value_in_cell_prop_indexed);
run_case('o_value_in_struct_prop_write', @o_value_in_struct_prop_write);
run_case('o_value_prop_struct_field_write', @o_value_prop_struct_field_write);
run_case('o_value_prop_structarr_grow', @o_value_prop_structarr_grow);
run_case('o_handle_in_cell_in_struct', @o_handle_in_cell_in_struct);
run_case('o_handle_prop_holding_handle', @o_handle_prop_holding_handle);
run_case('k_dict_brace_cell_read', @k_dict_brace_cell_read);
run_case('k_dict_brace_cell_write_alias', @k_dict_brace_cell_write_alias);
run_case('k_dict_paren_read_class', @k_dict_paren_read_class);
run_case('k_dict_brace_noncell_refused', @k_dict_brace_noncell_refused);
run_case('k_dict_brace_whole_write', @k_dict_brace_whole_write);
run_case('k_dict_two_keys_cell_values', @k_dict_two_keys_cell_values);

function run_case(name, fn, rule)
% The rule is exact unless a case names its accepted divergence: g_line_manual_x_growth (a series
% here is one pair of equal length, so a YData longer than a chosen XData is refused; ADR 0167).
if nargin < 3
    rule = 'exact';
end
try
    fprintf('CHK|%s|%s|%s\n', name, clean(fn()), rule);
catch err
    fprintf('CHK|%s|ERR %s|%s\n', name, clean(err.message), rule);
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

function s = logged(v)
global vlog_text
if isnumeric(v) || islogical(v)
    v = mat2str(v);
end
s = sprintf('%s / %s', vlog_text, v);
end

function d = three_days()
d = datetime(2020, 1, 1) + days(0:2);
end

function z = bump_prop(p)
p.YData(1) = 7;
z = 0;
end

function k = idx_c()
vlog('idx');
k = 2;
end

function v = rhs_c()
vlog('rhs');
v = 9;
end

function k = bump_k()
global cw_bumps
cw_bumps = cw_bumps + 1;
k = 2;
end

% --- graphics properties ---------------------------------------------------------------------------

function s = g_line_ydata_indexed_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(2) = 9;
s = sprintf('%s %s', mat2str(p.YData), mat2str(get(p, 'YData')));
close(f);
end

function s = g_line_ydata_two_subscripts()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(1, 3) = 7;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_range_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(1:2) = [8 9];
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_mask_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(logical([1 0 1])) = 0;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_end_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(end) = 5;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_read_modify_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(2) = p.YData(2) + 10;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_growth()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(end + 1) = 4;
s = sprintf('%s %s', mat2str(p.YData), mat2str(p.XData));
close(f);
end

function s = g_line_ydata_delete()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(2) = [];
s = sprintf('%s %s', mat2str(p.YData), mat2str(p.XData));
close(f);
end

function s = g_line_manual_x_growth()
f = figure('Visible', 'off');
p = plot([10 20 30], [1 2 3]);
w = warning('off', 'all');
p.YData(end + 1) = 4;
warning(w);
s = sprintf('%s %s', mat2str(p.YData), mat2str(p.XData));
close(f);
end

function s = g_line_ydata_self_overlap()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData([2 3 1]) = p.YData;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_through_callee()
f = figure('Visible', 'off');
p = plot([1 2 3]);
r = p.YData + bump_prop(p);
s = sprintf('%s %s', mat2str(r), mat2str(p.YData));
close(f);
end

function s = g_line_ydata_cell_held()
f = figure('Visible', 'off');
p = plot([1 2 3]);
c = {p};
c{1}.YData(1) = 0;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_struct_held()
f = figure('Visible', 'off');
p = plot([1 2 3]);
st.h = p;
st.h.YData(1) = 0;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_children_elem()
f = figure('Visible', 'off');
p = plot([1 2 3]);
ax = gca;
ch = ax.Children;
ch(1).YData(1) = 42;
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_handle_array_elem()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
h = [p1 p2];
h(2).YData(3) = 60;
s = sprintf('%s %s', mat2str(p1.YData), mat2str(p2.YData));
close(f);
end

function s = g_axes_xlim_indexed()
f = figure('Visible', 'off');
plot([1 2 3]);
ax = gca;
ax.XLim = [0 10];
ax.XLim(2) = 5;
s = mat2str(ax.XLim);
close(f);
end

function s = g_figure_position_indexed()
f = figure('Visible', 'off');
f.Position = [10 20 300 200];
f.Position(3) = 321;
s = sprintf('%g', f.Position(3));
close(f);
end

function s = g_line_color_indexed()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.Color = [0 0 0];
p.Color(1) = 1;
s = mat2str(p.Color);
close(f);
end

function s = g_line_linewidth_indexed()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.LineWidth(1) = 3;
s = sprintf('%g', p.LineWidth);
close(f);
end

function s = g_line_ydata_refused_atomic()
f = figure('Visible', 'off');
p = plot([1 2 3]);
try
    p.YData(4:5) = [7 8 9];
catch
end
s = mat2str(p.YData);
close(f);
end

function s = g_line_ydata_global()
global gp_cw
f = figure('Visible', 'off');
gp_cw = plot([1 2 3]);
write_global_elem();
s = mat2str(gp_cw.YData);
close(f);
end

function write_global_elem()
global gp_cw
gp_cw.YData(2) = 7;
end

function v = grow_global()
global gp_cw_order
vlog('rhs');
gp_cw_order.YData = [1 2 3 50];
v = 9;
end

function s = g_line_ydata_order_end_vs_rhs()
global gp_cw_order vlog_text
vlog_text = '';
f = figure('Visible', 'off');
gp_cw_order = plot([1 2 3]);
gp_cw_order.YData(end) = grow_global();
s = logged(gp_cw_order.YData);
close(f);
end

function s = g_line_ydata_subscript_once()
global cw_bumps
cw_bumps = 0;
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(bump_k()) = 9;
s = sprintf('%d %s', cw_bumps, mat2str(p.YData));
close(f);
end

function s = g_line_ydata_read_before_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
y = p.YData;
p.YData(2) = 9;
s = sprintf('%s %s', mat2str(y), mat2str(p.YData));
close(f);
end

function s = g_text_string_indexed()
f = figure('Visible', 'off');
t = text(0, 0, 'ab');
t.String(1) = 'z';
s = t.String;
close(f);
end

function s = g_line_ydata_dynamic_prop()
f = figure('Visible', 'off');
p = plot([1 2 3]);
nm = 'YData';
p.(nm)(2) = 9;
s = mat2str(p.YData);
close(f);
end

function s = g_image_cdata_indexed()
f = figure('Visible', 'off');
im = imagesc(magic(3));
im.CData(2, 2) = 0;
s = sprintf('%d %s', im.CData(2, 2), mat2str(size(im.CData)));
close(f);
end

% --- datetime components -----------------------------------------------------------------------------

function s = d_day_indexed_write()
d = three_days();
e = d;
e.Day(2) = 15;
s = sprintf('%d %d %d', day(d(2)), day(e(2)), day(e(1)));
end

function s = d_year_whole_write()
d = three_days();
e = d;
e.Year = 2021;
s = sprintf('%d %d', year(d(1)), year(e(1)));
end

function s = d_year_each_element()
e = three_days();
e.Year = [2021 2022 2023];
s = mat2str(year(e));
end

function s = d_month_rollover()
e = three_days();
e.Month(1) = 13;
s = sprintf('%d %d', year(e(1)), month(e(1)));
end

function s = d_hour_minute_second()
e = three_days();
e.Hour(2) = 5;
e.Minute(2) = 30;
e.Second(2) = 15.5;
s = sprintf('%g %g %g', hour(e(2)), minute(e(2)), second(e(2)));
end

function s = d_second_fraction()
e = three_days();
e.Second(1) = 12.25;
s = sprintf('%g %d', second(e(1)), day(e(1)));
end

function s = d_component_read_after_write()
e = three_days();
e.Day(2) = 15;
s = sprintf('%s %s', mat2str(e.Day), class(e.Day));
end

function s = d_component_count_mismatch()
e = three_days();
e.Day = [1 2];
s = mat2str(day(e));
end

function s = d_component_growth()
e = three_days();
e.Day(4) = 1;
s = sprintf('%d %s', numel(e), mat2str(day(e)));
end

function s = d_struct_held_component()
d = three_days();
st.t = d;
st.t.Year = 1999;
s = sprintf('%d %d %s', year(d(1)), year(st.t(1)), class(st.t));
end

function s = d_cell_held_format()
c = {datetime(2020, 1, 1)};
d = c;
d{1}.Format = 'yyyy';
s = sprintf('%s/%s', c{1}.Format, d{1}.Format);
end

function s = d_cell_held_day_indexed()
d = three_days();
c = {d};
c{1}.Day(2) = 15;
s = sprintf('%d %d', day(c{1}(2)), day(d(2)));
end

function s = d_format_alias()
d = three_days();
e = d;
e.Format = 'yyyy';
s = sprintf('%s %s', d.Format, e.Format);
end

function s = d_component_order()
global vlog_text
vlog_text = '';
e = three_days();
e.Day(idx_c()) = rhs_c();
s = logged(mat2str(day(e)));
end

function s = d_nat_stays_nat()
e = three_days();
e(2) = NaT;
e.Day = 5;
s = sprintf('%d %d', sum(isnat(e)), day(e(1)));
end

function s = d_zoned_hour()
z = datetime(2020, 1, 1, 'TimeZone', 'UTC');
z.Hour = 5;
s = sprintf('%d %s', hour(z), z.TimeZone);
end

function s = d_duration_component_refused()
h = hours(1);
h.Day = 1;
s = sprintf('%g', hours(h));
end

% --- handle and value objects held in containers --------------------------------------------------------

function s = o_struct_held_handle_nested_write()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.data(2) = 9;
s = mat2str(st.h.data);
end

function s = o_struct_copy_keeps_handle_class()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.data(2) = 9;
s = sprintf('%s %s', class(t.h), class(st.h));
end

function s = o_struct_copy_whole_prop_write()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.data = [4 5 6];
s = mat2str(st.h.data);
end

function s = o_cell_held_handle_nested_write()
c = {HandleHolder()};
c{1}.data = [1 2 3];
d = c;
d{1}.data(2) = 9;
s = mat2str(c{1}.data);
end

function s = o_struct_held_method_call()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.bump();
s = mat2str(st.h.data);
end

function s = o_handle_eq_identity()
st.h = HandleHolder();
t = st;
s = sprintf('%d %d %d', t.h == st.h, HandleHolder() == HandleHolder(), t.h ~= st.h);
end

function s = o_value_in_cell_prop_write()
v = ValueBox();
v.p = 1:5;
c = {v};
c{1}.p = 9;
s = sprintf('%s %d', mat2str(v.p), c{1}.p);
end

function s = o_value_in_cell_prop_indexed()
v = ValueBox();
v.p = 1:5;
c = {v};
c{1}.p(2) = 9;
s = sprintf('%s %s', mat2str(v.p), mat2str(c{1}.p));
end

function s = o_value_in_struct_prop_write()
v = ValueBox();
v.p = 1:5;
st.f = v;
st.f.p = 9;
s = sprintf('%s %d %s', mat2str(v.p), st.f.p, class(st.f));
end

function s = o_value_prop_struct_field_write()
o = ValueBox();
o.p = struct('f', 1:5);
o.p.f = 9;
o.p.g = 1;
s = sprintf('%d %s', o.p.f, strjoin(fieldnames(o.p)', ','));
end

function s = o_value_prop_structarr_grow()
o = ValueBox();
o.p = struct('f', num2cell(1:5));
o.p(end + 1).f = 9;
s = sprintf('%d %d', numel(o.p), o.p(6).f);
end

function s = o_handle_in_cell_in_struct()
st.c = {HandleHolder()};
st.c{1}.data = [1 2 3];
t = st;
t.c{1}.data(2) = 9;
s = mat2str(st.c{1}.data);
end

function s = o_handle_prop_holding_handle()
a = HandleHolder();
b = HandleHolder();
a.data = b;
a.data.data = [1 2];
a.data.data(2) = 7;
s = mat2str(b.data);
end

% --- a dictionary's cell values under braces ---------------------------------------------------------------

function s = k_dict_brace_cell_read()
d = dictionary("k", {[1 2]});
s = mat2str(d{"k"});
end

function s = k_dict_brace_cell_write_alias()
d = dictionary("k", {[1 2]});
e = d;
e{"k"}(1) = 9;
s = sprintf('%s %s', mat2str(d{"k"}), mat2str(e{"k"}));
end

function s = k_dict_paren_read_class()
d = dictionary("k", {[1 2]});
v = d("k");
s = sprintf('%s %s', class(v), mat2str(size(v)));
end

function s = k_dict_brace_noncell_refused()
d = dictionary("k", 5);
s = mat2str(d{"k"});
end

function s = k_dict_brace_whole_write()
d = dictionary("k", {[1 2]});
d{"k"} = [7 8 9];
s = sprintf('%s %s', mat2str(d{"k"}), class(d("k")));
end

function s = k_dict_two_keys_cell_values()
d = dictionary(["a" "b"], {1, [2 3]});
s = sprintf('%d %s', d{"a"}, mat2str(d{"b"}));
end
