% record: -noFigureWindows
% Open items 199, 272, 299 and 307 (ADR 0355). intmax/intmin answer in the class they name;
% x.^0.5 and x.^-1 are sqrt(x) and 1./x to the bit, and an integer is raised only to a whole
% power that is not negative; the floating-only verbs refuse a logical, a char or an integer;
% isoutlier of a single answers single fences; inv, \ and / of a singular or nearly singular
% matrix warn under R2025b's identifiers and answer. Probes probe_int, probe_pow, probe_pow2,
% probe_ipow, probe_float, probe_la, probe_misc, probe_sing, probe_sing2 (agent Q's scratch).

% --- 199: intmax and intmin ---------------------------------------------------------------
cls = {'int8', 'int16', 'int32', 'uint8', 'uint16', 'uint32'};
for k = 1:numel(cls)
    u9b_chk(['max_' cls{k}], @() intmax(cls{k}));
    u9b_chk(['min_' cls{k}], @() intmin(cls{k}));
    u9b_chk(['max_plus_' cls{k}], @() intmax(cls{k}) + 1);
    u9b_chk(['min_minus_' cls{k}], @() intmin(cls{k}) - 1);
    u9b_chk(['max_half_' cls{k}], @() intmax(cls{k}) / 2);
end
u9b_chk('max_none', @() intmax);
u9b_chk('min_none', @() intmin);
u9b_chk('max_string', @() intmax("int16"));
u9b_chk('max_like', @() intmax('like', int8(3)));
u9b_chk('min_like', @() intmin('like', uint16([1 2 3])));
u9b_chk('max_like_upper', @() intmax('LIKE', int16(1)));
u9b_chk('max_like_double', @() intmax('like', 3));
u9b_chk('min_like_logical', @() intmin('like', true));
u9b_chk('max_like_single', @() intmax('like', single(1)));
u9b_chk('max_value', @() intmax(int8(3)));
u9b_chk('max_double', @() intmax('double'));
u9b_chk('min_bad', @() intmin('foo'));
u9b_chk('max_upper', @() intmax('INT8'));
u9b_chk('max_two', @() intmax('int8', 'int8'));
u9b_chk('max_three', @() intmax('like', int8(1), 3));
u9b_chk('max_like_only', @() intmax('like'));
u9b_chk('neg_min_int8', @() -intmin('int8'));
u9b_chk('abs_min_int8', @() abs(intmin('int8')));
u9b_chk('max_cat', @() [intmax 1.5]);
u9b_chk('class_int64_max', @() class(intmax('int64')));
u9b_chk('class_uint64_max', @() class(intmax('uint64')));
u9b_chk('class_int64_min', @() class(intmin('int64')));
u9b_chk('int64_max_is_saturated', @() intmax('int64') == int64(2^63));
u9b_chk('uint64_max_is_saturated', @() intmax('uint64') == uint64(2^64));
u9b_chk('int64_min_exact', @() intmin('int64') == -2^63);
u9b_chk('int64_max_plus', @() intmax('int64') + 1 == intmax('int64'));
u9b_chk('int64_min_neg', @() -intmin('int64') == intmax('int64'));
u9b_chk('int64_max_to_int32', @() int32(intmax('int64')));
fprintf('CHK|int64_max_below_2p63|%s|div=ADR0355\n', u9b_text(intmax('int64') < 2^63));
fprintf('CHK|int64_max_text|%s|div=ADR0355\n', sprintf('%d', intmax('int64')));
fprintf('CHK|uint64_max_text|%s|div=ADR0355\n', num2str(intmax('uint64')));

% --- 272: x.^0.5 and x.^-1 ----------------------------------------------------------------
corners = [0, -0, Inf, NaN, 1, 2, 4, 0.1, realmin, realmin / 4, 4.9406564584124654e-324, realmax, 1e300];
fprintf('CHK|half_bits|%s|exact\n', sprintf('%08x', typecast(corners .^ 0.5, 'uint32')));
fprintf('CHK|half_eq_sqrt|%d|exact\n', isequal(typecast(corners .^ 0.5, 'uint32'), typecast(sqrt(corners + 0), 'uint32')));
fprintf('CHK|inv_bits|%s|exact\n', sprintf('%08x', typecast(corners .^ -1, 'uint32')));
fprintf('CHK|inv_eq_recip|%d|exact\n', isequal(typecast(corners .^ -1, 'uint32'), typecast(1 ./ corners, 'uint32')));
fprintf('CHK|m0_half|%s|exact\n', sprintf('%08x', typecast((-0) .^ 0.5, 'uint32')));
fprintf('CHK|m0_half_scalar|%s|exact\n', sprintf('%08x', typecast((-0) ^ 0.5, 'uint32')));
fprintf('CHK|m0_inv|%s|exact\n', sprintf('%08x', typecast((-0) .^ -1, 'uint32')));
fprintf('CHK|zero_inv|%s|exact\n', sprintf('%08x', typecast(0 .^ -1, 'uint32')));
fprintf('CHK|realmax_half|%s|exact\n', sprintf('%08x', typecast(realmax .^ 0.5, 'uint32')));
fprintf('CHK|realmax_half_scalar|%s|exact\n', sprintf('%08x', typecast(realmax ^ 0.5, 'uint32')));
fprintf('CHK|tenth_inv_scalar|%s|exact\n', sprintf('%08x', typecast(0.1 ^ -1, 'uint32')));
x = [0.1 0.2 0.3 0.7 1.1 2.9 3.3 10.7 123.456 1e-5 7e7 5e-310];
y = zeros(size(x));
for k = 1:numel(x), y(k) = x(k) ^ 0.5; end
fprintf('CHK|loop_half|%d|exact\n', isequal(typecast(y, 'uint32'), typecast(sqrt(x), 'uint32')));
for k = 1:numel(x), y(k) = x(k) ^ -1; end
fprintf('CHK|loop_inv|%d|exact\n', isequal(typecast(y, 'uint32'), typecast(1 ./ x, 'uint32')));
f = @(t) t .^ 0.5;
g = @(t) t .^ -1;
fprintf('CHK|anon_half|%d|exact\n', isequal(typecast(f(x), 'uint32'), typecast(sqrt(x), 'uint32')));
fprintf('CHK|anon_inv|%d|exact\n', isequal(typecast(g(x), 'uint32'), typecast(1 ./ x, 'uint32')));
fprintf('CHK|vec_half|%d|exact\n', isequal(typecast(x .^ 0.5, 'uint32'), typecast(sqrt(x), 'uint32')));
fprintf('CHK|vec_inv|%d|exact\n', isequal(typecast(x .^ -1, 'uint32'), typecast(1 ./ x, 'uint32')));
fprintf('CHK|scalar_base_vec|%s|exact\n', sprintf('%08x', typecast(4 .^ [0.5 -1 2], 'uint32')));
u9b_chk('neg_half', @() (-4) .^ 0.5);
u9b_chk('neg_half_real_part', @() real((-4) .^ 0.5));
u9b_chk('neginf_half', @() [real((-Inf) .^ 0.5), imag((-Inf) .^ 0.5)]);
u9b_chk('neg_vec_half', @() [-4 4] .^ 0.5);
u9b_chk('neg_vec_eq_sqrt', @() isequal([-4 -2 -0.5 4] .^ 0.5, sqrt([-4 -2 -0.5 4])));
u9b_chk('cplx_half', @() (-4 + 0i) .^ 0.5);
u9b_chk('cplx_half_eq', @() isequal((3 + 4i) .^ 0.5, sqrt(3 + 4i)));
u9b_chk('cplx_inv_eq', @() isequal((2 + 2i) .^ -1, 1 ./ (2 + 2i)));
s = single([0.1 0.2 0.3 2 3 7 1e-3 1e30]);
fprintf('CHK|single_half|%d %s|exact\n', isequal(typecast(s .^ 0.5, 'uint32'), typecast(sqrt(s), 'uint32')), class(s .^ 0.5));
fprintf('CHK|single_inv|%d %s|exact\n', isequal(typecast(s .^ -1, 'uint32'), typecast(1 ./ s, 'uint32')), class(s .^ -1));
fprintf('CHK|single_m0_half|%s|exact\n', sprintf('%08x', typecast(single(-0) .^ 0.5, 'uint32')));
fprintf('CHK|single_m0_inv|%s|exact\n', sprintf('%08x', typecast(single(-0) .^ -1, 'uint32')));
u9b_chk('logical_half', @() true .^ 0.5);
u9b_chk('logical_inv', @() [true false] .^ -1);
u9b_chk('int_half', @() int32([0 1 4 9]) .^ 0.5);
u9b_chk('int_half_mpower', @() int32(4) ^ 0.5);
u9b_chk('int_inv', @() int32(2) .^ -1);
u9b_chk('int_inv_mpower', @() int8(4) ^ -1);
u9b_chk('int_nan', @() int32(2) .^ NaN);
u9b_chk('int_neginf', @() int32(2) .^ -Inf);
u9b_chk('int_inf', @() int32(2) .^ Inf);
u9b_chk('int_zero', @() int32(2) .^ 0);
u9b_chk('int_negzero', @() int32(2) .^ -0);
u9b_chk('int_cuberoot', @() int32(-8) .^ (1/3));
u9b_chk('int_int_neg', @() int32(4) .^ int32(-1));
u9b_chk('dbl_int_neg', @() 4 .^ int8(-1));
u9b_chk('dbl_int_pos', @() 2.5 .^ int8(2));
u9b_chk('neg_dbl_int', @() (-2) .^ int8(3));
u9b_chk('int_square', @() int32([3 -3 50000]) .^ 2);
u9b_chk('int_empty_half', @() int32([]) .^ 0.5);
u9b_chk('power_fn_int', @() power(int8(4), 0.5));
u9b_chk('mpower_fn_int', @() mpower(int8(4), -1));

% --- 299: the floating-only verbs, isoutlier of a single, integral's options ---------------
fns = {'sqrt', 'exp', 'log', 'log2', 'sin', 'atan', 'sinh', 'acosh', 'sind', 'asecd', 'gamma', ...
    'gammaln', 'erf', 'erfinv', 'reallog', 'realsqrt', 'pow2', 'psi', 'det'};
ins = {true, int32(4), 'a', uint8(3), [true false], 0.5};
inn = {'logical', 'int32', 'char', 'uint8', 'mask', 'double'};
for k = 1:numel(fns)
    for j = 1:numel(ins)
        try
            v = feval(fns{k}, ins{j});
            fprintf('CHK|float_%s_%s|%s|exact\n', fns{k}, inn{j}, class(v));
        catch err
            fprintf('CHK|float_%s_%s|%s|exact\n', fns{k}, inn{j}, err.identifier);
        end
    end
end
u9b_chk('sqrt_true', @() sqrt(true));
u9b_chk('sqrt_empty_mask', @() sqrt(true(0)));
u9b_chk('hypot_second', @() hypot(2, true));
u9b_chk('atan2_int', @() atan2(int8(1), int8(1)));
u9b_chk('pow2_two', @() pow2(2, true));
u9b_chk('deg2rad_true', @() deg2rad(true));
u9b_chk('rad2deg_int', @() rad2deg(int16(1)));
u9b_chk('inv_int', @() inv(int32([1 2; 3 4])));
u9b_chk('inv_true', @() inv(true));
u9b_chk('eig_true', @() eig(true));
u9b_chk('svd_int', @() svd(int8(2)));
u9b_chk('rank_true', @() rank(true));
u9b_chk('norm_char', @() norm('a'));
u9b_chk('lu_true', @() lu(true));
u9b_chk('chol_int', @() chol(int32(4)));
u9b_chk('qr_true', @() qr(true));
u9b_chk('expm_true', @() expm(true));
u9b_chk('sqrtm_int', @() sqrtm(int32(4)));
u9b_chk('rcond_true', @() rcond(true));
u9b_chk('besselj_true', @() besselj(0, true));
u9b_chk('abs_true', @() abs(true));
u9b_chk('floor_int', @() floor(int8(3)));
u9b_chk('mod_true', @() mod(true, true));
u9b_chk('power_true', @() true .^ true);
u9b_chk('nthroot_true', @() nthroot(true, 2));
u9b_chk('isoutlier_true', @() isoutlier(true));
u9b_chk('isoutlier_mask', @() isoutlier([true false true false]));
u9b_chk('isoutlier_int', @() isoutlier(int8([1 2 3])));
u9b_chk('isoutlier_char', @() isoutlier('abc'));
u9b_chk('isoutlier_cell', @() isoutlier({1}));
u9b_chk('rmoutliers_int', @() rmoutliers(int8([1 2 3])));
u9b_chk('filloutliers_true', @() filloutliers(true, 'linear'));
u9b_chk('filloutliers_int', @() filloutliers(int8([1 2 3]), 'linear'));
sx = single([1 2 3 4 100 3 2 1]);
[tf, lo, hi, ce] = isoutlier(sx);
u9b_chk('iso_single_tf', @() tf);
u9b_chk('iso_single_lower', @() lo);
u9b_chk('iso_single_upper', @() hi);
u9b_chk('iso_single_center', @() ce);
[tf, lo, hi, ce] = isoutlier(double(sx));
u9b_chk('iso_double_lower', @() lo);
u9b_chk('iso_double_upper', @() hi);
[~, lo, hi, ce] = isoutlier(single(7));
u9b_chk('iso_single_scalar', @() {lo, hi, ce});
[~, lo] = isoutlier(single([1 2; 3 100; 2 2; 1 3]));
u9b_chk('iso_single_matrix', @() lo);
[~, lo, hi, ce] = isoutlier(sx, 'mean');
u9b_chk('iso_single_mean_class', @() {class(lo), class(hi), class(ce)});
[~, lo, hi, ce] = isoutlier(sx, 'quartiles');
u9b_chk('iso_single_quartiles_class', @() {class(lo), class(hi), class(ce)});
[~, lo] = isoutlier(sx, 'movmedian', 3);
u9b_chk('iso_single_moving_class', @() class(lo));
u9b_chk('rmoutliers_single', @() rmoutliers(sx));
u9b_chk('filloutliers_single', @() filloutliers(sx, 'linear'));
u9b_chk('filloutliers_single_center', @() filloutliers(sx, 'center'));
try
    v = integral(@(z) z.^2, 0, 1, 'Vectorized', true);
    fprintf('CHK|integral_vectorized|%s|div=ADR0355\n', u9b_text(v));
catch err
    fprintf('CHK|integral_vectorized|%s|div=ADR0355\n', err.identifier);
end

% --- 307: singular and nearly singular systems --------------------------------------------
% Every operand of an operator is a variable: R2025b folds an operator over literal arrays when it
% reads the file, warns then, and leaves lastwarn empty at the call.
A0 = [1 2; 2 4];
b2 = [1; 2];
Z3 = zeros(3);
U2 = [1 2; 0 0];
L2 = [0 0; 1 2];
O2 = [1 1; 1 1];
D3 = diag([1 0 2]);
S3 = [1 1 0; 1 1 0; 0 0 1];
I2 = [1 Inf; 2 3];
N2 = [1 NaN; 2 3];
T2 = [1 0; 0 1e-17];
UT2 = [1 1; 0 1e-17];
M4 = magic(4);
F2 = [2 1; 1 3];
R32 = [1 2; 2 4; 3 6];
R23 = [1 2 3; 2 4 6];
one3 = ones(3, 1);
one2 = [1; 1];
sS = single([1 0; 0 1e-8]);
sb = single([1; 1]);
u9b_chk('inv_singular', @() inv(A0));
u9b_chk('inv_zeros', @() inv(zeros(3)));
u9b_chk('inv_zero_scalar', @() inv(0));
u9b_chk('inv_upper_singular', @() inv([1 2; 0 0]));
u9b_chk('inv_lower_singular', @() inv([0 0; 1 2]));
u9b_chk('inv_diag_singular', @() inv(diag([1 0 2])));
u9b_chk('inv_upper3_singular', @() inv([1 2 3; 0 0 4; 0 0 5]));
u9b_chk('inv_inf', @() inv([1 Inf; 2 3]));
u9b_chk('inv_nan', @() inv([1 NaN; 2 3]));
u9b_chk('inv_tiny', @() inv([1 0; 0 1e-17]));
u9b_chk('inv_magic4_warns', @() size(inv(magic(4))));
u9b_chk('inv_single_singular', @() inv(single(A0)));
u9b_chk('inv_single_near', @() inv(single([1 0; 0 1e-8])));
u9b_chk('inv_complex_singular', @() inv([1i 2i; 2i 4i]));
u9b_chk('inv_empty', @() inv([]));
u9b_chk('inv_fine', @() inv([2 1; 1 3]));
u9b_chk('mld_singular', @() A0 \ b2);
u9b_chk('mld_fn_singular', @() mldivide(A0, b2));
u9b_chk('mld_zeros', @() Z3 \ one3);
u9b_chk('mld_scalar_zero', @() Z3(1) \ one3(1));
u9b_chk('mld_upper_singular', @() U2 \ b2);
u9b_chk('mld_lower_singular', @() L2 \ b2);
u9b_chk('mld_ones_singular', @() O2 \ b2);
u9b_chk('mld_diag_singular', @() D3 \ one3);
u9b_chk('mld_diag_zero_rhs', @() D3 \ (one3 - [0; 1; 0]));
u9b_chk('mld_upper_zero_rhs', @() U2 \ (b2 - [0; 2]));
u9b_chk('mld_identity_rhs', @() A0 \ eye(2));
u9b_chk('mld_wide_rhs', @() U2 \ reshape(1:6, 3, 2)');
u9b_chk('mld_spd_singular', @() S3 \ one3);
u9b_chk('mld_inf', @() I2 \ one2);
u9b_chk('mld_nan', @() N2 \ one2);
u9b_chk('mld_nan_rhs', @() F2 \ (one2 + [NaN; 0]));
u9b_chk('mld_tiny', @() T2 \ one2);
u9b_chk('mld_upper_tiny', @() UT2 \ one2);
u9b_chk('mld_magic4_warns', @() size(M4 \ [one3; 1]));
u9b_chk('mld_single_singular', @() single(A0) \ single(b2));
u9b_chk('mld_single_near', @() sS \ sb);
u9b_chk('mld_fine', @() F2 \ (b2 + [2; 3]));
u9b_chk('mrd_singular', @() b2' / A0);
u9b_chk('mrd_fn_singular', @() mrdivide(b2', A0));
u9b_chk('mrd_triangular', @() one2' / U2);
u9b_chk('mrd_row', @() (b2' - [0 0]) / A0);
u9b_chk('mpower_singular', @() A0 ^ -1);
u9b_chk('mpower_singular_two', @() A0 ^ -2);
u9b_chk('mld_rect_deficient', @() round(R32 \ [1; 2; 3], 12));
u9b_chk('mld_rect_wide', @() R23 \ b2);
lastwarn('');
v = inv([1 0; 0 1e-17]); %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|near_text|%s %s|exact\n', id, msg);
lastwarn('');
v = inv(A0); %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|singular_text|%s %s|exact\n', id, msg);
lastwarn('');
v = N2 \ one2; %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|nan_text|%s %s|exact\n', id, msg);
warning('off', 'MATLAB:singularMatrix');
u9b_chk('off_inv', @() inv(A0));
u9b_chk('off_mld', @() A0 \ b2);
warning('on', 'MATLAB:singularMatrix');
warning('error', 'MATLAB:singularMatrix');
u9b_chk('error_inv', @() inv(A0));
u9b_chk('error_mld', @() A0 \ b2);
u9b_chk('error_mrd', @() b2' / A0);
warning('on', 'MATLAB:singularMatrix');
warning('error', 'MATLAB:nearlySingularMatrix');
u9b_chk('error_near', @() inv([1 0; 0 1e-17]));
warning('on', 'MATLAB:nearlySingularMatrix');
warning('error', 'MATLAB:illConditionedMatrix');
u9b_chk('error_nan', @() N2 \ one2);
warning('on', 'MATLAB:illConditionedMatrix');
