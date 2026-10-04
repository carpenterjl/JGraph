% record: -noFigureWindows
% U4 of the app-building plan (ADR 0201): uiwait, uiresume, the figure's WaitStatus and waitfor,
% with no window. R2025b warns that uiwait has no display to wait in and waits all the same: a
% timeout, a timer or a deletion ends it. Times are checked as ranges, never as numbers.
f = figure('Visible', 'off');
c = uicontrol(f, 'Style', 'checkbox');
warm = timer('StartDelay', 0.1, 'TimerFcn', @(~, ~) 0); start(warm); pause(0.5); delete(warm);

% --- WaitStatus ---------------------------------------------------------------------------------
u2_chk('status_default', @() get(f, 'WaitStatus'));
fprintf('CHK|status_listed|%d %d|exact\n', isfield(get(f), 'WaitStatus'), isfield(set(f), 'WaitStatus'));
u2_err('status_set_waiting', @() set(f, 'WaitStatus', 'waiting'));
u2_chk('status_waiting', @() get(f, 'WaitStatus'));
u2_err('status_set_inactive', @() set(f, 'WaitStatus', 'inactive'));
u2_chk('status_inactive', @() get(f, 'WaitStatus'));
u2_err('status_set_bad', @() set(f, 'WaitStatus', 'bad'));
u2_err('status_set_number', @() set(f, 'WaitStatus', 1));
u2_err('status_set_empty', @() set(f, 'WaitStatus', ''));
u2_err('status_set_upper', @() set(f, 'WaitStatus', 'WAITING'));
u2_chk('status_upper', @() get(f, 'WaitStatus'));
u2_err('status_set_short', @() set(f, 'WaitStatus', 'in'));
u2_chk('status_short', @() get(f, 'WaitStatus'));
u2_err('status_on_control', @() get(c, 'WaitStatus'));

% --- the arguments ------------------------------------------------------------------------------
u2_err('uiwait_not_a_handle', @() uiwait(5.5));
u2_err('uiwait_control', @() uiwait(c));
u2_err('uiwait_two_figures', @() uiwait([f f]));
u2_err('uiwait_text', @() uiwait('a'));
u2_err('uiwait_text_timeout', @() uiwait(f, 'a'));
u2_err('uiwait_logical_timeout', @() uiwait(f, true));
u2_err('uiwait_vector_timeout', @() uiwait(f, [1 2]));
u2_err('uiwait_empty_timeout', @() uiwait(f, []));
u2_err('uiwait_nan_timeout', @() uiwait(f, NaN));
u2_err('uiwait_three', @() uiwait(f, 1, 2));
u2_err('uiresume_not_a_handle', @() uiresume(5.5));
u2_err('uiresume_control', @() uiresume(c));
u2_err('uiresume_idle', @() uiresume(f));
u2_err('uiresume_two', @() uiresume(f, 1));
u2_err('uiresume_two_figures', @() uiresume([f f]));

% --- a timeout ----------------------------------------------------------------------------------
set(f, 'Visible', 'off');
lastwarn('');
t = tic; uiwait(f, 1); e = toc(t);
[~, id] = lastwarn;
fprintf('CHK|timeout_time|%d|exact\n', e >= 0.9 && e < 6);
fprintf('CHK|timeout_status|%s|exact\n', get(f, 'WaitStatus'));
fprintf('CHK|timeout_shows_figure|%s|exact\n', char(get(f, 'Visible')));
fprintf('CHK|timeout_warning|%s|exact\n', id);
lastwarn('');
t = tic; uiwait(f, 0.2); e = toc(t);
[msg, id] = lastwarn;
fprintf('CHK|short_timeout_time|%d|exact\n', e >= 0.9 && e < 6);
fprintf('CHK|short_timeout_warning|%s|exact\n', id);
fprintf('CHK|short_timeout_message|%s|exact\n', msg);
t = tic; uiwait(f, single(1)); e = toc(t);
fprintf('CHK|single_timeout_time|%d|exact\n', e >= 0.9 && e < 6);

% --- what ends a wait ---------------------------------------------------------------------------
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume(f)); start(tm);
t = tic; uiwait(f); e = toc(t); delete(tm);
fprintf('CHK|ended_by_uiresume|%d %s|exact\n', e >= 0.4 && e < 6, get(f, 'WaitStatus'));
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'WaitStatus', 'inactive')); start(tm);
t = tic; uiwait(f); e = toc(t); delete(tm);
fprintf('CHK|ended_by_status|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume(f)); start(tm);
t = tic; uiwait(f, 30); e = toc(t); delete(tm);
fprintf('CHK|resumed_before_timeout|%d|exact\n', e >= 0.4 && e < 6);
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(g)); start(tm);
t = tic; uiwait(g); e = toc(t); delete(tm);
fprintf('CHK|ended_by_delete|%d %d|exact\n', e >= 0.4 && e < 6, ishghandle(g));
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) close(g)); start(tm);
t = tic; uiwait(g); e = toc(t); delete(tm);
fprintf('CHK|ended_by_close|%d %d|exact\n', e >= 0.4 && e < 6, ishghandle(g));
g = figure('Visible', 'off');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) fprintf('CHK|status_while_waiting|%s|exact\n', get(g, 'WaitStatus'))); start(tm);
uiwait(g, 1); delete(tm);
figure(g);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) uiresume); start(tm);
t = tic; uiwait(g, 30); e = toc(t); delete(tm);
fprintf('CHK|bare_uiresume_is_gcf|%d|exact\n', e >= 0.4 && e < 6);
delete(g);

% --- waitfor ------------------------------------------------------------------------------------
u2_err('waitfor_none', @() waitfor());
u2_err('waitfor_not_a_handle', @() waitfor(5.5));
u2_err('waitfor_text', @() waitfor('a'));
u2_err('waitfor_no_such_property', @() waitfor(f, 'NoSuch'));
u2_err('waitfor_number_property', @() waitfor(f, 5));
u2_err('waitfor_four', @() waitfor(f, 'Name', 'x', 4));
d = figure('Visible', 'off'); delete(d);
u2_err('waitfor_deleted', @() waitfor(d));
u2_err('waitfor_deleted_property', @() waitfor(d, 'Name'));
set(f, 'Name', '');
t = tic; waitfor(f, 'Name', ''); e = toc(t);
fprintf('CHK|waitfor_already_equal|%d|exact\n', e < 0.4);
t = tic; waitfor(c, 'Value', 0); e = toc(t);
fprintf('CHK|waitfor_value_already|%d|exact\n', e < 0.4);
t = tic; waitfor(c, 'Value', false); e = toc(t);
fprintf('CHK|waitfor_value_logical|%d|exact\n', e < 0.4);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(c, 'Value', 1)); start(tm);
t = tic; waitfor(c, 'Value', 1); e = toc(t); delete(tm);
fprintf('CHK|waitfor_value_reached|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(c, 'Value', 0)); start(tm);
t = tic; waitfor(c, 'Value'); e = toc(t); delete(tm);
fprintf('CHK|waitfor_change|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.4, 'TimerFcn', @(~, ~) set(c, 'Value', 0)); start(tm);
tm2 = timer('StartDelay', 1.2, 'TimerFcn', @(~, ~) set(c, 'Value', 1)); start(tm2);
t = tic; waitfor(c, 'Value'); e = toc(t); delete(tm); delete(tm2);
fprintf('CHK|waitfor_same_value_is_no_change|%d|exact\n', e >= 1.1 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'Name', 'abc')); start(tm);
t = tic; waitfor(f, 'name', 'abc'); e = toc(t); delete(tm);
fprintf('CHK|waitfor_name_any_case|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.4, 'TimerFcn', @(~, ~) set(f, 'Name', 'ABCD')); start(tm);
tm2 = timer('StartDelay', 1.2, 'TimerFcn', @(~, ~) set(f, 'Name', 'abcd')); start(tm2);
t = tic; waitfor(f, 'Name', 'abcd'); e = toc(t); delete(tm); delete(tm2);
fprintf('CHK|waitfor_text_is_exact|%d|exact\n', e >= 1.1 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'UserData', struct('a', 1))); start(tm);
t = tic; waitfor(f, 'UserData'); e = toc(t); delete(tm);
fprintf('CHK|waitfor_userdata_change|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) set(f, 'UserData', [1 2 3])); start(tm);
t = tic; waitfor(f, 'UserData', [1 2 3]); e = toc(t); delete(tm);
fprintf('CHK|waitfor_userdata_value|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(c)); start(tm);
t = tic; waitfor(c, 'Value', 5); e = toc(t); delete(tm);
fprintf('CHK|waitfor_ended_by_delete|%d|exact\n', e >= 0.4 && e < 6);
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(f)); start(tm);
t = tic; waitfor(f); e = toc(t); delete(tm);
fprintf('CHK|waitfor_figure_deleted|%d %d|exact\n', e >= 0.4 && e < 6, ishghandle(f));
