% Open items 200, 201, 202, 301 and 208's dynamic-field rows (ADR 0351). A containers.Map is a
% Count-by-1 column to size and the shape questions, and still one element to numel and to cellfun's
% named questions; struct and setfield keep a string value; a name no field may have is refused by a
% dynamic write, setfield, struct and cell2struct, and an absent one by getfield, rmfield and
% orderfields in R2025b's words; struct2cell answers fields by the struct's own size; a dynamic name
% that is a string array is refused, and a char matrix names its characters in column order.
% Probes p200, p201, p202, p208, p208t, p301 (agent P's scratch).

m0 = containers.Map();
m2 = containers.Map({'a', 'b'}, {1, 2});
mn = containers.Map('KeyType', 'double', 'ValueType', 'any');
mn(3) = 'x';
mn(1) = 'y';
mn(2) = 'z';
u9b_chk('map_size_empty', @() size(m0));
u9b_chk('map_size_two', @() size(m2));
u9b_chk('map_size_numeric_keys', @() size(mn));
u9b_chk('map_size_dim1', @() size(m2, 1));
u9b_chk('map_size_dim2', @() size(m2, 2));
u9b_chk('map_size_dim3', @() size(m2, 3));
u9b_chk('map_size_empty_dim1', @() size(m0, 1));
u9b_chk('map_size_dim_vector', @() size(m2, [1 2]));
u9b_chk('map_size_two_dims', @() size(m2, 1, 2));
u9b_chk('map_size_outputs_two', @() map_outputs(m2, 2));
u9b_chk('map_size_outputs_three', @() map_outputs(m2, 3));
u9b_chk('map_numel_empty', @() numel(m0));
u9b_chk('map_numel_two', @() numel(m2));
u9b_chk('map_length_empty', @() length(m0));
u9b_chk('map_length_two', @() length(m2));
u9b_chk('map_isempty_empty', @() isempty(m0));
u9b_chk('map_isempty_two', @() isempty(m2));
u9b_chk('map_ndims', @() ndims(m2));
u9b_chk('map_isscalar_two', @() isscalar(m2));
u9b_chk('map_isscalar_empty', @() isscalar(m0));
u9b_chk('map_isscalar_one', @() isscalar(containers.Map({'a'}, {1})));
u9b_chk('map_isvector', @() isvector(m2));
u9b_chk('map_isrow', @() isrow(m2));
u9b_chk('map_isrow_one', @() isrow(containers.Map({'a'}, {1})));
u9b_chk('map_iscolumn', @() iscolumn(m2));
u9b_chk('map_ismatrix', @() ismatrix(m2));
u9b_chk('map_grows', @() map_grown());
u9b_chk('map_shrinks', @() map_shrunk());
u9b_chk('map_in_struct', @() size(struct('m', m2)));
u9b_chk('map_held_by_field', @() size(getfield(struct('m', m2), 'm')));
u9b_chk('map_cellfun_numel', @() cellfun(@numel, {m0, m2}));
u9b_chk('map_cellfun_isempty', @() cellfun(@isempty, {m0, m2}));
u9b_chk('map_cellfun_length', @() cellfun(@length, {m0, m2}));
u9b_chk('map_cellfun_size', @() cellfun(@(x) size(x, 1), {m0, m2}));
u9b_chk('map_cellfun_named_isempty', @() cellfun('isempty', {m0, m2}));
u9b_chk('map_cellfun_named_length', @() cellfun('length', {m0, m2}));
u9b_chk('map_cellfun_named_prodofsize', @() cellfun('prodofsize', {m0, m2}));
u9b_chk('map_cellfun_named_ndims', @() cellfun('ndims', {m0, m2}));
u9b_chk('map_cellfun_named_size', @() cellfun('size', {m0, m2}, 1));
u9b_chk('map_text', @() m2);
u9b_chk('map_whos_size', @() getfield(whos_of(m2), 'size'));
u9b_chk('map_whos_bytes', @() getfield(whos_of(m2), 'bytes'));
u9b_chk('dictionary_size', @() size(dictionary("a", 1, "b", 2)));
u9b_chk('dictionary_numel', @() numel(dictionary("a", 1, "b", 2)));
u9b_chk('dictionary_length', @() length(dictionary("a", 1, "b", 2)));

u9b_chk('struct_string_value', @() getfield(struct('s', "text"), 's'));
u9b_chk('struct_string_name_and_value', @() struct("s", "text"));
u9b_chk('struct_string_row', @() struct('s', ["a" "b"]));
u9b_chk('struct_string_column', @() struct('s', ["a"; "b"]));
u9b_chk('struct_string_cell', @() struct('s', {"x", "y"}));
u9b_chk('struct_string_cell_one', @() struct('s', {"x"}));
u9b_chk('struct_string_empty', @() struct('s', string.empty));
u9b_chk('struct_string_mixed', @() struct('a', "x", 'b', 'y'));
u9b_chk('struct_string_spread', @() struct('a', 'x', 'b', "y", 'c', {"p", "q"}));
u9b_chk('struct_string_length', @() length(getfield(struct('s', "text"), 's')));
u9b_chk('struct_string_missing', @() ismissing(getfield(struct('s', string(missing)), 's')));
u9b_chk('struct_string_array_name', @() struct(["a" "b"], 1));
u9b_chk('setfield_string_value', @() setfield(struct('a', 1), 'b', "str"));
u9b_chk('setfield_string_name', @() setfield(struct('a', 1), "b", 2));
u9b_chk('cell2struct_string_value', @() cell2struct({"a"; 1}, {'f'; 'g'}, 1));

s = struct('a', 1);
u9b_chk('dyn_write_digit_first', @() dyn_write(s, '1bad'));
u9b_chk('dyn_write_string_name', @() dyn_write(s, "1bad"));
u9b_chk('dyn_write_space', @() dyn_write(s, 'a b'));
u9b_chk('dyn_write_empty', @() dyn_write(s, ''));
u9b_chk('dyn_write_underscore_first', @() dyn_write(s, '_x'));
u9b_chk('dyn_write_trailing_space', @() dyn_write(s, 'a '));
u9b_chk('dyn_write_leading_space', @() dyn_write(s, ' a'));
u9b_chk('dyn_write_letter_digits', @() dyn_write(s, 'x_1'));
u9b_chk('dyn_write_keyword_if', @() dyn_write(s, 'if'));
u9b_chk('dyn_write_keyword_end', @() dyn_write(s, 'end'));
u9b_chk('dyn_write_63_letters', @() numel(fieldnames(dyn_write(s, repmat('a', 1, 63)))));
u9b_chk('dyn_write_64_letters', @() numel(fieldnames(dyn_write(s, repmat('a', 1, 64)))));
u9b_chk('dyn_write_non_ascii', @() dyn_write(s, char(233)));
u9b_chk('dyn_read_digit_first', @() s.('1bad'));
u9b_chk('dyn_read_empty', @() s.(''));
u9b_chk('dyn_read_keyword', @() s.('if'));
u9b_chk('dyn_read_absent', @() s.('zz'));
u9b_chk('dyn_read_present', @() s.('a'));
u9b_chk('dyn_write_new_variable', @() dyn_write_unborn('1bad'));
u9b_chk('dyn_write_new_variable_left', @() dyn_write_unborn_exists('1bad'));
u9b_chk('dyn_write_then_dot', @() dyn_write_then_dot(s));
u9b_chk('dyn_write_indexed', @() dyn_write_indexed(s));
u9b_chk('dyn_write_empty_value', @() dyn_write_value(s, '1bad', []));
u9b_chk('dyn_write_element', @() dyn_write_element(struct('a', {1, 2})));
u9b_chk('dyn_write_deal', @() dyn_write_deal(struct('a', {1, 2})));
u9b_chk('dyn_write_in_cell', @() dyn_write_in_cell(s));
u9b_chk('dyn_read_array_absent', @() dyn_read_list(struct('a', {1, 2}), '1bad'));
u9b_chk('dyn_table_spaced_name', @() dyn_table('a b'));
u9b_chk('dyn_table_digit_first', @() dyn_table('1bad'));
u9b_chk('setfield_digit_first', @() setfield(s, '1bad', 1));
u9b_chk('setfield_trailing_blank', @() setfield(s, 'b  ', 1));
u9b_chk('getfield_digit_first', @() getfield(s, '1bad'));
u9b_chk('getfield_trailing_blank', @() getfield(s, 'a '));
u9b_chk('isfield_digit_first', @() isfield(s, '1bad'));
u9b_chk('isfield_cell', @() isfield(s, {'a', '1bad', 'a b'}));
u9b_chk('rmfield_absent', @() rmfield(s, '1bad'));
u9b_chk('rmfield_absent_cell', @() rmfield(struct('a', 1, 'b', 2), {'a', 'zz'}));
u9b_chk('rmfield_trailing_blank', @() rmfield(struct('a', 1, 'b', 2), 'a  '));
u9b_chk('rmfield_from_no_fields', @() rmfield(struct(), 'a'));
u9b_chk('rmfield_empty_cell', @() rmfield(s, {}));
u9b_chk('orderfields_absent', @() orderfields(s, {'1bad'}));
u9b_chk('orderfields_names', @() orderfields(struct('b', 1, 'a', 2), {'a', 'b'}));
u9b_chk('struct_digit_first', @() struct('1bad', 1));
u9b_chk('struct_space', @() struct('a b', 1));
u9b_chk('struct_empty_name', @() struct('', 1));
u9b_chk('struct_keyword', @() struct('if', 1));
u9b_chk('cell2struct_digit_first', @() cell2struct({1}, {'1bad'}, 1));
u9b_chk('cell2struct_space', @() cell2struct({1}, {'a b'}, 1));
u9b_chk('cell2struct_empty_name', @() cell2struct({1}, {''}, 1));

d = struct('a', 1, 'b', 2, 'ab', 3, 'acbd', 4);
u9b_chk('dyn_string_row', @() d.(["a" "b"]));
u9b_chk('dyn_string_row_write', @() dyn_write(d, ["a" "b"]));
u9b_chk('dyn_string_column', @() d.(["a"; "b"]));
u9b_chk('dyn_string_empty', @() d.(string.empty));
u9b_chk('dyn_string_missing', @() d.(string(missing)));
u9b_chk('dyn_char_column', @() d.(['a'; 'b']));
u9b_chk('dyn_char_column_write', @() getfield(dyn_write(d, ['a'; 'b']), 'ab'));
u9b_chk('dyn_char_matrix', @() d.(['ab'; 'cd']));
u9b_chk('dyn_char_column_new', @() getfield(dyn_write(d, ['x'; 'y']), 'xy'));
u9b_chk('dyn_char_pages', @() d.(reshape('ab', 1, 1, 2)));
u9b_chk('dyn_char_empty_column', @() d.(char(zeros(0, 1))));
u9b_chk('dyn_char_column_new_variable', @() dyn_write_unborn(['a'; 'b']));
u9b_chk('dyn_cell', @() d.({'a'}));
u9b_chk('dyn_cell_write', @() dyn_write(d, {'a'}));
u9b_chk('dyn_number', @() d.(1));
u9b_chk('dyn_number_write', @() dyn_write(d, 1));
u9b_chk('dyn_vector', @() d.([1 2]));
u9b_chk('dyn_logical', @() d.(true));
u9b_chk('dyn_string_scalar', @() d.("ab"));
u9b_chk('dyn_array_string_row', @() dyn_read_list(struct('a', {1, 2}), ["a" "b"]));
u9b_chk('dyn_array_char_column', @() dyn_read_list(struct('ab', {1, 2}), ['a'; 'b']));
u9b_chk('dyn_deal_char_column', @() dyn_deal_char(struct('a', {1, 2})));
T = table(1, 2, 'VariableNames', {'ab', 'b'});
u9b_chk('dyn_table_string_row', @() T.(["ab" "b"]));
u9b_chk('dyn_table_string_missing', @() T.(string(missing)));
u9b_chk('dyn_table_char_column', @() T.(['a'; 'b']));
u9b_chk('dyn_table_cell', @() T.({'ab'}));
u9b_chk('dyn_table_number', @() T.(2));

u9b_chk('s2c_scalar', @() struct2cell(struct('a', 1, 'b', 'x', 'c', {{}})));
u9b_chk('s2c_one_field', @() struct2cell(struct('a', 1)));
u9b_chk('s2c_row', @() struct2cell(struct('a', {1, 2}, 'b', {3, 4})));
u9b_chk('s2c_column', @() struct2cell(struct('a', {1; 2}, 'b', {3; 4})));
u9b_chk('s2c_grid', @() struct2cell(struct('a', {1, 2, 3; 4, 5, 6}, 'b', 0)));
u9b_chk('s2c_empty_one_field', @() struct2cell(struct('a', {})));
u9b_chk('s2c_empty_two_fields', @() struct2cell(struct('a', {}, 'b', {})));
u9b_chk('s2c_no_fields', @() struct2cell(struct()));
u9b_chk('s2c_bracket', @() struct2cell(struct([])));
u9b_chk('s2c_empty_0x3', @() struct2cell(struct('f', cell(0, 3))));
u9b_chk('s2c_pages', @() struct2cell(repmat(struct('a', 1, 'b', 2), 1, 1, 2)));
u9b_chk('s2c_index', @() s2c_pick(struct('a', 1, 'b', 2), 2));
u9b_chk('s2c_index_three', @() s2c_pick(struct('a', {1, 2}, 'b', {3, 4}), [2 1 2]));
u9b_chk('s2c_number', @() struct2cell(1));
u9b_chk('s2c_round_trip', @() cell2struct(struct2cell(struct('a', 1, 'b', 2)), {'a'; 'b'}, 1));

function r = map_outputs(m, n)
if n == 2
    [a, b] = size(m);
    r = [a b];
else
    [a, b, c] = size(m);
    r = [a b c];
end
end

function r = map_grown()
m = containers.Map({'a', 'b'}, {1, 2});
m('c') = 3;
r = size(m);
end

function r = map_shrunk()
m = containers.Map({'a', 'b'}, {1, 2});
remove(m, 'a');
r = size(m);
end

function w = whos_of(m) %#ok<INUSD>
w = whos('m');
end

function t = dyn_write(t, name)
t.(name) = 1;
end

function t = dyn_write_value(t, name, v)
t.(name) = v;
end

function r = dyn_write_unborn(name)
y.(name) = 1;
r = y;
end

function r = dyn_write_unborn_exists(name)
try
    y.(name) = 1;
catch
end
r = exist('y', 'var');
end

function t = dyn_write_then_dot(t)
t.('1bad').b = 2;
end

function t = dyn_write_indexed(t)
t.('1bad')(2) = 1;
end

function t = dyn_write_element(t)
t(2).('1bad') = 1;
end

function t = dyn_write_deal(t)
[t.('1bad')] = deal(5);
end

function r = dyn_write_in_cell(t)
c = {t};
c{1}.('1bad') = 2;
r = c{1};
end

function r = dyn_read_list(t, name)
r = {t.(name)};
end

function r = dyn_deal_char(t)
[t.(['a'; 'b'])] = deal(5);
r = [t.ab];
end

function r = dyn_table(name)
T = table(1, 'VariableNames', {'x'});
T.(name) = 2;
r = T.(name);
end

function r = s2c_pick(t, at)
c = struct2cell(t);
if isscalar(at)
    r = c{at};
else
    r = c{at(1), at(2), at(3)};
end
end
