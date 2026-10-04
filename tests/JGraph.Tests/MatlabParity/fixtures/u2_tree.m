% record: -noFigureWindows
% U2 of the app-building plan (ADR 0199): the tree. The order Children lists a figure's components
% and axes in, uistack, findobj/findall/allchild over containers, HandleVisibility's three states,
% moving things between parents, the order DeleteFcns run in, and how the root, gcf and close see a
% uifigure. No figure here is ever shown.
global U1LOG
f = figure('Visible', 'off', 'Position', [100 100 560 420], 'MenuBar', 'none', 'ToolBar', 'none', 'Tag', 'f');

% --- Children: components before axes, newest first ------------------------------------------------
a1 = axes(f, 'Tag', 'a1');
c1 = uicontrol(f, 'Tag', 'c1');
p1 = uipanel(f, 'Tag', 'p1');
a2 = axes(f, 'Tag', 'a2');
c2 = uicontrol(f, 'Tag', 'c2');
fprintf('CHK|children|%s|exact\n', u2_tags(f.Children));
c3 = uicontrol(p1, 'Tag', 'c3');
a3 = axes(p1, 'Tag', 'a3');
p2 = uipanel(p1, 'Tag', 'p2');
c4 = uicontrol(p2, 'Tag', 'c4');
fprintf('CHK|panel_children|%s|exact\n', u2_tags(p1.Children));
fprintf('CHK|findobj_figure|%s|exact\n', u2_tags(findobj(f)));
fprintf('CHK|findobj_depth_1|%s|exact\n', u2_tags(findobj(f, '-depth', 1)));
fprintf('CHK|findobj_flat|%s|exact\n', u2_tags(findobj(f, 'flat')));
fprintf('CHK|findobj_panel|%s|exact\n', u2_tags(findobj(p1)));
fprintf('CHK|findobj_uicontrols|%s|exact\n', u2_tags(findobj(f, 'Type', 'uicontrol')));
fprintf('CHK|findobj_panels|%s|exact\n', u2_tags(findobj(f, 'Type', 'uipanel')));
fprintf('CHK|findall_axes|%s|exact\n', u2_tags(findall(f, 'Type', 'axes')));
fprintf('CHK|findobj_everywhere|%d|exact\n', numel(findobj('Tag', 'c4')));
fprintf('CHK|findobj_property|%s|exact\n', u2_tags(findobj(f, '-property', 'BorderType')));
fprintf('CHK|allchild_figure|%s|exact\n', u2_tags(allchild(f)));
fprintf('CHK|allchild_panel|%s|exact\n', u2_tags(allchild(p1)));
fprintf('CHK|allchild_is_column|%d|exact\n', size(allchild(p1), 2));
fprintf('CHK|allchild_count_with_furniture|%d|div=ADR0199\n', numel(allchild(p1)));

% --- uistack -------------------------------------------------------------------------------------
steps = {
    {c1, 'top'}
    {c1, 'bottom'}
    {p1, 'up'}
    {p1, 'up', 2}
    {p1, 'down', 1}
    {p1, 'down', 99}
    {a1, 'top'}
    {[c1 c2], 'top'}
    {c1}
    {c2, 'up', -1}
    {f, 'top'}
    };
for k = 1:numel(steps)
    uistack(steps{k}{:});
    fprintf('CHK|uistack_%d|%s|exact\n', k, u2_tags(allchild(f)));
end
uistack(c3, 'top');
fprintf('CHK|uistack_in_panel|%s|exact\n', u2_tags(allchild(p1)));
u2_err('uistack_bogus', @() uistack(c1, 'bogus'));
u2_err('uistack_fraction', @() uistack(c1, 'up', 1.5));
u2_err('uistack_not_a_handle', @() uistack(5.5, 'top'));
u2_err('uistack_two_parents', @() uistack([c1 c3], 'top'));
u2_err('uistack_no_arguments', @() uistack());
fprintf('CHK|children_before_set|%s|exact\n', u2_tags(f.Children));
k = f.Children;
set(f, 'Children', k([2 1 3 4 5]));
fprintf('CHK|set_children|%s|exact\n', u2_tags(allchild(f)));
lastwarn('', '');
k = f.Children;
set(f, 'Children', k([4 2 3 1 5]));
[msg, id] = lastwarn;
fprintf('CHK|set_children_across_kinds|%s|exact\n', u2_tags(allchild(f)));
fprintf('CHK|set_children_across_kinds_warning|%s : %s|exact\n', id, msg);
u2_err('set_children_missing_one', @() set(f, 'Children', k(1:2)));
u2_err('set_children_not_handles', @() set(f, 'Children', [1.5 2.5 3.5 4.5 5.5]));

% --- HandleVisibility ----------------------------------------------------------------------------
c2.HandleVisibility = 'off';
p1.HandleVisibility = 'callback';
fprintf('CHK|hidden_children|%s|exact\n', u2_tags(f.Children));
fprintf('CHK|hidden_allchild|%s|exact\n', u2_tags(allchild(f)));
fprintf('CHK|hidden_findobj|%s|exact\n', u2_tags(findobj(f)));
fprintf('CHK|hidden_findall|%s|exact\n', u2_tags(findall(f)));
fprintf('CHK|hidden_findobj_from_hidden|%s|exact\n', u2_tags(findobj(p1)));
fprintf('CHK|hidden_findobj_itself|%s|exact\n', u2_tags(findobj(c2)));
fprintf('CHK|hidden_still_a_handle|%d %s|exact\n', ishandle(c2), c2.Tag);
set(groot, 'ShowHiddenHandles', 'on');
fprintf('CHK|show_hidden_children|%s|exact\n', u2_tags(f.Children));
fprintf('CHK|show_hidden_findobj|%s|exact\n', u2_tags(findobj(f)));
set(groot, 'ShowHiddenHandles', 'off');
fprintf('CHK|show_hidden_default|%s|exact\n', char(get(groot, 'ShowHiddenHandles')));
u2_err('handlevisibility_bogus', @() set(f, 'HandleVisibility', 'bogus'));
u2_chk('handlevisibility_upper', @() u2_setget(c1, 'HandleVisibility', 'CALL'));
c1.HandleVisibility = 'on'; c2.HandleVisibility = 'on'; p1.HandleVisibility = 'on';
f.HandleVisibility = 'off';
fprintf('CHK|hidden_figure_in_root|%d|exact\n', any(get(0, 'Children') == f));
fprintf('CHK|hidden_figure_findobj|%d|exact\n', numel(findobj(0, 'Type', 'figure')));
fprintf('CHK|hidden_figure_findall|%d|exact\n', numel(findall(0, 'Type', 'figure')));
fprintf('CHK|hidden_figure_current|%d|exact\n', isempty(get(0, 'CurrentFigure')));
g = gcf;
fprintf('CHK|gcf_makes_another|%d %d|exact\n', g ~= f, g.Number);
delete(g);
close all;
fprintf('CHK|close_all_leaves_hidden|%d|exact\n', ishandle(f));
f.HandleVisibility = 'on';

% --- moving things between parents -----------------------------------------------------------------
c1.Units = 'pixels'; c1.Position = [20 30 60 20];
p1.Units = 'pixels'; p1.Position = [100 100 200 150];
c1.Parent = p1;
fprintf('CHK|reparent_control_position|%s|exact\n', u2_text(c1.Position));
fprintf('CHK|reparent_control_in_figure|%s|exact\n', u2_text(getpixelposition(c1, true)));
fprintf('CHK|reparent_control_children|%s|exact\n', u2_tags(p1.Children));
a1.Parent = p1;
fprintf('CHK|reparent_axes_children|%s|exact\n', u2_tags(p1.Children));
fprintf('CHK|reparent_axes_figure_children|%s|exact\n', u2_tags(f.Children));
fprintf('CHK|reparent_axes_parent|%d|exact\n', a1.Parent == p1);
u2_err('reparent_into_own_child', @() set(p1, 'Parent', p2));
u2_err('reparent_into_itself', @() set(p1, 'Parent', p1));
u2_err('reparent_control_into_axes', @() set(c1, 'Parent', a2));
u2_err('reparent_control_into_control', @() set(c1, 'Parent', c2));
u2_err('reparent_axes_into_control', @() set(a2, 'Parent', c2));
u2_err('reparent_axes_into_axes', @() set(a2, 'Parent', a1));
u2_err('reparent_control_to_number', @() set(c1, 'Parent', 5.5));
u2_err('reparent_axes_to_number', @() set(a2, 'Parent', 5.5));
u2_err('reparent_panel_to_root', @() set(p1, 'Parent', groot));
u2_err('reparent_control_to_root', @() set(c2, 'Parent', 0));
f2 = figure('Visible', 'off', 'Tag', 'f2');
p1.Parent = f2;
fprintf('CHK|panel_moved_old_figure|%s|exact\n', u2_tags(f.Children));
fprintf('CHK|panel_moved_new_figure|%s|exact\n', u2_tags(f2.Children));
fprintf('CHK|panel_moved_descendants|%d %d|exact\n', ancestor(c4, 'figure') == f2, ancestor(a3, 'figure') == f2);
p1.Parent = f;

% --- the order DeleteFcns run in -------------------------------------------------------------------
U1LOG = {};
all = [f, p1, p2, c1, c3, c4, a1, a3, c2, a2];
for k = 1:numel(all)
    set(all(k), 'DeleteFcn', @(s, e) u1_log(sprintf('%s:%s:%s', get(s, 'Tag'), get(s, 'Type'), char(get(s, 'BeingDeleted')))));
end
delete(p2);
fprintf('CHK|delete_inner_panel|%s|exact\n', strjoin(U1LOG, ' '));
fprintf('CHK|delete_inner_panel_child_gone|%d|exact\n', ishandle(c4));
U1LOG = {};
fprintf('CHK|before_delete_panel|%s|exact\n', u2_tags(p1.Children));
delete(p1);
fprintf('CHK|delete_panel|%s|exact\n', strjoin(U1LOG, ' '));
fprintf('CHK|delete_panel_axes_gone|%d %d|exact\n', ishandle(a1), ishandle(a3));
U1LOG = {};
fprintf('CHK|before_delete_figure|%s|exact\n', u2_tags(f.Children));
delete(f);
fprintf('CHK|delete_figure|%s|exact\n', strjoin(U1LOG, ' '));
delete(f2);

% --- clf with a panel ------------------------------------------------------------------------------
U1LOG = {};
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
p = uipanel(f, 'Tag', 'p', 'DeleteFcn', @(s, e) u1_log('panel'));
c = uicontrol(p, 'Tag', 'c', 'DeleteFcn', @(s, e) u1_log('control'));
hv = uicontrol(f, 'Tag', 'hidden', 'HandleVisibility', 'off');
clf(f);
fprintf('CHK|clf_deleted|%s|exact\n', strjoin(U1LOG, ' '));
fprintf('CHK|clf_left|%s|exact\n', u2_tags(allchild(f)));
clf(f, 'reset');
fprintf('CHK|clf_reset_left|%s|exact\n', u2_tags(allchild(f)));
delete(f);

% --- uifigure: its handle, and how the root, gcf and close see it -----------------------------------
uf = uifigure('Visible', 'off');
d = double(uf);
fprintf('CHK|uifigure_type|%s|exact\n', uf.Type);
fprintf('CHK|uifigure_number|%s|exact\n', u2_text(uf.Number));
fprintf('CHK|uifigure_handle|%d %d %d|exact\n', d == floor(d), d > 0, ishandle(d));
fprintf('CHK|uifigure_root|%d %d %d %d|exact\n', numel(get(0, 'Children')), numel(findobj(0, 'Type', 'figure')), ...
    numel(findall(0, 'Type', 'figure')), isempty(get(0, 'CurrentFigure')));
f1 = figure('Visible', 'off');
fprintf('CHK|figure_after_uifigure|%d %d|exact\n', f1.Number, gcf == f1);
figure(uf);
fprintf('CHK|figure_uifigure_not_current|%d|exact\n', gcf == f1);
delete(f1);
fprintf('CHK|no_current_after_delete|%d|exact\n', isempty(get(0, 'CurrentFigure')));
close all;
fprintf('CHK|close_all_leaves_uifigure|%d|exact\n', ishandle(uf));
close(uf);
fprintf('CHK|close_uifigure|%d|exact\n', ishandle(uf));
uf = uifigure('Visible', 'off');
close all force;
fprintf('CHK|close_all_force|%d|exact\n', ishandle(uf));
uf = uifigure('Visible', 'off');
names = {'Units', 'Visible', 'Color', 'IntegerHandle', 'HandleVisibility', 'MenuBar', 'ToolBar', 'NumberTitle', ...
    'Name', 'AutoResizeChildren', 'Scrollable', 'Resize', 'Type', 'CloseRequestFcn', 'SizeChangedFcn', 'Tag', 'WindowState'};
for k = 1:numel(names)
    fprintf('CHK|uifigure_default_%s|%s|exact\n', names{k}, u2_text(get(uf, names{k})));
end
fprintf('CHK|uifigure_default_size|%s|exact\n', u2_text(uf.Position(3:4)));
u2_chk('uifigure_name', @() get(uifigure('Visible', 'off', 'Name', 'N'), 'Name'));
u2_chk('uifigure_number_argument', @() uifigure(5));
u2_chk('uifigure_odd', @() uifigure('Name'));
u2_chk('uifigure_unknown', @() uifigure('Bogus', 1));
u2_chk('uifigure_struct', @() get(uifigure(struct('Visible', 'off', 'Name', 'S')), 'Name'));
u2_err('uifigure_set_number', @() set(uf, 'Number', 3));
u2_chk('uifigure_units', @() u2_setget(uf, 'Units', 'normalized'));
uf.Units = 'pixels';
u2_chk('uifigure_scrollable', @() u2_setget(uf, 'Scrollable', 'on'));
u2_chk('uifigure_resize', @() u2_setget(uf, 'Resize', 'off'));
u2_err('uifigure_integerhandle_bogus', @() set(uf, 'IntegerHandle', 'bogus'));
u2_err('uifigure_autoresize_bogus', @() set(uf, 'AutoResizeChildren', 'bogus'));
b = uicontrol(uf, 'Tag', 'b');
p = uipanel(uf, 'Tag', 'p');
a = axes(uf, 'Tag', 'a');
b2 = uicontrol(uf, 'Tag', 'b2');
fprintf('CHK|uifigure_children|%s|exact\n', u2_tags(uf.Children));
fprintf('CHK|uifigure_findobj|%d|exact\n', numel(findobj(uf)));
fprintf('CHK|uifigure_panel_units|%s|exact\n', p.Units);
fprintf('CHK|uifigure_still_no_current|%d|exact\n', isempty(get(0, 'CurrentFigure')));
lastwarn('', '');
uf.SizeChangedFcn = 'disp(1)';
[msg, id] = lastwarn;
fprintf('CHK|uifigure_sizechanged_warning|%s : %s|exact\n', id, msg);

% Without a window nothing resizes the children and nothing calls SizeChangedFcn.
U1LOG = {};
uf.AutoResizeChildren = 'off';
uf.SizeChangedFcn = @(s, e) u1_log('resized');
b.Position = [100 100 100 50];
uf.Position = [100 100 800 600];
drawnow;
fprintf('CHK|headless_resize_control|%s|exact\n', u2_text(b.Position));
fprintf('CHK|headless_resize_calls|%d|exact\n', numel(U1LOG));
delete(uf);

% A figure whose IntegerHandle is off has no number, and is still in the root's list.
f = figure('Visible', 'off', 'IntegerHandle', 'off');
d = double(f);
fprintf('CHK|integerhandle_off|%d %d %d|exact\n', d == floor(d), isempty(f.Number), any(get(0, 'Children') == f));
delete(f);
