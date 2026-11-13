% record: -noFigureWindows
% Open item 273 (b) (ADR 0343): a logical picture is written as a 1-bit greyscale PNG and imread
% answers it as a logical matrix, which comparisons, indexing and sums take; BitDepth 1 writes the
% nonzero samples of any class. Probes imread_probe and bd1_probe (agent D's scratch).
p = [tempname '.png'];
A = magic(6) * 7;
imwrite(A > 100, p);
G = imread(p);
u9b_chk('logical_class', @() class(G));
u9b_chk('logical_size', @() size(G));
u9b_chk('logical_values', @() G);
u9b_chk('logical_isequal', @() isequal(G, A > 100));
u9b_chk('logical_gt0', @() G > 0);
u9b_chk('logical_sum', @() sum(G(:)));
u9b_chk('logical_index', @() G(2:4));
u9b_chk('logical_bitdepth', @() getfield(imfinfo(p), 'BitDepth'));
imwrite(true(3, 9), p);
u9b_chk('wide_true', @() imread(p));
imwrite(false(2, 5), p);
u9b_chk('all_false', @() imread(p));
imwrite(uint8([0 1 2 3 128 254 255]), p, 'BitDepth', 1);
u9b_chk('bd1_uint8', @() imread(p));
imwrite([0 0.1 0.5 1 0.0001], p, 'BitDepth', 1);
u9b_chk('bd1_double', @() imread(p));
imwrite(uint16([0 1 65535]), p, 'BitDepth', 1);
u9b_chk('bd1_uint16', @() imread(p));
imwrite(uint8(A), p);
u9b_chk('uint8_class', @() class(imread(p)));
u9b_chk('uint8_bitdepth', @() getfield(imfinfo(p), 'BitDepth'));
delete(p);
