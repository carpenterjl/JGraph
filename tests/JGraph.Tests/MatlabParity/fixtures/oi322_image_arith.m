% record: -noFigureWindows
% Open item 322 (ADR 0367): imadd, imsubtract, immultiply, imdivide, imabsdiff, imcomplement and
% imlincomb on double, single, integer and logical arrays by R2025b's class rules, with the scalar
% operand forms, the fast paths and the refusals; imwrite's writers refusing an int16 array at every
% depth (and the other integer classes they do not take); im2int16's rounding; imtile's array
% answer; and a logical written at BitDepth 16 (one div row: R2025b writes the logical's bytes as
% 16-bit samples, so its first samples are 256 and 1, and JGraph writes the logical's 0 and 1).
d = tempname;
mkdir(d);
p = fullfile(d, 'x.png');
A = [0.25 0.5; 0.75 1];
B = [0.5 0.75; 0.5 0.25];
u = uint8([10 200; 128 255]);
v = uint8([20 100; 128 1]);
s = int16([-30000 -5; 7 30000]);
t = int16([-10000 3; -7 10000]);
L = logical([1 0; 1 1]);
M = logical([1 1; 0 1]);

% --- imadd ----------------------------------------------------------------------------------------
u9b_chk('add_dd', @() imadd(A, B));
u9b_chk('add_d_scalar', @() imadd(A, 2));
u9b_chk('add_d_size', @() imadd(A, [1 2 3]));
u9b_chk('add_d_scalar_first', @() imadd(2, A));
u9b_chk('add_ss', @() imadd(single(A), single(B)));
u9b_chk('add_s_scalar', @() imadd(single(A), 0.1));
u9b_chk('add_ds', @() imadd(A, single(B)));
u9b_chk('add_uu', @() imadd(u, v));
u9b_chk('add_u_scalar', @() imadd(u, 50.5));
u9b_chk('add_u_neg', @() imadd(u, -20.5));
u9b_chk('add_u_logical', @() imadd(u, L));
u9b_chk('add_u_single_scalar', @() imadd(u, single(2)));
u9b_chk('add_ii', @() imadd(s, t));
u9b_chk('add_i_scalar', @() imadd(s, -3000.5));
u9b_chk('add_ll', @() imadd(L, M));
u9b_chk('add_l_scalar', @() imadd(L, 2.5));
u9b_chk('add_l_u', @() imadd(L, u));
u9b_chk('add_out_u16', @() imadd(u, v, 'uint16'));
u9b_chk('add_out_double', @() imadd(u, v, 'double'));
u9b_chk('add_d_out_u8', @() imadd(A * 100, B * 101, 'uint8'));
u9b_chk('add_out_bad', @() imadd(u, v, 'logical'));
u9b_chk('add_char', @() imadd('ab', 1));
u9b_chk('add_nan_u8', @() imadd(A, NaN));
u9b_chk('add_rgb', @() imadd(cat(3, u, v), 100));
u9b_chk('add_empty', @() imadd(zeros(0, 3), 1));

% --- imsubtract -----------------------------------------------------------------------------------
u9b_chk('sub_dd', @() imsubtract(A, B));
u9b_chk('sub_d_scalar', @() imsubtract(A, 2));
u9b_chk('sub_uu', @() imsubtract(v, u));
u9b_chk('sub_u_scalar', @() imsubtract(u, 10.5));
u9b_chk('sub_ii', @() imsubtract(s, t));
u9b_chk('sub_i_scalar', @() imsubtract(s, 5000));
u9b_chk('sub_ss', @() imsubtract(single(A), single(B)));
u9b_chk('sub_ll', @() imsubtract(L, M));
u9b_chk('sub_l_scalar', @() imsubtract(L, 0.5));
u9b_chk('sub_u_logical', @() imsubtract(u, L));
u9b_chk('sub_size', @() imsubtract(A, [1 2]));

% --- immultiply -----------------------------------------------------------------------------------
u9b_chk('mul_dd', @() immultiply(A, B));
u9b_chk('mul_d_scalar', @() immultiply(A, 3));
u9b_chk('mul_uu', @() immultiply(u, v));
u9b_chk('mul_u_scalar', @() immultiply(u, 0.5));
u9b_chk('mul_u_scalar_big', @() immultiply(u, 1.5));
u9b_chk('mul_ii', @() immultiply(s, int16([2 -3; 4 1])));
u9b_chk('mul_i_scalar', @() immultiply(s, -0.5));
u9b_chk('mul_ss', @() immultiply(single(A), single(B)));
u9b_chk('mul_ll', @() immultiply(L, M));
u9b_chk('mul_l_u', @() immultiply(L, u));
u9b_chk('mul_u_l', @() immultiply(u, M));
u9b_chk('mul_l_d', @() immultiply(L, A));
u9b_chk('mul_l_scalar', @() immultiply(L, 2));
u9b_chk('mul_l_size', @() immultiply(L, [1 2]));
u9b_chk('mul_class', @() immultiply(u, s));
u9b_chk('mul_size', @() immultiply(A, [1 2]));
u9b_chk('mul_empty', @() immultiply(zeros(0, 2), zeros(0, 2)));

% --- imdivide -------------------------------------------------------------------------------------
u9b_chk('div_dd', @() imdivide(A, B));
u9b_chk('div_d_scalar', @() imdivide(A, 4));
u9b_chk('div_uu', @() imdivide(u, v));
u9b_chk('div_u_zero', @() imdivide(u, uint8([0 0; 1 0])));
u9b_chk('div_u_scalar', @() imdivide(u, 4));
u9b_chk('div_ii', @() imdivide(s, t));
u9b_chk('div_i_zero', @() imdivide(s, int16([0 0; 0 0])));
u9b_chk('div_ss', @() imdivide(single(A), single(B)));
u9b_chk('div_ll', @() imdivide(L, M));
u9b_chk('div_l_scalar', @() imdivide(L, 4));
u9b_chk('div_l_d', @() imdivide(L, A));
u9b_chk('div_class', @() imdivide(A, single(B)));

% --- imabsdiff ------------------------------------------------------------------------------------
u9b_chk('abs_dd', @() imabsdiff(A, B));
u9b_chk('abs_uu', @() imabsdiff(u, v));
u9b_chk('abs_ii', @() imabsdiff(s, t));
u9b_chk('abs_i8', @() imabsdiff(int8([127 -128]), int8([-128 127])));
u9b_chk('abs_ss', @() imabsdiff(single(A), single(B)));
u9b_chk('abs_ll', @() imabsdiff(L, M));
u9b_chk('abs_class', @() imabsdiff(u, A));
u9b_chk('abs_size', @() imabsdiff(A, [1 2]));
u9b_chk('abs_scalar', @() imabsdiff(A, 1));

% --- imcomplement ---------------------------------------------------------------------------------
u9b_chk('cmp_d', @() imcomplement(A));
u9b_chk('cmp_d_out', @() imcomplement([2 -1]));
u9b_chk('cmp_s', @() imcomplement(single(A)));
u9b_chk('cmp_u8', @() imcomplement(u));
u9b_chk('cmp_u16', @() imcomplement(uint16([0 1000 65535])));
u9b_chk('cmp_i16', @() imcomplement(s));
u9b_chk('cmp_i8', @() imcomplement(int8([-128 0 127])));
u9b_chk('cmp_l', @() imcomplement(L));

% --- imlincomb ------------------------------------------------------------------------------------
u9b_chk('lin_uu', @() imlincomb(2, u, 0.5, v));
u9b_chk('lin_u_half', @() imlincomb(0.5, uint8([1 3 5 255])));
u9b_chk('lin_u_const', @() imlincomb(0.5, u, 10));
u9b_chk('lin_u_out', @() imlincomb(0.5, u, 'double'));
u9b_chk('lin_u_out_single', @() imlincomb(0.5, u, 'single'));
u9b_chk('lin_dd', @() imlincomb(2, A, -1, B));
u9b_chk('lin_d_out_u8', @() imlincomb(100, A, 'uint8'));
u9b_chk('lin_ii', @() imlincomb(-1, s, 0.5, t));
u9b_chk('lin_ll', @() imlincomb(1, L, 1, M));
u9b_chk('lin_plus', @() imlincomb(1, u, 1, v));
u9b_chk('lin_minus', @() imlincomb(1, u, -1, v));
u9b_chk('lin_class', @() imlincomb(1, u, 1, A));
u9b_chk('lin_size', @() imlincomb(1, A, 1, [1 2]));
u9b_chk('lin_ss', @() imlincomb(0.5, single(A), 0.25, single(B)));
u9b_chk('lin_s_double', @() imlincomb(0.5, single(A), 'double'));
u9b_chk('lin_three', @() imlincomb(0.5, A, 0.5, A, 0.5, A));
u9b_chk('lin_size_c', @() imlincomb(0.5, A, 1, [1 2]));
u9b_chk('lin_class_c', @() imlincomb(0.5, A, 1, single(A)));
u9b_chk('lin_class_l', @() imlincomb(1, L, 1, A));
u9b_chk('lin_d_l', @() imlincomb(1, A, 1, L));
u9b_chk('lin_k_single', @() imlincomb(single(0.5), A));
u9b_chk('lin_k_vector', @() imlincomb([1 2], A));
u9b_chk('lin_out_ambiguous', @() imlincomb(0.5, A, 'int'));
u9b_chk('lin_out_prefix', @() imlincomb(1, A, 'Doub'));
u9b_chk('lin_expand_bad', @() imlincomb(1, A, 1, [1 2 3]));
u9b_chk('lin_expand_single', @() imlincomb(1, A, 1, single([1 2])));
u9b_chk('lin_u_scalar', @() imlincomb(1, u, 2.5));
u9b_chk('lin_l_const', @() imlincomb(1, L, 2));
u9b_chk('lin_s_u8', @() imlincomb(2, single(A), 0.5, 'uint8'));
u9b_chk('lin_nan_u8', @() imlincomb(1, A, NaN, 'uint8'));
u9b_chk('lin_mixed_l', @() imlincomb(1, u, 1, L));
u9b_chk('lin_too_few', @() imlincomb(0.5));

% --- the checks, in R2025b's order and words -------------------------------------------------------
u9b_chk('add_out_doub', @() imadd(u, v, 'doub'));
u9b_chk('add_out_ambiguous', @() imadd(u, v, 'uint'));
u9b_chk('add_out_scalar', @() imadd(u, 2, 'logical'));
u9b_chk('add_out_number', @() imadd(u, v, 5));
u9b_chk('add_complex_x', @() imadd(1i, 1));
u9b_chk('add_complex_y', @() imadd(A, 1i));
u9b_chk('add_cell', @() imadd({1}, 1));
u9b_chk('add_one_arg', @() imadd(A));
u9b_chk('add_four_args', @() imadd(A, A, 'double', 1));
u9b_chk('add_int_classes', @() imadd(u, int16(u), 'uint8'));
u9b_chk('sub_l_u', @() imsubtract(L, u));
u9b_chk('sub_char', @() imsubtract('ab', 1));
u9b_chk('sub_i8', @() imsubtract(int8([-100 100]), int8([100 -100])));
u9b_chk('mul_char', @() immultiply(A, 'ab'));
u9b_chk('mul_complex', @() immultiply(1i, 2));
u9b_chk('mul_l_single', @() immultiply(L, single(A)));
u9b_chk('div_char', @() imdivide('ab', 1));
u9b_chk('div_char_y', @() imdivide(A, 'ab'));
u9b_chk('div_single_scalar', @() imdivide(A, single(2)));
u9b_chk('div_l_u', @() imdivide(L, u));
u9b_chk('div_u_by_zero', @() imdivide(u, 0));
u9b_chk('div_i_neg_zero', @() imdivide(-int16(5), 0));
u9b_chk('div_i_zero_zero', @() imdivide(int16(0), int16(0)));
u9b_chk('abs_char', @() imabsdiff('ab', 'cd'));
u9b_chk('abs_one_arg', @() imabsdiff(A));
u9b_chk('abs_i32', @() imabsdiff(int32([5 -2147483648]), int32([-5 2147483647])));
u9b_chk('abs_empty', @() imabsdiff(uint8([]), uint8([])));
u9b_chk('cmp_char', @() imcomplement('ab'));
u9b_chk('cmp_cell', @() imcomplement({1}));
u9b_chk('cmp_complex_single', @() imcomplement(single(1i)));
u9b_chk('cmp_u32', @() imcomplement(uint32([0 5])));
u9b_chk('cmp_empty', @() imcomplement([]));

% --- int16 written at every depth -----------------------------------------------------------------
w = int16([-32768 -1 0 1 255 256 32767]);
u9b_chk('i16_depth16', @() oi322_rw(w, p, {'BitDepth', 16}));
u9b_chk('i16_depth8', @() oi322_rw(w, p, {'BitDepth', 8}));
u9b_chk('i16_depth4', @() oi322_rw(w, p, {'BitDepth', 4}));
u9b_chk('i16_depth2', @() oi322_rw(w, p, {'BitDepth', 2}));
u9b_chk('i16_depth1', @() oi322_rw(w, p, {'BitDepth', 1}));
u9b_chk('i16_default', @() oi322_rw(w, p, {}));
u9b_chk('i16_tif', @() oi322_rw(w, fullfile(d, 'x.tif'), {}));
u9b_chk('i16_jpg', @() oi322_rw(w, fullfile(d, 'x.jpg'), {}));
u9b_chk('i16_bmp', @() oi322_rw(w, fullfile(d, 'x.bmp'), {}));
u9b_chk('i8_png', @() oi322_rw(int8([1 2]), p, {}));
u9b_chk('u32_png', @() oi322_rw(uint32([1 2]), p, {}));
u9b_chk('i8_tif', @() oi322_rw(int8([1 2]), fullfile(d, 'x.tif'), {}));
u9b_chk('i32_jpg', @() oi322_rw(int32([1 2]), fullfile(d, 'x.jpg'), {}));
u9b_chk('u32_bmp', @() oi322_rw(uint32([1 2]), fullfile(d, 'x.bmp'), {}));
u9b_chk('i16_gif', @() oi322_rw(w, fullfile(d, 'x.gif'), {}));
u9b_chk('u16_mul_read', @() immultiply(oi322_rw(uint16([100 200 40000]), p, {}), uint16([2 3 2])));
u9b_chk('i16_mul_im2', @() immultiply(im2int16(A), 2));
u9b_chk('i16_mul_im2_arr', @() immultiply(im2int16(A), int16([1 -1; 2 -2])));
u9b_chk('i16_add_im2', @() imadd(im2int16(A), -100));

% --- imtile ---------------------------------------------------------------------------------------
u9b_chk('tile_u8', @() imtile({u, v}));
u9b_chk('tile_u8_class', @() class(imtile({u, v})));
u9b_chk('tile_d3', @() imtile({A, B, A}));
u9b_chk('tile_d4', @() imtile({A, B, A, B}));
u9b_chk('tile_grid', @() imtile({A, B, A}, 'GridSize', [1 3]));
u9b_chk('tile_grid_nan', @() imtile({A, B, A}, 'GridSize', [NaN 2]));
u9b_chk('tile_border', @() imtile({u, v}, 'BorderSize', 1));
u9b_chk('tile_mixed', @() imtile({u, A}));
u9b_chk('tile_logical', @() imtile({L, M}));
u9b_chk('tile_rgb', @() size(imtile({cat(3, u, v, u), v})));
u9b_chk('tile_rgb_vals', @() imtile({cat(3, u, v, u), v}));
u9b_chk('tile_array3', @() imtile(cat(3, A, B)));
u9b_chk('tile_i16', @() imtile({s, t}));
u9b_chk('tile_single', @() class(imtile({single(A), single(B)})));
u9b_chk('tile_bg', @() imtile({u, v}, 'GridSize', [1 3], 'BackgroundColor', 'r'));
u9b_chk('tile_one', @() imtile({u}));
u9b_chk('tile_one_logical', @() imtile({L}));
u9b_chk('tile_logical_stack', @() imtile(cat(4, L, M)));
u9b_chk('tile_u16_u8', @() imtile({uint16([1 2; 3 4]), u}));
u9b_chk('tile_border_2', @() imtile({A, A}, 'BorderSize', [1 2]));
u9b_chk('tile_resize', @() imtile({A, [1 2 3; 4 5 6]}));
u9b_chk('tile_empty', @() imtile({}));
u9b_chk('tile_grid_one', @() imtile(u, 'GridSize', [2 2]));
u9b_chk('tile_scalar', @() imtile(5));
u9b_chk('tile_grid_nan_cols', @() imtile({A, A, A, A, A}, 'GridSize', [2 NaN]));
u9b_chk('tile_grid_short', @() imtile({A, A, A}, 'GridSize', [1 2]));
u9b_chk('tile_stack3', @() imtile(cat(3, A, A, A)));
u9b_chk('tile_stack4_rgb', @() imtile(cat(4, cat(3, A, A, A), cat(3, A, A, A))));
u9b_chk('tile_bg_border', @() imtile({u, u}, 'BackgroundColor', [0 1 0], 'BorderSize', 1));
u9b_chk('tile_bg_name', @() imtile({A, A}, 'BackgroundColor', 'g'));

% --- a logical at BitDepth 16 ---------------------------------------------------------------------
imwrite(logical([0 1 1 0 1 0 0 1]), p, 'BitDepth', 16);
r16 = imread(p);
fprintf('CHK|lg16_head|%s|div=ADR0367\n', u9b_text(r16(1:4)));
rmdir(d, 's');

function r = oi322_rw(x, p, opts)
% Writes x to p with the given options and reads it back, with warnings off so a refusal prints
% the same in both engines.
w = warning('off', 'all');
c = onCleanup(@() warning(w));
imwrite(x, p, opts{:});
r = imread(p);
end
