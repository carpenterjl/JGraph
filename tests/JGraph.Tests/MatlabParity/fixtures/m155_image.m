% m155_image.m -- conv2 over the shapes item 08 (ADR 0155) reroutes to the packed column-major
% kernels: the d06 pipeline at a quarter of its side (the 21-tap separable blur of a procedural
% field, the 9-by-9 box density of its edge mask), asymmetric and even taps, a general dense
% kernel, and Inf and NaN taps over a mask. Values are compared to R2025b within the kernel's own
% precision (the two engines sum in different orders); the bits between JGraph builds are pinned by
% head2head_v3/bits/bits_d06_image.m. The bits lines here are the sums that are exact in any
% order -- integer counts, and the same 1/81 added a count of times -- so they can be pinned to
% MATLAB's own bytes.
K = 256;
[X, Y] = meshgrid(linspace(-10, 10, K));
img = sin(3*X) .* cos(2*Y) + 0.6*sin(5*sqrt(X.^2 + Y.^2) + 1) + 0.3*cos(4*X - 3*Y);
img = (img - min(img(:))) / (max(img(:)) - min(img(:)));
gk = exp(-0.5 * ((-10:10) / 3.5).^2);
gk = gk / sum(gk);

% --- the blur row: separable, 21 taps each way, all three shapes ---
blurred = conv2(gk, gk, img, 'same');
chk('blur_same_shape', mat2str(size(blurred)), 'exact');
chk('blur_same_mean', sum(blurred(:)) / K^2, 'rel=1e-13');
chk('blur_same_centre', blurred(128, 128), 'rel=1e-13');
chk('blur_same_corner', blurred(1, 1), 'rel=1e-13');
chk('blur_same_edge', blurred(K, 77), 'rel=1e-13');
bf = conv2(gk, gk, img, 'full');
chk('blur_full_shape', mat2str(size(bf)), 'exact');
chk('blur_full_mean', sum(bf(:)) / numel(bf), 'rel=1e-13');
chk('blur_full_corner', bf(1, 1), 'rel=1e-13');
chk('blur_full_last', bf(end, end), 'rel=1e-13');
bv = conv2(gk, gk, img, 'valid');
chk('blur_valid_shape', mat2str(size(bv)), 'exact');
chk('blur_valid_mean', sum(bv(:)) / numel(bv), 'rel=1e-13');
chk('blur_valid_corner', bv(1, 1), 'rel=1e-13');

% --- asymmetric taps of unequal length, and even taps: an axis swapped would show here ---
u = [0.1 0.7 0.2 -0.3 0.05];
v = [0.4 0.6 -0.1];
small = img(1:97, 1:131);
shapes = {'full', 'same', 'valid'};
for s = 1:3
    r = conv2(u, v, small, shapes{s});
    chk(sprintf('sep_asym_%s_shape', shapes{s}), mat2str(size(r)), 'exact');
    chk(sprintf('sep_asym_%s_mean', shapes{s}), sum(r(:)) / numel(r), 'rel=1e-13');
    chk(sprintf('sep_asym_%s_r2c3', shapes{s}), r(2, 3), 'rel=1e-13');
    chk(sprintf('sep_asym_%s_r3c2', shapes{s}), r(3, 2), 'rel=1e-13');
    chk(sprintf('sep_asym_%s_last', shapes{s}), r(end, end), 'rel=1e-13');
    e = conv2([1 2 3 4], [5 6], small, shapes{s});
    chk(sprintf('sep_even_%s_shape', shapes{s}), mat2str(size(e)), 'exact');
    chk(sprintf('sep_even_%s_mean', shapes{s}), sum(e(:)) / numel(e), 'rel=1e-13');
    chk(sprintf('sep_even_%s_r1c1', shapes{s}), e(1, 1), 'rel=1e-13');
    chk(sprintf('sep_even_%s_r4c5', shapes{s}), e(4, 5), 'rel=1e-13');
end

% --- the threshold row: a sparse 0/1 mask under a 9-by-9 box ---
[gx, gy] = gradient(blurred);
edges = sqrt(gx.^2 + gy.^2);
edges = edges / max(edges(:));
bw = double(edges > 0.25);
chk('bw_frac', sum(bw(:)) / K^2, 'exact');
dens = conv2(bw, ones(9)/81, 'same');
chk('dens_same_shape', mat2str(size(dens)), 'exact');
chk('dens_mean', sum(dens(:)) / K^2, 'rel=1e-13');
chk('dens_max', max(dens(:)), 'rel=1e-13');
chk('clean_frac', sum(dens(:) > 0.5) / K^2, 'exact');
bitsdir = tempname;
mkdir(bitsdir);
bits('counts_same', bitsdir, conv2(bw, ones(9), 'same'));
bits('counts_full', bitsdir, conv2(bw, ones(9), 'full'));
bits('counts_valid', bitsdir, conv2(bw, ones(9), 'valid'));
bits('dens_same', bitsdir, dens);
bits('counts_even', bitsdir, conv2(bw, ones(4, 6), 'same'));
bits('counts_asym', bitsdir, conv2(bw, [1 2 3; 4 5 6], 'full'));

% --- a general dense kernel over the dense field, both orientations ---
asym = [1 2 3; 4 5 6];
for s = 1:3
    g = conv2(small, asym, shapes{s});
    chk(sprintf('gen_asym_%s_shape', shapes{s}), mat2str(size(g)), 'exact');
    chk(sprintf('gen_asym_%s_mean', shapes{s}), sum(g(:)) / numel(g), 'rel=1e-13');
    chk(sprintf('gen_asym_%s_r2c3', shapes{s}), g(2, 3), 'rel=1e-13');
    chk(sprintf('gen_asym_%s_r3c2', shapes{s}), g(3, 2), 'rel=1e-13');
    t = conv2(small, asym', shapes{s});
    chk(sprintf('gen_asymT_%s_shape', shapes{s}), mat2str(size(t)), 'exact');
    chk(sprintf('gen_asymT_%s_mean', shapes{s}), sum(t(:)) / numel(t), 'rel=1e-13');
    chk(sprintf('gen_asymT_%s_r2c3', shapes{s}), t(2, 3), 'rel=1e-13');
end
big = conv2(small(1:5, 1:6), ones(9)/81, 'same');
chk('gen_big_same_shape', mat2str(size(big)), 'exact');
chk('gen_big_same_mean', sum(big(:)) / numel(big), 'rel=1e-13');
chk('gen_big_valid_shape', mat2str(size(conv2(small(1:5, 1:6), ones(9)/81, 'valid'))), 'exact');

% --- Inf and NaN taps over the mask. R2025b forms every product, so a zero source under an Inf
% or a NaN tap is a NaN in the answer; JGraph's scatter has skipped zero sources before any
% multiply since M96, and item 08 keeps that rule as its bit-for-bit contract (ADR 0155), so the
% counts differ wherever a zero sits under such a tap and are recorded as div lines ---
odd = [0 Inf 0; NaN 1 0.5; -2 0 3];
m = bw(1:97, 1:131);
o = conv2(m, odd, 'same');
chk('odd_nan_count', sum(isnan(o(:))), 'div=ADR0155');
chk('odd_inf_count', sum(isinf(o(:))), 'div=ADR0155');
chk('odd_finite_sum', sum(o(~isnan(o) & ~isinf(o))), 'div=ADR0155');
chk('odd_shape', mat2str(size(o)), 'exact');
od = conv2(small, odd, 'same');
chk('odd_dense_nan_count', sum(isnan(od(:))), 'exact');
chk('odd_dense_inf_count', sum(isinf(od(:))), 'exact');

% --- the same at the benchmark's density: at a quarter of the side the gradient is eight times
% steeper per pixel, so the 0.25 threshold keeps two thirds of the pixels; the top percentile
% of the edge map is the 0.8 %-dense mask the d06 row convolves ---
s = sort(edges(:));
sparse = double(edges > s(round(0.99 * numel(s))));
chk('sparse_frac', sum(sparse(:)) / K^2, 'exact');
sd = conv2(sparse, ones(9)/81, 'same');
chk('sparse_dens_mean', sum(sd(:)) / K^2, 'rel=1e-13');
chk('sparse_dens_max', max(sd(:)), 'rel=1e-13');
chk('sparse_clean_frac', sum(sd(:) > 0.5) / K^2, 'exact');
bits('sparse_counts_same', bitsdir, conv2(sparse, ones(9), 'same'));
bits('sparse_dens_same', bitsdir, sd);
so = conv2(sparse(1:97, 1:131), odd, 'same');
chk('sparse_odd_nan_count', sum(isnan(so(:))), 'div=ADR0155');
chk('sparse_odd_inf_count', sum(isinf(so(:))), 'div=ADR0155');
chk('sparse_odd_finite_sum', sum(so(~isnan(so) & ~isinf(so))), 'div=ADR0155');
chk('sparse_odd_zero_count', sum(so(:) == 0), 'div=ADR0155');

function chk(name, v, rule)
if ischar(v)
    fprintf('CHK|%s|%s|%s\n', name, v, rule);
else
    fprintf('CHK|%s|%.17g|%s\n', name, double(v), rule);
end
end

function bits(name, folder, x)
p = fullfile(folder, [name '.bits']);
fid = fopen(p, 'w');
writebits(fid, x);
fclose(fid);
fprintf('CHK|%s|file:%s|bits\n', name, p);
end

function writebits(fid, x)
fprintf(fid, '%s', class(x));
fprintf(fid, ' %d', size(x));
fprintf(fid, '\n');
if ~isfloat(x)
    x = double(x);
end
if isreal(x)
    writeplane(fid, x);
else
    writeplane(fid, real(x));
    writeplane(fid, imag(x));
end
end

function writeplane(fid, v)
v = v(:);
n = numel(v);
chunk = 65536;
for s = 1:chunk:n
    e = min(n, s + chunk - 1);
    h = num2hex(v(s:e));
    t = [h, repmat(newline, e - s + 1, 1)].';
    fprintf(fid, '%s', t);
end
end
