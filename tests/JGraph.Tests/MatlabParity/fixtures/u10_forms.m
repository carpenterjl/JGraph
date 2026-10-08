% record: -noFigureWindows
% U10 of the app-building plan (ADR 0209): a custom component's constructor - its parent and
% name-value forms and their refusals - a setup or update that fails, children only in setup,
% reparenting, deletion, a class's own constructor, and research C's SpinnerGauge. Probes
% u10_matrix, u10_more.
f = uifigure('Visible', 'off');
u10_log();

% --- the forms
d = U10Probe('Parent', f, 'Value', 3);
u9b_chk('parent_pair', @() [d.Parent == f, d.Value]);
u9b_chk('parent_pair_log', @() u10_log());
u9b_chk('positional_value', @() U10Probe(f, 3));
u9b_chk('positional_value_log', @() u10_log());
u9b_chk('bogus_pair', @() U10Probe(f, 'Bogus', 1));
u9b_chk('bogus_pair_log', @() u10_log());
d = U10Probe(f, 'Position', [1 2 30 40]);
u9b_chk('position_pair', @() d.Position);
u10_log();
d = U10Probe(f, 'Units', 'normalized', 'Position', [0 0 .5 .5]);
u9b_chk('normalized', @() d.Position);
u9b_chk('normalized_pixels', @() getpixelposition(d));
u9b_chk('private_pair', @() U10Probe(f, 'SetupCalls', 4));
u9b_chk('private_access_pair', @() U10Probe(f, 'Grid', 4));
u9b_chk('lower_case_pair', @() get(U10Probe(f, 'value', 4), 'Value'));
u9b_chk('partial_pair', @() U10Probe(f, 'Val', 4));
u9b_chk('string_pair', @() get(U10Probe(f, "Value", 4), 'Value'));
u9b_chk('struct_pair', @() get(U10Probe(f, struct('Value', 8)), 'Value'));
u9b_chk('odd_pairs', @() U10Probe(f, 'Value'));
u9b_chk('bad_value_pair', @() U10Probe(f, 'Value', 'abc'));
u9b_chk('bad_position_pair', @() U10Probe(f, 'Position', 'abc'));
u9b_chk('bad_callback_pair', @() U10Probe(f, 'ValueChangedFcn', 5));
u9b_chkdiv('type_pair', @() get(U10Probe(f, 'Type', 'x'), 'Type'), '0209');
u9b_chk('number_parent', @() U10Probe(5));
u10_log();
d = U10Probe('Value', 2);
u9b_chk('no_parent_type', @() d.Parent.Type);
u9b_chk('no_parent_value', @() d.Value);
delete(d.Parent);
u10_log();
cf = figure('Visible', 'off');
d = U10Probe(cf);
u9b_chk('classic_figure', @() d.Parent.Type);
delete(cf);
d = U10Probe(uipanel(f));
u9b_chk('in_panel', @() d.Parent.Type);
g = uigridlayout(f, [2 2]);
d = U10Probe(g);
d.Layout.Row = 2; d.Layout.Column = [1 2];
u9b_chk('in_grid', @() [d.Layout.Row d.Layout.Column]);
delete(g);
u9b_chk('axes_parent', @() U10Probe(uiaxes(f)));
c = U10Probe(f);
u9b_chk('component_parent', @() U10Probe(c));
u9b_chk('abstract_half', @() U10Half(f));
u9b_chk('abstract_base', @() matlab.ui.componentcontainer.ComponentContainer(f));
u9b_chk('plain_class', @() U10Plain());

% --- a class's own constructor
drawnow; u10_log();
d = U10Ctor('ex', f);
u9b_chk('own_ctor_log', @() u10_log());
drawnow;
u9b_chk('own_ctor_drawnow', @() u10_log());
drawnow;
u9b_chk('own_ctor_drawnow_again', @() u10_log());
u9b_chk('own_ctor_count', @() d.Count);

% --- children: only in setup
u9b_chk('child_after', @() uibutton(c));
u9b_chk('child_parent_pair', @() uibutton(f, 'Parent', c));
b = uibutton(f);
u9b_chk('reparent_into', @() u10_set(b, 'Parent', c));
uilabel(c.grid());
u9b_chk('grid_child_after', @() numel(c.grid().Children));
drawnow; u10_log();
late = U10Late(f);
drawnow;
u9b_chk('child_in_update', @() u10_log());

% --- reparenting
f2 = uifigure('Visible', 'off');
drawnow; u10_log();
c.Parent = f2;
u9b_chk('reparent', @() [c.Parent == f2, numel(f2.Children)]);
u9b_chk('reparent_log', @() u10_log());
drawnow;
u9b_chk('reparent_drawnow_log', @() u10_log());

% --- failures
drawnow; pause(0.2); drawnow; u10_log();
u10_mode('setup');
n0 = numel(f.Children);
u9b_chk('setup_error', @() U10Bad(f));
u9b_chk('setup_error_log', @() u10_log());
u9b_chk('setup_error_children', @() numel(f.Children) - n0);
u10_mode('update');
d = U10Bad(f);
u10_log();
u9b_chk('update_error', @() u10_do(@() drawnow));
u9b_chk('update_error_log', @() u10_log());
drawnow;
u9b_chk('update_error_again_log', @() u10_log());
d.Mode = 'x';
drawnow;
u9b_chk('update_error_write_log', @() u10_log());
u10_mode('none');
d.Mode = 'y';
drawnow;
u9b_chk('update_recovered_log', @() u10_log());

% --- deletion
d = U10Probe(f);
gg = d.grid();
u10_log();
delete(d);
u9b_chk('delete', @() {u10_log(), isvalid(d), isvalid(gg)});
u9b_chk('dot_after_delete', @() d.Value);
d = U10Probe(f);
gg = d.grid();
delete(gg);
u9b_chk('delete_grid', @() [isvalid(d), numel(d.Children)]);
d = U10Probe(f);
f.Children(1).Value = 11;
u9b_chk('write_through_child_handle', @() d.Value);
u9b_chk('read_through_child_handle', @() f.Children(1).Value);
delete(f);
u10_log();
u9b_chk('figure_delete', @() isvalid(d));
delete(f2);
u9b_chk('figure_delete_log', @() u10_log());

% --- research C's SpinnerGauge
f = uifigure('Visible', 'off');
s = SpinnerGauge(f, 'Value', 40);
u9b_chk('sg_ctor', @() [s.SetupCalls s.UpdateCalls]);
drawnow;
u9b_chk('sg_drawnow', @() [s.SetupCalls s.UpdateCalls]);
s.Value = 50; s.Value = 60;
drawnow;
u9b_chk('sg_two_sets', @() s.UpdateCalls);
u9b_chk('sg_position', @() s.Position);
u9b_chk('sg_type', @() s.Type);
s.Limits = [0 200];
drawnow;
u9b_chk('sg_limits', @() s.Limits);
u9b_chk('sg_cb_default', @() s.ValueChangedFcn);
s.ValueChangedFcn = @(src, evt) disp(evt.EventName);
u9b_chk('sg_cb', @() class(s.ValueChangedFcn));
delete(f);
u9b_chk('sg_after_delete', @() isvalid(s));
