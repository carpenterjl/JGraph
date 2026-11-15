% Complex, logical and single sparse matrices, and the verbs ADR 0364 left refused (open item 331,
% ADR 0369). Every row was measured in R2025b 25.2 (probes p6b_a, p6b_b, p6b_d, p6b_e): a subscript
% of one entry is a 1-by-1 sparse matrix, a comparison a logical sparse mask, sparse(single(x))
% single; reductions, .^, comparisons and concatenation of sparse matrices, assignment of complex
% values, \ and the elementwise family answer as R2025b does. Solver and power rows are written to
% twelve digits, where the two engines differ in the last bits.

% --- one entry is a 1-by-1 sparse matrix, and reads as the number it holds ---------------------------
csv_chk('w_sub_one', 'S = sparse([1 0; 0 3]); v = S(1, 1);', 17, 'exact');
csv_chk('w_sub_lin', 'S = sparse([1 0; 0 3]); v = S(4);', 17, 'exact');
csv_chk('w_sub_zero', 'S = sparse([1 0; 0 3]); v = S(2, 1);', 17, 'exact');
csv_chk('w_sub_cplx', 'S = sparse([1+2i 0; 0 3]); v = S(1, 1);', 17, 'exact');
csv_chk('w_sub_cplx_lin', 'S = sparse([1+2i 0; 0 3]); v = S(4);', 17, 'exact');
csv_chk('w_sub_end', 'S = sparse([1 0; 0 3]); v = S(end);', 17, 'exact');
csv_chk('w_sub_end2', 'S = sparse([1 0; 0 3]); v = S(end, end);', 17, 'exact');
csv_chk('w_sub_logical', 'S = sparse([1 0; 0 3]); v = S(logical([1 0 0 0]));', 17, 'exact');
csv_chk('w_sub_three', 'S = sparse([1 0; 0 3]); v = S(1, 1, 1);', 17, 'exact');
csv_chk('w_sub_scalar_sp', 'S = sparse(5); v = S(1);', 17, 'exact');
csv_chk('w_sub_add', 'S = sparse([1 0; 0 3]); v = S(1, 1) + 1;', 17, 'exact');
csv_chk('w_sub_times_dense', 'S = sparse([1 0; 0 3]); v = S(1, 1) * [1 2];', 17, 'exact');
csv_chk('w_sub_if', 'S = sparse([1 0; 0 3]); if S(1,1), v = 1; else v = 0; end', 17, 'exact');
csv_chk('w_sub_for', 'S = sparse([1 0; 0 3]); v = 0; for k = 1:S(2,2), v = v + 1; end', 17, 'exact');
csv_chk('w_sub_colon', 'S = sparse([1 0; 0 3]); v = 1:S(2,2);', 17, 'exact');
csv_chk('w_sub_idx', 'S = sparse([1 0; 0 3]); x = [10 20 30]; v = x(S(2,2));', 17, 'exact');
csv_chk('w_sub_sprintf', 'S = sparse([1 0; 0 3]); v = sprintf(''%g'', S(2,2));', 17, 'div=ADR0369');
csv_chk('w_sub_num2str', 'S = sparse([1 0; 0 3]); v = num2str(S(2,2));', 17, 'exact');
csv_chk('w_sub_zeros', 'S = sparse([1 0; 0 3]); v = zeros(S(2,2), 1);', 17, 'exact');

% --- a sparse argument through the elementwise family, the number readers and the operators ----------
csv_chk('o_sqrt', 'S = sparse([4 0; 0 9]); v = sqrt(S(1,1));', 17, 'exact');
csv_chk('o_exp', 'S = sparse([4 0; 0 9]); v = exp(S(1,1));', 17, 'exact');
csv_chk('o_log', 'S = sparse([4 0; 0 9]); v = log(S(1,1));', 17, 'exact');
csv_chk('o_sin', 'S = sparse([4 0; 0 9]); v = sin(S(1,1));', 17, 'exact');
csv_chk('o_cos', 'S = sparse([4 0; 0 9]); v = cos(S(1,1));', 17, 'exact');
csv_chk('o_floor', 'S = sparse([4 0; 0 9]); v = floor(S(1,1) / 3);', 17, 'exact');
csv_chk('o_round', 'S = sparse([4 0; 0 9]); v = round(S(1,1));', 17, 'exact');
csv_chk('o_abs', 'S = sparse([4 0; 0 9]); v = abs(S(1,1));', 17, 'exact');
csv_chk('o_max2', 'S = sparse([4 0; 0 9]); v = max(S(1,1), 0);', 17, 'exact');
csv_chk('o_min2', 'S = sparse([4 0; 0 9]); v = min(S(1,1), 1);', 17, 'exact');
csv_chk('o_maxrow', 'S = sparse([4 0; 0 9]); v = max([S(1,1) 2]);', 17, 'exact');
csv_chk('o_mod', 'S = sparse([4 0; 0 9]); v = mod(S(1,1), 3);', 17, 'exact');
csv_chk('o_rem', 'S = sparse([4 0; 0 9]); v = rem(S(1,1), 3);', 17, 'exact');
csv_chk('o_mpow', 'S = sparse([4 0; 0 9]); v = S(1,1)^2;', 17, 'exact');
csv_chk('o_times', 'S = sparse([4 0; 0 9]); v = S(1,1)*S(2,2);', 17, 'exact');
csv_chk('o_div', 'S = sparse([4 0; 0 9]); v = S(1,1)/2;', 17, 'exact');
csv_chk('o_rdiv', 'S = sparse([4 0; 0 9]); v = 2/S(1,1);', 17, 'exact');
csv_chk('o_ldiv', 'S = sparse([4 0; 0 9]); v = 2./S(1,1);', 17, 'exact');
csv_chk('o_pow', 'S = sparse([4 0; 0 9]); v = S(1,1).^2;', 17, 'exact');
csv_chk('o_minus', 'S = sparse([4 0; 0 9]); v = S(1,1) - 1;', 17, 'exact');
csv_chk('o_double', 'S = sparse([4 0; 0 9]); v = double(S(1,1));', 17, 'exact');
csv_chk('o_int32', 'S = sparse([4 0; 0 9]); v = int32(S(1,1));', 17, 'exact');
csv_chk('o_logical', 'S = sparse([4 0; 0 9]); v = logical(S(1,1));', 17, 'exact');
csv_chk('o_zeros', 'S = sparse([4 0; 0 9]); v = zeros(S(1,1), 1);', 17, 'exact');
csv_chk('o_ones', 'S = sparse([4 0; 0 9]); v = ones(1, S(1,1));', 17, 'exact');
csv_chk('o_repmat', 'S = sparse([4 0; 0 9]); v = repmat(7, 1, S(1,1));', 17, 'exact');
csv_chk('o_linspace', 'S = sparse([4 0; 0 9]); v = linspace(0, 1, S(1,1));', 17, 'exact');
csv_chk('o_isempty', 'S = sparse([4 0; 0 9]); v = isempty(S(1,1));', 17, 'exact');
csv_chk('o_numel', 'S = sparse([4 0; 0 9]); v = numel(S(1,1));', 17, 'exact');
csv_chk('o_isnan', 'S = sparse([4 0; 0 9]); v = isnan(S(1,1));', 17, 'exact');
csv_chk('o_isfinite', 'S = sparse([4 0; 0 9]); v = isfinite(S(1,1));', 17, 'exact');
csv_chk('o_any', 'S = sparse([4 0; 0 9]); v = any(S(1,1));', 17, 'exact');
csv_chk('o_sum', 'S = sparse([4 0; 0 9]); v = sum(S(1,1));', 17, 'exact');
csv_chk('o_isequal', 'S = sparse([4 0; 0 9]); v = isequal(S(1,1), 4);', 17, 'exact');
csv_chk('o_eq', 'S = sparse([4 0; 0 9]); v = S(1,1) == 4;', 17, 'exact');
csv_chk('o_while', 'S = sparse([4 0; 0 9]); k = S(1,1); n = 0; while k, k = k - 1; n = n + 1; end; v = n;', 17, 'exact');
csv_chk('o_switch', 'S = sparse([4 0; 0 9]); switch S(1,1), case 4, v = 1; otherwise, v = 0; end', 17, 'exact');
csv_chk('o_eye', 'S = sparse([4 0; 0 9]); v = eye(S(1,1) / 2);', 17, 'exact');
csv_chk('o_cell', 'S = sparse([4 0; 0 9]); v = numel(cell(1, S(1,1)));', 17, 'exact');
csv_chk('o_int2str', 'S = sparse([4 0; 0 9]); v = int2str(S(1,1));', 17, 'exact');
csv_chk('o_mat2str', 'S = sparse([4 0; 0 9]); v = mat2str(S(1,1));', 17, 'exact');
csv_chk('o_fprintf', 'S = sparse([4 0; 0 9]); v = evalc(''fprintf(''''%d\n'''', S(1,1))'');', 17, 'div=ADR0369');
csv_chk('o_range', 'S = sparse([4 0; 0 9]); x = 10:10:90; v = x(S(1,1):S(1,1)+1);', 17, 'exact');
csv_chk('o_store', 'S = sparse([4 0; 0 9]); x = zeros(1, 3); x(2) = S(1,1); v = x;', 17, 'exact');
csv_chk('o_store_cell', 'S = sparse([4 0; 0 9]); x = {}; x{1} = S(1,1); v = x{1};', 17, 'exact');
csv_chk('o_accum', 'S = sparse([4 0; 0 9]); t = 0; for k = 1:2, t = t + S(k,k); end; v = t;', 17, 'exact');
csv_chk('o_accum_mul', 'S = sparse([4 0; 0 9]); t = 1; for k = 1:2, t = t * S(k,k); end; v = t;', 17, 'exact');
csv_chk('o_floor_div', 'S = sparse([4 0; 0 9]); v = floor(S(2,2) / S(1,1));', 17, 'exact');
csv_chk('o_hypot', 'S = sparse([4 0; 0 9]); v = hypot(S(1,1), 3);', 17, 'exact');
csv_chk('o_atan2', 'S = sparse([4 0; 0 9]); v = atan2(S(1,1), 1);', 17, 'exact');
csv_chk('o_sign', 'S = sparse([4 0; 0 9]); v = sign(S(1,1));', 17, 'exact');
csv_chk('o_real', 'S = sparse([4 0; 0 9]); v = real(S(1,1));', 17, 'exact');
csv_chk('o_num_col', 'S = sparse([4 0; 0 9]); v = [S(1,1); 2];', 17, 'exact');
csv_chk('o_cumsum', 'S = sparse([4 0; 0 9]); v = cumsum(S);', 17, 'exact');
csv_chk('o_prod', 'S = sparse([4 0; 0 9]); v = prod(S);', 17, 'exact');
csv_chk('o_max', 'S = sparse([4 0; 0 9]); v = max(S);', 17, 'exact');
csv_chk('o_max_idx', 'S = sparse([4 0; 0 9]); [m, i] = max(S); v = i;', 17, 'exact');
csv_chk('o_max_dim', 'S = sparse([4 0; 0 9]); v = max(S, [], 2);', 17, 'exact');
csv_chk('o_min', 'S = sparse([4 0; 0 9]); v = min(S);', 17, 'exact');
csv_chk('o_any_m', 'S = sparse([4 0; 0 9]); v = any(S);', 17, 'exact');
csv_chk('o_all_m', 'S = sparse([4 0; 0 9]); v = all(S);', 17, 'exact');
csv_chk('o_mean', 'S = sparse([4 0; 0 9]); v = mean(S);', 17, 'exact');
csv_chk('o_diag', 'S = sparse([4 0; 0 9]); v = diag(S);', 17, 'exact');
csv_chk('o_trace', 'S = sparse([4 0; 0 9]); v = trace(S);', 17, 'exact');
csv_chk('o_norm', 'S = sparse([4 0; 0 9]); v = norm(S, 1);', 17, 'exact');
csv_chk('o_tril', 'S = sparse([4 0; 0 9]); v = tril(S);', 17, 'exact');
csv_chk('o_reshape', 'S = sparse([4 0; 0 9]); v = reshape(S, 1, 4);', 17, 'exact');
csv_chk('o_kron', 'S = sparse([4 0; 0 9]); v = kron(S, [1 1]);', 17, 'exact');
csv_chk('o_inv', 'S = sparse([4 0; 0 9]); v = inv(S);', 12, 'exact');
csv_chk('o_det', 'S = sparse([4 0; 0 9]); v = det(S);', 12, 'exact');
csv_chk('o_sqrt_m', 'S = sparse([4 0; 0 9]); v = sqrt(S);', 17, 'exact');
csv_chk('o_exp_m', 'S = sparse([4 0; 0 9]); v = exp(S);', 17, 'exact');
csv_chk('o_cos_m', 'S = sparse([4 0; 0 9]); v = cos(S);', 17, 'exact');
csv_chk('o_isnan_m', 'S = sparse([4 0; 0 9]); v = isnan(S);', 17, 'exact');
csv_chk('o_floor_m', 'S = sparse([4 0; 0 9]); v = floor(S / 4);', 17, 'exact');
csv_chk('o_rdivide_ss', 'S = sparse([4 0; 0 9]); v = S ./ S;', 17, 'exact');
csv_chk('o_ldiv_s', 'S = sparse([4 0; 0 9]); v = 2 ./ S;', 17, 'exact');
csv_chk('o_s_div_dense', 'S = sparse([4 0; 0 9]); v = S ./ [1 2; 3 4];', 17, 'exact');
csv_chk('o_s_times_dense', 'S = sparse([4 0; 0 9]); v = S .* [1 2; 3 4];', 17, 'exact');
csv_chk('o_s_minus_ss', 'S = sparse([4 0; 0 9]); v = S - S;', 17, 'exact');
csv_chk('o_s_mpow', 'S = sparse([4 0; 0 9]); v = S^2;', 17, 'exact');
csv_chk('o_s_mpow3', 'S = sparse([4 0; 0 9]); v = S^3;', 17, 'exact');
csv_chk('o_s_mpowh', 'S = sparse([4 0; 0 9]); v = S^0.5;', 12, 'exact');
csv_chk('o_s_mpowneg', 'S = sparse([4 0; 0 9]); v = S^-1;', 12, 'exact');

% --- a comparison is a logical sparse mask -----------------------------------------------------------
csv_chk('w_cmp_gt', 'v = sparse([1 0 2]) > 1;', 17, 'exact');
csv_chk('w_cmp_eq0', 'v = sparse([1 0 2]) == 0;', 17, 'exact');
csv_chk('w_cmp_ne', 'v = sparse([1 0 2]) ~= 0;', 17, 'exact');
csv_chk('w_cmp_lt_dense', 'v = sparse([1 0 2]) < [2 2 2];', 17, 'exact');
csv_chk('w_cmp_cplx', 'v = sparse([1+2i 0 2]) == 2;', 17, 'exact');
csv_chk('w_cmp_cplx_ne', 'v = sparse([1+2i 0 2]) ~= 0;', 17, 'exact');
csv_chk('w_cmp_ss_eq', 'S = sparse([1 0 2]); v = S == S;', 17, 'exact');
csv_chk('w_cmp_ss_lt', 'v = sparse([1 0 2]) < sparse([2 0 1]);', 17, 'exact');
csv_chk('w_cmp_ss_ne', 'v = sparse([1 0 2]) ~= sparse([1 3 2]);', 17, 'exact');
csv_chk('w_cmp_ss_cplx', 'S = sparse([1+2i 0 2]); v = S == S;', 17, 'exact');
csv_chk('w_cmp_ss_cplx_ne', 'S = sparse([1+2i 0 2]); v = S ~= sparse([1 0 2]);', 17, 'exact');
csv_chk('w_cmp_ss_cplx_lt', 'v = sparse([1+2i 0 2]) < sparse([2 0 1]);', 17, 'exact');

csv_chk('w_not', 'v = ~sparse([1 0 2]);', 17, 'exact');

csv_chk('w_and', 'v = sparse([1 0 2]) & sparse([1 1 0]);', 17, 'exact');
csv_chk('w_and_dense', 'v = sparse([1 0 2]) & [1 1 1];', 17, 'exact');

csv_chk('w_or', 'v = sparse([1 0 2]) | sparse([0 0 1]);', 17, 'exact');

csv_chk('w_logical', 'v = logical(sparse([1 0 2]));', 17, 'exact');
csv_chk('w_logical_sp', 'v = sparse([true false true]);', 17, 'exact');

csv_chk('w_mask_idx', 'x = [10 20 30]; v = x(sparse([1 0 2]) > 1);', 17, 'exact');
csv_chk('w_mask_sum', 'v = sum(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_double', 'v = double(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_plus', 'v = (sparse([1 0 2]) > 0) + 1;', 17, 'exact');
csv_chk('w_mask_nnz', 'v = nnz(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_find', 'v = find(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_full', 'v = full(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_islogical', 'v = islogical(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_any', 'v = any(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('w_mask_sub', 'm = sparse([1 0 2]) > 0; v = m(1);', 17, 'exact');
csv_chk('w_mask_whos', 'm = sparse([1 0 2]) > 0; w = whos(''m''); v = w.bytes;', 17, 'exact');

% --- logical sparse matrices -------------------------------------------------------------------------
csv_chk('l_full_class', 'S = sparse([4 0; 0 9]); v = class(full(sparse([1 0]) > 0));', 17, 'exact');
csv_chk('l_minus', 'S = sparse([4 0; 0 9]); v = -(sparse([1 0]) > 0);', 17, 'exact');
csv_chk('l_transpose', 'S = sparse([4 0; 0 9]); v = (sparse([1 0]) > 0)'';', 17, 'exact');
csv_chk('l_sub', 'S = sparse([4 0; 0 9]); m = sparse([1 0 1]) > 0; v = m(2:3);', 17, 'exact');
csv_chk('l_and_scalar', 'S = sparse([4 0; 0 9]); v = sparse([1 0 2]) & 1;', 17, 'exact');
csv_chk('l_or_scalar', 'S = sparse([4 0; 0 9]); v = sparse([1 0 2]) | 0;', 17, 'exact');
csv_chk('l_or_scalar1', 'S = sparse([4 0; 0 9]); v = sparse([1 0 2]) | 1;', 17, 'exact');
csv_chk('l_not_cplx', 'S = sparse([4 0; 0 9]); v = ~sparse([1i 0]);', 17, 'exact');
csv_chk('l_logical_nan', 'S = sparse([4 0; 0 9]); v = logical(sparse([1 NaN]));', 17, 'exact');
csv_chk('l_logical_cplx', 'S = sparse([4 0; 0 9]); v = logical(sparse([1i 0]));', 17, 'exact');
csv_chk('l_single_of', 'S = sparse([4 0; 0 9]); v = single(sparse([1 0 2]));', 17, 'exact');
csv_chk('l_double_of', 'S = sparse([4 0; 0 9]); v = double(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('l_sum', 'S = sparse([4 0; 0 9]); v = sum(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('l_nnz', 'S = sparse([4 0; 0 9]); v = nnz(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('l_find_v', 'S = sparse([4 0; 0 9]); [~, ~, v] = find(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('l_nonzeros', 'S = sparse([4 0; 0 9]); v = nonzeros(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('l_isnumeric', 'S = sparse([4 0; 0 9]); v = isnumeric(sparse([1 0 2]) > 0);', 17, 'exact');
csv_chk('l_isa', 'S = sparse([4 0; 0 9]); v = isa(sparse([1 0 2]) > 0, ''numeric'');', 17, 'exact');
csv_chk('l_isequal', 'S = sparse([4 0; 0 9]); v = isequal(sparse([1 0 2]) > 0, [true false true]);', 17, 'exact');
csv_chk('l_whos2', 'S = sparse([4 0; 0 9]); m = sparse([1 0; 0 1]) > 0; w = whos(''m''); v = w.bytes;', 17, 'exact');
csv_chk('l_assign_cplx', 'S = sparse([4 0; 0 9]); m = sparse([1 0 2]) > 0; m(2) = 1i; v = m;', 17, 'exact');
csv_chk('l_assign_sp', 'S = sparse([4 0; 0 9]); m = sparse([1 0 2]) > 0; m(2) = sparse(3); v = m;', 17, 'exact');
csv_chk('l_cmp_eq', 'S = sparse([4 0; 0 9]); v = (sparse([1 0 2]) > 0) == true;', 17, 'exact');
csv_chk('l_cmp_s', 'S = sparse([4 0; 0 9]); v = sparse([1 0 2]) > sparse(1);', 17, 'exact');
csv_chk('l_cmp_dense_scalar_left', 'S = sparse([4 0; 0 9]); v = 1 < sparse([1 0 2]);', 17, 'exact');
csv_chk('l_cmp_mismatch', 'S = sparse([4 0; 0 9]); v = sparse([1 0 2]) == sparse([1 0]);', 17, 'exact');
csv_chk('l_cmp_expand', 'S = sparse([4 0; 0 9]); v = sparse([1 0 2]) == sparse([1; 0]);', 17, 'exact');
csv_chk('l_cmp_dense_row', 'S = sparse([4 0; 0 9]); v = sparse([1 0; 0 2]) == [1 0];', 17, 'exact');
csv_chk('l_xor', 'S = sparse([4 0; 0 9]); v = xor(sparse([1 0 2]), sparse([1 1 0]));', 17, 'exact');

% --- single sparse matrices, and the classes sparse refuses ------------------------------------------
csv_chk('w_single', 'v = sparse(single([1 0 2]));', 17, 'exact');
csv_chk('w_single_class', 'v = class(sparse(single([1 0 2])));', 17, 'exact');
csv_chk('w_single_cplx', 'v = sparse(single([1+2i 0 2]));', 17, 'exact');
csv_chk('w_single_trip', 'v = sparse([1 2], [1 2], single([1 2]));', 17, 'exact');
csv_chk('w_single_plus', 'S = sparse(single([1 0 2])); v = S + S;', 17, 'exact');
csv_chk('w_single_times', 'S = sparse(single([1 0 2])); v = S * 2;', 17, 'exact');
csv_chk('w_single_full', 'v = full(sparse(single([1 0 2])));', 17, 'exact');

csv_chk('w_int8', 'v = sparse(int8([1 0]));', 17, 'exact');
csv_chk('w_int32_trip', 'v = sparse([1 2], [1 2], int32([1 2]));', 17, 'exact');

csv_chk('w_uint8', 'v = sparse(uint8([1 0]));', 17, 'exact');

csv_chk('w_char', 'v = sparse(''ab'');', 17, 'exact');

csv_chk('s_whos', 'S = sparse([4 0; 0 9]); S1 = sparse(single([1 0 2])); w = whos(''S1''); v = w.bytes;', 17, 'exact');
csv_chk('s_whos_c', 'S = sparse([4 0; 0 9]); S1 = sparse(single([1i 0 2])); w = whos(''S1''); v = w.bytes;', 17, 'exact');
csv_chk('s_mix_double', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0 2])) + sparse([1 1 1]);', 17, 'exact');
csv_chk('s_dense_double', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0 2])) * 2.5;', 17, 'exact');
csv_chk('s_times_dense', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0 2])) * [1; 1; 1];', 17, 'exact');
csv_chk('s_sub', 'S = sparse([4 0; 0 9]); S1 = sparse(single([1 0 2])); v = S1(3);', 17, 'exact');
csv_chk('s_transpose', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0 2]))'';', 17, 'exact');
csv_chk('s_neg', 'S = sparse([4 0; 0 9]); v = -sparse(single([1 0 2]));', 17, 'exact');
csv_chk('s_sum', 'S = sparse([4 0; 0 9]); v = sum(sparse(single([1 0 2])));', 17, 'exact');
csv_chk('s_assign', 'S = sparse([4 0; 0 9]); S1 = sparse(single([1 0 2])); S1(2) = 1/3; v = double(S1(2));', 17, 'exact');
csv_chk('s_cmp', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0 2])) > 1;', 17, 'exact');
csv_chk('s_eps', 'S = sparse([4 0; 0 9]); v = full(sparse(single(1/3)));', 17, 'exact');
csv_chk('s_cat_dd', 'S = sparse([4 0; 0 9]); v = [sparse(single([1 0])) [2 3]];', 17, 'exact');
csv_chk('s_mtimes', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0; 0 2])) * sparse(single([1 0; 0 2]));', 17, 'exact');
csv_chk('s_complex', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0])) * 1i;', 17, 'exact');
csv_chk('s_logical_single', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0])) + (sparse([1 0]) > 0);', 17, 'exact');
csv_chk('s_single_int', 'S = sparse([4 0; 0 9]); v = sparse(single([1 0])) + int8(1);', 17, 'exact');

% --- reductions --------------------------------------------------------------------------------------
csv_chk('r_sum', 'v = sum(sparse([1 0; 0 3]));', 17, 'exact');
csv_chk('r_sum2', 'v = sum(sparse([1 0; 0 3]), 2);', 17, 'exact');
csv_chk('r_sum_all', 'v = sum(sparse([1 0; 0 3]), ''all'');', 17, 'exact');
csv_chk('r_csum', 'v = sum(sparse([1+2i 0; 0 3]));', 17, 'exact');
csv_chk('r_csum2', 'v = sum(sparse([1+2i 0; 0 3]), 2);', 17, 'exact');
csv_chk('r_csum_all', 'v = sum(sparse([1+2i 0; 0 3]), ''all'');', 17, 'exact');
csv_chk('r_csum_cancel', 'v = sum(sparse([1i 0; -1i 3]));', 17, 'exact');
csv_chk('r_cprod', 'v = prod(sparse([1+2i 1; 2 3]));', 17, 'exact');
csv_chk('r_cmax', 'v = max(sparse([1+2i 0; 0 3]));', 17, 'exact');
csv_chk('r_cmin', 'v = min(sparse([1+2i 0; 0 3]));', 17, 'exact');
csv_chk('r_cmean', 'v = mean(sparse([1+2i 0; 0 3]));', 17, 'exact');
csv_chk('r_cany', 'v = any(sparse([1+2i 0; 0 0]));', 17, 'exact');
csv_chk('r_call', 'v = all(sparse([1+2i 0; 2 3]));', 17, 'exact');
csv_chk('r_ccumsum', 'v = cumsum(sparse([1+2i 0; 0 3]));', 17, 'exact');
csv_chk('r_cnorm', 'v = norm(sparse([3+4i 0; 0 0]), 1);', 17, 'exact');
csv_chk('r_cnormfro', 'v = norm(sparse([3+4i 0; 0 0]), ''fro'');', 17, 'exact');
csv_chk('r_cdiag', 'v = diag(sparse([1+2i 0; 0 3]));', 17, 'exact');
csv_chk('r_ctrace', 'v = trace(sparse([1+2i 0; 0 3]));', 17, 'exact');

% --- elementwise power -------------------------------------------------------------------------------
csv_chk('p_cpow', 'v = sparse([1+2i 0; 0 3]).^2;', 12, 'exact');
csv_chk('p_cpow0', 'v = sparse([1+2i 0; 0 3]).^0;', 12, 'exact');
csv_chk('p_cpowh', 'v = sparse([1+2i 0; 0 4]).^0.5;', 12, 'exact');
csv_chk('p_rpow', 'v = sparse([1 0; 0 3]).^2;', 12, 'exact');
csv_chk('p_rpowneg', 'v = sparse([1 0; 0 3]).^-1;', 12, 'exact');
csv_chk('p_rpowh', 'v = sparse([-1 0; 0 4]).^0.5;', 12, 'exact');
csv_chk('p_cpowss', 'S = sparse([1+2i 0; 0 3]); v = S.^S;', 12, 'exact');
csv_chk('p_scalar_pow', 'v = 2.^sparse([1+2i 0; 0 3]);', 12, 'exact');
csv_chk('p_mpow', 'v = sparse([1+2i 0; 0 3])^2;', 12, 'exact');

% --- concatenation -----------------------------------------------------------------------------------
csv_chk('j_rr', 'S = sparse([1 0; 0 3]); v = [S S];', 17, 'exact');
csv_chk('j_rr_v', 'S = sparse([1 0; 0 3]); v = [S; S];', 17, 'exact');
csv_chk('j_rd', 'S = sparse([1 0; 0 3]); v = [S [5; 6]];', 17, 'exact');
csv_chk('j_dr', 'S = sparse([1 0; 0 3]); v = [[5 6]; S];', 17, 'exact');
csv_chk('j_cc', 'S = sparse([1+2i 0; 0 3]); v = [S S];', 17, 'exact');
csv_chk('j_cr', 'S = sparse([1+2i 0; 0 3]); v = [S sparse([1 0; 0 3])];', 17, 'exact');
csv_chk('j_cd', 'S = sparse([1+2i 0; 0 3]); v = [S; 7 8];', 17, 'exact');
csv_chk('j_rcd', 'S = sparse([1 0; 0 3]); v = [S [1i; 0]];', 17, 'exact');
csv_chk('j_horzcat', 'S = sparse([1 0; 0 3]); v = horzcat(S, S);', 17, 'exact');
csv_chk('j_cat3', 'S = sparse([1 0; 0 3]); v = cat(3, S, S);', 17, 'exact');
csv_chk('j_empty', 'S = sparse([1 0; 0 3]); v = [S []];', 17, 'exact');
csv_chk('j_logical', 'v = [sparse([1 0]) > 0, true];', 17, 'exact');
csv_chk('j_sp_single', 'v = [sparse([1 0]) single(2)];', 17, 'exact');
csv_chk('j_sp_int', 'v = [sparse([1 0]) int8(2)];', 17, 'exact');
csv_chk('j_sp_char', 'v = [sparse([1 0]) ''a''];', 17, 'exact');
csv_chk('j_mismatch', 'v = [sparse([1 0]) [1; 2]];', 17, 'exact');
csv_chk('j_repmat', 'v = repmat(sparse([1+2i 0]), 2, 1);', 17, 'exact');

% --- indexed assignment ------------------------------------------------------------------------------
csv_chk('a_rr', 'S = sparse([1 0; 0 3]); S(1, 2) = 5; v = S;', 17, 'exact');
csv_chk('a_rc', 'S = sparse([1 0; 0 3]); S(1, 2) = 2i; v = S;', 17, 'exact');
csv_chk('a_rc_lin', 'S = sparse([1 0; 0 3]); S(2) = 2i; v = S;', 17, 'exact');
csv_chk('a_cr', 'S = sparse([1+2i 0; 0 3]); S(1, 2) = 5; v = S;', 17, 'exact');
csv_chk('a_cc', 'S = sparse([1+2i 0; 0 3]); S(2, 1) = 1-1i; v = S;', 17, 'exact');
csv_chk('a_c_zero', 'S = sparse([1+2i 0; 0 3]); S(1, 1) = 0; v = S;', 17, 'exact');
csv_chk('a_c_real', 'S = sparse([1+2i 0; 0 3]); S(1, 1) = 4; v = S;', 17, 'exact');
csv_chk('a_c_grow', 'S = sparse([1+2i 0; 0 3]); S(3, 3) = 1i; v = S;', 17, 'exact');
csv_chk('a_c_row', 'S = sparse([1+2i 0; 0 3]); S(2, :) = [1i 2]; v = S;', 17, 'exact');
csv_chk('a_c_delete', 'S = sparse([1+2i 0; 0 3]); S(1, :) = []; v = S;', 17, 'exact');
csv_chk('a_c_sp_rhs', 'S = sparse([1+2i 0; 0 3]); S(:, 1) = sparse([0; 1i]); v = S;', 17, 'exact');
csv_chk('a_c_all', 'S = sparse([1+2i 0; 0 3]); S(:) = 1; v = S;', 17, 'exact');
csv_chk('a_r_cplx_row', 'S = sparse([1 0; 0 3]); S(1, :) = [1i 0]; v = S;', 17, 'exact');
csv_chk('a_r_cplx_flag', 'S = sparse([1 0; 0 3]); S(1, 1) = complex(2, 0); v = S;', 17, 'exact');
csv_chk('a_rlog', 'm = sparse([1 0 2]) > 0; m(2) = true; v = m;', 17, 'exact');
csv_chk('a_rlog_num', 'm = sparse([1 0 2]) > 0; m(2) = 5; v = m;', 17, 'exact');
csv_chk('a_rlog_nan', 'm = sparse([1 0 2]) > 0; m(2) = NaN; v = m;', 17, 'exact');

% --- solving and the orderings -----------------------------------------------------------------------
csv_chk('b_csolve', 'A = sparse([2 1i; 0 3]); v = A \ [1; 2];', 12, 'exact');
csv_chk('b_csolve_sp', 'A = sparse([2 1i; 0 3]); v = A \ sparse([1; 2]);', 12, 'exact');
csv_chk('b_rsolve_c', 'A = sparse([2 1; 0 3]); v = A \ [1i; 2];', 12, 'exact');
csv_chk('b_csolve_full', 'A = sparse([4 1+1i 0; 1-1i 4 1; 0 1 4]); v = A \ [1; 2; 3];', 12, 'exact');
csv_chk('b_cslash', 'A = sparse([2 1i; 0 3]); v = [1 2] / A;', 12, 'exact');
csv_chk('b_cinv', 'v = inv(sparse([2 1i; 0 3]));', 12, 'exact');
csv_chk('b_cdet', 'v = det(sparse([2 1i; 0 3]));', 12, 'exact');
csv_chk('b_csymrcm', 'v = symrcm(sparse([2 1i; -1i 3]));', 17, 'exact');

csv_chk('m_v73_mask', 'S = sparse([1 0 2]) > 0; f = [tempname ''.mat'']; save(f, ''S''); clear S; load(f); delete(f); v = S;', 17, 'exact');
csv_chk('m_v73_single', 'S = sparse(single([1 0 2])); f = [tempname ''.mat'']; save(f, ''S''); clear S; load(f); delete(f); v = S;', 17, 'exact');

% --- the rest ----------------------------------------------------------------------------------------
csv_chk('x_spfun', 'v = spfun(@exp, sparse([1i 0]));', 17, 'exact');
csv_chk('x_spones', 'v = spones(sparse([1i 0]));', 17, 'exact');
csv_chk('x_tril', 'v = tril(sparse([1 1i; 2 3]));', 17, 'exact');
csv_chk('x_triu', 'v = triu(sparse([1 1i; 2 3]));', 17, 'exact');
csv_chk('x_reshape', 'v = reshape(sparse([1 1i; 2 3]), 1, 4);', 17, 'exact');
csv_chk('x_isequal_ss', 'v = isequal(sparse([1 0]) > 0, sparse([1 0]) ~= 0);', 17, 'exact');

csv_chk('d_logical_dense', 'S = sparse([4 0; 0 9]); v = sparse([1 0]) + true;', 17, 'exact');
csv_chk('d_int', 'S = sparse([4 0; 0 9]); v = sparse([1 0]) + int8(1);', 17, 'exact');
csv_chk('d_int_times', 'S = sparse([4 0; 0 9]); v = sparse([1 0]) * int8(2);', 17, 'exact');

% --- more of each ------------------------------------------------------------------------------------
csv_chk('e_tan', 'v = tan(sparse([1 0; 0 2]));', 17, 'exact');
csv_chk('e_ceil', 'v = ceil(sparse([1.5 0; 0 -2.5]));', 17, 'exact');
csv_chk('e_fix', 'v = fix(sparse([1.5 0; 0 -2.5]));', 17, 'exact');
csv_chk('e_log2', 'v = log2(sparse([4 0; 0 8]));', 17, 'exact');
csv_chk('e_isinf', 'v = isinf(sparse([Inf 0; 0 8]));', 17, 'exact');
csv_chk('e_round2', 'v = round(sparse([1.25 0; 0 8]), 1);', 17, 'exact');
csv_chk('e_sqrt_neg', 'v = sqrt(sparse([-4 0; 0 9]));', 17, 'exact');
csv_chk('e_sqrt_c', 'v = sqrt(sparse([-4+0i 0; 0 9i]));', 12, 'exact');
csv_chk('e_exp_c', 'v = exp(sparse([1i 0]));', 17, 'exact');
csv_chk('e_floor_l', 'v = floor(sparse([1 0]) > 0);', 17, 'exact');
csv_chk('e_sqrt_single', 'v = sqrt(sparse(single([4 0])));', 17, 'exact');
csv_chk('e_mod_m', 'v = mod(sparse([4 0; 0 9]), 2);', 17, 'exact');
csv_chk('e_max_s', 'v = max(sparse([4 0; 0 9]), 5);', 17, 'exact');
csv_chk('e_max_l', 'v = max(sparse([1 0]) > 0);', 17, 'exact');
csv_chk('e_tril_c', 'v = tril(sparse([1 1i; 2i 3]));', 17, 'exact');
csv_chk('e_if_zero', 'if sparse(0), v = 1; else v = 0; end', 17, 'exact');
csv_chk('e_if_full', 'if sparse([1 2]), v = 1; else v = 0; end', 17, 'exact');
csv_chk('e_if_part', 'if sparse([1 0]), v = 1; else v = 0; end', 17, 'exact');
csv_chk('e_mat2str', 'v = mat2str(sparse([1 0; 0 3]));', 17, 'exact');
csv_chk('e_mat2str_one', 'v = mat2str(sparse(4));', 17, 'exact');
csv_chk('e_num2str', 'v = num2str(sparse(4));', 17, 'exact');
csv_chk('e_ss_div1', 'S = sparse([4 0; 0 9]); v = S / S(1,1);', 17, 'exact');
csv_chk('e_s_mul1', 'S = sparse([4 0; 0 9]); v = S(1,1) * S;', 17, 'exact');
csv_chk('e_s_plus1', 'S = sparse([4 0; 0 9]); v = S + S(1,1);', 17, 'exact');
csv_chk('e_s_minus_dense', 'v = sparse([4 0; 0 9]) - [1 1; 1 1];', 17, 'exact');
csv_chk('e_s_eq_dense', 'v = sparse([4 0; 0 9]) == [4 1; 0 0];', 17, 'exact');
csv_chk('e_c_eq_dense', 'v = sparse([4i 0; 0 9]) == [4i 1; 0 0];', 17, 'exact');
csv_chk('e_c_gt', 'v = sparse([4i 0; 0 9]) > 0;', 17, 'exact');
csv_chk('e_c_mean2', 'v = mean(sparse([1i 0; 2 3]), 2);', 17, 'exact');
csv_chk('e_c_prod_all', 'v = prod(sparse([1i 2; 2 3]), ''all'');', 17, 'exact');
csv_chk('e_c_max2', '[m, i] = max(sparse([1i 0; 2 3])); v = i;', 17, 'exact');
csv_chk('e_c_min_dim', 'v = min(sparse([1i 0; 2 3]), [], 2);', 17, 'exact');
csv_chk('e_c_cumprod', 'v = cumprod(sparse([1i 1; 2 3]));', 17, 'exact');
csv_chk('e_c_sum_neg', 'v = sum(sparse([1i 0; 2 3]), 2);', 17, 'exact');
csv_chk('e_r_cumprod', 'v = cumprod(sparse([1 1; 2 3]));', 17, 'exact');
csv_chk('e_r_mean', 'v = mean(sparse([1 0; 2 3]));', 17, 'exact');
csv_chk('e_ord_symamd', 'v = symamd(sparse([2 1i; -1i 3]));', 17, 'exact');
csv_chk('e_ord_dmperm', 'v = dmperm(sparse([0 1i; 1 0]));', 17, 'exact');
csv_chk('e_c_det', 'v = det(sparse([2 1i; 1 3]));', 12, 'exact');
csv_chk('e_c_norm2', 'v = norm(sparse([3 4i]));', 17, 'exact');
csv_chk('e_cs_mtimes1', 'S = sparse([1i 0; 0 2]); v = S(1,1) * [1 2];', 17, 'exact');
csv_chk('e_cs_scal', 'S = sparse([1i 0; 0 2]); v = S(1,1);', 17, 'exact');
csv_chk('e_ldiv_sparse_1', 'v = sparse(2) \ sparse([2 4]);', 17, 'exact');
csv_chk('e_assign_grow_c', 'S = sparse([1 0]); S(4) = 1i; v = S;', 17, 'exact');
csv_chk('e_assign_mask', 'S = sparse([1 0 2]); S(S > 1) = 7; v = S;', 17, 'exact');
csv_chk('e_assign_mask_c', 'S = sparse([1 0 2i]); S(abs(S) > 1) = 7; v = S;', 17, 'exact');
csv_chk('e_read_mask', 'S = sparse([1 0 2]); v = S(S > 0);', 17, 'exact');
csv_chk('e_dense_mask_idx', 'x = [5 6 7]; x(sparse([1 0 2]) > 0) = 0; v = x;', 17, 'exact');
csv_chk('e_cat_mixed', 'v = [sparse([1 0]); 1i 2];', 17, 'exact');
csv_chk('e_cat_three', 'v = [sparse(1) 2 sparse(3)];', 17, 'exact');
csv_chk('e_cat_empty_col', 'v = [sparse(zeros(2, 0)) sparse([1; 2])];', 17, 'exact');
csv_chk('e_cat_bad_row', 'v = [sparse([1 0]); [1 2 3]];', 17, 'exact');
csv_chk('e_vertcat', 'v = vertcat(sparse([1i 0]), [2 3]);', 17, 'exact');
csv_chk('e_cat2', 'v = cat(2, sparse([1 0]), 3);', 17, 'exact');

csv_chk('z_plus0', 'v = sparse([1 0 2]) + 0;', 17, 'exact');
csv_chk('z_minus0', 'v = sparse([1 0 2]) - 0;', 17, 'exact');
csv_chk('z_0minus', 'v = 0 - sparse([1 0 2]);', 17, 'exact');
csv_chk('z_plus0_c', 'v = sparse([1i 0 2]) + 0;', 17, 'exact');
csv_chk('z_plus_false', 'v = sparse([1 0 2]) + false;', 17, 'exact');
csv_chk('z_find_col', 'v = find(sparse([1; 0; 2]));', 17, 'exact');
csv_chk('z_find_mat', 'v = find(sparse([1 0; 0 2]));', 17, 'exact');
csv_chk('z_find_empty_row', 'v = find(sparse([0 0]));', 17, 'exact');
csv_chk('z_s_times_s1', 'v = sparse([1 0; 0 2]) * sparse(3);', 17, 'exact');
csv_chk('z_mpower_c', 'v = sparse([1i 0; 0 2])^2;', 17, 'exact');
csv_chk('z_cmp_single', 'v = class(sparse(single([1 0])) > 0);', 17, 'exact');
csv_chk('z_not_l', 'v = ~(sparse([1 0]) > 0);', 17, 'exact');
csv_chk('z_uminus_l', 'v = class(-(sparse([1 0]) > 0));', 17, 'exact');
csv_chk('z_cat_logical_single', 'v = [sparse(single(1)) true];', 17, 'exact');
csv_chk('z_whos_single_c', 'S = sparse(single([1i 0])); w = whos(''S''); v = w.bytes;', 17, 'exact');

function csv_chk(name, code, digits, rule)
% One measured operation as one line: the class, size, isreal and issparse of the answer and its
% real and imaginary parts (full), a char answer as its text, or the refusal's identifier and message.
try
    eval(code);
    if ischar(v)
        fprintf('CHK|%s|char %s|%s\n', name, strrep(v, newline, '\n'), rule);
    else
        x = full(v);
        x = double(x(:)).';
        fprintf('CHK|%s|%s %s real=%d sparse=%d re=%s im=%s|%s\n', name, class(v), mat2str(size(v)), ...
            isreal(v), issparse(v), mat2str(real(x), digits), mat2str(imag(x), digits), rule);
    end
catch err
    fprintf('CHK|%s|ERR %s %s|%s\n', name, err.identifier, strrep(err.message, newline, ' '), rule);
end
end
