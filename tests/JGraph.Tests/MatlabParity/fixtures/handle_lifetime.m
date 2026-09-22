% handle_lifetime.m -- V6 of the value-ownership plan: the graphics verbs the probes needed (appendix
% A #102-#104). guidata stores a copy on the figure an object belongs to and hands a copy back;
% axes(parent) makes an axes in the figure it is given and makes both current; isvalid answers for a
% graphics handle and for a handle object, and a deleted handle object is no longer valid. A case
% named aNNN is appendix row NNN; h_ cases are the forms around it.

run_case('a102_guidata_alias', @a102_guidata_alias);
run_case('h_guidata_roundtrip', @h_guidata_roundtrip);
run_case('h_guidata_read_back_isolated', @h_guidata_read_back_isolated);
run_case('h_guidata_via_child', @h_guidata_via_child);
run_case('h_guidata_via_axes_read', @h_guidata_via_axes_read);
run_case('h_guidata_empty', @h_guidata_empty);
run_case('h_guidata_overwrite', @h_guidata_overwrite);
run_case('h_guidata_nonstruct', @h_guidata_nonstruct);
run_case('h_guidata_handle_object', @h_guidata_handle_object);
run_case('h_guidata_bad_handle', @h_guidata_bad_handle);
run_case('h_guidata_after_close', @h_guidata_after_close);
run_case('a103_axes_parent_figure', @a103_axes_parent_figure);
run_case('h_axes_parent_makes_current', @h_axes_parent_makes_current);
run_case('h_axes_parent_with_options', @h_axes_parent_with_options);
run_case('h_axes_parent_second_axes', @h_axes_parent_second_axes);
run_case('h_axes_parent_name_value', @h_axes_parent_name_value);
run_case('h_axes_parent_line_refused', @h_axes_parent_line_refused);
run_case('h_axes_handle_selects', @h_axes_handle_selects);
run_case('h_isvalid_live_line', @h_isvalid_live_line);
run_case('a104_isvalid_after_delete', @a104_isvalid_after_delete);
run_case('h_isvalid_array', @h_isvalid_array);
run_case('h_isvalid_figure_after_close', @h_isvalid_figure_after_close);
run_case('h_isvalid_after_clf', @h_isvalid_after_clf);
run_case('h_isvalid_struct_held', @h_isvalid_struct_held);
run_case('h_isvalid_empty', @h_isvalid_empty);
run_case('h_isvalid_handle_object', @h_isvalid_handle_object);
run_case('h_isvalid_handle_object_deleted', @h_isvalid_handle_object_deleted);
run_case('h_isvalid_handle_alias_deleted', @h_isvalid_handle_alias_deleted);
run_case('h_isvalid_deleted_object_prop_read', @h_isvalid_deleted_object_prop_read);
run_case('h_isvalid_deleted_object_prop_write', @h_isvalid_deleted_object_prop_write);
run_case('h_isvalid_delete_twice', @h_isvalid_delete_twice);
run_case('h_isvalid_delete_runs_destructor', @h_isvalid_delete_runs_destructor);
run_case('h_isvalid_value_object', @h_isvalid_value_object);
run_case('h_isvalid_number', @h_isvalid_number);
close all

function run_case(name, fn)
global vlog_text
vlog_text = '';
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

function s = logged_text()
global vlog_text
s = vlog_text;
end

% --- guidata (#102) -------------------------------------------------------------------------------

function s = a102_guidata_alias()
f = figure('Visible', 'off');
g.a = 1;
guidata(f, g);
g.a = 2;
t = guidata(f);
s = sprintf('%d', t.a);
close(f);
end

function s = h_guidata_roundtrip()
f = figure('Visible', 'off');
g.a = 1;
g.b = [1 2 3];
guidata(f, g);
t = guidata(f);
s = sprintf('%d %s %s', t.a, mat2str(t.b), strjoin(fieldnames(t)', ','));
close(f);
end

function s = h_guidata_read_back_isolated()
f = figure('Visible', 'off');
g.a = 1;
guidata(f, g);
t = guidata(f);
t.a = 3;
u = guidata(f);
s = sprintf('%d %d', u.a, t.a);
close(f);
end

function s = h_guidata_via_child()
f = figure('Visible', 'off');
p = plot(1:3);
g.a = 5;
guidata(p, g);
t = guidata(f);
s = sprintf('%d', t.a);
close(f);
end

function s = h_guidata_via_axes_read()
f = figure('Visible', 'off');
ax = axes(f);
g.a = 6;
guidata(f, g);
t = guidata(ax);
s = sprintf('%d', t.a);
close(f);
end

function s = h_guidata_empty()
f = figure('Visible', 'off');
t = guidata(f);
s = sprintf('%s %s', mat2str(t), class(t));
close(f);
end

function s = h_guidata_overwrite()
f = figure('Visible', 'off');
g.a = 1;
guidata(f, g);
h.b = 2;
guidata(f, h);
t = guidata(f);
s = strjoin(fieldnames(t)', ',');
close(f);
end

function s = h_guidata_nonstruct()
f = figure('Visible', 'off');
v = [1 2 3];
guidata(f, v);
v(1) = 9;
s = mat2str(guidata(f));
close(f);
end

function s = h_guidata_handle_object()
f = figure('Visible', 'off');
o = HandleHolder;
o.data = [1 2 3];
guidata(f, o);
o.data(1) = 7;
t = guidata(f);
s = mat2str(t.data);
close(f);
end

function s = h_guidata_bad_handle()
g.a = 1;
guidata(1234567.5, g);
s = 'stored';
end

function s = h_guidata_after_close()
f = figure('Visible', 'off');
g.a = 1;
guidata(f, g);
close(f);
t = guidata(f);
s = sprintf('%d', t.a);
end

% --- axes(parent) (#103) ---------------------------------------------------------------------------

function s = a103_axes_parent_figure()
f = figure('Visible', 'off');
ax = axes(f);
lim = [0 10];
ax.XLim = lim;
lim(1) = -5;
s = sprintf('%s %d', mat2str(ax.XLim), ax.Parent == f);
close(f);
end

function s = h_axes_parent_makes_current()
f1 = figure('Visible', 'off');
f2 = figure('Visible', 'off');
figure(f1);
ax = axes(f2);
s = sprintf('%d %d %d', ax.Parent == f2, gcf == f2, gca == ax);
close(f1);
close(f2);
end

function s = h_axes_parent_with_options()
f = figure('Visible', 'off');
ax = axes(f, 'XLim', [0 5], 'Tag', 'left');
s = sprintf('%s %s %d', mat2str(ax.XLim), ax.Tag, ax.Parent == f);
close(f);
end

function s = h_axes_parent_second_axes()
f = figure('Visible', 'off');
ax1 = axes(f);
ax2 = axes(f);
s = sprintf('%d %d %d', numel(findobj(f, 'Type', 'axes')), ax1 == ax2, gca == ax2);
close(f);
end

function s = h_axes_parent_name_value()
f = figure('Visible', 'off');
ax = axes('Parent', f, 'Tag', 'nv');
s = sprintf('%d %s', ax.Parent == f, ax.Tag);
close(f);
end

function s = h_axes_parent_line_refused()
f = figure('Visible', 'off');
p = plot(1:3);
ax = axes(p); %#ok<NASGU>
s = 'made';
close(f);
end

function s = h_axes_handle_selects()
f = figure('Visible', 'off');
ax1 = axes(f);
ax2 = axes(f); %#ok<NASGU>
axes(ax1);
s = sprintf('%d %d', gca == ax1, numel(findobj(f, 'Type', 'axes')));
close(f);
end

% --- isvalid (#104) --------------------------------------------------------------------------------

function s = h_isvalid_live_line()
f = figure('Visible', 'off');
p = plot(1:2);
s = sprintf('%d %d', isvalid(p), isvalid(f));
close(f);
end

function s = a104_isvalid_after_delete()
f = figure('Visible', 'off');
q = plot(1:2);
r = q;
delete(q);
s = sprintf('%d %d', isvalid(r), isvalid(q));
close(f);
end

function s = h_isvalid_array()
f = figure('Visible', 'off');
p1 = plot(1:2);
hold on
p2 = plot(3:4);
h = [p1 p2];
delete(p2);
s = sprintf('%s %s', mat2str(isvalid(h)), class(isvalid(h)));
close(f);
end

function s = h_isvalid_figure_after_close()
f = figure('Visible', 'off');
p = plot(1:2);
close(f);
s = sprintf('%d %d', isvalid(f), isvalid(p));
end

function s = h_isvalid_after_clf()
f = figure('Visible', 'off');
p = plot(1:2);
ax = gca;
clf(f);
s = sprintf('%d %d %d', isvalid(p), isvalid(ax), isvalid(f));
close(f);
end

function s = h_isvalid_struct_held()
f = figure('Visible', 'off');
st.p = plot(1:2);
delete(st.p);
s = sprintf('%d', isvalid(st.p));
close(f);
end

function s = h_isvalid_empty()
e = gobjects(0);
r = isvalid(e);
s = sprintf('%s %s', mat2str(size(r)), class(r));
end

function s = h_isvalid_handle_object()
o = HandleHolder;
s = sprintf('%d %s', isvalid(o), class(isvalid(o)));
end

function s = h_isvalid_handle_object_deleted()
o = HandleHolder;
delete(o);
s = sprintf('%d', isvalid(o));
end

function s = h_isvalid_handle_alias_deleted()
o = HandleHolder;
o2 = o;
delete(o);
s = sprintf('%d %d', isvalid(o2), isvalid(o));
end

function s = h_isvalid_deleted_object_prop_read()
o = HandleHolder;
o.data = 1;
delete(o);
s = sprintf('%d', o.data);
end

function s = h_isvalid_deleted_object_prop_write()
o = HandleHolder;
delete(o);
o.data = 5;
s = 'written';
end

function s = h_isvalid_delete_twice()
o = HandleHolder;
delete(o);
delete(o);
s = sprintf('%d', isvalid(o));
end

function s = h_isvalid_delete_runs_destructor()
dl = DeleteLogger('D');
delete(dl);
vlog('after');
s = sprintf('%s %d', logged_text(), isvalid(dl));
end

function s = h_isvalid_value_object()
v = ValueBox;
s = sprintf('%d', isvalid(v));
end

function s = h_isvalid_number()
s = sprintf('%d', isvalid(5));
end
