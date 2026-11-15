% record: -noFigureWindows
% Open item 321 (ADR 0365): the numeric leftovers after ADR 0355. A singular complex \ warns and
% answers; isfloat(true) is false; sqrt(-0) is +0; the reciprocal, degree and special functions
% keep single; trace answers double for a logical, char or integer and refuses a non-square
% matrix in R2025b's words; filloutliers with no fill is minrhs; a matrix to a fractional power is
% mpower.m's eigenvalue or Schur-Pade road; an integer raised with a single, a logical or another
% integer class is MATLAB:mixedClasses. Probes probe1-probe4 (lane 5E's scratch).

% --- singular complex \ -------------------------------------------------------------------
% Every operand of an operator is a variable: R2025b folds an operator over literal arrays when it
% reads the file, warns then, and leaves lastwarn empty at the call.
C2 = [1i 2i; 2i 4i];
b2 = [1; 2];
o2 = [1; 1];
CU = [1i 2; 0 0];
CL = [0 0; 1i 2];
CD = [1i 0; 0 0];
CT = [1 0; 0 1e-17i];
CN = [1 NaN; 2i 3];
CO = [1+1i 1+1i; 1+1i 1+1i];
CS = single(C2);
CI = [1i Inf; 0 1];
CLN = [1i 0; NaN 1];
R2 = [1 2; 2 4];
RU = [1 2; 0 0];
RL = [1 0; 2 0];
RD = [1 0; 0 0];
RT = [1 1; 0 1e-17];
RN = [1 NaN; 2 3];
ci2 = [1i; 2];
ci1 = [1i; 1];
p5e_chk('c_mld', @() C2 \ b2);
p5e_chk('c_mld_ones', @() C2 \ o2);
p5e_chk('c_mld_eye', @() C2 \ eye(2));
p5e_chk('c_mld_upper', @() CU \ b2);
p5e_chk('c_mld_upper_zero_rhs', @() CU \ (b2 - [0; 2]));
p5e_chk('c_mld_lower', @() CL \ b2);
p5e_chk('c_mld_diag', @() CD \ o2);
p5e_chk('c_mld_tiny', @() CT \ o2);
p5e_chk('c_mld_nan', @() CN \ o2);
p5e_chk('c_mld_ones_mat', @() CO \ o2);
p5e_chk('c_mld_single', @() CS \ single(b2));
p5e_chk('c_mld_upper_inf', @() CI \ o2);
p5e_chk('c_mld_lower_nan', @() CLN \ o2);
p5e_chk('c_mrd', @() b2.' / C2);
p5e_chk('c_mrd_upper', @() o2.' / CU);
p5e_chk('c_mld_fn', @() mldivide(C2, b2));
p5e_chk('c_mrd_fn', @() mrdivide(b2.', C2));
p5e_chk('c_mpower_neg', @() C2 ^ -1);
p5e_chk('rc_full', @() R2 \ ci2);
p5e_chk('rc_upper', @() RU \ ci2);
p5e_chk('rc_lower', @() RL \ ci2);
p5e_chk('rc_diag', @() RD \ ci2);
p5e_chk('rc_tiny', @() RT \ ci1);
p5e_chk('rc_nan', @() RN \ ci1);
p5e_chk('rc_single', @() single(RU) \ single(ci2));
p5e_chk('rc_mrd_upper', @() ci2.' / RU);
p5e_chk('rc_mrd_full', @() ci2.' / R2);
p5e_chk('c_scalar_zero', @() (0 + 1i) \ 2);
p5e_chk('c_single_class', @() class(single([2 1i; -1i 3]) \ o2));
p5e_close('c_fine', [2 1i; -1i 3] \ o2, 1e-14);
p5e_close('rc_fine', [2 1; 1 3] \ ci2, 1e-14);
p5e_close('c_single_fine', single([2 1i; -1i 3]) \ single(o2), 1e-6);
lastwarn('');
v = C2 \ b2; %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|c_sing_text|%s %s|exact\n', id, msg);
lastwarn('');
v = CT \ o2; %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|c_near_text|%s %s|exact\n', id, msg);
lastwarn('');
v = CN \ o2; %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('CHK|c_nan_text|%s %s|exact\n', id, msg);
warning('off', 'MATLAB:singularMatrix');
p5e_chk('c_off_mld', @() C2 \ b2);
warning('on', 'MATLAB:singularMatrix');
warning('error', 'MATLAB:singularMatrix');
p5e_chk('c_error_mld', @() C2 \ b2);
p5e_chk('c_error_mrd', @() b2.' / C2);
p5e_chk('rc_error_mld', @() RU \ ci2);
warning('on', 'MATLAB:singularMatrix');

% --- isfloat --------------------------------------------------------------------------------
u9b_chk('isfloat_true', @() isfloat(true));
u9b_chk('isfloat_mask', @() isfloat([true false]));
u9b_chk('isfloat_empty_logical', @() isfloat(true(0)));
u9b_chk('isfloat_int', @() isfloat(int8(1)));
u9b_chk('isfloat_char', @() isfloat('a'));
u9b_chk('isfloat_single', @() isfloat(single(1)));
u9b_chk('isfloat_double', @() isfloat(2));
u9b_chk('isfloat_cplx', @() isfloat(1i));
u9b_chk('isfloat_cell', @() isfloat({1}));
u9b_chk('isfloat_string', @() isfloat("a"));

% --- sqrt(-0) -------------------------------------------------------------------------------
fprintf('CHK|sqrt_m0|%s|exact\n', sprintf('%08x', typecast(sqrt(-0), 'uint32')));
fprintf('CHK|sqrt_m0_vec|%s|exact\n', sprintf('%08x', typecast(sqrt([-0 0 4]), 'uint32')));
fprintf('CHK|sqrt_m0_single|%s|exact\n', sprintf('%08x', typecast(sqrt(single(-0)), 'uint32')));
fprintf('CHK|realsqrt_m0|%s|exact\n', sprintf('%08x', typecast(realsqrt(-0), 'uint32')));
z = sqrt([-0 -4]);
fprintf('CHK|sqrt_m0_negvec|%s %s|exact\n', sprintf('%08x', typecast(real(z), 'uint32')), sprintf('%08x', typecast(imag(z), 'uint32')));
x = -0;
y = zeros(1, 1);
for k = 1:1, y(k) = sqrt(x(k)); end
fprintf('CHK|sqrt_m0_loop|%s|exact\n', sprintf('%08x', typecast(y, 'uint32')));
big = -zeros(1, 40000);
fprintf('CHK|sqrt_m0_long|%d|exact\n', any(typecast(sqrt(big), 'uint32')));

% --- single kept ----------------------------------------------------------------------------
s = single([0.3 0.7 1.5 2.5]);
fnsS = {'asec', 'acsc', 'acot', 'asech', 'acsch', 'acoth', 'sec', 'csc', 'cot', 'sech', 'csch', 'coth', ...
    'asecd', 'acscd', 'acotd', 'secd', 'cscd', 'cotd', 'sind', 'cosd', 'tand', 'asind', 'acosd', 'atand', ...
    'deg2rad', 'rad2deg', 'gammaln', 'erfinv', 'erfcinv', 'erfcx', 'reallog', 'realsqrt', 'pow2', 'psi'};
for k = 1:numel(fnsS)
    v = feval(fnsS{k}, s);
    dv = feval(fnsS{k}, double(s));
    close = isequaln(isnan(double(v)), isnan(dv)) && all(abs(double(v(~isnan(v))) - dv(~isnan(dv))) <= 1e-6 * max(abs(dv(~isnan(dv))), 1));
    fprintf('CHK|single_%s|%s %d|exact\n', fnsS{k}, class(v), close);
end
u9b_chk('atan2d_single', @() class(atan2d(single(1), single(2))));
u9b_chk('atan2d_first', @() class(atan2d(single(1), 2)));
u9b_chk('atan2d_second', @() class(atan2d(1, single(2))));
u9b_chk('pow2_two_single', @() pow2(single(1.5), single(3)));
u9b_chk('pow2_two_first', @() pow2(single(1.5), 3));
u9b_chk('pow2_two_second', @() pow2(1.5, single(3)));
u9b_chk('psi_k_x_single', @() class(psi(1, single(2))));
u9b_chk('psi_k_single', @() class(psi(single(1), 2)));
u9b_chk('sind_single_exact', @() sind(single([0 30 90 180 360])));
u9b_chk('cosd_single_exact', @() cosd(single([0 60 90 270])));
u9b_chk('tand_single_exact', @() tand(single([0 45 90 135 180])));
u9b_chk('cotd_single_exact', @() cotd(single([0 45 90 180])));
u9b_chk('deg2rad_single_v', @() deg2rad(single(180)));
u9b_chk('rad2deg_single_v', @() rad2deg(single(pi)));
u9b_chk('pow2_single_vals', @() pow2(single([-1 0 1 10 -149 128])));
u9b_chk('erfinv_single_edges', @() erfinv(single([2 1 -1 0])));
u9b_chk('erfcinv_single_edges', @() erfcinv(single([0 1 2])));
u9b_chk('reallog_single_vals', @() reallog(single([0 1])));
u9b_chk('realsqrt_single_vals', @() realsqrt(single([0 4])));
u9b_chk('reallog_single_empty', @() reallog(single([])));
u9b_chk('acot_single_cplx', @() class(acot(single(1i + 2))));
u9b_chk('asecd_single_cplx', @() class(asecd(single(1i + 2))));
u9b_chk('sec_single_cplx', @() class(sec(single(1 + 1i))));
u9b_chk('sind_single_mixed', @() class(sind([single(1) 2])));
u9b_chk('asec_single_complex_class', @() class(asec(single(0.5))));

% --- trace ----------------------------------------------------------------------------------
u9b_chk('trace_true', @() trace(true));
u9b_chk('trace_mask', @() trace([true false; false true]));
u9b_chk('trace_int', @() trace(int8([100 2; 3 100])));
u9b_chk('trace_uint8', @() trace(uint8([200 0; 0 200])));
u9b_chk('trace_int_scalar', @() trace(int16(7)));
u9b_chk('trace_char', @() trace('ab'));
u9b_chk('trace_char_one', @() trace('a'));
u9b_chk('trace_single', @() trace(single([1 2; 3 4])));
u9b_chk('trace_single_scalar', @() trace(single(2.5)));
u9b_chk('trace_cplx', @() trace([1i 2; 3 4]));
u9b_chk('trace_empty_logical', @() trace(true(0)));
u9b_chk('trace_empty', @() trace([]));
u9b_chk('trace_rect', @() trace([1 2 3]));
u9b_chk('trace_nd', @() trace(ones(2, 2, 2)));
u9b_chk('trace_double', @() trace(magic(3)));

% --- filloutliers counts first --------------------------------------------------------------
u9b_chk('fill_true', @() filloutliers(true));
u9b_chk('fill_dbl', @() filloutliers(1:5));
u9b_chk('fill_none', @() filloutliers());
u9b_chk('fill_int', @() filloutliers(int8([1 2 3])));
u9b_chk('fill_cell', @() filloutliers({1}));
u9b_chk('fill_char', @() filloutliers('abc'));

% --- a matrix to a fractional power ---------------------------------------------------------
P1 = [4 0; 0 9];
P2 = [4 1; 0 9];
P3 = [2 1; 1 3];
P4 = [1 2; 3 4];
P5 = [0 1; 0 0];
P6 = [4 1; 0 4];
u9b_chk('mp_diag_half', @() P1 ^ 0.5);
u9b_chk('mp_fn', @() mpower(P1, 0.5));
u9b_chk('mp_diag_15', @() P1 ^ 1.5);
u9b_chk('mp_nilp_half', @() P5 ^ 0.5);
u9b_chk('mp_jordan_half', @() P6 ^ 0.5);
u9b_chk('mp_jordan_15', @() P6 ^ 1.5);
u9b_chk('mp_single', @() single(P1) ^ 0.5);
u9b_chk('mp_single_exp', @() P1 ^ single(0.5));
u9b_chk('mp_neg_eig', @() [-4 0; 0 9] ^ 0.5);
u9b_chk('mp_cplx_base_diag', @() [4i 0; 0 9] ^ 0.5);
u9b_chk('mp_logical', @() [true false; false true] ^ 0.5);
u9b_chk('mp_zero_half', @() zeros(2) ^ 0.5);
u9b_chk('mp_zero_neghalf', @() zeros(2) ^ -0.5);
u9b_chk('mp_neg_zero_eig', @() [0 0; 0 4] ^ -0.5);
u9b_chk('mp_empty_half', @() zeros(0) ^ 0.5);
u9b_chk('mp_nan', @() [NaN 0; 0 1] ^ 0.5);
u9b_chk('mp_inf_entry', @() [Inf 0; 0 1] ^ 0.5);
u9b_chk('mp_nan_gen', @() [1 2; NaN 4] ^ 0.5);
u9b_chk('mp_inf_exp', @() P3 ^ Inf);
u9b_chk('mp_nan_exp', @() P3 ^ NaN);
u9b_chk('mp_gen_inf', @() P4 ^ Inf);
u9b_chk('mp_rect', @() [1 2; 3 4; 5 6] ^ 0.5);
u9b_chk('mp_nd', @() ones(2, 2, 2) ^ 0.5);
u9b_chk('mp_int_frac', @() int8(P1) ^ 0.5);
u9b_chk('mp_int_square', @() int8(P1) ^ 2);
u9b_chk('mp_diag_complex_exp', @() P1 ^ (0.5 + 1i));
p5e_close('mp_upper_half', P2 ^ 0.5, 1e-12);
p5e_close('mp_spd_half', P3 ^ 0.5, 1e-12);
p5e_close('mp_gen_half', P4 ^ 0.5, 1e-12);
p5e_close('mp_spd_15', P3 ^ 1.5, 1e-12);
p5e_close('mp_spd_third', P3 ^ (1/3), 1e-12);
p5e_close('mp_gen_third', P4 ^ (1/3), 1e-12);
p5e_close('mp_spd_negh', P3 ^ -0.5, 1e-12);
p5e_close('mp_gen_25', P4 ^ 2.5, 1e-12);
p5e_close('mp_gen_m15', P4 ^ -1.5, 1e-12);
p5e_close('mp_upper_15', P2 ^ 1.5, 1e-12);
p5e_close('mp_lower_half', [4 0; 1 9] ^ 0.5, 1e-12);
p5e_close('mp_herm_neg_eig', [1 2; 2 1] ^ 0.5, 1e-12);
p5e_close('mp_rot_real', [3 -1; 1 3] ^ 0.5, 1e-12);
p5e_close('mp_pos_nonsym', [4 1; 2 3] ^ 0.5, 1e-12);
p5e_close('mp_cplx_half', [1i 2; 3 4] ^ 0.5, 1e-12);
p5e_close('mp_cplx_herm_half', [2 1i; -1i 3] ^ 0.5, 1e-12);
p5e_close('mp_skew_half', [0 1; -1 0] ^ 0.5, 1e-12);
p5e_close('mp_m3_gen', [4 1 0; 1 5 2; 0 3 6] ^ 0.5, 1e-12);
p5e_close('mp_m3_spd', [4 1 0; 1 4 1; 0 1 4] ^ 0.5, 1e-12);
p5e_close('mp_m3_def', [2 1 0; 0 2 1; 0 0 2] ^ 0.5, 1e-12);
p5e_close('mp_m3_def_15', [2 1 0; 0 2 1; 0 0 2] ^ 1.5, 1e-12);
p5e_close('mp_m3_def_neg', [2 1 0; 0 2 1; 0 0 2] ^ -0.5, 1e-12);
p5e_close('mp_m3_def_m25', [2 1 0; 0 2 1; 0 0 2] ^ -2.5, 1e-12);
p5e_close('mp_m3_def_third', [2 1 0; 0 2 1; 0 0 2] ^ (1/3), 1e-12);
p5e_close('mp_m3_def_full', [3 1 1; -1 1 0; 0 0 2] ^ 0.5, 1e-6);
p5e_close('mp_m4_def', [1 1 0 0; 0 1 1 0; 0 0 1 1; 0 0 0 1] ^ 0.3, 1e-12);
p5e_close('mp_m2_def_rot', [3 1; -1 1] ^ 0.5, 1e-7);
p5e_close('mp_m2_def_third', [3 1; -1 1] ^ (1/3), 1e-7);
p5e_close('mp_triu_def_cplx', [-4 1; 0 -4] ^ 0.5, 1e-12);
p5e_close('mp_triu_def_m', [4 1 2; 0 4 3; 0 0 9] ^ 0.5, 1e-12);
p5e_close('mp_single_gen', single(P4) ^ 0.5, 1e-6);
p5e_close('mp_char', ['ab'; 'cd'] ^ 0.5, 1e-12);
p5e_close('mp_complex_exp', P4 ^ (1 + 1i), 1e-12);
p5e_close('mp_ci_neg', [1i 2; 3 4] ^ -2, 1e-12);
u9b_chk('mp_ci_zero', @() [1i 2; 3 4] ^ 0);
u9b_chk('mp_ci_zero_nan', @() [1i NaN; 3 4] ^ 0);
u9b_chk('mp_ci_three', @() [1i 2; 3 4] ^ 3);
u9b_chk('mp_ci_square', @() [1i 2; 3 4] ^ 2);
u9b_chk('mp_ci_sing_neg', @() C2 ^ -1);
u9b_chk('mp_ci_single', @() single([1i 2; 3 4]) ^ 2);
lastwarn('');
v = R2 ^ 0.5; %#ok<NASGU>
[~, id] = lastwarn;
fprintf('CHK|mp_sing_no_warning|%s|exact\n', id);

% --- an integer with a single, a logical or another integer class ---------------------------
u9b_chk('mix_int_single', @() int32(4) .^ single(0.5));
u9b_chk('mix_int8_true', @() int8(2) .^ true);
u9b_chk('mix_int8_single_whole', @() int8(2) .^ single(2));
u9b_chk('mix_single_int8', @() single(2) .^ int8(2));
u9b_chk('mix_true_int8', @() true .^ int8(2));
u9b_chk('mix_int8_int16', @() int8(2) .^ int16(2));
u9b_chk('mix_mpower_int_single', @() int8(2) ^ single(2));
u9b_chk('mix_mpower_int_true', @() int8(2) ^ true);
u9b_chk('mix_mpower_true_int', @() true ^ int8(2));
u9b_chk('mix_power_fn', @() power(int8(2), true));
u9b_chk('mix_vec', @() int8([1 2]) .^ single(2));
u9b_chk('mix_vec_true', @() int8([1 2]) .^ [true false]);
u9b_chk('mix_empty', @() int8([]) .^ single(2));
u9b_chk('mix_int_single_neg', @() int8(2) .^ single(-1));
u9b_chk('mix_int8_char', @() int8(2) .^ 'a');
u9b_chk('mix_char_int8', @() 'a' .^ int8(1));
u9b_chk('mix_int_dbl_whole', @() int8(2) .^ 2);
u9b_chk('mix_int_same', @() int8(2) .^ int8(3));
