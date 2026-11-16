% record: -noFigureWindows
% Open item 344 (ADR 0380): a logical TIFF is a 1-bit file read back logical; a GIF holds writegif's
% indices (a logical's 0 and 1 with a black-and-white table, a double's x - 1 clamped after
% dataOutOfRange, a grey ramp without a map) and reads back as readgif's indices and table, an
% appended frame recast onto the first frame's table; imfinfo's Format is the format the bytes say
% (lower case, 'jpg' and 'tif' for every spelling, 'GIF' in capitals) with a BMP's and a GIF's own
% BitDepth and ColorType; imwrite warns signedPixelData before PNG, BMP and GIF refuse a signed class;
% and BMP files are read as readbmp reads them: RLE8 and RLE4 (indices, the map, bmpdrle's
% warnings), 16- and 32-bit files with and without bitfields, the map R2025b reads from between the
% header and the pixels, 12-, 108- and 124-byte headers, top-down rows and truncated data. The BMP
% files are built byte by byte here. The div rows are where readbmpdata is wrong (a bitfields sample
% plus 0.5 before uint8 rounds again, a 32-bit file's masks sorted with a zero alpha mask, a 10-bit
% mask read as a byte) and where its RLE8 delta moves from the wrong place (RLE4 and the format move
% from the cursor).
d = tempname;
mkdir(d);

% --- GIF writes and reads -----------------------------------------------------------------------------
fg = fullfile(d, 'g.gif');
u9b_chk('g_lg', @() oi344_rw(logical([0 1 1; 1 0 1]), [], fg));
u9b_chk('g_lg_info', @() oi344_info(logical([0 1 1; 1 0 1]), fg));
u9b_chk('g_d', @() oi344_rw([0 0.5 1], [], fg));
u9b_chk('g_d2', @() oi344_rw([1 2 3; 4.4 5.5 300], [], fg));
u9b_chk('g_d_nan', @() oi344_rw([NaN -3 2 Inf], [], fg));
u9b_chk('g_d_info', @() oi344_info([1 2 3], fg));
u9b_chk('g_u8', @() oi344_rw(uint8([0 1 2; 3 4 255]), [], fg));
u9b_chk('g_u8_info', @() oi344_info(uint8([0 1 2; 3 4 255]), fg));
u9b_chk('g_lg_map', @() oi344_rw(logical([0 1; 1 0]), [1 0 0; 0 0 1], fg));
u9b_chk('g_d_map', @() oi344_rw([1 2; 3 2], [1 0 0; 0 1 0; 0 0 1], fg));
u9b_chk('g_u8_map', @() oi344_rw(uint8([0 1; 2 1]), [1 0 0; 0 1 0; 0 0 1], fg));
u9b_chk('g_u8_map_over', @() oi344_rw(uint8([0 1; 5 1]), [1 0 0; 0 1 0; 0 0 1], fg));
u9b_chk('g_rgb', @() oi344_rw(uint8(cat(3, [1 2; 3 4], [5 6; 7 8], [9 10; 11 12])), [], fg));
u9b_chk('g_lg_one', @() oi344_rw(true(2), [], fg));
u9b_chk('g_w_logical', @() imread(oi344_write(logical([0 1 1; 1 0 1]), fullfile(d, 'w_logical.gif'))));
u9b_chk('g_w_double', @() imread(oi344_write([0 0.5 1; 0.25 0.75 1], fullfile(d, 'w_double.gif'))));
fa = fullfile(d, 'anim.gif');
imwrite(uint8([10 20; 30 40]), fa, 'LoopCount', Inf);
imwrite(uint8([50 60; 70 80]), fa, 'WriteMode', 'append');
u9b_chk('anim_f1', @() oi344_rdk(fa, 1));
u9b_chk('anim_f2', @() oi344_rdk(fa, 2));
u9b_chk('anim_n', @() numel(imfinfo(fa)));
fa2 = fullfile(d, 'anim2.gif');
imwrite(uint8([0 1; 2 0]), [1 0 0; 0 1 0; 0 0 1], fa2);
imwrite(uint8([1 0; 0 1]), [0 0 0; 1 1 1], fa2, 'WriteMode', 'append');
u9b_chk('anim2_f1', @() oi344_rdk(fa2, 1));
u9b_chk('anim2_f2', @() oi344_rdk(fa2, 2));
u9b_chk('anim2_info2', @() oi344_infk(fa2, 2));
fa3 = fullfile(d, 'anim3.gif');
imwrite(logical([0 1; 1 0]), fa3);
imwrite(logical([1 1; 0 0]), fa3, 'WriteMode', 'append');
u9b_chk('anim3_f2', @() oi344_rdk(fa3, 2));
fl = fullfile(d, 'lg.gif');
imwrite(logical([0 1; 1 0]), fl);
u9b_chk('g_lg_single', @() imread(fl));
imwrite(uint8(reshape(mod(0:299, 256), 15, 20)), fl);
u9b_chk('g_big', @() isequal(imread(fl), uint8(reshape(mod(0:299, 256), 15, 20))));
imwrite(uint8(magic(40)), fl);
u9b_chk('g_magic', @() isequal(imread(fl), uint8(magic(40))));

% --- A logical TIFF -------------------------------------------------------------------------------------
ft = fullfile(d, 't.tif');
u9b_chk('t_lg', @() oi344_rw(logical([0 1 1; 1 0 1]), [], ft));
u9b_chk('t_lg_info', @() oi344_info(logical([0 1 1; 1 0 1]), ft));
u9b_chk('t_lg_wide', @() oi344_rw(logical(mod(1:21, 3) == 0), [], ft));
u9b_chk('t_lg_rgb', @() oi344_rw(true(2, 2, 3), [], ft));
u9b_chk('t_lg_map', @() oi344_rw(logical([0 1; 1 0]), [1 0 0; 0 0 1], ft));
u9b_chk('t_lg_map_info', @() oi344_infomap(logical([0 1; 1 0]), [1 0 0; 0 0 1], ft));
u9b_chk('t_lg_big', @() isequal(imread(oi344_write(logical(mod(reshape(1:2775, 37, 75), 7) < 3), ft)), logical(mod(reshape(1:2775, 37, 75), 7) < 3)));
u9b_chk('t_lg_class', @() class(imread(oi344_write(false(3), ft))));
u9b_chk('t_w_logical', @() imread(oi344_write(logical([0 1 1; 1 0 1]), fullfile(d, 'w_logical.tif'))));

% --- imwrite's signedPixelData warning -------------------------------------------------------------------
w = {'png', 'bmp', 'gif', 'tif', 'jpg'};
for k = 1:numel(w)
    u9b_chk(['s_i16_' w{k}], @() oi344_signed(int16([0 1; 2 3]), fullfile(d, ['s.' w{k}])));
    u9b_chk(['s_i8_' w{k}], @() oi344_signed(int8([0 1; 2 3]), fullfile(d, ['s.' w{k}])));
end

% --- imfinfo's Format ------------------------------------------------------------------------------------
n = {'f.png', 'f.jpg', 'f.jpeg', 'f.tif', 'f.tiff', 'f.bmp', 'f.gif'};
for k = 1:numel(n)
    imwrite(uint8([1 2; 3 4]), fullfile(d, n{k}));
end
copyfile(fullfile(d, 'f.png'), fullfile(d, 'pngnamed.jpg'));
copyfile(fullfile(d, 'f.png'), fullfile(d, 'PNGUP.PNG'));
n = [n {'pngnamed.jpg', 'PNGUP.PNG'}];
for k = 1:numel(n)
    u9b_chk(['fmt_' strrep(n{k}, '.', '_')], @() getfield(imfinfo(fullfile(d, n{k})), 'Format'));
end

% --- BMP files R2025b reads: RLE8 and RLE4 ---------------------------------------------------------------
pal16 = [0:15; 16 * (0:15); 255 - 16 * (0:15)]';
pal4 = [10 20 30; 40 50 60; 70 80 90; 100 110 120];
f = oi344_bmp(d, 'rle8', 40, 4, 3, 8, 1, pal16, [], [2 5 2 7 0 0, 0 4 1 2 3 4 0 0, 1 9 0 3 10 11 12 0 0 1]);
u9b_chk('rle8', @() oi344_rd(f));
u9b_chk('rle8_info', @() oi344_inf(f));
f = oi344_bmp(d, 'rle8d', 40, 4, 3, 8, 1, pal16, [], [1 3 0 0, 0 2 1 1, 2 6 0 1]);
u9b_chk('rle8_delta', @() oi344_rd(f));
f = oi344_bmp(d, 'rle8n', 40, 2, 2, 8, 1, pal16, [], [2 3 0 0 2 4]);
u9b_chk('rle8_noend', @() oi344_rd(f));
f = oi344_bmp(d, 'rle4', 40, 5, 2, 4, 2, pal16, [], [5 18 0 0, 0 5 52 86 112 0 0 1]);
u9b_chk('rle4', @() oi344_rd(f));
u9b_chk('rle4_info', @() oi344_inf(f));
f = oi344_bmp(d, 'rle4b', 40, 4, 1, 4, 2, pal16, [], [3 33 0 1 64 0 0 0 1]);
u9b_chk('rle4_early_end', @() oi344_rd(f));
f = oi344_bmp(d, 'v5rle', 124, 2, 2, 8, 1, pal4, zeros(1, 16), [2 1 0 0 1 2 1 3 0 1]);
u9b_chk('v5_rle8', @() oi344_rd(f));
f = oi344_bmp(d, 'rle8over', 40, 2, 2, 8, 1, pal16, [], [5 3 0 0 2 4 0 1]);
u9b_chk('rle8_over', @() oi344_rd(f));
f = oi344_bmp(d, 'rle8bad', 40, 2, 2, 24, 1, [], [], [2 3 0 0 2 4 0 1]);
u9b_chk('rle_24', @() oi344_rd(f));
f = oi344_bmp(d, 'wrap', 40, 2, 2, 8, 1, pal16, [], [5 3 2 4 0 1]);
u9b_chk('rle8_wrap', @() imread(f));
f = oi344_bmp(d, 'wrap4', 40, 2, 2, 4, 2, pal16, [], [10 33 2 68 0 1]);
u9b_chk('rle4_wrap', @() imread(f));
f = oi344_bmp(d, 'delta_out', 40, 2, 2, 8, 1, pal16, [], [1 3 0 2 0 5 1 4 0 1]);
u9b_chk('rle8_delta_out', @() imread(f));
f = oi344_bmp(d, 'eol_extra', 40, 2, 1, 8, 1, pal16, [], [2 3 0 0 2 4 0 1]);
u9b_chk('rle8_extra_rows', @() imread(f));
f = oi344_bmp(d, 'trail', 40, 2, 1, 8, 1, pal16, [], [2 3 0 1 0 0]);
u9b_chk('rle8_trail', @() imread(f));
f = oi344_bmp(d, 'pad', 40, 2, 1, 8, 1, pal16, [], [2 3 0 0 0 1 0 0]);
u9b_chk('rle8_pad', @() oi344_rd(f));
f = oi344_bmp(d, 'abs_short', 40, 4, 1, 8, 1, pal16, [], [0 3 1 2]);
u9b_chk('rle8_abs_short', @() imread(f));
u9b_chk('rle8_abs_short_first', @() oi344_firstwarn(f));
f = oi344_bmp(d, 'run_short', 40, 4, 1, 8, 1, pal16, [], [2 1 3]);
u9b_chk('rle8_run_short', @() oi344_firstwarn(f));
f = oi344_bmp(d, 'esc_short', 40, 4, 1, 8, 1, pal16, [], [2 1 0]);
u9b_chk('rle8_esc_short', @() oi344_firstwarn(f));
f = oi344_bmp(d, 'delta_short', 40, 4, 1, 8, 1, pal16, [], [2 1 0 2 1]);
u9b_chk('rle8_delta_short', @() oi344_firstwarn(f));
f = oi344_bmp(d, 'eol_first', 40, 2, 1, 8, 1, pal16, [], [0 0 2 7 0 1]);
u9b_chk('rle8_eol_first', @() oi344_rd(f));
f = oi344_bmp(d, 'mix', 40, 2, 1, 4, 1, pal16, [], [2 7 0 1]);
u9b_chk('rle8_on_4bit', @() oi344_rd(f));
u9b_chk('rle8_on_4bit_info', @() oi344_inf(f));
f = oi344_bmp(d, 'd3', 40, 8, 2, 8, 1, pal16, [], [3 5 0 0 0 2 1 0 1 6 0 1]);
u9b_chk('rle8_delta_after_eol', @() imread(f));
f = oi344_bmp(d, 'd5', 40, 8, 1, 8, 1, pal16, [], [0 2 3 0 1 6 0 1]);
u9b_chk('rle8_delta_first', @() imread(f));
f = oi344_bmp(d, 'd7', 40, 8, 1, 4, 2, pal16, [], [3 33 0 2 2 0 1 68 0 1]);
u9b_chk('rle4_delta_after_run', @() imread(f));
f = oi344_bmp(d, 'd8', 40, 8, 1, 4, 2, pal16, [], [0 3 18 48 2 85 0 1]);
u9b_chk('rle4_abs_then_run', @() imread(f));
f = oi344_bmp(d, 'd1', 40, 8, 1, 8, 1, pal16, [], [3 5 0 2 2 0 1 6 0 1]);
oi344_div('rle8_delta_after_run', @() imread(f));
f = oi344_bmp(d, 'd2', 40, 8, 1, 8, 1, pal16, [], [0 3 1 2 3 0 0 2 2 0 1 6 0 1]);
oi344_div('rle8_delta_after_abs', @() imread(f));
f = oi344_bmp(d, 'd4', 40, 8, 1, 8, 1, pal16, [], [1 5 0 2 1 0 0 2 1 0 1 6 0 1]);
oi344_div('rle8_delta_twice', @() imread(f));
f = oi344_bmp(d, 'd6', 40, 4, 2, 8, 1, pal16, [], [2 5 0 2 0 1 1 6 0 1]);
oi344_div('rle8_delta_up', @() imread(f));

% --- BMP files R2025b reads: 16 and 32 bits, bitfields --------------------------------------------------
px = [hex2dec('7C00') hex2dec('03E0'); hex2dec('001F') hex2dec('4210')];
f = oi344_bmp(d, 'rgb16', 40, 2, 2, 16, 0, [], [], oi344_rows16(px));
u9b_chk('rgb16', @() oi344_rd(f));
u9b_chk('rgb16_info', @() oi344_inf(f));
px32 = [255 0 0 0 0 255 0 0 0 0 255 0 1 2 3 4];
f = oi344_bmp(d, 'rgb32', 40, 2, 2, 32, 0, [], [], px32);
u9b_chk('rgb32', @() oi344_rd(f));
u9b_chk('rgb32_info', @() oi344_inf(f));
f = oi344_bmp(d, 'bf32', 40, 2, 2, 32, 3, [], oi344_le([16711680 65280 255], 4), px32);
u9b_chk('bf32', @() oi344_rd(f));
u9b_chk('bf32_info', @() oi344_inf(f));
f = oi344_bmp(d, 'bf32r', 40, 2, 2, 32, 3, [], oi344_le([255 65280 16711680], 4), px32);
u9b_chk('bf32_swap', @() oi344_rd(f));
f = oi344_bmp(d, 'v4aff', 108, 2, 2, 32, 3, [], oi344_le([16711680 65280 255 4278190080], 4), px32);
u9b_chk('v4_bf32_alpha', @() oi344_rd(f));
f = oi344_bmp(d, 'v4rgb16', 108, 2, 2, 16, 0, [], zeros(1, 16), oi344_rows16(px));
u9b_chk('v4_rgb16', @() oi344_rd(f));
f = oi344_bmp(d, 'v4rgb32', 108, 2, 2, 32, 0, [], zeros(1, 16), px32);
u9b_chk('v4_rgb32', @() oi344_rd(f));
u9b_chk('v4_rgb32_info', @() oi344_inf(f));
pxw = [hex2dec('F800') hex2dec('07E0') hex2dec('001F'); hex2dec('FFFF') hex2dec('8410') hex2dec('0841')];
f = oi344_bmp(d, 'bf565', 40, 3, 2, 16, 3, [], oi344_le([hex2dec('F800') hex2dec('07E0') hex2dec('001F')], 4), oi344_rows16(pxw));
oi344_div('bf565', @() imread(f));
u9b_chk('bf565_info', @() oi344_inf(f));
f = oi344_bmp(d, 'bf555', 40, 2, 2, 16, 3, [], oi344_le([hex2dec('7C00') hex2dec('03E0') hex2dec('001F')], 4), oi344_rows16(px));
oi344_div('bf555', @() imread(f));
pxs = [hex2dec('0F00') hex2dec('00F0'); hex2dec('000F') hex2dec('0888')];
f = oi344_bmp(d, 'bf444', 40, 2, 2, 16, 3, [], oi344_le([hex2dec('0F00') hex2dec('00F0') hex2dec('000F')], 4), oi344_rows16(pxs));
oi344_div('bf444', @() imread(f));
f = oi344_bmp(d, 'bf16_0', 40, 2, 1, 16, 3, [], oi344_le([hex2dec('F800') hex2dec('07E0') hex2dec('001F')], 4), oi344_le([0 hex2dec('FFFF')], 2));
oi344_div('bf16_black', @() imread(f));
f = oi344_bmp(d, 'v4bf', 108, 2, 2, 16, 3, [], oi344_le([hex2dec('F800') hex2dec('07E0') hex2dec('001F') 0], 4), oi344_rows16([hex2dec('F800') hex2dec('07E0'); hex2dec('001F') hex2dec('FFFF')]));
oi344_div('v4_bf565', @() imread(f));
u9b_chk('v4_bf565_info', @() oi344_inf(f));
f = oi344_bmp(d, 'v4a0', 108, 2, 2, 32, 3, [], oi344_le([16711680 65280 255 0], 4), px32);
oi344_div('v4_bf32_a0', @() imread(f));
f = oi344_bmp(d, 'v5a0', 124, 2, 2, 32, 3, [], oi344_le([16711680 65280 255 0], 4), px32);
oi344_div('v5_bf32_a0', @() imread(f));
f = oi344_bmp(d, 'bf32_10', 40, 1, 1, 32, 3, [], oi344_le([1072693248 1047552 1023], 4), oi344_le(1023 * 2^20 + 512 * 2^10 + 1, 4));
oi344_div('bf32_10bit', @() imread(f));

% --- BMP files R2025b reads: headers, maps, rows ---------------------------------------------------------
f = oi344_bmp(d, 'v5i8', 124, 2, 2, 8, 0, pal4, zeros(1, 16), [0 1 0 0 2 3 0 0]);
u9b_chk('v5_i8', @() oi344_rd(f));
u9b_chk('v5_i8_info', @() oi344_inf(f));
f = oi344_bmp(d, 'td', 40, 2, -2, 8, 0, pal4, [], [0 1 0 0 2 3 0 0]);
u9b_chk('topdown', @() oi344_rd(f));
f = oi344_bmp(d, 'used', 40, 2, 1, 8, 0, pal4, [], [0 3 0 0], 2);
u9b_chk('clrused', @() oi344_rd(f));
f = oi344_bmp(d, 'i4', 40, 3, 1, 4, 0, pal16, [], [18 48 0 0]);
u9b_chk('i4', @() oi344_rd(f));
f = oi344_bmp(d, 'p24', 40, 1, 1, 24, 0, [1 2 3; 4 5 6], [], [10 20 30 0]);
u9b_chk('pal24', @() oi344_rd(f));
u9b_chk('pal24_info', @() oi344_inf(f));
f = oi344_bmp(d, 'np8', 40, 2, 1, 8, 0, zeros(0, 3), [], [7 9 0 0]);
u9b_chk('nopal8', @() oi344_rd(f));
u9b_chk('nopal8_info', @() oi344_inf(f));
f = fullfile(d, 'os2.bmp');
fid = fopen(f, 'w');
fwrite(fid, [66 77 oi344_le(36, 4) 0 0 0 0 oi344_le(32, 4) oi344_le(12, 4) oi344_le(1, 2) oi344_le(1, 2) oi344_le(1, 2) oi344_le(8, 2) 0 0 255 255 255 255 1 0 0 0], 'uint8');
fclose(fid);
u9b_chk('os2', @() oi344_rd(f));
u9b_chk('os2_info', @() oi344_inf(f));
f = oi344_bmp(d, 'trunc', 40, 4, 2, 8, 0, pal16, [], [1 2 3 4 5]);
u9b_chk('trunc8', @() oi344_rd(f));
f = oi344_bmp(d, 'c4', 40, 2, 1, 24, 4, [], [], [1 2 3 4 5 6 0 0]);
u9b_chk('comp4', @() oi344_rd(f));
u9b_chk('comp4_info', @() oi344_inf(f));
f = oi344_bmp(d, 'c5', 40, 2, 1, 24, 5, [], [], [1 2 3 4 5 6 0 0]);
u9b_chk('comp5', @() oi344_rd(f));
u9b_chk('comp5_info', @() oi344_inf(f));
rmdir(d, 's');

function p = oi344_write(x, p)
% Writes x to p and answers p.
imwrite(x, p);
end

function r = oi344_rw(x, m, p)
% Writes x (with map m when one is given) to p and reads it back with its map.
if isempty(m)
    imwrite(x, p);
else
    imwrite(x, m, p);
end
[v, map] = imread(p);
r = {v, size(map), map(1:min(end, 3), :)};
end

function r = oi344_info(x, p)
% imfinfo's BitDepth and ColorType for what was written.
imwrite(x, p);
s = imfinfo(p);
r = sprintf('%d %s', s(1).BitDepth, s(1).ColorType);
end

function r = oi344_infomap(x, m, p)
% imfinfo's BitDepth and ColorType for an indexed write.
imwrite(x, m, p);
s = imfinfo(p);
r = sprintf('%d %s', s(1).BitDepth, s(1).ColorType);
end

function r = oi344_signed(x, p)
% What imwrite does with a signed class, and the last warning it gave.
lastwarn('');
try
    imwrite(x, p);
    r = 'wrote';
catch e
    r = e.identifier;
end
[~, id] = lastwarn;
r = [r ' / ' id];
end

function r = oi344_rd(f)
% A file read with its map.
[v, map] = imread(f);
r = {v, size(map), map(1:min(end, 3), :)};
end

function r = oi344_rdk(f, k)
% Frame k of a file, with its map.
[v, map] = imread(f, k);
r = {v, size(map), map(1:min(end, 3), :)};
end

function r = oi344_inf(f)
% imfinfo's Format, BitDepth and ColorType.
s = imfinfo(f);
r = sprintf('%s %d %s', s.Format, s.BitDepth, s.ColorType);
end

function r = oi344_infk(f, k)
% imfinfo's Format, BitDepth and ColorType for frame k.
s = imfinfo(f);
s = s(k);
r = sprintf('%s %d %s', s.Format, s.BitDepth, s.ColorType);
end

function r = oi344_firstwarn(f)
% The warning before missingEndCode, which is made an error so lastwarn keeps the one before it.
w = warning('error', 'MATLAB:imagesci:bmpdrle:missingEndCode'); %#ok<CTPCT>
lastwarn('');
try
    v = imread(f);
    e = '';
catch err
    v = [];
    e = err.identifier;
end
warning(w);
[m, id] = lastwarn;
r = {v, e, id, m};
end

function b = oi344_le(v, n)
% Little-endian bytes of each value, n bytes each.
b = zeros(1, n * numel(v));
for i = 1:numel(v)
    x = v(i);
    for k = 1:n
        b((i - 1) * n + k) = mod(x, 256);
        x = floor(x / 256);
    end
end
end

function b = oi344_rows16(px)
% 16-bit pixels, bottom row first, each row padded to four bytes.
b = [];
for r = size(px, 1):-1:1
    row = oi344_le(px(r, :), 2);
    b = [b row zeros(1, mod(-numel(row), 4))]; %#ok<AGROW>
end
end

function f = oi344_bmp(d, name, hdr, w, h, bpp, comp, pal, extra, pixels, used)
% A BMP file from its parts: the header size, width, height (negative for top-down rows), bits,
% compression, the palette as 0-255 rows, bytes right after the first 40 header bytes (the masks),
% and the pixel bytes; used is biClrUsed (the palette's length unless given).
if nargin < 11, used = size(pal, 1); end
f = fullfile(d, [name '.bmp']);
palbytes = [];
for i = 1:size(pal, 1)
    palbytes = [palbytes pal(i, 3) pal(i, 2) pal(i, 1) 0]; %#ok<AGROW>
end
hbytes = oi344_le(h, 4);
if h < 0, hbytes = oi344_le(h + 2^32, 4); end
info = [oi344_le(hdr, 4) oi344_le(w, 4) hbytes oi344_le(1, 2) oi344_le(bpp, 2) oi344_le(comp, 4) ...
    oi344_le(numel(pixels), 4) oi344_le(0, 4) oi344_le(0, 4) oi344_le(used, 4) oi344_le(0, 4)];
if hdr > 40
    info = [info extra zeros(1, hdr - 40 - numel(extra))];
    extra = [];
end
offset = 14 + numel(info) + numel(extra) + numel(palbytes);
head = [66 77 oi344_le(offset + numel(pixels), 4) 0 0 0 0 oi344_le(offset, 4)];
fid = fopen(f, 'w');
fwrite(fid, [head info extra palbytes pixels], 'uint8');
fclose(fid);
end

function oi344_div(name, fn)
% One line in both engines for a recorded divergence: what the read answers.
try
    r = u9b_text(fn());
catch err
    r = err.identifier;
end
fprintf('CHK|%s|%s|div=ADR0380\n', name, r);
end
