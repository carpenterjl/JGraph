% record: -noFigureWindows
% Open items 346 and 349 (ADR 0386): the bracket's class and shape rules (char, logical, double,
% single, the integers, complex and flagged values, empties and N-D pieces under [a b], [a; b],
% horzcat, vertcat and cat(dim)), cat's dimension refusals, num2cell's classes and dimensions,
% [M, F, C] = mode(...), median of a complex array, cell2mat of an N-D cell, unique(z, 'rows') of a
% complex matrix, complex products with the sign of a zero part, power(x, 1) through an anonymous
% function, a flagged base to a whole power, and psi by Amos's algorithm. Probes probe_cat,
% probe_cat2, probe_msg, probe_misc, probe_349, probe_mul, probe_pow, probe_rpow and probe_psi2 in
% lane 8C's scratch.

% --- the bracket's class rule over pairs of values ------------------------------------------------
% A complex value beside an integer is a complex integer in R2025b, which has no storage here; those
% pairs are left out (ADR 0386's Still open).
names = {'ch', 'lg', 'db', 'sg', 'i8', 'i16', 'u8', 'i64', 'u64', 'zd', 'zs', 'fd', 'ech', 'edb', 'ei16'};
vals = {'a', true, 2.7, single(2.5), int8(3), int16(300), uint8(5), int64(6), uint64(7), ...
    complex(1, 2), complex(single(1), 2), complex(4, 0), '', [], zeros(0, 0, 'int16')};
integers = {'i8', 'i16', 'u8', 'i64', 'u64', 'ei16'};
complexes = {'zd', 'zs'};
for i = 1:numel(vals)
    for j = 1:numel(vals)
        if (any(strcmp(names{i}, integers)) && any(strcmp(names{j}, complexes))) ...
                || (any(strcmp(names{j}, integers)) && any(strcmp(names{i}, complexes)))
            continue
        end
        a = vals{i}; b = vals{j};
        p8c_chk(['h_' names{i} '_' names{j}], @() [a b]);
        p8c_chk(['v_' names{i} '_' names{j}], @() [a; b]);
        p8c_chk(['c3_' names{i} '_' names{j}], @() cat(3, a, b));
    end
end

% --- three pieces, char codes, integer saturation ---------------------------------------------
p8c_chk('h3_i8_i16_ch', @() [int8(1) int16(2) 'a']);
p8c_chk('h3_ch_i16_i8', @() ['a' int16(2) int8(1)]);
p8c_chk('h3_db_i16_i8', @() [2 int16(400) int8(1)]);
p8c_chk('h3_sg_u8_i8', @() [single(2.5) uint8(200) int8(1)]);
p8c_chk('h3_lg_ch_db', @() [true 'a' 66]);
p8c_chk('h3_e_i16_i8', @() [[] int16(400) int8(1)]);
p8c_chk('h3_ei8_i16', @() [zeros(1, 0, 'int8') int16(400)]);
p8c_chk('h3_ei8_i16_0', @() [zeros(0, 0, 'int8') int16(400)]);
p8c_chk('v_e_ch', @() [''; 2]);
p8c_chk('ch_neg', @() ['a' -1]);
p8c_chk('ch_nan', @() ['a' NaN]);
p8c_chk('ch_big', @() ['a' 70000]);
p8c_chk('ch_frac', @() ['a' 66.7]);
p8c_chk('ch_frac2', @() ['a' 66.5]);
p8c_chk('ch_cplx', @() ['a' 1+2i]);
p8c_chk('ch_i8neg', @() ['a' int8(-5)]);
p8c_chk('ch_u64', @() ['a' uint64(66)]);
p8c_chk('ch_negfrac', @() ['a' -0.9]);
p8c_chk('ch_sg', @() ['a' single(66.9)]);
p8c_chk('ch_inf', @() ['a' Inf]);
p8c_chk('ch_ninf', @() ['a' -0.5]);
p8c_chk('i8_sat', @() [int8(1) 300 -300 2.5 3.5 -2.5 NaN Inf]);
p8c_chk('i8_round', @() [int8(1) 2.5 -2.5 3.5 NaN -Inf Inf 127.5 -128.5]);
p8c_chk('u8_round', @() [uint8(1) 2.5 -2.5 0.4999 NaN 255.5]);
p8c_chk('sg_round', @() [single(1) 0.1 1e40 -1e40]);
p8c_chk('u8_i8', @() [uint8(250) int8(-5)]);
p8c_chk('i8_u8', @() [int8(-5) uint8(250)]);
p8c_chk('i8_lg', @() [int8(1) true]);
p8c_chk('i8_ch', @() [int8(1) 'a']);
p8c_chk('u16_ch', @() [uint16(70000) 'a']);
p8c_chk('cat1_i8_i16', @() cat(1, int8(1), int16(2)));
p8c_chk('cat2_i16_i8', @() cat(2, int16(1), int8(2)));
p8c_chk('hc_i8_i16', @() horzcat(int8(1), int16(2)));
p8c_chk('vc_i8_i16', @() vertcat(int8(1), int16(2)));
p8c_chk('vc_ch_db', @() vertcat('a', 2));
p8c_chk('hc_ch_db', @() horzcat('a', 2));
p8c_chk('cat1_ch_db', @() cat(1, 'a', 2));
p8c_chk('cat3_ch_db', @() cat(3, 'a', 66));
p8c_chk('cell_mix', @() cell2mat({'a', 1; 2, 3}));
p8c_chk('cell_int', @() cell2mat({int8(1), int16(2)}));

% --- flagged values ---------------------------------------------------------------------------
fs = complex(single(4), 0);
p8c_chk('fs_db', @() [fs 2.5]);
p8c_chk('db_fs', @() [2.5 fs]);
p8c_chk('fs_fs', @() [fs fs]);
p8c_chk('fs_sg', @() [fs single(1)]);
p8c_chk('fs_lg', @() [fs true]);
p8c_chk('fs_fd', @() [fs complex(2, 0)]);
p8c_chk('fd_fs', @() [complex(2, 0) fs]);
p8c_chk('fs_i8', @() [fs int8(1)]);
p8c_chk('fs_z', @() [fs 1i]);
p8c_chk('fd_alone', @() [complex(2, 0)]);
p8c_chk('fd_e', @() [complex(2, 0) []]);
p8c_chk('fd_c3', @() cat(3, complex([1 2], 0), [3 4]));

% --- empties ----------------------------------------------------------------------------------
p8c_chk('e2_h10', @() [ones(2,2) zeros(1,0)]);
p8c_chk('e2_h10r', @() [zeros(1,0) ones(2,2)]);
p8c_chk('e2_v01', @() [ones(2,2); zeros(0,1)]);
p8c_chk('e2_h03', @() [ones(2,2) zeros(0,3)]);
p8c_chk('e2_h20', @() [ones(2,2) zeros(2,0)]);
p8c_chk('e2_h30', @() [ones(2,2) zeros(3,0)]);
p8c_chk('e2_v03', @() [ones(2,2); zeros(0,3)]);
p8c_chk('e2_v02', @() [ones(2,2); zeros(0,2)]);
p8c_chk('e2_all1', @() [zeros(1,0) zeros(2,0)]);
p8c_chk('e2_all2', @() [zeros(0,3); zeros(0,2)]);
p8c_chk('e2_all3', @() [zeros(0,3) zeros(0,2)]);
p8c_chk('e3_c3_10', @() cat(3, ones(2), zeros(1,0)));
p8c_chk('e3_c3_002', @() cat(3, ones(2), zeros(0,0,2)));
p8c_chk('e3_c3_220', @() cat(3, ones(2), zeros(2,2,0)));
p8c_chk('e3_c3_230', @() cat(3, ones(2), zeros(2,3,0)));
p8c_chk('e3_c3_all', @() cat(3, zeros(1,0), zeros(2,0)));
p8c_chk('e3_c3_all2', @() cat(3, zeros(1,0), zeros(1,0)));
p8c_chk('e3_c3_00', @() cat(3, [], []));
p8c_chk('e3_c3_00s', @() cat(3, [], 5));
p8c_chk('e3_c3_ch00', @() cat(3, '', 'a'));
p8c_chk('e3_c4_e', @() cat(4, zeros(1,0), 1));
p8c_chk('e3_c1_1', @() cat(1, ones(2,2,2), zeros(0,2,2)));
p8c_chk('e3_c1_2', @() cat(1, ones(2,2,2), zeros(0,2)));
p8c_chk('e3_h_nd_e', @() [zeros(2,0,2) ones(2,2,2)]);
p8c_chk('e3_c3_lg_e', @() cat(3, true, []));
p8c_chk('e3_c3_lg_lg', @() cat(3, true, false));
p8c_chk('e3_c3_lg_e10', @() cat(3, true, zeros(1,0)));
p8c_chk('e3_h_lg_e10', @() [true zeros(1,0)]);
p8c_chk('e3_h_lg_le', @() [true true(1,0)]);
p8c_chk('e3_h_lg_i8e', @() [true zeros(1,0,'int8')]);
p8c_chk('e3_h_ch_e10', @() ['a' zeros(1,0)]);
p8c_chk('e3_v_ch_e', @() ['ab'; zeros(1,0)]);
p8c_chk('e3_v_ch_e00', @() ['ab'; []]);
p8c_chk('e3_v_ech_ech', @() [''; '']);
p8c_chk('e3_v_ech_n', @() [''; 5]);
p8c_chk('e3_h_ech_n', @() ['' 5]);
p8c_chk('e3_v_n_ech', @() [5; '']);
p8c_chk('e3_v_ech_db', @() [''; []]);
p8c_chk('e3_v_blk', @() [blanks(0); 5]);
p8c_chk('e3_h_blk_lg', @() [blanks(0) true]);

% --- N-D shapes ---------------------------------------------------------------------------------
p8c_chk('nd_h', @() [ones(2,2,2), ones(2,1,2)]);
p8c_chk('nd_v', @() [ones(2,2,2); ones(1,2,2)]);
p8c_chk('nd_h_mis', @() [ones(2,2,2), ones(2,1,3)]);
p8c_chk('nd_h_2d', @() [ones(2,2,2), ones(2,1)]);
p8c_chk('nd_h_e', @() [ones(2,2,2), []]);
p8c_chk('nd_h_e10', @() [ones(2,2,2), zeros(1,0)]);
p8c_chk('nd_h_e20', @() [ones(2,2,2), zeros(2,0)]);
p8c_chk('nd_h_e202', @() [ones(2,2,2), zeros(2,0,2)]);
p8c_chk('nd_blk', @() [ones(2,2,2) 2*ones(2,1,2); 3*ones(1,3,2)]);
p8c_chk('nd_vals', @() [reshape(1:8,2,2,2), reshape(9:12,2,1,2)]);
p8c_chk('nd_vvals', @() [reshape(1:8,2,2,2); reshape(9:12,1,2,2)]);
p8c_chk('nd_cplx', @() [reshape(1:8,2,2,2)*1i, reshape(9:12,2,1,2)]);
p8c_chk('nd_i8', @() [int8(reshape(1:8,2,2,2)), reshape(9:12,2,1,2)]);
p8c_chk('nd_ch', @() [repmat('ab',[1 1 2]), repmat('c',[1 1 2])]);
p8c_chk('nd_4d', @() [ones(1,1,1,2), 2*ones(1,1,1,2)]);
p8c_chk('nd_4d_v', @() [ones(1,1,1,2); 2*ones(1,1,1,2)]);
p8c_chk('nd_4d_3d', @() [ones(1,1,2,2), ones(1,1,2)]);
p8c_chk('nd_vert_lg', @() [true(1,1,2); false(1,1,2)]);
p8c_chk('nd_h_lg_db', @() [true(1,1,2) ones(1,1,2)]);
p8c_chk('nd_h_ch_db', @() [repmat('a',[1 1 2]) 66*ones(1,1,2)]);
p8c_chk('nd_h_i8_i16', @() [int8(ones(1,1,2)) int16(300*ones(1,1,2))]);
p8c_chk('nd_h_sg_z', @() [single(ones(1,1,2)) 1i*ones(1,1,2)]);
p8c_chk('nd_h_e_nd', @() [[] ones(1,1,2)]);
p8c_chk('nd_grid', @() [ones(1,1,2) 2*ones(1,1,2); 3*ones(1,2,2)]);
p8c_chk('nd_grid_mis', @() [ones(1,1,2) 2*ones(1,1,2); 3*ones(1,3,2)]);
p8c_chk('nd_scalar', @() [ones(1,1,2) 5]);
p8c_chk('nd_trail1', @() [ones(2,2,1,1) ones(2,1)]);
p8c_chk('hc_nd', @() horzcat(ones(2,2,2), ones(2,1,2)));
p8c_chk('vc_nd', @() vertcat(ones(2,2,2), ones(1,2,2)));
p8c_chk('cat3_nd', @() cat(3, ones(2,2,2), ones(2,2)));
p8c_chk('cat3_nd_mis', @() cat(3, ones(2,2,2), ones(2,3)));
p8c_chk('cat4', @() cat(4, [1 2], [3 4]));
p8c_chk('cat1_nd', @() cat(1, ones(2,2,2), 2*ones(1,2,2)));
p8c_chk('cat2_nd', @() cat(2, ones(2,2,2), 2*ones(2,1,2)));
z = [1+2i 3; 4 5-1i];
p8c_chk('c3_zz', @() cat(3, z, z));
p8c_chk('c3_zr', @() cat(3, z, [1 2; 3 4]));
p8c_chk('c3_rz', @() cat(3, [1 2; 3 4], z));
p8c_chk('c3_zs', @() cat(3, single(z), z));
p8c_chk('c1_zz', @() cat(1, z, z));
p8c_chk('c2_zz', @() cat(2, z, z));
p8c_chk('c4_zz', @() cat(4, z, z));
p8c_chk('c3_three', @() cat(3, z, z, 1i*ones(2)));
p8c_chk('c3_nd_z', @() cat(3, cat(3, z, z), z));
p8c_chk('h_nd_z', @() [cat(3, z, z), cat(3, z(:,1), z(:,1))]);
p8c_chk('c3_lg_z', @() cat(3, true(2), z));
p8c_chk('c3_ch_z', @() cat(3, 'ab', [1i 2]));
p8c_chk('c3_e_z', @() cat(3, [], z));
p8c_chk('c3_one', @() cat(3, z));

% --- cat's dimension ----------------------------------------------------------------------------
p8c_chk('catd_0', @() cat(0, 1, 2));
p8c_chk('catd_frac', @() cat(1.5, 1, 2));
p8c_chk('catd_neg', @() cat(-1, 1));
p8c_chk('catd_vec', @() cat([1 2], 1, 2));
p8c_chk('catd_char', @() cat('a', 1, 2));
p8c_chk('catd_only', @() cat(3));
p8c_chk('catd_none', @() cat());
p8c_chk('catd_one', @() cat(3, 1));
p8c_chk('catd_inf', @() cat(Inf, 1, 2));
p8c_chk('catd_nan', @() cat(NaN, 1, 2));
p8c_chk('catd_int', @() cat(int8(3), 1, 2));
p8c_chk('catd_true', @() cat(true, 1, 2));
p8c_chk('catd_flag', @() cat(complex(3, 0), 1, 2));
p8c_chk('catd_cplx', @() cat(3+1i, 1, 2));
p8c_chk('catd_single', @() cat(single(3), 1, 2));
p8c_chk('catd_big', @() cat(1e10, 1, 2));

% --- num2cell -----------------------------------------------------------------------------------
N = @(f, k, n) p8c_nth(f, k, n);
C1 = @(c) c{1};
C2 = @(c) c{2};
p8c_chk('n2c_single', @() class(C1(num2cell(single([1 2])))));
p8c_chk('n2c_single_v', @() C2(num2cell(single([1 2]))));
p8c_chk('n2c_int8_v', @() C2(num2cell(int8([1 2]))));
p8c_chk('n2c_u64_v', @() C2(num2cell(uint64([1 2]))));
p8c_chk('n2c_lg_v', @() C2(num2cell([true false])));
p8c_chk('n2c_ch_v', @() C2(num2cell('ab')));
p8c_chk('n2c_z_v', @() C2(num2cell([1+2i 3])));
p8c_chk('n2c_z1_v', @() C1(num2cell([1+2i 3])));
p8c_chk('n2c_flag_v', @() C2(num2cell(complex([1 2], 0))));
p8c_chk('n2c_sz_v', @() C1(num2cell(single([1+2i 3]))));
A = [1 2 3; 4 5 6];
p8c_chk('n2c_dim1_size', @() size(num2cell(A, 1)));
p8c_chk('n2c_dim1_v', @() C2(num2cell(A, 1)));
p8c_chk('n2c_dim2_size', @() size(num2cell(A, 2)));
p8c_chk('n2c_dim2_v', @() C2(num2cell(A, 2)));
p8c_chk('n2c_dim12_size', @() size(num2cell(A, [1 2])));
p8c_chk('n2c_dim12_v', @() C1(num2cell(A, [1 2])));
p8c_chk('n2c_dim21_size', @() size(num2cell(A, [2 1])));
p8c_chk('n2c_dim21_v', @() C1(num2cell(A, [2 1])));
p8c_chk('n2c_dim3_size', @() size(num2cell(A, 3)));
p8c_chk('n2c_dim3_v', @() C2(num2cell(A, 3)));
B = reshape(1:24, 2, 3, 4);
p8c_chk('n2c_nd13_size', @() size(num2cell(B, [1 3])));
p8c_chk('n2c_nd13_v', @() C2(num2cell(B, [1 3])));
p8c_chk('n2c_nd31_v', @() C2(num2cell(B, [3 1])));
p8c_chk('n2c_nd2_size', @() size(num2cell(B, 2)));
p8c_chk('n2c_nd2_v', @() C2(num2cell(B, 2)));
p8c_chk('n2c_nd3_v', @() C2(num2cell(B, 3)));
p8c_chk('n2c_nd_size', @() size(num2cell(B)));
p8c_chk('n2c_nd_v', @() C2(num2cell(B)));
p8c_chk('n2c_sdim1_v', @() C2(num2cell(single(A), 1)));
p8c_chk('n2c_i8dim2_v', @() C2(num2cell(int8(A), 2)));
p8c_chk('n2c_zdim1_v', @() C1(num2cell([1+2i 3; 4 5], 1)));
p8c_chk('n2c_fdim1_v', @() C1(num2cell(complex(A, 0), 1)));
p8c_chk('n2c_lgdim2_v', @() C1(num2cell(logical(A), 2)));
p8c_chk('n2c_chdim2_v', @() C1(num2cell('abc', 2)));
p8c_chk('n2c_chmdim1_v', @() C1(num2cell(['ab'; 'cd'], 1)));
p8c_chk('n2c_cell_dim', @() class(C1(num2cell({1, 2}, 2))));
p8c_chk('n2c_cell_dim_sz', @() size(C1(num2cell({1, 2}, 2))));
p8c_chk('n2c_cell', @() size(num2cell({1, 2})));
p8c_chk('n2c_cell_inner', @() class(C1(num2cell({1, 2}))));
p8c_chk('n2c_dim0', @() num2cell(A, 0));
p8c_chk('n2c_dimfrac', @() num2cell(A, 1.5));
p8c_chk('n2c_dimneg', @() num2cell(A, -1));
p8c_chk('n2c_dimbig', @() size(num2cell(A, 5)));
p8c_chk('n2c_dimbig_v', @() C1(num2cell(A, 5)));
p8c_chk('n2c_dimrep', @() size(num2cell(A, [1 1])));
p8c_chk('n2c_dimempty', @() size(num2cell(A, [])));
p8c_chk('n2c_dimchar', @() size(num2cell(A, 'a')));
p8c_chk('n2c_empty', @() size(num2cell(zeros(0, 3), 1)));
p8c_chk('n2c_empty_v', @() size(C1(num2cell(zeros(0, 3), 1))));
p8c_chk('n2c_empty2', @() size(num2cell(zeros(0, 3))));
p8c_chk('n2c_empty3', @() size(num2cell(zeros(0, 3), 2)));
p8c_chk('n2c_str', @() class(C1(num2cell(["a" "b"]))));
p8c_chk('n2c_str_dim', @() size(C1(num2cell(["a" "b"], 2))));
s = struct('a', {1, 2});
p8c_chk('n2c_struct', @() size(num2cell(s)));
p8c_chk('n2c_struct_dim', @() size(C1(num2cell(s, 2))));
p8c_chk('n2c_none', @() num2cell());
p8c_chk('n2c_three', @() num2cell(A, 1, 2));

% --- mode ---------------------------------------------------------------------------------------
x = [1 2 2 3 3 3 4];
M = [1 2 2; 1 3 2; 4 3 5];
p8c_chk('mode_mf_m', @() N(@() mode(x), 1, 2));
p8c_chk('mode_mf_f', @() N(@() mode(x), 2, 2));
p8c_chk('mode_c_cls', @() class(N(@() mode(x), 3, 3)));
p8c_chk('mode_c_v', @() C1(N(@() mode(x), 3, 3)));
p8c_chk('mode_tie_m', @() N(@() mode([1 1 2 2 3]), 1, 3));
p8c_chk('mode_tie_f', @() N(@() mode([1 1 2 2 3]), 2, 3));
p8c_chk('mode_tie_c', @() C1(N(@() mode([1 1 2 2 3]), 3, 3)));
p8c_chk('mode_M_m', @() N(@() mode(M), 1, 3));
p8c_chk('mode_M_f', @() N(@() mode(M), 2, 3));
p8c_chk('mode_M_csize', @() size(N(@() mode(M), 3, 3)));
p8c_chk('mode_M2_m', @() N(@() mode(M, 2), 1, 3));
p8c_chk('mode_M2_f', @() N(@() mode(M, 2), 2, 3));
p8c_chk('mode_M2_csize', @() size(N(@() mode(M, 2), 3, 3)));
p8c_chk('mode_M2_c1', @() C1(N(@() mode(M, 2), 3, 3)));
p8c_chk('mode_e_m', @() N(@() mode([]), 1, 3));
p8c_chk('mode_e_f', @() N(@() mode([]), 2, 3));
p8c_chk('mode_e_c', @() class(N(@() mode([]), 3, 3)));
p8c_chk('mode_e_csize', @() size(N(@() mode([]), 3, 3)));
p8c_chk('mode_e_c1', @() size(C1(N(@() mode([]), 3, 3))));
p8c_chk('mode_e3_m', @() N(@() mode(zeros(0, 3)), 1, 3));
p8c_chk('mode_e3_f', @() N(@() mode(zeros(0, 3)), 2, 3));
p8c_chk('mode_e3_csize', @() size(N(@() mode(zeros(0, 3)), 3, 3)));
p8c_chk('mode_e3_c1', @() size(C1(N(@() mode(zeros(0, 3)), 3, 3))));
p8c_chk('mode_nan_m', @() N(@() mode([NaN NaN 1]), 1, 2));
p8c_chk('mode_nan_f', @() N(@() mode([NaN NaN 1]), 2, 2));
p8c_chk('mode_nan_c', @() C1(N(@() mode([NaN NaN 1]), 3, 3)));
p8c_chk('mode_allnan_m', @() N(@() mode([NaN NaN]), 1, 3));
p8c_chk('mode_allnan_f', @() N(@() mode([NaN NaN]), 2, 3));
p8c_chk('mode_allnan_c', @() C1(N(@() mode([NaN NaN]), 3, 3)));
p8c_chk('mode_i8_m', @() N(@() mode(int8([3 3 1])), 1, 2));
p8c_chk('mode_i8_f', @() N(@() mode(int8([3 3 1])), 2, 2));
p8c_chk('mode_i8_c', @() C1(N(@() mode(int8([3 3 1])), 3, 3)));
p8c_chk('mode_s_f', @() N(@() mode(single([3 3 1])), 2, 3));
p8c_chk('mode_s_c', @() C1(N(@() mode(single([3 3 1])), 3, 3)));
p8c_chk('mode_lg_m', @() N(@() mode([true true false]), 1, 3));
p8c_chk('mode_lg_f', @() N(@() mode([true true false]), 2, 3));
p8c_chk('mode_lg_c', @() C1(N(@() mode([true true false]), 3, 3)));
p8c_chk('mode_ch_m', @() N(@() mode('aab'), 1, 3));
p8c_chk('mode_ch_f', @() N(@() mode('aab'), 2, 3));
p8c_chk('mode_ch_c', @() C1(N(@() mode('aab'), 3, 3)));
p8c_chk('mode_z_m', @() N(@() mode([1+1i 1+1i 2]), 1, 2));
p8c_chk('mode_z_f', @() N(@() mode([1+1i 1+1i 2]), 2, 2));
p8c_chk('mode_z_c', @() C1(N(@() mode([1+1i 1+1i 2]), 3, 3)));
p8c_chk('mode_all_m', @() N(@() mode(M, 'all'), 1, 2));
p8c_chk('mode_all_f', @() N(@() mode(M, 'all'), 2, 2));
p8c_chk('mode_all_c', @() C1(N(@() mode(M, 'all'), 3, 3)));
p8c_chk('mode_M3_f', @() N(@() mode(M, 3), 2, 3));
p8c_chk('mode_M3_csize', @() size(N(@() mode(M, 3), 3, 3)));
p8c_chk('mode_M3_c1', @() C1(N(@() mode(M, 3), 3, 3)));
T = reshape([1 1 2 2 2 3 3 1], 2, 2, 2);
p8c_chk('mode_nd3_m', @() N(@() mode(T, 3), 1, 3));
p8c_chk('mode_nd3_f', @() N(@() mode(T, 3), 2, 3));
p8c_chk('mode_nd3_csize', @() size(N(@() mode(T, 3), 3, 3)));
p8c_chk('mode_nd3_c', @() C2(N(@() mode(T, 3), 3, 3)));
p8c_chk('mode_nd_c', @() size(N(@() mode(T), 3, 3)));
p8c_chk('mode_col_c', @() C1(N(@() mode([5 5 1 1]'), 3, 3)));
p8c_chk('mode_vec_m', @() N(@() mode(x, [1 2]), 1, 2));
p8c_chk('mode_vec_f', @() N(@() mode(x, [1 2]), 2, 2));
p8c_chk('mode_vec_c', @() C1(N(@() mode(M, [1 2]), 3, 3)));
p8c_chk('mode_four', @() N(@() mode(x), 4, 4));

% --- median of a complex array --------------------------------------------------------------------
z = [3+1i 1-2i 2; 1i -1 4-4i];
p8c_chk('med_z', @() median(z));
p8c_chk('med_z2', @() median(z, 2));
p8c_chk('med_zall', @() median(z, 'all'));
p8c_chk('med_zrow', @() median([3+1i 1-2i 2 1i]));
p8c_chk('med_zodd', @() median([3+1i 1-2i 2]));
p8c_chk('med_zflag', @() median(complex([3 1 2], 0)));
p8c_chk('med_zflag4', @() median(complex([3 1 2 5], 0)));
p8c_chk('med_znan', @() median([1+1i NaN 2]));
p8c_chk('med_znan_omit', @() median([1+1i NaN 2], 'omitnan'));
p8c_chk('med_znan_omit2', @() median([1+1i complex(NaN, 1) 2 5i], 'omitnan'));
p8c_chk('med_ztie', @() median([1i -1 1 -1i]));
p8c_chk('med_ztie2', @() median([2 -2 1i]));
p8c_chk('med_zs', @() median(single([3+1i 1-2i 2 1i])));
p8c_chk('med_ze', @() median(complex(zeros(0, 3))));
p8c_chk('med_zinf', @() median([Inf+1i 1 2]));
p8c_chk('med_znd', @() median(cat(3, [1i 2], [3 -4i]), 3));
p8c_chk('med_zcol', @() median([1+1i; 2; 3i; -4]));
p8c_chk('med_zmean', @() median([1+1i 3-1i]));
p8c_chk('med_zvec', @() median(z, [1 2]));
p8c_chk('med_zmat_nan', @() median([1+1i NaN; 2 3i; 4 5]));
p8c_chk('med_zmat_omit', @() median([1+1i NaN; 2 3i; 4i 5], 'omitnan'));

% --- cell2mat of an N-D cell ----------------------------------------------------------------------
c = cell(1, 1, 2); c{1} = [1 2]; c{2} = [3 4];
p8c_chk('c2m_nd_pages', @() cell2mat(c));
c = cell(2, 1, 2); c{1, 1, 1} = [1 2]; c{2, 1, 1} = [3 4]; c{1, 1, 2} = [5 6]; c{2, 1, 2} = [7 8];
p8c_chk('c2m_nd_2x1x2', @() cell2mat(c));
c = cell(2, 2, 2); for k = 1:8, c{k} = k; end
p8c_chk('c2m_nd_2x2x2', @() cell2mat(c));
c = cell(1, 2, 2); c{1, 1, 1} = ones(2, 1); c{1, 2, 1} = 2 * ones(2, 3); c{1, 1, 2} = 3 * ones(2, 1); c{1, 2, 2} = 4 * ones(2, 3);
p8c_chk('c2m_nd_blocks', @() cell2mat(c));
c = cell(1, 1, 2); c{1} = ones(2, 2, 2); c{2} = 2 * ones(2, 2, 3);
p8c_chk('c2m_nd_deep', @() cell2mat(c));
c = cell(1, 1, 1, 2); c{1} = 1; c{2} = 2;
p8c_chk('c2m_4d', @() cell2mat(c));
c = cell(1, 1, 2); c{1} = 'ab'; c{2} = 'cd';
p8c_chk('c2m_nd_char', @() cell2mat(c));
c = cell(1, 1, 2); c{1} = int8(1); c{2} = int8(2);
p8c_chk('c2m_nd_i8', @() cell2mat(c));
c = cell(1, 1, 2); c{1} = [1 2]; c{2} = [3 4 5];
p8c_chk('c2m_nd_bad', @() cell2mat(c));
c = cell(2, 1, 2); c{1, 1, 1} = 1; c{2, 1, 1} = [2 3]; c{1, 1, 2} = 4; c{2, 1, 2} = 5;
p8c_chk('c2m_nd_bad2', @() cell2mat(c));
c = cell(1, 1, 2); c{1} = 1i; c{2} = 2;
p8c_chk('c2m_nd_cplx', @() cell2mat(c));
c = cell(1, 1, 0);
p8c_chk('c2m_nd_empty', @() cell2mat(c));
c = cell(2, 2, 2); for k = 1:8, c{k} = k; end
c{1, 1, 2} = [];
p8c_chk('c2m_nd_hole', @() cell2mat(c));

% --- unique(z, 'rows') of a complex matrix -------------------------------------------------------
Z = [1+2i 3; 1-2i 3; 1+2i 3; -2 1i; 2 1i];
p8c_chk('uq_z_u', @() N(@() unique(Z, 'rows'), 1, 3));
p8c_chk('uq_z_i', @() N(@() unique(Z, 'rows'), 2, 3));
p8c_chk('uq_z_j', @() N(@() unique(Z, 'rows'), 3, 3));
p8c_chk('uq_zs_u', @() N(@() unique(Z, 'rows', 'stable'), 1, 3));
p8c_chk('uq_zs_i', @() N(@() unique(Z, 'rows', 'stable'), 2, 3));
p8c_chk('uq_zs_j', @() N(@() unique(Z, 'rows', 'stable'), 3, 3));
p8c_chk('uq_zl_u', @() N(@() unique(Z, 'rows', 'last'), 1, 3));
p8c_chk('uq_zl_i', @() N(@() unique(Z, 'rows', 'last'), 2, 3));
p8c_chk('uq_zl_j', @() N(@() unique(Z, 'rows', 'last'), 3, 3));
p8c_chk('uq_zsort_u', @() N(@() unique(Z, 'rows', 'sorted'), 1, 3));
p8c_chk('uq_z3', @() unique([1i 2; 1i 2; -1i 2; 1 2; -1 2; 2i 0], 'rows'));
p8c_chk('uq_zinf', @() unique([Inf*1i 1; 1 Inf; Inf*1i 1], 'rows'));
p8c_chk('uq_z_nan', @() unique([NaN+1i 1; NaN+1i 1; 1 1], 'rows'));
p8c_chk('uq_z_neg0', @() unique([complex(-0, 0) 1; 0 1; 1i 1], 'rows'));
p8c_chk('uq_z_single', @() unique(single(Z), 'rows'));
p8c_chk('uq_z_mixed', @() unique([1i; -1; 1; -1i; 1i], 'rows'));
p8c_chk('uq_z_plain', @() unique(Z));
zv = [3 1-2i 1+2i 3 -1i NaN+1i NaN+1i];
p8c_chk('uq_zv_u', @() N(@() unique(zv), 1, 3));
p8c_chk('uq_zv_i', @() N(@() unique(zv), 2, 3));
p8c_chk('uq_zv_j', @() N(@() unique(zv), 3, 3));
p8c_chk('uq_zv_stable', @() unique(zv, 'stable'));
p8c_chk('uq_zv_last', @() N(@() unique(zv, 'last'), 2, 2));
p8c_chk('uq_zv_col', @() unique(zv.'));
p8c_chk('uq_zv_single', @() unique(single(zv)));
p8c_chk('sr_z', @() sortrows(Z));

% --- complex products with the sign of a zero part ----------------------------------------------------
% Parts in {0, -0, 1, -1, Inf, -Inf, NaN, 2}; a flagged value with a -0 imaginary part is left out
% (the elementwise road reads it +0; ADR 0386's Still open). One line per left factor.
parts = [0, -0, 1, -1, Inf, -Inf, NaN, 2];
zs = {};
for i = 1:numel(parts)
    for j = 1:numel(parts)
        if parts(j) == 0 && 1 / parts(j) < 0
            continue
        end
        zs{end + 1} = complex(parts(i), parts(j)); %#ok<SAGROW>
    end
end
for i = 1:numel(parts)
    zs{end + 1} = parts(i); %#ok<SAGROW>
end
for i = 1:numel(zs)
    txt = p8c_zs(zs{i});
    for j = 1:numel(zs)
        txt = [txt ' ' p8c_zs(zs{i} * zs{j})]; %#ok<AGROW>
    end
    fprintf('CHK|mul_%02d|%s|exact\n', i, txt);
end
o = [1 1]; v = [complex(-0, -1) complex(-0, -1)];
p8c_chk('mul_arr_rc', @() p8c_zs(o(1) * v(1)));
pd = o .* v;
p8c_chk('mul_arr_dot', @() 1 / real(pd(1)));
ps = 1 .* v;
p8c_chk('mul_arr_scal', @() 1 / real(ps(1)));
p8c_chk('cplx_negz', @() 1 / imag(complex(1, -0)));
p8c_chk('cplx_negz_arr', @() 1 ./ imag(complex([1 2], [-0 -0])));

% --- power(x, 1) through an anonymous function --------------------------------------------------
xf = complex([3 1 2], 0);
g = @(w) power(w, 1);
p8c_chk('pow_anon_lit', @() g(xf));
h = @(w) w .^ 1;
p8c_chk('pow_anon_op', @() h(xf));
k3 = @(w, p) w .^ p;
p8c_chk('pow_anon_opvar', @() k3(xf, 1));
p8c_chk('pow_fn_lit', @() power(xf, 1));

% --- a flagged base to a whole power: R2025b's real road ---------------------------------------------
b = complex(1.1, 0);
for n = [2 3 5 7 10 17 30 -1 -2 -3]
    p8c_chk(sprintf('flagpow_%d', n), @() b .^ n);
end
p8c_chk('flagpow_4', @() b .^ 4, 'div=ADR0386');
p8c_chk('flagpow_vec', @() complex([1.1 0.3 -2.7], 0) .^ 3);
p8c_chk('flagpow_neg', @() complex(-1.1, 0) .^ 3);
p8c_chk('flagpow_half', @() complex(1.1, 0) .^ 0.5);
p8c_chk('flagpow_negh', @() complex(-4, 0) .^ 0.5);
p8c_chk('flagpow_ovf', @() complex(1e200, 0) .^ 2);
p8c_chk('flagpow_s', @() complex(single(1.1), 0) .^ 3);
p8c_chk('flagpow_zero', @() complex(0, 0) .^ 3);
p8c_chk('flagpow_inf', @() complex(Inf, 0) .^ 3);
p8c_chk('flagpow_nan', @() complex(NaN, 0) .^ 3);

% --- psi by Amos's algorithm ----------------------------------------------------------------------
for k = [0:6 10]
    for xv = [1e-8 0.25 0.5 0.75 1 1.25 1.5 1.75 2 2.5 3 3.5 5 7.25 9.1 12 25.1 64.5 100 1000 1e4 1e8]
        p8c_chk(sprintf('psi_%d_%s', k, strrep(sprintf('%g', xv), '.', 'p')), @() psi(k, xv));
    end
end
p8c_chk('psi_half', @() psi(0.5));
p8c_chk('psi_three_halves', @() psi(1.5));
p8c_chk('psi_s', @() psi(1, single([0.5 1.5 2.5 10.5])));
