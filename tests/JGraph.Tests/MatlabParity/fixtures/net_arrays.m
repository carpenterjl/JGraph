% net_arrays.m -- .NET arrays in MATLAB (interop plan, stage 4): what a returned array is, 1-based
% indexing and its refusals, conversion to MATLAB arrays, jagged and 2-D arrays, string and object
% arrays, NET.createArray and NET.convertArray, and MATLAB arrays passed to array parameters.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

r = JGTest.ArrayMaker.Ramp(int32(3));
ix_chk('class', class(r));
ix_chk('size', size(r));
ix_chk('numel', numel(r));
ix_chk('Length', r.Length);
ix_chk('Rank', r.Rank);
ix_chk('index_1', r(1));
ix_chk('index_0_refused', ix_id(@() r(0)));
ix_chk('index_end_refused', ix_id(@() r(end)));
ix_chk('index_range_refused', ix_id(@() r(1:2)));
ix_chk('index_colon_refused', ix_id(@() r(:)));
ix_chk('GetValue', r.GetValue(0));
r(1) = 50;
ix_chk('set_index', r.GetValue(0));
ix_chk('set_out_of_range', ix_id(@() setat(r, 10, 1)));
ix_chk('double', double(r));
ix_chk('plus_refused', ix_id(@() r + 1));
ix_chk('sum_refused', ix_id(@() sum(r)));
ix_chk('echo', ix_flat(evalc('r')), 'div=ADR0174');
ix_chk('empty_isempty', isempty(JGTest.ArrayMaker.Empty()));
ix_chk('empty_double', double(JGTest.ArrayMaker.Empty()));

g = JGTest.ArrayMaker.Grid(int32(2), int32(3));
ix_chk('grid_class', class(g));
ix_chk('grid_size', size(g));
ix_chk('grid_index', g(2, 3));
ix_chk('grid_linear_refused', ix_id(@() g(4)));
ix_chk('grid_double', double(g));
ix_chk('grid_GetLength', g.GetLength(1));

j = JGTest.ArrayMaker.Jagged();
ix_chk('jagged_class', class(j));
ix_chk('jagged_outer', class(j(2)));
ix_chk('jagged_double_row', double(j(3)));
ix_chk('jagged_cell', cell(j));
ix_chk('jagged_double_refused', ix_id(@() double(j)));

ix_chk('ints', int32(JGTest.ArrayMaker.Ints()));
ix_chk('ints_class', class(JGTest.ArrayMaker.Ints()));
ix_chk('bools', logical(JGTest.ArrayMaker.Bools()));
ix_chk('chars_refused', ix_id(@() char(JGTest.ArrayMaker.Chars())));
ix_chk('bytes', uint8(JGTest.ArrayMaker.Bytes()));

w = JGTest.ArrayMaker.Words();
ix_chk('words_class', class(w));
ix_chk('words_index', w(2));
ix_chk('words_string', string(w));
ix_chk('words_cell', cell(w));
ix_chk('words_cellstr_refused', ix_id(@() cellstr(w)));
ix_chk('words_char_refused', ix_id(@() char(w)));

mx = JGTest.ArrayMaker.Mixed();
ix_chk('mixed_class', class(mx));
ix_chk('mixed_cell', cell(mx));
ix_chk('mixed_index_1', mx(1));
ix_chk('mixed_index_null', mx(5));

ix_chk('pass_row', JGTest.ArrayMaker.Sum([1 2 3]));
ix_chk('pass_col', JGTest.ArrayMaker.Sum([1; 2; 3]));
ix_chk('pass_int32', JGTest.ArrayMaker.Sum(int32([1 2 3])));
ix_chk('pass_scalar', JGTest.ArrayMaker.Sum(5));
ix_chk('pass_empty_is_null', ix_id(@() JGTest.ArrayMaker.Sum([])));
ix_chk('pass_matrix_2d', JGTest.ArrayMaker.Sum2([1 2 3; 4 5 6]));
ix_chk('pass_matrix_shape', JGTest.ArrayMaker.Shape([1 2 3; 4 5 6]));
ix_chk('pass_row_shape', JGTest.ArrayMaker.Shape([1 2 3]));
ix_chk('pass_cellstr', JGTest.ArrayMaker.Count({'a', 'b'}));
ix_chk('pass_string_array', JGTest.ArrayMaker.Count(["a" "b" "c"]));
ix_chk('pass_char_matrix_refused', ix_id(@() JGTest.ArrayMaker.Count(['ab'; 'cd'])));
ix_chk('pass_cell_kinds', JGTest.ArrayMaker.Kinds({1, 'a', int8(2), true, "s", [1 2], {3}}));
ix_chk('pass_cell_empty_is_null', JGTest.ArrayMaker.Kinds({[]}));

ix_chk('createArray_class', class(NET.createArray('System.Double', 3)));
ix_chk('createArray_Length', NET.createArray('System.Double', 3).Length);
ix_chk('createArray_zeros', double(NET.createArray('System.Double', 3)));
ix_chk('createArray_2d', class(NET.createArray('System.Int32', 2, 3)));
ix_chk('createArray_2d_Rank', NET.createArray('System.Int32', 2, 3).Rank);
ix_chk('createArray_jagged', class(NET.createArray('System.Double[]', 2)));
ix_chk('createArray_String', class(NET.createArray('System.String', 2)));
ix_chk('createArray_bad_type', ix_id(@() NET.createArray('No.Such.Type', 2)));
ix_chk('convertArray_row', class(NET.convertArray([1 2 3])));
ix_chk('convertArray_mat_Rank', NET.convertArray([1 2; 3 4]).Rank);
ix_chk('convertArray_col_Rank', NET.convertArray([1; 2; 3]).Rank);
ix_chk('convertArray_type', class(NET.convertArray([1 2 3], 'System.Int32')));
ix_chk('convertArray_dims', NET.convertArray([1 2 3], 'System.Double', [1 3]).Rank);
ix_chk('convertArray_char', class(NET.convertArray('abc')));

% Stage 4's own probes (probe4, probe4b, probe4c): the edges of the rules above.
jr = NET.createArray('System.Double[]', 2);
jr(1) = NET.convertArray([1 2 3]);
jr(2) = NET.convertArray([4 5 6]);
ix_chk('jagged_rect_double', double(jr));
ix_chk('jagged_rect_int32', int32(jr));
ix_chk('jagged_null_double', double(NET.createArray('System.Double[]', 2)));
ix_chk('index_two_on_1d_refused', ix_id(@() r(1, 1)));
ix_chk('index_none_refused', ix_id(@() r()));
ix_chk('index_frac_refused', ix_id(@() r(1.5)));
ix_chk('index_logical_refused', ix_id(@() r(true)));
ix_chk('index_char_refused', ix_id(@() r('a')));
ix_chk('index_int32', r(int32(2)));
ix_chk('index_out_of_range', ix_id(@() r(10)));
ix_chk('grid_three_refused', ix_id(@() g(1, 1, 1)));
ix_chk('grid_out_of_range', ix_id(@() g(3, 1)));
ix_chk('set_char_refused', ix_id(@() setat(r, 1, 'x')));
ix_chk('set_vector_refused', ix_id(@() setat(r, 1, [1 2])));
ix_chk('set_empty_refused', ix_id(@() setat(r, 1, [])));
setat(r, 2, int8(7));
ix_chk('set_int8', r(2));
setat2(g, 2, 1, 99);
ix_chk('set_grid', g(2, 1));
ix_chk('set_grid_linear_refused', ix_id(@() setat(g, 2, 99)));
setat(w, 1, 'zz');
ix_chk('set_words', w(1));
ix_chk('set_words_number_refused', ix_id(@() setat(w, 1, 5)));
ix_chk('brace_refused', ix_id(@() r{1}));
ix_chk('cell_double_refused', ix_id(@() cell(r)));
ix_chk('cell_ints_refused', ix_id(@() cell(JGTest.ArrayMaker.Ints())));
ix_chk('cell_grid_refused', ix_id(@() cell(g)));
ix_chk('cell_empty_objects', cell(NET.createArray('System.Object', 0)));
ix_chk('string_double_refused', ix_id(@() string(r)));
ix_chk('string_mixed_refused', ix_id(@() string(mx)));
ix_chk('double_words_refused', ix_id(@() double(w)));
ix_chk('int8', int8(r));
ix_chk('single', single(r));
ix_chk('logical', logical(JGTest.ArrayMaker.Ints()));
ix_chk('double_bools', double(JGTest.ArrayMaker.Bools()));
ix_chk('minus_refused', ix_id(@() r - 1));
ix_chk('max_refused', ix_id(@() max(r)));
ix_chk('mean_refused', ix_id(@() mean(r)));
ix_chk('concat_refused', ix_id(@() [r r]));
ix_chk('eq_self', r == r);
ch = JGTest.ArrayMaker.Chars();
ix_chk('chars_index', ch(2));
ix_chk('chars_Get', ch.Get(int32(0)));
ix_chk('empty_index_refused', ix_id(@() getat(JGTest.ArrayMaker.Empty(), 1)));
ix_chk('createArray_dims_vector', class(NET.createArray('System.Double', [2 3])));
ix_chk('createArray_GenericClass', class(NET.createArray(NET.GenericClass('System.Collections.Generic.List', 'System.Double'), 2)));
ix_chk('createArray_zero', NET.createArray('System.Double', 0).Length);
ix_chk('createArray_enum', class(NET.createArray('JGTest.Color', 2)));
ix_chk('enum_array_double_refused', ix_id(@() double(NET.createArray('JGTest.Color', 2))));
ix_chk('convertArray_cell_refused', ix_id(@() NET.convertArray({1, {2}})));
ix_chk('convertArray_bad_type', ix_id(@() NET.convertArray([1 2], 'No.Such')));
ix_chk('properties', strjoin(properties(r)', ','));

function setat2(g, i, j, v)
g(i, j) = v;
end

function v = getat(a, k)
v = a(k);
end

function setat(r, k, v)
r(k) = v;
end
