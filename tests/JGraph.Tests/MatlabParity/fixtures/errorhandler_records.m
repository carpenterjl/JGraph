% errorhandler_records.m -- V6 of the value-ownership plan: what cellfun and arrayfun hand an
% 'ErrorHandler' (appendix A #54), and mat2str of a negative zero (#163). The record carries the
% failure: its identifier, its message under R2025b's "Error using <function> (line N)" header
% where the failing function has a name, and the index of the element that failed.

run_case('h_cellfun_identifier_fn', @h_cellfun_identifier_fn);
run_case('h_arrayfun_identifier_fn', @h_arrayfun_identifier_fn);
run_case('h_cellfun_identifier_anon', @h_cellfun_identifier_anon);
run_case('h_record_fields', @h_record_fields);
run_case('h_message_local_function', @h_message_local_function);
run_case('h_message_anonymous_error', @h_message_anonymous_error);
run_case('h_message_anonymous_calls_local', @h_message_anonymous_calls_local);
run_case('h_message_builtin_failure', @h_message_builtin_failure, 'div=ADR0167');
run_case('h_message_no_identifier', @h_message_no_identifier);
run_case('h_index_nonuniform', @h_index_nonuniform);
run_case('h_index_arrayfun_matrix', @h_index_arrayfun_matrix);
run_case('h_handler_gets_the_inputs', @h_handler_gets_the_inputs);
run_case('h_handler_two_inputs', @h_handler_two_inputs);
run_case('h_handler_two_outputs', @h_handler_two_outputs);
run_case('h_handler_branches_on_identifier', @h_handler_branches_on_identifier);
run_case('h_handler_that_errors', @h_handler_that_errors);
run_case('h_handler_rethrows_record', @h_handler_rethrows_record);
run_case('h_only_failures_reach_the_handler', @h_only_failures_reach_the_handler);
run_case('h_record_is_a_struct', @h_record_is_a_struct);
run_case('z_mat2str_negative_zero', @z_mat2str_negative_zero);
run_case('z_mat2str_negative_zero_array', @z_mat2str_negative_zero_array);
run_case('z_mat2str_negative_zero_complex', @z_mat2str_negative_zero_complex);
run_case('z_mat2str_negative_zero_precision', @z_mat2str_negative_zero_precision);
run_case('z_mat2str_small_negative', @z_mat2str_small_negative);

function run_case(name, fn, rule)
% The rule is exact unless a case names its accepted divergence: h_message_builtin_failure (an
% error the runtime raises itself has no identifier and no "Error using" header; ADR 0062, 0167).
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

function y = failing(x)
error('probe:bad', 'bad %d', x);
y = x; %#ok<UNRCH>
end

function y = plain_failing(x)
error('no identifier here %d', x);
y = x; %#ok<UNRCH>
end

function y = fails_on_even(x)
if mod(x, 2) == 0
    error('probe:even', 'even %d', x);
end
y = x * 10;
end

function [a, b] = two_out(x)
if x > 1
    error('probe:big', 'big');
end
a = x; b = -x;
end

function s = h_cellfun_identifier_fn()
r = cellfun(@failing, {1}, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'));
s = num2str(r);
end

function s = h_arrayfun_identifier_fn()
r = arrayfun(@failing, 1, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'));
s = num2str(r);
end

function s = h_cellfun_identifier_anon()
r = cellfun(@(x) error('probe:bad', 'bad'), {1}, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'));
s = num2str(r);
end

function s = h_record_fields()
r = cellfun(@failing, {1}, 'UniformOutput', false, 'ErrorHandler', @(e, x) strjoin(fieldnames(e)', ','));
s = r{1};
end

function s = h_message_local_function()
r = cellfun(@failing, {1}, 'UniformOutput', false, 'ErrorHandler', @(e, x) e.message);
s = r{1};
end

function s = h_message_anonymous_error()
r = cellfun(@(x) error('probe:bad', 'bad %d', x), {4}, 'UniformOutput', false, 'ErrorHandler', @(e, x) e.message);
s = r{1};
end

function s = h_message_anonymous_calls_local()
r = cellfun(@(x) failing(x) + 1, {5}, 'UniformOutput', false, 'ErrorHandler', @(e, x) e.message);
s = r{1};
end

function s = h_message_builtin_failure()
r = cellfun(@(x) ones(2) * ones(3), {1}, 'UniformOutput', false, ...
    'ErrorHandler', @(e, x) sprintf('%s#%d', e.identifier, contains(e.message, 'Error using')));
s = r{1};
end

function s = h_message_no_identifier()
r = cellfun(@plain_failing, {3}, 'UniformOutput', false, ...
    'ErrorHandler', @(e, x) sprintf('[%s]#%s', e.identifier, e.message));
s = r{1};
end

function s = h_index_nonuniform()
r = cellfun(@failing, {1, 2}, 'UniformOutput', false, ...
    'ErrorHandler', @(e, x) sprintf('%s#%d', e.identifier, e.index));
s = strjoin(r, ',');
end

function s = h_index_arrayfun_matrix()
r = arrayfun(@fails_on_even, [1 2; 3 4], 'ErrorHandler', @(e, x) -e.index);
s = mat2str(r);
end

function s = h_handler_gets_the_inputs()
r = cellfun(@failing, {7, 8}, 'ErrorHandler', @(e, x) x * 100);
s = mat2str(r);
end

function s = h_handler_two_inputs()
r = cellfun(@(a, b) failing(a + b), {1, 2}, {10, 20}, 'ErrorHandler', @(e, a, b) a + b);
s = mat2str(r);
end

function s = h_handler_two_outputs()
[p, q] = arrayfun(@two_out, [1 2], 'ErrorHandler', @two_out_handler);
s = sprintf('%s %s', mat2str(p), mat2str(q));
end

function [a, b] = two_out_handler(e, x)
a = e.index * 100; b = x;
end

function s = h_handler_branches_on_identifier()
r = arrayfun(@fails_on_even, 1:4, 'ErrorHandler', @pick);
s = mat2str(r);
end

function y = pick(e, x)
if strcmp(e.identifier, 'probe:even')
    y = -x;
else
    y = NaN;
end
end

function s = h_handler_that_errors()
try
    cellfun(@failing, {1}, 'ErrorHandler', @(e, x) error('probe:handler', 'handler failed'));
    s = 'no error';
catch err
    s = sprintf('%s %s', err.identifier, err.message);
end
end

function s = h_handler_rethrows_record()
try
    cellfun(@failing, {1}, 'ErrorHandler', @(e, x) rethrow(e));
    s = 'no error';
catch err
    s = sprintf('%s %d', err.identifier, contains(err.message, 'bad 1'));
end
end

function s = h_only_failures_reach_the_handler()
r = arrayfun(@fails_on_even, 1:4, 'ErrorHandler', @(e, x) 0);
s = mat2str(r);
end

function s = h_record_is_a_struct()
r = cellfun(@failing, {1}, 'UniformOutput', false, 'ErrorHandler', @(e, x) class(e));
s = r{1};
end

% --- mat2str and the sign of zero (#163) -------------------------------------------------------------

function s = z_mat2str_negative_zero()
s = mat2str(-0);
end

function s = z_mat2str_negative_zero_array()
s = mat2str([0 -0 1; -0 2 -3]);
end

function s = z_mat2str_negative_zero_complex()
s = mat2str([complex(-0, 1) complex(2, -0)]);
end

function s = z_mat2str_negative_zero_precision()
s = mat2str([-0 -0.5], 3);
end

function s = z_mat2str_small_negative()
s = mat2str([-1e-320 -0.0001]);
end
