% record: -noFigureWindows
% Open items 330 and 333 and one row of 325 (ADR 0370): the complex flag's leftovers after ADR 0364
% (unary plus, sort, unique, cell2mat, x.^1, logical, a string part of complex, a flagged
% singular system) and the numeric leftovers after ADR 0365 (a vector to a scalar power, an
% integer with a logical, a single or a double array under every arithmetic operator, gammaln and
% psi of a negative argument, gammaln(Inf), psi(0), reallog/realsqrt/realpow's identifiers, mat2str
% of a complex infinity, a complex inv that is nearly singular), and a real number times a complex
% one with an infinite or NaN part. Probes probe1-probe8 (lane 6C's scratch).

% Every operand is a variable: R2025b folds an operator over literals when it reads the file.
x1 = complex(1, 0);
xa = complex([3 1 2], 0);
xm = complex([3 1; 2 4], 0);
xs = complex(single([2 1]), 0);
xn = complex([-3 1 -1 2 1], 0);

% --- unary plus -----------------------------------------------------------------------------
p6c_chk('uplus_scalar', @() +x1);
p6c_chk('uplus_array', @() +xa);
p6c_chk('uplus_single', @() +xs);
p6c_chk('uplus_fn', @() uplus(xa));
p6c_chk('uplus_char', @() +'a');
p6c_chk('uplus_charrow', @() +'ab');
p6c_chk('uplus_true', @() +true);
p6c_chk('uplus_mask', @() +[true false]);
p6c_chk('uplus_int8', @() +int8(3));
p6c_chk('uplus_single_real', @() +single(3));
p6c_chk('uplus_cplx', @() +(1+2i));
p6c_chk('uplus_neg', @() -+xa);
p6c_chk('uplus_paren', @() +(xa));
p6c_chk('uplus_in_sum', @() 1 + +xa);
p6c_chk('uplus_empty', @() +complex([], 0));
p6c_chk('uplus_neg_zero', @() 1 ./ (+(-0)));
p6c_chk('uplus_str', @() +"a");
p6c_chk('uplus_cell', @() +{1});
p6c_chk('uplus_struct', @() +struct('a', 1));
p6c_chk('uplus_fh', @() +@sin);
y = +xa;
p6c_chk('uplus_bound', @() y);

% --- sort, unique, cell2mat -----------------------------------------------------------------
p6c_chk('sort_arr', @() sort(xa));
p6c_chk('sort_desc', @() sort(xa, 'descend'));
p6c_chk('sort_mat', @() sort(xm));
p6c_chk('sort_mat_dim2', @() sort(xm, 2));
p6c_chk('sort_scalar', @() sort(x1));
p6c_chk('sort_single', @() sort(xs));
p6c_chk('sort_neg', @() sort(xn));
p6c_chk('sort_neg_desc', @() sort(xn, 'descend'));
p6c_chk('sort_neg_real', @() sort(xn, 'ComparisonMethod', 'real'));
p6c_chk('sort_nan', @() sort(complex([NaN 1 -2], 0)));
p6c_chk('sort_nan_desc', @() sort(complex([NaN 1 -2], 0), 'descend'));
p6c_chk('sort_col', @() sort(complex([3; 1; 2], 0)));
p6c_chk('sort_mat_neg', @() sort(complex([-3 1; 2 -1], 0)));
p6c_chk('sort_mat_neg_dim2', @() sort(complex([-3 1; 2 -1], 0), 2, 'descend'));
p6c_chk('sort_empty', @() sort(complex([], 0)));
[s1, i1] = sort(xn);
p6c_chk('sort_two_s', @() s1);
p6c_chk('sort_two_i', @() i1);
p6c_chk('unique_arr', @() unique(xa));
p6c_chk('unique_dup', @() unique(complex([2 1 2], 0)));
p6c_chk('unique_scalar', @() unique(x1));
p6c_chk('unique_stable', @() unique(complex([2 1 2], 0), 'stable'));
p6c_chk('unique_mat', @() unique(xm));
p6c_chk('unique_col', @() unique(complex([3; 1; 3], 0)));
p6c_chk('unique_single', @() unique(xs));
p6c_chk('unique_neg', @() unique(xn));
p6c_chk('unique_single_neg', @() unique(complex(single([-2 1 2]), 0)));
p6c_chk('unique_zeros', @() unique(complex([0 -0 1], 0)));
p6c_chk('unique_nan', @() unique(complex([NaN 1 NaN], 0)));
[u1, ia1, ic1] = unique(xn);
p6c_chk('unique_three_u', @() u1);
p6c_chk('unique_three_ia', @() ia1);
p6c_chk('unique_three_ic', @() ic1);
[u2, ia2, ic2] = unique(xn, 'last');
p6c_chk('unique_last_u', @() u2);
p6c_chk('unique_last_ia', @() ia2);
p6c_chk('unique_last_ic', @() ic2);
[u3, ia3, ic3] = unique(xn, 'stable');
p6c_chk('unique_stable_u', @() u3);
p6c_chk('unique_stable_ia', @() ia3);
p6c_chk('unique_stable_ic', @() ic3);
p6c_chk('cell2mat_scalar', @() cell2mat({x1}));
p6c_chk('cell2mat_arr', @() cell2mat({xa}));
p6c_chk('cell2mat_two', @() cell2mat({x1, 2}));
p6c_chk('cell2mat_two_rev', @() cell2mat({2, x1}));
p6c_chk('cell2mat_col', @() cell2mat({x1; x1}));
p6c_chk('cell2mat_three', @() cell2mat({x1; x1; x1}));
p6c_chk('cell2mat_grid', @() cell2mat({x1 x1; x1 x1}));
p6c_chk('cell2mat_rows', @() cell2mat({complex([1 2], 0); [3 4]}));
p6c_chk('cell2mat_single', @() cell2mat({complex(single(1), 0), single(2)}));
p6c_chk('cell2mat_cplx', @() cell2mat({1+2i, x1}));
p6c_chk('cell2mat_int', @() cell2mat({int8(1), x1}));
p6c_chk('cell2mat_mismatch', @() cell2mat({x1, [2 3]; [4 5], 6}));

% --- x .^ 1 ---------------------------------------------------------------------------------
p6c_chk('pow1_arr', @() xa .^ 1);
p6c_chk('pow1_scalar', @() x1 .^ 1);
p6c_chk('pow1_mat', @() xm .^ 1);
p6c_chk('pow1_single', @() xs .^ 1);
p6c_chk('pow1_col', @() complex([1; 2], 0) .^ 1);
p6c_chk('pow1_neg', @() complex([-1 -2], 0) .^ 1);
p6c_chk('pow1_nan', @() complex([NaN 1], 0) .^ 1);
p6c_chk('pow1_fn', @() power(xa, 1));
p6c_chk('pow1_empty', @() complex([], 0) .^ 1);
p6c_chk('pow1_exp_single', @() xa .^ single(1));
p6c_chk('pow1_exp_true', @() xa .^ true);
p6c_chk('pow1_exp_flagged', @() xa .^ complex(1, 0));
p6c_chk('pow1_exp_vector', @() xa .^ [1 1 1]);
p6c_chk('pow1_exp_int8', @() xa .^ int8(1));
p6c_chk('pow2_arr', @() xa .^ 2);
p6c_chk('pow0_arr', @() xa .^ 0);
p6c_chk('powm1_arr', @() xa .^ -1);
p6c_chk('mpow1_mat', @() xm ^ 1);
p6c_chk('pow1_cplx_exact', @() [1+2i 3] .^ 1);

% --- logical and complex --------------------------------------------------------------------
p6c_chk('logical_flag', @() logical(x1));
p6c_chk('logical_flag_arr', @() logical(complex([1 0], 0)));
p6c_chk('logical_flag_empty', @() logical(complex([], 0)));
p6c_chk('logical_flag_single', @() logical(xs));
p6c_chk('logical_cplx', @() logical(1+2i));
p6c_chk('logical_cplx_arr', @() logical([1 2i]));
p6c_chk('complex_str_re', @() complex("a", 0));
p6c_chk('complex_str_im', @() complex(1, "a"));
p6c_chk('complex_str_one', @() complex("a"));
p6c_chk('complex_str_arr', @() complex(["a" "b"], [0 0]));
p6c_chk('complex_str_both', @() complex("a", "b"));
p6c_chk('complex_str_empty', @() complex(string.empty, 0));
p6c_chk('complex_char', @() complex('a', 0));
p6c_chk('complex_char_one', @() complex('a'));

% --- a flagged singular system --------------------------------------------------------------
Z = complex(zeros(2));
b = [1; 2];
p6c_chk('zflag_mld', @() Z \ b);
p6c_chk('zflag_mld_eye', @() Z \ eye(2));
p6c_chk('zflag_mld_cplx_rhs', @() Z \ [1i; 2]);
p6c_chk('zflag_imag_mld', @() (zeros(2) * 1i) \ b);
E = complex(eye(2));
p6c_chk('eflag_mld', @() E \ b);
p6c_chk('eflag_rhs', @() eye(2) \ complex(b, 0));
p6c_chk('zflag_scalar', @() complex(0, 0) \ 1);
U = complex([1 2; 0 0]);
p6c_chk('uflag_mld', @() U \ b);

% --- a vector to a scalar power -------------------------------------------------------------
v3 = [1 2 3];
two = 2;
half = 0.5;
p6c_chk('vpow_2', @() v3 ^ two);
p6c_chk('vpow_half', @() v3 ^ half);
p6c_chk('vpow_1', @() v3 ^ 1);
p6c_chk('vpow_0', @() v3 ^ 0);
p6c_chk('vpow_m1', @() v3 ^ -1);
p6c_chk('vpow_col', @() [1; 2; 3] ^ two);
p6c_chk('vpow_col_half', @() [4; 9] ^ half);
p6c_chk('vpow_neg_row', @() [-1 2] ^ 3);
p6c_chk('vpow_cplx_exp', @() v3 ^ 1i);
p6c_chk('vpow_fn', @() mpower(v3, 2));
p6c_chk('vpow_single', @() single(v3) ^ 2);
p6c_chk('vpow_logical', @() [true false] ^ 2);
p6c_chk('vpow_char', @() 'ab' ^ 2);
p6c_chk('vpow_cplx_base', @() [1i 2] ^ 2);
p6c_chk('vpow_empty_row', @() zeros(1, 0) ^ 2);
p6c_chk('vpow_empty_col', @() zeros(0, 1) ^ 2);
p6c_chk('vpow_empty_square', @() [] ^ 2);
p6c_chk('vpow_inf', @() v3 ^ Inf);
p6c_chk('vpow_nan', @() v3 ^ NaN);
p6c_chk('vpow_nonsquare', @() [1 2; 3 4; 5 6] ^ 2);
p6c_chk('vpow_nd', @() ones(2, 2, 2) ^ 2);
p6c_chk('vpow_int', @() int8(v3) ^ 2);
p6c_chk('vpow_scalar_base', @() two ^ v3);
p6c_chk('vpow_vec_vec', @() v3 ^ v3);
p6c_chk('vpow_mat_mat', @() [1 2; 3 4] ^ [1 2; 3 4]);
p6c_chk('vpow_scalar_bool', @() true ^ 2);
p6c_chk('vpow_scalar_int', @() int8(2) ^ 2);

% --- an integer with what it does not combine with ------------------------------------------
a8 = int8(2);
t = true;
s25 = single(2.5);
i16 = int16(2);
ops = {'plus', 'minus', 'times', 'rdivide', 'ldivide', 'mtimes', 'mrdivide', 'mldivide'};
pairs = {'i8_l', a8, t; 'l_i8', t, a8; 'i8_s', a8, s25; 's_i8', s25, a8; 'i8_i16', a8, i16; ...
    'i8_c', a8, 'a'; 'c_i8', 'a', a8; 'i8_d', a8, 2.5; 'i8_lv', a8, [true false]; 'i8v_l', int8([1 2]), t; ...
    'i8_le', a8, true(0); 'i8_se', a8, single([]); 'i8e_l', int8([]), t; 'i8_dv', int8([1 2]), [1 2]; ...
    'i8_cs', a8, single(1+2i); 'i8_cd', a8, 1+2i; 'i8_flag', a8, x1; 'i8_de', a8, []; 'i8e_d', int8([]), 2};
for k = 1:numel(ops)
    fn = str2func(ops{k});
    for p = 1:size(pairs, 1)
        l = pairs{p, 2};
        r = pairs{p, 3};
        p6c_chk(sprintf('mix_%s_%s', ops{k}, pairs{p, 1}), @() fn(l, r));
    end
end
p6c_chk('mix_op_plus', @() a8 + t);
p6c_chk('mix_op_plus_single', @() a8 + s25);
p6c_chk('mix_op_times', @() a8 .* t);
p6c_chk('mix_op_ldivide', @() a8 .\ t);
p6c_chk('mix_op_cat', @() [a8 t]);
p6c_chk('mix_vec_dvec', @() int8([1 2]) + [1 2]);
p6c_chk('mix_vec_dcol', @() int8([1 2]) + [1; 2]);
p6c_chk('mix_vec_dempty', @() int8([1 2]) + []);
p6c_chk('mix_vec_charrow', @() int8([1 2]) + 'ab');
p6c_chk('mix_vec_char', @() int8([1 2]) .* 'a');
p6c_chk('mix_vec_dscalar', @() int8([1 2]) + 2);
p6c_chk('mix_vec_d11', @() int8([1 2]) + ones(1, 1));
p6c_chk('mix_vec_cplx', @() int8([1 2]) + 1i);
p6c_chk('mix_nd', @() int8(ones(1, 2, 2)) + ones(1, 2, 2));
p6c_chk('mix_uint8', @() uint8([1 2]) .* [1 2]);
p6c_chk('mix_same', @() int8([1 2]) + int8([1 2]));
p6c_chk('mix_la_mtimes_dmat', @() int8([1 2; 3 4]) * [1 2; 3 4]);
p6c_chk('mix_la_mtimes_lv', @() int8([1 2; 3 4]) * [true; false]);
p6c_chk('mix_la_mtimes_l', @() int8([1 2; 3 4]) * t);
p6c_chk('mix_la_mrdivide_dmat', @() int8([1 2; 3 4]) / [1 2; 3 4]);
p6c_chk('mix_la_mldivide_dmat', @() [1 2; 3 4] \ int8([1 2; 3 4]));
p6c_chk('mix_la_mldivide_cd', @() int8([1 2; 3 4]) \ [1i; 2]);
p6c_chk('mix_la_mtimes_cd', @() int8([1 2; 3 4]) * [1i; 2]);
p6c_chk('mix_la_mtimes_i16', @() int8([1 2; 3 4]) * int16([1; 2]));
A8 = int8([1 2; 3 4]);
b8 = int8([1; 2]);
p6c_chk('la_mldivide_same', @() A8 \ b8);
p6c_chk('la_mrdivide_same', @() b8.' / A8);
p6c_chk('la_mtimes_same', @() A8 * A8);
p6c_chk('la_mldivide_dscalar', @() A8 \ 2);
p6c_chk('la_mrdivide_dscalar', @() 2 / A8);
p6c_chk('la_mrdivide_by_scalar', @() A8 / 2);
p6c_chk('la_mldivide_scalar_left', @() 2 \ A8);
p6c_chk('la_mtimes_scalar', @() A8 * 2);
p6c_chk('la_empty_mldivide', @() int8([]) \ 2);
p6c_chk('mix_power_cplx', @() a8 .^ single(1+2i));
p6c_chk('mix_power_dvec', @() int8([1 2]) .^ [1 2]);
p6c_chk('mix_power_dempty', @() a8 .^ []);
p6c_chk('mix_power_vec_scalar', @() int8([1 2]) .^ 2);
p6c_chk('mix_power_char', @() a8 .^ 'ab');
p6c_chk('mix_mpower_cplx', @() a8 ^ (1+2i));

% --- gammaln and psi ------------------------------------------------------------------------
p6c_chk('gammaln_m1', @() gammaln(-1));
p6c_chk('gammaln_mhalf', @() gammaln(-0.5));
p6c_chk('gammaln_vec', @() gammaln([1 -1]));
p6c_chk('gammaln_nanvec', @() gammaln([NaN -1]));
p6c_chk('gammaln_single_m1', @() gammaln(single(-1)));
p6c_chk('gammaln_minf', @() gammaln(-Inf));
p6c_chk('gammaln_inf', @() gammaln(Inf));
p6c_chk('gammaln_single_inf', @() gammaln(single(Inf)));
p6c_chk('gammaln_nan', @() gammaln(NaN));
p6c_chk('gammaln_0', @() gammaln(0));
p6c_chk('gammaln_m0', @() gammaln(-0));
p6c_chk('gammaln_big', @() gammaln([1e306 realmax]));
p6c_chk('gammaln_cplx', @() gammaln(1i));
p6c_chk('gammaln_sparse', @() gammaln(sparse([1 2])));
p6c_chk('gammaln_int', @() gammaln(int8(-2)));
p6c_chk('gammaln_empty', @() gammaln([]));
p6c_chk('psi_m1', @() psi(-1));
p6c_chk('psi_mhalf', @() psi(-0.5));
p6c_chk('psi_vec_neg', @() psi([1 -1]));
p6c_chk('psi_single_m1', @() psi(single(-1)));
p6c_chk('psi_minf', @() psi(-Inf));
p6c_chk('psi_0', @() psi(0));
p6c_chk('psi_m0', @() psi(-0));
p6c_chk('psi_single_0', @() psi(single(0)));
p6c_chk('psi_vec_0', @() psi([0 0]));
p6c_chk('psi_inf', @() psi(Inf));
p6c_chk('psi_nan', @() psi(NaN));
p6c_chk('psi_k_m1', @() psi(1, -1));
p6c_chk('psi_k0_m1', @() psi(0, -1));
p6c_chk('psi_k_single_m1', @() psi(1, single(-1)));
p6c_chk('psi_k_0', @() psi(1, 0));
p6c_chk('psi_k0_0', @() psi(0, 0));
p6c_chk('psi_k_neg', @() psi(-1, 1));
p6c_chk('psi_cplx', @() psi(1i));
p6c_chk('psi_int', @() psi(int8(-1)));

% --- reallog, realsqrt, realpow -------------------------------------------------------------
p6c_chk('reallog_m1', @() reallog(-1));
p6c_chk('reallog_single_m1', @() reallog(single(-1)));
p6c_chk('reallog_vec', @() reallog([1 -1]));
p6c_chk('reallog_minf', @() reallog(-Inf));
p6c_chk('reallog_m0', @() reallog(-0));
p6c_chk('reallog_ok', @() reallog([1 0 Inf]));
p6c_chk('reallog_cplx', @() reallog(1i));
p6c_chk('realsqrt_m1', @() realsqrt(-1));
p6c_chk('realsqrt_single_m1', @() realsqrt(single(-1)));
p6c_chk('realsqrt_vec', @() realsqrt([1 -1]));
p6c_chk('realsqrt_minf', @() realsqrt(-Inf));
p6c_chk('realsqrt_cplx', @() realsqrt(1i));
p6c_chk('realpow_m1', @() realpow(-1, 0.5));
p6c_chk('realpow_cplx', @() realpow(1i, 2));
p6c_chk('realpow_cplx2', @() realpow(2, 1i));
p6c_chk('realpow_nan', @() realpow(NaN, 0.5));

% --- mat2str of a complex infinity ----------------------------------------------------------
p6c_chk('m2s_inf_nan', @() mat2str(complex(Inf, NaN)));
p6c_chk('m2s_inf_inf', @() mat2str(complex(Inf, Inf)));
p6c_chk('m2s_1_inf', @() mat2str(complex(1, Inf)));
p6c_chk('m2s_1_minf', @() mat2str(complex(1, -Inf)));
p6c_chk('m2s_1_nan', @() mat2str(complex(1, NaN)));
p6c_chk('m2s_nan_1', @() mat2str(complex(NaN, 1)));
p6c_chk('m2s_nan_nan', @() mat2str(complex(NaN, NaN)));
p6c_chk('m2s_minf_minf', @() mat2str(complex(-Inf, -Inf)));
p6c_chk('m2s_vec', @() mat2str([1+2i complex(Inf, 1)]));
p6c_chk('m2s_mat', @() mat2str([complex(1, Inf) 2; 3 complex(NaN, -Inf)]));
p6c_chk('m2s_inf_0', @() mat2str(complex(Inf, 0)));
p6c_chk('m2s_0_inf', @() mat2str(complex(0, Inf)));
p6c_chk('m2s_prec', @() mat2str(complex(Inf, NaN), 4));
p6c_chk('m2s_single', @() mat2str(single(complex(1, Inf))));
p6c_chk('m2s_class', @() mat2str(complex(1, Inf), 'class'));
p6c_chk('m2s_frac', @() mat2str(complex(0.5, -Inf)));
p6c_chk('m2s_finite', @() mat2str(complex(2, -1)));

% --- a complex inv that is nearly singular --------------------------------------------------
N1 = [1 0; 0 1e-17i];
N2 = [1 2; 2 4] * 1i;
N4 = [2 1i; 0 4]; % an inverse exact in both linear algebra lanes
N5 = [1 NaN; 1i 2];
p6c_chk('inv_near', @() inv(N1));
p6c_chk('inv_near_single', @() inv(single(N1)));
p6c_chk('inv_sing', @() inv(N2));
p6c_chk('inv_fine', @() inv(N4));
p6c_chk('inv_nan', @() inv(N5));
p6c_chk('inv_hermitian', @() inv([2 1i; -1i 3]));
p6c_chk('inv_real_near', @() inv([1 0; 0 1e-17]));
lastwarn('');
w = inv(N1); %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|inv_near_text|%s %s|exact\n', id, msg);
warning('off', 'MATLAB:nearlySingularMatrix');
p6c_chk('inv_near_off', @() inv(N1));
warning('on', 'MATLAB:nearlySingularMatrix');
warning('error', 'MATLAB:nearlySingularMatrix');
p6c_chk('inv_near_error', @() inv(N1));
warning('on', 'MATLAB:nearlySingularMatrix');

% --- a real number times a complex one ------------------------------------------------------
p6c_chk('lit_inf_1i', @() Inf*1i);
p6c_chk('lit_1i_inf', @() 1i*Inf);
p6c_chk('lit_neg_inf_1i', @() -Inf*1i);
p6c_chk('lit_nan_1i', @() NaN*1i);
p6c_chk('lit_inf_2i', @() Inf*2i);
xi = Inf;
j1 = 1i;
p6c_chk('var_inf_1i', @() xi * j1);
p6c_chk('var_1i_inf', @() j1 * xi);
p6c_chk('var_times', @() times(xi, j1));
p6c_chk('var_mtimes', @() mtimes(xi, j1));
reals = [Inf -Inf NaN 0 -0 2];
parts = [0 -0 1 Inf -Inf NaN];
names = {'inf', 'minf', 'nan', 'zero', 'mzero', 'two'};
for p = 1:numel(reals)
    for q = 1:numel(parts)
        for w = 1:numel(parts)
            xr = reals(p);
            zc = complex(parts(q), parts(w));
            r1 = xr * zc;
            r2 = zc .* xr;
            r3 = [xr xr] .* zc;
            same = isequaln(r1, r2) && isequaln([r1 r1], r3) && isreal(r1) == isreal(r3);
            if isreal(r1)
                txt = sprintf('real %s', num2str(r1));
            else
                txt = sprintf('complex %s %s', num2str(real(r1)), num2str(imag(r1)));
            end
            fprintf('CHK|rc_%s_%s_%s|%s %d|exact\n', names{p}, names{q}, names{w}, txt, same);
        end
    end
end
p6c_chk('rc_array', @() [Inf 0 NaN 2] * j1);
p6c_chk('rc_array_dot', @() [Inf 0 NaN 2] .* [1i 1i 1i complex(Inf, 1)]);
p6c_chk('rc_scalar_array', @() xi .* [1i 2+1i 0]);
p6c_chk('rc_single', @() single(xi) * j1);
p6c_chk('rc_single_cplx', @() xi * single(1i));
p6c_chk('rc_flagged', @() complex(Inf, 0) * 1i);
p6c_chk('rc_logical', @() true * complex(Inf, 1));
