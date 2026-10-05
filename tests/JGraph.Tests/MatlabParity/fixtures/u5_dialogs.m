% record: -noFigureWindows
% U5 of the app-building plan (ADR 0202): the dialogs a uifigure lays over itself - uialert,
% uiconfirm, uiprogressdlg - with focus and uiaxes, where there is no display. An alert and a
% progress dialog want a figure that is shown; a confirmation wants somebody to answer it.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off');
b = uibutton(uf);

% --- on a figure that is not shown ---------------------------------------------------------------
u5_run('alert_invisible', @() uialert(uf, 'm', 't'));
u5_run('alert_no_arguments', @() uialert());
u5_run('alert_one_argument', @() uialert(uf));
u5_run('alert_two_arguments', @() uialert(uf, 'm'));
u5_run('alert_number', @() uialert(5.5, 'm', 't'));
u5_run('alert_classic_figure', @() uialert(f, 'm', 't'));
u5_run('alert_component', @() uialert(b, 'm', 't'));
u5_run('alert_invisible_bad_option', @() uialert(uf, 'm', 't', 'Bogus', 1));
u5_run('alert_invisible_bad_message', @() uialert(uf, 5, 't'));
u2_chk('confirm_invisible', @() uiconfirm(uf, 'm', 't'));
u2_chk('confirm_no_arguments', @() uiconfirm());
u2_chk('confirm_classic_figure', @() uiconfirm(f, 'm', 't'));
u2_chk('confirm_number', @() uiconfirm(5.5, 'm', 't'));
u2_chk('progress_invisible', @() uiprogressdlg(uf));
u2_chk('progress_no_arguments', @() uiprogressdlg());
u2_chk('progress_classic_figure', @() uiprogressdlg(f));
u2_chk('progress_number', @() uiprogressdlg(5.5));
u2_chk('progress_invisible_bad_option', @() uiprogressdlg(uf, 'Bogus', 1));
u5_run('focus_invisible_figure', @() focus(uf));
u5_run('focus_invisible_component', @() focus(b));
u5_run('focus_classic_figure', @() focus(f));
u5_run('focus_label', @() focus(uilabel(uf)));
u5_run('focus_axes', @() focus(uiaxes(uf)));
u5_run('focus_panel', @() focus(uipanel(uf)));
u5_run('focus_uicontrol', @() focus(uicontrol(f)));
u5_run('focus_no_arguments', @() focus());

% --- on a figure that is shown, as far as a script can tell --------------------------------------
vf = uifigure('Position', [100 100 400 300]);
u5_run('alert_shown', @() uialert(vf, 'm', 't'));
u5_run('alert_message_cell', @() uialert(vf, {'a', 'b'}, 't'));
u5_run('alert_message_strings', @() uialert(vf, ["a" "b"], "t"));
u5_run('alert_message_number', @() uialert(vf, 5, 't'));
u5_run('alert_message_empty', @() uialert(vf, '', 't'));
u5_run('alert_title_number', @() uialert(vf, 'm', 5));
u5_run('alert_title_cell', @() uialert(vf, 'm', {'t'}));
u5_run('alert_bad_option', @() uialert(vf, 'm', 't', 'Bogus', 1));
u5_run('alert_odd_options', @() uialert(vf, 'm', 't', 'Icon'));
icons = {'error', 'warning', 'info', 'success', 'question', 'none', '', 'bogus', 5, 'ERROR', 'err'};
for k = 1:numel(icons), u5_run(sprintf('alert_icon_%02d', k), @() uialert(vf, 'm', 't', 'Icon', icons{k})); end
u5_run('alert_modal_false', @() uialert(vf, 'm', 't', 'Modal', false));
u5_run('alert_modal_word', @() uialert(vf, 'm', 't', 'Modal', 'bogus'));
u5_run('alert_interpreter', @() uialert(vf, 'm', 't', 'Interpreter', 'html'));
u5_run('alert_interpreter_bad', @() uialert(vf, 'm', 't', 'Interpreter', 'bogus'));
u5_run('alert_closefcn_handle', @() uialert(vf, 'm', 't', 'CloseFcn', @(s, e) disp(1)));
u5_run('alert_closefcn_text', @() uialert(vf, 'm', 't', 'CloseFcn', 'disp(1)'));
u5_run('alert_closefcn_cell', @() uialert(vf, 'm', 't', 'CloseFcn', {@disp, 1}));
u5_run('alert_closefcn_number', @() uialert(vf, 'm', 't', 'CloseFcn', 5));
u5_run('alert_option_lower_case', @() uialert(vf, 'm', 't', 'icon', 'info'));
u5_run('alert_option_prefix', @() uialert(vf, 'm', 't', 'Ic', 'info'));
u5_run('alert_on_a_component', @() uialert(uibutton(vf), 'm', 't'));
u2_chk('confirm_shown', @() uiconfirm(vf, 'm', 't'));
u5_run('focus_shown_figure', @() focus(vf));
u5_run('focus_shown_component', @() focus(uibutton(vf)));

d = uiprogressdlg(vf);
names = {'Value', 'Message', 'Title', 'Indeterminate', 'Icon', 'ShowPercentage', 'Cancelable', 'CancelText', 'Interpreter', 'CancelRequested'};
for k = 1:numel(names), fprintf('CHK|progress_default_%s|%s|exact\n', names{k}, u5_text(get(d, names{k}))); end
values = {0.5, 1, 0, -0.1, 1.5, [0.1 0.2], 'a', NaN, int8(1), true, []};
for k = 1:numel(values), u5_try(sprintf('progress_value_%02d', k), d, 'Value', values{k}); end
texts = {'abc', "str", {'a', 'b'}, ["a"; "b"], 5, '', []};
for k = 1:numel(texts), u5_try(sprintf('progress_message_%d', k), d, 'Message', texts{k}); end
for k = 1:numel(texts), u5_try(sprintf('progress_title_%d', k), d, 'Title', texts{k}); end
for k = 1:numel(texts), u5_try(sprintf('progress_canceltext_%d', k), d, 'CancelText', texts{k}); end
states = {'on', 'off', true, 0, 'bogus', [1 0]};
flags = {'Indeterminate', 'Cancelable', 'ShowPercentage'};
for i = 1:3
    for k = 1:numel(states), u5_try(sprintf('progress_%s_%d', flags{i}, k), d, flags{i}, states{k}); end
end
for k = 1:numel(icons), u5_try(sprintf('progress_icon_%02d', k), d, 'Icon', icons{k}); end
u5_try('progress_interpreter', d, 'Interpreter', 'html');
u5_try('progress_interpreter_bad', d, 'Interpreter', 'bogus');
u5_try('progress_cancelrequested', d, 'CancelRequested', true);
d.Value = 0.25; d.Message = 'working';
fprintf('CHK|progress_dot|%s %s|exact\n', u5_text(d.Value), d.Message);
close(d);
u5_run('progress_value_after_close', @() set(d, 'Value', 0.3));
d = uiprogressdlg(vf, 'Title', 'T', 'Message', 'M', 'Value', 0.25, 'Cancelable', 'on', 'Indeterminate', 'on');
fprintf('CHK|progress_made_with_pairs|%s %s %s %s %s|exact\n', d.Title, d.Message, u5_text(d.Value), char(d.Cancelable), char(d.Indeterminate));
delete(d);
u2_chk('progress_odd', @() uiprogressdlg(vf, 'Title'));
u2_chk('progress_bad_option', @() uiprogressdlg(vf, 'Bogus', 1));
u2_chk('progress_bad_value', @() uiprogressdlg(vf, 'Value', 5));
delete(vf);

% --- uiaxes -------------------------------------------------------------------------------------
u2_chk('uiaxes_in_uifigure', @() u5_axes(uiaxes(uf)));
u2_chk('uiaxes_in_figure', @() u5_axes(uiaxes(f)));
u2_chk('uiaxes_in_panel', @() u5_axes(uiaxes(uipanel(uf))));
u2_chk('uiaxes_in_classic_panel', @() u5_axes(uiaxes(uipanel(f))));
u2_chk('uiaxes_in_group', @() u5_axes(uiaxes(uibuttongroup(uf))));
u2_chk('uiaxes_in_axes', @() u5_axes(uiaxes(axes(f))));
u2_chk('uiaxes_in_button', @() u5_axes(uiaxes(b)));
u2_chk('uiaxes_number', @() u5_axes(uiaxes(5.5)));
u2_chk('uiaxes_named_parent', @() u5_axes(uiaxes('Parent', uf)));
u2_chk('uiaxes_position', @() u5_axes(uiaxes(uf, 'Position', [20 30 200 150])));
u2_chk('uiaxes_unknown', @() u5_axes(uiaxes(uf, 'Bogus', 1)));
u2_chk('uiaxes_odd', @() u5_axes(uiaxes(uf, 'XLim')));
u2_chk('uiaxes_pairs', @() get(uiaxes(uf, 'XLim', [0 5]), 'XLim'));
u2_chk('uiaxes_struct', @() u5_axes(uiaxes(uf, struct('Tag', 's'))));
before = numel(findall(groot, 'Type', 'figure'));
alone = uiaxes();
owner = ancestor(alone, 'figure');
fprintf('CHK|uiaxes_alone|%s; %d new figure; a uifigure %d|exact\n', u5_axes(alone), numel(findall(groot, 'Type', 'figure')) - before, ...
    strcmp(get(owner, 'HandleVisibility'), 'off'));
delete(owner);
delete(allchild(uf)); delete(allchild(f));
ax = uiaxes(uf);
fprintf('CHK|uiaxes_nextplot|%s|exact\n', ax.NextPlot);
fprintf('CHK|uiaxes_backgroundcolor|%s|exact\n', u5_text(ax.BackgroundColor));
ax.BackgroundColor = [1 0 0];
fprintf('CHK|uiaxes_backgroundcolor_set|%s|exact\n', u5_text(ax.BackgroundColor));
ax.BackgroundColor = 'none';
fprintf('CHK|uiaxes_backgroundcolor_none|%s|exact\n', u5_text(ax.BackgroundColor));
u5_rect('uiaxes_outer', ax.OuterPosition, 0.01);
ax.Position = [50 60 300 200];
u5_rect('uiaxes_position_set', ax.Position, 0.01);
u5_rect('uiaxes_position_set_outer', ax.OuterPosition, 0.01);
title(ax, 'T'); xlabel(ax, 'X');
plot(ax, 1:3, [2 4 3]);
fprintf('CHK|uiaxes_after_plot|%d [%s] [%s]|exact\n', numel(ax.Children), ax.Title.String, ax.XLabel.String);
plot(ax, 1:5);
fprintf('CHK|uiaxes_after_second_plot|%d [%s] %d|exact\n', numel(ax.Children), ax.Title.String, numel(ax.Children(1).YData));
hold(ax, 'on'); plot(ax, 5:-1:1);
fprintf('CHK|uiaxes_hold|%d %s|exact\n', numel(ax.Children), ax.NextPlot);
figures = numel(findall(groot, 'Type', 'figure'));
other = gca;
fprintf('CHK|uiaxes_is_never_gca|%d %d|exact\n', other == ax, numel(findall(groot, 'Type', 'figure')) - figures);
b2 = uibutton(uf, 'Tag', 'b2'); set(ax, 'Tag', 'ax'); ax2 = uiaxes(uf, 'Tag', 'ax2');
fprintf('CHK|uiaxes_children_order|%s|exact\n', u2_tags(uf.Children));
cla(ax);
fprintf('CHK|uiaxes_cla|%d|exact\n', numel(ax.Children));
delete(uf); delete(f);
