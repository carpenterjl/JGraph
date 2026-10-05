% record: -noFigureWindows
% U7 of the app-building plan (ADR 0204): what a graphics handle is. isa answers R2025b's class of
% every object and each of its superclasses, a property typed with a graphics class takes the
% handles of that class and refuses the rest, and a graphics object raises ObjectBeingDestroyed
% for a listener as it goes.
global vlog_text
vlog_text = '';
f = figure('Visible', 'off');
ax = axes(f);
ln = plot(ax, 1:3);
tx = text(ax, 0, 0, 'a');
uc = uicontrol(f);
uf = uifigure('Visible', 'off');
g = uigridlayout(uf);
b = uibutton(g);
lbl = uilabel(g);
ef = uieditfield(g);
nf = uieditfield(g, 'numeric');
dd = uidropdown(g);
cb = uicheckbox(g);
sl = uislider(g);
uax = uiaxes(g);
p = uipanel(uf);
bg = uibuttongroup(uf);
rb = uiradiobutton(bg);

% --- isa -----------------------------------------------------------------------------------------
names = {'handle', 'matlab.graphics.Graphics', 'matlab.mixin.SetGet', 'dynamicprops', ...
    'matlab.ui.Figure', 'matlab.graphics.axis.Axes', 'matlab.graphics.axis.AbstractAxes', ...
    'matlab.ui.control.UIAxes', 'matlab.graphics.chart.primitive.Line', 'matlab.graphics.primitive.Text', ...
    'matlab.ui.control.UIControl', 'matlab.ui.container.GridLayout', 'matlab.ui.container.Panel', ...
    'matlab.ui.container.ButtonGroup', 'matlab.ui.control.Button', 'matlab.ui.control.Label', ...
    'matlab.ui.control.EditField', 'matlab.ui.control.NumericEditField', 'matlab.ui.control.DropDown', ...
    'matlab.ui.control.CheckBox', 'matlab.ui.control.Slider', 'matlab.ui.control.RadioButton', ...
    'matlab.ui.Root', 'Figure', 'figure', 'matlab.ui.figure', 'struct', 'char'};
objs = {'figure', f; 'axes', ax; 'line', ln; 'text', tx; 'uicontrol', uc; 'uifigure', uf; 'grid', g; ...
    'button', b; 'label', lbl; 'editfield', ef; 'numericfield', nf; 'dropdown', dd; 'checkbox', cb; ...
    'slider', sl; 'uiaxes', uax; 'panel', p; 'buttongroup', bg; 'radio', rb; 'root', groot};
for k = 1:size(objs, 1)
    h = objs{k, 2};
    fprintf('CHK|isa_%s|%s|exact\n', objs{k, 1}, sprintf('%d', cellfun(@(n) isa(h, n), names)));
end
u2_chk('isa_number', @() isa(5.5, 'matlab.ui.Figure'));
u2_chk('isa_empty', @() isa([], 'matlab.ui.Figure'));
u2_chk('isa_char', @() isa('a', 'matlab.ui.control.Button'));
u2_chk('isa_struct', @() isa(struct('a', 1), 'matlab.graphics.Graphics'));
fprintf('CHK|isa_gobjects|%d|div=ADR0204\n',isa(gobjects(1), 'matlab.graphics.Graphics'));
fprintf('CHK|isa_gobjects_handle|%d|div=ADR0204\n',isa(gobjects(1), 'handle'));
u2_chk('isa_two_figures', @() isa([f uf], 'matlab.ui.Figure'));
u2_chk('isa_two_buttons', @() isa([b uibutton(g)], 'matlab.ui.control.Button'));
u2_chk('isgraphics_button', @() isgraphics(b));
u2_chk('isgraphics_button_type', @() isgraphics(b, 'uibutton'));
u2_chk('ishghandle_figure', @() ishghandle(uf));
u2_chk('isvalid_button', @() isvalid(b));
fprintf('CHK|class_figure|%s|div=ADR0204\n', class(f));
fprintf('CHK|class_button|%s|div=ADR0204\n', class(b));
fprintf('CHK|isa_figure_double|%d|div=ADR0204\n', isa(f, 'double'));
fprintf('CHK|isa_one_is_figure|%d|div=ADR0204\n', isa(double(f.Number), 'matlab.ui.Figure'));
fprintf('CHK|isobject_button|%d|div=ADR0204\n', isobject(b));

% --- a property typed with a graphics class ------------------------------------------------------
t = U7Typed;
u2_chk('typed_default_isempty', @() isempty(t.Fig));
u2_chk('typed_default_size', @() size(t.Fig));
u2_chk('typed_default_numel', @() numel(t.Btn));
u2_chk('typed_default_number', @() t.Num);
fprintf('CHK|typed_default_isa|%d|div=ADR0204\n', isa(t.Fig, 'matlab.ui.Figure'));
u5_run('typed_delete_empty', @() delete(t.Fig));
u5_run('typed_figure_uifigure', @() u6_setp(t, 'Fig', uf));
u2_chk('typed_figure_held', @() isequal(t.Fig, uf));
u5_run('typed_figure_classic', @() u6_setp(t, 'Fig', f));
u2_chk('typed_figure_classic_held', @() t.Fig.Number);
u5_run('typed_figure_button', @() u6_setp(t, 'Fig', b));
u5_run('typed_figure_number', @() u6_setp(t, 'Fig', 5.5));
u5_run('typed_figure_text', @() u6_setp(t, 'Fig', 'a'));
u5_run('typed_figure_cell', @() u6_setp(t, 'Fig', {f}));
u2_chk('typed_figure_kept', @() isequal(t.Fig, f));
fprintf('CHK|typed_figure_empty|%s|div=ADR0204\n',u7_id(@() u6_setp(t, 'Fig', [])));
fprintf('CHK|typed_figure_emptied|%d|div=ADR0204\n',isempty(t.Fig));
t.Fig = f;
u5_run('typed_figure_two', @() u6_setp(t, 'Fig', [f uf]));
u2_chk('typed_figure_two_size', @() size(t.Fig));
u5_run('typed_button', @() u6_setp(t, 'Btn', b));
u2_chk('typed_button_held', @() t.Btn.Text);
u5_run('typed_button_label', @() u6_setp(t, 'Btn', lbl));
u5_run('typed_button_figure', @() u6_setp(t, 'Btn', uf));
u5_run('typed_uiaxes', @() u6_setp(t, 'Ax', uax));
u5_run('typed_uiaxes_axes', @() u6_setp(t, 'Ax', ax));
u5_run('typed_axes_uiaxes', @() u6_setp(t, 'AnyAx', uax));
u5_run('typed_axes_axes', @() u6_setp(t, 'AnyAx', ax));
u5_run('typed_axes_line', @() u6_setp(t, 'AnyAx', ln));
u5_run('typed_panel', @() u6_setp(t, 'Pan', p));
u5_run('typed_panel_buttongroup', @() u6_setp(t, 'Pan', bg));
u5_run('typed_panel_grid', @() u6_setp(t, 'Pan', g));
u5_run('typed_graphics_line', @() u6_setp(t, 'Any', ln));
u5_run('typed_graphics_button', @() u6_setp(t, 'Any', b));
u5_run('typed_graphics_number', @() u6_setp(t, 'Any', 5.5));
u5_run('typed_graphics_object', @() u6_setp(t, 'Any', U7Typed));
u5_run('typed_line', @() u6_setp(t, 'Ln', ln));
u5_run('typed_line_text', @() u6_setp(t, 'Ln', tx));
u2_chk('typed_line_held', @() t.Ln.LineStyle);
t.Btn.Text = 'through';
u2_chk('typed_write_through', @() b.Text);
t.Ax.XLim = [0 5];
u2_chk('typed_write_through_axes', @() uax.XLim);

% --- ObjectBeingDestroyed on a graphics handle ---------------------------------------------------
u6_log();
uf2 = uifigure('Visible', 'off');
p2 = uipanel(uf2);
b2 = uibutton(p2);
L = addlistener(b2, 'ObjectBeingDestroyed', @(s, e) u7_note(sprintf('button %s %s %d', class(e), e.EventName, isvalid(s))));
u2_chk('listener_class', @() class(L));
u2_chk('listener_valid', @() isvalid(L));
b2.DeleteFcn = @(s, e) u7_note('button deletefcn');
addlistener(p2, 'ObjectBeingDestroyed', @(s, e) u7_note('panel'));
p2.DeleteFcn = @(s, e) u7_note('panel deletefcn');
addlistener(uf2, 'ObjectBeingDestroyed', @(s, e) u7_note('figure'));
addlistener(uf2, 'ObjectBeingDestroyed', @(s, e) u7_note('figure again'));
uf2.DeleteFcn = @(s, e) u7_note('figure deletefcn');
delete(uf2);
u2_chk('destroy_order', @() u6_log());
u2_chk('listener_valid_after', @() isvalid(L));

uf3 = uifigure('Visible', 'off');
b3 = uibutton(uf3);
L3 = addlistener(b3, 'ObjectBeingDestroyed', @(s, e) u7_note('removed'));
delete(L3);
L4 = addlistener(b3, 'ObjectBeingDestroyed', @(s, e) u7_note('disabled'));
L4.Enabled = false;
L5 = listener(b3, 'ObjectBeingDestroyed', @(s, e) u7_note('listener'));
delete(b3);
u2_chk('destroy_removed', @() u6_log());
clear L5
b4 = uibutton(uf3);
L6 = listener(b4, 'ObjectBeingDestroyed', @(s, e) u7_note('dropped'));
clear L6
delete(b4);
u2_chk('destroy_dropped_listener', @() u6_log());
u5_run('listener_unknown_event', @() addlistener(uf3, 'Bogus', @(s, e) 1));
u5_run('listener_dead_handle', @() addlistener(b3, 'ObjectBeingDestroyed', @(s, e) 1));
u5_run('listener_number', @() addlistener(5.5, 'ObjectBeingDestroyed', @(s, e) 1));

f5 = figure('Visible', 'off');
ax5 = axes(f5);
ln5 = plot(ax5, 1:3);
addlistener(ln5, 'ObjectBeingDestroyed', @(s, e) u7_note('line'));
addlistener(ax5, 'ObjectBeingDestroyed', @(s, e) u7_note('axes'));
addlistener(f5, 'ObjectBeingDestroyed', @(s, e) u7_note('figure'));
cla(ax5);
u2_chk('destroy_cla', @() u6_log());
close(f5);
u2_chk('destroy_close', @() u6_log());
addlistener(uf3, 'ObjectBeingDestroyed', @(s, e) u7_note('all'));
delete(findall(groot, 'Type', 'figure'));
u2_chk('destroy_all', @() u6_log());
