% timer_callbacks.m -- V6 of the value-ownership plan: timers (appendix A #105). A timer is a handle
% whose callbacks run on the script thread at a drain point (start, stop, wait, pause, a statement
% boundary), never inside a statement; the a105 lines in value_isolation_lifetime hold the ownership
% questions, these hold the forms around them: callback order, the event, the execution modes, the
% properties and their refusals, delete and isvalid. Figure-free, with waits generous enough for
% either engine. Every case deletes its timer, and a case that expects an error raises it before
% start, so no timer outlives its case.

run_case('h_timer_start_fcn_order', @h_timer_start_fcn_order);
run_case('h_timer_startdelay_zero_order', @h_timer_startdelay_zero_order);
run_case('h_timer_stop_fcn_sync', @h_timer_stop_fcn_sync);
run_case('h_timer_stop_stopped_runs_stopfcn', @h_timer_stop_stopped_runs_stopfcn);
run_case('h_timer_pause_drains', @h_timer_pause_drains);
run_case('h_timer_busy_loop_fires_between_statements', @h_timer_busy_loop_fires_between_statements);
run_case('h_timer_two_timers_order', @h_timer_two_timers_order);
run_case('h_timer_event_fields', @h_timer_event_fields);
run_case('h_timer_event_data', @h_timer_event_data);
run_case('h_timer_timerfcn_gets_timer', @h_timer_timerfcn_gets_timer);
run_case('h_timer_string_callback', @h_timer_string_callback);
run_case('h_timer_cell_callback', @h_timer_cell_callback);
run_case('h_timer_capture_anon', @h_timer_capture_anon);
run_case('h_timer_single_shot_ignores_tasks', @h_timer_single_shot_ignores_tasks);
run_case('h_timer_fixed_rate_stop_after_last', @h_timer_fixed_rate_stop_after_last);
run_case('h_timer_fixed_delay_mode', @h_timer_fixed_delay_mode);
run_case('h_timer_period_then_start', @h_timer_period_then_start);
run_case('h_timer_restart_counts', @h_timer_restart_counts);
run_case('h_timer_tasks_reset_on_restart', @h_timer_tasks_reset_on_restart);
run_case('h_timer_running_states', @h_timer_running_states);
run_case('h_timer_stop_inside_timerfcn', @h_timer_stop_inside_timerfcn);
run_case('h_timer_delete_in_timerfcn', @h_timer_delete_in_timerfcn);
run_case('h_timer_error_stops', @h_timer_error_stops);
run_case('h_timer_error_no_errorfcn', @h_timer_error_no_errorfcn);
run_case('h_timer_defaults', @h_timer_defaults);
run_case('h_timer_userdata_default', @h_timer_userdata_default);
run_case('h_timer_alias_write', @h_timer_alias_write);
run_case('h_timer_userdata_share_from_callback', @h_timer_userdata_share_from_callback);
run_case('h_timer_ctor_case_insensitive', @h_timer_ctor_case_insensitive);
run_case('h_timer_isvalid_class', @h_timer_isvalid_class);
run_case('h_timer_wait_not_started', @h_timer_wait_not_started);
run_case('h_timer_wait_infinite_refused', @h_timer_wait_infinite_refused);
run_case('h_timer_start_running', @h_timer_start_running);
run_case('h_timer_start_no_timerfcn', @h_timer_start_no_timerfcn);
run_case('h_timer_period_while_running', @h_timer_period_while_running);
run_case('h_timer_bad_prop_ctor', @h_timer_bad_prop_ctor);
run_case('h_timer_bad_prop_write', @h_timer_bad_prop_write);
run_case('h_timer_unknown_read', @h_timer_unknown_read);
run_case('h_timer_bad_mode', @h_timer_bad_mode);
run_case('h_timer_period_negative', @h_timer_period_negative);
run_case('h_timer_startdelay_negative', @h_timer_startdelay_negative);
run_case('h_timer_tasks_zero', @h_timer_tasks_zero);
run_case('h_timer_timerfcn_not_callable', @h_timer_timerfcn_not_callable);
run_case('h_timer_tasks_executed_readonly', @h_timer_tasks_executed_readonly);
run_case('h_timer_running_readonly', @h_timer_running_readonly);
run_case('h_timer_delete_running', @h_timer_delete_running);
run_case('h_timer_delete_stopped_no_stopfcn', @h_timer_delete_stopped_no_stopfcn);
run_case('h_timer_delete_twice', @h_timer_delete_twice);
run_case('h_timer_deleted_prop_read', @h_timer_deleted_prop_read);
run_case('h_timer_deleted_prop_write', @h_timer_deleted_prop_write);
run_case('h_timer_deleted_start', @h_timer_deleted_start);
run_case('h_timer_deleted_stop', @h_timer_deleted_stop);
run_case('h_timer_deleted_wait', @h_timer_deleted_wait);

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

% --- when a callback runs ------------------------------------------------------------------------

function s = h_timer_start_fcn_order()
t = timer('StartFcn', @(~, ~) vlog('start'), 'TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.05);
start(t);
vlog('after_start');
wait(t);
vlog('after_wait');
delete(t);
s = logged_text();
end

function s = h_timer_startdelay_zero_order()
t = timer('StartFcn', @(~, ~) vlog('start'), 'TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'));
start(t);
vlog('after_start');
wait(t);
delete(t);
s = logged_text();
end

function s = h_timer_stop_fcn_sync()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
start(t);
stop(t);
vlog('after_stop');
delete(t);
s = logged_text();
end

function s = h_timer_stop_stopped_runs_stopfcn()
t = timer('StopFcn', @(~, ~) vlog('stop'));
stop(t);
s = ['[' logged_text() ']'];
delete(t);
end

function s = h_timer_pause_drains()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartDelay', 0.01);
start(t);
pause(0.2);
vlog('after_pause');
wait(t);
delete(t);
s = logged_text();
end

function s = h_timer_busy_loop_fires_between_statements()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartDelay', 0.001);
start(t);
x = 0;
for i = 1:1e7
    x = x + i;
end
vlog('loop_done');
wait(t);
delete(t);
s = logged_text();
end

function s = h_timer_two_timers_order()
a = timer('TimerFcn', @(~, ~) vlog('a'), 'StartDelay', 0.08);
b = timer('TimerFcn', @(~, ~) vlog('b'), 'StartDelay', 0.01);
start(a);
start(b);
wait(a);
wait(b);
delete(a);
delete(b);
s = logged_text();
end

% --- the event and the callback forms ------------------------------------------------------------

function s = h_timer_event_fields()
t = timer('TimerFcn', @(~, e) vlog(strjoin(fieldnames(e)', ',')), 'StartDelay', 0.01);
start(t);
wait(t);
delete(t);
s = logged_text();
end

function s = h_timer_event_data()
t = timer('TimerFcn', @(~, e) vlog([strjoin(fieldnames(e.Data)', ',') ':' e.Type ':' class(e.Data.time) ':' mat2str(size(e.Data.time))]), 'StartDelay', 0.01);
start(t);
wait(t);
delete(t);
s = logged_text();
end

function s = h_timer_timerfcn_gets_timer()
t = timer('TimerFcn', @(src, ~) vlog([class(src) ':' src.Tag ':' src.Running]), 'StartDelay', 0.01, 'Tag', 'tg');
start(t);
wait(t);
s = logged_text();
delete(t);
end

function s = h_timer_string_callback()
t = timer('TimerFcn', 'vlog(''str'')', 'StartDelay', 0.01);
start(t);
wait(t);
delete(t);
s = logged_text();
end

function cell_target(~, e, arg)
vlog([arg ':' e.Type]);
end

function s = h_timer_cell_callback()
t = timer('TimerFcn', {@cell_target, 'cellarg'}, 'StartDelay', 0.01);
start(t);
wait(t);
delete(t);
s = logged_text();
end

function s = h_timer_capture_anon()
c = {1, 2};
t = timer('TimerFcn', @(~, ~) vlog(sprintf('%d', c{1})), 'StartDelay', 0.01);
c{1} = 9;
start(t);
wait(t);
delete(t);
s = logged_text();
end

% --- execution modes and counts ------------------------------------------------------------------

function s = h_timer_single_shot_ignores_tasks()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'TasksToExecute', 3, 'StartDelay', 0.01);
start(t);
wait(t);
s = sprintf('%s %d', logged_text(), t.TasksExecuted);
delete(t);
end

function s = h_timer_fixed_rate_stop_after_last()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 3, 'StartDelay', 0.01);
start(t);
wait(t);
vlog('after_wait');
s = sprintf('%s %d %s', logged_text(), t.TasksExecuted, t.Running);
delete(t);
end

function s = h_timer_fixed_delay_mode()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'ExecutionMode', 'fixedDelay', 'Period', 0.02, 'TasksToExecute', 2, 'StartDelay', 0.01);
start(t);
wait(t);
s = sprintf('%s %d', logged_text(), t.TasksExecuted);
delete(t);
end

function s = h_timer_period_then_start()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'ExecutionMode', 'fixedRate', 'TasksToExecute', 2, 'StartDelay', 0.01);
t.Period = 0.02;
start(t);
wait(t);
s = sprintf('%s %g', logged_text(), t.Period);
delete(t);
end

function s = h_timer_restart_counts()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartFcn', @(~, ~) vlog('start'), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.01);
start(t);
wait(t);
n1 = t.TasksExecuted;
start(t);
wait(t);
s = sprintf('%s %d %d', logged_text(), n1, t.TasksExecuted);
delete(t);
end

function s = h_timer_tasks_reset_on_restart()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'ExecutionMode', 'fixedRate', 'Period', 0.02, 'StartDelay', 0.01);
start(t);
pause(0.2);
stop(t);
n = t.TasksExecuted;
start(t);
pause(0.05);
stop(t);
s = sprintf('%d %d %d', n >= 3, t.TasksExecuted >= 1, t.TasksExecuted < n);
delete(t);
end

function s = h_timer_running_states()
t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
a = t.Running;
start(t);
b = t.Running;
stop(t);
c = t.Running;
s = sprintf('%s %s %s', a, b, c);
delete(t);
end

function stop_and_log(t)
vlog('tick');
stop(t);
end

function s = h_timer_stop_inside_timerfcn()
t = timer('TimerFcn', @(t, ~) stop_and_log(t), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 5, 'StartDelay', 0.01);
start(t);
wait(t);
s = sprintf('%s %d %s', logged_text(), t.TasksExecuted, t.Running);
delete(t);
end

function s = h_timer_delete_in_timerfcn()
t = timer('TimerFcn', @(src, ~) delete(src), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.01);
start(t);
pause(0.1);
s = sprintf('%s %d', logged_text(), isvalid(t));
end

function s = h_timer_error_stops()
t = timer('TimerFcn', @(~, ~) error('probe:bad', 'bad'), 'ErrorFcn', @(~, ~) vlog('err'), 'StopFcn', @(~, ~) vlog('stop'), 'StartFcn', @(~, ~) vlog('start'), 'ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 3, 'StartDelay', 0.01);
start(t);
wait(t);
s = sprintf('%s %d %s', logged_text(), t.TasksExecuted, t.Running);
delete(t);
end

function s = h_timer_error_no_errorfcn()
t = timer('TimerFcn', @(~, ~) error('probe:bad', 'bad'), 'StartDelay', 0.01);
start(t);
wait(t);
s = sprintf('%d %s', t.TasksExecuted, t.Running);
delete(t);
end

% --- properties ----------------------------------------------------------------------------------

function s = h_timer_defaults()
t = timer;
s = sprintf('%s %s %s %s %s %s %s %s %d %s', class(t.Period), mat2str(t.Period), mat2str(t.StartDelay), t.ExecutionMode, mat2str(t.TasksToExecute), t.BusyMode, t.Running, t.Type, t.TasksExecuted, mat2str(t.AveragePeriod));
delete(t);
end

function s = h_timer_userdata_default()
t = timer;
s = sprintf('%s %s', mat2str(t.UserData), class(t.UserData));
delete(t);
end

function s = h_timer_alias_write()
t = timer;
t2 = t;
t2.UserData = 5;
s = sprintf('%d', t.UserData);
delete(t);
end

function bump_userdata(t)
t.UserData(1) = 7;
end

function s = h_timer_userdata_share_from_callback()
t = timer('TimerFcn', @(src, ~) bump_userdata(src), 'StartDelay', 0.01);
t.UserData = [1 2 3];
u = t.UserData;
start(t);
wait(t);
s = sprintf('%s %s', mat2str(t.UserData), mat2str(u));
delete(t);
end

function s = h_timer_ctor_case_insensitive()
t = timer('timerfcn', @(~, ~) vlog('tick'), 'startdelay', 0.01, 'tag', 'lower');
start(t);
wait(t);
s = sprintf('%s %s', logged_text(), t.Tag);
delete(t);
end

function s = h_timer_isvalid_class()
t = timer;
s = sprintf('%d %s %s %d %d', isvalid(t), class(isvalid(t)), class(t), isa(t, 'handle'), isa(t, 'timer'));
delete(t);
end

% --- the verbs' refusals -------------------------------------------------------------------------

function s = h_timer_wait_not_started()
t = timer('StartDelay', 5);
wait(t);
s = 'returned';
delete(t);
end

function s = h_timer_wait_infinite_refused()
t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
start(t);
try
    wait(t);
    s = 'waited';
catch err
    s = ['ERR2 ' err.message];
end
stop(t);
delete(t);
end

function s = h_timer_start_running()
t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
start(t);
try
    start(t);
    s = 'started twice';
catch err
    s = ['ERR2 ' err.message];
end
stop(t);
delete(t);
end

function s = h_timer_start_no_timerfcn()
t = timer;
try
    start(t);
    s = 'started';
catch err
    s = ['ERR2 ' err.message];
end
delete(t);
end

function s = h_timer_period_while_running()
t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
start(t);
try
    t.Period = 0.5;
    s = sprintf('set %g', t.Period);
catch err
    s = ['ERR2 ' err.message];
end
stop(t);
delete(t);
end

function s = h_timer_bad_prop_ctor()
t = timer('Foo', 1); %#ok<NASGU>
s = 'made';
end

function s = h_timer_bad_prop_write()
t = timer;
try
    t.Foo = 1;
    s = 'written';
catch err
    s = ['ERR2 ' err.message];
end
delete(t);
end

function s = h_timer_unknown_read()
t = timer;
try
    s = sprintf('%d', t.Foo);
catch err
    s = ['ERR2 ' err.message];
end
delete(t);
end

function s = h_timer_bad_mode()
t = timer('ExecutionMode', 'sometimes'); %#ok<NASGU>
s = 'made';
end

function s = h_timer_period_negative()
t = timer('Period', -1); %#ok<NASGU>
s = 'made';
end

function s = h_timer_startdelay_negative()
t = timer('StartDelay', -1); %#ok<NASGU>
s = 'made';
end

function s = h_timer_tasks_zero()
t = timer('TasksToExecute', 0); %#ok<NASGU>
s = 'made';
end

function s = h_timer_timerfcn_not_callable()
t = timer('TimerFcn', 5); %#ok<NASGU>
s = 'made';
end

function s = h_timer_tasks_executed_readonly()
t = timer;
try
    t.TasksExecuted = 5;
    s = 'written';
catch err
    s = ['ERR2 ' err.message];
end
delete(t);
end

function s = h_timer_running_readonly()
t = timer;
try
    t.Running = 'on';
    s = 'set';
catch err
    s = ['ERR2 ' err.message];
end
delete(t);
end

% --- delete and what is left ---------------------------------------------------------------------

function s = h_timer_delete_running()
t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
start(t);
delete(t);
vlog('after_delete');
s = sprintf('%s %d', logged_text(), isvalid(t));
end

function s = h_timer_delete_stopped_no_stopfcn()
t = timer('TimerFcn', @(~, ~) [], 'StopFcn', @(~, ~) vlog('stop'));
delete(t);
s = sprintf('[%s] %d', logged_text(), isvalid(t));
end

function s = h_timer_delete_twice()
t = timer;
delete(t);
delete(t);
s = sprintf('%d', isvalid(t));
end

function s = h_timer_deleted_prop_read()
t = timer;
delete(t);
s = sprintf('%d', t.TasksExecuted);
end

function s = h_timer_deleted_prop_write()
t = timer;
delete(t);
t.UserData = 5;
s = 'written';
end

function s = h_timer_deleted_start()
t = timer('TimerFcn', @(~, ~) []);
delete(t);
start(t);
s = 'started';
end

function s = h_timer_deleted_stop()
t = timer;
delete(t);
stop(t);
s = 'stopped';
end

function s = h_timer_deleted_wait()
t = timer;
delete(t);
wait(t);
s = 'waited';
end
