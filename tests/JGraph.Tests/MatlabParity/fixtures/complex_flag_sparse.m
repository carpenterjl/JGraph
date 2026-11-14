% The complex flag of a value whose imaginary parts are all zero, and complex sparse matrices (open
% item 298, ADR 0364). complex(1, 0) is complex in R2025b; which operations keep that flag and which
% drop it was measured (probes p5c, p5e, p5i), and each row below is one of them. A complex sparse
% matrix is built from a complex dense matrix or complex triplets, and answers full, nnz, find,
% arithmetic, ' and .', real/imag/abs/conj and a MAT-file round trip.

% --- complex() keeps the flag --------------------------------------------------------------------
cfs_chk('c_scalar', 'v = complex(1, 0);');
cfs_chk('c_row', 'v = complex([1 2], 0);');
cfs_chk('c_single', 'v = complex(single(1), 0);');
cfs_chk('c_one_arg', 'v = complex(1);');
cfs_chk('c_one_arg_row', 'v = complex([1 2]);');
cfs_chk('c_logical', 'v = complex(true, 0);');
cfs_chk('c_spread', 'v = complex(1, [0 0]);');
cfs_chk('c_matrix', 'v = complex(zeros(2), 0);');
cfs_chk('c_single_imag', 'v = complex(1, single(0));');
cfs_chk('c_nd', 'v = complex(ones(2, 2, 2), 0);');
cfs_chk('c_empty', 'v = complex([], 0);');
cfs_chk('c_char', 'v = complex(''ab'', [1 2]);');
cfs_chk('c_logical_row', 'v = complex(true(1, 2));');
cfs_chk('c_sparse_dense', 'v = complex(sparse([1 0; 0 2]), 0);');
cfs_chk('c_sparse_both', 'v = complex(sparse([1 0; 0 2]), sparse([0 0; 0 1]));');
cfs_chk('c_dense_sparse', 'v = complex(1, sparse(0));');
cfs_chk('c_bad_imag', 'v = complex(1, 1i);');
cfs_chk('c_bad_real', 'v = complex(1i, 1i);');
cfs_chk('c_bad_cell', 'v = complex({1}, 0);');
cfs_chk('c_bad_cell_imag', 'v = complex(1, {0});');
cfs_chk('c_bad_size', 'v = complex(ones(2), ones(1, 4));');
cfs_chk('c_bad_shape', 'v = complex([1 2], [3; 4]);');
cfs_chk('c_bad_count', 'v = complex([1 2], [1 2 3]);');

% --- an indexed read drops it --------------------------------------------------------------------
cfs_chk('i_first', 'x = complex([1 2], 0); v = x(1);');
cfs_chk('i_scalar', 'x = complex(1, 0); v = x(1);');
cfs_chk('i_colon', 'x = complex([1 2], 0); v = x(:);');
cfs_chk('i_range', 'x = complex([1 2 3], 0); v = x(2:3);');
cfs_chk('i_end', 'x = complex([1 2], 0); v = x(end);');
cfs_chk('i_three', 'x = complex(1, 0); v = x(1, 1, 1);');
cfs_chk('i_row', 'x = complex(zeros(2), 0); v = x(2, :);');
cfs_chk('i_none', 'x = complex(1, 0); v = x([]);');

% --- an indexed write drops it; a copy keeps it --------------------------------------------------
cfs_chk('a_one', 'x = complex([1 2], 0); x(1) = 5; v = x;');
cfs_chk('a_grow', 'x = complex(1, 0); x(2) = 3; v = x;');
cfs_chk('a_grow_row', 'x = complex([1 2], 0); x(3) = 7; v = x;');
cfs_chk('a_into_real', 'x = [1 2]; x(1) = complex(5, 0); v = x;');
cfs_chk('a_scalar', 'x = complex(1, 0); x(1) = 2; v = x;');
cfs_chk('a_all', 'x = complex([1 2], 0); x(:) = [3 4]; v = x;');
cfs_chk('a_delete', 'x = complex([1 2], 0); x(1) = []; v = x;');
cfs_chk('a_into_scalar', 'x = 1; x(1) = complex(5, 0); v = x;');
cfs_chk('a_copy', 'x = complex(1, 0); y = x; v = y;');
cfs_chk('a_field', 's.a = complex(1, 0); v = s.a;');
cfs_chk('a_cell', 'c = {complex(1, 0)}; v = c{1};');
cfs_chk('a_return', 'f = @() complex(1, 0); v = f();');

% --- concatenation, transpose and reshape keep it ------------------------------------------------
cfs_chk('j_row', 'v = [complex(1, 0) 2];');
cfs_chk('j_col', 'v = [2; complex(1, 0)];');
cfs_chk('j_arrays', 'v = [complex([1 2], 0) 3];');
cfs_chk('j_cat', 'v = cat(1, complex(1, 0), 2);');
cfs_chk('j_bracket', 'v = [complex(1, 0)];');
cfs_chk('j_empty', 'v = horzcat(complex(1, 0), []);');
cfs_chk('j_two', 'v = [complex(1, 0) complex(2, 0)];');
cfs_chk('j_vertcat', 'v = vertcat(complex(3, 0), 1);');
cfs_chk('t_ctranspose', 'v = complex(1, 0)'';');
cfs_chk('t_transpose', 'v = complex([1 2], 0).'';');
cfs_chk('t_ctranspose_row', 'v = complex([1 2], 0)'';');
cfs_chk('t_reshape', 'v = reshape(complex([1 2], 0), 2, 1);');
cfs_chk('t_permute', 'v = permute(complex([3 1], 0), [2 1]);');
cfs_chk('t_squeeze', 'v = squeeze(complex([3 1], 0));');
cfs_chk('t_full', 'v = full(complex([3 1], 0));');

% --- arithmetic and the conversions drop it ------------------------------------------------------
cfs_chk('o_uminus', 'v = -complex(1, 0);');
cfs_chk('o_uminus_row', 'v = -complex([1 2], 0);');
cfs_chk('o_plus_zero', 'v = complex(1, 0) + 0;');
cfs_chk('o_plus_zero_row', 'v = complex([1 2], 0) + 0;');
cfs_chk('o_times', 'v = complex(1, 0) * 2;');
cfs_chk('o_minus_self', 'v = complex(1, 0) - complex(1, 0);');
cfs_chk('o_times_row', 'v = complex([1 2], 0) .* [1 2];');
cfs_chk('o_divide', 'v = complex(1, 0) / 1;');
cfs_chk('o_abs', 'v = abs(complex(1, 0));');
cfs_chk('o_sum', 'v = sum(complex([1 2], 0));');
cfs_chk('o_eq', 'v = complex(1, 0) == 1;');
cfs_chk('o_sqrt', 'v = sqrt(complex(4, 0));');
cfs_chk('o_power', 'v = complex(1, 0)^2;');
cfs_chk('o_mtimes', 'v = complex([1 2], 0) * [1; 1];');
cfs_chk('o_zero_plus', 'v = 0 + complex(1, 0);');
cfs_chk('o_uplus_fn', 'v = uplus(complex(3, 0));');
cfs_chk('o_real', 'v = real(complex(1, 0));');
cfs_chk('o_imag', 'v = imag(complex(1, 0));');
cfs_chk('o_real_row', 'v = real(complex([1 2], 0));');
cfs_chk('o_conj', 'v = conj(complex(1, 0));');
cfs_chk('o_double', 'v = double(complex(3, 0));');
cfs_chk('o_single', 'v = single(complex(1, 0));');
cfs_chk('o_int8', 'v = int8(complex(1, 0));');
cfs_chk('o_int8_row', 'v = int8(complex([3 1], 0));');
cfs_chk('o_repmat', 'v = repmat(complex(1, 0), 1, 2);');
cfs_chk('o_flip', 'v = flip(complex([3 1], 0));');
cfs_chk('o_circshift', 'v = circshift(complex([3 1], 0), 1);');
cfs_chk('o_max', 'v = max(complex([3 1], 0));');
cfs_chk('o_cumsum', 'v = cumsum(complex([3 1], 0));');
cfs_chk('o_isequal', 'v = isequal(complex(1, 0), 1);');
cfs_chk('q_isreal', 'v = isreal(complex(1, 0));');
cfs_chk('q_isreal_row', 'v = isreal(complex([1 2], 0));');
cfs_chk('q_class', 'v = class(complex(1, 0));');
cfs_chk('q_num2str', 'v = num2str(complex(1, 0));');
cfs_chk('q_mat2str', 'v = mat2str(complex(1, 0));');
cfs_chk('q_whos', 'x = complex(1, 0); w = whos(''x''); v = w.complex;');
cfs_chk('q_whos_bytes', 'x = complex([1 2], 0); w = whos(''x''); v = w.bytes;');

% --- a MAT-file keeps it ---------------------------------------------------------------------------
cfs_chk('m_scalar', 'x = complex(1, 0); f = [tempname ''.mat'']; save(f, ''x''); clear x; load(f); delete(f); v = x;');
cfs_chk('m_row', 'x = complex([1 2], 0); f = [tempname ''.mat'']; save(f, ''x''); clear x; load(f); delete(f); v = x;');
cfs_chk('m_v73_scalar', 'x = complex(1, 0); f = [tempname ''.mat'']; save(f, ''x'', ''-v7.3''); clear x; load(f); delete(f); v = x;');
cfs_chk('m_v73_row', 'x = complex([1 2], 0); f = [tempname ''.mat'']; save(f, ''x'', ''-v7.3''); clear x; load(f); delete(f); v = x;');
cfs_chk('m_whos_file', 'x = complex(1, 0); f = [tempname ''.mat'']; save(f, ''x''); w = whos(''-file'', f); delete(f); v = w.complex;');

% --- sparse of a flagged value keeps it ------------------------------------------------------------
cfs_chk('f_sparse', 'v = sparse(complex(1, 0));');
cfs_chk('f_sparse_row', 'v = sparse(complex([1 2], 0));');
cfs_chk('f_sparse_zero', 'v = sparse(complex([1 0], 0));');
cfs_chk('f_sparse_matrix', 'v = sparse(complex([1 0; 0 2], 0));');
cfs_chk('f_full', 'v = full(sparse(complex([1 0], 0)));');
cfs_chk('f_transpose', 'v = sparse(complex([1 0; 0 2], 0)).'';');
cfs_chk('f_ctranspose', 'v = sparse(complex([1 0; 0 2], 0))'';');
cfs_chk('f_uminus', 'v = -sparse(complex([1 0; 0 2], 0));');
cfs_chk('f_plus', 'v = sparse(complex([1 0; 0 2], 0)) + sparse(complex([1 0; 0 2], 0));');
cfs_chk('f_index', 'S = sparse(complex([1 0; 0 2], 0)); v = S(1, :);');
cfs_chk('f_scale', 'v = sparse(complex([1 0; 0 2], 0)) * 2;');
cfs_chk('f_mtimes', 'v = sparse(complex([1 0; 0 2], 0)) * sparse(complex([1 0; 0 2], 0));');
cfs_chk('f_save', 'S = sparse(complex([1 0; 0 2], 0)); f = [tempname ''.mat'']; save(f, ''S''); clear S; load(f); delete(f); v = S;');
cfs_chk('f_nnz', 'v = nnz(sparse(complex([1 0; 0 2], 0)));');
cfs_chk('f_find', '[~, ~, v] = find(sparse(complex([1 0; 0 2], 0)));');
cfs_chk('f_triplets', 'v = sparse([1 2], [1 2], complex([1 3], 0));');
cfs_chk('f_triplets_mn', 'v = sparse([1 2], [1 2], complex([1 3], 0), 2, 2);');

% --- complex sparse matrices -----------------------------------------------------------------------
cfs_chk('s_dense', 'v = sparse([1+2i 0; 0 3]);');
cfs_chk('s_triplets_mn', 'v = sparse([1 2], [1 2], [1+2i 3], 2, 2);');
cfs_chk('s_triplets', 'v = sparse([1 2], [1 2], [1+2i 3]);');
cfs_chk('s_triplets_nzmax', 'v = sparse([1 2], [1 2], [1+2i 3], 3, 3, 5);');
cfs_chk('s_sum_dupes', 'v = sparse([1 1], [1 1], [1i 2]);');
cfs_chk('s_cancel_dupes', 'v = sparse([1 1], [1 1], [1i -1i]);');
cfs_chk('s_full', 'v = full(sparse([1+2i 0; 0 3]));');
cfs_chk('s_nnz', 'v = nnz(sparse([1+2i 0; 0 3]));');
cfs_chk('s_nzmax', 'v = nzmax(sparse([1+2i 0; 0 3]));');
cfs_chk('s_find_i', '[i1, j1, vv] = find(sparse([1+2i 0; 0 3])); v = i1;');
cfs_chk('s_find_j', '[i1, j1, vv] = find(sparse([1+2i 0; 0 3])); v = j1;');
cfs_chk('s_find_v', '[i1, j1, vv] = find(sparse([1+2i 0; 0 3])); v = vv;');
cfs_chk('s_find_linear', 'v = find(sparse([1+2i 0; 0 3]));');
cfs_chk('s_nonzeros', 'v = nonzeros(sparse([1+2i 0; 0 3]));');
cfs_chk('s_issparse', 'v = issparse(sparse([1+2i 0; 0 3]));');
cfs_chk('s_isreal', 'v = isreal(sparse([1+2i 0; 0 3]));');
cfs_chk('s_class', 'v = class(sparse([1+2i 0; 0 3]));');
cfs_chk('s_size', 'v = size(sparse([1+2i 0; 0 3]));');
cfs_chk('s_whos_bytes', 'S = sparse([1+2i 0; 0 3]); w = whos(''S''); v = w.bytes;');
cfs_chk('s_whos_bytes_flag', 'S = sparse(complex([1 0; 0 2], 0)); w = whos(''S''); v = w.bytes;');
cfs_chk('s_plus', 'S = sparse([1+2i 0; 0 3]); v = S + S;');
cfs_chk('s_minus_self', 'S = sparse([1+2i 0; 0 3]); v = S - S;');
cfs_chk('s_scale', 'S = sparse([1+2i 0; 0 3]); v = S * 2;');
cfs_chk('s_scale_complex', 'S = sparse([1+2i 0; 0 3]); v = S * (1+1i);');
cfs_chk('s_scale_i_left', 'S = sparse([1+2i 0; 0 3]); v = 1i * S;');
cfs_chk('s_divide', 'S = sparse([1+2i 0; 0 3]); v = S / 2;');
cfs_chk('s_divide_i', 'S = sparse([1+2i 0; 0 3]); v = S / (1i);');
cfs_chk('s_times', 'S = sparse([1+2i 0; 0 3]); v = S .* S;');
cfs_chk('s_times_real', 'S = sparse([1+2i 0; 0 3]); v = S .* sparse([2 0; 0 0]);');
cfs_chk('s_mtimes', 'S = sparse([1+2i 0; 0 3]); v = S * S;');
cfs_chk('s_mtimes_real', 'S = sparse([1+2i 0; 0 3]); v = S * sparse([1 0; 0 1]);');
cfs_chk('s_mtimes_dense', 'S = sparse([1+2i 0; 0 3]); v = S * [1; 1];');
cfs_chk('s_dense_mtimes', 'S = sparse([1+2i 0; 0 3]); v = [1 1] * S;');
cfs_chk('s_plus_dense', 'S = sparse([1+2i 0; 0 3]); v = S + 1;');
cfs_chk('s_plus_i', 'v = sparse(2, 2) + 1i;');
cfs_chk('s_real_times_i', 'v = speye(2) * 1i;');
cfs_chk('s_real_times_complex', 'S = sparse([1 0; 0 3]); v = S * (1+1i);');
cfs_chk('s_uminus', 'S = sparse([1+2i 0; 0 3]); v = -S;');
cfs_chk('s_ctranspose', 'S = sparse([1+2i 0; 0 3]); v = S'';');
cfs_chk('s_transpose', 'S = sparse([1+2i 0; 0 3]); v = S.'';');
cfs_chk('s_real', 'v = real(sparse([1+2i 0; 0 3]));');
cfs_chk('s_imag', 'v = imag(sparse([1+2i 0; 0 3]));');
cfs_chk('s_abs', 'v = abs(sparse([3+4i 0; 0 3]));');
cfs_chk('s_conj', 'v = conj(sparse([1+2i 0; 0 3]));');
cfs_chk('s_column', 'S = sparse([1+2i 0; 0 3]); v = S(:, 1);');
cfs_chk('s_column_real', 'S = sparse([1+2i 0; 0 3]); v = S(:, 2);');
cfs_chk('s_row_times_i', 'S = sparse([1+2i 0; 0 3]); v = S(1, :) * 1i;');
cfs_chk('s_isequal_dense', 'S = sparse([1+2i 0; 0 3]); v = isequal(S, [1+2i 0; 0 3]);');
cfs_chk('s_isequal_transpose', 'S = sparse([1+2i 0; 0 3]); v = isequal(S, S.'');');
cfs_chk('s_save', 'S = sparse([1+2i 0; 0 3]); f = [tempname ''.mat'']; save(f, ''S''); clear S; load(f); delete(f); v = S;');
cfs_chk('s_real_uminus', 'S = sparse([1 0; 0 3]); v = -S;');
cfs_chk('s_real_real', 'S = sparse([1 0; 0 3]); v = real(S);');
cfs_chk('s_real_nonzeros', 'S = sparse([1 0; 0 3]); v = nonzeros(S);');
cfs_chk('s_real_times', 'S = sparse([1 0; 0 3]); v = S .* S;');

function cfs_chk(name, code)
% One measured operation as one line: the value's class, size, isreal, issparse and its real and
% imaginary parts (full), a char answer as its text, or the refusal's identifier and message.
try
    eval(code);
    if ischar(v)
        fprintf('CHK|%s|char %s|exact\n', name, v);
    else
        x = full(v);
        x = double(x(:)).';
        fprintf('CHK|%s|%s %s real=%d sparse=%d re=%s im=%s|exact\n', name, class(v), mat2str(size(v)), ...
            isreal(v), issparse(v), mat2str(real(x), 17), mat2str(imag(x), 17));
    end
catch err
    fprintf('CHK|%s|ERR %s %s|exact\n', name, err.identifier, err.message);
end
end
