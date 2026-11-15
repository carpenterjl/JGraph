% record: -noFigureWindows
% Open item 341 (ADR 0377): the complex arithmetic and numeric leftovers after ADR 0370. ++x and
% --x are two signs; uplus and uminus of a string refuse; x .^ 1 keeps the flag only for a written
% constant; cell2mat of numbers, text and logicals is R2025b's cell2mat.m; sortrows and
% unique(..., 'rows') of a flagged array order rows in the complex order; complex products and
% quotients with infinite or NaN parts, the matrix product with an Inf, 0 .^ a complex exponent and
% whole complex powers; any and all of a complex matrix; psi's scalar order, its digits and
% psi(k, Inf); b / D of a singular diagonal D and inv of a singular complex triangle. Probes
% probe1-probe5 and the product and quotient grid in lane 7D's scratch.

% Every operand is a variable: R2025b folds an operator over literals when it reads the file.
a = 3; b = 2; t = true; c = 'a';
xa = complex([3 1 2], 0); x1 = complex(2, 0); xm = complex([3 1; 2 4], 0); e1 = 1;

% --- ++ and -- are two signs ------------------------------------------------------------------
p7d_chk('pp_x', @() ++a);
p7d_chk('mm_x', @() --a);
p7d_chk('pp_char', @() ++c);
p7d_chk('mm_true', @() --t);
p7d_chk('a_pp_b', @() a++b);
p7d_chk('a_mm_b', @() a--b);
p7d_chk('a_pm_b', @() a+-b);
p7d_chk('a_mp_b', @() a-+b);
p7d_chk('mat_pp', @() [a ++b]);
p7d_chk('mat_mm', @() [a --b]);
p7d_chk('mat_pp_sp', @() [a ++ b]);
p7d_chk('mat_ppp', @() [a +++b]);
p7d_chk('ppp_x', @() +++a);
p7d_chk('mmm_x', @() ---a);
p7d_chk('pmpm_x', @() +-+-a);
p7d_chk('pp_paren', @() ++(a));
p7d_chk('x_pow_plus', @() a^+b);
p7d_chk('x_pow_pp', @() a^++b);
p7d_chk('x_pow_mm', @() a^--b);
p7d_chk('x_times_mm', @() a*--b);
p7d_chk('pp_flag', @() ++xa);
p7d_chk('mm_flag', @() --xa);

% --- uplus and uminus of what they refuse ---------------------------------------------------
p7d_chk('uplus_str', @() uplus("a"));
p7d_chk('uplus_strarr', @() uplus(["a" "b"]));
p7d_chk('uplus_cell', @() uplus({1}));
p7d_chk('uplus_char', @() uplus('a'));
p7d_chk('uplus_true', @() uplus(true));
p7d_chk('uminus_str', @() uminus("a"));
p7d_chk('uminus_strop', @() -"a");
p7d_chk('uminus_cell', @() uminus({1}));
p7d_chk('neg_cell', @() -{1});
p7d_chk('neg_struct', @() -struct('a', 1));
p7d_chk('uminus_char', @() uminus('ab'));

% --- x .^ 1 keeps the flag only for a written constant ---------------------------------------
p7d_chk('pow_lit1', @() xa .^ 1);
p7d_chk('pow_var1', @() xa .^ e1);
p7d_chk('pow_ones', @() xa .^ ones(1, 1));
p7d_chk('pow_eye1', @() xa .^ eye(1));
p7d_chk('pow_double1', @() xa .^ double(1));
p7d_chk('pow_2m1', @() xa .^ (2 - 1));
p7d_chk('pow_brk1', @() xa .^ [1]);
p7d_chk('pow_plus1', @() xa .^ +1);
p7d_chk('pow_1p0', @() xa .^ 1.0);
p7d_chk('pow_negneg', @() xa .^ -(-1));
p7d_chk('pow_2d2', @() xa .^ (2 / 2));
p7d_chk('pow_1pow', @() xa .^ (1 ^ 5));
p7d_chk('pow_e1p0', @() xa .^ (e1 + 0));
p7d_chk('pow_true', @() xa .^ true);
p7d_chk('pow_m_ones', @() xm .^ ones(1, 1));
p7d_chk('pow_m_lit', @() xm .^ 1);
p7d_chk('pow_s_var', @() x1 .^ e1);
p7d_chk('mpow_var1', @() xm ^ e1);
p7d_chk('pow_power_fn', @() power(xa, 1));
p7d_chk('pow_power_fn_var', @() power(xa, e1), 'div=ADR0377');
f1 = @(z, p) z .^ p;
p7d_chk('pow_lambda_1', @() f1(xa, 1));
y1 = xa .^ 1;
p7d_chk('pow_bound', @() y1);

% --- cell2mat ------------------------------------------------------------------------------
s1.a = 1; s2.a = 2;
p7d_chk('c2m_col', @() cell2mat({[1 2]; [3 4]}));
p7d_chk('c2m_row', @() cell2mat({[1 2], [3 4]}));
p7d_chk('c2m_2x2', @() cell2mat({1 2; 3 4}));
p7d_chk('c2m_cols', @() cell2mat({[1; 2], [3; 4]}));
p7d_chk('c2m_blocks', @() cell2mat({[1 2], 3; [4 5], 6}));
p7d_chk('c2m_blocks2', @() cell2mat({[1 2; 3 4], [5; 6]; [7 8], 9}));
p7d_chk('c2m_mismatch', @() cell2mat({[1 2], [3 4 5]; 6, 7}));
p7d_chk('c2m_mismatch2', @() cell2mat({[1 2], 3; 4, [5 6]}));
p7d_chk('c2m_int8', @() cell2mat({int8(1), int8(2)}));
p7d_chk('c2m_int8_col', @() cell2mat({int8([1 2]); int8([3 4])}));
p7d_chk('c2m_char', @() cell2mat({'ab'; 'cd'}));
p7d_chk('c2m_char_row', @() cell2mat({'ab', 'cd'}));
p7d_chk('c2m_logical', @() cell2mat({true false; false true}));
p7d_chk('c2m_single', @() cell2mat({single(1); single(2)}));
p7d_chk('c2m_empty', @() cell2mat({}));
p7d_chk('c2m_emptypiece', @() cell2mat({[]}));
p7d_chk('c2m_one', @() cell2mat({[1 2; 3 4]}));
p7d_chk('c2m_with_empty', @() cell2mat({[], [1 2]}));
p7d_chk('c2m_with_empty_col', @() cell2mat({[]; [1 2]}));
p7d_chk('c2m_mixed', @() cell2mat({1, 'a'}));
p7d_chk('c2m_mixed_int', @() cell2mat({int8(1), 2}));
p7d_chk('c2m_mixed_single', @() cell2mat({single(1), 2}));
p7d_chk('c2m_mixed_logical', @() cell2mat({true, 2}));
p7d_chk('c2m_cplx', @() cell2mat({1+2i; 3}));
p7d_chk('c2m_cplx_row', @() cell2mat({[1 2], [3i 4]}));
p7d_chk('c2m_cplx_flag', @() cell2mat({complex(1, 0), 2}));
p7d_chk('c2m_3x2', @() cell2mat({1 2; 3 4; 5 6}));
p7d_chk('c2m_2x3', @() cell2mat({1 2 3; 4 5 6}));
p7d_chk('c2m_3x2_blk', @() cell2mat({[1 1] 2; [3 3] 4; [5 5] 6}));
p7d_chk('c2m_tall_blocks', @() cell2mat({[1;1], [2;2]; 3, 4}));
p7d_chk('c2m_wide_rows', @() cell2mat({[1 2] [3]; [4] [5 6]}));
p7d_chk('c2m_cellcell', @() cell2mat({{1}, {2}}));
p7d_chk('c2m_later_cell', @() cell2mat({1, {2}}));
p7d_chk('c2m_notcell', @() cell2mat([1 2]));
p7d_chk('c2m_col_3', @() cell2mat({[1 2]; [3 4]; [5 6]}));
p7d_chk('c2m_rowcells', @() cell2mat({[1 2 3]; [4 5 6]; [7 8 9]}));
p7d_chk('c2m_big', @() cell2mat(num2cell(magic(4))));
p7d_chk('c2m_empty_rows', @() size(cell2mat({zeros(0, 2); [1 2]})));
p7d_chk('c2m_empty_cols', @() size(cell2mat({zeros(2, 0), [1; 2]})));
p7d_chk('c2m_empty_2x2', @() size(cell2mat(cell(2, 2))));
p7d_chk('c2m_empty_0x3', @() size(cell2mat(cell(0, 3))));
p7d_chk('c2m_struct', @() cell2mat({s1, s2}));
p7d_chk('c2m_struct_col', @() cell2mat({s1; s2}));
p7d_chk('c2m_catfail', @() cell2mat({int8([1 2]), [3 4 5]; 6, 7}));
p7d_chk('c2m_logchar', @() cell2mat({true, 'a'}));
p7d_chk('c2m_str', @() cell2mat({"a", "b"}));
p7d_chk('c2m_struct_num', @() cell2mat({s1, 1}));
p7d_chk('c2m_int_single', @() cell2mat({int8(1), single(2)}));
p7d_chk('c2m_dbl_logical_col', @() cell2mat({1; true}));
p7d_chk('c2m_char_mixed_rows', @() cell2mat({'ab'; 'c'}));
p7d_chk('c2m_empty_char', @() cell2mat({'', 'ab'}));

% --- sortrows and unique(..., 'rows') of a flagged array ----------------------------------------
xr = complex([-3 1; 1 2; -1 0; 2 -5; 1 2], 0);
p7d_chk('urows', @() unique(xr, 'rows'));
p7d_chk('urows_stable', @() unique(xr, 'rows', 'stable'));
[~, ia, ic] = unique(xr, 'rows');
p7d_chk('urows_ia', @() ia);
p7d_chk('urows_ic', @() ic);
[~, ia, ~] = unique(xr, 'rows', 'last');
p7d_chk('urows_last_ia', @() ia);
[~, ia, ic] = unique(xr, 'rows', 'stable');
p7d_chk('urows_stable_ia', @() ia);
p7d_chk('urows_stable_ic', @() ic);
p7d_chk('urows_rows_sorted', @() unique(xr, 'rows', 'sorted'));
p7d_chk('srows', @() sortrows(xr));
p7d_chk('srows_neg1', @() sortrows(xr, -1));
p7d_chk('srows_2', @() sortrows(xr, 2));
p7d_chk('srows_desc', @() sortrows(xr, 'descend'));
p7d_chk('srows_12', @() sortrows(xr, [2 1]));
p7d_chk('srows_cols_desc', @() sortrows(xr, [1 -2]));
[~, k] = sortrows(xr);
p7d_chk('srows_k', @() k);
[~, k] = sortrows(xr, -1);
p7d_chk('srows_k_neg', @() k);
p7d_chk('srows_real', @() sortrows(xr, 'ComparisonMethod', 'real'));
p7d_chk('srows_abs', @() sortrows(xr, 'ComparisonMethod', 'abs'));
p7d_chk('srows_cm_real', @() sortrows(xr, 1, 'ComparisonMethod', 'real'));
p7d_chk('srows_descend_1', @() sortrows(xr, 1, 'descend'));
xr2 = complex([-1 0; 1 0; -1 1], 0);
p7d_chk('srows_tie', @() sortrows(xr2));
p7d_chk('urows_tie', @() unique(xr2, 'rows'));
p7d_chk('srows_col', @() sortrows(complex([-2; 1; -1; 3], 0)));
p7d_chk('urows_col', @() unique(complex([-2; 1; -1; 3; 1], 0), 'rows'));
p7d_chk('urows_nan', @() unique(complex([NaN 1; -1 2; NaN 1], 0), 'rows'));
p7d_chk('srows_nan', @() sortrows(complex([NaN 1; -1 2; 1 1], 0)));
p7d_chk('srows_negzero', @() sortrows(complex([-0 1; 0 0], 0)));
p7d_chk('urows_negzero', @() unique(complex([-0 1; 0 1; 1 -0], 0), 'rows'));
p7d_chk('srows_single', @() sortrows(complex(single([-2 1; 1 0]), 0)));
p7d_chk('urows_single', @() unique(complex(single([-2 1; 1 0; -2 1]), 0), 'rows'));
p7d_chk('srows_empty', @() sortrows(complex(zeros(0, 2), 0)));
p7d_chk('urows_empty', @() unique(complex(zeros(0, 2), 0), 'rows'));
xn = complex([NaN 1; -1 2; 1 1; 2 0; -2 3], 0);
p7d_chk('srows_nan_desc', @() sortrows(xn, -1));
p7d_chk('srows_nan_desc2', @() sortrows(xn, 'descend'));
[~, k] = sortrows(xn, -1);
p7d_chk('srows_nan_desc_k', @() k);
p7d_chk('srows_cell_dir', @() sortrows(xn, [1 2], {'descend', 'ascend'}));
p7d_chk('srows_cplx', @() sortrows([-3 1; 1+1i 2; -1 0; 2 -5]));
p7d_chk('srows_cplx2', @() sortrows([1+1i 2; -1 3; 1-1i 0]));
[~, k] = sortrows([1+1i 2; -1 3; 1-1i 0]);
p7d_chk('srows_cplx2_k', @() k);

% --- complex products and quotients ------------------------------------------------------------
zi = complex(Inf, 1); j1 = 1i; ri = Inf;
p7d_chk('m_infp1_i', @() zi * j1);
p7d_chk('m_infp1_i_el', @() zi .* j1);
p7d_chk('m_vec_el', @() [zi zi 1+1i] .* [j1 complex(Inf, Inf) complex(Inf, Inf)]);
p7d_chk('d_inf_i', @() ri / j1);
p7d_chk('d_inf_i_el', @() ri ./ j1);
p7d_chk('d_vec_el', @() [ri 1i 1+1i] ./ [j1 0 complex(Inf, Inf)]);
p7d_chk('ld_vec_el', @() [j1 0 complex(Inf, Inf)] .\ [ri 1i 1+1i]);
w0 = complex(1e200, 1e200);
p7d_chk('m_overflow', @() w0 .* w0);
p7d_chk('m_overflow_sq', @() w0 .^ 2);
rv = [Inf 1]; cv = [1i; 1];
p7d_chk('mm_inf_vec', @() rv * cv);
cv2 = [complex(0, Inf); 1]; rv2 = [1 1];
p7d_chk('mm_real_cinf', @() rv2 * cv2);
p7d_chk('mm_cinf_real', @() cv2.' * rv2.');
p7d_chk('mm_mat_inf', @() [Inf 1; 1 1] * [1i 1; 1 1i]);
p7d_chk('mm_cc_inf', @() [complex(Inf, 1) 1] * [1i; 1]);
p7d_chk('mm_outer_inf', @() [Inf; 1] * [1i 1]);
p7d_chk('mm_scalar_mat', @() zi * [1i 2]);
p7d_chk('mm_nan', @() [NaN 1] * [1i; 2]);
p7d_chk('mm_finite', @() [1 2; 3 4] * [1i 2; 3 4i]);

% The grid: every complex pair with parts in {0, 1, Inf, -Inf, NaN} (one side may be real), its
% product and quotient as two real parts each, a signed zero read as 0.
pv = [0 1 Inf -Inf NaN];
nv = {'0', '1', 'Inf', 'nInf', 'NaN'};
for i1 = 1:5
    for i2 = 1:5
        za = complex(pv(i1), pv(i2));
        for j1g = 1:5
            for j2g = 1:5
                if pv(i2) == 0 && pv(j2g) == 0
                    continue
                end
                zb = complex(pv(j1g), pv(j2g));
                m = za * zb;
                d = za / zb;
                fprintf('CHK|g_%s_%s_%s_%s|%s|exact\n', nv{i1}, nv{i2}, nv{j1g}, nv{j2g}, ...
                    sprintf('%.17g %.17g / %.17g %.17g', real(m) + 0, imag(m) + 0, real(d) + 0, imag(d) + 0));
            end
        end
    end
end
for i1 = 1:5
    xg = pv(i1);
    for j1g = 1:5
        for j2g = 1:5
            if pv(j2g) == 0
                continue
            end
            zb = complex(pv(j1g), pv(j2g));
            d1 = xg / zb;
            d2 = zb / xg;
            fprintf('CHK|r_%s_%s_%s|%s|exact\n', nv{i1}, nv{j1g}, nv{j2g}, ...
                sprintf('%.17g %.17g / %.17g %.17g', real(d1) + 0, imag(d1) + 0, real(d2) + 0, imag(d2) + 0));
        end
    end
end

% --- powers ----------------------------------------------------------------------------------
z0 = 0; zr = complex(0, 0);
p7d_chk('pow_0_i', @() z0 .^ j1);
p7d_chk('pow_0_1pi', @() z0 .^ (1 + 1i));
p7d_chk('pow_0_m1pi', @() z0 .^ (-1 + 1i));
p7d_chk('pow_0_flag1', @() z0 .^ complex(1, 0));
p7d_chk('pow_0_flag0', @() z0 .^ complex(0, 0));
p7d_chk('pow_0_flagm1', @() z0 .^ complex(-1, 0));
p7d_chk('pow_0_mi', @() z0 .^ (-1i));
p7d_chk('pow_zr_i', @() zr .^ j1);
p7d_chk('pow_0_i_m', @() z0 ^ j1);
p7d_chk('pow_0vec_i', @() [0 1 2] .^ j1);
p7d_chk('pow_negz_i', @() (-0) .^ j1);
p7d_chk('pow_0_vec', @() [0 0] .^ [1i 1+1i]);
p7d_chk('pow_0_infre', @() z0 .^ complex(Inf, 1));
p7d_chk('pow_0_infim', @() z0 .^ complex(1, Inf));
p7d_chk('pow_0_nan', @() z0 .^ complex(NaN, 1));
p7d_chk('pow_i_i', @() j1 .^ j1);
w = 1 + 2i; two = 2; three = 3;
p7d_chk('sq_12i', @() w .^ two);
p7d_chk('sq_12i_m', @() w ^ two);
p7d_chk('sq_12i_mul', @() w .* w);
p7d_chk('cube_12i', @() w .^ three);
p7d_chk('pow10_12i', @() w .^ 10);
p7d_chk('powm2_12i', @() w .^ -2);
p7d_chk('powm3_12i', @() w .^ -3);
p7d_chk('pow2_34i', @() (3.3 + 4.7i) .^ two);
zv = [1+2i 0.1+0.7i -3.3+1e-3i 3.3+4.7i];
p7d_chk('pow2_vec', @() zv .^ two);
p7d_chk('pow3_vec', @() zv .^ three);
p7d_chk('pow4_vec', @() zv .^ 4);
p7d_chk('pow5_vec', @() zv .^ 5);
p7d_chk('pow17_vec', @() zv .^ 17);
p7d_chk('pow100_vec', @() zv .^ 100);
p7d_chk('powm2_vec', @() zv .^ -2);
p7d_chk('pow_n_vec', @() (1+2i) .^ [2 3 4]);
p7d_chk('pow_big1', @() (0.1+0.7i) .^ 1000);
p7d_chk('pow_big2', @() (1+1e-10i) .^ 65537);
p7d_chk('pow_big3', @() (0.6-0.8i) .^ 12345);
p7d_chk('pow_big4', @() (-1.5+0.5i) .^ 12345);
p7d_chk('pow_bigneg', @() (0.6-0.8i) .^ -1000);
p7d_chk('pow_inf_base', @() [complex(Inf, 1) complex(1, Inf)] .^ 3);
p7d_chk('pow_imag_base', @() [1i -1i 2i] .^ 7);
p7d_chk('pow_half', @() w .^ 0.5);

% --- any and all of a complex matrix ------------------------------------------------------------
M = [1i 0; 0 0];
p7d_chk('any_cm', @() any(M));
p7d_chk('any_cm_2', @() any(M, 2));
p7d_chk('any_cm_all', @() any(M, 'all'));
p7d_chk('all_cm', @() all([1i 1; 0 2]));
p7d_chk('all_cm_2', @() all([1i 1; 0 2], 2));
p7d_chk('any_flag', @() any(complex(zeros(2), 0)));
p7d_chk('any_cvec', @() any([0 1i 0]));
p7d_chk('any_ccol', @() any([0; 1i; 0]));
p7d_chk('all_flag', @() all(complex([1 1; 0 1], 0)));
p7d_chk('any_cnan', @() any([NaN*1i 0]));
p7d_chk('any_cempty', @() any(complex(zeros(0, 3), 0)));
p7d_chk('any_nan_re1', @() any(complex(NaN, 1)));
p7d_chk('any_1_nan', @() any(complex(1, NaN)));
p7d_chk('all_0_nan', @() all(complex(0, NaN)));
p7d_chk('all_nan_0', @() all(complex([NaN 1], [0 1])));
p7d_chk('any_nan_vec', @() any([complex(NaN, 0) 1i]));
p7d_chk('any_nan_mat', @() any([complex(0, NaN) 0; 0 complex(NaN, NaN)]));
p7d_chk('all_nan_mat', @() all([complex(0, NaN) 1; 1i complex(NaN, NaN)]));
p7d_chk('any_vecdim', @() any([1i 0; 0 0], [1 2]));
p7d_chk('all_single', @() all(single([1i 1; 0 2])));
p7d_chk('any_cnum', @() any(1i));
p7d_chk('any_czero', @() any(complex(0, 0)));

% --- psi ---------------------------------------------------------------------------------------
p7d_chk('psi_kvec', @() psi([1 2], 3));
p7d_chk('psi_kcol', @() psi([1; 2], 3));
p7d_chk('psi_kmat', @() psi([1 2; 3 4], 3));
p7d_chk('psi_kempty', @() psi([], 3));
p7d_chk('psi_kfrac', @() psi(1.5, 3));
p7d_chk('psi_1', @() psi(1));
p7d_chk('psi_exact', @() psi([1 2 3 10 100 1e-3 1e5]));
p7d_chk('psi_near', @() psi([0.5 1.5]), 'exact', 12);
p7d_chk('psi_0_inf', @() psi(0, Inf));
p7d_chk('psi_inf', @() psi(Inf));
p7d_chk('psi_1_inf', @() psi(1, Inf), 'div=ADR0377');
p7d_chk('psi_2_inf', @() psi(2, Inf), 'div=ADR0377');
p7d_chk('psi_3_inf', @() psi(3, Inf), 'div=ADR0377');
p7d_chk('psi_1_vec_inf', @() psi(1, [1 Inf]), 'div=ADR0377');
p7d_chk('psi_s_inf', @() psi(1, single(Inf)), 'div=ADR0377');
p7d_chk('psi_1_1', @() psi(1, 1));
p7d_chk('psi_1_vals', @() psi(1, [0.5 1 2 3 10]), 'exact', 12);
p7d_chk('psi_2_vals', @() psi(2, [0.5 1 2 3 10]), 'exact', 12);
p7d_chk('psi_1_big', @() psi(1, [1e10 1e100]), 'exact', 12);
p7d_chk('psi_1_nan', @() psi(1, NaN));
p7d_chk('psi_s1', @() psi(single(1)));

% --- b / D of a singular diagonal D, inv of a singular complex triangle ---------------------------
bc = [1; 2];
p7d_chk('sing_rdiv', @() bc.' / zeros(2));
p7d_chk('sing_rdiv_flag', @() bc.' / complex(zeros(2)));
p7d_chk('sing_ldiv', @() zeros(2) \ bc);
p7d_chk('sing_ldiv_flag', @() complex(zeros(2)) \ bc);
p7d_chk('rdiv_real_sing2', @() [1 2; 3 4] / zeros(2));
p7d_chk('rdiv_diag', @() [1 2] / diag([1 0]));
p7d_chk('rdiv_cplx_rhs', @() [1i 2] / zeros(2));
p7d_chk('rdiv_real_sing', @() bc.' / [1 2; 2 4]);
p7d_chk('rdiv_lower', @() bc.' / [1 0; 2 0]);
p7d_chk('inv_flag_zeros', @() inv(complex(zeros(2))));
p7d_chk('inv_zeros', @() inv(zeros(2)));
p7d_chk('inv_flag_zeros3', @() inv(complex(zeros(3))));
p7d_chk('inv_cplx_sing', @() inv([1i 2i; 2i 4i]));
p7d_chk('inv_flag_sing', @() inv(complex([1 2; 2 4], 0)));
p7d_chk('inv_flag_diag0', @() inv(complex([1 0; 0 0], 0)));
p7d_chk('inv_flag_triu0', @() inv(complex([1 2; 0 0], 0)));
p7d_chk('inv_cplx_diag0', @() inv([2i 0; 0 0]));
p7d_chk('inv_cplx_tril0', @() inv([1i 0; 2 0]));
