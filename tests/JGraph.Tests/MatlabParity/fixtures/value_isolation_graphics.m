% value_isolation_graphics.m -- appendix A of the value-ownership plan: values that pass through
% graphics objects. An indexed write through a property (silently dropped today, V6), a property
% read then written, appdata and a stored callback retaining the caller's value (V2), guidata,
% axes(parent), isvalid, growth and deletion through a property, a comma-list write to a handle
% array, a default property value, a handle held in a global (V4), and the write order through a
% property. Every figure is invisible and closed by its case. A case named aNNN is appendix row
% NNN; g_ cases agree on both engines today.

try
    set(groot, 'DefaultFigureVisible', 'off');
catch
end
run_case('g_gfx_ydata_read_alias', @g_gfx_ydata_read_alias);
run_case('g_gfx_set_then_write_source', @g_gfx_set_then_write_source);
run_case('a095_gfx_indexed_prop_write', @a095_gfx_indexed_prop_write);
run_case('a095_gfx_indexed_write_then_get', @a095_gfx_indexed_write_then_get);
run_case('g_gfx_handle_alias_write', @g_gfx_handle_alias_write);
run_case('a097_gfx_cell_handle_nested_write', @a097_gfx_cell_handle_nested_write);
run_case('g_gfx_userdata_alias', @g_gfx_userdata_alias);
run_case('g_gfx_userdata_struct_write', @g_gfx_userdata_struct_write);
run_case('a100_gfx_appdata_alias', @a100_gfx_appdata_alias);
run_case('a102_gfx_guidata_alias', @a102_gfx_guidata_alias);
run_case('a103_gfx_xlim_alias', @a103_gfx_xlim_alias);
run_case('a101_gfx_callback_capture', @a101_gfx_callback_capture);
run_case('a104_gfx_delete_alias_isvalid', @a104_gfx_delete_alias_isvalid);
run_case('a096_gfx_operand_scope_prop_write', @a096_gfx_operand_scope_prop_write);
run_case('g_gfx_text_string_alias', @g_gfx_text_string_alias);
run_case('a098_gfx_children_handle_write', @a098_gfx_children_handle_write);
run_case('a099_gfx_prop_self_overlap', @a099_gfx_prop_self_overlap);
run_case('g_gfx_refused_prop_write_atomic', @g_gfx_refused_prop_write_atomic);
run_case('g_gfx_whole_prop_rebind_after_read', @g_gfx_whole_prop_rebind_after_read);
run_case('g_gfx_set_indexed_via_temp', @g_gfx_set_indexed_via_temp);
run_case('a132_axes_xlim_indexed_write', @a132_axes_xlim_indexed_write);
run_case('a132_figure_position_indexed_write', @a132_figure_position_indexed_write);
run_case('a141_handle_array_element_prop_write', @a141_handle_array_element_prop_write, 'div=ADR0167');
run_case('a134_handle_array_cslist_prop_write', @a134_handle_array_cslist_prop_write);
run_case('g_multi_handle_set', @g_multi_handle_set);
run_case('a133_graphics_prop_growth_write', @a133_graphics_prop_growth_write);
run_case('a133_graphics_prop_delete_write', @a133_graphics_prop_delete_write);
run_case('a141_line_width_default', @a141_line_width_default, 'div=ADR0167');
run_case('a150_global_handle_prop_write', @a150_global_handle_prop_write);
run_case('a153_order_graphics_ydata_end_vs_rhs', @a153_order_graphics_ydata_end_vs_rhs);
close all

function run_case(name, fn, rule)
% The rule is exact unless a case names its accepted divergence: the two a141 cases (R2025b's
% default LineWidth is 0.5 points; JGraph draws 1.5 by design; ADR 0167).
if nargin < 3
    rule = 'exact';
end
global vlog_text
vlog_text = '';
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

% --- property reads, writes and aliases (borrow_probe19.m, borrow_probe25.m) ---------------------

function s = g_gfx_ydata_read_alias()
f = figure('Visible', 'off');
p = plot([1 2 3]);
y = p.YData;
y(1) = 7;
s = mat2str(p.YData);
close(f);
end

function s = g_gfx_set_then_write_source()
f = figure('Visible', 'off');
v = [1 2 3];
p = plot(v);
set(p, 'YData', v);
v(1) = 7;
s = mat2str(get(p, 'YData'));
close(f);
end

function s = a095_gfx_indexed_prop_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(2) = 9;
s = mat2str(p.YData);
close(f);
end

function s = a095_gfx_indexed_write_then_get()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData(2) = 9;
s = sprintf('%s %s', mat2str(p.YData), mat2str(get(p, 'YData')));
close(f);
end

function s = g_gfx_handle_alias_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
g = p;
g.YData = [4 5 6];
s = mat2str(p.YData);
close(f);
end

function s = a097_gfx_cell_handle_nested_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
c = {p};
c{1}.YData(1) = 0;
s = mat2str(p.YData);
close(f);
end

function s = g_gfx_userdata_alias()
f = figure('Visible', 'off');
v = [1 2 3];
f.UserData = v;
v(1) = 7;
u = f.UserData;
u(2) = 8;
s = sprintf('%s %s', mat2str(f.UserData), mat2str(v));
close(f);
end

function s = g_gfx_userdata_struct_write()
f = figure('Visible', 'off');
f.UserData = struct('a', [1 2]);
f.UserData.a(2) = 5;
s = mat2str(f.UserData.a);
close(f);
end

function s = a100_gfx_appdata_alias()
f = figure('Visible', 'off');
v = [1 2 3];
setappdata(f, 'k', v);
v(1) = 7;
w = getappdata(f, 'k');
w(2) = 8;
s = mat2str(getappdata(f, 'k'));
close(f);
end

function s = a102_gfx_guidata_alias()
f = figure('Visible', 'off');
g.a = 1;
guidata(f, g);
g.a = 2;
t = guidata(f);
t.a = 3;
u = guidata(f);
s = sprintf('%d', u.a);
close(f);
end

function s = a103_gfx_xlim_alias()
f = figure('Visible', 'off');
ax = axes(f);
lim = [0 10];
ax.XLim = lim;
lim(1) = -5;
s = mat2str(ax.XLim);
close(f);
end

function s = a101_gfx_callback_capture()
f = figure('Visible', 'off');
p = plot([1 2 3]);
v = [1 2 3];
p.ButtonDownFcn = @(~, ~) v;
v(1) = 7;
fn = p.ButtonDownFcn;
s = mat2str(fn([], []));
close(f);
end

function s = a104_gfx_delete_alias_isvalid()
f = figure('Visible', 'off');
q = plot(1:2);
r = q;
delete(q);
s = sprintf('%d %d', isvalid(r), isvalid(q));
close(f);
end

function z = bump_prop(p)
p.YData(1) = 7;
z = 0;
end

function s = a096_gfx_operand_scope_prop_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
r = p.YData + bump_prop(p);
s = sprintf('%s %s', mat2str(r), mat2str(p.YData));
close(f);
end

function s = g_gfx_text_string_alias()
f = figure('Visible', 'off');
t = text(0, 0, 'ab');
w = t.String;
w(1) = 'z';
s = sprintf('%s %s', t.String, w);
close(f);
end

function s = a098_gfx_children_handle_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
ax = gca;
ch = ax.Children;
ch(1).YData(1) = 42;
s = mat2str(p.YData);
close(f);
end

function s = a099_gfx_prop_self_overlap()
f = figure('Visible', 'off');
p = plot([1 2 3]);
p.YData([2 3 1]) = p.YData;
s = mat2str(p.YData);
close(f);
end

function s = g_gfx_refused_prop_write_atomic()
f = figure('Visible', 'off');
p = plot([1 2 3]);
try
    p.YData(4:5) = [7 8 9];
catch
end
s = mat2str(p.YData);
close(f);
end

function s = g_gfx_whole_prop_rebind_after_read()
f = figure('Visible', 'off');
p = plot([1 2 3]);
y = p.YData;
p.YData = [4 5 6];
s = sprintf('%s %s', mat2str(y), mat2str(p.YData));
close(f);
end

function s = g_gfx_set_indexed_via_temp()
f = figure('Visible', 'off');
p = plot([1 2 3]);
y = p.YData;
y(2) = 9;
p.YData = y;
s = mat2str(p.YData);
close(f);
end

% --- axes and figure properties, handle arrays, growth and deletion (borrow_probe26.m) -----------

function s = a132_axes_xlim_indexed_write()
f = figure('Visible', 'off');
plot([1 2 3]);
ax = gca;
ax.XLim = [0 10];
ax.XLim(2) = 5;
s = mat2str(ax.XLim);
close(f);
end

function s = a132_figure_position_indexed_write()
f = figure('Visible', 'off');
f.Position = [10 20 300 200];
f.Position(3) = 321;
s = sprintf('%g', f.Position(3));
close(f);
end

% The element write agrees; the line is #141's, because it prints the other line's default width.
function s = a141_handle_array_element_prop_write()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
h = [p1 p2];
h(2).LineWidth = 4;
s = sprintf('%g %g', p1.LineWidth, p2.LineWidth);
close(f);
end

function s = a134_handle_array_cslist_prop_write()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
h = [p1 p2];
[h.LineWidth] = deal(3);
s = sprintf('%g %g', p1.LineWidth, p2.LineWidth);
close(f);
end

function s = g_multi_handle_set()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
set([p1 p2], 'YData', [5 5 5]);
s = sprintf('%s %s', mat2str(p1.YData), mat2str(p2.YData));
close(f);
end

function s = a133_graphics_prop_growth_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
w = warning('off', 'all');
p.YData(end + 1) = 4;
warning(w);
s = mat2str(p.YData);
close(f);
end

function s = a133_graphics_prop_delete_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
w = warning('off', 'all');
p.YData(2) = [];
warning(w);
s = mat2str(p.YData);
close(f);
end

function s = a141_line_width_default()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
s = sprintf('%g', p1.LineWidth);
close(f);
end

% --- a handle held in a global; the write order through a property (borrow_probe29.m, #150) ------

function write_gp()
global gp_gfx
gp_gfx.YData = [1 2 3 50];
end

function s = a150_global_handle_prop_write()
global gp_gfx
f = figure('Visible', 'off');
gp_gfx = plot([1 2 3]);
write_gp();
s = mat2str(gp_gfx.YData);
close(f);
end

function v = growp_g()
global gp_order
vlog('rhs');
gp_order.YData = [1 2 3 50];
v = 9;
end

function s = a153_order_graphics_ydata_end_vs_rhs()
global gp_order
f = figure('Visible', 'off');
gp_order = plot([1 2 3]);
gp_order.YData(end) = growp_g();
s = logged(gp_order.YData);
close(f);
end
