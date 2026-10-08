% Open items 13, 16 and 79 and the remediation plan's item 7 (ADR 0214): the error records a script
% branches on. A subscript, a brace, a size, a bracket, an assignment, an unknown name and an arity
% refusal carry R2025b's identifier and words; an error at a script's top level has a frame naming
% the script, and getReport heads an error() with it. Probe probe_b4 (open-items scratch).
x = 1;
v = [10 20 30];
M = magic(3);
A = zeros(2, 2, 2);
c = {1, 2};
s = struct('a', {1, 2});
t = "abc";
ch = 'ab';
L = [true false];
f = @sin;

% --- subscripts (item 79) ---
u9b_chk('sub_past_scalar', @() x(2));
u9b_chk('sub_zero', @() x(0));
u9b_chk('sub_negative', @() x(-1));
u9b_chk('sub_fraction', @() x(2.5));
u9b_chk('sub_past_row', @() v(4));
u9b_chk('sub_past_in_list', @() v([1 4]));
u9b_chk('sub_end_plus', @() v(end+1));
u9b_chk('sub_mask_true_past', @() v(logical([1 0 0 1])));
u9b_chk('sub_mask_false_past', @() v(logical([1 0 0 0])));
u9b_chk('sub_mask_short', @() v(logical([0 1])));
u9b_chk('sub_nan', @() v(NaN));
u9b_chk('sub_inf', @() v(Inf));
u9b_chk('sub_row_past', @() M(4, 1));
u9b_chk('sub_col_past', @() M(1, 5));
u9b_chk('sub_row_zero', @() M(0, 1));
u9b_chk('sub_row_fraction', @() M(1.5, :));
u9b_chk('sub_col_fraction', @() M(:, 1.5));
u9b_chk('sub_linear_matrix', @() M(10));
u9b_chk('sub_page_past', @() A(1, 1, 3));
u9b_chk('sub_first_of_three', @() A(3, 1, 1));
u9b_chk('sub_cell_paren', @() c(3));
u9b_chk('sub_cell_brace', @() c{3});
u9b_chk('sub_struct', @() s(3));
u9b_chk('sub_string', @() t(2));
u9b_chk('sub_char', @() ch(3));
u9b_chk('sub_logical', @() L(3));

% --- braces (item 16) ---
u9b_chk('brace_number', @() x{1});
u9b_chk('brace_logical', @() L{1});
u9b_chk('brace_char', @() ch{1});
u9b_chk('brace_handle', @() f{1});
u9b_chk('brace_struct', @() s{1});
u9b_chk('brace_string', @() t{1});

% --- sizes, brackets and products ---
u9b_chk('size_plus', @() [1 2] + [1 2 3]);
u9b_chk('size_times', @() [1 2] .* [1 2 3]);
u9b_chk('size_equal', @() [1 2] == [1 2 3]);
u9b_chk('size_chars', @() 'ab' + 'cde');
u9b_chk('size_expands', @() size([1; 2] + [1 2 3]));
u9b_chk('inner_rows', @() [1 2] * [3 4]);
u9b_chk('inner_columns', @() [1; 2; 3] * [1; 2]);
u9b_chk('inner_fits', @() [1 2] * [3; 4]);
u9b_chk('cat_side', @() [[1; 2] [1 2 3]]);
u9b_chk('cat_stack', @() [1 2; 1 2 3]);

% --- names and arity ---
u9b_chk('undefined_name', @() no_such_variable_zz + 1);
u9b_chk('undefined_call_double', @() no_such_function_zz(3));
u9b_chk('undefined_call_char', @() no_such_function_zz('a', 2));
u9b_chk('undefined_call_cell', @() no_such_function_zz({1}));
u9b_chk('undefined_call_int8', @() no_such_function_zz(int8(1)));
u9b_chk('too_many', @() sin(1, 2));
u9b_chk('not_enough', @() mod(1));

% --- assignments ---
u9b_chk('write_brace_number', @() wbrace(5));
u9b_chk('write_brace_logical', @() wbrace(true));
u9b_chk('write_brace_char', @() wbrace('ab'));
u9b_chk('write_brace_struct', @() wbrace(struct('a', 1)));
u9b_chk('write_zero', @() wpos(5, 0));
u9b_chk('write_fraction', @() wpos(5, 1.5));
u9b_chk('write_count', @() wcount());
u9b_chk('write_shape', @() wshape());

% --- the top-level stack and its report (item 13) ---
try
    error('oi:top', 'boom');
catch e
    u9b_chk('top_stack_n', @() numel(e.stack));
    u9b_chk('top_stack_name', @() e.stack(1).name);
    u9b_chk('top_report', @() getReport(e, 'basic', 'hyperlinks', 'off'));
    u9b_chk('top_report_extended', @() getReport(e, 'extended', 'hyperlinks', 'off'));
end
try
    v(9);
catch e
    u9b_chk('index_stack_n', @() numel(e.stack));
    u9b_chk('index_report', @() getReport(e, 'basic', 'hyperlinks', 'off'));
end
try
    deep_error();
catch e
    u9b_chk('local_stack_n', @() numel(e.stack));
    u9b_chk('local_stack_names', @() {e.stack.name});
    u9b_chk('local_report', @() getReport(e, 'basic', 'hyperlinks', 'off'));
end
try
    throw(MException('oi:thrown', 'thrown %d', 7));
catch e
    u9b_chk('throw_report', @() getReport(e, 'basic', 'hyperlinks', 'off'));
end

function y = wbrace(y)
y{1} = 2;
end

function y = wpos(y, k)
y(k) = 1;
end

function y = wcount()
y = [1 2 3];
y(1:2) = [1 2 3];
end

function y = wshape()
y = magic(3);
y(:, 1) = [1 2];
end

function deep_error()
error('oi:local', 'deep');
end
