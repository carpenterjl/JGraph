% value_isolation_calls.m -- appendix A of the value-ownership plan: the call contract. A persistent
% variable as one binding (M9, V5), the lexical workspace boundary of nested functions and clear in
% the active frame (M12, V7), a compiled loop loading its registers after its bounds (M13, V8), and
% the output count, the call site and the maxlhs refusal (V9). A case named aNNN is appendix row
% NNN; g_ cases agree on both engines today. The two script-level cases (a clear inside a callback
% reaching the base workspace, #64 and #65) run last, after every other line has printed, so
% whatever they clear cannot reach an earlier one.

run_case('a017_persistent_recursive_elem', @a017_persistent_recursive_elem);
run_case('a018_persistent_recursive_rebind', @a018_persistent_recursive_rebind);
run_case('a036_nested_three_levels', @a036_nested_three_levels);
run_case('a037_loop_bound_rebinds', @a037_loop_bound_rebinds);
run_case('a055_disp_asked_for_value', @a055_disp_asked_for_value);
run_case('a055_error_asked_for_value', @a055_error_asked_for_value);
run_case('a055_anon_error_asked_for_value', @a055_anon_error_asked_for_value);
run_case('a077_nargout_statement_calls', @a077_nargout_statement_calls);
run_case('g_nargout_value_calls', @g_nargout_value_calls);
run_case('a078_inputname_multi_output_handle', @a078_inputname_multi_output_handle);
run_case('g_inputname_named_call', @g_inputname_named_call);
run_case('a079_inputname_feval', @a079_inputname_feval);
run_case('g_tilde_first_output', @g_tilde_first_output);
run_case('g_tilde_deal', @g_tilde_deal);
run_case('g_tilde_nargout_seen', @g_tilde_nargout_seen);
run_case('g_ans_after_zero_output_call', @g_ans_after_zero_output_call);
run_case('a081_ans_after_varargout_statement', @a081_ans_after_varargout_statement);
run_case('a084_tilde_unassigned_output', @a084_tilde_unassigned_output);
run_case('a084_tilde_unassigned_nargout', @a084_tilde_unassigned_nargout);
run_case('g_feval_by_name', @g_feval_by_name);
run_case('g_nargin_in_nested', @g_nargin_in_nested);
run_case('g_handle_recursion_accumulates', @g_handle_recursion_accumulates);
run_case('g_nested_handle_after_return', @g_nested_handle_after_return);
run_case('g_clear_functions_inactive_resets', @g_clear_functions_inactive_resets);
run_case('a072_clear_functions_active_persistent', @a072_clear_functions_active_persistent);

victim = 42;
r = cellfun(@wipe, {0});
fprintf('CHK|a064_clear_named_in_callback|%d %d|exact\n', r, exist('victim', 'var'));
keep = 5; %#ok<NASGU>
r2 = cellfun(@wipe_all, {0});
fprintf('CHK|a065_clear_all_in_callback|%d %d|exact\n', r2, exist('keep', 'var'));

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

% --- a persistent variable under recursion (borrow_probe4.m) --------------------------------------

function r = pcount_elem(depth)
persistent p
if isempty(p), p = zeros(1, 3); end
p(1) = p(1) + 1;
if depth > 0, pcount_elem(depth - 1); end
p(2) = p(2) + 10;
r = p;
end

function r = pcount_rebind(depth)
persistent p
if isempty(p), p = zeros(1, 3); end
p = p + [1 0 0];
if depth > 0, pcount_rebind(depth - 1); end
p = p + [0 10 0];
r = p;
end

function s = a017_persistent_recursive_elem()
s = mat2str(pcount_elem(1));
end

function s = a018_persistent_recursive_rebind()
s = mat2str(pcount_rebind(1));
end

% --- nested workspaces and a compiled loop's bounds (borrow_probe8.m) -----------------------------

function s = a036_nested_three_levels()
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

function s = a037_loop_bound_rebinds()
x = zeros(1, 5);
for k = 1:resetx()
    x(k) = k;
end
s = mat2str(x);
    function n = resetx()
        x = 7 * ones(1, 5);
        n = 1;
    end
end

% --- a function with no outputs asked for a value (borrow_probe10b.m) -----------------------------

function s = a055_disp_asked_for_value()
try
    y = disp(1); %#ok<NASGU>
    s = 'no error';
catch e
    s = e.identifier;
end
end

function s = a055_error_asked_for_value()
try
    y = error('probe:bad', 'bad'); %#ok<NASGU>
    s = 'no error';
catch e
    s = e.identifier;
end
end

function s = a055_anon_error_asked_for_value()
try
    f = @(x) error('probe:bad', 'bad');
    y = f(1); %#ok<NASGU>
    s = 'no error';
catch e
    s = e.identifier;
end
end

% --- nargout for statement calls, inputname through handles (borrow_probe14.m) --------------------

function y = nout_probe()
vlog(sprintf('%d', nargout));
y = [];
end

function s = a077_nargout_statement_calls()
nout_probe();
h = @nout_probe;
h();
feval(h);
feval('nout_probe');
s = logged_text();
end

function s = g_nargout_value_calls()
v = nout_probe(); %#ok<NASGU>
h = @nout_probe;
v = h(); %#ok<NASGU>
v = feval(h); %#ok<NASGU>
s = logged_text();
end

function [a, b] = two_named(v)
a = inputname(1);
b = v;
end

function s = a078_inputname_multi_output_handle()
x = 5; h = @two_named;
[a, b] = h(x);
s = sprintf('%s %d', a, b);
end

function s = g_inputname_named_call()
x = 5;
[a, b] = two_named(x);
s = sprintf('%s %d', a, b);
end

function s = a079_inputname_feval()
x = 5;
[a, b] = feval(@two_named, x);
s = sprintf('%s %d', a, b);
end

% --- ignored outputs, ans after statement calls (borrow_probe15.m, borrow_probe16.m) --------------

function [a, b] = two_out()
a = [1 2 3];
b = [4 5 6];
end

function s = g_tilde_first_output()
[~, b] = two_out();
s = mat2str(b);
end

function s = g_tilde_deal()
v = [1 2 3];
[~, b] = deal(7, v);
b(1) = 9;
s = sprintf('%s %s', mat2str(v), mat2str(b));
end

function varargout = count_out()
varargout = cell(1, nargout);
for k = 1:nargout
    varargout{k} = nargout;
end
end

function s = g_tilde_nargout_seen()
[~, b] = count_out();
s = num2str(b);
end

function none_out()
end

function s = g_ans_after_zero_output_call()
clear ans
none_out();
s = num2str(exist('ans', 'var'));
end

function varargout = maybe_out()
if nargout > 0
    varargout{1} = 5;
end
end

function s = a081_ans_after_varargout_statement()
clear ans
maybe_out();
s = num2str(exist('ans', 'var'));
end

function [a, b] = second_only()
b = 2;
end

function s = a084_tilde_unassigned_output()
[~, b] = second_only();
s = num2str(b);
end

function [a, b] = second_only_count()
b = nargout;
end

function s = a084_tilde_unassigned_nargout()
[~, b] = second_only_count();
s = num2str(b);
end

% --- name-based calls, nargin in a nested function, handles (borrow_probe11.m, borrow_probe13.m) --

function s = g_feval_by_name()
s = num2str(feval('plus', 2, 3));
end

function r = outer_nargin(a, b) %#ok<INUSD>
r = inner();
    function q = inner()
        q = sprintf('%d', nargin);
    end
end

function s = g_nargin_in_nested()
s = outer_nargin(1, 2);
end

function [acc, total] = rec(f, n, acc)
if n == 0
    total = 0;
    return
end
acc{end + 1} = f(n);
[acc, rest] = rec(f, n - 1, acc);
total = n + rest;
end

function s = g_handle_recursion_accumulates()
acc = {};
f = @(n) n;
[acc, total] = rec(f, 3, acc);
s = sprintf('%d %d', numel(acc), total);
end

function [g, st] = make_counter()
count = 0;
g = @() count;
st = @(v) setcount(v);
    function setcount(v)
        count = v;
    end
end

function s = g_nested_handle_after_return()
[get, set] = make_counter();
set(5);
s = num2str(get());
end

% --- clear functions against an inactive and an active function (borrow_probe12.m, #72) ----------

function y = counter_fn()
persistent n
if isempty(n), n = 0; end
n = n + 1;
y = n;
end

function s = g_clear_functions_inactive_resets()
counter_fn();
counter_fn();
clear counter_fn
s = num2str(counter_fn());
end

function s = a072_clear_functions_active_persistent()
s = num2str(clear_active(1));
end

% --- clear inside a callback (clear_callback_script.m) --------------------------------------------

function y = wipe(~)
victim = 7; %#ok<NASGU>
clear victim
y = exist('victim', 'var');
end

function y = wipe_all(~)
keep = 7; %#ok<NASGU>
clear
y = exist('keep', 'var');
end
