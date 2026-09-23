% table_forms.m -- V6 of the value-ownership plan, table forms (appendix A #117, #121, #122):
% paren row writes, appends and deletions, [T; T] and [T1 T2], rowfun, varfun, addvars,
% table2array, and a string-array variable that stays a string array. Every write is the
% metadata-keeping rebuild, so an alias taken before it keeps what it had.

run_case('pw_row_table', @pw_row_table);
run_case('pw_append_cell', @pw_append_cell);
run_case('pw_delete_row_alias', @pw_delete_row_alias);
run_case('pw_delete_mask', @pw_delete_mask);
run_case('pw_delete_var_pos', @pw_delete_var_pos);
run_case('pw_delete_var_name', @pw_delete_var_name);
run_case('pw_delete_needs_colon', @pw_delete_needs_colon);
run_case('pw_numeric_rhs_refused', @pw_numeric_rhs_refused);
run_case('pw_cell_element', @pw_cell_element);
run_case('pw_cell_scalar_over_vars', @pw_cell_scalar_over_vars);
run_case('pw_table_rhs_two', @pw_table_rhs_two);
run_case('pw_table_rhs_names_ignored', @pw_table_rhs_names_ignored);
run_case('pw_rows_block_table', @pw_rows_block_table);
run_case('pw_cell_column', @pw_cell_column);
run_case('pw_cell_scalar_over_rows', @pw_cell_scalar_over_rows);
run_case('pw_cell_row_over_rows', @pw_cell_row_over_rows);
run_case('pw_cell_column_over_vars', @pw_cell_column_over_vars);
run_case('pw_growth_gap', @pw_growth_gap);
run_case('pw_rownames_write_delete', @pw_rownames_write_delete);
run_case('pw_rhs_subtable_keeps_names', @pw_rhs_subtable_keeps_names);
run_case('pw_rhs_rownames_into_plain', @pw_rhs_rownames_into_plain);
run_case('pw_new_row_by_name', @pw_new_row_by_name);
run_case('pw_tt_delete', @pw_tt_delete);
run_case('pw_tt_append', @pw_tt_append);
run_case('pw_tt_row_write_cell', @pw_tt_row_write_cell);
run_case('pw_tt_row_write_table', @pw_tt_row_write_table);
run_case('pw_new_var_by_name', @pw_new_var_by_name);
run_case('pw_new_var_by_pos', @pw_new_var_by_pos);
run_case('pw_int8_convert', @pw_int8_convert);
run_case('pw_table_width_one_expands', @pw_table_width_one_expands);
run_case('pw_cell_width_mismatch', @pw_cell_width_mismatch);
run_case('pw_table_height_mismatch', @pw_table_height_mismatch);
run_case('pw_table_two_rows_into_one', @pw_table_two_rows_into_one);
run_case('pw_zero_row', @pw_zero_row);
run_case('pw_one_subscript', @pw_one_subscript);
run_case('pw_delete_only_row', @pw_delete_only_row);
run_case('pw_delete_all_rows', @pw_delete_all_rows);
run_case('pw_int8_rhs_into_double', @pw_int8_rhs_into_double);
run_case('pw_mask_write', @pw_mask_write);
run_case('pw_mask_var_delete', @pw_mask_var_delete);
run_case('pw_props_survive_delete', @pw_props_survive_delete);
run_case('pw_string_into_cellstr', @pw_string_into_cellstr);
run_case('pw_matrix_var_delete', @pw_matrix_var_delete);
run_case('pw_matrix_var_write', @pw_matrix_var_write);
run_case('pw_cell_matrix_element', @pw_cell_matrix_element);
run_case('pw_char_into_cellstr', @pw_char_into_cellstr);
run_case('pw_end_plus_one_names', @pw_end_plus_one_names);
run_case('pw_alias_untouched', @pw_alias_untouched);
run_case('pw_in_struct_field', @pw_in_struct_field);
run_case('pw_in_cell', @pw_in_cell);
run_case('pw_growth_int8_logical_fill', @pw_growth_int8_logical_fill);
run_case('pw_delete_rows_by_name', @pw_delete_rows_by_name);
run_case('pw_var_whole_by_name', @pw_var_whole_by_name);
run_case('vc_alias', @vc_alias);
run_case('vc_other_names', @vc_other_names);
run_case('vc_reordered', @vc_reordered);
run_case('vc_counts', @vc_counts);
run_case('vc_rownames_both', @vc_rownames_both);
run_case('vc_rownames_dup', @vc_rownames_dup);
run_case('vc_rownames_one', @vc_rownames_one);
run_case('vc_tt', @vc_tt);
run_case('vc_tt_table', @vc_tt_table);
run_case('vc_empty', @vc_empty);
run_case('vc_cell', @vc_cell);
run_case('vc_number', @vc_number);
run_case('vc_text', @vc_text);
run_case('vc_int8_double', @vc_int8_double);
run_case('vc_num_text', @vc_num_text);
run_case('vc_three', @vc_three);
run_case('vc_first_metadata', @vc_first_metadata);
run_case('vc_then_write_alias', @vc_then_write_alias);
run_case('vc_hc_functions', @vc_hc_functions);
run_case('hc_basic', @hc_basic);
run_case('hc_dup', @hc_dup);
run_case('hc_height', @hc_height);
run_case('hc_rownames_one', @hc_rownames_one);
run_case('hc_rownames_differ', @hc_rownames_differ);
run_case('hc_tt_table', @hc_tt_table);
run_case('hc_cell', @hc_cell);
run_case('hc_matrix_var', @hc_matrix_var);
run_case('sv_string_var', @sv_string_var);
run_case('sv_string_var_paren', @sv_string_var_paren);
run_case('t2a_numbers', @t2a_numbers);
run_case('t2a_text', @t2a_text);
run_case('t2a_mixed', @t2a_mixed);
run_case('t2a_matrix', @t2a_matrix);
run_case('t2a_tt', @t2a_tt);
run_case('t2a_int8_double', @t2a_int8_double);
run_case('t2a_string', @t2a_string);
run_case('t2a_alias', @t2a_alias);
run_case('av_default', @av_default);
run_case('av_named', @av_named);
run_case('av_before_after', @av_before_after);
run_case('av_height', @av_height);
run_case('av_clash', @av_clash);
run_case('av_two', @av_two);
run_case('av_expr', @av_expr);
run_case('av_tt', @av_tt);
run_case('av_leaves_source', @av_leaves_source);
run_case('vf_anon_alias', @vf_anon_alias);
run_case('vf_named', @vf_named);
run_case('vf_uniform', @vf_uniform);
run_case('vf_cell', @vf_cell);
run_case('vf_input', @vf_input);
run_case('vf_drops_rownames', @vf_drops_rownames);
run_case('vf_text', @vf_text);
run_case('vf_tt', @vf_tt);
run_case('rf_default', @rf_default);
run_case('rf_uniform', @rf_uniform);
run_case('rf_names', @rf_names);
run_case('rf_two_outputs', @rf_two_outputs);
run_case('rf_input', @rf_input);
run_case('rf_cell', @rf_cell);
run_case('rf_text_arg', @rf_text_arg);
run_case('rf_extract', @rf_extract);
run_case('rf_separate_false', @rf_separate_false);
run_case('rf_rownames', @rf_rownames);
run_case('rf_order', @rf_order);
run_case('rf_matrix_var', @rf_matrix_var);
run_case('rf_tt', @rf_tt);
run_case('rf_global_operand', @rf_global_operand);

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

% One line for a table, cell, string, char, time or number: the class, the shape and the contents.
function s = sh(v)
if istable(v) || istimetable(v)
    s = sprintf('%s %dx%d {%s}', class(v), height(v), width(v), strjoin(v.Properties.VariableNames, ','));
    for k = 1:width(v)
        s = [s ' ' sh(v.(v.Properties.VariableNames{k}))]; %#ok<AGROW>
    end
    if istimetable(v)
        s = [s ' times=' sh(v.Properties.RowTimes)];
    elseif ~isempty(v.Properties.RowNames)
        s = [s ' rows=' strjoin(v.Properties.RowNames', ',')];
    end
elseif iscell(v)
    parts = cellfun(@sh, v, 'UniformOutput', false);
    s = ['{' strjoin(parts(:)', ',') '}' sprintf('[%dx%d]', size(v, 1), size(v, 2))];
elseif isstring(v)
    s = ['"' strjoin(cellstr(v(:)'), '","') '"' class(v)];
elseif ischar(v)
    s = ['''' v ''''];
elseif isdatetime(v)
    s = ['dt ' strjoin(cellstr(string(v(:)')), ',')];
elseif isduration(v)
    s = ['dur ' mat2str(seconds(v(:)'))];
elseif isstruct(v)
    s = ['struct ' strjoin(fieldnames(v)', ',')];
elseif isnumeric(v) || islogical(v)
    s = [mat2str(v) '/' class(v)];
else
    s = class(v);
end
end

function T = two()
T = table([1; 2], {'a'; 'b'});
end

function TT = tt()
TT = timetable(seconds([1; 2; 3]), [1; 2; 3]);
end

% --- paren row writes, appends and deletions --------------------------------------------------------

function s = pw_row_table()
T = table([1; 2]); T(1, :) = table(9); s = sh(T);
end
function s = pw_append_cell()
T = two(); T(end + 1, :) = {3, 'c'}; s = sh(T);
end
function s = pw_delete_row_alias()
T = table([1; 2; 3]); U = T; U(2, :) = []; s = sprintf('%d %d %s', height(T), height(U), sh(U));
end
function s = pw_delete_mask()
T = two(); T(3, :) = {3, 'c'}; U = T; U(U.Var1 > 1, :) = []; s = sprintf('%s | %s', sh(T), sh(U));
end
function s = pw_delete_var_pos()
T = two(); T(:, 1) = []; s = sh(T);
end
function s = pw_delete_var_name()
T = two(); T(:, 'Var2') = []; s = sh(T);
end
function s = pw_delete_needs_colon()
T = two(); T(1, 1) = []; s = sh(T);
end
function s = pw_numeric_rhs_refused()
T = two(); T(1, :) = 5; s = sh(T);
end
function s = pw_cell_element()
T = two(); T(1, 1) = {5}; s = sh(T);
end
function s = pw_cell_scalar_over_vars()
T = two(); T(1, :) = {5}; s = sh(T);
end
function s = pw_table_rhs_two()
T = two(); T(1, :) = table(9, {'z'}); s = sh(T);
end
function s = pw_table_rhs_names_ignored()
T = two(); T(1, :) = table(9, {'z'}, 'VariableNames', {'p', 'q'}); s = sh(T);
end
function s = pw_rows_block_table()
T = two(); T(1:2, :) = table([7; 8], {'x'; 'y'}); s = sh(T);
end
function s = pw_cell_column()
T = two(); T(1:2, 1) = {7; 8}; s = sh(T);
end
function s = pw_cell_scalar_over_rows()
T = two(); T(1:2, 1) = {7}; s = sh(T);
end
function s = pw_cell_row_over_rows()
T = two(); T(1:2, :) = {7, 'x'}; s = sh(T);
end
function s = pw_cell_column_over_vars()
T = two(); T(1:2, :) = {7; 8}; s = sh(T);
end
function s = pw_growth_gap()
T = two(); T(4, :) = {4, 'd'}; s = sprintf('%s %s', sh(T), class(T.Var2{3}));
end
function s = pw_rownames_write_delete()
T = table([1; 2], 'RowNames', {'a'; 'b'}); T('a', :) = {9}; s1 = sh(T); T('b', :) = []; s = sprintf('%s | %s', s1, sh(T));
end
function s = pw_rhs_subtable_keeps_names()
T = table([1; 2], 'RowNames', {'a'; 'b'}); T(1, :) = T(2, :); s = sh(T);
end
function s = pw_rhs_rownames_into_plain()
T = table([1; 2]); A = table([7; 8], 'RowNames', {'a'; 'b'}); T(1, :) = A(2, :); s = sh(T);
end
function s = pw_new_row_by_name()
T = table([1; 2], 'RowNames', {'a'; 'b'}); T('c', :) = {3}; s = sh(T);
end
function s = pw_tt_delete()
TT = tt(); U = TT; U(2, :) = []; s = sprintf('%s | %s', sh(U), sh(TT));
end
function s = pw_tt_append()
TT = tt(); TT(end + 1, :) = {5}; s = sh(TT);
end
function s = pw_tt_row_write_cell()
TT = tt(); TT(1, :) = {9}; s = sh(TT);
end
function s = pw_tt_row_write_table()
TT = tt(); TT(1, :) = table(9); s = sh(TT);
end
function s = pw_new_var_by_name()
T = two(); T(1, 'New') = {7}; s = sh(T);
end
function s = pw_new_var_by_pos()
T = two(); T(:, 3) = {7; 8}; s = sh(T);
end
function s = pw_int8_convert()
T = table(int8([1; 2])); T(1, 1) = {3.7}; s = sh(T);
end
function s = pw_table_width_one_expands()
T = table([1; 2], [3; 4]); T(1, :) = table(9); s = sh(T);
end
function s = pw_cell_width_mismatch()
T = two(); T(1, :) = {1, 2, 3}; s = sh(T);
end
function s = pw_table_height_mismatch()
T = two(); T(1:2, :) = table(9, {'z'}); s = sh(T);
end
function s = pw_table_two_rows_into_one()
T = two(); T(1, :) = table([9; 9], {'z'; 'z'}); s = sh(T);
end
function s = pw_zero_row()
T = two(); T(0, :) = []; s = sh(T);
end
function s = pw_one_subscript()
T = two(); T(3) = []; s = sh(T);
end
function s = pw_delete_only_row()
T = table(5); T(1, :) = []; s = sh(T);
end
function s = pw_delete_all_rows()
T = two(); T(:, :) = []; s = sh(T);
end
function s = pw_int8_rhs_into_double()
T = table([1; 2]); T(1, :) = table(int8(9)); s = sh(T);
end
function s = pw_mask_write()
T = two(); T(logical([1 0]), :) = {9, 'q'}; s = sh(T);
end
function s = pw_mask_var_delete()
T = two(); T(:, [true false]) = []; s = sh(T);
end
function s = pw_props_survive_delete()
T = two(); T.Properties.Description = 'd'; T.Properties.VariableUnits = {'u', 'v'}; U = T; U(2, :) = [];
s = sprintf('%s %s %s', U.Properties.Description, strjoin(U.Properties.VariableUnits, ','), sh(U));
end
function s = pw_string_into_cellstr()
T = two(); T(1, :) = {5, "s"}; s = sh(T);
end
function s = pw_matrix_var_delete()
T = table([1 2; 3 4], [5; 6]); T(2, :) = []; s = sh(T);
end
function s = pw_matrix_var_write()
T = table([1 2; 3 4], [5; 6]); T(1, :) = table([7 8], 9); s = sh(T);
end
function s = pw_cell_matrix_element()
T = table([1 2; 3 4], [5; 6]); T(1, :) = {[7 8], 9}; s = sh(T);
end
function s = pw_char_into_cellstr()
T = two(); T(1, :) = {5, 'zz'}; s = sh(T);
end
function s = pw_end_plus_one_names()
T = table([1; 2], 'RowNames', {'a'; 'b'}); T(end + 1, :) = {3}; s = sh(T);
end
function s = pw_alias_untouched()
T = two(); U = T; U(1, :) = {9, 'z'}; s = sprintf('%s | %s', sh(T), sh(U));
end
function s = pw_in_struct_field()
st.T = two(); st.T(1, :) = {9, 'z'}; s = sh(st.T);
end
function s = pw_in_cell()
c = {two()}; c{1}(2, :) = []; s = sh(c{1});
end
function s = pw_growth_int8_logical_fill()
T = table(int8([1; 2]), [true; false]); T(4, :) = {4, true}; s = sh(T);
end
function s = pw_delete_rows_by_name()
T = table([1; 2; 3], 'RowNames', {'a'; 'b'; 'c'}); T({'a', 'c'}, :) = []; s = sh(T);
end
function s = pw_var_whole_by_name()
T = two(); T(:, 'Var1') = {9; 8}; s = sh(T);
end

% --- [T; T] and [T1 T2] -------------------------------------------------------------------------------

function s = vc_alias()
T = table([1; 2]); T2 = [T; T]; T2.Var1(1) = 9; s = sprintf('%s | %s', sh(T), sh(T2));
end
function s = vc_other_names()
A = table([1; 2]); B = table([3; 4], 'VariableNames', {'x'}); s = sh([A; B]);
end
function s = vc_reordered()
A = table([1; 2], {'a'; 'b'}, 'VariableNames', {'n', 't'}); B = table({'c'}, 3, 'VariableNames', {'t', 'n'}); s = sh([A; B]);
end
function s = vc_counts()
A = table([1; 2]); B = table([3; 4], [5; 6]); s = sh([A; B]);
end
function s = vc_rownames_both()
A = table([1; 2], 'RowNames', {'a'; 'b'}); B = table([3; 4], 'RowNames', {'c'; 'd'}); s = sh([A; B]);
end
function s = vc_rownames_dup()
A = table([1; 2], 'RowNames', {'a'; 'b'}); C = table([3; 4], 'RowNames', {'a'; 'x'}); s = sh([A; C]);
end
function s = vc_rownames_one()
A = table([1; 2], 'RowNames', {'a'; 'b'}); B = table([3; 4]); s = sprintf('%s | %s', sh([A; B]), sh([B; A]));
end
function s = vc_tt()
TT = timetable(seconds([1; 2]), [1; 2]); s = sh([TT; TT]);
end
function s = vc_tt_table()
TT = timetable(seconds([1; 2]), [1; 2]); T = table([3; 4]); s = sh([TT; T]);
end
function s = vc_empty()
T = table([1; 2]); s = sprintf('%s | %s', sh([T; []]), sh([[]; T]));
end
function s = vc_cell()
T = two(); s = sh([T; {3, 'c'}]);
end
function s = vc_number()
T = table([1; 2]); s = sh([T; 5]);
end
function s = vc_text()
A = table({'a'; 'b'}); s = sh([A; A]);
end
function s = vc_int8_double()
A = table(int8([1; 2])); B = table([300; 4]); s = sh([A; B]);
end
function s = vc_num_text()
A = table([1; 2]); B = table({'a'; 'b'}); s = sh([A; B]);
end
function s = vc_three()
T = table([1; 2]); s = sh([T; T; T]);
end
function s = vc_first_metadata()
T = table([1; 2]); T.Properties.Description = 'd'; T.Properties.VariableUnits = {'u'}; U = table([3; 4]); U.Properties.Description = 'e';
V = [T; U]; s = sprintf('[%s] [%s] %s', V.Properties.Description, strjoin(V.Properties.VariableUnits, ','), sh(V));
end
function s = vc_then_write_alias()
T = two(); U = [T; T]; U.Var2{1} = 'zz'; U(1, :) = []; s = sprintf('%s | %s', sh(T), sh(U));
end
function s = vc_hc_functions()
T = table([1; 2]); U = table({'a'; 'b'}, 'VariableNames', {'t'}); s = sprintf('%s | %s', sh(vertcat(T, T)), sh(horzcat(T, U)));
end
function s = hc_basic()
A = table([1; 2]); B = table({'a'; 'b'}, 'VariableNames', {'t'}); C = [A B]; C.t{1} = 'z'; s = sprintf('%s | %s', sh(B), sh(C));
end
function s = hc_dup()
A = table([1; 2]); B = table([3; 4]); s = sh([A B]);
end
function s = hc_height()
A = table([1; 2]); B = table(3, 'VariableNames', {'t'}); s = sh([A B]);
end
function s = hc_rownames_one()
A = table([1; 2], 'RowNames', {'a'; 'b'}); B = table([3; 4], 'VariableNames', {'y'}); s = sprintf('%s | %s', sh([A B]), sh([B A]));
end
function s = hc_rownames_differ()
A = table([1; 2], 'RowNames', {'a'; 'b'}); B = table([3; 4], 'VariableNames', {'y'}, 'RowNames', {'c'; 'd'}); s = sh([A B]);
end
function s = hc_tt_table()
TT = timetable(seconds([1; 2]), [1; 2]); T = table({'a'; 'b'}, 'VariableNames', {'t'}); s = sh([TT T]);
end
function s = hc_cell()
T = table([1; 2]); s = sh([T {3; 4}]);
end
function s = hc_matrix_var()
A = table([1 2; 3 4]); B = table([5; 6], 'VariableNames', {'v'}); s = sh([A B]);
end

% --- a string-array variable ---------------------------------------------------------------------------

function s = sv_string_var()
T = table(["a"; "b"]); U = [T; T]; V = T; V.Var1(1) = "z"; s = sprintf('%s %s | %s | %s | %s | %s', class(T.Var1), sh(T.Var1(1)), sh(U), sh(V), sh(T{1, 1}), sh(table2array(T)));
end
function s = sv_string_var_paren()
T = table(["a"; "b"]); T(1, :) = {"z"}; U = T; U(2, :) = []; s = sprintf('%s | %s', sh(T), sh(U));
end

% --- table2array, addvars, varfun, rowfun --------------------------------------------------------------

function s = t2a_numbers()
s = sh(table2array(table([1; 2], [3; 4])));
end
function s = t2a_text()
s = sh(table2array(table({'a'; 'b'}, {'c'; 'd'})));
end
function s = t2a_mixed()
s = sh(table2array(table([1; 2], {'a'; 'b'})));
end
function s = t2a_matrix()
s = sh(table2array(table([1 2; 3 4], [5; 6])));
end
function s = t2a_tt()
s = sh(table2array(timetable(seconds([1; 2]), [1; 2], [3; 4])));
end
function s = t2a_int8_double()
s = sprintf('%s | %s', sh(table2array(table(int8([1; 2]), [300; 4]))), sh(table2array(table([true; false], [3; 4]))));
end
function s = t2a_string()
s = sh(table2array(table(["a"; "b"], ["c"; "d"])));
end
function s = t2a_alias()
T = table([1; 2]); A = table2array(T); A(1) = 9; s = sprintf('%s %s', sh(T), sh(A));
end
function s = av_default()
T = table([1; 2]); U = addvars(T, [5; 6]); s = sprintf('%s | %s', sh(T), sh(U));
end
function s = av_named()
T = table([1; 2]); x = [5; 6]; U = addvars(T, x); V = addvars(T, x, 'NewVariableNames', 'w'); s = sprintf('%s | %s', sh(U), sh(V));
end
function s = av_before_after()
T = table([1; 2], [3; 4]); U = addvars(T, [5; 6], 'Before', 'Var2'); V = addvars(T, [5; 6], 'After', 1); W = addvars(T, [5; 6], 'Before', 1);
s = sprintf('%s | %s | %s', sh(U), sh(V), sh(W));
end
function s = av_height()
T = table([1; 2]); U = addvars(T, [5; 6; 7]); s = sh(U);
end
function s = av_clash()
T = table([1; 2]); U = addvars(T, [5; 6], 'NewVariableNames', 'Var1'); s = sh(U);
end
function s = av_two()
T = table([1; 2]); U = addvars(T, [5; 6], {'a'; 'b'}, 'NewVariableNames', {'n', 't'}); s = sh(U);
end
function s = av_expr()
T = table([1; 2]); x = [5; 6]; U = addvars(T, x * 2, x); s = sh(U);
end
function s = av_tt()
TT = timetable(seconds([1; 2]), [1; 2]); U = addvars(TT, [5; 6]); s = sh(U);
end
function s = av_leaves_source()
T = table([1; 2]); x = [5; 6]; U = addvars(T, x); U.x(1) = 9; x(2) = 7; s = sprintf('%s | %s | %s', sh(T), sh(U), sh(x));
end
function s = vf_anon_alias()
T = table([1; 2]); V = varfun(@(x) x, T); V.Fun_Var1(1) = 9; s = sprintf('%s | %s', sh(T), sh(V));
end
function s = vf_named()
T = table([1; 2], [3; 4]); s = sh(varfun(@mean, T));
end
function s = vf_uniform()
T = table([1; 2], [3; 4]); s = sh(varfun(@mean, T, 'OutputFormat', 'uniform'));
end
function s = vf_cell()
T = table([1; 2], [3; 4]); s = sh(varfun(@(x) x * 2, T, 'OutputFormat', 'cell'));
end
function s = vf_input()
T = table([1; 2], [3; 4]); s = sprintf('%s | %s', sh(varfun(@sum, T, 'InputVariables', 'Var2')), sh(varfun(@sum, T, 'InputVariables', {'Var2', 'Var1'})));
end
function s = vf_drops_rownames()
T = table([1; 2], 'RowNames', {'a'; 'b'}); T.Properties.Description = 'd'; V = varfun(@(x) x * 2, T); s = sprintf('%s [%s]', sh(V), V.Properties.Description);
end
function s = vf_text()
T = table({'a'; 'bb'}); s = sprintf('%s | %s', sh(varfun(@numel, T)), sh(varfun(@(c) upper(c), T)));
end
function s = vf_tt()
TT = timetable(seconds([1; 2]), [1; 2]); s = sprintf('%s | %s', sh(varfun(@(x) x * 2, TT)), sh(varfun(@mean, TT)));
end
function s = rf_default()
T = table([1; 2], [3; 4]); s = sh(rowfun(@(a, b) a + b, T));
end
function s = rf_uniform()
T = table([1; 2], [3; 4]); s = sh(rowfun(@(a, b) a + b, T, 'OutputFormat', 'uniform'));
end
function s = rf_names()
T = table([1; 2], [3; 4]); s = sh(rowfun(@(a, b) a + b, T, 'OutputVariableNames', 's'));
end
function s = rf_two_outputs()
T = table([1; 2], [3; 4]); s = sprintf('%s | %s', sh(rowfun(@(a, b) deal(a + b, a * b), T, 'NumOutputs', 2)), sh(rowfun(@(a, b) deal(a + b, a * b), T, 'NumOutputs', 2, 'OutputVariableNames', {'p', 'q'})));
end
function s = rf_input()
T = table([1; 2], [3; 4]); s = sh(rowfun(@(b) b * 10, T, 'InputVariables', 'Var2'));
end
function s = rf_cell()
T = table([1; 2], [3; 4]); s = sh(rowfun(@(a, b) a + b, T, 'OutputFormat', 'cell'));
end
function s = rf_text_arg()
T = table([1; 2], {'a'; 'bb'}); s = sh(rowfun(@(a, t) sprintf('%s:%d', class(t), numel(t)), T, 'OutputFormat', 'cell'));
end
function s = rf_extract()
T = table([1; 2], {'a'; 'bb'}); s = sh(rowfun(@(a, t) sprintf('%s:%d', class(t), numel(t)), T, 'OutputFormat', 'cell', 'ExtractCellContents', true));
end
function s = rf_separate_false()
T = table([1; 2], [3; 4]); s = sh(rowfun(@(r) sum(r), T, 'SeparateInputs', false));
end
function s = rf_rownames()
T = table([1; 2], [3; 4], 'RowNames', {'a'; 'b'}); s = sh(rowfun(@(a, b) a + b, T));
end
function s = rf_order()
global tf_log
tf_log = '';
T = table([1; 2], [3; 4]);
r = rowfun(@logrow, T, 'OutputFormat', 'uniform');
s = sprintf('%s %s', tf_log, mat2str(r'));
end
function y = logrow(a, b)
global tf_log
tf_log = [tf_log sprintf('%d,%d;', a, b)];
y = a + b;
end
function s = rf_matrix_var()
T = table([1 2; 3 4], [5; 6]); s = sh(rowfun(@(m, v) sum(m) + v, T, 'OutputFormat', 'uniform'));
end
function s = rf_tt()
TT = timetable(seconds([1; 2]), [1; 2], [3; 4]); s = sh(rowfun(@(a, b) a + b, TT));
end
function y = bump_tf(x)
global tf_g
tf_g(1) = 7;
y = x;
end
function z = rowfun_zero_tf()
out = rowfun(@bump_tf, table(1), 'OutputFormat', 'uniform');
z = out * 0;
end
function s = rf_global_operand()
global tf_g
tf_g = [1 2];
r = tf_g + rowfun_zero_tf();
s = sprintf('%s %s', mat2str(r), mat2str(tf_g));
end
