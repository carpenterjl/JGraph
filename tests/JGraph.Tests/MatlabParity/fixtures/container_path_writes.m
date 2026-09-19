% container_path_writes.m -- V6 of the value-ownership plan, fifth sub-stage: writes through a
% container path (appendix A #60, #63, #82, #83, #85, #90-#92, #149). A paren write, a deletion and
% a growth into a char, numeric, logical and string payload reached through c{k}, s.f, s(k).f and
% nested combinations, each with an alias checked afterwards; a path whose levels do not exist yet
% creates each one as R2025b does; a path through a value of the wrong kind is refused in its
% words; struct([]); a cell and a struct array written and read by more than one subscript.

run_case('p_cell_char_elem', @p_cell_char_elem);
run_case('p_cell_char_growth', @p_cell_char_growth);
run_case('p_cell_char_delete', @p_cell_char_delete);
run_case('p_cell_numeric_growth_end', @p_cell_numeric_growth_end);
run_case('p_cell_numeric_delete', @p_cell_numeric_delete);
run_case('p_cell_logical_elem', @p_cell_logical_elem);
run_case('p_cell_string_elem', @p_cell_string_elem);
run_case('p_cell_string_brace_char', @p_cell_string_brace_char);
run_case('p_field_char_elem', @p_field_char_elem);
run_case('p_field_numeric_delete', @p_field_numeric_delete);
run_case('p_field_numeric_growth_2d', @p_field_numeric_growth_2d);
run_case('p_field_logical_mask_write', @p_field_logical_mask_write);
run_case('p_field_string_growth', @p_field_string_growth);
run_case('p_structarr_field_elem', @p_structarr_field_elem);
run_case('p_structarr_field_growth', @p_structarr_field_growth);
run_case('p_structarr_field_delete', @p_structarr_field_delete);
run_case('p_structarr_field_char', @p_structarr_field_char);
run_case('p_nested_cell_in_field', @p_nested_cell_in_field);
run_case('p_nested_field_in_cell', @p_nested_field_in_cell);
run_case('p_nested_three_levels', @p_nested_three_levels);
run_case('p_strrep_result_elem', @p_strrep_result_elem);
run_case('n_struct_array_through_field', @n_struct_array_through_field);
run_case('n_struct_array_through_field_existing', @n_struct_array_through_field_existing);
run_case('n_cell_element_struct', @n_cell_element_struct);
run_case('n_cell_element_struct_existing_cell', @n_cell_element_struct_existing_cell);
run_case('n_field_cell_paren', @n_field_cell_paren);
run_case('n_cell_struct_array_field', @n_cell_struct_array_field);
run_case('n_deep_fields', @n_deep_fields);
run_case('n_struct_array_elem_field_array', @n_struct_array_elem_field_array);
run_case('n_cell_in_cell', @n_cell_in_cell);
run_case('n_fresh_name_brace_paren', @n_fresh_name_brace_paren);
run_case('n_fresh_name_paren_field_paren', @n_fresh_name_paren_field_paren);
run_case('n_alias_untouched', @n_alias_untouched);
run_case('r_field_of_number', @r_field_of_number);
run_case('r_field_of_cell', @r_field_of_cell);
run_case('r_brace_of_struct', @r_brace_of_struct);
run_case('r_brace_of_number', @r_brace_of_number);
run_case('r_field_of_struct_array', @r_field_of_struct_array);
run_case('r_nested_field_of_char', @r_nested_field_of_char);
run_case('r_refused_leaves_value', @r_refused_leaves_value);
run_case('e_struct_empty_ctor', @e_struct_empty_ctor);
run_case('e_struct_empty_fields', @e_struct_empty_fields);
run_case('e_struct_empty_growth_by_field', @e_struct_empty_growth_by_field);
run_case('e_struct_empty_assign_struct', @e_struct_empty_assign_struct);
run_case('e_struct_empty_concat', @e_struct_empty_concat);
run_case('e_struct_empty_isempty_loop', @e_struct_empty_isempty_loop);
run_case('m_cell_paren_two_subs_read', @m_cell_paren_two_subs_read);
run_case('m_cell_paren_two_subs_write', @m_cell_paren_two_subs_write);
run_case('m_cell_three_subs_growth', @m_cell_three_subs_growth);
run_case('m_cell_brace_three_subs', @m_cell_brace_three_subs);
run_case('m_struct_two_subs_growth', @m_struct_two_subs_growth);
run_case('m_struct_two_subs_read', @m_struct_two_subs_read);
run_case('m_struct_two_subs_alias', @m_struct_two_subs_alias);
run_case('d_dictionary_vector_keys', @d_dictionary_vector_keys);
run_case('d_dictionary_vector_keys_strings', @d_dictionary_vector_keys_strings);
run_case('d_dictionary_vector_missing_key', @d_dictionary_vector_missing_key);

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

% --- payloads reached through a path --------------------------------------------------------------------

function s = p_cell_char_elem()
c = {'bat', 'cat'}; d = c; d{1}(1) = 'z';
s = sprintf('%s %s', c{1}, d{1});
end

function s = p_cell_char_growth()
c = {'ab'}; d = c; d{1}(4) = 'z';
s = sprintf('%s %s %s', c{1}, mat2str(double(d{1})), class(d{1}));
end

function s = p_cell_char_delete()
c = {'abcd'}; d = c; d{1}([1 3]) = [];
s = sprintf('%s %s', c{1}, d{1});
end

function s = p_cell_numeric_growth_end()
c = {[1 2], [3 4]}; d = c; d{end}(end + 1) = 5;
s = sprintf('%s %s', mat2str(c{2}), mat2str(d{2}));
end

function s = p_cell_numeric_delete()
c = {[1 2 3]}; d = c; d{1}(2) = [];
s = sprintf('%s %s', mat2str(c{1}), mat2str(d{1}));
end

function s = p_cell_logical_elem()
c = {[true false true]}; d = c; d{1}(2) = true;
s = sprintf('%s %s %s', mat2str(c{1}), mat2str(d{1}), class(d{1}));
end

function s = p_cell_string_elem()
c = {["a" "b"]}; d = c; d{1}(3) = "z";
s = sprintf('%s %s', strjoin(c{1}, ','), strjoin(d{1}, ','));
end

function s = p_cell_string_brace_char()
x = ["abc" "def"]; y = x; y{1}(2) = 'Z';
s = sprintf('%s %s', strjoin(x, ','), strjoin(y, ','));
end

function s = p_field_char_elem()
st.t = 'bat'; r = st; r.t(1) = 'c';
s = sprintf('%s %s', st.t, r.t);
end

function s = p_field_numeric_delete()
st.v = [1 2 3]; r = st; r.v(2) = [];
s = sprintf('%s %s', mat2str(st.v), mat2str(r.v));
end

function s = p_field_numeric_growth_2d()
st.v = [1 2]; r = st; r.v(2, 3) = 9;
s = sprintf('%s %s', mat2str(st.v), mat2str(r.v));
end

function s = p_field_logical_mask_write()
st.v = [1 2 3]; r = st; r.v(logical([0 0 0 1])) = 9;
s = sprintf('%s %s', mat2str(st.v), mat2str(r.v));
end

function s = p_field_string_growth()
st.v = ["a" "b"]; r = st; r.v(4) = "d";
s = sprintf('%d %d %d', numel(st.v), numel(r.v), ismissing(r.v(3)));
end

function s = p_structarr_field_elem()
a = struct('v', {[1 2], [3 4]}); b = a; b(2).v(1) = 9;
s = sprintf('%s %s', mat2str(a(2).v), mat2str(b(2).v));
end

function s = p_structarr_field_growth()
a = struct('v', {[1 2], [3 4]}); b = a; b(1).v(end + 1) = 9;
s = sprintf('%s %s', mat2str(a(1).v), mat2str(b(1).v));
end

function s = p_structarr_field_delete()
a = struct('v', {[1 2 3], [4 5 6]}); b = a; b(2).v(1) = [];
s = sprintf('%s %s', mat2str(a(2).v), mat2str(b(2).v));
end

function s = p_structarr_field_char()
a = struct('t', {'ab', 'cd'}); b = a; b(2).t(2) = 'z';
s = sprintf('%s %s', a(2).t, b(2).t);
end

function s = p_nested_cell_in_field()
st.c = {[1 2], 'ab'}; r = st; r.c{1}(2) = 9; r.c{2}(1) = 'z';
s = sprintf('%s %s %s %s', mat2str(st.c{1}), st.c{2}, mat2str(r.c{1}), r.c{2});
end

function s = p_nested_field_in_cell()
c = {struct('v', [1 2 3])}; d = c; d{1}.v(2) = [];
s = sprintf('%s %s', mat2str(c{1}.v), mat2str(d{1}.v));
end

function s = p_nested_three_levels()
st.a.b = {[1 2 3]}; r = st; r.a.b{1}(end + 1) = 4;
s = sprintf('%s %s', mat2str(st.a.b{1}), mat2str(r.a.b{1}));
end

function s = p_strrep_result_elem()
c = {'aa', 'ba'}; d = strrep(c, 'a', 'x'); d{1}(1) = 'Q';
s = sprintf('%s,%s %s,%s', c{1}, c{2}, d{1}, d{2});
end

% --- levels that do not exist yet ----------------------------------------------------------------------

function s = n_struct_array_through_field()
clear x; x.y(3).z = 1;
s = sprintf('%s %d %d %d', class(x), numel(x.y), isempty(x.y(1).z), x.y(3).z);
end

function s = n_struct_array_through_field_existing()
x.k = 5; x.y(2).z = 7;
s = sprintf('%d %d %d %s', x.k, numel(x.y), x.y(2).z, mat2str(size(x.y)));
end

function s = n_cell_element_struct()
c = {}; c{3}.f = 1;
s = sprintf('%d %s %s %d %s', numel(c), class(c{1}), class(c{3}), c{3}.f, mat2str(size(c)));
end

function s = n_cell_element_struct_existing_cell()
c = {1, 2}; d = c; d{2}.f = 9;
s = sprintf('%s %s', class(c{2}), class(d{2}));
end

function s = n_field_cell_paren()
clear s0; s0.c{2}(3) = 1;
s = sprintf('%s %d %s %s', class(s0.c), numel(s0.c), mat2str(s0.c{2}), mat2str(size(s0.c{1})));
end

function s = n_cell_struct_array_field()
c = {}; c{2}.a(2).b = 1;
s = sprintf('%d %s %d %d %d', numel(c), class(c{2}.a), numel(c{2}.a), isempty(c{2}.a(1).b), c{2}.a(2).b);
end

function s = n_deep_fields()
clear q; q.a.b.c.d = 4;
s = sprintf('%s %s %s %d', class(q.a), class(q.a.b), class(q.a.b.c), q.a.b.c.d);
end

function s = n_struct_array_elem_field_array()
clear t; t(2).v(3) = 5;
s = sprintf('%d %d %s', numel(t), isempty(t(1).v), mat2str(t(2).v));
end

function s = n_cell_in_cell()
c = {}; c{2}{3} = 'x';
s = sprintf('%d %s %d %s', numel(c), class(c{2}), numel(c{2}), c{2}{3});
end

function s = n_fresh_name_brace_paren()
clear w; w{2}(2) = 7;
s = sprintf('%s %d %s', class(w), numel(w), mat2str(w{2}));
end

function s = n_fresh_name_paren_field_paren()
clear u; u(2).f(2) = 3;
s = sprintf('%s %d %s', class(u), numel(u), mat2str(u(2).f));
end

function s = n_alias_untouched()
x.k = 1; y = x; y.n(2).z = 5; y.m{2} = 'q';
s = sprintf('%s %s', strjoin(fieldnames(x)', ','), strjoin(fieldnames(y)', ','));
end

% --- a path through a value of the wrong kind ----------------------------------------------------------

function s = r_field_of_number()
x = 5; x.f = 1;
s = class(x);
end

function s = r_field_of_cell()
c = {1, 2}; c.f = 1;
s = class(c);
end

function s = r_brace_of_struct()
st.a = 1; st{2} = 5;
s = class(st);
end

function s = r_brace_of_number()
x = 5; x{2} = 1;
s = class(x);
end

function s = r_field_of_struct_array()
a = struct('v', {1, 2}); a.v = 9;
s = mat2str([a.v]);
end

function s = r_nested_field_of_char()
st.t = 'ab'; st.t.f = 1;
s = class(st.t);
end

function s = r_refused_leaves_value()
st.t = 'ab'; st.n = [1 2];
try
    st.t.f = 1;
catch
end
try
    st.n{2} = 5;
catch
end
s = sprintf('%s %s', st.t, mat2str(st.n));
end

% --- struct([]) (#85) ----------------------------------------------------------------------------------

function s = e_struct_empty_ctor()
st = struct([]);
s = sprintf('%s %s %d', class(st), mat2str(size(st)), numel(fieldnames(st)));
end

function s = e_struct_empty_fields()
st = struct('a', {});
s = sprintf('%s %s %s', class(st), mat2str(size(st)), strjoin(fieldnames(st)', ','));
end

function s = e_struct_empty_growth_by_field()
st = struct([]); st(2).a = 5;
s = sprintf('%s %d %d', mat2str(size(st)), isempty(st(1).a), st(2).a);
end

function s = e_struct_empty_assign_struct()
st = struct([]); st(1) = struct('a', 1);
s = sprintf('%s', mat2str(size(st)));
end

function s = e_struct_empty_concat()
st = struct([]); st = [st, struct('a', 1)];
s = sprintf('%s %d', mat2str(size(st)), st(1).a);
end

function s = e_struct_empty_isempty_loop()
st = struct([]);
n = 0;
for k = 1:numel(st), n = n + 1; end
s = sprintf('%d %d', isempty(st), n);
end

% --- more than one subscript on a cell and on a struct array --------------------------------------------

function s = m_cell_paren_two_subs_read()
c = {1, 'a'; [2 3], 'bc'};
d = c(2, :); e = c(:, 1);
s = sprintf('%s %s %s %s %s', class(d), mat2str(size(d)), d{2}, mat2str(size(e)), mat2str(e{2}));
end

function s = m_cell_paren_two_subs_write()
c = {1, 2; 3, 4}; d = c; d(2, 2) = {9}; d(1, :) = {7, 8};
s = sprintf('%d %d %d %d', c{2, 2}, d{2, 2}, d{1, 1}, d{1, 2});
end

function s = m_cell_three_subs_growth()
c = {1, 'a'}; t = c; c(1, 2, 2) = {2};
s = sprintf('%s %s %s %d %d', class(c), mat2str(size(c)), class(c{1, 1, 2}), c{1, 2, 2}, numel(t));
end

function s = m_cell_brace_three_subs()
c = {1, 'a'}; c{1, 2, 2} = [5 6];
s = sprintf('%s %s %s', mat2str(size(c)), mat2str(c{1, 2, 2}), mat2str(size(c{1, 1, 2})));
end

function s = m_struct_two_subs_growth()
b = struct('a', {1, 2}); b(2, 3).a = 5;
s = sprintf('%s %d %d %d', mat2str(size(b)), isempty(b(2, 1).a), b(2, 3).a, b(1, 2).a);
end

function s = m_struct_two_subs_read()
b = struct('a', {1, 2; 3, 4});
e = b(2, 1); r = b(1, :);
s = sprintf('%d %s %d', e.a, mat2str(size(r)), r(2).a);
end

function s = m_struct_two_subs_alias()
b = struct('a', {1, 2; 3, 4}); t = b; b(2, 2).a = 9;
s = sprintf('%d %d', t(2, 2).a, b(2, 2).a);
end

% --- a dictionary read by several keys (#149) -----------------------------------------------------------

function s = d_dictionary_vector_keys()
d = dictionary([1 2], [10 20]);
a = d([1 2]); d(2) = 9; b = d([1 2]);
s = sprintf('%s %s', mat2str(a), mat2str(b));
end

function s = d_dictionary_vector_keys_strings()
d = dictionary(["a" "b" "c"], [1 2 3]);
v = d(["c" "a"]);
s = sprintf('%s %s', mat2str(v), mat2str(size(d(["a"; "b"]))));
end

function s = d_dictionary_vector_missing_key()
d = dictionary([1 2], [10 20]);
v = d([1 5]);
s = mat2str(v);
end
