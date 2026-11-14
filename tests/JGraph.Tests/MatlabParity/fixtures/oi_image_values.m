% record: -noFigureWindows
% Open items 292 and 294 (ADR 0356): what imread answers is a plain uint8 or uint16 array, which
% operators, indexing, reductions and the image makers take as they take any array of the class;
% and imwrite's and imfinfo's bit depths 2, 4, 8 and 16, a logical at depth 8, a double's rounding,
% and the empty map of a file with no palette. The pictures are written into a folder made here.
d = tempname;
mkdir(d);
A = magic(6) * 7;
g8 = uint8(A);
g16 = uint16(A) * 250;
rgb = uint8(cat(3, A, 255 - A, mod(A * 3, 256)));
p8 = fullfile(d, 'g8.png');
p16 = fullfile(d, 'g16.png');
prgb = fullfile(d, 'rgb.png');
q = fullfile(d, 'back.png');
imwrite(g8, p8);
imwrite(g16, p16);
imwrite(rgb, prgb);

% --- uint8 greyscale ------------------------------------------------------------------------------
G = imread(p8);
u9b_chk('g8_class', @() class(G));
u9b_chk('g8_size', @() size(G));
u9b_chk('g8_isequal', @() isequal(G, g8));
u9b_chk('g8_gt', @() G > 100);
u9b_chk('g8_eq', @() G == 7);
u9b_chk('g8_ge_array', @() G >= g8');
u9b_chk('g8_plus', @() G + 200);
u9b_chk('g8_minus', @() G - 50);
u9b_chk('g8_times', @() G .* 2);
u9b_chk('g8_rdivide', @() G ./ 3);
u9b_chk('g8_mtimes', @() 2 * G);
u9b_chk('g8_uminus', @() -G);
u9b_chk('g8_not', @() ~G);
u9b_chk('g8_plus_array', @() G + g8');
u9b_chk('g8_double', @() sum(double(G(:))));
u9b_chk('g8_sum', @() sum(G(:)));
u9b_chk('g8_sum_cols', @() sum(G));
u9b_chk('g8_mean', @() mean(G(:)));
u9b_chk('g8_max', @() max(G(:)));
u9b_chk('g8_min_cols', @() min(G));
u9b_chk('g8_index', @() G(2, 3));
u9b_chk('g8_end', @() G(end));
u9b_chk('g8_column', @() G(:, 1)');
u9b_chk('g8_logical_index', @() G(G > 240)');
u9b_chk('g8_horzcat', @() class([G G]));
u9b_chk('g8_mixed_cat', @() [G(1, 1:2) 300]);
H = G;
H(1, 1) = 300;
u9b_chk('g8_assign', @() H(1, 1));
u9b_chk('g8_isnumeric', @() [isnumeric(G) isinteger(G) isa(G, 'uint8') isreal(G)]);
u9b_chk('g8_mat2str', @() mat2str(G(1:2, 1:2)));
u9b_chk('g8_find', @() find(G == 7)');
u9b_chk('g8_int16', @() int16(G(1, :)) - 300);
imwrite(G, q);
u9b_chk('g8_roundtrip', @() isequal(imread(q), G));
f = figure('Visible', 'off');
h = image(G);
u9b_chk('g8_image_cdata', @() class(h.CData));
hi = imshow(G);
u9b_chk('g8_imshow_cdata', @() class(hi.CData));
u9b_chk('g8_imshow_size', @() size(hi.CData));
close(f);

% --- uint16 greyscale -----------------------------------------------------------------------------
G = imread(p16);
u9b_chk('g16_class', @() class(G));
u9b_chk('g16_isequal', @() isequal(G, g16));
u9b_chk('g16_gt', @() G > 30000);
u9b_chk('g16_plus', @() G(1, :) + 60000);
u9b_chk('g16_minus', @() G(1, :) - 10000);
u9b_chk('g16_rdivide', @() G(1, :) ./ 7);
u9b_chk('g16_sum', @() sum(G(:)));
u9b_chk('g16_mean', @() mean(G(:)));
u9b_chk('g16_double', @() double(G(2, :)) / 65535);
imwrite(G, q);
u9b_chk('g16_roundtrip', @() isequal(imread(q), G));

% --- uint8 RGB ------------------------------------------------------------------------------------
G = imread(prgb);
u9b_chk('rgb_class', @() class(G));
u9b_chk('rgb_size', @() size(G));
u9b_chk('rgb_isequal', @() isequal(G, rgb));
u9b_chk('rgb_gt_count', @() nnz(G > 128));
u9b_chk('rgb_gt_size', @() size(G > 128));
u9b_chk('rgb_plane', @() G(:, :, 2));
u9b_chk('rgb_pixel', @() squeeze(G(2, 3, :))');
u9b_chk('rgb_sum3', @() sum(G, 3));
u9b_chk('rgb_plus', @() G(1:2, 1:2, :) + 100);
u9b_chk('rgb_mean', @() mean(G(:)));
u9b_chk('rgb_numel', @() numel(G));
imwrite(G, q);
u9b_chk('rgb_roundtrip', @() isequal(imread(q), G));
f = figure('Visible', 'off');
h = image(G);
u9b_chk('rgb_image_cdata', @() [class(h.CData) ' ' mat2str(size(h.CData))]);
close(f);

% --- indexed and alpha ----------------------------------------------------------------------------
pind = fullfile(d, 'ind.png');
imwrite(uint8([0 1 2; 3 2 1]), [0 0 0; 1 0 0; 0 1 0; 0 0 1], pind);
[X, map] = imread(pind);
u9b_chk('ind_class', @() class(X));
u9b_chk('ind_values', @() X);
u9b_chk('ind_plus', @() X + 1);
u9b_chk('ind_map_size', @() size(map));
u9b_chk('ind_map', @() map(1:4, :));
pa = fullfile(d, 'alpha.png');
imwrite(g8, pa, 'Alpha', A / 252);
[G, ~, al] = imread(pa);
u9b_chk('alpha_image_class', @() [class(G) ' ' mat2str(size(G))]);
u9b_chk('alpha_class', @() class(al));
u9b_chk('alpha_values', @() al);
u9b_chk('alpha_gt', @() al > 128);
u9b_chk('alpha_isequal', @() isequal(G, g8));

% --- open item 294: bit depths --------------------------------------------------------------------
[X, map] = imread(p8);
u9b_chk('plain_map', @() size(map));
u9b_chk('plain_map_class', @() class(map));
p = fullfile(d, 'depth.png');
imwrite(uint8([0 40 42 43 85 127 128 170 212 213 255]), p, 'BitDepth', 2);
u9b_chk('bd2_uint8', @() imread(p));
u9b_chk('bd2_info', @() getfield(imfinfo(p), 'BitDepth'));
u9b_chk('bd2_type', @() getfield(imfinfo(p), 'ColorType'));
imwrite([0 0.16 0.17 0.2 0.5 0.83 0.84 1], p, 'BitDepth', 2);
u9b_chk('bd2_double', @() imread(p));
imwrite(uint16([0 10922 10923 32768 65535]), p, 'BitDepth', 2);
u9b_chk('bd2_uint16', @() imread(p));
imwrite(uint8(0:17:255), p, 'BitDepth', 4);
u9b_chk('bd4_uint8', @() imread(p));
u9b_chk('bd4_info', @() getfield(imfinfo(p), 'BitDepth'));
imwrite(uint8([8 9 25 26 127 128 246 247]), p, 'BitDepth', 4);
u9b_chk('bd4_uint8_edges', @() imread(p));
imwrite((0:15) / 15 - 0.02, p, 'BitDepth', 4);
u9b_chk('bd4_double', @() imread(p));
imwrite(uint8(magic(6) * 7), p, 'BitDepth', 4);
u9b_chk('bd4_magic', @() imread(p));
imwrite(logical([0 1 1 0]), p, 'BitDepth', 8);
u9b_chk('bd8_logical', @() imread(p));
u9b_chk('bd8_logical_info', @() getfield(imfinfo(p), 'BitDepth'));
imwrite(logical([0 1 1 0]), p, 'BitDepth', 4);
u9b_chk('bd4_logical', @() imread(p));
imwrite(uint8([0 1 128 255]), p, 'BitDepth', 16);
u9b_chk('bd16_uint8', @() imread(p));
imwrite(uint16([0 128 129 257 32768 65535]), p, 'BitDepth', 8);
u9b_chk('bd8_uint16', @() imread(p));
imwrite(magic(6) * 7 / 252, p);
u9b_chk('double_round', @() imread(p));
imwrite([0.5 1.5 2.5 3.5 126.5 127.5 253.5 254.5] / 255, p);
u9b_chk('double_halves', @() imread(p));
imwrite([0.5 1.5 2.5 3.5 32767.5 65534.5] / 65535, p, 'BitDepth', 16);
u9b_chk('double_halves16', @() imread(p));
imwrite(single([0.5 1.5 2.5 3.5] / 255), p);
u9b_chk('single_halves', @() imread(p));
imwrite(uint16(cat(3, A, 2 * A, 3 * A)) * 80, p);
u9b_chk('rgb16_class', @() class(imread(p)));
u9b_chk('rgb16_isequal', @() isequal(imread(p), uint16(cat(3, A, 2 * A, 3 * A)) * 80));
u9b_chk('rgb16_info', @() getfield(imfinfo(p), 'BitDepth'));
u9b_chk('rgb16_type', @() getfield(imfinfo(p), 'ColorType'));
imwrite(rgb, p, 'BitDepth', 16);
G = imread(p);
u9b_chk('rgb_bd16', @() squeeze(G(2, 3, :))');
try
    imwrite(rgb, p, 'BitDepth', 4);
    fprintf('CHK|rgb_bd4|written|exact\n');
catch err
    fprintf('CHK|rgb_bd4|%s|exact\n', err.identifier);
end
try
    imwrite(g8, p, 'BitDepth', 3);
    fprintf('CHK|g8_bd3|written|exact\n');
catch err
    fprintf('CHK|g8_bd3|%s|exact\n', err.identifier);
end
try
    imwrite(g8, p, 'BitDepth', 4, 'Alpha', A / 252);
    fprintf('CHK|g8_bd4_alpha|written|exact\n');
catch err
    fprintf('CHK|g8_bd4_alpha|%s|exact\n', err.identifier);
    fprintf('CHK|g8_bd4_alpha_msg|%s|exact\n', err.message);
end
imwrite(uint16(cat(3, [0 128 129 257 32768 65535], [0 128 129 257 32768 65535], zeros(1, 6))), p, 'BitDepth', 8);
G = imread(p);
u9b_chk('rgb16_bd8', @() G(:, :, 1));
imwrite(logical([0 1 1 0]), p, 'BitDepth', 2);
u9b_chk('bd2_logical', @() imread(p));
imwrite(uint8([0 64 128 255]), p, 'BitDepth', 8, 'Alpha', uint16([0 257 32768 65535]));
[~, ~, al] = imread(p);
u9b_chk('alpha_uint16_bd8', @() al);
imwrite(uint16([0 128 129 257]), p, 'Alpha', [0 0.5 1 1]);
[~, ~, al] = imread(p);
u9b_chk('alpha_double_bd16', @() al);
u9b_chk('info_g8', @() getfield(imfinfo(p8), 'BitDepth'));
u9b_chk('info_g8_type', @() getfield(imfinfo(p8), 'ColorType'));
u9b_chk('info_g16', @() getfield(imfinfo(p16), 'BitDepth'));
u9b_chk('info_rgb', @() getfield(imfinfo(prgb), 'BitDepth'));
u9b_chk('info_rgb_type', @() getfield(imfinfo(prgb), 'ColorType'));
u9b_chk('info_alpha', @() getfield(imfinfo(pa), 'BitDepth'));
u9b_chk('info_alpha_type', @() getfield(imfinfo(pa), 'ColorType'));
u9b_chk('info_ind', @() getfield(imfinfo(pind), 'BitDepth'));
u9b_chk('info_ind_type', @() getfield(imfinfo(pind), 'ColorType'));
imwrite(rgb, p, 'Alpha', A / 252);
u9b_chk('info_rgba', @() getfield(imfinfo(p), 'BitDepth'));
u9b_chk('info_rgba_type', @() getfield(imfinfo(p), 'ColorType'));

% --- the class verbs and image arithmetic take imread's array -------------------------------------
G = imread(p8);
H = imread(p16);
C = imread(prgb);
u9b_chk('v_im2double_u8', @() im2double(G(1, :)));
u9b_chk('v_im2double_u16', @() im2double(H(1, :)));
u9b_chk('v_im2double_rgb', @() size(im2double(C)));
u9b_chk('v_im2double_rgb_px', @() squeeze(im2double(C(2, 3, :)))');
u9b_chk('v_im2double_scalar', @() im2double(G(1, 2)));
u9b_chk('v_im2uint8_u16', @() im2uint8(H(1, :)));
u9b_chk('v_im2uint16_u8', @() im2uint16(G(1, :)));
u9b_chk('v_im2single_u8', @() im2single(G(1, 1:2)));
u9b_chk('v_im2uint8_rgb', @() class(im2uint8(C)));
u9b_chk('v_im2uint8_logical', @() im2uint8(G(1, :) > 100));
u9b_chk('v_im2uint8_back', @() im2uint8(im2double(G(1, :))));
u9b_chk('v_im2uint8_double', @() im2uint8([0 0.25 0.5 1 1.5 -1]));
u9b_chk('v_im2uint16_double', @() im2uint16([0 0.25 0.5 1]));
u9b_chk('v_im2single_double', @() im2single([0.1 0.5 2 -1]));
u9b_chk('v_im2double_double', @() im2double([0.1 0.5 2 -1]));
u9b_chk('v_imadd_u8', @() imadd(G(1, :), 50));
u9b_chk('v_imadd_u8_arr', @() imadd(G(1, :), G(2, :)));
u9b_chk('v_imsubtract_u8', @() imsubtract(G(1, :), 100));
u9b_chk('v_immultiply_u8', @() immultiply(G(1, :), 0.5));
u9b_chk('v_imdivide_u8', @() imdivide(G(1, :), 3));
u9b_chk('v_imadd_u16', @() imadd(H(1, :), 10000));
u9b_chk('v_imadd_rgb', @() imadd(C(1:2, 1:2, :), 10));
rmdir(d, 's');
