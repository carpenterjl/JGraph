% dot_rules.m -- dot as R2025b answers it: the scalar product of two vectors, the columnwise product
% of two same-size matrices, the product along a named dimension, the classes it answers in, and the
% refusals in dot's own words. Integer-valued inputs are exact on any fold. On the phi surfaces the
% matrix product is pinned to sum(conj(a).*b) bit for bit on each engine's own sum (R2025b is), and
% the long vector product, a BLAS dot in MATLAB, to a tolerance.
M1 = [1 2; 3 4];
M2 = [5 6; 7 8];

vals('vec_row_row', dot([1 2 3], [4 5 6]));
vals('vec_row_col', dot([1 2 3], [4; 5; 6]));
vals('vec_col_row', dot([1; 2; 3], [4 5 6]));
vals('mat', dot(M1, M2));
vals('mat_dim1', dot(M1, M2, 1));
vals('mat_dim2', dot(M1, M2, 2));
vals('mat_dim3', dot(M1, M2, 3));
vals('mat_dim4_magic', dot(magic(3), magic(3), 4));
vals('mat_dim_int8', dot(magic(3), magic(3), int8(2)));
vals('mat_dim_2point0', dot(M1, M2, 2.0));
vals('nd_dim3', dot(reshape(1:12, 2, 3, 2), reshape(12:-1:1, 2, 3, 2), 3));
vals('nd_no_dim', dot(reshape(1:12, 2, 3, 2), reshape(12:-1:1, 2, 3, 2)));
vals('nd_134_no_dim', dot(reshape(1:12, 1, 3, 4), reshape(12:-1:1, 1, 3, 4)));
vals('nd_vector_pair', dot(reshape([1 2 3], 1, 1, 3), reshape([4 5 6], 1, 1, 3)));
vals('nd_ones113', dot(ones(1, 1, 3), ones(1, 1, 3)));
vals('row_dim2', dot([1 2 3], [4 5 6], 2));
vals('row_dim1', dot([1 2 3], [4 5 6], 1));
vals('complex_vec', dot([1+2i 3-1i], [2-1i 1i]));
vals('complex_self', dot([1+2i; 3-1i], [1+2i; 3-1i]));
vals('complex_mat', dot([1+2i 3; 4 5i], [1 2i; 3 4]));
vals('complex_mat_dim2', dot([1+1i 2; 3 4], [1 1; 1 1], 2));
vals('complex_scalar', dot(2+1i, 3));
vals('scalar', dot(5, 7));
vals('empty00', dot([], []));
vals('empty03', dot(zeros(0, 3), zeros(0, 3)));
vals('empty30', dot(zeros(3, 0), zeros(3, 0)));
vals('empty10_01', dot(zeros(1, 0), zeros(0, 1)));
vals('char', dot('ab', 'cd'));
vals('logical', dot([true false; true true], [true true; false true]));
vals('logical_vec', dot([true false true], [1 1 1]));
vals('single_mat', dot(single([1 2; 3 4]), [1 1; 1 1]));
vals('single_dim2', dot(single([1 2; 3 4]), [1 1; 1 1], 2));
vals('double_single', dot([1 2; 3 4], single([1 1; 1 1])));
vals('single_vec', dot(single([1 2 3]), [4 5 6]));
vals('nan', dot([1 NaN 3], [1 1 1]));

msg('int_class', @() dot(int8([1 2]), [3 4]));
msg('int_before_size', @() dot(int8([1 2 3]), [1 2]));
msg('size_vec', @() dot([1 2 3], [1 2]));
msg('size_mat_vec', @() dot([1 2; 3 4], [1 2 3 4]));
msg('size_row_113', @() dot([1 2 3], reshape([4 5 6], 1, 1, 3)));
msg('size_113_col', @() dot(reshape([1 2 3], 1, 1, 3), [4; 5; 6]));
msg('size_nd', @() dot(reshape(1:12, 2, 3, 2), reshape(1:18, 2, 3, 3)));
msg('size_with_dim', @() dot([1 2 3], [1 2], 1));
msg('dim_all', @() dot(magic(3), magic(3), 'all'));
msg('dim_vector', @() dot(magic(3), magic(3), [1 2]));
msg('dim_logical', @() dot(magic(3), magic(3), true));
msg('dim_empty', @() dot(M1, M1, []));
msg('cell', @() dot({1}, {2}));
refused('dim_zero', @() dot(magic(3), magic(3), 0));
refused('dim_fraction', @() dot(magic(3), magic(3), 1.5));
refused('dim_negative', @() dot(M1, M2, -1));
refused('dim_inf', @() dot(M1, M2, Inf));
refused('dim_nan', @() dot(M1, M2, NaN));

phi = 0.618033988749895;
A = reshape(mod((1:490000) * phi, 1) - 0.5, 700, 700);
B = reshape(mod((1:490000) * phi * phi, 1) - 0.25, 700, 700);
D = dot(A, B);
chk('big_matrix_is_sum', double(isequal(D, sum(A .* B))), 'exact');
chk('big_dim2_is_sum', double(isequal(dot(A, B, 2), sum(A .* B, 2))), 'exact');
chk('big_matrix_size', mat2str(size(D)), 'exact');
chk('big_matrix_total', sum(D), 'abs=1e-9');
chk('big_matrix_first', D(1), 'abs=1e-12');
chk('big_matrix_last', D(end), 'abs=1e-12');
x = A(:);
y = B(:);
chk('big_vector', dot(x, y), 'abs=1e-8');
chk('big_vector_row_col', dot(x', y), 'abs=1e-8');

function vals(name, v)
fprintf('CHK|%s_class|%s|exact\n', name, class(v));
fprintf('CHK|%s_size|%s|exact\n', name, mat2str(size(v)));
w = double(v(:));
for i = 1:numel(w)
    fprintf('CHK|%s_%d|%.17g|exact\n', name, i, real(w(i)));
    fprintf('CHK|%s_%di|%.17g|exact\n', name, i, imag(w(i)));
end
end

function msg(name, f)
text = 'none';
try
    f();
catch err
    text = err.message;
end
fprintf('CHK|%s|%s|exact\n', name, text);
end

function refused(name, f)
ok = 0;
try
    f();
catch
    ok = 1;
end
fprintf('CHK|%s|%d|exact\n', name, ok);
end

function chk(name, v, rule)
if ischar(v)
    fprintf('CHK|%s|%s|%s\n', name, v, rule);
else
    fprintf('CHK|%s|%.17g|%s\n', name, double(v), rule);
end
end
