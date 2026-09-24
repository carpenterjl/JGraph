% sparse_indexed_assign.m -- V6 of the value-ownership plan, third sub-stage: indexed assignment
% into a sparse matrix (appendix A #26). S(i, j) = v, S(k) = v, S(mask) = v, S(i, :) = row and
% deletion rebuild the stored entries: the result stays sparse, a written zero is dropped rather
% than stored, a write past the extent grows the matrix, and an alias taken before the write keeps
% what it had.

run_case('s_scalar_alias', @s_scalar_alias);
run_case('s_scalar_new_entry', @s_scalar_new_entry);
run_case('s_scalar_overwrite_entry', @s_scalar_overwrite_entry);
run_case('s_write_zero_drops_entry', @s_write_zero_drops_entry);
run_case('s_write_zero_where_none', @s_write_zero_where_none);
run_case('s_linear_index', @s_linear_index);
run_case('s_linear_vector', @s_linear_vector);
run_case('s_linear_scalar_expands', @s_linear_scalar_expands);
run_case('s_row_write', @s_row_write);
run_case('s_column_write', @s_column_write);
run_case('s_block_write', @s_block_write);
run_case('s_block_scalar_expands', @s_block_scalar_expands);
run_case('s_colon_all', @s_colon_all);
run_case('s_mask_write', @s_mask_write);
run_case('s_mask_from_comparison', @s_mask_from_comparison);
run_case('s_end_subscripts', @s_end_subscripts);
run_case('s_growth_two_subscripts', @s_growth_two_subscripts);
run_case('s_growth_linear_on_row_vector', @s_growth_linear_on_row_vector);
run_case('s_growth_linear_on_matrix_refused', @s_growth_linear_on_matrix_refused);
run_case('s_rhs_sparse', @s_rhs_sparse);
run_case('s_rhs_dense_matrix', @s_rhs_dense_matrix);
run_case('s_rhs_is_own_rows', @s_rhs_is_own_rows);
run_case('s_rhs_logical', @s_rhs_logical);
run_case('s_rhs_complex', @s_rhs_complex, 'div=ADR0167');
run_case('s_delete_row', @s_delete_row);
run_case('s_delete_column', @s_delete_column);
run_case('s_delete_rows_by_mask', @s_delete_rows_by_mask);
run_case('s_delete_linear_on_vector', @s_delete_linear_on_vector);
run_case('s_delete_alias', @s_delete_alias);
run_case('s_class_and_issparse_after', @s_class_and_issparse_after);
run_case('s_dense_target_sparse_rhs', @s_dense_target_sparse_rhs);
run_case('s_in_cell_alias', @s_in_cell_alias);
run_case('s_in_struct_field_alias', @s_in_struct_field_alias);
run_case('s_in_function_argument', @s_in_function_argument);
run_case('s_refuse_count_mismatch', @s_refuse_count_mismatch);
run_case('s_refuse_count_mismatch_leaves', @s_refuse_count_mismatch_leaves);
run_case('s_refuse_zero_index', @s_refuse_zero_index);
run_case('s_refuse_three_subscripts', @s_refuse_three_subscripts);
run_case('s_refuse_cell_rhs', @s_refuse_cell_rhs);
run_case('s_loop_fill_diagonal', @s_loop_fill_diagonal);

function run_case(name, fn, rule)
% The rule is exact unless a case names its accepted divergence: s_rhs_complex (a sparse matrix
% here holds real values, so a complex value is refused into one; ADR 0167).
if nargin < 3
    rule = 'exact';
end
try
    fprintf('CHK|%s|%s|%s\n', name, clean(fn()), rule);
catch err
    fprintf('CHK|%s|ERR %s|%s\n', name, clean(err.message), rule);
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

function s = show(S)
s = sprintf('%d %d %s', issparse(S), nnz(S), mat2str(full(S)));
end

function S = base()
S = sparse([1 0 2; 0 3 0]);
end

function s = s_scalar_alias()
S = base(); S2 = S; S2(1, 1) = 7;
s = sprintf('%s / %s', show(S), show(S2));
end

function s = s_scalar_new_entry()
S = base(); S(2, 1) = 5;
s = show(S);
end

function s = s_scalar_overwrite_entry()
S = base(); S(2, 2) = -1;
s = show(S);
end

function s = s_write_zero_drops_entry()
S = base(); S(2, 2) = 0;
s = show(S);
end

function s = s_write_zero_where_none()
S = base(); S(2, 1) = 0;
s = show(S);
end

function s = s_linear_index()
S = base(); S(4) = 9;
s = show(S);
end

function s = s_linear_vector()
S = base(); S([2 6]) = [8 9];
s = show(S);
end

function s = s_linear_scalar_expands()
S = base(); S([1 2 3]) = 4;
s = show(S);
end

function s = s_row_write()
S = base(); S(2, :) = [7 0 9];
s = show(S);
end

function s = s_column_write()
S = base(); S(:, 2) = [5; 0];
s = show(S);
end

function s = s_block_write()
S = base(); S(1:2, [1 3]) = [10 20; 30 40];
s = show(S);
end

function s = s_block_scalar_expands()
S = base(); S(1:2, 2:3) = 6;
s = show(S);
end

function s = s_colon_all()
S = base(); S(:) = 1:6;
s = show(S);
end

function s = s_mask_write()
S = base(); S(logical([1 0 0; 0 1 1])) = [7 8 9];
s = show(S);
end

function s = s_mask_from_comparison()
S = base(); S(S > 1) = 0;
s = show(S);
end

function s = s_end_subscripts()
S = base(); S(end, end) = 4; S(end) = 5; S(1, end - 1) = 6;
s = show(S);
end

function s = s_growth_two_subscripts()
S = base(); S(3, 5) = 1;
s = sprintf('%s %s', mat2str(size(S)), show(S));
end

function s = s_growth_linear_on_row_vector()
S = sparse([1 0 2]); S(6) = 3;
s = sprintf('%s %s', mat2str(size(S)), show(S));
end

function s = s_growth_linear_on_matrix_refused()
S = base(); S(9) = 1;
s = show(S);
end

function s = s_rhs_sparse()
S = base(); S(1, :) = sparse([0 4 0]);
s = show(S);
end

function s = s_rhs_dense_matrix()
S = base(); S(:, :) = [0 1 0; 2 0 3];
s = show(S);
end

function s = s_rhs_is_own_rows()
S = base(); S([2 1], :) = S;
s = show(S);
end

function s = s_rhs_logical()
S = base(); S(1, 2) = true;
s = sprintf('%s %s', class(S), show(S));
end

function s = s_rhs_complex()
S = base(); S(1, 2) = 2i;
s = sprintf('%d %d %d %s', issparse(S), isreal(S), nnz(S), mat2str(full(S(1, 2))));
end

function s = s_delete_row()
S = base(); S(1, :) = [];
s = sprintf('%s %s', mat2str(size(S)), show(S));
end

function s = s_delete_column()
S = base(); S(:, 2) = [];
s = sprintf('%s %s', mat2str(size(S)), show(S));
end

function s = s_delete_rows_by_mask()
S = sparse([1 0; 0 2; 3 0]); S(logical([1 0 1]), :) = [];
s = sprintf('%s %s', mat2str(size(S)), show(S));
end

function s = s_delete_linear_on_vector()
S = sparse([1 0 2 0 3]); S([2 3]) = [];
s = sprintf('%s %s', mat2str(size(S)), show(S));
end

function s = s_delete_alias()
S = base(); T = S; T(:, 1) = [];
s = sprintf('%s / %s', show(S), show(T));
end

function s = s_class_and_issparse_after()
S = base(); S(1, 1) = 2.5;
s = sprintf('%s %d %d', class(S), issparse(S), issparse(S(1, :)));
end

function s = s_dense_target_sparse_rhs()
A = zeros(2, 3); A(1, :) = sparse([1 0 2]);
s = sprintf('%d %s', issparse(A), mat2str(A));
end

function s = s_in_cell_alias()
c = {base()}; d = c;
d{1}(1, 1) = 9;
s = sprintf('%d %d %d', full(c{1}(1, 1)), full(d{1}(1, 1)), issparse(d{1}));
end

function s = s_in_struct_field_alias()
st.m = base(); r = st;
r.m(2, 2) = 0;
s = sprintf('%d %d %d', nnz(st.m), nnz(r.m), issparse(r.m));
end

function S = zero_corner(S)
S(1, 1) = 0;
end

function s = s_in_function_argument()
S = base();
T = zero_corner(S);
s = sprintf('%d %d', nnz(S), nnz(T));
end

function s = s_refuse_count_mismatch()
S = base(); S(1, :) = [1 2];
s = show(S);
end

function s = s_refuse_count_mismatch_leaves()
S = base();
try
    S(1, :) = [1 2];
catch
end
s = show(S);
end

function s = s_refuse_zero_index()
S = base(); S(0, 1) = 1;
s = show(S);
end

function s = s_refuse_three_subscripts()
S = base(); S(1, 1, 2) = 1;
s = show(S);
end

function s = s_refuse_cell_rhs()
S = base(); S(1, 1) = {1};
s = show(S);
end

function s = s_loop_fill_diagonal()
S = sparse(4, 4);
for k = 1:4
    S(k, k) = k;
end
s = show(S);
end
