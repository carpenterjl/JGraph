% growth_keeps_type.m -- V6 of the value-ownership plan, fourth sub-stage: every rebuild keeps what
% the value is (appendix A #52, #53, #157, #162). A write that grows an array in one, two or three
% dimensions, a deletion, and a write of another class into an array each rebuild the array; the
% class, the tags and the growth fill are the target's own, and an alias taken before the write is
% untouched. One line per type crosses {1-D growth, 2-D growth, N-D growth, deletion}; the rest
% are the conversions a write makes and the scalar targets a write grows.

run_case('k_double', @() grow_report([1 2], 5));
run_case('k_single', @() grow_report(single([1 2]), single(5)));
run_case('k_int8', @() grow_report(int8([1 2]), int8(5)));
run_case('k_uint8', @() grow_report(uint8([1 2]), uint8(5)));
run_case('k_int32', @() grow_report(int32([1 2]), int32(5)));
run_case('k_uint16', @() grow_report(uint16([1 2]), uint16(5)));
run_case('k_logical', @() grow_report([true true], true));
run_case('k_char', @() grow_report('ab', 'c'));
run_case('k_string', @() grow_report(["a" "b"], "c"));
run_case('k_cell', @() grow_report({1, 'a'}, {2}));
run_case('k_complex', @() grow_report([1+2i 3], 4i));
run_case('k_datetime', @() grow_report(datetime(2020, 1, [1 2]), datetime(2020, 2, 1)));
run_case('k_duration', @() grow_report(seconds([1 2]), seconds(5)));
run_case('k_struct', @k_struct);
run_case('w_double_into_logical', @w_double_into_logical);
run_case('w_double_into_logical_range', @w_double_into_logical_range);
run_case('w_double_into_logical_2d', @w_double_into_logical_2d);
run_case('w_double_into_logical_mask', @w_double_into_logical_mask);
run_case('w_double_into_logical_growth', @w_double_into_logical_growth);
run_case('w_double_into_logical_alias', @w_double_into_logical_alias);
run_case('w_double_into_logical_field', @w_double_into_logical_field);
run_case('w_double_into_logical_cell_slot', @w_double_into_logical_cell_slot);
run_case('w_double_into_int8', @w_double_into_int8);
run_case('w_int8_into_double', @w_int8_into_double);
run_case('w_single_into_double', @w_single_into_double);
run_case('w_logical_into_double', @w_logical_into_double);
run_case('w_number_into_char', @w_number_into_char);
run_case('w_char_into_double', @w_char_into_double);
run_case('w_number_into_string', @w_number_into_string);
run_case('w_string_into_double', @w_string_into_double);
run_case('w_char_into_string', @w_char_into_string);
run_case('w_int8_into_int16', @w_int8_into_int16);
run_case('w_complex_into_double', @w_complex_into_double);
run_case('q_scalar_double_linear', @q_scalar_double_linear);
run_case('q_scalar_double_two_subs', @q_scalar_double_two_subs);
run_case('q_scalar_double_three_subs', @q_scalar_double_three_subs);
run_case('q_scalar_logical', @q_scalar_logical);
run_case('q_scalar_int8', @q_scalar_int8);
run_case('q_scalar_char', @q_scalar_char);
run_case('q_scalar_in_field', @q_scalar_in_field);
run_case('q_scalar_in_cell_slot', @q_scalar_in_cell_slot);
run_case('q_scalar_alias', @q_scalar_alias);
run_case('q_scalar_end_plus_one', @q_scalar_end_plus_one);
run_case('c_concat_int8_double', @c_concat_int8_double);
run_case('c_concat_logical_double', @c_concat_logical_double);
run_case('c_concat_char_number', @c_concat_char_number);
run_case('c_end_plus_one_loop_int8', @c_end_plus_one_loop_int8);
run_case('c_end_plus_one_loop_logical', @c_end_plus_one_loop_logical);
run_case('c_end_plus_one_loop_string', @c_end_plus_one_loop_string);
run_case('d_delete_2d_column_keeps_class', @d_delete_2d_column_keeps_class);
run_case('d_delete_nd_page_keeps_class', @d_delete_nd_page_keeps_class);
run_case('d_delete_all_keeps_class', @d_delete_all_keeps_class);
run_case('n_char_matrix_growth', @n_char_matrix_growth);
run_case('n_char_row_to_matrix', @n_char_row_to_matrix);
run_case('n_string_nd_alias', @n_string_nd_alias);

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

function s = show(x)
if iscell(x)
    s = sprintf('{%s %s}', class(x{1}), mat2str(size(x{1})));
elseif isstring(x)
    if ismissing(x), s = '<missing>'; else, s = ['"' char(x) '"']; end
elseif ischar(x)
    s = ['c' mat2str(double(x))];
elseif isdatetime(x)
    if isnat(x), s = 'NaT'; else, s = char(x); end
elseif isduration(x)
    s = sprintf('%gs', seconds(x));
elseif isstruct(x)
    s = sprintf('struct[%s]', mat2str(size(x.a)));
else
    s = mat2str(x);
end
end

function s = all_of(x)
parts = cell(1, numel(x));
for k = 1:numel(x)
    parts{k} = show(x(k));
end
s = strjoin(parts, ',');
end

function s = grow_report(x, v)
t = x;
a = x; a(4) = v;
b = x; b(2, 3) = v;
c = x; c(1, 2, 2) = v;
d = x; d(1) = [];
s = sprintf('%s %s %s; %s %s %s; %s %s %s; %s %s; %s %s', ...
    class(a), mat2str(size(a)), show(a(3)), ...
    class(b), mat2str(size(b)), show(b(2, 1)), ...
    class(c), mat2str(size(c)), show(c(1, 1, 2)), ...
    class(d), mat2str(size(d)), class(t), all_of(t));
end

function s = k_struct()
x = struct('a', {1, 2});
t = x;
a = x; a(4).a = 5;
b = x; b(2, 3).a = 5;
d = x; d(1) = [];
s = sprintf('%s %s %s; %s %s %s; %s %s; %d', class(a), mat2str(size(a)), show(a(3)), ...
    class(b), mat2str(size(b)), show(b(2, 1)), class(d), mat2str(size(d)), numel(t));
end

% --- a write of another class ------------------------------------------------------------------------

function s = w_double_into_logical()
v = [true false true]; v(2) = 9;
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_double_into_logical_range()
v = [true false true false]; v(2:3) = [0 7];
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_double_into_logical_2d()
v = [true false; false true]; v(1, 2) = 3; v(2, :) = [0 0];
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_double_into_logical_mask()
v = [true false true]; v(v) = 0;
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_double_into_logical_growth()
v = [true false]; v(4) = 2;
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_double_into_logical_alias()
v = [true false true]; t = v; v(2) = 9;
s = sprintf('%s %s %s %s', class(v), mat2str(v), class(t), mat2str(t));
end

function s = w_double_into_logical_field()
st.v = [true false true]; r = st; r.v(2) = 9;
s = sprintf('%s %s %s', class(r.v), mat2str(r.v), mat2str(st.v));
end

function s = w_double_into_logical_cell_slot()
c = {[true false true]}; d = c; d{1}(2) = 9;
s = sprintf('%s %s %s', class(d{1}), mat2str(d{1}), mat2str(c{1}));
end

function s = w_double_into_int8()
v = int8([1 2 3]); v(2) = 3.7; v(3) = 300;
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_int8_into_double()
v = [1.5 2.5 3.5]; v(2) = int8(7);
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_single_into_double()
v = [1.5 2.5]; v(2) = single(7.25);
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_logical_into_double()
v = [1.5 2.5]; v(2) = true;
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_number_into_char()
v = 'abc'; v(2) = 65;
s = sprintf('%s %s', class(v), v);
end

function s = w_char_into_double()
v = [1 2 3]; v(2) = 'a';
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_number_into_string()
v = ["a" "b"]; v(2) = 5;
s = sprintf('%s %s', class(v), strjoin(v, ','));
end

function s = w_string_into_double()
v = [1 2 3]; v(2) = "7";
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_char_into_string()
v = ["a" "b"]; v(2) = 'xyz';
s = sprintf('%s %s %d', class(v), strjoin(v, ','), numel(v));
end

function s = w_int8_into_int16()
v = int16([1 2 3]); v(2) = int8(5);
s = sprintf('%s %s', class(v), mat2str(v));
end

function s = w_complex_into_double()
v = [1 2 3]; t = v; v(2) = 1i;
s = sprintf('%d %d %s', isreal(v), isreal(t), mat2str(v));
end

% --- a scalar target that a write grows (#162) --------------------------------------------------------

function s = q_scalar_double_linear()
q = 1; q(3) = 5;
s = sprintf('%s %s', mat2str(size(q)), mat2str(q));
end

function s = q_scalar_double_two_subs()
q = 1; q(2, 2) = 5;
s = sprintf('%s %s', mat2str(size(q)), mat2str(q));
end

function s = q_scalar_double_three_subs()
q = 1; q(1, 1, 2) = 5;
s = sprintf('%s %s', mat2str(size(q)), mat2str(q(:)'));
end

function s = q_scalar_logical()
q = true; q(3) = true;
s = sprintf('%s %s', class(q), mat2str(q));
end

function s = q_scalar_int8()
q = int8(1); q(3) = 2;
s = sprintf('%s %s', class(q), mat2str(q));
end

function s = q_scalar_char()
q = 'a'; q(3) = 'c';
s = sprintf('%s %s', class(q), mat2str(double(q)));
end

function s = q_scalar_in_field()
st.q = 1; r = st; r.q(3) = 5;
s = sprintf('%s %s', mat2str(st.q), mat2str(r.q));
end

function s = q_scalar_in_cell_slot()
c = {1}; d = c; d{1}(3) = 5;
s = sprintf('%s %s', mat2str(c{1}), mat2str(d{1}));
end

function s = q_scalar_alias()
q = 1; t = q; q(3) = 5;
s = sprintf('%s %s', mat2str(t), mat2str(q));
end

function s = q_scalar_end_plus_one()
q = 1; q(end + 1) = 2; q(end + 1) = 3;
s = mat2str(q);
end

% --- growth by concatenation and by end + 1 -----------------------------------------------------------

function s = c_concat_int8_double()
x = int8([1 2]); x = [x 4.7];
s = sprintf('%s %s', class(x), mat2str(x));
end

function s = c_concat_logical_double()
x = [true false]; x = [x 2];
s = sprintf('%s %s', class(x), mat2str(x));
end

function s = c_concat_char_number()
x = 'ab'; x = [x 67];
s = sprintf('%s %s', class(x), x);
end

function s = c_end_plus_one_loop_int8()
x = int8([]);
for k = 1:3
    x(end + 1) = k * 100;
end
s = sprintf('%s %s', class(x), mat2str(x));
end

function s = c_end_plus_one_loop_logical()
x = true(1, 0);
for k = 1:3
    x(end + 1) = k - 2;
end
s = sprintf('%s %s', class(x), mat2str(x));
end

function s = c_end_plus_one_loop_string()
x = strings(1, 0);
for k = 1:3
    x(end + 1) = k;
end
s = sprintf('%s %s', class(x), strjoin(x, ','));
end

% --- deletion ------------------------------------------------------------------------------------------

function s = d_delete_2d_column_keeps_class()
x = int8([1 2 3; 4 5 6]); t = x; x(:, 2) = [];
y = [true false true; false true false]; y(1, :) = [];
z = ['abc'; 'def']; z(:, 1) = [];
s = sprintf('%s %s %s; %s %s; %s %s %s', class(x), mat2str(x), mat2str(size(t)), class(y), mat2str(y), class(z), mat2str(size(z)), z(1, :));
end

function s = d_delete_nd_page_keeps_class()
x = uint8(ones(2, 2, 3)); x(:, :, 2) = [];
w = ["a" "b"]; w(1, 2, 2) = "c"; w(:, :, 1) = [];
s = sprintf('%s %s; %s %s', class(x), mat2str(size(x)), class(w), mat2str(size(w)));
end

function s = d_delete_all_keeps_class()
x = int8([1 2 3]); x(:) = [];
y = [true false]; y([1 2]) = [];
w = ["a" "b"]; w(:) = [];
s = sprintf('%s %s; %s %s; %s %s', class(x), mat2str(size(x)), class(y), mat2str(size(y)), class(w), mat2str(size(w)));
end

% --- N-D text (#52, #53) -------------------------------------------------------------------------------

function s = n_char_matrix_growth()
x = ['ab'; 'cd']; t = x; x(3, 3) = 'z';
s = sprintf('%s %s %s %s', class(x), mat2str(size(x)), mat2str(double(x(3, :))), mat2str(size(t)));
end

function s = n_char_row_to_matrix()
x = 'ab'; t = x; x(2, 1) = 'c';
s = sprintf('%s %s %s %s', class(x), mat2str(size(x)), mat2str(double(x)), t);
end

function s = n_string_nd_alias()
x = ["a" "b"]; t = x; x(1, 2, 2) = "c";
s = sprintf('%d %d %s %d', isstring(x), isstring(t), mat2str(size(x)), ismissing(x(1, 1, 2)));
end
