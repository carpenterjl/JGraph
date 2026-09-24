% nested_workspaces.m -- V7 of the value-ownership plan (ADR 0168): a nested function's workspace
% boundary is lexical. A frame is a call boundary exactly when its function is not nested in the
% function whose frame is its closure, so a write from two, three or four levels down reaches the
% outer variable (#36); a sibling nested function shares the same workspace; a name only a nested
% function uses is its own and fresh each call, as are its parameters and outputs whatever the
% parent holds under the name; global and persistent at each level; a nested
% function returned as a handle keeps its parent's workspace alive after the parent returned;
% clear inside a nested function reaches the parent's variables; nargin and nargout at each level;
% and a local (non-nested) function called from a nested one is a boundary. Measured in R2025b
% before the fixture was written: a parent that reads a name only a nested function assigns, and a
% global declared at two levels, are refused at parse time, so neither is here.

run_case('n_rebind_two_levels', @n_rebind_two_levels);
run_case('n_rebind_three_levels', @n_rebind_three_levels);
run_case('n_rebind_four_levels', @n_rebind_four_levels);
run_case('n_elem_write_four_levels', @n_elem_write_four_levels);
run_case('n_growth_three_levels', @n_growth_three_levels);
run_case('n_delete_three_levels', @n_delete_three_levels);
run_case('n_field_write_three_levels', @n_field_write_three_levels);
run_case('n_cell_write_three_levels', @n_cell_write_three_levels);
run_case('n_grandparent_shares_skipping_middle', @n_grandparent_shares_skipping_middle);
run_case('n_middle_and_inner_write', @n_middle_and_inner_write);
run_case('n_sibling_shares', @n_sibling_shares);
run_case('n_sibling_calls_sibling', @n_sibling_calls_sibling);
run_case('n_nested_only_is_local', @n_nested_only_is_local);
run_case('n_nested_only_fresh_each_call', @n_nested_only_fresh_each_call);
run_case('n_siblings_without_parent', @n_siblings_without_parent);
run_case('n_parent_inits_after_nested_writes', @n_parent_inits_after_nested_writes);
run_case('n_parameter_is_local', @n_parameter_is_local);
run_case('n_output_is_local', @n_output_is_local);
run_case('n_output_written_twice', @n_output_written_twice);
run_case('n_global_in_parent_nested_writes', @n_global_in_parent_nested_writes);
run_case('n_global_in_nested_only', @n_global_in_nested_only);
run_case('n_persistent_in_parent_nested_writes', @n_persistent_in_parent_nested_writes);
run_case('n_persistent_in_nested_own', @n_persistent_in_nested_own);
run_case('n_persistent_in_nested_own_again', @n_persistent_in_nested_own);
run_case('n_escaped_counter', @n_escaped_counter);
run_case('n_escaped_two_instances', @n_escaped_two_instances);
run_case('n_escaped_shared_state', @n_escaped_shared_state);
run_case('n_escaped_inner_of_inner', @n_escaped_inner_of_inner);
run_case('n_nargin_each_level', @n_nargin_each_level);
run_case('n_nargout_each_level', @n_nargout_each_level);
run_case('n_local_function_is_boundary', @n_local_function_is_boundary);
run_case('n_local_function_sees_nothing', @n_local_function_sees_nothing);
run_case('n_clear_shared_in_nested', @n_clear_shared_in_nested);
run_case('n_clear_unshared_in_nested', @n_clear_unshared_in_nested);
run_case('n_clear_plain_in_nested', @n_clear_plain_in_nested);
run_case('n_clear_plain_keeps_parent_persistent', @n_clear_plain_keeps_parent_persistent);
run_case('n_clear_all_in_nested', @n_clear_all_in_nested);
run_case('n_clear_all_takes_parent_persistent', @n_clear_all_takes_parent_persistent);
run_case('n_clear_then_write_in_nested', @n_clear_then_write_in_nested);

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

% --- writes from two, three and four levels of nesting (#36) ---------------------------------------

function s = n_rebind_two_levels()
v = [1 2];
inner();
s = mat2str(v);
    function inner()
        v = v + [10 0];
    end
end

function s = n_rebind_three_levels()
v = [1 2];
middle();
s = mat2str(v);
    function middle()
        inner();
        function inner()
            v = v + [10 0];
        end
    end
end

function s = n_rebind_four_levels()
v = [1 2];
l2();
s = mat2str(v);
    function l2()
        l3();
        function l3()
            l4();
            function l4()
                v = v + [10 0];
            end
        end
    end
end

function s = n_elem_write_four_levels()
v = [1 2];
l2();
s = mat2str(v);
    function l2()
        l3();
        function l3()
            l4();
            function l4()
                v(2) = v(2) + 100;
            end
        end
    end
end

function s = n_growth_three_levels()
v = [1 2];
middle();
s = mat2str(v);
    function middle()
        inner();
        function inner()
            v(end + 1) = 3;
        end
    end
end

function s = n_delete_three_levels()
v = [1 2 3];
middle();
s = mat2str(v);
    function middle()
        inner();
        function inner()
            v(2) = [];
        end
    end
end

function s = n_field_write_three_levels()
st.f = [1 2];
middle();
s = mat2str(st.f);
    function middle()
        inner();
        function inner()
            st.f(2) = 9;
        end
    end
end

function s = n_cell_write_three_levels()
c = {1, 'a'};
middle();
s = sprintf('%d %s', c{1}, c{2});
    function middle()
        inner();
        function inner()
            c{2} = 'zz';
        end
    end
end

function s = n_grandparent_shares_skipping_middle()
v = [1 2];
middle();
s = mat2str(v);
    function middle()
        inner();
        function inner()
            v(2) = 9;
        end
    end
end

function s = n_middle_and_inner_write()
v = 1;
middle();
s = num2str(v);
    function middle()
        v = v + 10;
        inner();
        v = v + 100;
        function inner()
            v = v + 1000;
        end
    end
end

% --- sibling nested functions ---------------------------------------------------------------------

function s = n_sibling_shares()
v = 1;
first();
second();
s = num2str(v);
    function first()
        v = v + 10;
    end
    function second()
        v = v * 2;
    end
end

function s = n_sibling_calls_sibling()
v = 1;
first();
s = num2str(v);
    function first()
        v = v + 10;
        second();
    end
    function second()
        v = v * 2;
    end
end

% --- a name only a nested function uses is its own ---------------------------------------------

function s = n_nested_only_is_local()
inner();
s = num2str(exist('u', 'var'));
    function inner()
        u = 3; %#ok<NASGU>
    end
end

function s = n_nested_only_fresh_each_call()
s = sprintf('%d %d', inner(), inner());
    function r = inner()
        if ~exist('u', 'var'), u = 0; end
        u = u + 1;
        r = u;
    end
end

function s = n_siblings_without_parent()
s = sprintf('%d %d', first(), second());
    function r = first()
        q = 1;
        r = q;
    end
    function r = second()
        r = exist('q', 'var');
    end
end

function s = n_parent_inits_after_nested_writes()
inner();
t = 1;
s = num2str(t);
    function inner()
        t = 5;
    end
end

function s = n_parameter_is_local()
v = 1;
inner(5);
s = num2str(v);
    function inner(v)
        v = v + 1; %#ok<NASGU>
    end
end

function s = n_output_is_local()
r = 1;
x = inner();
s = sprintf('%d %d', r, x);
    function r = inner()
        r = 5;
    end
end

function s = n_output_written_twice()
r = 1;
x = inner();
y = inner();
s = sprintf('%d %d %d', r, x, y);
    function r = inner()
        r = 5;
        r = r + 1;
    end
end

% --- global and persistent at each level ---------------------------------------------------------

function s = n_global_in_parent_nested_writes()
global nw_g1
nw_g1 = 5;
inner();
s = sprintf('%d %d', nw_g1, read_g1());
    function inner()
        nw_g1 = nw_g1 + 1;
    end
end

function r = read_g1()
global nw_g1
r = nw_g1;
end

function s = n_global_in_nested_only()
inner();
s = num2str(read_g2());
    function inner()
        global nw_g2
        nw_g2 = 7;
    end
end

function r = read_g2()
global nw_g2
r = nw_g2;
end

function s = n_persistent_in_parent_nested_writes()
s = sprintf('%d %d', pcount(), pcount());
end

function r = pcount()
persistent p
if isempty(p), p = 0; end
inner();
r = p;
    function inner()
        p = p + 1;
    end
end

function s = n_persistent_in_nested_own()
s = sprintf('%d %d', inner(), inner());
    function r = inner()
        persistent k
        if isempty(k), k = 0; end
        k = k + 1;
        r = k;
    end
end

% --- a nested function returned as a handle, called after its parent returned --------------------

function h = make_counter()
n = 0;
h = @bump;
    function r = bump()
        n = n + 1;
        r = n;
    end
end

function s = n_escaped_counter()
h = make_counter();
h(); h();
s = num2str(h());
end

function s = n_escaped_two_instances()
a = make_counter();
b = make_counter();
a(); a();
s = sprintf('%d %d', b(), a());
end

function [inc, get] = make_pair()
n = 0;
inc = @bump;
get = @read;
    function bump()
        n = n + 1;
    end
    function r = read()
        r = n;
    end
end

function s = n_escaped_shared_state()
[inc, get] = make_pair();
inc(); inc();
s = num2str(get());
end

function h = make_deep()
n = 10;
h = middle();
    function g = middle()
        g = @inner;
        function r = inner()
            n = n + 1;
            r = n;
        end
    end
end

function s = n_escaped_inner_of_inner()
h = make_deep();
h();
s = num2str(h());
end

% --- nargin and nargout at each level -------------------------------------------------------------

function s = n_nargin_each_level()
s = outer_in(1, 2);
end

function s = outer_in(a, b) %#ok<INUSD>
s = sprintf('%d %s', nargin, middle(a));
    function t = middle(x) %#ok<INUSD>
        t = sprintf('%d %s', nargin, inner());
        function u = inner()
            u = sprintf('%d', nargin);
        end
    end
end

function s = n_nargout_each_level()
s = outer_out();
end

function s = outer_out()
[p, q] = middle();
s = sprintf('%d %d %d', nargout, p, q);
    function [a, b] = middle()
        a = nargout;
        b = inner();
        function r = inner()
            r = nargout;
        end
    end
end

% --- a local function called from a nested one is a boundary ------------------------------------

function s = n_local_function_is_boundary()
v = [1 2];
inner();
s = mat2str(v);
    function inner()
        helper_rebind();
        v(2) = v(2) + 5;
    end
end

function helper_rebind()
v = [9 9]; %#ok<NASGU>
end

function s = n_local_function_sees_nothing()
v = 1; %#ok<NASGU>
s = inner();
    function r = inner()
        r = num2str(helper_looks());
    end
end

function r = helper_looks()
r = exist('v', 'var');
end

% --- clear inside a nested function ----------------------------------------------------------------

function s = n_clear_shared_in_nested()
v = 1; w = 2;
inner();
s = sprintf('%d %d', exist('v', 'var'), w);
    function inner()
        t = v; %#ok<NASGU>
        clear v
    end
end

function s = n_clear_unshared_in_nested()
v = 1;
inner();
s = sprintf('%d', exist('v', 'var'));
    function inner()
        clear v
    end
end

function s = n_clear_plain_in_nested()
v = 1; w = 2;
inner();
s = sprintf('%d %d', exist('v', 'var'), exist('w', 'var'));
    function inner()
        t = v + w; %#ok<NASGU>
        clear
    end
end

function r = clear_plain_owner(cmd)
persistent q
if isempty(q), q = 0; end
q = q + 5;
if strcmp(cmd, 'set')
    inner();
    r = exist('q', 'var');
else
    r = q;
end
    function inner()
        clear
    end
end

function s = n_clear_plain_keeps_parent_persistent()
s = sprintf('%d %d', clear_plain_owner('set'), clear_plain_owner('get'));
end

function s = n_clear_all_in_nested()
v = 1; w = 2;
inner();
s = sprintf('%d %d', exist('v', 'var'), exist('w', 'var'));
    function inner()
        t = v + w; %#ok<NASGU>
        clear all %#ok<CLALL>
    end
end

function r = clear_all_owner(cmd)
persistent q
if isempty(q), q = 0; end
q = q + 5;
if strcmp(cmd, 'set')
    inner();
    r = exist('q', 'var');
else
    r = q;
end
    function inner()
        clear all %#ok<CLALL>
    end
end

function s = n_clear_all_takes_parent_persistent()
s = sprintf('%d %d', clear_all_owner('set'), clear_all_owner('get'));
end

function s = n_clear_then_write_in_nested()
v = 1;
inner();
s = mat2str(v);
    function inner()
        clear v
        v = [7 8];
    end
end
