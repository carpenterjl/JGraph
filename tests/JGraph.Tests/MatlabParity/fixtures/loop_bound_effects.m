% loop_bound_effects.m -- V8 of the value-ownership plan (ADR 0169): a for loop's bounds run first,
% once, in the order written, and the loop - compiled or walked - reads its variables as the bounds
% left them (#37). A bound that rebinds, grows, clears or demotes a vector the body writes; one that
% rebinds, clears or promotes a scalar the body reads; start, step and stop each with an effect, in
% order, exactly once, whether the loop then runs or not; a bound that throws after an earlier bound
% ran; a stop that reads the loop variable's prior binding, and a bound that rebinds the loop
% variable; the 0-by-0 double a zero-trip loop leaves in its variable whatever its head was
% (measured: a range, an empty array, cell, char or string, an integer range; a 0-by-3 source runs
% three passes over 0-by-1 columns); an inner loop's bound with an effect
% on every pass of the outer; and a bound that assigns a builtin's name into the loop's workspace,
% which shadows the builtin in a script (measured) and not in a function, where MATLAB binds the
% name at parse time (measured).

run_case('lb_rebinds_vector', @lb_rebinds_vector);
run_case('lb_grows_vector', @lb_grows_vector);
run_case('lb_clears_vector', @lb_clears_vector);
run_case('lb_demotes_vector', @lb_demotes_vector);
run_case('lb_step_rebinds_vector', @lb_step_rebinds_vector);
run_case('lb_stop_grows_after_start_read', @lb_stop_grows_after_start_read);
run_case('lb_rebinds_scalar', @lb_rebinds_scalar);
run_case('lb_clears_scalar', @lb_clears_scalar);
run_case('lb_promotes_scalar', @lb_promotes_scalar);
run_case('lb_demotes_vector_body_reads', @lb_demotes_vector_body_reads);
run_case('lb_start_step_stop_order', @lb_start_step_stop_order);
run_case('lb_bounds_run_once_zero_trip', @lb_bounds_run_once_zero_trip);
run_case('lb_bound_throws', @lb_bound_throws);
run_case('lb_stop_reads_prior_loop_variable', @lb_stop_reads_prior_loop_variable);
run_case('lb_bound_rebinds_loop_variable', @lb_bound_rebinds_loop_variable);
run_case('lb_zero_trip_loop_variable', @lb_zero_trip_loop_variable);
run_case('lb_zero_trip_over_empties', @lb_zero_trip_over_empties);
run_case('lb_zero_trip_over_zero_rows', @lb_zero_trip_over_zero_rows);
run_case('lb_inner_bound_effect_each_pass', @lb_inner_bound_effect_each_pass);
run_case('lb_shadows_builtin_in_script', @lb_shadows_builtin_in_script);
run_case('lb_shadow_ignored_in_function', @lb_shadow_ignored_in_function, 'div=ADR0169');

function run_case(name, fn, rule)
if nargin < 3
    rule = 'exact';
end
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

% --- a bound with an effect on a vector the body writes (#37) --------------------------------------

function s = lb_rebinds_vector()
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

function s = lb_grows_vector()
x = zeros(1, 3);
for k = 1:growx()
    x(k) = k;
end
s = mat2str(x);
    function n = growx()
        x(end + 1) = 9;
        n = 4;
    end
end

function s = lb_clears_vector()
x = zeros(1, 3);
for k = 1:clearx()
    x(k) = k * 10;
end
s = mat2str(x);
    function n = clearx()
        clear x
        n = 3;
    end
end

function s = lb_demotes_vector()
x = zeros(1, 3);
for k = 1:demotex()
    x(k) = k * 10;
end
s = mat2str(x);
    function n = demotex()
        x = 5;
        n = 3;
    end
end

function s = lb_step_rebinds_vector()
x = zeros(1, 4);
for k = 1:stepx():4
    x(k) = k;
end
s = mat2str(x);
    function n = stepx()
        x = 7 * ones(1, 4);
        n = 2;
    end
end

function s = lb_stop_grows_after_start_read()
x = [1 2];
for k = numel(x):growx()
    x(k) = x(k) * 10;
end
s = mat2str(x);
    function n = growx()
        x(end + 1) = 3;
        n = 3;
    end
end

% --- a bound with an effect on a scalar the body reads ----------------------------------------------

function s = lb_rebinds_scalar()
a = 1;
t = 0;
for k = 1:seta()
    t = t + a * k;
end
s = sprintf('%d %d', t, a);
    function n = seta()
        a = 5;
        n = 3;
    end
end

function s = lb_clears_scalar()
a = 1;
t = 0;
for k = 1:cleara()
    if k == 1
        a = 10;
    end
    t = t + a;
end
s = sprintf('%d %d', t, a);
    function n = cleara()
        clear a
        n = 2;
    end
end

function s = lb_promotes_scalar()
a = 1;
t = 0;
for k = 1:veca()
    t = t + a(k);
end
s = sprintf('%d %s', t, mat2str(a));
    function n = veca()
        a = [1 2 3];
        n = 3;
    end
end

function s = lb_demotes_vector_body_reads()
a = [1 2 3];
t = 0;
for k = 1:scalara()
    t = t + a;
end
s = sprintf('%d %d', t, a);
    function n = scalara()
        a = 5;
        n = 3;
    end
end

% --- start, step and stop: each once, in order, whether the loop runs or not ---------------------

function s = lb_start_step_stop_order()
log = '';
t = 0;
for k = tick('s'):tick('d'):tick('e')
    t = t + k;
end
s = sprintf('%s %d', log, t);
    function n = tick(c)
        log = [log c];
        if c == 'e'
            n = 3;
        else
            n = 1;
        end
    end
end

function s = lb_bounds_run_once_zero_trip()
log = '';
t = 0;
for k = tick('s'):tick('z')
    t = t + 1;
end
s = sprintf('%s %d', log, t);
    function n = tick(c)
        log = [log c];
        if c == 'z'
            n = 0;
        else
            n = 1;
        end
    end
end

function s = lb_bound_throws()
log = '';
x = zeros(1, 3);
try
    for k = tick('s'):boom()
        x(k) = k;
    end
    s = 'no error';
catch err
    s = sprintf('%s/%s/%s', log, err.message, mat2str(x));
end
    function n = tick(c)
        log = [log c];
        n = 1;
    end
end

function n = boom()
error('lb:boom', 'the stop threw');
end

% --- the loop variable ------------------------------------------------------------------------------

function s = lb_stop_reads_prior_loop_variable()
k = 3;
t = 0;
for k = 1:k + 1
    t = t + k;
end
s = sprintf('%d %d', t, k);
end

function s = lb_bound_rebinds_loop_variable()
k = 0;
t = 0;
for k = 1:setk()
    t = t + k;
end
s = sprintf('%d %d', t, k);
    function n = setk()
        k = 99;
        n = 3;
    end
end

function s = lb_zero_trip_loop_variable()
for k = 1:0
end
j = 5;
for j = 1:0
end
s = sprintf('%d %s %s', exist('k', 'var'), mat2str(size(k)), mat2str(j));
end

function s = lb_zero_trip_over_empties()
a = 'was'; b = 'was'; c = 'was'; d = 'was'; e = 'was'; f = 'was'; g = 'was';
for a = []
end
for b = zeros(1, 0)
end
for c = {}
end
for d = ''
end
for e = strings(1, 0)
end
for f = int8(1):int8(0)
end
for g = 5:-1:6
end
s = sprintf('%s %s %s %s %s %s %s', shape(a), shape(b), shape(c), shape(d), shape(e), shape(f), shape(g));
end

function s = lb_zero_trip_over_zero_rows()
j = 'was';
n = 0;
for j = zeros(0, 3)
    n = n + 1;
end
s = sprintf('%d %s', n, shape(j));
end

function s = shape(v)
s = sprintf('%s[%s]', class(v), strjoin(string(size(v)), 'x'));
end

% --- an inner loop's bound with an effect on every pass ---------------------------------------------

function s = lb_inner_bound_effect_each_pass()
c = 0;
t = 0;
for i = 1:2
    for j = 1:bump()
        t = t + j;
    end
end
s = sprintf('%d %d', t, c);
    function n = bump()
        c = c + 1;
        n = c;
    end
end

% --- a bound that assigns a builtin's name into the loop's workspace ----------------------------------

function s = lb_shadows_builtin_in_script()
lb_shadow_script
s = mat2str(y);
end

function s = lb_shadow_ignored_in_function()
y = zeros(1, 3);
for k = 1:lb_shadow_abs()
    y(k) = abs(k);
end
s = mat2str(y);
end
