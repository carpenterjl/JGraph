% value_isolation_persist.m -- V5 of the value-ownership plan: a persistent variable is one binding
% (M9). Every read, write and rebinding of it reaches the owner function's slot at once, so a
% recursive or re-entered call sees what the outer call wrote and the outer call sees what the inner
% one wrote. Appendix A #17, #18 and #72 are the seeds (value_isolation_calls); this is the matrix
% around them. A script's local functions are never cleared while the script runs, so the clear
% cases use function files in helpers/ (vp_*.m). g_ cases agree on both engines today.

run_case('p_recursive_elem_write', @p_recursive_elem_write);
run_case('p_recursive_rebind', @p_recursive_rebind);
run_case('p_recursive_grow', @p_recursive_grow);
run_case('p_recursive_end_append', @p_recursive_end_append);
run_case('p_recursive_delete', @p_recursive_delete);
run_case('p_recursive_cell_slot', @p_recursive_cell_slot);
run_case('p_recursive_struct_field', @p_recursive_struct_field);
run_case('p_recursive_nested_struct_elem', @p_recursive_nested_struct_elem);
run_case('p_recursive_three_deep', @p_recursive_three_deep);
run_case('p_recursive_two_names', @p_recursive_two_names);
run_case('p_recursive_multi_assign', @p_recursive_multi_assign);
run_case('p_recursive_eval_write', @p_recursive_eval_write);
run_case('p_recursive_value_object_prop', @p_recursive_value_object_prop);
run_case('p_recursive_inner_reads_outer_write', @p_recursive_inner_reads_outer_write);
run_case('p_reentry_through_cellfun', @p_reentry_through_cellfun);
run_case('p_reentry_through_handle', @p_reentry_through_handle);
run_case('p_reentry_through_arrayfun_elem', @p_reentry_through_arrayfun_elem);
run_case('p_mutual_recursion', @p_mutual_recursion);
run_case('p_error_after_update_keeps_it', @p_error_after_update_keeps_it);
run_case('p_error_in_recursion_keeps_both', @p_error_in_recursion_keeps_both);
run_case('p_error_mid_elem_write_keeps_earlier', @p_error_mid_elem_write_keeps_earlier);
run_case('p_two_functions_one_name', @p_two_functions_one_name);
run_case('p_global_of_the_same_name', @p_global_of_the_same_name);
run_case('p_nested_sees_parent_slot', @p_nested_sees_parent_slot);
run_case('p_nested_writes_parent_slot_recursive', @p_nested_writes_parent_slot_recursive);
run_case('p_nested_own_persistent', @p_nested_own_persistent);
run_case('g_alias_isolated_from_later_write', @g_alias_isolated_from_later_write);
run_case('g_returned_copy_isolated', @g_returned_copy_isolated);
run_case('p_returned_copy_isolated_recursive', @p_returned_copy_isolated_recursive);
run_case('g_argument_pass_leaves_slot', @g_argument_pass_leaves_slot);
run_case('g_anonymous_capture_is_a_snapshot', @g_anonymous_capture_is_a_snapshot);
run_case('g_handle_object_is_shared', @g_handle_object_is_shared);
run_case('g_loop_accumulates_across_calls', @g_loop_accumulates_across_calls);
run_case('g_loop_elem_writes_across_calls', @g_loop_elem_writes_across_calls);
run_case('p_loop_with_recursion', @p_loop_with_recursion);
run_case('g_loop_variable_is_persistent', @g_loop_variable_is_persistent);
run_case('g_exist_and_who', @g_exist_and_who);
run_case('p_first_read_is_empty_double', @p_first_read_is_empty_double);
run_case('g_declared_in_a_branch', @g_declared_in_a_branch);
run_case('p_clear_variable_in_owner', @p_clear_variable_in_owner);
run_case('p_clear_name_inactive_resets', @p_clear_name_inactive_resets);
run_case('g_clear_functions_inactive_resets', @g_clear_functions_inactive_resets);
run_case('p_clear_functions_local_function', @p_clear_functions_local_function);
run_case('p_clear_functions_active_recursive', @p_clear_functions_active_recursive);
run_case('p_clear_name_active', @p_clear_name_active);
run_case('p_clear_functions_from_sibling', @p_clear_functions_from_sibling);
run_case('p_clear_functions_in_callback', @p_clear_functions_in_callback);
run_case('p_clear_functions_active_then_inactive', @p_clear_functions_active_then_inactive);
run_case('p_clear_functions_spares_active_resets_others', @p_clear_functions_spares_active_resets_others);

function run_case(name, fn)
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

% --- recursion: every write shape ---------------------------------------------------------------------

function r = rec_elem(depth)
persistent p
if isempty(p), p = zeros(1, 3); end
p(1) = p(1) + 1;
if depth > 0, rec_elem(depth - 1); end
p(2) = p(2) + 10;
r = p;
end

function s = p_recursive_elem_write()
s = mat2str(rec_elem(2));
end

function r = rec_rebind(depth)
persistent p
if isempty(p), p = zeros(1, 3); end
p = p + [1 0 0];
if depth > 0, rec_rebind(depth - 1); end
p = p + [0 10 0];
r = p;
end

function s = p_recursive_rebind()
s = mat2str(rec_rebind(2));
end

function r = rec_grow(depth)
persistent p
if isempty(p), p = [1 1]; end
p(numel(p) + 2) = depth;
if depth > 0, rec_grow(depth - 1); end
r = p;
end

function s = p_recursive_grow()
s = mat2str(rec_grow(2));
end

function r = rec_append(depth)
persistent p
p(end + 1) = depth;
if depth > 0, rec_append(depth - 1); end
p(end + 1) = -depth - 1;
r = p;
end

function s = p_recursive_end_append()
s = mat2str(rec_append(2));
end

function r = rec_delete(depth)
persistent p
if isempty(p), p = 1:6; end
p(1) = [];
if depth > 0, rec_delete(depth - 1); end
r = p;
end

function s = p_recursive_delete()
s = mat2str(rec_delete(2));
end

function r = rec_cell(depth)
persistent c
if isempty(c), c = {0, 'a'}; end
c{1} = c{1} + 1;
if depth > 0, rec_cell(depth - 1); end
c{end + 1} = depth;
r = c;
end

function s = p_recursive_cell_slot()
c = rec_cell(1);
s = sprintf('%d %s %d %d', c{1}, c{2}, c{3}, c{4});
end

function r = rec_struct(depth)
persistent st
if isempty(st), st = struct('n', 0, 'log', ''); end
st.n = st.n + 1;
if depth > 0, rec_struct(depth - 1); end
st.log = [st.log sprintf('%d', depth)];
r = st;
end

function s = p_recursive_struct_field()
st = rec_struct(2);
s = sprintf('%d %s', st.n, st.log);
end

function r = rec_nested_struct(depth)
persistent st
if isempty(st), st.a.b = zeros(1, 3); end
st.a.b(1) = st.a.b(1) + 1;
if depth > 0, rec_nested_struct(depth - 1); end
st.a.b(3) = st.a.b(3) + 5;
r = st.a.b;
end

function s = p_recursive_nested_struct_elem()
s = mat2str(rec_nested_struct(1));
end

function s = p_recursive_three_deep()
rec_elem_b(3);
s = mat2str(rec_elem_b(0));
end

function r = rec_elem_b(depth)
persistent p
if isempty(p), p = zeros(1, 2); end
p(1) = p(1) + 1;
if depth > 0, rec_elem_b(depth - 1); end
p(2) = p(2) + p(1);
r = p;
end

function r = rec_two(depth)
persistent a b
if isempty(a), a = 0; b = 100; end
a = a + 1;
if depth > 0, rec_two(depth - 1); end
b = b + a;
r = [a b];
end

function s = p_recursive_two_names()
s = mat2str(rec_two(1));
end

function r = rec_multi(depth)
persistent p q
if isempty(p), p = 0; q = 0; end
[p, q] = deal(p + 1, q + 2);
if depth > 0, rec_multi(depth - 1); end
[p, q] = deal(p + 10, q + 20);
r = [p q];
end

function s = p_recursive_multi_assign()
s = mat2str(rec_multi(1));
end

function r = rec_eval(depth)
persistent p
if isempty(p), p = 0; end
eval('p = p + 1;');
if depth > 0, rec_eval(depth - 1); end
eval('p = p + 10;');
r = p;
end

function s = p_recursive_eval_write()
s = num2str(rec_eval(1));
end

function r = rec_object(depth)
persistent o
if isempty(o), o = ValueBox(); o.p = 0; end
o.p = o.p + 1;
if depth > 0, rec_object(depth - 1); end
o.p = o.p + 10;
r = o.p;
end

function s = p_recursive_value_object_prop()
s = num2str(rec_object(1));
end

function r = rec_reads(depth)
persistent p
if isempty(p), p = 0; end
p = p + 1;
if depth > 0
    r = rec_reads(depth - 1);
else
    r = p;
end
end

function s = p_recursive_inner_reads_outer_write()
s = num2str(rec_reads(3));
end

% --- re-entry through a callback -----------------------------------------------------------------------

function r = reenter_cellfun(depth)
persistent p
if isempty(p), p = 0; end
p = p + 1;
if depth > 0, cellfun(@(k) reenter_cellfun(k), {depth - 1}); end
p = p + 10;
r = p;
end

function s = p_reentry_through_cellfun()
s = num2str(reenter_cellfun(1));
end

function r = reenter_handle(fn, depth)
persistent p
if isempty(p), p = 0; end
p = p + 1;
if depth > 0, fn(fn, depth - 1); end
p = p + 10;
r = p;
end

function s = p_reentry_through_handle()
s = num2str(reenter_handle(@reenter_handle, 1));
end

function r = reenter_arrayfun(depth)
persistent p
if isempty(p), p = zeros(1, 2); end
p(1) = p(1) + 1;
if depth > 0, arrayfun(@(k) sum(reenter_arrayfun(0)) * k, 1:2); end
p(2) = p(2) + 10;
r = p;
end

function s = p_reentry_through_arrayfun_elem()
s = mat2str(reenter_arrayfun(1));
end

function r = mutual_a(depth)
persistent p
if isempty(p), p = 0; end
p = p + 1;
if depth > 0, mutual_b(depth - 1); end
p = p + 10;
r = p;
end

function r = mutual_b(depth)
persistent p
if isempty(p), p = 1000; end
p = p + 1;
if depth > 0, mutual_a(depth - 1); end
r = p;
end

function s = p_mutual_recursion()
s = sprintf('%d %d', mutual_a(2), mutual_b(0));
end

% --- an error leaves the slot as the last write made it ------------------------------------------------

function r = err_after(cmd)
persistent p
if isempty(p), p = 0; end
if strcmp(cmd, 'peek'), r = p; return; end
p = p + 1;
error('vp:boom', 'boom');
end

function s = p_error_after_update_keeps_it()
try
    err_after('go');
catch
end
try
    err_after('go');
catch
end
s = num2str(err_after('peek'));
end

function r = err_rec(depth)
persistent p
if isempty(p), p = zeros(1, 2); end
if depth < 0, r = p; return; end
p(1) = p(1) + 1;
if depth == 0, error('vp:boom', 'boom'); end
err_rec(depth - 1);
p(2) = p(2) + 10;
r = p;
end

function s = p_error_in_recursion_keeps_both()
try
    err_rec(2);
catch
end
s = mat2str(err_rec(-1));
end

function r = err_mid(cmd)
persistent p
if isempty(p), p = zeros(1, 3); end
if strcmp(cmd, 'peek'), r = p; return; end
p(1) = 1;
p(2) = [1 2];
p(3) = 3;
r = p;
end

function s = p_error_mid_elem_write_keeps_earlier()
try
    err_mid('go');
catch
end
s = mat2str(err_mid('peek'));
end

% --- whose slot ----------------------------------------------------------------------------------------

function r = own_a()
persistent p
if isempty(p), p = 0; end
p = p + 1;
r = p;
end

function r = own_b()
persistent p
if isempty(p), p = 100; end
p = p + 1;
r = p;
end

function s = p_two_functions_one_name()
own_a(); own_a(); own_b();
s = sprintf('%d %d', own_a(), own_b());
end

function r = own_persistent()
persistent vp_shared_name
if isempty(vp_shared_name), vp_shared_name = 0; end
vp_shared_name = vp_shared_name + 1;
r = vp_shared_name;
end

function r = own_global()
global vp_shared_name
if isempty(vp_shared_name), vp_shared_name = 500; end
vp_shared_name = vp_shared_name + 1;
r = vp_shared_name;
end

function s = p_global_of_the_same_name()
own_persistent(); own_global(); own_persistent();
s = sprintf('%d %d', own_persistent(), own_global());
end

function r = nested_parent(depth)
persistent p
if isempty(p), p = 0; end
bump();
bump();
r = p;
    function bump()
        p = p + 1;
    end
end

function s = p_nested_sees_parent_slot()
nested_parent();
s = num2str(nested_parent());
end

function r = nested_parent_rec(depth)
persistent p
if isempty(p), p = zeros(1, 2); end
bump();
if depth > 0, nested_parent_rec(depth - 1); end
bump10();
r = p;
    function bump()
        p(1) = p(1) + 1;
    end
    function bump10()
        p(2) = p(2) + 10;
    end
end

function s = p_nested_writes_parent_slot_recursive()
s = mat2str(nested_parent_rec(1));
end

function r = nested_owner()
r = count();
    function n = count()
        persistent k
        if isempty(k), k = 0; end
        k = k + 1;
        n = k;
    end
end

function s = p_nested_own_persistent()
nested_owner(); nested_owner();
s = num2str(nested_owner());
end

% --- the slot is an entry: copies of it are isolated (M2, M3) ------------------------------------------

function r = alias_then_write()
persistent p
if isempty(p), p = [1 2 3]; end
q = p;
p(1) = p(1) + 10;
r = [q; p];
end

function s = g_alias_isolated_from_later_write()
alias_then_write();
s = mat2str(alias_then_write());
end

function r = give_copy()
persistent p
if isempty(p), p = [0 0]; end
p(1) = p(1) + 1;
r = p;
end

function s = g_returned_copy_isolated()
a = give_copy();
give_copy();
a(2) = 99;
s = mat2str([a; give_copy()]);
end

function r = give_copy_rec(depth)
persistent p
if isempty(p), p = [0 0]; end
p(1) = p(1) + 1;
r = p;
if depth > 0
    inner = give_copy_rec(depth - 1);
    r = [r; inner; p];
end
end

function s = p_returned_copy_isolated_recursive()
s = mat2str(give_copy_rec(1));
end

function v = poke(v)
v(1) = -1;
end

function r = pass_out()
persistent p
if isempty(p), p = [1 2 3]; end
t = poke(p);
r = [p; t];
end

function s = g_argument_pass_leaves_slot()
s = mat2str(pass_out());
end

function r = capture(cmd)
persistent p h
if isempty(p), p = 1; h = @() p; end
if strcmp(cmd, 'bump'), p = p + 1; end
r = [h() p];
end

function s = g_anonymous_capture_is_a_snapshot()
capture('bump');
s = mat2str(capture('bump'));
end

function r = hold_handle(v)
persistent h
if isempty(h), h = HandleHolder(); end
w = h;
w.data = v;
r = h.data;
end

function s = g_handle_object_is_shared()
hold_handle(3);
s = num2str(hold_handle(7));
end

% --- loops ---------------------------------------------------------------------------------------------

function r = loop_sum(n)
persistent acc
if isempty(acc), acc = 0; end
for i = 1:n
    acc = acc + i;
end
r = acc;
end

function s = g_loop_accumulates_across_calls()
loop_sum(100);
s = num2str(loop_sum(100));
end

function r = loop_elems(n)
persistent v
if isempty(v), v = zeros(1, 4); end
for i = 1:n
    v(i) = v(i) + i;
end
r = v;
end

function s = g_loop_elem_writes_across_calls()
loop_elems(4);
s = mat2str(loop_elems(3));
end

function r = loop_rec(depth)
persistent v
if isempty(v), v = zeros(1, 3); end
for i = 1:3
    v(i) = v(i) + 1;
    if depth > 0 && i == 2, loop_rec(depth - 1); end
end
r = v;
end

function s = p_loop_with_recursion()
s = mat2str(loop_rec(1));
end

function r = loop_var(cmd)
persistent k
if strcmp(cmd, 'peek'), r = k; return; end
for k = 1:3
end
r = k;
end

function s = g_loop_variable_is_persistent()
loop_var('go');
s = mat2str(loop_var('peek'));
end

% --- what the workspace says ---------------------------------------------------------------------------

function r = ask_exist()
persistent p
names = who;
r = [exist('p', 'var') any(strcmp(names, 'p')) isempty(p)];
end

function s = g_exist_and_who()
s = mat2str(ask_exist());
end

function r = first_read()
persistent fresh
r = sprintf('%s %s', class(fresh), mat2str(size(fresh)));
end

function s = p_first_read_is_empty_double()
s = first_read();
end

function r = in_branch(flag)
if flag
    persistent p %#ok<PSTAT>
    if isempty(p), p = 0; end
    p = p + 1;
    r = p;
else
    r = -1;
end
end

function s = g_declared_in_a_branch()
in_branch(true);
s = num2str(in_branch(true));
end

function r = clear_var(cmd)
persistent p
if isempty(p), p = 0; end
p = p + 1;
if strcmp(cmd, 'clear')
    clear p
    r = exist('p', 'var');
else
    r = p;
end
end

function s = p_clear_variable_in_owner()
clear_var('go');
a = clear_var('clear');
s = sprintf('%d %d', a, clear_var('go'));
end

% --- clear against a function file (helpers/vp_*.m) ----------------------------------------------------

function s = p_clear_name_inactive_resets()
vp_count(); vp_count();
clear vp_count
s = num2str(vp_count());
end

function s = g_clear_functions_inactive_resets()
vp_count(); vp_count();
clear functions %#ok<CLFUNC>
s = num2str(vp_count());
end

function s = p_clear_functions_local_function()
own_c(); own_c();
clear functions %#ok<CLFUNC>
s = num2str(own_c());
end

function r = own_c()
persistent p
if isempty(p), p = 0; end
p = p + 1;
r = p;
end

function s = p_clear_functions_active_recursive()
clear vp_clear_rec
s = mat2str(vp_clear_rec(2));
end

function s = p_clear_name_active()
clear vp_clear_self
a = vp_clear_self(1);
s = sprintf('%d %d', a, vp_clear_self(0));
end

function s = p_clear_functions_from_sibling()
clear vp_clear_sibling
a = vp_clear_sibling(1);
s = sprintf('%d %d', a, vp_clear_sibling(0));
end

function s = p_clear_functions_in_callback()
clear vp_clear_in_callback
s = num2str(vp_clear_in_callback(1));
end

function s = p_clear_functions_active_then_inactive()
clear vp_clear_rec
vp_clear_rec(1);
clear functions %#ok<CLFUNC>
s = mat2str(vp_clear_rec(0));
end

function s = p_clear_functions_spares_active_resets_others()
clear vp_count vp_clear_other
vp_count(); vp_count();
a = vp_clear_other();
s = sprintf('%d %d', a, vp_count());
end
