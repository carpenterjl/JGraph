% table_brace_assign.m -- V6 of the value-ownership plan, second sub-stage: T{rows, vars} = v
% (appendix A #25). The brace form writes the contents of the selected variables, laid side by
% side, through the same get-modify-set rebuild a dot write takes, so the table stays what it is
% and an alias taken before the write keeps what it had. Scalar, row, column, block, logical-mask,
% name and end subscripts; conversion into the variable's class; growth past the last row; the
% refusals' messages.

run_case('b_scalar_alias', @b_scalar_alias);
run_case('b_by_variable_name', @b_by_variable_name);
run_case('b_by_cell_of_names', @b_by_cell_of_names);
run_case('b_row', @b_row);
run_case('b_column', @b_column);
run_case('b_column_scalar_expands', @b_column_scalar_expands);
run_case('b_block', @b_block);
run_case('b_block_scalar_expands', @b_block_scalar_expands);
run_case('b_mask_rows', @b_mask_rows);
run_case('b_mask_vars', @b_mask_vars);
run_case('b_end_both', @b_end_both);
run_case('b_end_arithmetic', @b_end_arithmetic);
run_case('b_index_vector_rows', @b_index_vector_rows);
run_case('b_growth_one_row', @b_growth_one_row);
run_case('b_growth_gap', @b_growth_gap);
run_case('b_growth_end_plus_one_row', @b_growth_end_plus_one_row);
run_case('b_growth_new_variable', @b_growth_new_variable);
run_case('b_text_var_cell_rhs', @b_text_var_cell_rhs);
run_case('b_text_var_char_rhs', @b_text_var_char_rhs);
run_case('b_text_growth_fill', @b_text_growth_fill);
run_case('b_matrix_var_row', @b_matrix_var_row);
run_case('b_convert_into_int8', @b_convert_into_int8);
run_case('b_convert_into_logical', @b_convert_into_logical);
run_case('b_convert_logical_into_double', @b_convert_logical_into_double);
run_case('b_rhs_is_own_column', @b_rhs_is_own_column);
run_case('b_rhs_self_overlap_rows', @b_rhs_self_overlap_rows);
run_case('b_row_by_name', @b_row_by_name);
run_case('b_timetable', @b_timetable);
run_case('b_keeps_properties', @b_keeps_properties);
run_case('b_in_cell_alias', @b_in_cell_alias);
run_case('b_in_struct_field_alias', @b_in_struct_field_alias);
run_case('b_in_function_argument', @b_in_function_argument);
run_case('b_refuse_wrong_count', @b_refuse_wrong_count);
run_case('b_refuse_wrong_count_leaves_table', @b_refuse_wrong_count_leaves_table);
run_case('b_refuse_one_subscript', @b_refuse_one_subscript);
run_case('b_unknown_name_adds_variable', @b_unknown_name_adds_variable);
run_case('b_char_into_number_converts', @b_char_into_number_converts);
run_case('b_refuse_zero_row', @b_refuse_zero_row);
run_case('b_delete_is_refused', @b_delete_is_refused);
run_case('g_brace_read_block', @g_brace_read_block);

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

function T = two()
T = table([1; 2], [3; 4]);
end

function s = both(T)
s = sprintf('%s %s', mat2str(T.Var1'), mat2str(T.Var2'));
end

function s = b_scalar_alias()
T = two(); U = T; U{1, 1} = 9;
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_by_variable_name()
T = two(); U = T; U{2, 'Var2'} = 7;
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_by_cell_of_names()
T = two(); U = T; U{2, {'Var2', 'Var1'}} = [7 8];
s = both(U);
end

function s = b_row()
T = two(); U = T; U{1, :} = [8 9];
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_column()
T = two(); U = T; U{:, 1} = [5; 6];
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_column_scalar_expands()
T = two(); U = T; U{:, 2} = 0;
s = both(U);
end

function s = b_block()
T = two(); U = T; U{1:2, 1:2} = [10 20; 30 40];
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_block_scalar_expands()
T = two(); U = T; U{:, :} = 7;
s = both(U);
end

function s = b_mask_rows()
T = table([1; 2; 3], [4; 5; 6]); U = T;
U{U.Var1 > 1, 1} = 0;
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_mask_vars()
T = two(); U = T; U{1, logical([0 1])} = 9;
s = both(U);
end

function s = b_end_both()
T = two(); U = T; U{end, end} = 100;
s = both(U);
end

function s = b_end_arithmetic()
T = table([1; 2; 3], [4; 5; 6]); U = T;
U{end - 1, end - 1} = 50;
s = both(U);
end

function s = b_index_vector_rows()
T = table([1; 2; 3], [4; 5; 6]); U = T;
U{[3 1], 2} = [60; 40];
s = both(U);
end

function s = b_growth_one_row()
T = two(); U = T; U{3, 1} = 9;
s = sprintf('%d %s / %d', height(U), both(U), height(T));
end

function s = b_growth_gap()
T = two(); U = T; U{5, 2} = 9;
s = sprintf('%d %s', height(U), both(U));
end

function s = b_growth_end_plus_one_row()
T = two(); U = T; U{end + 1, :} = [7 8];
s = sprintf('%d %s', height(U), both(U));
end

function s = b_growth_new_variable()
T = two(); U = T; U{1, 3} = 5;
s = sprintf('%d %s', width(U), strjoin(U.Properties.VariableNames, ','));
end

function s = b_text_var_cell_rhs()
T = table([1; 2], {'a'; 'b'}); U = T;
U{2, 2} = {'z'};
s = sprintf('%s / %s', strjoin(T.Var2', ','), strjoin(U.Var2', ','));
end

function s = b_text_var_char_rhs()
T = table([1; 2], {'a'; 'b'}); U = T;
U{2, 2} = 'z';
s = sprintf('%s %s', class(U.Var2), strjoin(U.Var2', ','));
end

function s = b_text_growth_fill()
T = table([1; 2], {'a'; 'b'}); U = T;
U{4, 1} = 9;
s = sprintf('%d %s %s %d', height(U), class(U.Var2{3}), mat2str(size(U.Var2{3})), U.Var1(3));
end

function s = b_matrix_var_row()
T = table([1 2; 3 4], [5; 6]); U = T;
U{2, 1} = [30 40];
s = sprintf('%s / %s', mat2str(T.Var1), mat2str(U.Var1));
end

function s = b_convert_into_int8()
T = table(int8([1; 2])); U = T;
U{1, 1} = 3.7;
s = sprintf('%s %s', class(U.Var1), mat2str(U.Var1'));
end

function s = b_convert_into_logical()
T = table([true; false]); U = T;
U{2, 1} = 5;
s = sprintf('%s %s', class(U.Var1), mat2str(U.Var1'));
end

function s = b_convert_logical_into_double()
T = two(); U = T;
U{1, 1} = true;
s = sprintf('%s %s', class(U.Var1), mat2str(U.Var1'));
end

function s = b_rhs_is_own_column()
T = two(); U = T;
U{:, 1} = U{:, 2};
s = sprintf('%s / %s', both(T), both(U));
end

function s = b_rhs_self_overlap_rows()
T = table([1; 2; 3], [4; 5; 6]); U = T;
U{[2 3 1], 1} = U{:, 1};
s = both(U);
end

function s = b_row_by_name()
T = table([1; 2], 'RowNames', {'a'; 'b'}); U = T;
U{'b', 1} = 9;
s = sprintf('%s %s %s', mat2str(T.Var1'), mat2str(U.Var1'), strjoin(U.Properties.RowNames', ','));
end

function s = b_timetable()
TT = timetable(seconds([1; 2]), [1; 2]); U = TT;
U{2, 1} = 9;
s = sprintf('%d %s %s %s', istimetable(U), mat2str(seconds(U.Time)'), mat2str(U.Var1'), mat2str(TT.Var1'));
end

function s = b_keeps_properties()
T = two();
T.Properties.Description = 'about';
T.Properties.VariableUnits = {'m', 's'};
T{1, 2} = 0;
s = sprintf('[%s] %s %s', T.Properties.Description, strjoin(T.Properties.VariableUnits, ','), both(T));
end

function s = b_in_cell_alias()
T = two(); c = {T}; d = c;
d{1}{1, 1} = 9;
s = sprintf('%d %d', c{1}.Var1(1), d{1}.Var1(1));
end

function s = b_in_struct_field_alias()
st.t = two(); r = st;
r.t{2, 2} = 0;
s = sprintf('%d %d', st.t.Var2(2), r.t.Var2(2));
end

function T = zero_corner(T)
T{1, 1} = 0;
end

function s = b_in_function_argument()
T = two();
U = zero_corner(T);
s = sprintf('%d %d', T.Var1(1), U.Var1(1));
end

function s = b_refuse_wrong_count()
T = two(); T{1, :} = [1 2 3];
s = both(T);
end

function s = b_refuse_wrong_count_leaves_table()
T = two();
try
    T{1, :} = [1 2 3];
catch
end
s = both(T);
end

function s = b_refuse_one_subscript()
T = two(); T{1} = 5;
s = both(T);
end

function s = b_unknown_name_adds_variable()
T = two(); T{1, 'Nope'} = 5;
s = strjoin(T.Properties.VariableNames, ',');
end

function s = b_char_into_number_converts()
T = two(); T{1, 1} = 'a';
s = sprintf('%s %s', class(T.Var1), mat2str(double(T.Var1')));
end

function s = b_refuse_zero_row()
T = two(); T{0, 1} = 5;
s = both(T);
end

function s = b_delete_is_refused()
T = two(); T{1, :} = [];
s = sprintf('%d', height(T));
end

function s = g_brace_read_block()
T = two();
M = T{:, :};
M(1) = 9;
s = sprintf('%s %s', mat2str(M), both(T));
end
