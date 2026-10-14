% record: -noFigureWindows
% Open item 115 (ADR 0232): categorical as a type of its own, in the MATLAB dialect: the
% constructor forms and their refusals, display and summary, comparison by name and by order,
% concatenation that merges categories, indexed writes of names, the category verbs, the
% conversions, sort/unique/min/max/median/mode, the set verbs, missing handling, findgroups and
% discretize, a table's categorical variable, and StyleConfigurations.Target as a protected
% categorical. Lines marked div=ADR0232 are where this build keeps its own display of a cell,
% struct, table or N-D array, a handle's class name (a double here), a categorical's method and
% property lists, ismissing and fillmissing over a table, grp2idx (a JGraph extra) and a table's
% summary struct; ADR 0233 made the charts, the table verbs and the refusals exact.
close all force
c = categorical({'a', 'b', 'a', 'c'});
o = categorical([1 3 2], [1 2 3], {'lo', 'mid', 'hi'}, 'Ordinal', true);
u9b_chk('ctor_cell', @() c);
u9b_chk('class', @() class(c)); u9b_chk('iscategorical', @() iscategorical(c)); u9b_chk('isa', @() isa(c, 'categorical'));
u9b_chk('cats', @() categories(c)); u9b_chk('countcats', @() countcats(c)); u9b_chk('isordinal', @() isordinal(c)); u9b_chk('isprotected', @() isprotected(c));
u9b_chk('ctor_col', @() categorical({'b'; 'a'})); u9b_chk('ctor_num', @() categorical([3 1 2 1])); u9b_chk('ctor_num_cats', @() categories(categorical([3 1 2 1])));
u9b_chk('ctor_string', @() categorical(["x" "y" "x"])); u9b_chk('ctor_char', @() categorical('abc')); u9b_chk('ctor_char_col', @() categorical(['ab'; 'cd']));
u9b_chk('ctor_logical', @() categorical([true false]));
u9b_chk('ctor_valueset', @() categorical({'a', 'b'}, {'a', 'b', 'c'})); u9b_chk('ctor_valueset_cats', @() categories(categorical({'a', 'b'}, {'a', 'b', 'c'})));
u9b_chk('ctor_valueset_names', @() categorical([1 2 3 2], [1 2 3], {'lo', 'mid', 'hi'}));
u9b_chk('ctor_not_in_set', @() categorical({'a', 'z'}, {'a', 'b'}));
u9b_chk('ctor_ordinal', @() o);
u9b_chk('ord_isordinal', @() isordinal(o)); u9b_chk('ord_lt', @() o(1) < o(2)); u9b_chk('ord_max', @() max(o)); u9b_chk('ord_min', @() min(o)); u9b_chk('ord_sort', @() sort(o)); u9b_chk('ord_sortd', @() sort(o, 'descend'));
u9b_chk('ord_ge', @() o >= 'mid'); u9b_chk('ord_cmp_str', @() o > "lo"); u9b_chk('ord_median', @() median(o)); u9b_chk('ord_cats', @() categories(o));
u9b_chk('nonord_lt', @() c(1) < c(2)); u9b_chk('nonord_max', @() max(c)); u9b_chk('nonord_min', @() min(c));
u9b_chk('ctor_protected', @() isprotected(categorical({'a'}, {'a', 'b'}, 'Protected', true)));
u9b_chk('ctor_missing', @() categorical({'a', ''})); u9b_chk('ctor_missing_str', @() categorical(["a" missing]));
u9b_chk('ctor_nan', @() categorical([1 NaN 2]));
u9b_chk('ctor_empty', @() categorical({})); u9b_chk('ctor_empty2', @() categorical([])); u9b_chk('ctor_empty_cats', @() categories(categorical({})));
u9b_chk('ctor_cats_dup', @() categorical({'a', 'b'}, {'a', 'a'}));
u9b_chk('ctor_matrix', @() categorical({'a', 'b'; 'c', 'a'}));
u9b_chk('ctor_undef', @() categorical({'a', '<undefined>'}));
u9b_chk('ctor_num_names', @() categorical([1 2], [1 2], ["one", "two"])); u9b_chk('ctor_names_bad_count', @() categorical([1 2], [1 2], {'one'}));
u9b_chk('ctor_ordinal_num', @() isordinal(categorical({'a'}, 'Ordinal', 1)));
u9b_chk('ctor_of_cat', @() categorical(c)); u9b_chk('ctor_of_cat_cats', @() categories(categorical(c, {'c', 'a'})));
u9b_chk('cats_order_num', @() categories(categorical([10 2 33]))); u9b_chk('cats_order_str', @() categories(categorical({'b', 'A', 'a', 'B'})));
u9b_chk('cat_num_names', @() string(categorical([1.5 2 1e6 0.1 100000 123456789])));
u9b_chkdiv('cat_datetime', @() oi_try( @() categories(categorical(datetime(2020, 1, [1 2])))), 'ADR0232');
u9b_chk('cat_bool_names', @() categories(categorical([true false])));
u9b_chk('cat_cell_ctor_num', @() categorical({1, 2}));
u9b_chk('cat_mixed_cell', @() categorical({'a', 1}));
u9b_chk('cat_nested', @() categorical({{'a'}}));
u9b_chk('string_ctor_dupes', @() categories(categorical(["b", "a", "b"])));
u9b_chk('valueset_string', @() categorical(["a", "b"], ["b", "a"]));
u9b_chk('valueset_num_names_str', @() categorical([1 2], [1 2], ["one", "two"]));
u9b_chk('strings_in_cats_ws', @() categories(categorical({' a ', 'a'})));
u9b_chk('cats_case', @() categories(categorical({'a', 'A'})));
u9b_chk('cats_num_text', @() categories(categorical({'2', '10', '1'})));
u9b_chk('cats_valueset_order', @() categories(categorical({'a', 'b'}, {'b', 'a'})));
u9b_chk('cats_valueset_dup_names', @() categorical([1 2 3], [1 2 3], {'x', 'x', 'y'}));
u9b_chk('cats_valueset_dup_names_cats', @() categories(categorical([1 2 3], [1 2 3], {'x', 'x', 'y'})));
u9b_chk('cat_of_logical_matrix', @() categorical(eye(2) > 0));
u9b_chk('cat_int', @() categories(categorical(int8([3 1]))));
u9b_chk('cat_single', @() categories(categorical(single([1.5 2]))));
u9b_chk('cat_cellstr_num_mixed_names', @() categorical({'a', 'b'}, {'a', 'b'}, {'x', 'y'}));
u9b_chk('cat_cellstr_num_mixed_names_val', @() cellstr(categorical({'a', 'b'}, {'a', 'b'}, {'x', 'y'})));
u9b_chk('eq_char', @() c == 'a'); u9b_chk('eq_str', @() c == "a"); u9b_chk('ne_char', @() c ~= 'a'); u9b_chk('eq_cat', @() c == c); u9b_chk('eq_cat2', @() c == categorical({'a', 'a', 'b', 'c'}));
u9b_chk('eq_other_cats', @() c == categorical({'a', 'q', 'a', 'q'}, {'q', 'a'}));
u9b_chk('eq_num', @() c == 1); u9b_chk('eq_cell', @() c == {'a'}); u9b_chk('eq_cell_many', @() c == {'a', 'b', 'a', 'c'});
u9b_chk('eq_scalar_expand', @() c == c(1));
u9b_chk('strcmp', @() strcmp(c, 'a')); u9b_chk('strcmp_cell', @() strcmp(c, {'a', 'b', 'a', 'c'})); u9b_chk('strcmpi', @() strcmpi(c, 'A'));
u9b_chk('ismember', @() ismember(c, 'a')); u9b_chk('ismember2', @() ismember('a', c)); u9b_chk('ismember3', @() ismember(c, {'a', 'b'})); u9b_chk('ismember_cat', @() ismember(c, categorical({'a'})));
[tf, loc] = ismember(c, {'c', 'a'}); u9b_chk('ismember_loc', @() loc);
u9b_chk('isequal', @() isequal(c, categorical({'a', 'b', 'a', 'c'}))); u9b_chk('isequal_cell', @() isequal(c, {'a', 'b', 'a', 'c'}));
u9b_chk('isequal_diffcats', @() isequal(c, categorical({'a', 'b', 'a', 'c'}, {'c', 'b', 'a'})));
u9b_chk('isequal_ord', @() isequal(o, categorical([1 3 2], [1 2 3], {'lo', 'mid', 'hi'})));
u9b_chk('isequaln', @() isequaln(categorical({'a', ''}), categorical({'a', ''}))); u9b_chk('isequal_undef', @() isequal(categorical({'a', ''}), categorical({'a', ''})));
u9b_chk('eq_undef', @() categorical({'a', ''}) == categorical({'a', ''}));
u9b_chk('ismember_undef', @() ismember(categorical({'a', ''}), 'a'));
u9b_chk('ord_lt_nonord', @() o < c(1:3));
u9b_chk('ord_eq_other_ord', @() o == categorical([1 3 2], [1 2 3], {'lo', 'mid', 'hi'}, 'Ordinal', true));
u9b_chk('ord_eq_other_ord_cats', @() o == categorical([1 3 2], [1 2 3], {'lo', 'hi', 'mid'}, 'Ordinal', true));
u9b_chk('string', @() string(c)); u9b_chk('char', @() char(c)); u9b_chk('cellstr', @() cellstr(c)); u9b_chk('double', @() double(c)); u9b_chk('single', @() single(c)); u9b_chk('int32', @() int32(c)); u9b_chk('uint8', @() uint8(c)); u9b_chk('logical', @() logical(c));
u9b_chk('string_undef', @() string(categorical({'a', ''}))); u9b_chk('cellstr_undef', @() cellstr(categorical({'a', ''}))); u9b_chk('double_undef', @() double(categorical({'a', ''}))); u9b_chk('char_undef', @() char(categorical({'a', ''})));
u9b_chk('num2str', @() num2str(c)); u9b_chk('mat2str', @() mat2str(c)); u9b_chk('sprintf', @() sprintf('%s', c)); u9b_chk('sprintf_d', @() sprintf('%d', c)); u9b_chk('sprintf_one', @() sprintf('%s', c(1)));
u9b_chk('isundefined', @() isundefined(categorical({'a', ''}))); u9b_chk('ismissing', @() ismissing(categorical({'a', ''}))); u9b_chk('ismissing_string', @() ismissing(categorical({'a', ''}), 'a'));
u9b_chk('iscategory', @() iscategory(c, 'a')); u9b_chk('iscategory2', @() iscategory(c, {'a', 'z'}));
u9b_chk('double_shape', @() size(double(c'))); u9b_chk('string_shape', @() size(string(c'))); u9b_chk('char_shape', @() size(char(c'))); u9b_chk('cellstr_shape', @() size(cellstr(c')));
u9b_chk('matrix_cellstr', @() cellstr(categorical({'a', 'b'; 'c', 'a'}))); u9b_chk('matrix_char', @() char(categorical({'a', 'bb'; 'c', 'a'}))); u9b_chk('mat_string', @() string(categorical({'a', 'b'; 'c', 'a'})));
u9b_chk('cellstr_cats_class', @() class(categories(c))); u9b_chk('cats_shape', @() size(categories(c))); u9b_chk('countcats_shape', @() size(countcats(c))); u9b_chk('countcats_col', @() countcats(c'));
u9b_chk('categorical_to_cat_double_codes', @() double(categorical({'b', 'a'}, {'b', 'a'})));
u9b_chk('datestr_like', @() datestr(c));
u9b_chk('str2double', @() str2double(c)); u9b_chk('str2num', @() str2num(c));
u9b_chk('int_cast_back', @() categorical(int32(c)));
u9b_chk('jsonencode', @() jsonencode(c));
u9b_chk('idx1', @() c(2)); u9b_chk('idx_range', @() c([1 3])); u9b_chk('idx_logical', @() c(c == 'a')); u9b_chk('idx_end', @() c(end)); u9b_chk('idx_colon', @() c(:)); u9b_chk('transpose', @() c'); u9b_chk('idx_out', @() c(9)); u9b_chk('idx_zero', @() c(0));
u9b_chk('end_in_cats', @() c(end - 1:end)); u9b_chk('colon_cat', @() c(1):c(2));
u9b_chk('idx_2d', @() categorical({'a', 'b'; 'c', 'a'})); u9b_chk('idx_2d_el', @() subsref2d()); u9b_chk('idx_2d_row', @() subsref2drow());
u9b_chk('assign_existing', @() assign(c, 2, 'c'));
u9b_chk('assign_new', @() assign(c, 2, 'z')); u9b_chk('assign_new_cats', @() categories(assign(c, 2, 'z')));
u9b_chk('assign_grow', @() assign(c, 6, 'a')); u9b_chk('assign_grow_undef', @() isundefined(assign(c, 6, 'a')));
u9b_chk('delete', @() del(c, 2));
u9b_chk('assign_cat', @() assign(c, 1, c(3)));
u9b_chk('assign_string', @() assign(c, 1, "b"));
u9b_chk('assign_missing', @() assign(c, 1, missing));
u9b_chk('assign_undef_text', @() assign(c, 1, '<undefined>'));
u9b_chk('assign_num', @() assign(c, 1, 1));
u9b_chk('assign_num_code', @() assign(categorical([1 2 3]), 1, 3));
u9b_chk('assign_protected', @() assign(categorical({'a'}, {'a', 'b'}, 'Protected', true), 1, 'b'));
u9b_chk('assign_protected_new', @() assign(categorical({'a'}, {'a', 'b'}, 'Protected', true), 1, 'q'));
u9b_chk('assign_other_cat', @() assign(categorical({'a', 'b'}), 2, categorical({'q'}))); u9b_chk('assign_other_cat_cats', @() categories(assign(categorical({'a', 'b'}), 2, categorical({'q'}))));
u9b_chk('assign_ord_text', @() assign(o, 1, 'hi')); u9b_chk('assign_ord_new', @() assign(o, 1, 'zz'));
u9b_chk('assign_range_scalar', @() assign(c, [1 2], 'c')); u9b_chk('assign_range_cell', @() assign(c, [1 2], {'c', 'b'})); u9b_chk('assign_logical', @() assign(c, c == 'a', 'c'));
u9b_chk('assign_int', @() assign(c, 1, int8(1)));
u9b_chk('assign_empty_to_make', @() assignfresh());
u9b_chk('assign_cell_many', @() assign(c, [1 2 3 4], {'b', 'b', 'b', 'b'}));
u9b_chk('assign_colon', @() assign(c, ':', 'b'));
u9b_chk('cat_same', @() [c c]); u9b_chk('cat_diff', @() [c categorical({'z'})]); u9b_chk('cat_diff_cats', @() categories([c categorical({'z'})]));
u9b_chk('cat_vert', @() [c; c]); u9b_chk('cat_char', @() [c 'a']); u9b_chk('cat_cell', @() [c {'a'}]); u9b_chk('cat_string', @() [c "a"]); u9b_chk('cat_num', @() [c 1]); u9b_chk('cat_missing', @() [c missing]);
u9b_chk('cat_char_cats', @() categories([c 'z'])); u9b_chk('cat_string_cats', @() categories([c "z"]));
u9b_chk('cat_ord_nonord', @() [o c]); u9b_chk('cat_ord_ord', @() [o o]); u9b_chk('cat_ord_diffcats', @() [o categorical(1, 1, {'lo'}, 'Ordinal', true)]);
u9b_chk('horzcat', @() horzcat(c, c)); u9b_chk('vertcat', @() vertcat(c, c)); u9b_chk('cat3', @() size(cat(3, c, c))); u9b_chk('vertcat_mismatch', @() vertcat(c, c(1:2)));
u9b_chk('cat_empty', @() [c categorical({})]); u9b_chk('cat_empty_cats', @() categories([c categorical({})])); u9b_chk('cat_empty_double', @() [c []]);
u9b_chk('repmat', @() repmat(c, 2, 1)); u9b_chk('reshape', @() reshape(c, 2, 2)); u9b_chk('fliplr', @() fliplr(c)); u9b_chk('circshift', @() circshift(c, 1)); u9b_chk('flipud', @() flipud(c')); u9b_chk('permute', @() size(permute(reshape(c, 2, 2), [2 1])));
u9b_chk('cat_prot_nonprot', @() [categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'z'})]);
u9b_chk('cat_prot_same', @() categories([categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'}, {'a', 'b'}, 'Protected', true)]));
u9b_chk('cat_cats_order', @() categories([categorical({'b'}) categorical({'a'})]));
u9b_chk('cat_cats_order2', @() categories([categorical({'b'}, {'b', 'c'}) categorical({'a'}, {'a', 'b'})]));
u9b_chk('addcats', @() categories(addcats(c, 'z'))); u9b_chk('addcats_after', @() categories(addcats(c, 'z', 'After', 'a'))); u9b_chk('addcats_before', @() categories(addcats(c, 'z', 'Before', 'a')));
u9b_chk('addcats_dup', @() addcats(c, 'a')); u9b_chk('addcats_many', @() categories(addcats(c, {'y', 'z'}))); u9b_chk('addcats_string', @() categories(addcats(c, ["y", "z"])));
u9b_chk('removecats', @() removecats(c, 'a')); u9b_chk('removecats_cats', @() categories(removecats(c, 'a'))); u9b_chk('removecats_unused', @() categories(removecats(categorical({'a', 'b'}, {'a', 'b', 'c'}))));
u9b_chk('removecats_missing', @() removecats(c, 'q'));
u9b_chk('mergecats', @() mergecats(c, {'a', 'b'})); u9b_chk('mergecats_cats', @() categories(mergecats(c, {'a', 'b'}))); u9b_chk('mergecats_name', @() categories(mergecats(c, {'a', 'b'}, 'ab'))); u9b_chk('mergecats_name_val', @() mergecats(c, {'a', 'b'}, 'ab'));
u9b_chk('mergecats_order', @() categories(mergecats(c, {'c', 'a'})));
u9b_chk('renamecats', @() renamecats(c, 'a', 'A')); u9b_chk('renamecats_all', @() categories(renamecats(c, {'A', 'B', 'C'}))); u9b_chk('renamecats_bad', @() renamecats(c, 'q', 'Q')); u9b_chk('renamecats_dup', @() renamecats(c, 'a', 'b'));
u9b_chk('reordercats', @() categories(reordercats(c, {'c', 'b', 'a'}))); u9b_chk('reordercats_sorted', @() categories(reordercats(categorical({'b', 'a'}, {'b', 'a'})))); u9b_chk('reordercats_partial', @() reordercats(c, {'c', 'b'}));
u9b_chk('reordercats_ord', @() categories(reordercats(o, {'hi', 'mid', 'lo'}))); u9b_chk('reordercats_ord_lt', @() reordercats(o, {'hi', 'mid', 'lo'}) < 'mid');
u9b_chk('setcats', @() setcats(c, {'a', 'z'})); u9b_chk('setcats_cats', @() categories(setcats(c, {'a', 'z'})));
u9b_chk('sort', @() sort(c)); u9b_chk('sort_desc', @() sort(c, 'descend')); u9b_chk('sort_undef', @() sort(categorical({'b', '', 'a'}))); u9b_chk('sort_undef_desc', @() sort(categorical({'b', '', 'a'}), 'descend'));
[sc, si] = sort(c); u9b_chk('sort_idx', @() si);
u9b_chk('sort_matrix', @() sort(categorical({'b', 'a'; 'a', 'c'}))); u9b_chk('sort_dim2', @() sort(categorical({'b', 'a'; 'a', 'c'}), 2));
u9b_chk('sortrows_cat', @() sortrows(categorical({'b', 'a'; 'a', 'c'})));
u9b_chk('unique', @() unique(c)); u9b_chk('unique_cats', @() categories(unique(c))); u9b_chk('unique_stable', @() unique(categorical({'b', 'a', 'b'}), 'stable')); u9b_chk('unique_undef', @() unique(categorical({'b', '', 'a', ''})));
[u, ia, ic] = unique(c); u9b_chk('unique_ia', @() ia); u9b_chk('unique_ic', @() ic);
u9b_chk('countcats_matrix', @() countcats(categorical({'a', 'b'; 'a', 'a'}))); u9b_chk('countcats_dim', @() countcats(categorical({'a', 'b'; 'a', 'a'}), 2)); u9b_chk('countcats_undef', @() countcats(categorical({'a', ''})));
u9b_chk('histcounts', @() histcounts(c)); [n, cats] = histcounts(c); u9b_chk('histcounts_cats', @() cats); u9b_chk('histcounts_undef', @() histcounts(categorical({'a', ''})));
u9b_chk('nnz', @() nnz(c == 'a')); u9b_chk('sum', @() sum(c == 'a')); u9b_chk('any', @() any(c == 'a')); u9b_chk('find', @() find(c == 'a'));
u9b_chk('numel', @() numel(c)); u9b_chk('length', @() length(c)); u9b_chk('size', @() size(c)); u9b_chk('isempty', @() isempty(c)); u9b_chk('ndims', @() ndims(c)); u9b_chk('isrow', @() isrow(c)); u9b_chk('isvector', @() isvector(c)); u9b_chk('isscalar', @() isscalar(c(1))); u9b_chk('width', @() width(c)); u9b_chk('height', @() height(c));
u9b_chk('ischar', @() ischar(c)); u9b_chk('isstring', @() isstring(c)); u9b_chk('iscell', @() iscell(c)); u9b_chk('iscellstr', @() iscellstr(c)); u9b_chk('isnumeric', @() isnumeric(c)); u9b_chk('isobject', @() isobject(c)); u9b_chk('islogical', @() islogical(c)); u9b_chk('isreal', @() isreal(c)); u9b_chk('isfloat', @() isfloat(c)); u9b_chk('isinteger', @() isinteger(c)); u9b_chk('isenum', @() isenum(c)); u9b_chk('isdiscrete_like_istabular', @() istabular(c));
u9b_chk('plus', @() c + 1); u9b_chk('minus', @() c - c); u9b_chk('times', @() c .* 2); u9b_chk('uminus', @() -c); u9b_chk('not', @() ~c); u9b_chk('and', @() c & c); u9b_chk('colon_num', @() 1:c(1)); u9b_chk('abs', @() abs(c));
u9b_chk('length_cats', @() length(categories(c)));
u9b_chk('mode', @() mode(c)); u9b_chk('mean', @() mean(c)); u9b_chk('median_nonord', @() median(c)); u9b_chk('max_two', @() max(c, c)); u9b_chk('ord_max_two', @() max(o, o(1)));
u9b_chk('lower', @() lower(c)); u9b_chk('upper', @() upper(c)); u9b_chk('strcat', @() strcat(c, 'x')); u9b_chk('regexprep', @() regexprep(c, 'a', 'b')); u9b_chk('strlength', @() strlength(c)); u9b_chk('contains', @() contains(c, 'a')); u9b_chk('startsWith', @() startsWith(c, 'a'));
u9b_chk('strjoin', @() strjoin(c, ',')); u9b_chk('join', @() join(c)); u9b_chk('strsplit', @() strsplit(c(1))); u9b_chk('strtrim', @() strtrim(c)); u9b_chk('regexp', @() regexp(c, 'a'));
u9b_chk('cellfun', @() cellfun(@(x) x, c)); u9b_chk('arrayfun', @() arrayfun(@(x) double(x), c)); u9b_chk('arrayfun_cat', @() arrayfun(@(x) x, c)); u9b_chk('arrayfun_nonuni', @() arrayfun(@(x) char(x), c, 'UniformOutput', false));
u9b_chk('num2cell', @() num2cell(c)); u9b_chk('num2cell_class', @() class(num2cellel(c))); u9b_chk('cell_from_cat', @() cell(c));
u9b_chk('brace', @() bracecat(c));
u9b_chk('dot', @() dotcat(c));
u9b_chk('fieldnames', @() fieldnames(c)); u9b_chkdiv('properties', @() oi_try( @() properties(c)), 'ADR0232'); u9b_chkdiv('methods_count', @() oi_try( @() numel(methods(c))), 'ADR0232'); u9b_chkdiv('methods_list', @() oi_try( @() strjoin(methods(c)', ',')), 'ADR0232');
u9b_chk('struct', @() struct('f', c)); u9b_chk('struct_field_class', @() class(getfield(struct('f', c), 'f')));
u9b_chk('summary_out', @() summary(c));
u9b_chk('summary_out_undef', @() summary(categorical({'a', ''})));
u9b_chk('summary_out_ord', @() summary(o));
u9b_chk('disp_matrix', @() evalc('disp(categorical({''a'', ''b''; ''c'', ''a''}))'));
u9b_chkdiv('display_long', @() oi_try( @() evalc('disp(categorical({''alpha'', ''beta'', ''gamma'', ''delta'', ''epsilon'', ''zeta'', ''eta'', ''theta'', ''iota'', ''kappa'', ''lambda'', ''mu'', ''nu'', ''xi'', ''omicron'', ''pi'', ''rho'', ''sigma'', ''tau'', ''upsilon''}))')), 'ADR0232');
u9b_chk('display_mixed_len', @() evalc('disp(categorical({''a'', ''bbbbbb''; ''ccc'', ''d''}))'));
u9b_chk('mat2str_cat', @() mat2str(c));
u9b_chk('cat_in_if', @() ifcat(c)); u9b_chk('switch_cat', @() switchcat(c)); u9b_chk('logical_cat_direct', @() logicaltest(c)); u9b_chk('while_eq', @() c(1) == 'a' && true);
u9b_chk('fillmissing', @() fillmissing(categorical({'a', ''}), 'constant', 'b')); u9b_chk('rmmissing', @() rmmissing(categorical({'a', ''}))); u9b_chk('standardizeMissing', @() standardizeMissing(c, 'a'));
u9b_chk('issorted', @() issorted(sort(c))); u9b_chk('issorted_unsorted', @() issorted(c));
u9b_chk('save_load', @() saveload(c));
u9b_chk('nnz_cats', @() nnz(countcats(c)));
u9b_chk('accumarray_like_grouping', @() splitapply(@numel, 1:4, findgroups(c)));
[g, gn] = findgroups(c); u9b_chk('findgroups_g', @() g); u9b_chk('findgroups_gn', @() gn); u9b_chk('findgroups_gn_class', @() class(gn));
u9b_chkdiv('grp2idx_absent', @() oi_try( @() exist('grp2idx')), 'ADR0232');
u9b_chk('discretize_cat', @() discretize([1 5 9], [0 4 8 12], 'categorical')); u9b_chk('discretize_cat_names', @() categories(discretize([1 5 9], [0 4 8 12], 'categorical', {'lo', 'mid', 'hi'}))); u9b_chk('discretize_cat_ord', @() isordinal(discretize([1 5 9], [0 4 8 12], 'categorical')));
u9b_chk('cat_from_string_missing', @() categories(categorical(["a", missing, "b"])));
u9b_chk('isequal_string', @() isequal(c, string(c)));
u9b_chk('cat_logical_index_assign_cell', @() assign(c, [true false false false], {'c'}));
u9b_chk('intersect', @() intersect(c, categorical({'a', 'z'}))); u9b_chk('union', @() union(c, categorical({'z'}))); u9b_chk('setdiff', @() setdiff(c, 'a')); u9b_chk('intersect_cell', @() intersect(c, {'a', 'b'}));
u9b_chk('ismember_rows', @() ismember(c', {'a'}));
u9b_chk('cat_colon_syntax', @() c(:)');
u9b_chk('numel_categories_after_delete', @() categories(del(c, [1 3])));
u9b_chk('cat_double_index_assign', @() assignnum(categorical([1 2 3]), 2));
u9b_chk('min_max_undef', @() max(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true)));
u9b_chk('cat_eq_undef_vs_undef_text', @() categorical({''}) == '<undefined>');
u9b_chk('cat_eq_missing', @() categorical({''}) == missing);
u9b_chk('cat_num_eq', @() categorical([1 2]) == '1');
u9b_chk('cat_num_eq_num', @() categorical([1 2]) == 1);
T = table([1; 2; 3], categorical({'x'; 'y'; 'x'}), 'VariableNames', {'n', 'kind'});
u9b_chk('table_var_class', @() class(T.kind));
u9b_chkdiv('table_summary_out', @() oi_try( @() summary(T)), 'ADR0232'); u9b_chk('table_summary_out_kind', @() fieldnames(getfield(summary(T), 'kind')));
u9b_chk('table_rows', @() T(T.kind == 'x', :)); u9b_chk('table_rows_n', @() T.n(T.kind == 'x'));
u9b_chk('table_assign', @() tassign(T, 1, 'y'));
u9b_chk('table_assign_new', @() tassign(T, 1, 'zz')); u9b_chk('table_assign_new_cats', @() categories(tassign(T, 1, 'zz')));
u9b_chk('table_brace', @() tbrace(T));
u9b_chk('table_sortrows', @() sortrows(T, 'kind').kind); u9b_chk('table_sortrows_desc', @() sortrows(T, 'kind', 'descend').kind); u9b_chk('table_sortrows_two', @() sortrows(T, {'kind', 'n'}, {'descend', 'ascend'}).n);
u9b_chk('table_unique', @() unique(T.kind)); u9b_chk('table_groupsummary', @() groupsummary(T, 'kind')); u9b_chk('table_groupsummary_kind_class', @() class(groupsummary(T, 'kind').kind));
u9b_chk('table_groupcounts', @() groupcounts(T, 'kind'));
u9b_chk('splitapply', @() splitapply(@sum, T.n, findgroups(T.kind)));
u9b_chk('table_cat_in_ctor', @() class(table(categorical({'a'})).Var1));
u9b_chk('array2table', @() class(array2table(categorical({'a', 'b'})).Var1));
u9b_chk('table_varfun', @() varfun(@(x) countcats(x), T(:, 'kind')));
u9b_chk('table_rowfun', @() rowfun(@(n, k) k == 'x', T));
u9b_chk('table_writeread', @() tableio(T));
u9b_chk('table_kind_string_write', @() tstrwrite(T));
u9b_chk('table_kind_cell_write', @() tcellwrite(T));
u9b_chk('table_kind_num_write', @() tnumwrite(T));
u9b_chk('table_vartype', @() size(T(:, vartype('categorical'))));
u9b_chk('table_convertvars', @() class(convertvars(T, 'n', 'categorical').n));
u9b_chk('table_convertvars_vals', @() convertvars(T, 'n', 'categorical').n);
u9b_chk('table_ismember', @() ismember(T.kind, 'x'));
u9b_chk('table_cat_eq_in_index', @() T{T.kind == 'y', 'n'});
u9b_chk('table2cell', @() t2c(T));
u9b_chk('table2struct', @() t2s(T));
u9b_chk('T_parens_kind', @() T{:, 'kind'}); u9b_chk('T_parens_kind_class', @() class(T{:, 'kind'}));
u9b_chk('T_kind_1_class', @() class(T{1, 'kind'}));
u9b_chk('T_mixed_brace', @() T{:, :});
u9b_chk('table_join', @() size(innerjoin(T, table(categorical({'x'; 'y'}), [10; 20], 'VariableNames', {'kind', 'w'}))));
u9b_chk('table_join_w', @() innerjoin(T, table(categorical({'x'; 'y'}), [10; 20], 'VariableNames', {'kind', 'w'})).w);
u9b_chk('table_stack', @() class(stack(T, 'n').n));
u9b_chk('timetable_cat', @() class(timetable(seconds([1; 2]), categorical({'a'; 'b'})).Var1));
u9b_chkdiv('table_cat_isundefined', @() oi_try( @() ismissing(table(categorical({'a'; ''})))), 'ADR0232');
u9b_chkdiv('table_fillmissing', @() oi_try( @() fillmissing(table(categorical({'a'; ''})), 'constant', {'z'}).Var1), 'ADR0232');
f = figure('Visible', 'off'); ax = axes(f);
b = bar(ax, categorical({'x', 'y', 'z'}), [1 2 3]); drawnow;
u9b_chk('bar_cat_xdata', @() b.XData); u9b_chk('bar_cat_xdata_class', @() class(b.XData)); u9b_chk('bar_cat_xtick', @() ax.XTick); u9b_chk('bar_cat_xtick_class', @() class(ax.XTick)); u9b_chk('bar_cat_xticklabel', @() ax.XTickLabel); u9b_chk('bar_cat_xlim', @() ax.XLim); u9b_chk('bar_cat_xlim_class', @() class(ax.XLim)); u9b_chkdiv('bar_cat_xruler', @() oi_try( @() class(ax.XAxis)), 'ADR0232'); u9b_chk('bar_cat_cats', @() ax.XAxis.Categories); u9b_chk('bar_cat_xend', @() b.XEndPoints);
u9b_chk('bar_cat_xlim_num', @() ruler2num(ax.XLim, ax.XAxis));
u9b_chk('bar_cat_xlim_set', @() setxlim(ax, categorical({'x', 'z'})));
u9b_chk('bar_cat_xlim_set_num', @() setxlim(ax, [0.5 2.5]));
u9b_chk('bar_cat_xtick_set', @() setxtick(ax, categorical({'x'})));
u9b_chk('bar_cat_xticklabel_set', @() setxticklabel(ax, {'A', 'B', 'C'}));
u9b_chk('bar_cat_text', @() textat(ax, categorical({'y'}), 2));
u9b_chk('bar_cat_hold_plot', @() holdplot(ax, categorical({'x', 'z'}), [5 6]));
u9b_chk('bar_cat_hold_plot_num', @() holdplotnum(ax));
u9b_chk('bar_cat_two', @() size(bar(ax, categorical({'x', 'y'}), [1 2; 3 4])));
u9b_chk('bar_cat_order', @() bar(ax, categorical({'b', 'a'}), [1 2]).XData);
u9b_chk('bar_cat_order_ticks', @() ax.XTickLabel);
u9b_chk('bar_cat_reorder', @() bar(ax, reordercats(categorical({'b', 'a'}), {'b', 'a'}), [1 2]).XData);
u9b_chk('bar_cat_reorder_ticks', @() ax.XTickLabel);
u9b_chk('bar_cat_y', @() class(barh(ax, categorical({'x', 'y'}), [1 2]).YData));
u9b_chkdiv('barh_cat_yruler', @() oi_try( @() class(ax.YAxis)), 'ADR0232');
cla(ax, 'reset');
u9b_chk('bar_cat_undef', @() bar(ax, categorical({'x', ''}), [1 2]).XData);
cla(ax, 'reset');
hp = pie(ax, categorical({'x', 'y', 'x'})); u9b_chk('pie_cat_n', @() numel(hp)); u9b_chk('pie_cat_labels', @() strjoin(arrayfun(@(h) h.String, hp(2:2:end), 'UniformOutput', false), ','));
cla(ax, 'reset'); hh = histogram(ax, categorical({'x', 'y', 'x'})); u9b_chk('hist_cat_values', @() hh.Values); u9b_chk('hist_cat_cats', @() hh.Categories); u9b_chkdiv('hist_cat_class', @() oi_try( @() class(hh)), 'ADR0232'); u9b_chkdiv('hist_cat_xruler', @() oi_try( @() class(ax.XAxis)), 'ADR0232'); u9b_chk('hist_cat_data', @() hh.Data); u9b_chk('hist_cat_numbins', @() hh.NumDisplayBins); u9b_chk('hist_cat_barwidth', @() hh.BarWidth);
cla(ax, 'reset'); sc = scatter(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('scatter_cat_xdata', @() sc.XData); u9b_chk('scatter_cat_class', @() class(sc.XData)); u9b_chkdiv('scatter_cat_ruler', @() oi_try( @() class(ax.XAxis)), 'ADR0232');
cla(ax, 'reset'); pl = plot(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('plot_cat_xdata', @() pl.XData); u9b_chkdiv('plot_cat_ruler', @() oi_try( @() class(ax.XAxis)), 'ADR0232'); u9b_chk('plot_cat_xtick', @() ax.XTick); u9b_chk('plot_cat_xlim', @() ax.XLim);
cla(ax, 'reset'); pl2 = plot(ax, [1 2 3], categorical({'x', 'y', 'x'})); u9b_chk('plot_cat_y', @() pl2.YData); u9b_chkdiv('plot_cat_yruler', @() oi_try( @() class(ax.YAxis)), 'ADR0232'); u9b_chk('plot_cat_ytick', @() ax.YTick);
cla(ax, 'reset'); st = stem(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('stem_cat_class', @() class(st.XData));
cla(ax, 'reset'); ar = area(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('area_cat_class', @() class(ar.XData));
cla(ax, 'reset'); sw = swarmchart(ax, categorical({'x', 'y', 'x'}), [1 2 3]); u9b_chk('swarm_cat_class', @() class(sw.XData));
cla(ax, 'reset'); bx = boxchart(ax, categorical({'x', 'y', 'x'}), [1 2 3]); u9b_chkdiv('boxchart_class', @() oi_try( @() class(bx)), 'ADR0232'); u9b_chk('boxchart_xdata', @() bx.XData);
cla(ax, 'reset'); hm = heatmap(f, table(categorical({'x'; 'y'; 'x'}), categorical({'p'; 'q'; 'p'}), 'VariableNames', {'a', 'b'}), 'a', 'b'); u9b_chk('heatmap_cat', @() hm.XDisplayLabels); u9b_chk('heatmap_color', @() hm.ColorData);
clf(f); ax = axes(f); hold(ax, 'on'); bar(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('hold_plot_other_cats', @() plotother(ax));
clf(f); ax = axes(f); xl = xline(ax, 1); u9b_chkdiv('xline_num', @() oi_try( @() class(xl)), 'ADR0232');
clf(f); ax = axes(f); bar(ax, categorical({'x', 'y'}), [1 2]); u9b_chkdiv('xline_cat', @() oi_try( @() class(xline(ax, categorical({'x'})))), 'ADR0232'); u9b_chk('xline_cat_value', @() class(xline(ax, categorical({'x'})).Value));
u9b_chkdiv('xregion_cat', @() oi_try( @() class(xregion(ax, categorical({'x'}), categorical({'y'})))), 'ADR0232');
clf(f); ax = axes(f); e = errorbar(ax, categorical({'x', 'y'}), [1 2], [0.1 0.1]); u9b_chk('errorbar_cat_class', @() class(e.XData));
close all force
uf = uifigure('Visible', 'off'); ut = uitable(uf, 'Data', T); u9b_chk('uitable_data_class', @() class(ut.Data)); u9b_chk('uitable_kind', @() ut.Data.kind);
u9b_chk('uitable_colformat', @() ut.ColumnFormat); u9b_chk('uitable_coleditable', @() ut.ColumnEditable); u9b_chk('uitable_colname', @() ut.ColumnName);
u9b_chk('uitable_edit', @() utedit(ut, 'y')); u9b_chk('uitable_edit_new', @() utedit(ut, 'zz')); u9b_chk('uitable_edit_new_cats', @() categories(utedit(ut, 'zz')));
u9b_chk('uitable_display_data', @() class(ut.DisplayData)); u9b_chk('uitable_display_kind', @() ut.DisplayData.kind);
ut2 = uitable(uf, 'Data', {1, 'a'}); u9b_chk('uitable_cell_colformat', @() ut2.ColumnFormat);
u9b_chk('uitable_cat_direct', @() oi_try(@() class(uitable(uf, 'Data', categorical({'a'; 'b'})).Data)));
ut4 = uitable(uf, 'Data', T, 'ColumnFormat', {[], {'x', 'y', 'w'}}); u9b_chk('uitable_colformat_set', @() ut4.ColumnFormat);
uf2 = uifigure('Visible', 'off'); dd = uidropdown(uf2, 'Items', categorical({'a', 'b'})); u9b_chk('uidropdown_cat_items', @() class(dd.Items));
lb = uilistbox(uf2, 'Items', {'a', 'b'}); addStyle(lb, uistyle('FontWeight', 'bold'), 'item', 1); S = lb.StyleConfigurations; u9b_chk('style_target', @() S.Target); u9b_chk('style_target_class', @() class(S.Target)); u9b_chk('style_target_cats', @() categories(S.Target)); u9b_chk('style_target_prot', @() isprotected(S.Target)); u9b_chk('style_target_ord', @() isordinal(S.Target));
u9b_chk('style_target_eq', @() S.Target == 'item'); u9b_chk('style_target_str', @() string(S.Target)); u9b_chk('style_target_brace', @() tbrace2(S));
close all force
u9b_chk('cat_in_cellfun_uniform', @() cellfun(@(x) numel(x), {c, o}));
u9b_chk('cat_eq_with_cell_elementwise', @() c == {'a', 'b', 'c', 'd'});
u9b_chk('cat_dims', @() size(categorical({'a', 'b', 'c', 'd', 'e', 'f'}), 2));
u9b_chk('isequal_shape_diff', @() isequal(c, c'));
u9b_chk('cat_count_class', @() class(countcats(c)));
u9b_chk('cat_label_num', @() categorical([1 2], [1 2], {'1', '2'}) == '1');
u9b_chk('ord_compare_to_text_not_cat', @() o < 'zz');
u9b_chk('nonord_compare_text', @() c < 'b');
u9b_chk('cat_plus_string', @() c + "x");
u9b_chk('cat_strcat_string', @() strcat(string(c), "x"));
u9b_chk('cat_disp_in_fprintf', @() sprintf('%s', char(c(1))));
u9b_chk('cat_sprintf_string', @() sprintf('%s', string(c)));
u9b_chk('cat_mat2str_string', @() mat2str(string(c)));
u9b_chk('cat_char_of_matrix', @() size(char(categorical({'a', 'b'; 'c', 'd'}))));
u9b_chk('cat_vertcat_char', @() [c; 'a']);
u9b_chk('cat_repmat_undef', @() repmat(categorical({''}), 1, 2));
u9b_chk('cat_of_cat_ordinal_kept', @() isordinal(categorical(o)));
u9b_chk('cat_of_cat_ordinal_cats', @() categories(categorical(o, {'hi', 'lo'})));
u9b_chk('cat_of_cat_ordinal_off', @() isordinal(categorical(o, 'Ordinal', false)));
u9b_chk('addcats_ordinal', @() categories(addcats(o, 'top')));
u9b_chk('removecats_ordinal', @() removecats(o, 'mid'));
u9b_chk('mergecats_ordinal', @() mergecats(o, {'lo', 'mid'}));
u9b_chk('mergecats_ordinal_nonadjacent', @() mergecats(o, {'lo', 'hi'}));
u9b_chk('countcats_ordinal', @() countcats(o));
u9b_chk('ord_lt_scalar', @() o < o(2));
u9b_chk('ord_sort_undef', @() sort(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true)));
u9b_chk('ord_issorted', @() issorted(o));
u9b_chk('ord_double', @() double(o));
u9b_chk('ord_min_undef', @() min(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true)));
u9b_chk('ord_max_omit', @() max(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true), [], 'omitmissing'));
u9b_chk('ord_max_include', @() max(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true), [], 'includemissing'));
u9b_chk('cat_protected_assign_cat_other', @() assign(categorical({'a'}, {'a', 'b'}, 'Protected', true), 1, categorical({'b'})));
u9b_chk('cat_protected_cat_with_other', @() [categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'})]);
u9b_chk('cat_protected_cat_with_other_cats', @() categories([categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'})]));
u9b_chk('cat_protected_addcats', @() categories(addcats(categorical({'a'}, {'a', 'b'}, 'Protected', true), 'c')));
u9b_chk('cat_protected_removecats', @() categories(removecats(categorical({'a'}, {'a', 'b'}, 'Protected', true), 'b')));
u9b_chkdiv('cat_protected_display', @() oi_try( @() evalc('pp = categorical({''a''}, {''a'', ''b''}, ''Protected'', true)')), 'ADR0232');
u9b_chk('cat_protected_cats_after_cat', @() isprotected([categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'}, {'a', 'b'}, 'Protected', true)]));
u9b_chkdiv('cat_ord_display_ordering', @() oi_try( @() evalc('summary(o)')), 'ADR0232');
u9b_chk('cat_num_input_valueset_missing', @() categorical([1 2 3], [1 2]));
u9b_chk('cat_valueset_undefined_text', @() categorical({'a', ''}, {'a', ''}));
u9b_chk('cat_valueset_undefined_cats', @() categorical({'a'}, {'a', '<undefined>'}));
u9b_chk('cat_string_valueset_missing', @() categorical(["a"], ["a", missing]));
u9b_chk('cat_isundefined_count', @() nnz(isundefined(categorical({'a', '', ''}))));
u9b_chk('cat_pct', @() 100 * countcats(c) / numel(c));


close all force
T = table([1; 2; 3], categorical({'x'; 'y'; 'x'}), 'VariableNames', {'n', 'kind'});
uf = uifigure('Visible', 'off');
ut4 = uitable(uf, 'Data', T, 'ColumnFormat', {[], {'x', 'y', 'w'}}); u9b_chk('uitable_colformat_set_2', @() ut4.ColumnFormat);
ut5 = uitable(uf, 'Data', T); u9b_chk('uitable_display_data_kind_class', @() class(ut5.DisplayData.kind));
ut6 = uitable(uf, 'Data', table(categorical({'a'; 'b'}, {'a', 'b', 'c'}))); u9b_chk('uitable_cats_unused', @() categories(ut6.Data.Var1)); u9b_chk('uitable_cats_colformat', @() ut6.ColumnFormat);
dd = uidropdown(uf, 'Items', categorical({'a', 'b'})); u9b_chk('uidropdown_cat_items_2', @() class(dd.Items));
lb = uilistbox(uf, 'Items', {'a', 'b'}); addStyle(lb, uistyle('FontWeight', 'bold'), 'item', 1); S = lb.StyleConfigurations; u9b_chk('style_target_2', @() S.Target); u9b_chk('style_target_class_2', @() class(S.Target)); u9b_chk('style_target_cats_2', @() categories(S.Target)); u9b_chk('style_target_prot_2', @() isprotected(S.Target)); u9b_chk('style_target_ord_2', @() isordinal(S.Target));
u9b_chk('style_target_eq_2', @() S.Target == 'item'); u9b_chk('style_target_str_2', @() string(S.Target)); u9b_chk('style_target_brace_2', @() tbrace2(S));
tt = uitable(uf, 'Data', magic(3)); addStyle(tt, uistyle('FontWeight', 'bold'), 'row', 1); S2 = tt.StyleConfigurations; u9b_chk('style_table_cats', @() categories(S2.Target)); u9b_chk('style_table_prot', @() isprotected(S2.Target));
tr = uitree(uf); n1 = uitreenode(tr, 'Text', 'a'); addStyle(tr, uistyle('FontWeight', 'bold'), 'node', n1); S3 = tr.StyleConfigurations; u9b_chk('style_tree_cats', @() categories(S3.Target));
dd2 = uidropdown(uf, 'Items', {'a', 'b'}); addStyle(dd2, uistyle('FontWeight', 'bold'), 'item', 1); S4 = dd2.StyleConfigurations; u9b_chk('style_dropdown_cats', @() categories(S4.Target));
lb2 = uilistbox(uf, 'Items', {'a', 'b'}); S5 = lb2.StyleConfigurations; u9b_chk('style_empty_cats', @() categories(S5.Target)); u9b_chk('style_empty_size', @() size(S5.Target)); u9b_chk('style_empty_class', @() class(S5.Target));
close all force
c = categorical({'a', 'b', 'a', 'c'});
o = categorical([1 3 2], [1 2 3], {'lo', 'mid', 'hi'}, 'Ordinal', true);
u9b_chk('cat_in_cellfun_uniform_2', @() cellfun(@(x) numel(x), {c, o}));
u9b_chk('cat_eq_with_cell_elementwise_2', @() c == {'a', 'b', 'c', 'd'});
u9b_chk('cat_dims_2', @() size(categorical({'a', 'b', 'c', 'd', 'e', 'f'}), 2));
u9b_chk('isequal_shape_diff_2', @() isequal(c, c'));
u9b_chk('cat_count_class_2', @() class(countcats(c)));
u9b_chk('cat_label_num_2', @() categorical([1 2], [1 2], {'1', '2'}) == '1');
u9b_chk('ord_compare_to_text_not_cat_2', @() o < 'zz');
u9b_chk('nonord_compare_text_2', @() c < 'b');
u9b_chk('cat_plus_string_2', @() c + "x");
u9b_chk('cat_strcat_string_2', @() strcat(string(c), "x"));
u9b_chk('cat_disp_in_fprintf_2', @() sprintf('%s', char(c(1))));
u9b_chk('cat_sprintf_string_2', @() sprintf('%s', string(c)));
u9b_chk('cat_mat2str_string_2', @() mat2str(string(c)));
u9b_chk('cat_char_of_matrix_2', @() size(char(categorical({'a', 'b'; 'c', 'd'}))));
u9b_chk('cat_char_of_col', @() char(categorical({'a'; 'bb'})));
u9b_chk('cat_char_of_col_size', @() size(char(categorical({'a'; 'bb'}))));
u9b_chk('cat_vertcat_char_2', @() [c; 'a']);
u9b_chk('cat_repmat_undef_2', @() repmat(categorical({''}), 1, 2));
u9b_chk('cat_of_cat_ordinal_kept_2', @() isordinal(categorical(o)));
u9b_chk('cat_of_cat_ordinal_cats_2', @() categories(categorical(o, {'hi', 'lo'})));
u9b_chk('cat_of_cat_ordinal_off_2', @() isordinal(categorical(o, 'Ordinal', false)));
u9b_chk('addcats_ordinal_2', @() categories(addcats(o, 'top')));
u9b_chk('removecats_ordinal_2', @() removecats(o, 'mid'));
u9b_chk('mergecats_ordinal_2', @() mergecats(o, {'lo', 'mid'}));
u9b_chk('mergecats_ordinal_nonadjacent_2', @() mergecats(o, {'lo', 'hi'}));
u9b_chk('countcats_ordinal_2', @() countcats(o));
u9b_chk('ord_lt_scalar_2', @() o < o(2));
u9b_chk('ord_sort_undef_2', @() sort(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true)));
u9b_chk('ord_issorted_2', @() issorted(o));
u9b_chk('ord_double_2', @() double(o));
u9b_chk('ord_min_undef_2', @() min(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true)));
u9b_chk('ord_max_omit_2', @() max(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true), [], 'omitmissing'));
u9b_chk('ord_max_include_2', @() max(categorical([1 NaN 2], [1 2], {'a', 'b'}, 'Ordinal', true), [], 'includemissing'));
u9b_chk('cat_protected_assign_cat_other_2', @() assign(categorical({'a'}, {'a', 'b'}, 'Protected', true), 1, categorical({'b'})));
u9b_chk('cat_protected_cat_with_other_2', @() [categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'})]);
u9b_chk('cat_protected_cat_with_other_cats_2', @() categories([categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'})]));
u9b_chk('cat_protected_addcats_2', @() categories(addcats(categorical({'a'}, {'a', 'b'}, 'Protected', true), 'c')));
u9b_chk('cat_protected_removecats_2', @() categories(removecats(categorical({'a'}, {'a', 'b'}, 'Protected', true), 'b')));
u9b_chk('cat_protected_cats_after_cat_2', @() isprotected([categorical({'a'}, {'a', 'b'}, 'Protected', true) categorical({'b'}, {'a', 'b'}, 'Protected', true)]));
u9b_chk('cat_num_input_valueset_missing_2', @() categorical([1 2 3], [1 2]));
u9b_chk('cat_valueset_undefined_text_2', @() categorical({'a', ''}, {'a', ''}));
u9b_chk('cat_valueset_undefined_cats_2', @() categorical({'a'}, {'a', '<undefined>'}));
u9b_chk('cat_string_valueset_missing_2', @() categorical("a", ["a", missing]));
u9b_chk('cat_isundefined_count_2', @() nnz(isundefined(categorical({'a', '', ''}))));
u9b_chk('cat_pct_2', @() 100 * countcats(c) / numel(c));
u9b_chk('ismember_cats_order', @() ismember({'c', 'a'}, c));
u9b_chk('cat_ordinal_cats_from_num_order', @() categories(categorical([3 1 2], [3 1 2], {'c', 'a', 'b'}, 'Ordinal', true)));
u9b_chk('cat_valueset_sorts', @() categories(categorical({'b', 'a'}, {'b', 'a'})));
u9b_chk('cat_valueset_num_unsorted', @() categories(categorical([2 1], [2 1])));
u9b_chk('cat_valueset_num_names', @() categories(categorical([2 1], [2 1], {'two', 'one'})));
u9b_chk('cat_ctor_string_missing_cats', @() categorical(["a", missing]));
u9b_chk('cat_cat_vert_mixed_cats', @() categories([categorical({'b'}); categorical({'a'})]));
u9b_chk('cat_num_display_names', @() categories(categorical([0.1 0.25 1/3 2/3 1e-5 123456 1234567 0.000123])));
u9b_chk('cat_num_display_names2', @() categories(categorical([1.23456789 100.5 -2 1e10 NaN Inf -Inf])));
u9b_chk('cat_num_display_int', @() categories(categorical(int32([5 -3]))));
u9b_chk('cat_num_display_logical', @() categories(categorical([true])));
u9b_chk('cat_num_display_single', @() categories(categorical(single([0.1 1/3]))));
u9b_chk('cat_num_valueset_mismatch_type', @() categorical({'a'}, [1 2]));
u9b_chk('cat_num_valueset_text', @() categorical([1 2], {'1', '2'}));
u9b_chk('cat_num_valueset_text_val', @() cellstr(categorical([1 2], {'1', '2'})));
u9b_chk('cat_cellnum_valueset', @() categorical({'1', '2'}, [1 2]));
u9b_chk('cat_ctor_sort_numeric_text', @() categories(categorical({'b10', 'b2', 'a'})));
u9b_chk('cat_ctor_4args', @() categorical({'a'}, {'a'}, {'A'}, 'Ordinal', true, 'Protected', false));
u9b_chk('cat_ctor_4args_prot', @() isprotected(categorical({'a'}, {'a'}, {'A'}, 'Ordinal', true, 'Protected', false)));
u9b_chk('cat_ctor_bad_option', @() categorical({'a'}, 'Bogus', true));
u9b_chk('cat_ctor_too_many', @() categorical({'a'}, {'a'}, {'A'}, {'B'}));
u9b_chk('cat_ctor_valueset_cell_num', @() categorical([1 2], {1, 2}));
u9b_chk('cat_ctor_names_dup_case', @() categorical([1 2], [1 2], {'a', 'A'}));
u9b_chk('cat_ctor_names_empty', @() categorical([1 2], [1 2], {'a', ''}));
u9b_chk('cat_ctor_names_undef', @() categorical([1 2], [1 2], {'a', '<undefined>'}));
u9b_chk('cat_ctor_names_ws', @() categories(categorical([1 2], [1 2], {' a ', 'b '})));
u9b_chk('cat_ctor_data_ws', @() categories(categorical({' a ', 'b '})));
u9b_chk('cat_ctor_data_ws_val', @() cellstr(categorical({' a ', 'b '})));
u9b_chk('cat_ctor_empty_valueset', @() categorical({'a'}, {}));
u9b_chk('cat_ctor_empty_valueset_cats', @() categories(categorical({'a'}, {})));
u9b_chk('cat_ctor_empty_data_valueset', @() categories(categorical({}, {'x', 'y'})));
u9b_chk('cat_ctor_string_col', @() categorical(["a"; "b"]));
u9b_chk('cat_ctor_3d', @() size(categorical(ones(2, 2, 2))));
u9b_chkdiv('cat_ctor_3d_disp', @() oi_try( @() evalc('disp(categorical(ones(2, 2, 2)))')), 'ADR0232');
close all force
fprintf('CHK|DISP_ROW|%s|exact\n', strrep(strrep(regexprep(evalc('disp(c)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISP_COL|%s|exact\n', strrep(strrep(regexprep(evalc('disp(c'')'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_ROW|%s|exact\n', strrep(strrep(regexprep(evalc('c'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_COL|%s|exact\n', strrep(strrep(regexprep(evalc('cc = c'''), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_ORD|%s|exact\n', strrep(strrep(regexprep(evalc('o'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_SCALAR|%s|exact\n', strrep(strrep(regexprep(evalc('q = c(1)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY|%s|exact\n', strrep(strrep(regexprep(evalc('e = categorical({})'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY_0x1|%s|exact\n', strrep(strrep(regexprep(evalc('ee = categorical(cell(0, 1))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY_1x0|%s|exact\n', strrep(strrep(regexprep(evalc('ee = categorical(cell(1, 0))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_UNDEF|%s|exact\n', strrep(strrep(regexprep(evalc('u = categorical({''a'', ''''})'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_1x1_UNDEF|%s|exact\n', strrep(strrep(regexprep(evalc('uu = categorical({''''})'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_NUM|%s|exact\n', strrep(strrep(regexprep(evalc('nn = categorical([1 2 3])'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_MATRIX|%s|exact\n', strrep(strrep(regexprep(evalc('mm = categorical({''a'', ''bbbbbb''; ''ccc'', ''d''})'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_3D|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('m3 = categorical(ones(2, 2, 2))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_CATS|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('categories(c)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_IN_CELL|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('cl = {c}'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_IN_CELL_SCALAR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('cl2 = {c(1)}'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_IN_STRUCT|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('st = struct(''f'', c)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_IN_STRUCT_SCALAR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('st2 = struct(''f'', c(1))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|SUMMARY_OUT_ORD|%s|exact\n', strrep(strrep(regexprep(evalc('so = summary(o); disp(so.Min); disp(so.Median); disp(so.Max); disp(class(so.Min))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_FPRINTF|%s|exact\n', strrep(strrep(regexprep(evalc('fprintf(''%s\n'', c)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_FPRINTF_D|%s|exact\n', strrep(strrep(regexprep(evalc('fprintf(''%d\n'', c)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISP_SINGLE|%s|exact\n', strrep(strrep(regexprep(evalc('disp(c(2))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_FORMAT_LONG|%s|exact\n', strrep(strrep(regexprep(evalc('format long; disp(c); format short'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISP_WIDE|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('disp(categorical(repmat({''abcdefghij''}, 1, 12)))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISP_WIDE2|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('disp(categorical(repmat({''abcdefghijklmnopqrstuvwxyz''}, 1, 6)))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISP_MIXED|%s|exact\n', strrep(strrep(regexprep(evalc('disp(categorical({''a'', ''bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'', ''c''; ''dd'', ''e'', ''ffffff''}))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISP_UNDEF_MATRIX|%s|exact\n', strrep(strrep(regexprep(evalc('disp(categorical({''a'', ''''; '''', ''bb''}))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISPLAY_LONGROW|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('lr = categorical(repmat({''abcdefghij''}, 1, 12))'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_DISPLAY_PROT|%s|exact\n', strrep(strrep(regexprep(evalc('pp = categorical({''a''}, {''a'', ''b''}, ''Protected'', true)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_ANS_DISPLAY|%s|exact\n', strrep(strrep(regexprep(evalc('c(2)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|CAT_ANS_DISPLAY2|%s|exact\n', strrep(strrep(regexprep(evalc('categorical({''z''})'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|MISSING_DISPLAY|%s|exact\n', strrep(strrep(regexprep(evalc('md = categorical({''a'', ''''}); md(2)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));



function pr(label, f)
try
    v = f();
    fprintf('%s\t%s\t%s\t%s\n', label, class(v), mat2str(size(v)), s(v));
catch e
    fprintf('%s\tERR\t%s\t%s\n', label, e.identifier, strrep(e.message, newline, ' '));
end
end

function t = s(v)
if iscategorical(v)
    cs = cellstr(v); t = strjoin(reshape(cs, 1, []), ',');
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 10);
elseif ischar(v)
    t = strrep(v, newline, '\n');
elseif isstring(v)
    v(ismissing(v)) = "<missing>"; t = strjoin(reshape(v, 1, []), ',');
elseif iscell(v)
    t = strjoin(cellfun(@(x) s(x), reshape(v, 1, []), 'UniformOutput', false), '|');
elseif isstruct(v)
    t = strjoin(fieldnames(v)', ',');
elseif istable(v)
    t = strjoin(v.Properties.VariableNames, ',');
else
    t = class(v);
end
end

function d = assign(d, k, v)
d(k) = v;
end

function d = del(d, k)
d(k) = [];
end

function d = assignnum(d, k)
d(k) = 1;
end

function d = assignfresh()
clear q;
q(3) = categorical({'a'});
d = q;
end

function x = subsref2d()
m = categorical({'a', 'b'; 'c', 'a'}); x = m(2, 1);
end

function x = subsref2drow()
m = categorical({'a', 'b'; 'c', 'a'}); x = m(2, :);
end

function x = bracecat(c)
x = c{1};
end

function x = dotcat(c)
x = c.a;
end

function x = num2cellel(c)
n = num2cell(c); x = n{1};
end

function x = tbrace(T)
x = T.kind{1};
end

function x = tbrace2(S)
x = S.Target{1};
end

function k = tassign(T, i, v)
T.kind(i) = v; k = T.kind;
end

function x = tstrwrite(T)
T.kind = ["q"; "r"; "s"]; x = class(T.kind);
end

function x = tcellwrite(T)
T.kind = {'q'; 'r'; 's'}; x = class(T.kind);
end

function x = tnumwrite(T)
T.kind(2) = 5; x = T.kind;
end

function x = tableio(T)
p = [tempname '.csv']; writetable(T, p); R = readtable(p); delete(p);
x = sprintf('%s %s', class(R.kind), s(R.kind));
end

function x = ifcat(c)
if c(1)
    x = 'truthy';
else
    x = 'falsy';
end
end

function x = switchcat(c)
switch c(1)
    case 'a'
        x = 'matched a';
    otherwise
        x = 'other';
end
end

function x = logicaltest(c)
x = c == 'a' | c == 'b';
end

function x = saveload(c)
p = [tempname '.mat']; save(p, 'c'); L = load(p); delete(p); x = sprintf('%s %s', class(L.c), s(L.c));
end

function x = utedit(ut, v)
D = ut.Data; D.kind(1) = v; ut.Data = D; x = ut.Data.kind;
end

function x = setxlim(ax, v)
ax.XLim = v; x = ax.XLim;
end

function x = setxtick(ax, v)
ax.XTick = v; x = ax.XTick;
end

function x = setxticklabel(ax, v)
ax.XTickLabel = v; x = ax.XTickLabel;
end

function x = textat(ax, cx, y)
t = text(ax, cx, y, 'lbl'); x = class(t.Position);
end

function x = holdplot(ax, cx, y)
hold(ax, 'on'); p = plot(ax, cx, y); x = sprintf('%s %s', class(p.XData), s(ax.XTickLabel));
end

function x = holdplotnum(ax)
p = plot(ax, [1 2], [3 4]); x = sprintf('%s %s', class(p.XData), s(ax.XTickLabel));
end

function x = plotother(ax)
p = plot(ax, categorical({'q'}), 3); x = s(ax.XTickLabel);
end

function x = t2c(T)
c = table2cell(T); x = class(c{1, 2});
end

function x = t2s(T)
s = table2struct(T); x = class(s(1).kind);
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end
