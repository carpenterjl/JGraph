% record: -noFigureWindows
% U3 of the app-building plan (ADR 0200): uibuttongroup in a classic figure and in a uifigure,
% neither ever shown — its defaults, the buttons it watches, what it writes into them, and
% SelectedObject and SelectionChangedFcn with R2025b's refusals.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);

% --- defaults, in both kinds of figure ----------------------------------------------------------
props = {'Type', 'Title', 'TitlePosition', 'BorderType', 'BorderWidth', 'BackgroundColor', 'ForegroundColor', ...
    'BorderColor', 'HighlightColor', 'FontName', 'FontSize', 'FontUnits', 'FontWeight', 'FontAngle', 'Units', ...
    'Position', 'InnerPosition', 'OuterPosition', 'Visible', 'Enable', 'Scrollable', 'AutoResizeChildren', ...
    'Clipping', 'Tag', 'Tooltip', 'HandleVisibility', 'SizeChangedFcn', 'SelectionChangedFcn', 'ButtonDownFcn', ...
    'BusyAction', 'Interruptible', 'BeingDeleted'};
bg = uibuttongroup(f);
ubg = uibuttongroup(uf);
p = uipanel(f);
for k = 1:numel(props)
    fprintf('CHK|classic_default_%s|%s|exact\n', props{k}, u2_text(get(bg, props{k})));
    fprintf('CHK|uifigure_default_%s|%s|exact\n', props{k}, u2_text(get(ubg, props{k})));
end
fprintf('CHK|default_selected_empty|%d %d|exact\n', isempty(bg.SelectedObject), isempty(ubg.SelectedObject));
fprintf('CHK|names_only_in_group|%s|exact\n', strjoin(sort(setdiff(fieldnames(get(bg)), fieldnames(get(p))))', ' '));
fprintf('CHK|names_only_in_panel|%s|exact\n', strjoin(sort(setdiff(fieldnames(get(p)), fieldnames(get(bg))))', ' '));
fprintf('CHK|set_names_only_in_group|%s|exact\n', strjoin(sort(setdiff(fieldnames(set(bg)), fieldnames(set(p))))', ' '));
for name = {'SelectionChangeFcn', 'ResizeFcn', 'ShadowColor', 'TooltipString'}
    u2_chk(['hidden_' name{1}], @() get(bg, name{1}));
end
u2_chk('hidden_Buttons_empty', @() isempty(get(bg, 'Buttons')));
fprintf('CHK|isa_panel_type|%s|exact\n', get(bg, 'Type'));
delete(p); delete(ubg);

% --- the forms of the call ----------------------------------------------------------------------
c0 = uicontrol(f);
u2_chk('form_parent', @() get(uibuttongroup(f), 'Parent') == f);
u2_chk('form_named_parent', @() get(uibuttongroup('Parent', f, 'Title', 'T'), 'Title'));
u2_chk('form_struct', @() get(uibuttongroup(f, struct('Title', 'S')), 'Title'));
u2_chk('form_odd', @() uibuttongroup('Title'));
u2_chk('form_parent_odd', @() uibuttongroup(f, 'Title'));
u2_chk('form_not_a_handle', @() uibuttongroup(5.5));
u2_chk('form_in_uicontrol', @() uibuttongroup(c0));
u2_chk('form_unknown', @() uibuttongroup(f, 'Bogus', 1));
u2_chk('form_in_panel', @() get(get(uibuttongroup(uipanel(f)), 'Parent'), 'Type'));
u2_chk('form_panel_in_group', @() get(get(uipanel(uibuttongroup(f)), 'Parent'), 'Type'));
u2_chk('form_axes_in_group', @() get(get(axes(uibuttongroup(f)), 'Parent'), 'Type'));
u2_chk('form_group_in_group', @() get(get(uibuttongroup(uibuttongroup(f)), 'Parent'), 'Type'));
clf(f);

% --- the buttons a group watches ----------------------------------------------------------------
bg = uibuttongroup(f, 'Units', 'pixels', 'Position', [20 20 300 300], 'Tag', 'bg');
r1 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'A', 'Tag', 'r1', 'Position', [10 250 100 20]);
fprintf('CHK|first_radio|%s|exact\n', u3_state(bg, r1));
r2 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'B', 'Tag', 'r2', 'Position', [10 220 100 20]);
t1 = uicontrol(bg, 'Style', 'togglebutton', 'String', 'T', 'Tag', 't1', 'Position', [10 190 100 20]);
cb = uicontrol(bg, 'Style', 'checkbox', 'String', 'C', 'Tag', 'cb', 'Position', [10 160 100 20]);
pb = uicontrol(bg, 'Style', 'pushbutton', 'String', 'P', 'Tag', 'pb', 'Position', [10 130 100 20]);
all5 = [r1 r2 t1 cb pb];
fprintf('CHK|five|%s|exact\n', u3_state(bg, all5));
fprintf('CHK|children|%s|exact\n', u2_tags(bg.Children));

% --- selecting through Value --------------------------------------------------------------------
steps = {
    'r2_one', r2, 1
    'r2_zero_selected', r2, 0
    'r1_zero_unselected', r1, 0
    't1_one', t1, 1
    'cb_one', cb, 1
    'pb_one', pb, 1
    'r1_half', r1, 0.5
    'r1_five', r1, 5
    'r1_true', r1, true
    'r1_half_selected', r1, 0.5
    'r1_ones', r1, [1 1]
    'r2_ones', r2, [1 1]
    'r2_one_two', r2, [1 2]
    };
for k = 1:size(steps, 1)
    set(steps{k, 2}, 'Value', steps{k, 3});
    fprintf('CHK|value_%s|%s|exact\n', steps{k, 1}, u3_state(bg, all5));
end
cb.Value = 0; pb.Value = 0;

% --- selecting through SelectedObject -----------------------------------------------------------
out = uicontrol(f, 'Style', 'radiobutton', 'Tag', 'out', 'Position', [400 20 100 20]);
picks = {'r1', r1; 't1', t1; 'none', []; 'checkbox', cb; 'pushbutton', pb; 'figure', f; 'outside', out; ...
    'number', 5.5; 'text', 'r1'; 'two', [r1 r2]; 'double', double(r2)};
for k = 1:size(picks, 1)
    u2_err(['select_' picks{k, 1}], @() set(bg, 'SelectedObject', picks{k, 2}));
    fprintf('CHK|select_%s_state|%s|exact\n', picks{k, 1}, u3_state(bg, all5));
end
bg.SelectedObject = [];
fprintf('CHK|selected_empty_size|%s|exact\n', mat2str(size(bg.SelectedObject)));

% --- Min and Max do not enter into it -----------------------------------------------------------
bg.SelectedObject = r1;
r2.Min = 2; r2.Max = 7; fprintf('CHK|minmax_set|%s|exact\n', u3_state(bg, all5));
r2.Value = 7; fprintf('CHK|minmax_at_max|%s|exact\n', u3_state(bg, all5));
r2.Value = 1; fprintf('CHK|minmax_one|%s|exact\n', u3_state(bg, all5));
bg.SelectedObject = r1; fprintf('CHK|minmax_deselected|%s|exact\n', u3_state(bg, all5));
bg.SelectedObject = r2; fprintf('CHK|minmax_selected|%s|exact\n', u3_state(bg, all5));
r2.Min = 0; r2.Max = 1;

% --- moving in and out, deleting, changing style ------------------------------------------------
bg.SelectedObject = r1;
out.Value = 1; out.Parent = bg; fprintf('CHK|moved_in_at_max|%s|exact\n', u3_state(bg, [all5 out]));
out.Parent = f; fprintf('CHK|moved_out|%s out:%s|exact\n', u3_state(bg, all5), mat2str(out.Value));
bg.SelectedObject = r2; r2.Parent = f; fprintf('CHK|selected_moved_out|%s r2:%s|exact\n', u3_state(bg, [r1 t1 cb pb]), mat2str(r2.Value));
r2.Parent = bg; fprintf('CHK|moved_back|%s|exact\n', u3_state(bg, all5));
bg.SelectedObject = t1; delete(t1); fprintf('CHK|selected_deleted|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
pb.Style = 'radiobutton'; pb.Value = 1; fprintf('CHK|late_radio_value|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
u2_err('late_radio_select', @() set(bg, 'SelectedObject', pb));
fprintf('CHK|late_radio_selected|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
r1.Value = 1; fprintf('CHK|after_late_radio|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
r1.Style = 'checkbox'; fprintf('CHK|selected_becomes_checkbox|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
r2.Value = 1; fprintf('CHK|other_selected|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
u2_err('checkbox_value_one', @() set(r1, 'Value', 1));
fprintf('CHK|checkbox_value_one_state|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
r1.Style = 'radiobutton'; r1.Value = 0; r1.Value = 1; fprintf('CHK|radio_again|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
r1.Visible = 'off'; r2.Enable = 'off'; r2.Value = 1; fprintf('CHK|disabled_selected|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
set([r1 r2], 'Value', 1); fprintf('CHK|set_both|%s|exact\n', u3_state(bg, [r1 r2 cb pb]));
delete(bg); delete(out);

% --- joining a group ----------------------------------------------------------------------------
g = uibuttongroup(f);
a = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'a', 'Value', 0); fprintf('CHK|join_first_value_0|%s|exact\n', u3_state(g, a));
b = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'b', 'Value', 1); fprintf('CHK|join_second_value_1|%s|exact\n', u3_state(g, [a b]));
c = uicontrol('Parent', g, 'Style', 'radiobutton', 'Tag', 'c'); fprintf('CHK|join_named_parent|%s|exact\n', u3_state(g, [a b c]));
d = uicontrol('Style', 'radiobutton', 'Tag', 'd', 'Parent', g); fprintf('CHK|join_style_first|%s|exact\n', u3_state(g, [a b c d]));
g.SelectedObject = [];
e = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'e'); fprintf('CHK|join_no_selection|%s|exact\n', u3_state(g, [a b c d e]));
h = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'h', 'Value', 5); fprintf('CHK|join_value_5|%s|exact\n', u3_state(g, [a b c d e h]));
delete(g);
g = uibuttongroup(f);
t = uicontrol(g, 'Style', 'togglebutton', 'Tag', 't'); fprintf('CHK|join_first_toggle|%s|exact\n', u3_state(g, t));
e = uicontrol(g, 'Tag', 'e'); e.Style = 'radiobutton'; fprintf('CHK|join_as_pushbutton|%s|exact\n', u3_state(g, [t e]));
delete(g);
g = uibuttongroup(f);
e = uicontrol(g, 'Tag', 'e'); e.Style = 'radiobutton'; fprintf('CHK|join_empty_as_pushbutton|%s|exact\n', u3_state(g, e));
delete(g);
g = uibuttongroup(f, 'SelectedObject', []);
h1 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'h1');
inner = uipanel(g); h2 = uicontrol(inner, 'Style', 'radiobutton', 'Tag', 'h2', 'Value', 1);
fprintf('CHK|join_in_inner_panel|%s|exact\n', u3_state(g, [h1 h2]));
delete(g);
g = uibuttongroup(f);
q1 = uicontrol(f, 'Style', 'radiobutton', 'Tag', 'q1'); q1.Parent = g; fprintf('CHK|join_moved_first|%s|exact\n', u3_state(g, q1));
q2 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'q2'); q2.Value = 3;
q3 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'q3', 'Value', 1); fprintf('CHK|join_third_at_max|%s|exact\n', u3_state(g, [q1 q2 q3]));
q1.Max = 5; q1.Value = 5; fprintf('CHK|max_5_value_5|%s|exact\n', u3_state(g, [q1 q2 q3]));
q1.Value = 1; fprintf('CHK|max_5_value_1|%s|exact\n', u3_state(g, [q1 q2 q3]));
delete(g);
g = uibuttongroup(f);
m1 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'm1', 'Max', 7); fprintf('CHK|join_first_max_7|%s|exact\n', u3_state(g, m1));
m2 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'm2'); m2.Value = 1; fprintf('CHK|join_then_plain|%s|exact\n', u3_state(g, [m1 m2]));
delete(g);
g = uibuttongroup(f);
n1 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'n1', 'Min', -1); fprintf('CHK|join_first_min_minus_1|%s|exact\n', u3_state(g, n1));
n2 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'n2'); n2.Value = 1; fprintf('CHK|join_after_unwatched|%s|exact\n', u3_state(g, [n1 n2]));
delete(g);
g = uibuttongroup(f);
x1 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'x1', 'Min', 2, 'Max', 7);
x2 = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'x2', 'Min', 3, 'Max', 9); fprintf('CHK|unwatched_pair|%s|exact\n', u3_state(g, [x1 x2]));
x2.Value = 1; x1.Value = 1; fprintf('CHK|unwatched_pair_ones|%s|exact\n', u3_state(g, [x1 x2]));
g.SelectedObject = x2; fprintf('CHK|unwatched_pair_selected|%s|exact\n', u3_state(g, [x1 x2]));
g.SelectedObject = x1; fprintf('CHK|unwatched_pair_other|%s|exact\n', u3_state(g, [x1 x2]));
delete(g);

% --- copies -------------------------------------------------------------------------------------
g = uibuttongroup(f, 'Tag', 'g');
a = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'a');
b = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'b');
c2 = copyobj(b, g); c2.Tag = 'c2'; fprintf('CHK|copy_of_button|%s|exact\n', u3_state(g, [a b c2]));
gc = copyobj(g, f);
fprintf('CHK|copy_of_group|%s %d|exact\n', u3_tag(gc.SelectedObject), numel(gc.Children));
fprintf('CHK|copy_is_its_own|%d|exact\n', gc.SelectedObject ~= a);
delete(g); delete(gc);

% --- the callbacks ------------------------------------------------------------------------------
global U1LOG
U1LOG = {};
bg = uibuttongroup(f, 'Units', 'pixels', 'Position', [20 20 300 300], 'Tag', 'bg');
r1 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r1');
r2 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r2');
u2_chk('changedfcn_handle', @() class(u2_setget(bg, 'SelectionChangedFcn', @(s, e) u1_log('changed'))));
u2_chk('changefcn_reads', @() class(get(bg, 'SelectionChangeFcn')));
u2_chk('changefcn_char', @() u2_setget(bg, 'SelectionChangeFcn', 'disp(1)'));
u2_chk('changedfcn_reads', @() get(bg, 'SelectionChangedFcn'));
u2_chk('changedfcn_cell', @() u2_setget(bg, 'SelectionChangedFcn', {@u1_log, 1}));
u2_chk('changedfcn_number', @() u2_setget(bg, 'SelectionChangedFcn', 5));
u2_chk('changedfcn_empty', @() u2_setget(bg, 'SelectionChangedFcn', ''));
bg.SelectionChangedFcn = @(s, e) u1_log('SelectionChangedFcn');
r1.Callback = @(s, e) u1_log('r1.Callback');
r2.Callback = @(s, e) u1_log('r2.Callback');
r1.Value = 1; drawnow; r2.Value = 1; drawnow; bg.SelectedObject = r1; drawnow; bg.SelectedObject = []; drawnow;
fprintf('CHK|callbacks_from_script_writes|%d|exact\n', numel(U1LOG));

% --- the panel half -----------------------------------------------------------------------------
u2_chk('title', @() u2_setget(bg, 'Title', 'Group'));
u2_chk('inner_titled', @() bg.InnerPosition);
u2_chk('enable_inactive', @() u2_setget(bg, 'Enable', 'inactive'));
u2_chk('enable_off', @() u2_setget(bg, 'Enable', 'off'));
fprintf('CHK|child_enable|%s|exact\n', r1.Enable);
u2_chk('background_none', @() u2_setget(bg, 'BackgroundColor', 'none'));
u2_chk('pixel_position', @() getpixelposition(bg));
u2_chk('findobj_type', @() numel(findobj(f, 'Type', 'uibuttongroup')));
u2_chk('findobj_panel', @() numel(findobj(f, 'Type', 'uipanel')));
u2_chk('findobj_style', @() numel(findobj(f, 'Style', 'radiobutton')));
u2_chk('ancestor', @() u3_tag(ancestor(r1, 'uibuttongroup')));
u2_chk('get_unknown', @() get(bg, 'Bogus'));
u2_err('set_unknown', @() set(bg, 'Bogus', 1));

% --- the delete order ---------------------------------------------------------------------------
U1LOG = {};
set(bg, 'DeleteFcn', @(s, e) u1_log('bg'));
set(r1, 'DeleteFcn', @(s, e) u1_log(['r1 selected=' u3_tag(bg.SelectedObject)]));
set(r2, 'DeleteFcn', @(s, e) u1_log('r2'));
bg.SelectedObject = r1;
delete(bg);
fprintf('CHK|delete_order|%s|exact\n', strjoin(U1LOG, ', '));

% --- in a uifigure ------------------------------------------------------------------------------
ubg = uibuttongroup(uf, 'Tag', 'ubg');
ua = uicontrol(ubg, 'Style', 'radiobutton', 'Tag', 'ua');
ub = uicontrol(ubg, 'Style', 'radiobutton', 'Tag', 'ub');
fprintf('CHK|uifigure_group|%s|exact\n', u3_state(ubg, [ua ub]));
ub.Value = 1; fprintf('CHK|uifigure_group_second|%s|exact\n', u3_state(ubg, [ua ub]));
delete(f); delete(uf);
