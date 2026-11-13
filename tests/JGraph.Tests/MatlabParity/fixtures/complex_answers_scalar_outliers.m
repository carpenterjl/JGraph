% Small numeric refusals R2025b answers: a uniform arrayfun or cellfun whose function answers a
% complex scalar (open item 255), an integral whose midpoint is real and whose panels are not
% (256), isoutlier and its two siblings over one reading (259), and the matrix shape questions over
% complex, sparse, empty and N-D input (276); with arrayfun's refusal of a misspelt option, which
% since ADR 0316 is a third data input of the wrong size (253).
d = @(x) sprintf('%s %s %d %s %s', class(x), mat2str(size(x)), isreal(x), ...
    mat2str(double(real(x(:))).', 17), mat2str(double(imag(x(:))).', 17));

% --- 253: a misspelt option is data -------------------------------------------------------------
try
    arrayfun(@(k) k, [1 2], 'UnifromOutput', false);
    fprintf('CHK|af_misspelt|no error|exact\n');
catch err
    fprintf('CHK|af_misspelt|%s %s|exact\n', err.identifier, strrep(err.message, newline, ' '));
end
fprintf('CHK|af_three_scalars|%d|exact\n', arrayfun(@(k, a, b) k + a + b, 1, 'U', false));

% --- 255: a complex scalar is a scalar ----------------------------------------------------------
fprintf('CHK|af_sqrt_row|%s|exact\n', d(arrayfun(@(a) sqrt(a), [4 -1 9])));
fprintf('CHK|af_sqrt_col|%s|exact\n', d(arrayfun(@(a) sqrt(a), [4; -1; 9])));
fprintf('CHK|af_sqrt_mat|%s|exact\n', d(arrayfun(@(a) sqrt(a), [4 -1; 9 -16])));
fprintf('CHK|af_sqrt_real|%s|exact\n', d(arrayfun(@(a) sqrt(a), [4 9])));
fprintf('CHK|af_sqrt_one|%s|exact\n', d(arrayfun(@(a) sqrt(a), -1)));
fprintf('CHK|af_nd|%s|exact\n', d(arrayfun(@(a) 1i * a, reshape(1:8, 2, 2, 2))));
fprintf('CHK|af_single|%s|exact\n', d(arrayfun(@(a) sqrt(single(a)), [4 -1])));
[p, q] = arrayfun(@(a) deal(sqrt(a), a), [-4 9]);
fprintf('CHK|af_two_out|%s / %s|exact\n', d(p), d(q));
fprintf('CHK|cf_sqrt_row|%s|exact\n', d(cellfun(@(a) sqrt(a), {4, -1, 9})));
fprintf('CHK|cf_times_i_col|%s|exact\n', d(cellfun(@(a) a * 1i, {1; 2})));
fprintf('CHK|cf_sqrt_mat|%s|exact\n', d(cellfun(@(a) sqrt(a), {4, -1; 9, 16})));

% --- 256: complex panels behind a real midpoint ---------------------------------------------------
% integral promises max(AbsTol, RelTol*|q|) on the complex total, so the parts are held to that.
q = integral(@(x) sqrt(x - 1), 0, 2);
fprintf('CHK|int_re|%.17g|rel=1e-6\n', real(q));
fprintf('CHK|int_im|%.17g|rel=1e-6\n', imag(q));
q = quadgk(@(x) sqrt(x - 1), 0, 2);
fprintf('CHK|quadgk_re|%.17g|rel=1e-6\n', real(q));
fprintf('CHK|quadgk_im|%.17g|rel=1e-6\n', imag(q));
q = integral(@(x) sqrt(x - 1), 0, 2, 'ArrayValued', true);
fprintf('CHK|int_arrayvalued_im|%.17g|rel=1e-6\n', imag(q));
q = integral(@(x) sqrt(1 - x), 0, 2);
fprintf('CHK|int_mirror_re|%.17g|rel=1e-6\n', real(q));
fprintf('CHK|int_mirror_im|%.17g|rel=1e-6\n', imag(q));
q = integral(@(x) sqrt(x), -1, 3);
fprintf('CHK|int_left_re|%.17g|rel=1e-6\n', real(q));
fprintf('CHK|int_left_im|%.17g|abs=4e-6\n', imag(q));
q = integral(@(x) sqrt(x - 1), 0, 2, 'RelTol', 1e-12);
fprintf('CHK|int_tight_re|%.17g|rel=1e-10\n', real(q));
fprintf('CHK|int_tight_im|%.17g|rel=1e-10\n', imag(q));
fprintf('CHK|int_isreal|%d|exact\n', isreal(integral(@(x) sqrt(x - 1), 0, 2)));

% --- 259: one reading is never an outlier of itself ---------------------------------------------
[tf, L, U, C] = isoutlier(7);
fprintf('CHK|iso_default|%s %s %s %s %s|exact\n', class(tf), mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
o = {'median', 'mean', 'quartiles', 'grubbs', 'gesd'};
for k = 1:numel(o)
    [tf, L, U, C] = isoutlier(7, o{k});
    fprintf('CHK|iso_%s|%s %s %s %s %s|exact\n', o{k}, class(tf), mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
end
fprintf('CHK|iso_one|%s %s|exact\n', class(isoutlier(7)), mat2str(isoutlier(7)));
[tf, L, U, C] = isoutlier(7, 'movmedian', 3);
fprintf('CHK|iso_movmedian|%s %s %s %s|exact\n', mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
[tf, L, U, C] = isoutlier(7, 'movmean', 3);
fprintf('CHK|iso_movmean|%s %s %s %s|exact\n', mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
[tf, L, U, C] = isoutlier(7, 'percentiles', [10 90]);
fprintf('CHK|iso_percentiles|%s %s %s %s|exact\n', mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
[tf, L, U, C] = isoutlier(7, 2);
fprintf('CHK|iso_dim2|%s %s %s %s|exact\n', mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
[tf, L, U, C] = isoutlier(NaN);
fprintf('CHK|iso_nan|%s %s %s %s|exact\n', mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
[tf, L, U, C] = isoutlier([NaN NaN]);
fprintf('CHK|iso_all_nan|%s %s %s %s|exact\n', mat2str(tf), mat2str(L), mat2str(U), mat2str(C));
[B, TF] = rmoutliers(7);
fprintf('CHK|rm_one|%s %s|exact\n', mat2str(B), mat2str(TF));
[B, TF] = rmoutliers(7, 'mean');
fprintf('CHK|rm_mean|%s %s|exact\n', mat2str(B), mat2str(TF));
[B, TF] = rmoutliers(NaN);
fprintf('CHK|rm_nan|%s %s|exact\n', mat2str(B), mat2str(TF));
[B, TF] = rmoutliers(7, 'OutlierLocations', true);
fprintf('CHK|rm_located|%s %s %s|exact\n', mat2str(size(B)), mat2str(B), mat2str(TF));
[B, TF, L, U, C] = filloutliers(7, 'linear');
fprintf('CHK|fill_linear|%s %s %s %s %s|exact\n', mat2str(B), mat2str(TF), mat2str(L), mat2str(U), mat2str(C));
[B, TF, L, U, C] = filloutliers(7, 'center');
fprintf('CHK|fill_center|%s %s %s %s %s|exact\n', mat2str(B), mat2str(TF), mat2str(L), mat2str(U), mat2str(C));
[B, TF, L, U, C] = filloutliers(7, 0, 'grubbs');
fprintf('CHK|fill_grubbs|%s %s %s %s %s|exact\n', mat2str(B), mat2str(TF), mat2str(L), mat2str(U), mat2str(C));
[B, TF] = filloutliers(NaN, 'linear');
fprintf('CHK|fill_nan|%s %s|exact\n', mat2str(B), mat2str(TF));
[B, TF] = filloutliers(7, 0, 'OutlierLocations', true);
fprintf('CHK|fill_located|%s %s|exact\n', mat2str(B), mat2str(TF));

% --- 276: the shape questions over any double, single or logical matrix -------------------------
s = @(varargin) strjoin(cellfun(@mat2str, varargin, 'UniformOutput', false), ' ');
Z = [1 2i; 0 3];
[lo, up] = bandwidth(Z);
fprintf('CHK|sh_complex_tri|%s|exact\n', s(istriu(Z), istril(Z), isdiag(Z), isbanded(Z, 0, 1), isbanded(Z, 0, 0)));
fprintf('CHK|sh_complex_band|%s|exact\n', s(bandwidth(Z), lo, up, bandwidth(Z, 'upper')));
fprintf('CHK|sh_complex_sym|%s|exact\n', s(issymmetric(Z), ishermitian(Z)));
H = [2 1i; -1i 3];
fprintf('CHK|sh_hermitian|%s|exact\n', s(issymmetric(H), ishermitian(H), istriu(H), istril(H)));
S = [1 1i; 1i 2];
fprintf('CHK|sh_complex_symmetric|%s|exact\n', s(issymmetric(S), ishermitian(S)));
D = [1i 0; 0 1];
fprintf('CHK|sh_complex_diag|%s|exact\n', s(issymmetric(D), ishermitian(D), isdiag(D)));
C = complex([1 0; 0 1]);
fprintf('CHK|sh_zero_imag|%s|exact\n', s(istriu(C), istril(C), isdiag(C), issymmetric(C), ishermitian(C), bandwidth(C)));
L = [1 0 0; 2i 3 0; 0 4 5i];
[lo, up] = bandwidth(L);
fprintf('CHK|sh_lower|%s|exact\n', s(istril(L), istriu(L), lo, up, isbanded(L, 1, 0)));
K = [0 1i; -1i 0];
fprintf('CHK|sh_skew_a|%s|exact\n', s(issymmetric(K, 'skew'), ishermitian(K, 'skew'), ishermitian(K)));
K = [1i 2; -2 1i];
fprintf('CHK|sh_skew_b|%s|exact\n', s(issymmetric(K, 'skew'), ishermitian(K, 'skew')));
K = [0 1 2; -1 0 3; -2 -3 0];
fprintf('CHK|sh_skew_real|%s|exact\n', s(issymmetric(K, 'skew'), ishermitian(K, 'skew'), issymmetric(K)));
fprintf('CHK|sh_nonskew|%s|exact\n', s(issymmetric([1 2; 2 1], 'nonskew'), ishermitian([1 2; 2 1], 'nonskew')));
try
    issymmetric([1 2; 2 1], 'bogus');
    fprintf('CHK|sh_bad_flag|no error|exact\n');
catch err
    fprintf('CHK|sh_bad_flag|%s %s|exact\n', err.identifier, err.message);
end
try
    ishermitian([1 2; 2 1], 'bogus');
    fprintf('CHK|sh_bad_flag_h|no error|exact\n');
catch err
    fprintf('CHK|sh_bad_flag_h|%s %s|exact\n', err.identifier, err.message);
end
B = logical([1 1; 0 1]);
[lo, up] = bandwidth(B);
fprintf('CHK|sh_logical|%s|exact\n', s(istriu(B), istril(B), isdiag(B), issymmetric(B), ishermitian(B), isbanded(B, 0, 1), lo, up));
P = sparse([1 2; 0 3]);
[lo, up] = bandwidth(P);
fprintf('CHK|sh_sparse|%s|exact\n', s(istriu(P), istril(P), isdiag(P), issymmetric(P), ishermitian(P), isbanded(P, 0, 1), lo, up));
fprintf('CHK|sh_sparse_sym|%s|exact\n', s(issymmetric(sparse([1 2; 2 1])), ishermitian(sparse([1 2; 3 1])), ...
    issymmetric(sparse([0 1; -1 0]), 'skew'), ishermitian(sparse([0 1; -1 0]), 'skew'), issymmetric(sparse([1 2 3; 2 1 0]))));
T = single([1 2i; 0 3]);
fprintf('CHK|sh_single|%s|exact\n', s(istriu(T), istril(T), ishermitian(T), bandwidth(T, 'upper')));
[~, T] = schur([1 2; -3 4], 'complex');
fprintf('CHK|sh_schur|%s|exact\n', s(istriu(T), istril(T)));
Z = [1 2i 0; 0 3 4];
[lo, up] = bandwidth(Z);
fprintf('CHK|sh_wide|%s|exact\n', s(istriu(Z), istril(Z), isdiag(Z), issymmetric(Z), ishermitian(Z), lo, up));
fprintf('CHK|sh_nan|%s|exact\n', s(issymmetric([2 NaN; NaN 3]), ishermitian([2 NaN; NaN 3]), ...
    issymmetric([NaN 0; 0 1]), ishermitian([NaN 0; 0 1]), issymmetric([NaN 0; 0 1], 'skew')));
Z = complex(zeros(0, 3));
fprintf('CHK|sh_empty|%s|exact\n', s(istriu(Z), istril(Z), isdiag(Z), issymmetric(Z), ishermitian(Z), bandwidth(Z)));
fprintf('CHK|sh_empty_real|%s|exact\n', s(istriu([]), istril(zeros(0, 2)), isdiag([]), issymmetric([]), bandwidth(zeros(3, 0))));
[lo, up] = bandwidth(5);
fprintf('CHK|sh_scalar|%s|exact\n', s(istriu(5), istril(5i), isdiag([]), issymmetric(5i), ishermitian(5i), ishermitian(5), lo, up));
X = zeros(2, 2, 2);
fprintf('CHK|sh_nd|%s|exact\n', s(istriu(X), istril(X), isdiag(X), issymmetric(X), ishermitian(X), issymmetric(X, 'skew'), isbanded(X, 1, 1)));
try
    bandwidth(X);
    fprintf('CHK|sh_nd_bandwidth|no error|exact\n');
catch err
    fprintf('CHK|sh_nd_bandwidth|%s %s|exact\n', err.identifier, err.message);
end
try
    isbanded([1 2i; 0 3], -1, 0);
    fprintf('CHK|sh_band_negative|no error|exact\n');
catch err
    fprintf('CHK|sh_band_negative|%s %s|exact\n', err.identifier, err.message);
end
% JGraph keeps answering an integer matrix; R2025b takes only double, single and logical.
try
    fprintf('CHK|sh_integer|%s|div=ADR0339\n', s(istriu(int8([1 0; 2 3]))));
catch err
    fprintf('CHK|sh_integer|%s|div=ADR0339\n', err.identifier);
end
