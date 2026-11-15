% record: -noFigureWindows
% Open item 335 (ADR 0373): imcomplement of a complex double; imresize's [r NaN] sizes (rounded up)
% and its one scale for both directions, a scale applied as given, and a logical array answered as
% a logical; imtile of file names, of an indexed image with its colour map, and its refusal of
% ThumbnailSize "auto"; imwrite's class refusals for every class into PNG, TIFF, JPEG, BMP and GIF;
% uint32 and uint64 into JPEG; a greyscale or logical JPEG read back as one plane; and the BMP writer
% (writebmp's layout, an indexed BMP read back with its indices and palette). Resized values are
% rounded to 1e-8, since the two engines' kernels differ in the last place. JPEG values are only
% compared on flat pictures (the two encoders round a detailed picture differently). The div rows
% are 12- and 16-bit and lossless JPEG, which R2025b writes and JGraph refuses.
d = tempname;
mkdir(d);
A = reshape(1:35, 5, 7);

% --- imcomplement of a complex double ---------------------------------------------------------------
u9b_chk('ic_c', @() imcomplement(1+2i));
u9b_chk('ic_c_real', @() isreal(imcomplement(complex(1, 0))));
u9b_chk('ic_c_real_val', @() imcomplement(complex([1 2], 0)));
u9b_chk('ic_c_mat', @() imcomplement([1+2i 3-1i; 0.5 2i]));
u9b_chk('ic_c_isreal_mat', @() isreal(imcomplement([1+2i 3-1i; 0.5 2i])));
u9b_chk('ic_c_single', @() imcomplement(single(1+2i)));

% --- imresize ---------------------------------------------------------------------------------------
u9b_chk('rs_nan', @() round(imresize(A, [3 NaN]), 8));
u9b_chk('rs_nan2', @() round(imresize(A, [NaN 4]), 8));
u9b_chk('rs_nan_sizes', @() [size(imresize(ones(4, 6), [3 NaN])) size(imresize(ones(6, 4), [NaN 3])) size(imresize(ones(3, 7), [2 NaN]))]);
u9b_chk('rs_scale03', @() round(imresize(A, 0.3), 8));
u9b_chk('rs_scale07', @() round(imresize(A, 0.7), 8));
u9b_chk('rs_scale_tenth', @() size(imresize(ones(10), 0.3)));
u9b_chk('rs_scale_pair', @() round(imresize(A, [0.5 0.7]), 8));
u9b_chk('rs_scale_vec', @() size(imresize(A, 'Scale', [0.5 0.3])));
u9b_chk('rs_nearest_nan', @() imresize(A, [3 NaN], 'nearest'));
u9b_chk('rs_bilinear_nan', @() round(imresize(A, [3 NaN], 'bilinear'), 8));
u9b_chk('rs_u8_nan', @() imresize(uint8(A * 7), [3 NaN]));
u9b_chk('rs_logical', @() imresize(logical([1 0 1 1 0; 0 1 1 0 1; 1 1 0 0 1]), [2 NaN]));
u9b_chk('rs_logical_up', @() imresize(logical([1 0; 0 1]), 2));
u9b_chk('rs_size_both', @() round(imresize(A, [3 4]), 8));
u9b_chk('rs_outsize', @() round(imresize(A, 'OutputSize', [3 NaN]), 8));
u9b_chk('rs_scale_opt', @() round(imresize(A, 'Scale', 0.3), 8));
u9b_chk('rs_up', @() round(imresize([1 2; 3 4], [3 NaN]), 8));
u9b_chk('rs_frac_size', @() size(imresize(A, [2.5 NaN])));

% --- imtile of file names ---------------------------------------------------------------------------
f1 = fullfile(d, 'a.png');
f2 = fullfile(d, 'b.png');
f3 = fullfile(d, 'c.png');
f4 = fullfile(d, 'g.bmp');
imwrite(uint8([10 20; 30 40]), f1);
imwrite(uint8(cat(3, [1 2; 3 4], [5 6; 7 8], [9 10; 11 12])), f2);
imwrite(uint8([0 1; 2 1]), [0 0 0; 1 0.5 0; 0 0 1], f3);
imwrite(uint8([0 128; 255 64]), f4);
u9b_chk('tile_files', @() imtile({f1, f2}));
u9b_chk('tile_files_str', @() imtile([string(f1) string(f2)]));
u9b_chk('tile_file_char', @() imtile(f1));
u9b_chk('tile_file_indexed', @() imtile({f3}));
u9b_chk('tile_file_bmp', @() imtile(f4));
u9b_chk('tile_files_grid', @() imtile({f1, f1, f1}, 'GridSize', [1 3]));
u9b_chk('tile_files_mixed', @() imtile({f1, uint8(1)}));
u9b_chk('tile_files_mixed2', @() imtile({uint8(1), f1}));
u9b_chk('tile_files_missing', @() imtile({'oi335_nope.png'}));

% --- imtile of an indexed image with its map --------------------------------------------------------
X = [1 2; 3 1];
map = [0 0 0; 1 0.5 0; 0 0 1];
u9b_chk('tile_map', @() imtile(X, map));
u9b_chk('tile_map_stack', @() imtile(cat(3, X, [2 2; 1 3]), map));
u9b_chk('tile_map_u8', @() imtile(uint8([0 1; 2 0]), map));
u9b_chk('tile_map_logical', @() imtile(logical([0 1; 1 0]), map));
u9b_chk('tile_map_clamp', @() imtile([0 5; 2 1], map));
u9b_chk('tile_map_cell', @() imtile({X, [2 2; 1 3]}, map));
u9b_chk('tile_map_grid', @() imtile(cat(3, X, X), map, 'GridSize', [2 1]));
u9b_chk('tile_map_i16', @() imtile(int16(X), map));
u9b_chk('tile_map_single', @() imtile(X, single(map)));
u9b_chk('tile_map_rgb', @() imtile(cat(3, A(1:2, 1:2) / 35, A(1:2, 1:2) / 35, A(1:2, 1:2) / 35), map));
u9b_chk('tile_nonchar', @() imtile(X, map, 5));

% --- imtile parameter classes -----------------------------------------------------------------------
u9b_chk('tile_auto', @() imtile({X, X}, 'ThumbnailSize', 'auto'));
u9b_chk('tile_auto_str', @() imtile({X, X}, 'ThumbnailSize', "auto"));
u9b_chk('tile_thumb_logical', @() imtile({X, X}, 'ThumbnailSize', true(1, 2)));
u9b_chk('tile_thumb_empty', @() imtile({X, X}, 'ThumbnailSize', []));
u9b_chk('tile_grid_char', @() imtile({X, X}, 'GridSize', 'ab'));
u9b_chk('tile_logical_resize', @() imtile({true(2, 2), logical([1 0 1; 0 1 0; 1 1 0])}));
u9b_chk('tile_logical_resize2', @() imtile({true(4, 4), logical([1 0 1 0 1; 0 1 0 1 0; 1 1 0 0 1])}));
u9b_chk('tile_thumb', @() round(imtile({A, A'}, 'ThumbnailSize', [3 NaN]), 8));
u9b_chk('tile_four_planes', @() imtile({ones(2, 2, 2)}));

% --- imwrite: every class into PNG, TIFF, BMP and GIF -----------------------------------------------
cls = {'logical', 'uint8', 'uint16', 'uint32', 'uint64', 'int8', 'int16', 'int32', 'int64', 'single', 'double'};
fmts = {'png', 'tif', 'bmp', 'gif'};
for i = 1:numel(cls)
    c = cls{i};
    switch c
        case 'logical', x = logical([0 1 1; 1 0 1]);
        case {'single', 'double'}, x = cast([0 0.5 1; 0.25 0.75 1], c);
        otherwise, x = cast([0 1 2; 3 4 5], c);
    end
    for j = 1:numel(fmts)
        % A logical TIFF, and a logical or double GIF, are written otherwise here (handoff 7A).
        if (strcmp(c, 'logical') && any(strcmp(fmts{j}, {'tif', 'gif'}))) || (strcmp(c, 'double') && strcmp(fmts{j}, 'gif'))
            continue
        end
        f = fullfile(d, ['w_' c '.' fmts{j}]);
        u9b_chk(['w_' c '_' fmts{j}], @() oi335_rw(x, f, {}));
    end
    % JPEG: the class and size read back (a detailed picture's values are the encoder's).
    u9b_chk(['w_' c '_jpg'], @() oi335_rwsize(x, fullfile(d, ['w_' c '.jpg']), {}));
end

% --- JPEG ---------------------------------------------------------------------------------------------
fj = fullfile(d, 'f.jpg');
u9b_chk('jpg_flat_u8', @() oi335_rw(uint8(100 * ones(8)), fj, {}));
u9b_chk('jpg_flat_rgb', @() size(oi335_rw(uint8(100 * ones(8, 8, 3)), fj, {})));
u9b_chk('jpg_flat_logical', @() oi335_rw(true(8), fj, {}));
u9b_chk('jpg_flat_single', @() oi335_rw(single(0.5 * ones(8)), fj, {}));
u9b_chk('jpg_flat_double', @() oi335_rw(0.25 * ones(8), fj, {}));
u9b_chk('jpg_flat_u32', @() oi335_rw(uint32(3 * ones(8)), fj, {}));
u9b_chk('jpg_flat_u64', @() oi335_rw(uint64(ones(8)), fj, {}));
u9b_chk('jpg_flat_u32_zero', @() oi335_rw(uint32(zeros(8)), fj, {}));
u9b_chk('jpg_u16', @() oi335_rw(uint16(ones(8)), fj, {}));
u9b_chk('jpg_u16_bd8', @() oi335_rw(uint16(ones(8)), fj, {'BitDepth', 8}));
u9b_chk('jpg_bd10', @() oi335_rw(uint8(ones(8)), fj, {'BitDepth', 10}));
u9b_chk('jpg_mode_bad', @() oi335_rw(uint8(ones(8)), fj, {'Mode', 'zz'}));
u9b_chk('jpg_rgb_bd16', @() oi335_rw(uint16(ones(8, 8, 3)), fj, {'BitDepth', 16}));
u9b_chk('jpg_bd16_lossy', @() oi335_rw(uint16(ones(8)), fj, {'BitDepth', 16, 'Mode', 'lossy'}));
u9b_chk('jpg_info_grey', @() oi335_info(uint8(100 * ones(8)), fj));
u9b_chk('jpg_info_logical', @() oi335_info(true(8), fj));
u9b_chk('jpg_info_rgb', @() oi335_info(uint8(100 * ones(8, 8, 3)), fj));
oi335_div('jpg_u16_bd12', @() oi335_rw(uint16(100 * ones(8)), fj, {'BitDepth', 12}));
oi335_div('jpg_u16_bd16', @() oi335_rw(uint16(1000 * ones(8)), fj, {'BitDepth', 16}));
oi335_div('jpg_u8_bd12', @() oi335_rw(uint8(100 * ones(8)), fj, {'BitDepth', 12}));
oi335_div('jpg_lossless8', @() oi335_rw(uint8(100 * ones(8)), fj, {'Mode', 'lossless'}));

% --- BMP ----------------------------------------------------------------------------------------------
fb = fullfile(d, 'b.bmp');
u9b_chk('bmp_rgb_u8', @() oi335_rw(uint8(cat(3, [1 2; 3 4], [5 6; 7 8], [9 10; 11 12])), fb, {}));
u9b_chk('bmp_rgb_d', @() oi335_rw(cat(3, [0 0.5; 1 0.25], [1 1; 0 0], [0.2 0.4; 0.6 0.8]), fb, {}));
u9b_chk('bmp_rgb_grey', @() size(oi335_rw(uint8(7 * ones(2, 2, 3)), fb, {})));
u9b_chk('bmp_grey_d', @() oi335_rw([0 0.5 1; 0.25 0.75 2], fb, {}));
u9b_chk('bmp_grey_d_neg', @() oi335_rw([-1 0.5 NaN], fb, {}));
u9b_chk('bmp_grey_s', @() oi335_rw(single([0 0.5 1]), fb, {}));
u9b_chk('bmp_grey_u8', @() oi335_rw(uint8([0 7 255; 128 1 2]), fb, {}));
u9b_chk('bmp_logical_wide', @() oi335_rw(logical(mod(1:33, 2)), fb, {}));
u9b_chk('bmp_rgb_odd', @() oi335_rw(uint8(reshape(1:45, 3, 5, 3)), fb, {}));
u9b_chk('bmp_info_grey', @() oi335_info(uint8([1 2; 3 4]), fb));
u9b_chk('bmp_info_rgb', @() oi335_info(uint8(ones(2, 2, 3)), fb));
u9b_chk('bmp_info_logical', @() oi335_info(true(2), fb));
u9b_chk('bmp_bytes_grey', @() oi335_bytes(uint8([1 2 3; 4 5 6]), fb));
u9b_chk('bmp_bytes_rgb', @() oi335_bytes(uint8(cat(3, [1 2; 3 4], [5 6; 7 8], [9 10; 11 12])), fb));
u9b_chk('bmp_bytes_logical', @() oi335_bytes(logical([1 0 1; 0 1 1]), fb));
u9b_chk('bmp_map_read', @() oi335_rwmap(uint8([0 1; 2 1]), [], fb));
u9b_chk('bmp_map_write', @() oi335_rwmap(uint8([0 1; 2 1]), [0 0 0; 1 0 0; 0 1 0], fb));
u9b_chk('bmp_map_write_d', @() oi335_rwmap([1 2; 3 2], [0 0 0; 1 0 0; 0 1 0], fb));
u9b_chk('bmp_map_write_lg', @() oi335_rwmap(logical([0 1; 1 0]), [1 0 0; 0 0 1], fb));
u9b_chk('bmp_map_lg3', @() oi335_rwmap(logical([0 1; 1 0]), [1 0 0; 0 0 1; 0 1 0], fb));
u9b_chk('bmp_logical_rgb', @() oi335_rw(true(2, 2, 3), fb, {}));
rmdir(d, 's');

function r = oi335_rw(x, p, opts)
% Writes x to p with the given options and reads it back, with warnings off so a refusal prints
% the same in both engines.
w = warning('off', 'all');
c = onCleanup(@() warning(w));
imwrite(x, p, opts{:});
r = imread(p);
end

function r = oi335_rwsize(x, p, opts)
% The class and size of what a write and a read give back.
v = oi335_rw(x, p, opts);
r = sprintf('%s %s', class(v), mat2str(size(v)));
end

function r = oi335_info(x, p)
% imfinfo's BitDepth and ColorType for what was written.
imwrite(x, p);
s = imfinfo(p);
r = sprintf('%d %s', s.BitDepth, s.ColorType);
end

function r = oi335_bytes(x, p)
% The bytes of the file written.
imwrite(x, p);
fid = fopen(p, 'r');
b = fread(fid, Inf, 'uint8')';
fclose(fid);
r = sprintf('%d ', b(1:min(end, 70)), numel(b));
end

function r = oi335_rwmap(x, m, p)
% An indexed write (or a plain one when m is empty), read back with its map.
if isempty(m)
    imwrite(x, p);
else
    imwrite(x, m, p);
end
[v, map] = imread(p);
r = {v, size(map), map(1:2, :)};
end

function oi335_div(name, fn)
% One line in both engines for a recorded divergence: the class and size, or ERR.
try
    v = fn();
    r = sprintf('%s %s', class(v), mat2str(size(v)));
catch
    r = 'ERR';
end
fprintf('CHK|%s|%s|div=ADR0373\n', name, r);
end
