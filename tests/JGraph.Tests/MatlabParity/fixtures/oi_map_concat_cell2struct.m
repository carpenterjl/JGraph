% Open item 317 (ADR 0358). A containers.Map joins by its class's own verbs: side by side is refused,
% stacked is a new map, a copy of the first with each later map's entries written in (a later key wins,
% each key and value converted to the first map's types or refused); a piece that is not a map after the
% first is the method's argument validation refusing it, a first piece that is not a map R2025b's failed
% conversion through the constructor, and leading empties leave the bracket. A for loop over a map is
% refused. A map inside a struct field or a cell shows its size and class. cell2struct takes a dimension
% and a cell of any size. Probes p317, p317b, p317c, p317d, p317e (agent X's scratch).

m1 = containers.Map({'a', 'b'}, {1, 2});
m2 = containers.Map({'b', 'c'}, {20, 30});
mn = containers.Map([1 2], {'x', 'y'});
md = containers.Map({'a'}, [5]);
ms = containers.Map({'z'}, {'text'});
mi = containers.Map('KeyType', 'int32', 'ValueType', 'double');
mi(int32(4)) = 7;
m0 = containers.Map();
ma = containers.Map({'p', 'q'}, {1, 'two'});
mai = containers.Map('KeyType', 'char', 'ValueType', 'any');
mai('k') = 4;
ml = containers.Map('KeyType', 'char', 'ValueType', 'logical');
ml('t') = true;
mint = containers.Map('KeyType', 'char', 'ValueType', 'int32');
mint('i') = int32(3);
msg = containers.Map('KeyType', 'single', 'ValueType', 'any');
msg(single(2)) = 'w';
u9b_chk('hcat', @() [m1 m2]);
u9b_chk('hcat_same', @() [m1, m1]);
u9b_chk('hcat_fn', @() horzcat(m1, m2));
u9b_chk('hcat_with_empty', @() [m1, []]);
u9b_chk('hcat_with_number', @() [m1, 5]);
u9b_chk('hcat_three', @() [m1, m2, 5]);
u9b_chk('grid', @() [m1 m2; m1 m2]);
u9b_chk('grid_lower_row', @() [m1; m2 m1]);
u9b_chk('vcat_class', @() class([m1; m2]));
u9b_chk('vcat', @() kv([m1; m2]));
u9b_chk('vcat_count', @() prop([m1; m2], 'Count'));
u9b_chk('vcat_size', @() size([m1; m2]));
u9b_chk('vcat_fn', @() kv(vertcat(m1, m2)));
u9b_chk('vcat_later_wins', @() kv([m2; m1]));
u9b_chk('vcat_same', @() kv([m1; m1]));
u9b_chk('vcat_empty_map_first', @() kv([m0; m1]));
u9b_chk('vcat_empty_maps', @() prop([m0; m0], 'Count'));
u9b_chk('vcat_numeric_keys', @() kv([mn; containers.Map([2 3], {'Y', 'Z'})]));
u9b_chk('vcat_copies', @() vcat_copies(m1, m2));
u9b_chk('vcat_three_mismatch', @() kv([m1; m2; ms]));
u9b_chk('vcat_value_char_into_double', @() kv([m1; ms]));
u9b_chk('vcat_value_double_double', @() kv([md; md]));
u9b_chk('vcat_value_any_double', @() kv([ma; md]));
u9b_chk('vcat_value_double_any', @() kv([md; ma]));
u9b_chk('vcat_value_double_any_of_doubles', @() kv([md; mai]));
u9b_chk('vcat_value_double_logical', @() kv([md; ml]));
u9b_chk('vcat_value_logical_double', @() kv([ml; md]));
u9b_chk('vcat_value_double_int32', @() kv([md; mint]));
u9b_chk('vcat_value_int32_double', @() kv([mint; md]));
u9b_chk('vcat_value_char_any', @() kv([ms; ma]));
u9b_chk('vcat_value_any_char', @() kv([ma; ms]));
u9b_chk('vcat_key_char_double', @() kv([m1; mn]));
u9b_chk('vcat_key_double_single', @() kv([mn; msg]));
u9b_chk('vcat_key_single_double', @() kv([msg; mn]));
u9b_chk('vcat_key_int32_double', @() kv([mi; containers.Map([1.5], [2])]));
u9b_chk('vcat_key_double_int32', @() kv([containers.Map([1.5], [2]); mi]));
u9b_chk('vcat_key_int32_char_values', @() kv([mi; mn]));
u9b_chk('vcat_key_double_int32_char', @() kv([mn; mi]));
u9b_chk('vcat_with_number', @() [m1; 5]);
u9b_chk('vcat_with_empty', @() [m1; []]);
u9b_chk('vcat_with_struct', @() [m1; struct('a', 1)]);
u9b_chk('vcat_with_cell', @() [m1; {1}]);
u9b_chk('vcat_with_char', @() [m1; 'x']);
u9b_chk('vcat_with_zeros', @() [m1; zeros(1, 0)]);
u9b_chk('vcat_three_number', @() [m1; m2; 5]);
u9b_chk('vcat_fn_number', @() vertcat(m1, m2, 5));
u9b_chk('vcat_empty_middle', @() [m1; []; m2]);
u9b_chk('vcat_number_first', @() [5; m1]);
u9b_chk('vcat_char_first', @() ['x'; m1]);
u9b_chk('vcat_cell_first', @() [{1}; m1]);
u9b_chk('vcat_struct_first', @() [struct('a', 1); m1]);
u9b_chk('hcat_number_first', @() [5, m1]);
u9b_chk('vcat_empty_then_map', @() kv([[]; m1]));
u9b_chk('vcat_empty_char_then_map', @() kv(['', m1]));
u9b_chk('vcat_empty_cell_then_map', @() kv([{}; m1]));
u9b_chk('vcat_empty_struct_then_map', @() kv([struct([]); m1]));
u9b_chk('vcat_zeros_then_map', @() kv([zeros(1, 0); m1; zeros(1, 0)]));
u9b_chk('vcat_empties_around', @() kv([[]; m1; []]));
u9b_chk('hcat_empties_around', @() kv(['', m1, []]));
u9b_chk('vcat_one_fn', @() kv(vertcat(m1)));
u9b_chk('cat1', @() kv(cat(1, m1, m2)));
u9b_chk('cat1_one', @() kv(cat(1, m1)));
u9b_chk('cat2', @() cat(2, m1, m2));
u9b_chk('cat3', @() cat(3, m1, m2));
u9b_chk('cat3_one', @() cat(3, m1));
u9b_chk('cat3_empty_first', @() cat(3, [], m1));
u9b_chk('cat3_number', @() cat(3, m1, 5));
u9b_chk('cat1_number', @() cat(1, m1, 5));
u9b_chk('cat1_number_first', @() cat(1, 5, m1));
u9b_chk('vcat_nested', @() prop([[m1; m2]; ms], 'Count'));
u9b_chk('vcat_in_cell', @() size({[m1; m2]}));
u9b_chkdiv('vcat_empty_then_two_maps', @() id_or_count(@() [[]; m1; m2]), 'ADR0358');
u9b_chk('for_map', @() for_map(m1));
u9b_chk('for_empty_map', @() for_map(m0));
u9b_chk('for_one_map', @() for_map(containers.Map({'q'}, {1})));
u9b_chk('for_map_body', @() for_body(m1));
u9b_chk('show_struct_field', @() field_shown(struct('m', m1), 'm'));
u9b_chk('show_struct_field_empty', @() field_shown(struct('m', m0), 'm'));
u9b_chk('show_struct_field_one', @() field_shown(struct('m', containers.Map({'q'}, {1})), 'm'));
u9b_chk('show_struct_second_field', @() field_shown(struct('n', 1, 'm', m1), 'm'));
u9b_chk('show_nested_struct_field', @() field_shown(getfield(struct('s', struct('m', m1)), 's'), 'm'));
u9b_chk('show_cell', @() cell_shown({m1}));
u9b_chk('show_cell_empty', @() cell_shown({m0}));
c23 = {1, 2, 3; 4, 5, 6};
c3 = reshape(num2cell(1:24), 2, 3, 4);
c4 = reshape(num2cell(1:16), 2, 2, 2, 2);
u9b_chk('c2s_col_dim1', @() cell2struct({1; 2}, {'a', 'b'}, 1));
u9b_chk('c2s_row_dim2', @() cell2struct({1, 2}, {'a', 'b'}, 2));
u9b_chk('c2s_row_dim1', @() cell2struct({1, 2}, {'a'}, 1));
u9b_chk('c2s_col_dim2', @() cell2struct({1; 2}, {'a'}, 2));
u9b_chk('c2s_23_dim1', @() cell2struct(c23, {'a', 'b'}, 1));
u9b_chk('c2s_23_dim2', @() cell2struct(c23, {'x', 'y', 'z'}, 2));
u9b_chk('c2s_23_dim3', @() cell2struct(c23, {'q'}, 3));
u9b_chk('c2s_23_dim4_size', @() size(cell2struct(c23, {'q'}, 4)));
u9b_chk('c2s_nd_dim1_size', @() size(cell2struct(c3, {'a', 'b'}, 1)));
u9b_chk('c2s_nd_dim2_size', @() size(cell2struct(c3, {'a', 'b', 'c'}, 2)));
u9b_chk('c2s_nd_dim3_size', @() size(cell2struct(c3, {'a', 'b', 'c', 'd'}, 3)));
u9b_chk('c2s_nd_dim1', @() cell2struct(c3, {'a', 'b'}, 1));
u9b_chk('c2s_nd_dim2_element', @() pick(cell2struct(c3, {'a', 'b', 'c'}, 2), [2 4]));
u9b_chk('c2s_nd_dim3_element', @() pick(cell2struct(c3, {'a', 'b', 'c', 'd'}, 3), [2 3]));
u9b_chk('c2s_4d_dim2_size', @() size(cell2struct(c4, {'a', 'b'}, 2)));
u9b_chk('c2s_4d_dim4_size', @() size(cell2struct(c4, {'a', 'b'}, 4)));
u9b_chk('c2s_4d_dim4_element', @() pick(cell2struct(c4, {'a', 'b'}, 4), [2 1 2]));
u9b_chk('c2s_two_args', @() cell2struct({1; 2}, {'a', 'b'}));
u9b_chk('c2s_two_args_row', @() cell2struct({1, 2}, {'a', 'b'}));
u9b_chk('c2s_fields_char_matrix', @() cell2struct({1; 2}, ['ab'; 'cd'], 1));
u9b_chk('c2s_fields_char_row', @() cell2struct({1}, 'abc', 1));
u9b_chk('c2s_fields_char_padded', @() cell2struct({1; 2}, ['a '; 'bc'], 1));
u9b_chk('c2s_fields_string', @() cell2struct({1; 2}, ["a" "b"], 1));
u9b_chk('c2s_fields_string_scalar', @() cell2struct({1}, "a", 1));
u9b_chk('c2s_fields_string_column', @() cell2struct({1; 2}, ["a"; "b"], 1));
u9b_chk('c2s_fields_string_empty', @() cell2struct({1}, "", 1));
u9b_chk('c2s_fields_missing', @() cell2struct({1}, string(missing), 1));
u9b_chk('c2s_fields_column_cell', @() cell2struct({1; 2}, {'a'; 'b'}, 1));
u9b_chk('c2s_fields_cell_char_matrix', @() cell2struct({1}, {['a'; 'b']}, 1));
u9b_chk('c2s_fields_cell_number', @() cell2struct({1}, {1}, 1));
u9b_chk('c2s_fields_cell_empty_name', @() cell2struct({1}, {''}, 1));
u9b_chk('c2s_fields_cell_string', @() cell2struct({1}, {"a"}, 1));
u9b_chk('c2s_fields_number', @() cell2struct({1, 2}, [1 2], 2));
u9b_chk('c2s_fields_leading_blank', @() cell2struct({1}, {' a'}, 1));
u9b_chk('c2s_fields_trailing_blank', @() cell2struct({1}, {'a '}, 1));
u9b_chk('c2s_long_name', @() fieldnames(cell2struct({1}, {repmat('a', 1, 70)}, 1)));
u9b_chk('c2s_count_wrong', @() cell2struct(c23, {'a', 'b', 'c'}, 1));
u9b_chk('c2s_count_short', @() cell2struct(c23, {'a'}, 1));
u9b_chk('c2s_count_before_name', @() cell2struct({1; 2}, {'1a'}, 1));
u9b_chk('c2s_duplicate', @() cell2struct({1; 2}, {'a', 'a'}, 1));
u9b_chk('c2s_invalid', @() cell2struct({1}, {'1a'}, 1));
u9b_chk('c2s_invalid_after_duplicate', @() cell2struct({1; 2; 3}, {'b', 'b', '1a'}, 1));
u9b_chk('c2s_dim0', @() cell2struct({1; 2}, {'a', 'b'}, 0));
u9b_chk('c2s_dim_negative', @() cell2struct({1; 2}, {'a', 'b'}, -1));
u9b_chk('c2s_dim_fraction', @() cell2struct({1; 2}, {'a', 'b'}, 1.5));
u9b_chk('c2s_dim_vector', @() cell2struct({1; 2}, {'a', 'b'}, [1 2]));
u9b_chk('c2s_dim_empty', @() cell2struct({1; 2}, {'a', 'b'}, []));
u9b_chk('c2s_dim_char', @() cell2struct({1; 2}, {'a', 'b'}, '1'));
u9b_chk('c2s_dim_single', @() cell2struct({1; 2}, {'a', 'b'}, single(1)));
u9b_chk('c2s_dim_int8', @() cell2struct({1; 2}, {'a', 'b'}, int8(1)));
u9b_chk('c2s_dim_logical', @() cell2struct({1; 2}, {'a', 'b'}, true));
u9b_chk('c2s_dim_inf', @() cell2struct({1; 2}, {'a', 'b'}, Inf));
u9b_chk('c2s_dim_nan', @() cell2struct({1; 2}, {'a', 'b'}, NaN));
u9b_chk('c2s_dim_past_ndims', @() size(cell2struct({1; 2}, {'a'}, 5)));
u9b_chk('c2s_not_cell', @() cell2struct([1 2], {'a', 'b'}, 2));
u9b_chk('c2s_not_cell_before_dim', @() cell2struct(5, {'a'}, 0));
u9b_chk('c2s_dim_before_fields', @() cell2struct({1}, 5, 0));
u9b_chk('c2s_dim_before_count', @() cell2struct({1; 2}, {'a'}, 0));
u9b_chk('c2s_one_arg', @() cell2struct({1}));
u9b_chk('c2s_four_args', @() cell2struct({1}, {'a'}, 1, 2));
u9b_chk('c2s_empty_rows', @() size(cell2struct(cell(0, 3), {'a', 'b', 'c'}, 2)));
u9b_chk('c2s_empty_rows_dim1', @() size(cell2struct(cell(0, 3), {}, 1)));
u9b_chk('c2s_empty_columns', @() size(cell2struct(cell(2, 0), {'a', 'b'}, 1)));
u9b_chk('c2s_empty_both_dim1', @() size(cell2struct({}, {}, 1)));
u9b_chk('c2s_empty_both_dim2', @() size(cell2struct({}, {}, 2)));
u9b_chk('c2s_no_fields', @() cell2struct(cell(0, 2), {}, 1));
u9b_chk('c2s_roundtrip', @() isequal(cell2struct(struct2cell(struct('a', {1, 2}, 'b', {3, 4})), {'a', 'b'}, 1), struct('a', {1, 2}, 'b', {3, 4})));
u9b_chk('c2s_roundtrip_size', @() size(cell2struct(struct2cell(struct('a', {1, 2}, 'b', {3, 4})), {'a', 'b'}, 1)));
u9b_chk('c2s_roundtrip_column', @() size(cell2struct(struct2cell(struct('a', {1; 2}, 'b', {3; 4})), {'a', 'b'}, 1)));
u9b_chk('c2s_scalar_roundtrip', @() cell2struct(struct2cell(struct('a', 1, 'b', 'x')), {'a', 'b'}, 1));
u9b_chk('c2s_one', @() size(cell2struct({5}, {'a'}, 2)));
u9b_chk('c2s_row_dim1_size', @() size(cell2struct(num2cell(ones(1, 4)), {'a'}, 1)));
u9b_chk('c2s_struct_value', @() cell2struct({struct('x', 1); [1 2]}, {'s', 'v'}, 1));

function r = kv(m)
r = {keys(m), values(m), m.KeyType, m.ValueType};
end

function r = prop(m, name)
r = m.(name);
end

function r = vcat_copies(m1, m2)
m = [m1; m2];
m('new') = 9;
r = {m1.Count, m2.Count, m.Count};
end

function r = id_or_count(fn)
try
    m = fn();
    r = m.Count;
catch err
    r = err.identifier;
end
end

function r = for_map(m)
r = 0;
for k = m
    r = r + 1;
end
end

function r = for_body(m)
r = 'unrun';
for k = m
    r = class(k);
end
end

function r = field_shown(s, name)
% The fragment a struct's display writes after the field's name (R2025b's own line layout aside).
t = evalc('disp(s)');
r = regexp(t, [name ': ([^\n,)]*)'], 'tokens', 'once');
r = r{1};
end

function r = cell_shown(c)
% A one-element cell's display, trimmed.
r = strtrim(evalc('disp(c)'));
end

function r = pick(s, at)
if numel(at) == 2
    r = s(at(1), at(2));
else
    r = s(at(1), at(2), at(3));
end
end
