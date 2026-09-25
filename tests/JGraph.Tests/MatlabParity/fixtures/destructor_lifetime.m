% destructor_lifetime.m -- V10 of the value-ownership plan (ADR 0171): when a destructor runs.
% An onCleanup task and a handle class's delete method run when the last holder of the object
% goes -- at a frame's exit, at clear, at a rebinding, at an error's unwinding, when a container,
% a snapshot or an escaped nested workspace that held it is released -- and in a measured order.
% Each case logs through vlog (helpers/vlog.m) and answers the log; a dl_ case uses DeleteLogger,
% a cl_ case onCleanup, an ho_ case DeleteHolder (a handle with a destructor and a property).

run_case('dl_frame_exit_declaration_order', @dl_frame_exit_declaration_order);
run_case('dl_frame_exit_rebound_keeps_slot', @dl_frame_exit_rebound_keeps_slot);
run_case('dl_frame_exit_output_kept', @dl_frame_exit_output_kept);
run_case('dl_clear_all_declaration_order', @dl_clear_all_declaration_order);
run_case('dl_clear_variables_declaration_order', @dl_clear_variables_declaration_order);
run_case('dl_clear_named_order', @dl_clear_named_order);
run_case('dl_error_unwinds_before_catch', @dl_error_unwinds_before_catch);
run_case('cl_error_unwinds_before_catch', @cl_error_unwinds_before_catch);
run_case('cl_task_error_is_warning', @cl_task_error_is_warning);
run_case('dl_delete_error_is_warning', @dl_delete_error_is_warning);
run_case('cl_reads_captured_value', @cl_reads_captured_value);
run_case('dl_alias_outlives_clear', @dl_alias_outlives_clear);
run_case('dl_rebind_alias_then_clear', @dl_rebind_alias_then_clear);
run_case('dl_rebind_variable', @dl_rebind_variable);
run_case('dl_returned_handle', @dl_returned_handle);
run_case('dl_anon_capture_cleared', @dl_anon_capture_cleared);
run_case('cl_escaping_nested_two_handles', @cl_escaping_nested_two_handles);
run_case('dl_escaping_nested_workspace_order', @dl_escaping_nested_workspace_order);
run_case('cl_self_referencing_nested_handle', @cl_self_referencing_nested_handle);
run_case('cl_nested_call_reads_then_exit', @cl_nested_call_reads_then_exit);
run_case('cl_clear_in_nested_function', @cl_clear_in_nested_function);
run_case('cl_clear_in_function', @cl_clear_in_function);
run_case('dl_temporary_argument', @dl_temporary_argument);
run_case('dl_statement_binds_ans', @dl_statement_binds_ans);
run_case('dl_call_output_in_ans', @dl_call_output_in_ans);
run_case('dl_discarded_output', @dl_discarded_output);
run_case('dl_callee_clears_parameter', @dl_callee_clears_parameter);
run_case('dl_cell_cleared', @dl_cell_cleared);
run_case('dl_cell_slot_rebind', @dl_cell_slot_rebind);
run_case('dl_nested_container_order', @dl_nested_container_order);
run_case('dl_cell_in_cell_then_direct', @dl_cell_in_cell_then_direct);
run_case('dl_struct_fields_order', @dl_struct_fields_order);
run_case('dl_struct_mixed_order', @dl_struct_mixed_order);
run_case('dl_struct_array_elements', @dl_struct_array_elements);
run_case('dl_rmfield', @dl_rmfield);
run_case('dl_object_array', @dl_object_array);
run_case('dl_two_slots_one_object', @dl_two_slots_one_object);
run_case('cl_cell_wrapped_in_cell', @cl_cell_wrapped_in_cell);
run_case('dl_for_over_cell_of_handles', @dl_for_over_cell_of_handles);
run_case('dl_cellfun_answers_in_ans', @dl_cellfun_answers_in_ans);
run_case('dl_value_object_property', @dl_value_object_property);
run_case('dl_object_properties_order', @dl_object_properties_order);
run_case('dl_handle_holder_property', @dl_handle_holder_property);
run_case('ho_delete_then_properties', @ho_delete_then_properties);
run_case('ho_explicit_delete_releases_properties', @ho_explicit_delete_releases_properties);
run_case('ho_deleted_alias_reads', @ho_deleted_alias_reads);
run_case('dl_delete_then_clear', @dl_delete_then_clear);
run_case('cl_explicit_delete', @cl_explicit_delete);
run_case('cl_isvalid_after_delete_alias', @cl_isvalid_after_delete_alias);
run_case('cl_class_and_isa', @cl_class_and_isa);
run_case('dl_global_unlink_then_clear_global', @dl_global_unlink_then_clear_global);
run_case('dl_persistent_reset', @dl_persistent_reset);
run_case('dl_map_cleared', @dl_map_cleared);

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

function s = logged()
global vlog_text
s = vlog_text;
end

% --- a frame's exit ---------------------------------------------------------------------------

function make_zam()
z = DeleteLogger('Z'); %#ok<NASGU>
a = DeleteLogger('A'); %#ok<NASGU>
m = DeleteLogger('M'); %#ok<NASGU>
end

function s = dl_frame_exit_declaration_order()
make_zam();
vlog('after');
s = logged();
end

function make_rebound()
m = DeleteLogger('M1'); %#ok<NASGU>
a = DeleteLogger('A'); %#ok<NASGU>
m = DeleteLogger('M2'); %#ok<NASGU>
end

function s = dl_frame_exit_rebound_keeps_slot()
make_rebound();
vlog('after');
s = logged();
end

function h = make_h_with_locals()
z = DeleteLogger('Z'); %#ok<NASGU>
h = DeleteLogger('H');
a = DeleteLogger('A'); %#ok<NASGU>
end

function s = dl_frame_exit_output_kept()
h = make_h_with_locals(); %#ok<NASGU>
vlog('kept');
clear h
vlog('after');
s = logged();
end

% --- clear ------------------------------------------------------------------------------------

function s = dl_clear_all_declaration_order()
z = DeleteLogger('Z'); %#ok<NASGU>
a = DeleteLogger('A'); %#ok<NASGU>
m = DeleteLogger('M'); %#ok<NASGU>
clear
vlog('after');
s = logged();
end

function s = dl_clear_variables_declaration_order()
z = DeleteLogger('Z'); %#ok<NASGU>
a = DeleteLogger('A'); %#ok<NASGU>
m = DeleteLogger('M'); %#ok<NASGU>
clear variables
vlog('after');
s = logged();
end

function s = dl_clear_named_order()
a = DeleteLogger('A'); %#ok<NASGU>
b = DeleteLogger('B'); %#ok<NASGU>
c = DeleteLogger('C'); %#ok<NASGU>
clear c a
vlog('mid');
clear b
vlog('after');
s = logged();
end

% --- errors -----------------------------------------------------------------------------------

function thrower_dl()
a = DeleteLogger('A'); %#ok<NASGU>
b = DeleteLogger('B'); %#ok<NASGU>
error('lt:boom', 'boom');
end

function s = dl_error_unwinds_before_catch()
try
    thrower_dl();
catch err
    vlog(['caught:' err.identifier]);
end
vlog('after');
s = logged();
end

function thrower_cl()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
error('lt:boom', 'boom');
end

function s = cl_error_unwinds_before_catch()
try
    thrower_cl();
catch
    vlog('caught');
end
vlog('after');
s = logged();
end

function s = cl_task_error_is_warning()
lastwarn('');
c = onCleanup(@() error('lt:bad', 'bad task')); %#ok<NASGU>
clear c
vlog('after');
[msg, id] = lastwarn;
lines = strsplit(msg, char(10));
s = sprintf('%s [%s] {%s}', logged(), id, lines{1});
end

function s = dl_delete_error_is_warning()
lastwarn('');
d = DeleteThrower(); %#ok<NASGU>
clear d
vlog('after');
[msg, id] = lastwarn;
lines = strsplit(msg, char(10));
s = sprintf('%s [%s] {%s}', logged(), id, lines{1});
end

% --- aliases, rebinding, captures -------------------------------------------------------------

function s = cl_reads_captured_value()
v = [1 2 3];
c = onCleanup(@() vlog(sprintf('%d', v(1)))); %#ok<NASGU>
v(1) = 7;
vlog(sprintf('%d', v(1)));
clear c
vlog('after');
s = logged();
end

function s = dl_alias_outlives_clear()
a = DeleteLogger('A');
b = a; %#ok<NASGU>
clear a
vlog('mid');
clear b
vlog('after');
s = logged();
end

function s = dl_rebind_alias_then_clear()
c = DeleteLogger('A');
d = c; %#ok<NASGU>
c = DeleteLogger('B'); %#ok<NASGU>
vlog('mid');
clear d
vlog('two');
clear c
vlog('after');
s = logged();
end

function s = dl_rebind_variable()
c = DeleteLogger('A');
c = 5; %#ok<NASGU>
vlog('after');
s = logged();
end

function h = make_h()
h = DeleteLogger('H');
end

function s = dl_returned_handle()
h = make_h(); %#ok<NASGU>
vlog('kept');
clear h
vlog('after');
s = logged();
end

function s = dl_anon_capture_cleared()
c = DeleteLogger('C');
g = @() c; %#ok<NASGU>
clear c
vlog('kept');
clear g
vlog('after');
s = logged();
end

% --- nested workspaces ------------------------------------------------------------------------

function [f1, f2] = make_two_readers()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
f1 = @read1;
f2 = @read2;
    function y = read1()
        y = isa(c, 'onCleanup');
    end
    function y = read2()
        y = isa(c, 'onCleanup');
    end
end

function s = cl_escaping_nested_two_handles()
[f1, f2] = make_two_readers();
vlog(sprintf('%d', f1()));
clear f1
vlog('one');
vlog(sprintf('%d', f2()));
clear f2
vlog('after');
s = logged();
end

function f = make_reader_zam()
z = DeleteLogger('Z'); %#ok<NASGU>
a = DeleteLogger('A'); %#ok<NASGU>
m = DeleteLogger('M'); %#ok<NASGU>
f = @read;
    function y = read()
        y = a.tag;
    end
end

function s = dl_escaping_nested_workspace_order()
f = make_reader_zam();
vlog(f());
clear f
vlog('after');
s = logged();
end

function make_selfref()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
f = @read; %#ok<NASGU>
    function y = read()
        y = c;
    end
end

function s = cl_self_referencing_nested_handle()
make_selfref();
vlog('after');
s = logged();
end

function outer_with_nested()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
inner();
vlog('back');
    function inner()
        vlog(sprintf('%d', isa(c, 'onCleanup')));
    end
end

function s = cl_nested_call_reads_then_exit()
outer_with_nested();
vlog('after');
s = logged();
end

function outer_clear_in_nested()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
inner();
vlog('back');
    function inner()
        clear c
        vlog('cleared');
    end
end

function s = cl_clear_in_nested_function()
outer_clear_in_nested();
vlog('after');
s = logged();
end

function s = cl_clear_in_function()
c = onCleanup(@() vlog('done')); %#ok<NASGU>
clear c
vlog('after');
s = logged();
end

% --- temporaries and ans ----------------------------------------------------------------------

function use_tag(x)
vlog(['used' x.tag]);
end

function s = dl_temporary_argument()
use_tag(DeleteLogger('T'));
vlog('after');
s = logged();
end

function s = dl_statement_binds_ans()
DeleteLogger('T');
vlog('mid');
max(1, 2);
vlog('after');
s = logged();
end

function s = dl_call_output_in_ans()
make_h();
vlog('mid');
3;
vlog('after');
s = logged();
end

function s = dl_discarded_output()
[~] = make_h();
vlog('after');
s = logged();
end

function takes_and_clears(x) %#ok<INUSD>
clear x
vlog('in');
end

function s = dl_callee_clears_parameter()
a = DeleteLogger('A'); %#ok<NASGU>
takes_and_clears(a);
vlog('mid');
clear a
vlog('after');
s = logged();
end

% --- containers -------------------------------------------------------------------------------

function s = dl_cell_cleared()
c = {DeleteLogger('A'), DeleteLogger('B')}; %#ok<NASGU>
vlog('held');
clear c
vlog('after');
s = logged();
end

function s = dl_cell_slot_rebind()
c = {DeleteLogger('A'), 1};
c{1} = 5;
vlog('after');
s = logged();
end

function s = dl_nested_container_order()
c = {DeleteLogger('A'), {DeleteLogger('B'), DeleteLogger('C')}, DeleteLogger('D')}; %#ok<NASGU>
clear c
vlog('after');
s = logged();
end

function s = dl_cell_in_cell_then_direct()
c = {{DeleteLogger('B')}, DeleteLogger('A')}; %#ok<NASGU>
clear c
vlog('after');
s = logged();
end

function s = dl_struct_fields_order()
st.z = DeleteLogger('Z');
st.a = DeleteLogger('A'); %#ok<STRNU>
clear st
vlog('after');
s = logged();
end

function s = dl_struct_mixed_order()
st.c = {DeleteLogger('A'), DeleteLogger('B')};
st.d = DeleteLogger('D'); %#ok<STRNU>
clear st
vlog('after');
s = logged();
end

function s = dl_struct_array_elements()
st(2).h = DeleteLogger('B');
st(1).h = DeleteLogger('A'); %#ok<STRNU>
clear st
vlog('after');
s = logged();
end

function s = dl_rmfield()
st.a = DeleteLogger('A');
st = rmfield(st, 'a'); %#ok<NASGU>
vlog('after');
s = logged();
end

function s = dl_object_array()
arr = [DeleteLogger('A'), DeleteLogger('B')]; %#ok<NASGU>
clear arr
vlog('after');
s = logged();
end

function s = dl_two_slots_one_object()
a = DeleteLogger('A');
c = {a, a};
clear a
vlog('mid');
clear c
vlog('after');
s = logged();
end

function s = cl_cell_wrapped_in_cell()
c = {onCleanup(@() vlog('done'))};
c = {c}; %#ok<NASGU>
vlog('wrapped');
clear c
vlog('after');
s = logged();
end

function s = dl_for_over_cell_of_handles()
for x = {DeleteLogger('A'), DeleteLogger('B')}
    vlog(['it' x{1}.tag]);
end
vlog('after');
s = logged();
end

function s = dl_cellfun_answers_in_ans()
cellfun(@(t) DeleteLogger(t), {'A', 'B'}, 'UniformOutput', false);
vlog('mid');
clear ans
vlog('after');
s = logged();
end

% --- objects holding objects ------------------------------------------------------------------

function s = dl_value_object_property()
o = ValueBox();
o.p = DeleteLogger('P'); %#ok<NASGU>
clear o
vlog('after');
s = logged();
end

function s = dl_object_properties_order()
o = PropOrder();
o.z = DeleteLogger('Z');
o.a = DeleteLogger('A');
clear o
vlog('after');
s = logged();
end

function s = dl_handle_holder_property()
h = HandleHolder();
h.data = DeleteLogger('A');
g = h; %#ok<NASGU>
clear h
vlog('kept');
clear g
vlog('after');
s = logged();
end

function s = ho_delete_then_properties()
h = DeleteHolder('H');
h.inner = DeleteLogger('I');
clear h
vlog('after');
s = logged();
end

function s = ho_explicit_delete_releases_properties()
h = DeleteHolder('H');
h.inner = {DeleteLogger('I'), DeleteLogger('J')};
delete(h);
vlog('mid');
clear h
vlog('after');
s = logged();
end

function s = ho_deleted_alias_reads()
h = DeleteHolder('H');
h.inner = DeleteLogger('I');
g = h;
delete(h);
vlog(sprintf('%d', isvalid(g)));
try
    x = g.inner; %#ok<NASGU>
catch err
    vlog(err.identifier);
end
clear g h
vlog('after');
s = logged();
end

function s = dl_delete_then_clear()
a = DeleteLogger('A');
delete(a);
vlog('mid');
clear a
vlog('after');
s = logged();
end

function s = cl_explicit_delete()
c = onCleanup(@() vlog('done'));
delete(c);
vlog('mid');
clear c
vlog('after');
s = logged();
end

function s = cl_isvalid_after_delete_alias()
c = onCleanup(@() vlog('done'));
d = c;
delete(c);
vlog(sprintf('%d', isvalid(d)));
clear c d
vlog('after');
s = logged();
end

function s = cl_class_and_isa()
c = onCleanup(@() vlog('done'));
s = sprintf('%s %d %d %d %s', class(c), isa(c, 'handle'), isa(c, 'onCleanup'), isvalid(c), class(c.task));
end

% --- other holders ----------------------------------------------------------------------------

function s = dl_global_unlink_then_clear_global()
global dl_lifetime_g
dl_lifetime_g = DeleteLogger('G');
clear dl_lifetime_g
vlog('unlinked');
clear global dl_lifetime_g
vlog('after');
s = logged();
end

function keep_persistent(reset)
persistent p
if reset
    p = [];
    return
end
if isempty(p)
    p = DeleteLogger('P');
end
end

function s = dl_persistent_reset()
keep_persistent(false);
vlog('set');
keep_persistent(true);
vlog('reset');
s = logged();
end

function s = dl_map_cleared()
m = containers.Map();
m('k') = DeleteLogger('A');
clear m
vlog('after');
s = logged();
end
