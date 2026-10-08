% Open items 2, 3, 7, 8, 9, 10, 22/54 and 42 (ADR 0213): the silent wrong answers. A char is its
% codes under every operator with a numeric or char partner; a char row flattens to a column; a
% logical stored into a numeric array takes the array's class; a zero step is an empty range; a
% fractional colon subscript rounds with a warning and a classed colon refuses ends outside its
% class; accumarray's shape follows its subscripts; the reductions take complex input; string()
% writes an array at one precision; func2str writes the handle as R2025b does; true and false take
% any number of sizes. Probes probe_b2 and probe_b3 (open-items scratch).

% --- a char is its codes (item 2) and a column under (:) ---
u9b_chk('chr_plus_num', @() 'a' + 1);
u9b_chk('chr_plus_num_row', @() 'cd' + 7);
u9b_chk('chr_plus_chr', @() 'ab' + 'cd');
u9b_chk('chr_plus_row', @() 'cd' + [1 2]);
u9b_chk('chr_chain_string', @() 'x' + 1 + "y");
u9b_chk('string_plus_chr', @() "y" + 'x');
u9b_chk('chr_minus_chr', @() '123' - '0');
u9b_chk('chr_step', @() char('A' + 2));
u9b_chk('chr_eq_chr', @() 'abc' == 'abd');
u9b_chk('chr_lt_chr', @() 'ab' < 'ac');
u9b_chk('num_times_chr', @() 2 * 'a');
u9b_chk('chr_dottimes', @() 'a' .* [1 2]);
u9b_chk('chr_divide', @() 'ab' / 2);
u9b_chk('chr_negate', @() -'a');
u9b_chk('chr_plus_logical', @() 'a' + true);
u9b_chk('chr_plus_int8', @() 'a' + int8(1));
u9b_chk('chr_plus_int8_class', @() class('a' + int8(1)));
u9b_chk('chr_plus_single_class', @() class('a' + single(1)));
u9b_chk('chr_plus_shorter', @() 'ab' + 'c');
u9b_chk('chr_matrix_plus', @() ['ab'; 'cd'] + 1);
u9b_chk('chr_plus_column', @() 'ab' + [1; 2]);
u9b_chk('chr_power', @() 'a' ^ 2);
u9b_chk('chr_plus_cell', @() 'a' + {1});
u9b_chk('chr_plus_imag', @() 'a' + 1i);
u9b_chk('chr_and', @() 'a' & 1);
u9b_chk('chr_not', @() ~'ab');
u9b_chk('chr_empty_plus', @() size('' + 1));
v = 'abc';
u9b_chk('chr_colon_size', @() size(v(:)));
u9b_chk('chr_colon_back', @() v(:)');
u9b_chk('chr_colon_equal', @() isequal(v(:), ['a'; 'b'; 'c']));

% --- a logical stored into a numeric array, and a zero step (item 9) ---
x = [1 -1 2];
f = zeros(1, 3);
for k = 1:3
    f(k) = x(k) > 0;
end
u9b_chk('store_loop', @() {class(f), f});
g = zeros(1, 3);
g(:) = true;
u9b_chk('store_all', @() class(g));
p = zeros(1, 4);
p([1 3]) = [true false];
u9b_chk('store_mask', @() {class(p), p});
q = int8([1 2]);
q(2) = true;
u9b_chk('store_int8', @() class(q));
r = true(1, 3);
r(2) = false;
u9b_chk('store_into_logical', @() class(r));
e = [];
e(2) = true;
u9b_chk('store_into_empty', @() {class(e), e});
u9b_chk('zero_step_size', @() size(1:0:5));
u9b_chk('zero_step_class', @() class(int8(1):int8(0):5));
n = 0;
for j = 1:0:5
    n = n + 1;
end
u9b_chk('zero_step_loop', @() n);

% --- a colon written as a subscript (item 8) ---
y = 1:10;
u9b_chk('colon_half_step', @() y(2:0.5:3));
u9b_chk('colon_whole_elements', @() y(1:2.5));
u9b_chk('colon_fraction_start', @() y(1.4:1:3.4));
u9b_chk('colon_down', @() y(3:-0.5:1));
M = magic(3);
u9b_chk('colon_two_subscripts', @() M(1:0.5:2, 1));
u9b_chk('colon_uint8_out', @() uint8(254):258);
u9b_chk('colon_uint8_step_out', @() uint8(250):2:300);
u9b_chk('colon_int16_out', @() int16(1):40000);
u9b_chk('colon_int8_below', @() int8(-130):-127);
u9b_chk('colon_uint8_big_step', @() uint8(1):300:2);
u9b_chk('colon_uint8_down', @() uint8(1):-1:0);

% --- accumarray (item 7) ---
u9b_chk('acc_column', @() accumarray([1; 2; 2; 3], 1));
u9b_chk('acc_column_size', @() size(accumarray([1; 2; 2; 3], 1, [4 1])));
u9b_chk('acc_column_row_size', @() accumarray([1; 2; 2; 3], 1, [1 4]));
u9b_chk('acc_row_subscript', @() size(accumarray([1 2 2 3], 1)));
u9b_chk('acc_matrix', @() accumarray([1 1; 2 2; 2 2], 5));
u9b_chk('acc_matrix_size', @() accumarray([1 1; 2 2], [5 6], [3 3]));
u9b_chk('acc_values', @() accumarray([1; 3], [10; 20]));
u9b_chk('acc_empty', @() size(accumarray(zeros(0, 1), 1)));
u9b_chk('acc_empty_size', @() size(accumarray(zeros(0, 1), 1, [3 1])));
u9b_chk('acc_row_values', @() accumarray([2; 1], [3 4]));
u9b_chk('acc_function', @() accumarray([1; 2; 2], [4; 5; 6], [], @max));

% --- complex reductions (item 10) ---
u9b_chk('sum_complex', @() sum([1+2i 3]));
u9b_chk('sum_complex_matrix', @() sum([1+2i 3; 4 5i]));
u9b_chk('sum_complex_dim2', @() sum([1+1i 2; 3 4], 2));
u9b_chk('prod_complex', @() prod([1+2i 3]));
u9b_chk('mean_complex', @() mean([1+2i 3]));
u9b_chk('cumsum_complex', @() cumsum([1+2i 3 1i]));
u9b_chk('cumprod_complex', @() cumprod([1+2i 3 1i]));
u9b_chk('diff_complex', @() diff([1+2i 3 1i]));
u9b_chk('sum_complex_zero_imag', @() isreal(sum(complex([1 2], 0))));
u9b_chk('sum_complex_all', @() sum([1+2i 3; 1 1], 'all'));
u9b_chk('sum_complex_single', @() class(sum(single([1+2i 3]))));
u9b_chk('sum_fractional_dim', @() sum([1 2], 1.5));

% --- string() of an array (item 3) ---
u9b_chk('string_array_precision', @() string([9016.9943749474514 12345.5]));
u9b_chk('string_array_thirds', @() string([1/3 200/3]));
u9b_chk('string_array_hundred', @() string([1/3 100]));
u9b_chk('string_array_thousand', @() string([1/3 1000]));
u9b_chk('string_array_cap', @() string([1/3 1e15]));
u9b_chk('string_array_inf', @() string([Inf 1234.5678]));
u9b_chk('string_array_big', @() string([1e20 0.5]));
u9b_chk('string_single_array', @() string(single([9016.9943749474514 12345.5])));
u9b_chk('string_single_hundred', @() string(single([1/3 100])));
u9b_chk('string_scalar', @() string(9016.9943749474514));
u9b_chk('string_plus_array', @() "a" + [9016.9943749474514 12345.5]);
u9b_chk('array_plus_string', @() [9016.9943749474514 12345.5] + "a");
u9b_chk('strings_plus_array', @() ["x"; "y"] + [1/3; 200/3]);
u9b_chk('string_single_pi', @() string(single(pi)));

% --- func2str (items 22 and 54) ---
u9b_chk('f2s_builtin', @() func2str(@disp));
u9b_chk('f2s_sin', @() func2str(@sin));
u9b_chk('f2s_call', @() func2str(@(n) plus(1, n)));
u9b_chk('f2s_operators', @() func2str(@(x,y) x + y * 2));
u9b_chk('f2s_matrix', @() func2str(@(x) [x, 1; 2 x]));
u9b_chk('f2s_fields', @() func2str(@(s) s.a(1) + s.('b')));
u9b_chk('f2s_transpose', @() func2str(@(x) x' * 2));
u9b_chk('f2s_char', @() func2str(@() disp('a b')));
u9b_chk('f2s_cell_string', @() func2str(@(x) {x, "s t"}));
u9b_chk('f2s_logic', @() func2str(@(x) ~x && ~isempty(x)));
u9b_chk('f2s_negatives', @() func2str(@(x) -x + -1));
u9b_chk('f2s_end', @() func2str(@(x) x(end:-1:1)));
u9b_chk('f2s_empty_arg', @() func2str(@(x) max(x, [], 2)));
u9b_chk('f2s_dotted', @() func2str(@(x) x.^2 ./ 3));
u9b_chk('f2s_varargin', @() func2str(@(varargin) numel(varargin)));
u9b_chk('f2s_compare', @() func2str(@(x) x == 1 | x ~= 2));
u9b_chk('f2s_literal', @() func2str(@(x) 1e-3 + 0.5));
u9b_chk('f2s_spaces', @() func2str(@(x) [1 2 3]));
u9b_chk('f2s_commas', @() func2str(@(x) [1,2,3]));
u9b_chk('f2s_nested', @() func2str(@(x) @(y) x + y));
u9b_chk('f2s_str2func', @() func2str(str2func('@(x) x+1')));
u9b_chk('f2s_colon_transpose', @() func2str(@(x) x(:)'));

% --- true and false with sizes (item 42) ---
u9b_chk('true_three', @() size(true(2, 2, 3)));
u9b_chk('false_vector', @() size(false([2 3 4])));
u9b_chk('true_empty', @() size(true(0, 3)));
u9b_chk('true_square', @() true(2));
u9b_chk('true_like', @() true(2, 'like', true));
u9b_chk('true_trailing', @() size(true(2, 3, 1, 1)));
u9b_chk('true_negative', @() size(true(-1)));
u9b_chk('true_fraction', @() true(1.5));
u9b_chk('true_like_double', @() true(2, 'like', 5));
u9b_chk('false_like_int8', @() false(2, 'like', int8(1)));
u9b_chk('true_like_sparse', @() class(true(2, 'like', sparse(true))));
