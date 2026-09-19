% value_isolation_objscope.m -- V4 of the value-ownership plan: a dotted write onto an object the
% frame reaches through something other than a local -- a global, a nested function's parent
% workspace, a persistent -- for a value class, a handle class and a graphics handle. Appendix A
% #16 and #150 are the two seeds (value_isolation_scope, value_isolation_graphics); this is the
% matrix around them. Every figure is invisible and closed by its case. g_ cases agree on both
% engines today.

run_case('v_global_prop_write', @v_global_prop_write);
run_case('v_global_prop_write_other_frame', @v_global_prop_write_other_frame);
run_case('v_global_prop_write_alias_isolated', @v_global_prop_write_alias_isolated);
run_case('v_global_prop_write_local_copy', @v_global_prop_write_local_copy);
run_case('v_global_prop_dynamic_name', @v_global_prop_dynamic_name);
run_case('v_global_prop_elem_write', @v_global_prop_elem_write);
run_case('v_global_prop_elem_grow', @v_global_prop_elem_grow);
run_case('v_global_prop_elem_delete', @v_global_prop_elem_delete);
run_case('v_global_prop_elem_alias_isolated', @v_global_prop_elem_alias_isolated);
run_case('v_global_nested_object_write', @v_global_nested_object_write);
run_case('v_global_nested_object_alias_isolated', @v_global_nested_object_alias_isolated);
run_case('v_global_prop_struct_field_write', @v_global_prop_struct_field_write);
run_case('v_global_prop_cell_slot_write', @v_global_prop_cell_slot_write);
run_case('v_global_unknown_prop_refused', @v_global_unknown_prop_refused);
run_case('v_global_write_in_script_scope', @v_global_write_in_script_scope);
run_case('v_global_prop_write_via_anonymous_reader', @v_global_prop_write_via_anonymous_reader);
run_case('v_nested_prop_write', @v_nested_prop_write);
run_case('v_nested_prop_elem_write', @v_nested_prop_elem_write);
run_case('v_nested_prop_alias_isolated', @v_nested_prop_alias_isolated);
run_case('v_nested_two_deep_prop_write', @v_nested_two_deep_prop_write);
run_case('v_persistent_prop_write', @v_persistent_prop_write);
run_case('v_persistent_prop_elem_write', @v_persistent_prop_elem_write);
run_case('v_persistent_returned_copy_isolated', @v_persistent_returned_copy_isolated);
run_case('h_global_prop_write', @h_global_prop_write);
run_case('h_global_prop_write_other_frame', @h_global_prop_write_other_frame);
run_case('h_global_prop_write_alias_shared', @h_global_prop_write_alias_shared);
run_case('h_global_prop_elem_write', @h_global_prop_elem_write);
run_case('h_global_prop_elem_grow', @h_global_prop_elem_grow);
run_case('h_global_prop_read_then_write', @h_global_prop_read_then_write);
run_case('h_global_holds_value_object_write', @h_global_holds_value_object_write);
run_case('h_global_method_write', @h_global_method_write);
run_case('v_global_holds_handle_write', @v_global_holds_handle_write);
run_case('h_nested_prop_write', @h_nested_prop_write);
run_case('h_nested_prop_elem_write', @h_nested_prop_elem_write);
run_case('h_persistent_prop_write', @h_persistent_prop_write);
run_case('x_global_gfx_prop_write', @x_global_gfx_prop_write);
run_case('x_global_gfx_prop_write_same_frame', @x_global_gfx_prop_write_same_frame);
run_case('x_global_gfx_prop_elem_write', @x_global_gfx_prop_elem_write);
run_case('x_global_gfx_handle_array_elem_write', @x_global_gfx_handle_array_elem_write);
run_case('x_global_gfx_chain_write', @x_global_gfx_chain_write);
run_case('x_global_gfx_read_then_write', @x_global_gfx_read_then_write);
run_case('x_nested_gfx_prop_write', @x_nested_gfx_prop_write);
run_case('x_persistent_gfx_prop_write', @x_persistent_gfx_prop_write);
close all

function run_case(name, fn)
try
    fprintf('CHK|%s|%s|exact\n', name, clean(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\n', name, clean(err.message));
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

% --- a value object held in a global ----------------------------------------------------------------

function write_os_p(v)
global gO_os
gO_os.p = v;
end

function s = v_global_prop_write()
global gO_os
gO_os = ValueBox();
gO_os.p = 7;
s = num2str(gO_os.p);
end

function s = v_global_prop_write_other_frame()
global gO_os
gO_os = ValueBox();
write_os_p(7);
s = num2str(gO_os.p);
end

function s = v_global_prop_write_alias_isolated()
global gO_os
gO_os = ValueBox();
w = gO_os;
write_os_p(7);
s = sprintf('%d %d', w.p, gO_os.p);
end

function s = v_global_prop_write_local_copy()
global gO_os
gO_os = ValueBox();
w = gO_os;
w.p = 7;
s = sprintf('%d %d', w.p, gO_os.p);
end

function s = v_global_prop_dynamic_name()
global gO_os
gO_os = ValueBox();
name = 'p';
gO_os.(name) = 7;
s = num2str(gO_os.p);
end

function s = v_global_prop_elem_write()
global gO_os
gO_os = ValueBox();
gO_os.p = ones(1, 3);
gO_os.p(2) = 7;
s = mat2str(gO_os.p);
end

function s = v_global_prop_elem_grow()
global gO_os
gO_os = ValueBox();
gO_os.p = ones(1, 3);
gO_os.p(5) = 9;
s = mat2str(gO_os.p);
end

function s = v_global_prop_elem_delete()
global gO_os
gO_os = ValueBox();
gO_os.p = [1 2 3 4];
gO_os.p(2) = [];
s = mat2str(gO_os.p);
end

function s = v_global_prop_elem_alias_isolated()
global gO_os
gO_os = ValueBox();
gO_os.p = ones(1, 3);
w = gO_os;
x = gO_os.p;
gO_os.p(2) = 7;
s = sprintf('%s %s %s', mat2str(w.p), mat2str(x), mat2str(gO_os.p));
end

function s = v_global_nested_object_write()
global gO_os
gO_os = ValueBox();
gO_os.p = ValueBox();
gO_os.p.p = 7;
s = num2str(gO_os.p.p);
end

function s = v_global_nested_object_alias_isolated()
global gO_os
gO_os = ValueBox();
gO_os.p = ValueBox();
w = gO_os;
inner = gO_os.p;
gO_os.p.p = 7;
s = sprintf('%d %d %d', w.p.p, inner.p, gO_os.p.p);
end

function s = v_global_prop_struct_field_write()
global gO_os
gO_os = ValueBox();
gO_os.p = struct('f', 1);
gO_os.p.f = 7;
gO_os.p.g = 8;
s = sprintf('%d %d', gO_os.p.f, gO_os.p.g);
end

function s = v_global_prop_cell_slot_write()
global gO_os
gO_os = ValueBox();
gO_os.p = {1, 2};
gO_os.p{2} = 7;
s = sprintf('%d %d', gO_os.p{1}, gO_os.p{2});
end

function s = v_global_unknown_prop_refused()
global gO_os
gO_os = ValueBox();
try
    gO_os.nosuch = 7;
    s = 'assigned';
catch
    s = sprintf('refused %d', gO_os.p);
end
end

function s = v_global_write_in_script_scope()
global gO_os
gO_os = ValueBox();
eval('global gO_os; gO_os.p = 7;');
s = num2str(gO_os.p);
end

function s = v_global_prop_write_via_anonymous_reader()
global gO_os
gO_os = ValueBox();
held = gO_os;
reader = @() held.p;
write_os_p(7);
s = sprintf('%d %d', reader(), gO_os.p);
end

% --- a value object in a nested function's parent workspace -------------------------------------------

function s = v_nested_prop_write()
v = ValueBox();
bump();
s = num2str(v.p);
    function bump()
        v.p = 7;
    end
end

function s = v_nested_prop_elem_write()
v = ValueBox();
v.p = ones(1, 3);
bump();
s = mat2str(v.p);
    function bump()
        v.p(2) = 7;
    end
end

function s = v_nested_prop_alias_isolated()
v = ValueBox();
w = v;
bump();
s = sprintf('%d %d', w.p, v.p);
    function bump()
        v.p = 7;
    end
end

function s = v_nested_two_deep_prop_write()
v = ValueBox();
outer();
s = num2str(v.p);
    function outer()
        inner();
        function inner()
            v.p = 7;
        end
    end
end

% --- a value object held in a persistent ------------------------------------------------------------

function out = persistent_value(mode)
persistent pv
if isempty(pv)
    pv = ValueBox();
end
switch mode
    case 'bump'
        pv.p = pv.p + 1;
    case 'elems'
        pv.p = ones(1, 3);
        pv.p(2) = 7;
end
out = pv;
end

function s = v_persistent_prop_write()
persistent_value('bump');
persistent_value('bump');
o = persistent_value('bump');
s = num2str(o.p);
end

function s = v_persistent_prop_elem_write()
o = persistent_value('elems');
s = mat2str(o.p);
end

function s = v_persistent_returned_copy_isolated()
o = persistent_value('elems');
o.p(1) = 50;
again = persistent_value('read');
s = sprintf('%s %s', mat2str(o.p), mat2str(again.p));
end

% --- a handle object held in a global ---------------------------------------------------------------

function write_hs_data(v)
global gH_os
gH_os.data = v;
end

function s = h_global_prop_write()
global gH_os
gH_os = HandleHolder();
gH_os.data = 7;
s = num2str(gH_os.data);
end

function s = h_global_prop_write_other_frame()
global gH_os
gH_os = HandleHolder();
write_hs_data(7);
s = num2str(gH_os.data);
end

function s = h_global_prop_write_alias_shared()
global gH_os
gH_os = HandleHolder();
gH_os.data = 1;
w = gH_os;
write_hs_data(7);
s = sprintf('%d %d', w.data, gH_os.data);
end

function s = h_global_prop_elem_write()
global gH_os
gH_os = HandleHolder();
gH_os.data = ones(1, 3);
gH_os.data(2) = 7;
s = mat2str(gH_os.data);
end

function s = h_global_prop_elem_grow()
global gH_os
gH_os = HandleHolder();
gH_os.data = ones(1, 3);
gH_os.data(5) = 9;
s = mat2str(gH_os.data);
end

function s = h_global_prop_read_then_write()
global gH_os
gH_os = HandleHolder();
gH_os.data = ones(1, 3);
x = gH_os.data;
gH_os.data(2) = 7;
s = sprintf('%s %s', mat2str(x), mat2str(gH_os.data));
end

function s = h_global_holds_value_object_write()
global gH_os
gH_os = HandleHolder();
gH_os.data = ValueBox();
v = gH_os.data;
gH_os.data.p = 7;
s = sprintf('%d %d', v.p, gH_os.data.p);
end

function s = h_global_method_write()
global gH_os
gH_os = HandleHolder();
gH_os.data = ones(1, 3);
gH_os.bump();
s = mat2str(gH_os.data);
end

function s = v_global_holds_handle_write()
global gO_os
gO_os = ValueBox();
gO_os.p = HandleHolder();
w = gO_os;
gO_os.p.data = 7;
s = sprintf('%d %d', w.p.data, gO_os.p.data);
end

% --- a handle object in a nested function's parent workspace, and in a persistent --------------------

function s = h_nested_prop_write()
h = HandleHolder();
bump();
s = num2str(h.data);
    function bump()
        h.data = 7;
    end
end

function s = h_nested_prop_elem_write()
h = HandleHolder();
h.data = ones(1, 3);
bump();
s = mat2str(h.data);
    function bump()
        h.data(2) = 7;
    end
end

function out = persistent_handle()
persistent ph
if isempty(ph)
    ph = HandleHolder();
    ph.data = 0;
end
ph.data = ph.data + 1;
out = ph;
end

function s = h_persistent_prop_write()
persistent_handle();
first = persistent_handle();
persistent_handle();
s = num2str(first.data);
end

% --- a graphics handle held in a global, a parent workspace, a persistent ---------------------------

function write_gx_ydata(v)
global gX_os
gX_os.YData = v;
end

function s = x_global_gfx_prop_write()
global gX_os
f = figure('Visible', 'off');
gX_os = plot([1 2 3]);
write_gx_ydata([4 5 6]);
s = mat2str(gX_os.YData);
close(f);
end

function s = x_global_gfx_prop_write_same_frame()
global gX_os
f = figure('Visible', 'off');
gX_os = plot([1 2 3]);
gX_os.LineWidth = 3;
s = num2str(gX_os.LineWidth);
close(f);
end

function s = x_global_gfx_prop_elem_write()
global gX_os
f = figure('Visible', 'off');
gX_os = plot([1 2 3]);
gX_os.YData(2) = 7;
s = mat2str(gX_os.YData);
close(f);
end

function s = x_global_gfx_handle_array_elem_write()
global gX_os
f = figure('Visible', 'off');
gX_os = plot([1 2 3; 4 5 6]);
gX_os(1).LineWidth = 2;
gX_os(2).LineWidth = 3;
s = sprintf('%g %g', gX_os(1).LineWidth, gX_os(2).LineWidth);
close(f);
end

function s = x_global_gfx_chain_write()
global gX_os
f = figure('Visible', 'off');
gX_os = gca;
gX_os.XAxis.Color = [1 0 0];
s = mat2str(gX_os.XAxis.Color);
close(f);
end

function s = x_global_gfx_read_then_write()
global gX_os
f = figure('Visible', 'off');
gX_os = plot([1 2 3]);
y = gX_os.YData;
write_gx_ydata([9 9 9]);
s = sprintf('%s %s', mat2str(y), mat2str(gX_os.YData));
close(f);
end

function s = x_nested_gfx_prop_write()
f = figure('Visible', 'off');
p = plot([1 2 3]);
bump();
s = mat2str(p.YData);
close(f);
    function bump()
        p.YData = [4 5 6];
    end
end

function out = persistent_line()
persistent pl
if isempty(pl)
    pl = plot([1 2 3]);
end
pl.LineWidth = pl.LineWidth + 1;
out = pl.LineWidth;
end

function s = x_persistent_gfx_prop_write()
f = figure('Visible', 'off');
first = persistent_line();
second = persistent_line();
s = num2str(second - first);
close(f);
end
