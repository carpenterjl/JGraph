% record: -noFigureWindows
% Open items 340 and 341 (ADR 0379): max, min, rem, mod, bitand, bitor, bitxor, idivide and the colon
% with operands of mixed classes; the logical operators, the comparisons and xor with empties,
% NaN, complex numbers and non-numeric operands; NaN in logical, in a condition and beside && and ||;
% a uniform arrayfun or cellfun whose answers mix classes, and the class arrayfun hands its function.
% Probes probe1-probe4 (lane 7C's scratch). Every operand is a variable: R2025b folds an operator
% over literals when it reads the file.

names = {'d', 'da', 'd0', 'dc', 's', 'sa', 's0', 'i8', 'i8a', 'i80', 'i16', 'u8a', 't', 'ta', 't0', 'c', 'ca', 'z'};
vals = {2, [2 3], [], [2; 3], single(2), single([2 3]), single([]), int8(2), int8([2 3]), int8([]), ...
        int16(2), uint8([2 3]), true, [true false], true(0), 'a', 'ab', 1+2i};
fns = {'max', @(a, b) max(a, b); 'min', @(a, b) min(a, b); 'rem', @(a, b) rem(a, b); 'mod', @(a, b) mod(a, b); ...
       'bitand', @(a, b) bitand(a, b); 'bitor', @(a, b) bitor(a, b); 'bitxor', @(a, b) bitxor(a, b); ...
       'idivide', @(a, b) idivide(a, b); 'colon', @(a, b) a:b; ...
       'and', @(a, b) a & b; 'or', @(a, b) a | b; 'xor', @(a, b) xor(a, b); 'eq', @(a, b) a == b; ...
       'lt', @(a, b) a < b; 'ne', @(a, b) a ~= b};
for f = 1:size(fns, 1)
    for i = 1:numel(vals)
        for j = 1:numel(vals)
            % max and min of a complex number and a non-integer are not this fixture's (ADR 0379, Still open).
            if any(strcmp(fns{f, 1}, {'max', 'min'})) && (strcmp(names{i}, 'z') || strcmp(names{j}, 'z')) ...
                    && ~isinteger(vals{i}) && ~isinteger(vals{j})
                continue;
            end
            a = vals{i};
            b = vals{j};
            g = fns{f, 2};
            p7c_chk(sprintf('g_%s_%s_%s', fns{f, 1}, names{i}, names{j}), @() g(a, b), false);
        end
    end
end

% --- the refusals' words ----------------------------------------------------------------------
i8 = int8(2); i8a = int8([2 3]); i16 = int16(2); da = [2 3]; t = true; s = single(2); z = 1+2i;
p7c_chk('w_max_mixed', @() max(i8, da));
p7c_chk('w_min_mixed', @() min(i8, t));
p7c_chk('w_max_cplx', @() max(i8, z));
p7c_chk('w_max_size', @() max(da, [2; 3; 4; 5].'));
p7c_chk('w_rem_mixed', @() rem(i8, s));
p7c_chk('w_mod_mixed', @() mod(i16, i8));
p7c_chk('w_rem_cplx', @() rem(2, z));
p7c_chk('w_bitand_mixed', @() bitand(i8, t));
p7c_chk('w_bitor_mixed', @() bitor(i8, i16));
p7c_chk('w_bitxor_mixed', @() bitxor(i8, da));
p7c_chk('w_bitand_notnum', @() bitand('a', 1));
p7c_chk('w_bitor_notint', @() bitor(s, 1));
p7c_chk('w_bitxor_size', @() bitxor(i8a, int8([1 2 3])));
p7c_chk('w_bitand_cplx', @() bitand(z, 1));
p7c_chk('w_idivide_mixed', @() idivide(i8, s));
p7c_chk('w_idivide_one', @() idivide(2, 3));
p7c_chk('w_idivide_dim', @() idivide(i8a, int8([1 2 3])));
p7c_chk('w_idivide_cplx', @() idivide(i8, z));
p7c_chk('w_colon_mixed', @() i8:i16);
p7c_chk('w_colon_cplx', @() 1:z);
p7c_chk('w_and_cplx', @() z & 1);
p7c_chk('w_lt_cplxint', @() i8 < z);

% --- max and min of two arrays --------------------------------------------------------------------
x = [3 NaN 1]; y = [2 2 NaN];
p7c_chk('max_omit3', @() max(x, y, 'omitnan'));
p7c_chk('max_incl3', @() max(x, y, 'includenan'));
p7c_chk('min_incl3', @() min(x, y, 'includenan'));
p7c_chk('max_i8_nan_incl', @() max(i8, NaN, 'includenan'));
p7c_chk('max_i8_nan', @() max(i8, NaN));
p7c_chk('max_i8_inf', @() max(i8, Inf));
p7c_chk('max_i8_frac', @() max(i8, 2.5));
p7c_chk('max_word2', @() max(x, 'omitnan'));
p7c_chk('max_word2_incl', @() max(x, 'includenan'));
p7c_chk('max_str_all', @() max(x, "all"));
p7c_chk('max_al', @() max(x, 'al'));
p7c_chk('max_linear2', @() max(x, 'linear'));
p7c_chk('max_A_all', @() max(x, 'All'));
p7c_chk('max_t_w', @() max(x, 'x'));
p7c_chk('min_c_all', @() min(x, 'a'));
p7c_chk('min_ca', @() min(x, 'ab'));
p7c_chk('max_i8_expand', @() max(int8([1 5]), int8([3; 4])));
p7c_chk('max_nd', @() size(max(ones(2, 1, 3), ones(1, 2))));
p7c_chk('max_s_nan', @() max(single(NaN), 1));
p7c_chk('max_nan_nan', @() max(NaN, NaN));
p7c_chk('min_t_f', @() min(true, false));
p7c_chk('min_t_2', @() min(true, 2));
p7c_chk('max_c_c', @() max('a', 'b'));
p7c_chk('max_ca_d', @() max('ab', 1));
p7c_chk('max_c_i8', @() max('a', int8(1)));
p7c_chk('max_i8_neg', @() max(int8(-5), -200));
p7c_chk('max_u8_neg', @() max(uint8(5), -200));
p7c_chk('max_u8_big', @() max(uint8(5), 300));
p7c_chk('min_i8_frac', @() min(int8(3), 2.5));
p7c_chk('min_i8_frac2', @() min(int8(3), 2.4));
p7c_chk('max_s_frac', @() max(single(1), 1.1));
p7c_chk('max_2out', @() p7c_max2());
p7c_chk('max_d_e_dim', @() max([1 2], [], 2));
p7c_chk('max_e_e', @() max([], []));
p7c_chk('max_e10', @() max(zeros(1, 0), 5));
p7c_chk('max_e10_e01', @() max(zeros(1, 0), zeros(0, 1)));
p7c_chk('max_struct', @() max(struct('a', 1), 1));
p7c_chk('max_cell', @() max({1}, 1));
p7c_chk('max_str', @() max("a", 1));
p7c_chk('max_i8_cplx0', @() max(i8, complex(1, 0)));
p7c_chk('max_cplx_d', @() max(1+2i, 3));
p7c_chk('max_omit_i8', @() max(int8([1 2]), int8([2 1]), 'omitnan'));
p7c_chk('max_reduce', @() max(int8([1 5 3])));
p7c_chk('max_reduce_dim', @() max(int8([1 5 3]), [], 2));
p7c_chk('max_reduce_omit', @() max([1 NaN 3], [], 'omitnan'));

% --- rem and mod --------------------------------------------------------------------------------
p7c_chk('rem_i8_0', @() rem(int8(5), int8(0)));
p7c_chk('mod_i8_0', @() mod(int8(5), int8(0)));
p7c_chk('rem_neg', @() rem(int8(-5), int8(3)));
p7c_chk('mod_neg', @() mod(int8(-5), int8(3)));
p7c_chk('mod_u8', @() mod(uint8(5), 3));
p7c_chk('rem_i8_nan', @() rem(int8(7), NaN));
p7c_chk('mod_i8_nan', @() mod(int8(7), NaN));
p7c_chk('mod_i8_inf', @() mod(int8(7), Inf));
p7c_chk('rem_i8_inf', @() rem(int8(7), Inf));
p7c_chk('mod_i8_ninf', @() mod(int8(7), -Inf));
p7c_chk('mod_d_inf', @() mod([7 -7 0], Inf));
p7c_chk('mod_d_ninf', @() mod([7 -7 0], -Inf));
p7c_chk('rem_d_inf', @() rem([7 -7 Inf], Inf));
p7c_chk('rem_d_0', @() rem([7 -7], 0));
p7c_chk('mod_d_0', @() mod([7 -7], 0));
p7c_chk('rem_t_t', @() rem(true, true));
p7c_chk('mod_t_2', @() mod(true, 2));
p7c_chk('rem_ca_2', @() rem('ab', 2));
p7c_chk('rem_s_d', @() rem(single(7.5), 2));
p7c_chk('mod_s_neg', @() mod(single(7.5), -2));
p7c_chk('rem_i8_frac', @() rem(int8(7), 2.6));
p7c_chk('mod_i8_frac', @() mod(int8(7), 2.6));
p7c_chk('rem_i8_dneg', @() rem(int8(7), -2.5));
p7c_chk('mod_i8_dneg', @() mod(int8(7), -2.5));
p7c_chk('mod_i8a_d', @() mod(int8([7 -7 100]), 3));
p7c_chk('rem_d_i8', @() rem(7.5, int8(2)));
p7c_chk('mod_d_i8', @() mod(-7.5, int8(2)));
p7c_chk('mod_nd', @() size(mod(ones(2, 1, 3), [2 3])));
p7c_chk('mod_expand', @() mod([5 7], [2; 3]));
p7c_chk('rem_cell', @() rem({1}, 2));
p7c_chk('rem_e_e', @() rem([], []));
p7c_chk('mod_e10_s', @() mod(zeros(1, 0), 3));
p7c_chk('mod_i8_cplx0', @() mod(int8(2), complex(1, 0)));
p7c_chk('mod_cplx0', @() mod(complex(5, 0), 3));

% --- the bit verbs ----------------------------------------------------------------------------
p7c_chk('bitand_da_dc', @() bitand([1 2], [3; 1]));
p7c_chk('bitand_t_3', @() bitand(true, 3));
p7c_chk('bitand_neg', @() bitand(-1, 1));
p7c_chk('bitand_i8neg', @() bitand(int8(-1), int8(1)));
p7c_chk('bitor_i8neg', @() bitor(int8(-128), int8(1)));
p7c_chk('bitxor_i8neg', @() bitxor(int8(-1), int8(5)));
p7c_chk('bitand_frac', @() bitand(1.5, 1));
p7c_chk('bitand_i8_d_frac', @() bitand(int8(3), 1.5));
p7c_chk('bitand_i8a_col', @() bitand(int8([1 3]), int8([1; 2])));
p7c_chk('bitor_type', @() bitor(1, 2, 'uint8'));
p7c_chk('bitand_str', @() bitand("a", 1));
p7c_chk('bitand_s_s', @() bitand(single(1), single(3)));
p7c_chk('bitand_i8_i8a', @() bitand(int8(3), int8([1 2])));
p7c_chk('bitand_u8_d', @() bitand(uint8(255), 15));
p7c_chk('bitand_t_t', @() bitand(true, true));
p7c_chk('bitand_e_t', @() bitand([], true));
p7c_chk('bitsz_1', @() bitand([1 2], [1 2 3]));
p7c_chk('bitsz_2', @() bitand([1 2], [1; 2; 3]));
p7c_chk('bitsz_3', @() bitand(int8([1 2]), int8([1 2 3])));
p7c_chk('bitsz_4', @() bitand(zeros(1, 0), [1 2]));
p7c_chk('bitsz_5', @() bitand(zeros(0, 1), [1 2]));
p7c_chk('bitsz_6', @() bitand(zeros(2, 0), [1 2 3]));
p7c_chk('bitsz_7', @() bitand([], [1 2]));
p7c_chk('bitsz_9', @() bitand(int8([1 2]), int8([])));
p7c_chk('bitsz_10', @() bitand(zeros(1, 0), zeros(0, 1)));
p7c_chk('bitsz_11', @() bitand(int8([1 2]), [1 2 3]));
p7c_chk('bitsz_12', @() bitand(int8([1 2]), single([1 2 3])));
p7c_chk('bitsz_13', @() bitand(single([1 2]), [1 2 3]));
p7c_chk('bitsz_14', @() bitand(ones(2, 2), ones(3, 3)));
p7c_chk('bitsz_16', @() bitor(true(1, 2), true(1, 3)));
p7c_chk('bitsz_17', @() bitxor(uint8([1 2]), uint8(zeros(1, 0))));

% --- idivide ------------------------------------------------------------------------------------
p7c_chk('idivide_i8a_col', @() idivide(int8([7 9]), int8([2; 3])));
p7c_chk('idivide_i80_i8', @() idivide(int8([]), int8(2)));
p7c_chk('idivide_e_i8', @() idivide([], int8(2)));
p7c_chk('idivide_i8_e', @() idivide(int8(2), []));
p7c_chk('idivide_t_i8', @() idivide(true, int8(2)));
p7c_chk('idivide_i8_0', @() idivide(int8(7), int8(0)));
p7c_chk('idivide_i8_neg0', @() idivide(int8(-7), int8(0)));
p7c_chk('idivide_i8_00', @() idivide(int8(0), int8(0)));
p7c_chk('idivide_floor', @() idivide(int8(-7), int8(2), 'floor'));
p7c_chk('idivide_ceil', @() idivide(int8([-7 7]), int8(2), 'ceil'));
p7c_chk('idivide_round', @() idivide(int8([-7 7]), int8(2), 'round'));
p7c_chk('idivide_fl', @() idivide(int8(-7), int8(2), 'fl'));
p7c_chk('idivide_bad', @() idivide(int8(7), int8(2), 'x'));
p7c_chk('idivide_i8_d_frac', @() idivide(int8(7), 2.5));
p7c_chk('idivide_d_i8_frac', @() idivide(7.5, int8(2)));
p7c_chk('idivide_d0', @() idivide(7, int8(0)));

% --- the colon --------------------------------------------------------------------------------
p7c_chk('colon3_i8_d_i16', @() int8(1):2:int16(5));
p7c_chk('colon3_i8_d_d', @() int8(1):2:5);
p7c_chk('colon3_d_i8_d', @() 1:int8(2):5);
p7c_chk('colon3_i8_s_i8', @() int8(1):single(2):int8(5));
p7c_chk('colon3_i8_t_i8', @() int8(1):true:int8(5));
p7c_chk('colon3_s_d_d', @() single(1):2:5);
p7c_chk('colon3_t_d_d', @() true:2:5);
p7c_chk('colon3_c_d_c', @() 'a':2:'e');
p7c_chk('colon3_i8_i8_i16', @() int8(1):int8(1):int16(5));
p7c_chk('colon3_u8_i8_u8', @() uint8(1):int8(1):uint8(5));
p7c_chk('colon_t_t', @() true:true);
p7c_chk('colon_t_d', @() true:3);
p7c_chk('colon_s_d', @() single(1):3);
p7c_chk('colon_i8_c', @() int8(97):'c');
p7c_chk('colon_s_c', @() single(97):'c');
p7c_chk('colon_t_c', @() true:'c');
p7c_chk('colon_d_c', @() 97:'c');
p7c_chk('colon_i8_d_c', @() int8(97):1:'c');
p7c_chk('colon_d_i8_c', @() 97:int8(1):'c');
p7c_chk('colon_c_i8_c', @() 'a':int8(1):'c');
p7c_chk('colon_c_s_c', @() 'a':single(1):'c');
p7c_chk('colon_c_t_c', @() 'a':true:'c');
p7c_chk('colon_i8_t0', @() int8(1):true(0));
p7c_chk('colon_e_i8', @() []:int8(3));
p7c_chk('colon_i80_d', @() int8([]):3);
p7c_chk('colon_z_i8', @() (1+2i):int8(3));
p7c_chk('colon_i8_z0', @() int8(1):complex(3, 0));
p7c_chk('colon_s_z0', @() single(1):complex(3, 0));
p7c_chk('colon_fn', @() colon(int8(1), int16(3)));
p7c_chk('colon_fn3', @() colon(int8(1), single(1), int8(3)));
p7c_chk('colon_u8_neg', @() uint8(3):-1:1);
p7c_chk('colon_i8_i8step_s', @() int8(1):int8(1):single(3));
p7c_chk('colon_s0_s', @() single([]):3);
p7c_chk('colon_d0_d0_s0', @() []:1:single([]));

% --- NaN where a logical is needed ------------------------------------------------------------
n3 = [1 0 NaN]; n = NaN; ns = single(NaN); n2 = [NaN 1]; ne = zeros(1, 0);
p7c_chk('nan_and_n3_1', @() n3 & 1);
p7c_chk('nan_and_1_n3', @() 1 & n3);
p7c_chk('nan_and_n_0', @() n & 0);
p7c_chk('nan_and_0_n', @() 0 & n);
p7c_chk('nan_and_n_e', @() n & ne);
p7c_chk('nan_and_ns_1', @() ns & 1);
p7c_chk('nan_and_fn', @() and(n3, 1));
p7c_chk('nan_or_n2_0', @() n2 | 0);
p7c_chk('nan_or_0_n2', @() 0 | n2);
p7c_chk('nan_or_fn', @() or(n2, 0));
p7c_chk('nan_or_n_1', @() n | 1);
p7c_chk('nan_xor_n_1', @() xor(n, 1));
p7c_chk('nan_xor_1_n', @() xor(1, n));
p7c_chk('nan_not_n', @() ~n);
p7c_chk('nan_not_n3', @() ~n3);
p7c_chk('nan_not_fn', @() not(n));
p7c_chk('nan_not_ns', @() ~ns);
p7c_chk('nan_logical_n2', @() logical([1 NaN]));
p7c_chk('nan_logical_n', @() logical(n));
p7c_chk('nan_logical_ns', @() logical(ns));
p7c_chk('nan_logical_snan', @() logical(single([1 NaN])));
p7c_chk('nan_andand_n_1', @() n && 1);
p7c_chk('nan_andand_1_n', @() 1 && n);
p7c_chk('nan_andand_0_n', @() 0 && n);
p7c_chk('nan_andand_n_0', @() n && 0);
p7c_chk('nan_oror_n_0', @() n || 0);
p7c_chk('nan_oror_1_n', @() 1 || n);
p7c_chk('nan_oror_0_n', @() 0 || n);
p7c_chk('nan_andand_arr', @() [1 NaN] && 1);
p7c_chk('nan_any', @() any(n3));
p7c_chk('nan_all', @() all(n3));
p7c_chk('nan_nnz', @() nnz(n3));
p7c_chk('nan_find', @() find(n3));
p7c_chk('nan_eq', @() n3 == 1);
p7c_chk('nan_i8_and_n', @() int8(1) & n);
p7c_chk('nan_and_n3_t', @() n3 & true);
p7c_chk('nan_and_n3_ta', @() n3 & [true true false]);
p7c_chk('nan_and_n_i8', @() n & int8(0));
p7c_chk('nan_and_c_n', @() 'a' & n);
p7c_chk('nan_and_e_n', @() [] & n);
p7c_chk('nan_and_ne_n', @() zeros(1, 0) & n);
p7c_chk('nan_and_n_n', @() n & n);
p7c_chk('nan_and_ns_s', @() ns & single(1));
p7c_chk('nan_xor_n3_0', @() xor(n3, 0));
p7c_chk('nan_xor_e_n', @() xor([], n));
p7c_chk('nan_and_inf', @() Inf & 1);
p7c_chk('nan_not_inf', @() ~[Inf -Inf]);
p7c_chk('nan_if', @() p7c_if(n2));
p7c_chk('nan_if_s', @() p7c_if(n));
p7c_chk('nan_if_e', @() p7c_if(zeros(1, 0)));
p7c_chk('nan_if_0nan', @() p7c_if([0 NaN]));
p7c_chk('nan_if_nan0', @() p7c_if([NaN 0]));
p7c_chk('nan_if_snan', @() p7c_if(single(NaN)));
p7c_chk('nan_if_i8', @() p7c_if(int8(0)));
p7c_chk('nan_if_cnan', @() p7c_if(complex(NaN, 1)));
p7c_chk('nan_if_cplx', @() p7c_if(1i));
p7c_chk('nan_elseif', @() p7c_elseif(NaN));
p7c_chk('nan_while_arr', @() p7c_while([1 NaN]));
p7c_chk('nan_if_and', @() p7c_cond(n));
p7c_chk('nan_idx', @() p7c_idx(n3));

% --- complex and non-numeric operands of the logical operators -------------------------------
p7c_chk('cx_and', @() (1+2i) & 1);
p7c_chk('cx_and_nan', @() complex(NaN, 0) & 1);
p7c_chk('cx_not', @() ~(1+2i));
p7c_chk('cx_not_z0', @() ~complex(1, 0));
p7c_chk('cx_not_arr', @() ~[1i 0]);
p7c_chk('cx_or_z0', @() complex(1, 0) | 0);
p7c_chk('cx_xor_z0', @() xor(complex(1, 0), 0));
p7c_chk('cx_not_fn', @() not(1i));
p7c_chk('cx_and_fn', @() and(1i, 1));
p7c_chk('cx_le_i8', @() int8(1) <= 1i);
p7c_chk('cx_ge_i8', @() int8(1) >= 1i);
p7c_chk('cx_gt_i8', @() int8(1) > 1i);
p7c_chk('cx_lt_i8_z0', @() int8(1) < complex(2, 0));
p7c_chk('cx_eq_i8', @() int8(1) == 1i);
p7c_chk('cx_lt_s', @() single(1) < 1i);
p7c_chk('cx_lt_fn_i8', @() lt(int8(1), 1i));
p7c_chk('cx_lt_i8a', @() int8([1 2]) < [1i 2]);
p7c_chk('cx_lt_e_i8', @() int8([]) < 1i);
p7c_chk('nn_xor_c_c', @() xor('ab', 'a'));
p7c_chk('nn_xor_nd', @() size(xor(ones(2, 1, 2), ones(1, 2))));
p7c_chk('nn_xor_3', @() xor(1, 0, 1));
p7c_chk('nn_xor_str', @() xor("a", 1));
p7c_chk('nn_xor_cell', @() xor({1}, 1));
p7c_chk('nn_and_str', @() "a" & 1);
p7c_chk('nn_and_cell', @() {1} & 1);
p7c_chk('nn_not_c', @() ~'a');
p7c_chk('nn_and_c_c', @() 'ab' & 'a');
p7c_chk('nn_logical_c', @() logical('a'));
p7c_chk('nn_logical_i8', @() logical(int8([1 0])));
p7c_chk('nn_logical_e', @() logical(zeros(1, 0)));

% --- logical empties ------------------------------------------------------------------------
p7c_chk('em_i8_and_t0', @() int8(2) & true(0));
p7c_chk('em_t0_and_i8', @() true(0) & int8(2));
p7c_chk('em_i8_or_t0', @() int8(2) | true(0));
p7c_chk('em_i8_xor_t0', @() xor(int8(2), true(0)));
p7c_chk('em_i8_eq_t0', @() int8(2) == true(0));
p7c_chk('em_i8_lt_t0', @() int8(2) < true(0));
p7c_chk('em_i8_and_d0', @() int8(2) & []);
p7c_chk('em_d_and_t0', @() 2 & true(0));
p7c_chk('em_d_and_d0', @() 2 & []);
p7c_chk('em_d_eq_d0', @() 2 == []);
p7c_chk('em_i8_eq_d0', @() int8(2) == []);
p7c_chk('em_i80_and_d', @() int8([]) & 2);
p7c_chk('em_i80_eq_d', @() int8([]) == 2);
p7c_chk('em_s_and_t0', @() single(2) & true(0));
p7c_chk('em_s_eq_t0', @() single(2) == true(0));
p7c_chk('em_i8_and_e10', @() int8(2) & zeros(1, 0));
p7c_chk('em_i8_eq_e10', @() int8(2) == zeros(1, 0));
p7c_chk('em_i8_eq_t10', @() int8(2) == true(1, 0));
p7c_chk('em_i8_and_t10', @() int8(2) & true(1, 0));
p7c_chk('em_t_and_i80', @() true & int8([]));
p7c_chk('em_t_eq_i80', @() true == int8([]));
p7c_chk('em_c_and_t0', @() 'a' & true(0));
p7c_chk('em_c_eq_t0', @() 'a' == true(0));
p7c_chk('em_i8_ne_t0', @() int8(2) ~= true(0));
p7c_chk('em_i8a_and_t0', @() int8([1 2]) & true(0));
p7c_chk('em_i8_and_i80', @() int8(2) & int8([]));
p7c_chk('em_i8_eq_i80', @() int8(2) == int8([]));
p7c_chk('em_u8_and_i80', @() uint8(2) & int8([]));
p7c_chk('em_u8_eq_i80', @() uint8(2) == int8([]));
p7c_chk('em_i8_ge_s0', @() int8(2) >= single([]));
p7c_chk('em_not_t0', @() ~true(0));
p7c_chk('em_not_i80', @() ~int8([]));
p7c_chk('em_not_e', @() ~[]);
p7c_chk('em_not_c_e', @() ~'');
p7c_chk('em_and_t0_t0', @() true(0) & true(0));
p7c_chk('em_and_nd', @() size(ones(2, 1, 2) & ones(1, 2)));
p7c_chk('em_and_e_nd', @() size(zeros(0, 1, 2) & 1));
p7c_chk('em_lt_e', @() [] < 1);
p7c_chk('em_lt_nd_e', @() size(zeros(1, 0, 2) < 1));
p7c_chk('em_lt_nd_e_cls', @() class(zeros(1, 0, 2) < 1));

% --- uniform arrayfun and cellfun -----------------------------------------------------------
id = @(v) v;
p7c_chk('cf_d_i8', @() cellfun(id, {1, int8(2)}));
p7c_chk('cf_i8_d', @() cellfun(id, {int8(2), 1}));
p7c_chk('cf_d_t', @() cellfun(id, {1, true}));
p7c_chk('cf_t_d', @() cellfun(id, {true, 1}));
p7c_chk('cf_str_chr', @() cellfun(id, {"a", 'b'}));
p7c_chk('cf_chr_str', @() cellfun(id, {'a', "b"}));
p7c_chk('cf_chr_chr', @() cellfun(id, {'a', 'b'}));
p7c_chk('cf_str_str', @() cellfun(id, {"a", "b"}));
p7c_chk('cf_d_s', @() cellfun(id, {1, single(2)}));
p7c_chk('cf_s_d', @() cellfun(id, {single(1), 2}));
p7c_chk('cf_i8_i16', @() cellfun(id, {int8(1), int16(2)}));
p7c_chk('cf_i8_i8', @() cellfun(id, {int8(1), int8(2)}));
p7c_chk('cf_t_t', @() cellfun(id, {true, false}));
p7c_chk('cf_d_c', @() cellfun(id, {1, 'a'}));
p7c_chk('cf_c_d', @() cellfun(id, {'a', 1}));
p7c_chk('cf_t_c', @() cellfun(id, {true, 'a'}));
p7c_chk('cf_d_z', @() cellfun(id, {1, 1+2i}));
p7c_chk('cf_z_d', @() cellfun(id, {1+2i, 1}));
p7c_chk('cf_s_z', @() cellfun(id, {single(1), 1+2i}));
p7c_chk('cf_d_str', @() cellfun(id, {1, "a"}));
p7c_chk('cf_str_d', @() cellfun(id, {"a", 1}));
p7c_chk('cf_d_struct', @() cellfun(id, {1, struct('a', 1)}));
p7c_chk('cf_d_cell', @() cellfun(id, {1, {2}}));
p7c_chk('cf_fh', @() cellfun(id, {@sin, @cos}));
p7c_chk('cf_d_fh', @() cellfun(id, {1, @cos}));
p7c_chk('cf_s_s', @() cellfun(id, {single(1), single(2)}));
p7c_chk('cf_u8_i8', @() cellfun(id, {uint8(1), int8(2)}));
p7c_chk('cf_d_d_t', @() cellfun(id, {1, 2, true}));
p7c_chk('cf_three_i8', @() cellfun(id, {int8(1), int8(2), 3}));
p7c_chk('cf_empty', @() cellfun(id, {}));
p7c_chk('cf_one_i8', @() cellfun(id, {int8(3)}));
p7c_chk('cf_2x1', @() cellfun(id, {1; true}));
p7c_chk('cf_d_ca', @() cellfun(id, {1, 'ab'}));
p7c_chk('cf_d_i8a', @() cellfun(id, {1, int8([1 2])}));
p7c_chk('cf_ca_d', @() cellfun(id, {'ab', 1}));
p7c_chk('cf_i8_da', @() cellfun(id, {int8(1), [1 2]}));
p7c_chk('cf_d_e', @() cellfun(id, {1, []}));
p7c_chk('cf_i8_e', @() cellfun(id, {int8(1), int8([])}));
p7c_chk('cf_d_cat', @() cellfun(id, {1, categorical({'a'})}));
p7c_chk('cf_z_s', @() cellfun(id, {1+2i, single(1)}));
p7c_chk('cf_s_sz', @() cellfun(id, {single(1), single(1+2i)}));
p7c_chk('cf_i8_cls', @() class(cellfun(id, {int8(1), int8(2)})));
p7c_chk('cf_u16_mat', @() cellfun(id, {uint16(1), uint16(2); uint16(3), uint16(4)}));
p7c_chk('cf_t_t_t', @() cellfun(id, {true; false; true}));
p7c_chk('cf_i8_two_out', @() p7c_two());
p7c_chk('cf_t_cls_mix', @() cellfun(@(v) v > 1, {1, 2}));
p7c_chk('cf_isreal', @() cellfun(@isreal, {1, 2}));
p7c_chk('cf_handler_s', @() cellfun(@(v) error('a:b', 'x'), {1, 2}, 'ErrorHandler', @(e, v) single(1)));
p7c_chk('cf_handler', @() cellfun(@(v) error('a:b', 'x'), {1, 2}, 'ErrorHandler', @(e, v) int8(1)));
p7c_chk('cf_handler_mix', @() cellfun(@p7c_maybe, {1, 2}, 'ErrorHandler', @(e, v) int8(1)));
p7c_chk('cf_isempty', @() cellfun('isempty', {1, []}));
p7c_chk('cf_length', @() cellfun('length', {1, [1 2]}));
p7c_chk('cf_class_str', @() cellfun('isclass', {int8(1), 2}, 'int8'));
p7c_chk('cf_cls_i8', @() cellfun(@(v) class(v), {int8(1)}, 'UniformOutput', false));
p7c_chk('af_d_i8', @() arrayfun(@(k) p7c_pick(k), 1:2));
p7c_chk('af_t_d', @() arrayfun(@(k) p7c_pickt(k), 1:2));
p7c_chk('af_str_chr', @() arrayfun(@(k) p7c_pickc(k), 1:2));
p7c_chk('af_two_out_d_i8', @() p7c_twoaf());
p7c_chk('af_mixed_str', @() arrayfun(@(v) p7c_strnum(v), [1 2]));
p7c_chk('af_char_ans', @() arrayfun(@(v) char(v + 96), [1 2 3]));
p7c_chk('af_str_ans', @() arrayfun(@(v) string(v), [1 2]));
p7c_chk('af_cls_i8', @() arrayfun(@(v) class(v), int8([1 2]), 'UniformOutput', false));
p7c_chk('af_cls_s', @() arrayfun(@(v) class(v), single([1 2]), 'UniformOutput', false));
p7c_chk('af_cls_u16', @() arrayfun(@(v) class(v), uint16([1 2]), 'UniformOutput', false));
p7c_chk('af_cls_t', @() arrayfun(@(v) class(v), [true false], 'UniformOutput', false));
p7c_chk('af_cls_c', @() arrayfun(@(v) class(v), 'ab', 'UniformOutput', false));
p7c_chk('af_cls_cplx', @() arrayfun(@(v) class(v), single([1+2i 3]), 'UniformOutput', false));
p7c_chk('af_id_i8', @() arrayfun(id, int8([1 2])));
p7c_chk('af_id_s', @() arrayfun(id, single([1 2])));
p7c_chk('af_id_t', @() arrayfun(id, [true false]));
p7c_chk('af_id_c', @() arrayfun(id, 'ab'));
p7c_chk('af_id_u8_mat', @() arrayfun(id, uint8([1 2; 3 4])));
p7c_chk('af_half_i8', @() arrayfun(@(v) v / 2, int8([1 3 5])));
p7c_chk('af_half_s', @() arrayfun(@(v) v / 3, single([1 2])));
p7c_chk('af_sat_u8', @() arrayfun(@(v) v + 300, uint8([1 2])));
p7c_chk('af_two_args', @() arrayfun(@(v, w) v + w, int8([1 2]), int8([3 4])));
p7c_chk('af_two_args_cls', @() arrayfun(@(v, w) [class(v) class(w)], int8([1 2]), single([3 4]), 'UniformOutput', false));
p7c_chk('af_i8_plus_d', @() arrayfun(@(v) v + 0.4, int8([1 2])));
p7c_chk('af_s_eps', @() arrayfun(@(v) eps(v), single([1 2])));
p7c_chk('af_cplx_flag', @() arrayfun(@(v) isreal(v), complex([1 2], 0)));
p7c_chk('af_cmplx_s', @() arrayfun(@(v) v, single([1+2i 3])));
p7c_chk('af_struct', @() arrayfun(@(v) v.a, struct('a', {int8(1), int8(2)})));
p7c_chk('af_i8_out_sum', @() sum(arrayfun(@(v) v * 100, int8([1 2]))));
p7c_chk('af_single_mean', @() arrayfun(@(v) mean([v 1]), single([1 2])));
p7c_chk('af_u8_img', @() arrayfun(@(v) v - 10, uint8([5 20])));
p7c_chk('af_l_plus', @() arrayfun(@(v) v + 1, [true false]));
p7c_chk('af_i16_two', @() arrayfun(@(v, w) v * w, int16([100 200]), [400 500]));

function r = p7c_max2()
[r, ~] = max([1 2], [2 1]);
end
function r = p7c_two()
[a, b] = cellfun(@(v) deal(v, int8(v)), {1, 2});
r = [class(a) '/' class(b)];
end
function r = p7c_twoaf()
[a, b] = arrayfun(@(k) deal(1, p7c_pick(k)), 1:2);
r = [a; double(b)];
end
function r = p7c_pick(k)
if k == 1
    r = 1;
else
    r = int8(2);
end
end
function r = p7c_pickt(k)
if k == 1
    r = true;
else
    r = 1;
end
end
function r = p7c_pickc(k)
if k == 1
    r = "a";
else
    r = 'b';
end
end
function r = p7c_strnum(v)
if v == 1
    r = 'a';
else
    r = "b";
end
end
function r = p7c_maybe(v)
if v == 1
    r = 1;
else
    error('a:b', 'x');
end
end
function r = p7c_if(v)
if v
    r = 1;
else
    r = 0;
end
end
function r = p7c_elseif(v)
if false
    r = 0;
elseif v
    r = 1;
else
    r = 2;
end
end
function r = p7c_while(v)
r = 0;
while v
    r = 1;
    break;
end
end
function r = p7c_cond(v)
r = 0;
if v && true
    r = 1;
end
end
function r = p7c_idx(v)
y = [10 20 30];
r = y(logical(v));
end
