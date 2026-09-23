% value_isolation_lifetime.m -- appendix A of the value-ownership plan: when a destructor runs
% (V10: onCleanup and a handle class's delete, through aliases, frame exit, an escaping nested
% workspace and shared containers), what save and load share or bind (V9's load demand, V6's
% missing save forms), and timers (V6). A case named aNNN is appendix row NNN; g_ cases agree on
% both engines today.

run_case('a029_oncleanup_capture', @a029_oncleanup_capture);
run_case('a029_oncleanup_alias_lifetime', @a029_oncleanup_alias_lifetime);
run_case('a088_handle_delete_on_clear_alias', @a088_handle_delete_on_clear_alias);
run_case('a089_handle_delete_on_frame_exit', @a089_handle_delete_on_frame_exit);
run_case('a093_escaping_nested_cleanup', @a093_escaping_nested_cleanup);
run_case('a094_clear_cleanup_in_function', @a094_clear_cleanup_in_function);
run_case('a154_cell_alias_clear_order', @a154_cell_alias_clear_order);
run_case('a154_struct_alias_clear_order', @a154_struct_alias_clear_order);
run_case('a154_nested_cell_alias', @a154_nested_cell_alias);
run_case('a154_delete_element_from_one_alias', @a154_delete_element_from_one_alias);
run_case('a154_overwrite_container_alias_kept', @a154_overwrite_container_alias_kept);
run_case('a154_detach_by_write_other_slot', @a154_detach_by_write_other_slot);
run_case('a154_cell_of_two_cleanups_order', @a154_cell_of_two_cleanups_order);
run_case('a109_save_then_write', @a109_save_then_write);
run_case('a109_save_write_trace', @a109_save_write_trace);
run_case('g_write_after_exist_trace', @g_write_after_exist_trace);
run_case('g_load_over_alias', @g_load_over_alias);
run_case('g_two_loads_independent', @g_two_loads_independent);
run_case('g_save_cell_detached_children', @g_save_cell_detached_children);
run_case('a110_save_struct_flag', @a110_save_struct_flag);
run_case('g_load_struct_aliases_independent', @g_load_struct_aliases_independent);
run_case('a111_save_handle_load_is_new', @a111_save_handle_load_is_new);
run_case('a111_save_handle_aliases_one_identity', @a111_save_handle_aliases_one_identity);
run_case('a111_save_value_object', @a111_save_value_object);
run_case('a112_matfile_read_alias', @a112_matfile_read_alias);
run_case('a146_matfile_indexed_write', @a146_matfile_indexed_write, 'div=ADR0167');
run_case('g_save_append', @g_save_append);
run_case('a113_save_captured_handle', @a113_save_captured_handle);
run_case('g_save_complex_alias', @g_save_complex_alias);
run_case('g_load_in_function_binds', @g_load_in_function_binds);
run_case('g_load_handle_statement', @g_load_handle_statement);
run_case('a144_load_handle_expression', @a144_load_handle_expression);
run_case('g_load_feval_statement', @g_load_feval_statement);
run_case('a144_load_feval_expression', @a144_load_feval_expression);
run_case('g_load_anon_expression', @g_load_anon_expression);
run_case('a145_load_anon_statement', @a145_load_anon_statement);
run_case('a144_load_cellfun', @a144_load_cellfun);
run_case('a105_timer_capture', @a105_timer_capture);
run_case('a105_timer_userdata_alias', @a105_timer_userdata_alias);
run_case('a105_timer_writes_global_during_pause', @a105_timer_writes_global_during_pause);
run_case('a105_timer_fixed_rate_count', @a105_timer_fixed_rate_count);
run_case('a105_timer_callback_order', @a105_timer_callback_order);
run_case('a105_timer_error_fcn', @a105_timer_error_fcn);

function run_case(name, fn, rule)
% The rule is exact unless a case names its accepted divergence: a146 (this build writes version
% 5 MAT-files only, so save -v7.3 is refused).
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

function s = logged_text()
global vlog_text
s = vlog_text;
end

% --- onCleanup and a handle class's delete (borrow_probe7b.m, 16, 17, 18) --------------------------

function s = a029_oncleanup_capture()
v = ones(1, 3);
c = onCleanup(@() vlog(sprintf('%d', v(1)))); %#ok<NASGU>
v(1) = 7;
clear c
s = logged_text();
end

function s = a029_oncleanup_alias_lifetime()
c = onCleanup(@() vlog('done'));
d = c; %#ok<NASGU>
clear c
vlog('kept');
clear d
s = logged_text();
end

function s = a088_handle_delete_on_clear_alias()
a = DeleteLogger('A');
b = a; %#ok<NASGU>
clear a
vlog('mid');
clear b
s = logged_text();
end

function make_and_drop()
x = DeleteLogger('X'); %#ok<NASGU>
end

function s = a089_handle_delete_on_frame_exit()
make_and_drop();
vlog('after');
s = logged_text();
end

function f = make_reader()
c = onCleanup(@() vlog('done'));
f = @read;
    function y = read()
        y = isa(c, 'onCleanup');
    end
end

function s = a093_escaping_nested_cleanup()
f = make_reader();
vlog('kept');
vlog(sprintf('%d', f()));
clear f
s = logged_text();
end

function s = a094_clear_cleanup_in_function()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
clear c
vlog('after');
s = logged_text();
end

% --- a cleanup inside a shared container (borrow_probe32.m) ---------------------------------------

function s = a154_cell_alias_clear_order()
c = {onCleanup(@() vlog('done'))};
d = c; %#ok<NASGU>
clear c
vlog('kept');
clear d
vlog('after');
s = logged_text();
end

function s = a154_struct_alias_clear_order()
st.h = onCleanup(@() vlog('done'));
t = st; %#ok<NASGU>
clear st
vlog('kept');
clear t
vlog('after');
s = logged_text();
end

function s = a154_nested_cell_alias()
c = {{onCleanup(@() vlog('done'))}};
d = c{1}; %#ok<NASGU>
clear c
vlog('kept');
clear d
vlog('after');
s = logged_text();
end

function s = a154_delete_element_from_one_alias()
c = {onCleanup(@() vlog('done')), 1};
d = c;
d(1) = [];
vlog('deleted');
clear c
vlog('after');
s = logged_text();
end

function s = a154_overwrite_container_alias_kept()
c = {onCleanup(@() vlog('done'))};
d = c;
c = 5; %#ok<NASGU>
vlog('overwritten');
d = 6; %#ok<NASGU>
vlog('after');
s = logged_text();
end

function s = a154_detach_by_write_other_slot()
c = {onCleanup(@() vlog('done')), 1};
d = c;
d{2} = 2;
clear c
vlog('kept');
clear d
vlog('after');
s = logged_text();
end

function s = a154_cell_of_two_cleanups_order()
c = {onCleanup(@() vlog('A')), onCleanup(@() vlog('B'))}; %#ok<NASGU>
clear c
vlog('after');
s = logged_text();
end

% --- save and load (borrow_probe22.m, borrow_probe25.m, borrow_probe28b.m) ------------------------

function fn = scratch_mat()
fn = [tempname '.mat'];
end

function s = a109_save_then_write()
fn = scratch_mat();
v = [1 2 3];
save(fn, 'v');
v(1) = 7;
S = load(fn);
s = sprintf('%s %s', mat2str(S.v), mat2str(v));
delete(fn);
end

function s = a109_save_write_trace()
fn = scratch_mat();
v = [1 2 3];
save(fn, 'v');
a = mat2str(v);
v(1) = 7;
b = mat2str(v);
S = load(fn);
c = mat2str(v);
delete(fn);
s = sprintf('%s %s %s %s', a, b, c, mat2str(S.v));
end

function s = g_write_after_exist_trace()
v = [1 2 3];
e = exist('v', 'var');
v(1) = 7;
s = sprintf('%d %s', e, mat2str(v));
end

function s = g_load_over_alias()
fn = scratch_mat();
v = [1 2 3];
w = v;
save(fn, 'v');
v = [9 9 9]; %#ok<NASGU>
load(fn);
v(2) = 5;
s = sprintf('%s %s', mat2str(v), mat2str(w));
delete(fn);
end

function s = g_two_loads_independent()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
S1 = load(fn);
S2 = load(fn);
S1.v(1) = 0;
s = sprintf('%s %s', mat2str(S1.v), mat2str(S2.v));
delete(fn);
end

function s = g_save_cell_detached_children()
fn = scratch_mat();
c = {[1 2], [1 2]};
d = c;
d{1}(1) = 9;
save(fn, 'c', 'd');
S = load(fn);
S.c{2}(2) = 0;
s = sprintf('%s %s %s', mat2str(S.c{1}), mat2str(S.d{1}), mat2str(S.c{2}));
delete(fn);
end

function s = a110_save_struct_flag()
fn = scratch_mat();
st.a = [1 2];
st.b = 'x';
save(fn, '-struct', 'st');
S = load(fn);
s = sprintf('%s %s %s', strjoin(fieldnames(S)', ','), mat2str(S.a), S.b);
delete(fn);
end

function s = g_load_struct_aliases_independent()
fn = scratch_mat();
st.a = [1 2];
t = st; %#ok<NASGU>
save(fn, 'st', 't');
S = load(fn);
S.st.a(1) = 7;
s = mat2str(S.t.a);
delete(fn);
end

function s = a111_save_handle_load_is_new()
fn = scratch_mat();
h = HandleHolder();
h.data = [1 2];
save(fn, 'h');
S = load(fn);
S.h.data(1) = 7;
s = sprintf('%s %d', mat2str(h.data), S.h == h);
delete(fn);
end

function s = a111_save_handle_aliases_one_identity()
fn = scratch_mat();
h = HandleHolder();
h.data = 1;
g = h; %#ok<NASGU>
save(fn, 'h', 'g');
S = load(fn);
S.h.data = 5;
s = sprintf('%g %d', S.g.data, S.g == S.h);
delete(fn);
end

function s = a111_save_value_object()
fn = scratch_mat();
o = ValueBox();
o.p = 3;
save(fn, 'o');
o.p = 4;
S = load(fn);
s = sprintf('%s %g', class(S.o), S.o.p);
delete(fn);
end

function s = a112_matfile_read_alias()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
m = matfile(fn);
x = m.v;
x(1) = 7;
s = mat2str(m.v);
delete(fn);
end

function s = a146_matfile_indexed_write()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v', '-v7.3');
m = matfile(fn, 'Writable', true);
m.v(1, 2) = 8;
S = load(fn);
s = mat2str(S.v);
delete(fn);
end

function s = g_save_append()
fn = scratch_mat();
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
u = 5; %#ok<NASGU>
save(fn, 'u', '-append');
S = load(fn);
s = strjoin(sort(fieldnames(S))', ',');
delete(fn);
end

function s = a113_save_captured_handle()
fn = scratch_mat();
v = [1 2 3];
f = @() v; %#ok<NASGU>
save(fn, 'f');
v(1) = 7; %#ok<NASGU>
S = load(fn);
s = mat2str(S.f());
delete(fn);
end

function s = g_save_complex_alias()
fn = scratch_mat();
z = [1 2 3];
y = z;
y(2) = 1i; %#ok<NASGU>
save(fn, 'z', 'y');
S = load(fn);
s = sprintf('%d %d', isreal(S.z), isreal(S.y));
delete(fn);
end

function s = g_load_in_function_binds()
fn = scratch_mat();
q = [4 5]; %#ok<NASGU>
save(fn, 'q');
clear q
load(fn);
r = q;
r(1) = 0;
s = sprintf('%s %s', mat2str(q), mat2str(r));
delete(fn);
end

function fn = saved_v()
fn = [tempname '.mat'];
v = [1 2 3]; %#ok<NASGU>
save(fn, 'v');
end

function s = g_load_handle_statement()
fn = saved_v();
h = @load;
h(fn);
s = sprintf('%d', exist('v', 'var'));
delete(fn);
end

function s = a144_load_handle_expression()
fn = saved_v();
h = @load;
S = h(fn);
s = sprintf('%d %d', exist('v', 'var'), isfield(S, 'v'));
delete(fn);
end

function s = g_load_feval_statement()
fn = saved_v();
feval(@load, fn);
s = sprintf('%d', exist('v', 'var'));
delete(fn);
end

function s = a144_load_feval_expression()
fn = saved_v();
S = feval('load', fn);
s = sprintf('%d %d', exist('v', 'var'), isfield(S, 'v'));
delete(fn);
end

function s = g_load_anon_expression()
fn = saved_v();
g = @(f) load(f);
S = g(fn);
s = sprintf('%d %d', exist('v', 'var'), isfield(S, 'v'));
delete(fn);
end

function s = a145_load_anon_statement()
fn = saved_v();
g = @(f) load(f);
g(fn);
s = sprintf('%d', exist('v', 'var'));
delete(fn);
end

function s = a144_load_cellfun()
fn = saved_v();
C = cellfun(@load, {fn}, 'UniformOutput', false);
s = sprintf('%d %d', exist('v', 'var'), isfield(C{1}, 'v'));
delete(fn);
end

% --- timers (borrow_probe20.m) ----------------------------------------------------------------------

function store_timer(x)
global timer_out
timer_out = x;
end

function s = a105_timer_capture()
global timer_out
timer_out = [];
v = [1 2 3];
t = timer('TimerFcn', @(~, ~) store_timer(v), 'StartDelay', 0.05);
v(1) = 7;
start(t);
wait(t);
delete(t);
s = mat2str(timer_out);
end

function s = a105_timer_userdata_alias()
t = timer;
u = [1 2 3];
t.UserData = u;
u(1) = 7;
w = t.UserData;
w(2) = 8;
s = mat2str(t.UserData);
delete(t);
end

function bump_timer()
global g_timer
g_timer(1) = 7;
end

function z = pause_then_zero()
pause(0.5);
z = 0;
end

function s = a105_timer_writes_global_during_pause()
global g_timer
g_timer = [1 2 3];
t = timer('TimerFcn', @(~, ~) bump_timer(), 'StartDelay', 0.01);
start(t);
r = g_timer + pause_then_zero();
wait(t);
delete(t);
s = sprintf('%s %s', mat2str(r), mat2str(g_timer));
end

function s = a105_timer_fixed_rate_count()
t = timer('ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 3, 'TimerFcn', @(~, ~) []);
start(t);
wait(t);
s = sprintf('%d', t.TasksExecuted);
delete(t);
end

function s = a105_timer_callback_order()
t = timer('StartFcn', @(~, ~) vlog('start'), 'TimerFcn', @(~, ~) vlog('tick'), ...
    'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.01);
start(t);
wait(t);
delete(t);
s = logged_text();
end

function s = a105_timer_error_fcn()
t = timer('TimerFcn', @(~, ~) error('probe:bad', 'bad'), 'ErrorFcn', @(~, e) vlog(e.Data.messageID), ...
    'StartDelay', 0.01);
start(t);
wait(t);
delete(t);
s = logged_text();
end
