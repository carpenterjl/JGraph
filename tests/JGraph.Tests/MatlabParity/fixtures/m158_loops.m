% m158_loops.m -- the loops item 11 (ADR 0158) threads, pinned to R2025b before any of them moves:
% the expensive scalar maps (11a), diff's contiguous branch (11b), discretize (11c), histcounts
% (11d) and sortrows (11e). Small cases spell every element (num2hex where a sign of zero or a NaN
% payload matters); the large cases sit above every threading threshold and are bits lines over
% the files both engines write, so a partition that moved one element would fail the line. The
% large series is built from products, sums and mod only — every one of them correctly rounded on
% both engines — so its bits are the same bits on both sides; a sine would not be (ADR 0093).
bitsdir = tempname;
mkdir(bitsdir);

phi = 0.618033988749895;
N = 3e6;
y = 2 * mod((1:N) * phi, 1) - 1 + 0.3 * (mod((1:N) * 0.381966011250105, 1) - 0.5);

% --- 11a: the expensive scalar maps ---
xs = 0.1 + 19.9 * mod((1:12) * phi, 1);
pts('erf', erf(xs / 5 - 2), 'rel=1e-13');
pts('erfc', erfc(xs / 5 - 2), 'rel=1e-13');
pts('gamma', gamma(xs), 'rel=1e-13');
pts('gammaln', gammaln(xs), 'rel=1e-13');
pts('besseli', besseli(0, xs / 4), 'rel=1e-12');
pts('besselk', besselk(0, xs), 'rel=1e-12');
pts('besselj', besselj(1, xs), 'rel=1e-12');
pts('bessely', bessely(1, xs), 'rel=1e-12');
pts('besseli_scaled', besseli(2, xs, 1), 'rel=1e-12');
pts('besselk_scaled', besselk(2, xs, 1), 'rel=1e-12');
pts('besselk_nu_row', besselk([0 1 2], 3), 'rel=1e-12');
z = 0.1 + 19.9 * mod((1:3e5) * phi, 1);
chk('erf_3e5', sum(erf(z / 5 - 2)) / 3e5, 'rel=1e-12');
chk('gamma_3e5', sum(gamma(z / 4)) / 3e5, 'rel=1e-12');
chk('gammaln_3e5', sum(gammaln(z)) / 3e5, 'rel=1e-12');
chk('besseli_3e5', sum(besseli(0, z / 4)) / 3e5, 'rel=1e-11');
chk('besselk_3e5', sum(besselk(0, z)) / 3e5, 'rel=1e-11');
chk('besseli_shape', mat2str(size(besseli(0, reshape(z(1:12), 3, 4)))), 'exact');
clear z;

% --- 11b: diff ---
d = [0 -0 0 -0 1 -1 Inf -Inf NaN 0 Inf Inf -Inf -Inf];
chk('diff_signs', hexrow(diff(d)), 'exact');
chk('diff_signs_2', hexrow(diff(d, 2)), 'exact');
pay = hex2num('7ff8000000000123');
chk('diff_nan_payload', hexrow(diff([pay 1 pay -pay 2])), 'exact');
chk('diff_col', hexrow(diff([1; 4; 9; 16; 25])), 'exact');
M = reshape((1:12) .^ 2, 3, 4);
chk('diff_mat', mat2str(diff(M)), 'exact');
chk('diff_mat_dim2', mat2str(diff(M, 1, 2)), 'exact');
chk('diff_mat_order2', mat2str(diff(M, 2)), 'exact');
chk('diff_mat_order3_dim2', mat2str(diff(M, 3, 2)), 'exact');
chk('diff_mat_order_too_high', mat2str(size(diff(M, 5))), 'exact');
chk('diff_row_order_too_high', mat2str(size(diff([1 2 3], 4))), 'exact');
chk('diff_col_order_too_high', mat2str(size(diff([1; 2; 3], 3))), 'exact');
chk('diff_dim2_too_high', mat2str(size(diff(M, 5, 2))), 'exact');
chk('diff_scalar', mat2str(size(diff(7))), 'exact');
chk('diff_empty', mat2str(size(diff([]))), 'exact');
chk('diff_zero_rows', mat2str(size(diff(zeros(0, 3)))), 'exact');
chk('diff_two', mat2str(diff([3 8])), 'exact');
chk('diff_three_twice', mat2str(diff([1 4 9], 2)), 'exact');
bits('diff_3M', bitsdir, diff(y));
bits('diff_3M_order2', bitsdir, diff(y, 2));
bits('diff_3M_col', bitsdir, diff(y.'));
bits('diff_3M_rows', bitsdir, diff(reshape(y, 3, [])));
bits('diff_3M_dim2', bitsdir, diff(reshape(y, 3, []), 1, 2));
bits('diff_3M_wide', bitsdir, diff(reshape(y, [], 4)));
bits('diff_3M_wide_dim2', bitsdir, diff(reshape(y, [], 4), 1, 2));
y0 = y;
y0(1:1000:end) = 0;
y0(2:1000:end) = -0;
y0(3:1000:end) = 0;
y0(500:997:end) = NaN;
y0(700:1499:end) = Inf;
y0(701:1499:end) = Inf;
bits('diff_3M_specials', bitsdir, diff(y0));
bits('diff_3M_specials_order2', bitsdir, diff(y0, 2));
clear y0;

% --- 11c: discretize ---
e = [0 0.25 0.5 0.75 1];
v = [e, e + eps(e), e - eps(e), -Inf, Inf, NaN, -1, 2, 0.1, 0.999999];
chk('disc_left', mat2str(discretize(v, e)), 'exact');
chk('disc_right', mat2str(discretize(v, e, 'IncludedEdge', 'right')), 'exact');
chk('disc_values', mat2str(discretize(v, e, [10 20 30 40])), 'exact');
chk('disc_values_right', mat2str(discretize(v, e, [10 20 30 40], 'IncludedEdge', 'right')), 'exact');
chk('disc_matrix', mat2str(discretize([0.1 0.6; 0.3 0.9], e)), 'exact');
chk('disc_column', mat2str(discretize([0.1; 0.6; 1], e)), 'exact');
chk('disc_uneven', mat2str(discretize(v, [0 0.1 0.15 0.9 1])), 'exact');
chk('disc_uneven_right', mat2str(discretize(v, [0 0.1 0.15 0.9 1], 'IncludedEdge', 'right')), 'exact');
[b4, e4] = discretize(v, 4);
chk('disc_count', mat2str(b4), 'exact');
chk('disc_count_edges', sprintf('%.17g,', e4), 'exact');
chk('disc_empty', mat2str(size(discretize(zeros(0, 3), e))), 'exact');
chk('disc_one_bin', mat2str(discretize([0 0.5 1 1.5], [0 1])), 'exact');
fixed_edges = linspace(min(y), max(y), 257);
bins = discretize(y, fixed_edges);
chk('disc_3M_sum', sum(bins(~isnan(bins))), 'exact');
chk('disc_3M_nan', sum(isnan(bins)), 'exact');
bits('disc_3M', bitsdir, bins);
bits('disc_3M_right', bitsdir, discretize(y, fixed_edges, 'IncludedEdge', 'right'));
bits('disc_3M_values', bitsdir, discretize(y, fixed_edges, 256:-1:1));
bits('disc_3M_col', bitsdir, discretize(y.', fixed_edges));
bits('disc_3M_uneven', bitsdir, discretize(y, [-1.5 -1 -0.5 -0.25 0 0.1 0.2 0.3 0.6 1.5]));
bits('disc_3M_on_edges', bitsdir, discretize([y(1:1000) fixed_edges fixed_edges - eps(fixed_edges) fixed_edges + eps(fixed_edges) y(1001:end)], fixed_edges));
clear bins;

% --- 11d: histcounts ---
h = [1 2 Inf -Inf NaN 3 2 2 0 5];
[n1, e1] = histcounts(h, [0 2 Inf]);
chk('hist_explicit_inf', mat2str([n1, e1]), 'exact');
[n2, e2] = histcounts(h);
chk('hist_auto', mat2str(n2), 'exact');
chk('hist_auto_edges', sprintf('%.17g,', e2), 'exact');
[n3, e3, b3] = histcounts(h, 4);
chk('hist_count4', mat2str(n3), 'exact');
chk('hist_count4_edges', sprintf('%.17g,', e3), 'exact');
chk('hist_count4_bin', mat2str(b3), 'exact');
[n4, e4, b4] = histcounts(h, [-1 0 2 4 6]);
chk('hist_edges_bin', mat2str([n4, e4, b4]), 'exact');
[n5, e5] = histcounts(h, 'NumBins', 3);
chk('hist_numbins', mat2str([n5, e5]), 'exact');
[n6, e6] = histcounts(h, 'BinLimits', [1 4], 'NumBins', 3);
chk('hist_limits', mat2str([n6, e6]), 'exact');
[n7, e7] = histcounts(h, 'BinWidth', 1.5);
chk('hist_width', mat2str([n7, e7]), 'exact');
chk('hist_prob', mat2str(histcounts(h, [0 2 4 6], 'Normalization', 'probability')), 'exact');
chk('hist_cdf', mat2str(histcounts(h, [0 2 4 6], 'Normalization', 'cdf')), 'exact');
chk('hist_cumcount', mat2str(histcounts(h, [0 2 4 6], 'Normalization', 'cumcount')), 'exact');
chk('hist_countdensity', mat2str(histcounts(h, [0 1 4 6], 'Normalization', 'countdensity')), 'exact');
chk('hist_pdf', mat2str(histcounts(h, [0 1 4 6], 'Normalization', 'pdf')), 'exact');
chk('hist_matrix', mat2str(histcounts([1 2; 3 4], [0 2.5 5])), 'exact');
[n8, e8, b8] = histcounts([1 2; 3 4], [0 2.5 5]);
chk('hist_matrix_bin', mat2str(b8), 'exact');
chk('hist_empty', mat2str(histcounts([], [0 1 2])), 'exact');
chk('hist_all_nan', mat2str(histcounts([NaN NaN], [0 1 2])), 'exact');
chk('hist_one_value', mat2str(histcounts([2 2 2], 3)), 'exact');
[nz, ez] = histcounts([0 -0 0 1], 2);
chk('hist_zero_first', sprintf('%.17g,', [nz ez]), 'exact');
[nz, ez] = histcounts([-0 0 0 1], 2);
chk('hist_negzero_first', sprintf('%.17g,', [nz ez]), 'exact');
[nc, ec] = histcounts(y, 256);
chk('hist_3M_counts', mat2str(nc), 'exact');
chk('hist_3M_edges', sprintf('%.17g,', ec), 'exact');
[nc, ec] = histcounts(y, 100);
chk('hist_3M_100_counts', mat2str(nc), 'exact');
chk('hist_3M_100_edges', sprintf('%.17g,', ec), 'exact');
[nc, ec] = histcounts(y, 7);
chk('hist_3M_7_counts', mat2str(nc), 'exact');
chk('hist_3M_7_edges', sprintf('%.17g,', ec), 'exact');
[nc, ec] = histcounts(y * 1000, 33);
chk('hist_3M_scaled_counts', mat2str(nc), 'exact');
chk('hist_3M_scaled_edges', sprintf('%.17g,', ec), 'exact');
[~, ~, bc] = histcounts(y, 256);
bits('hist_3M_bin', bitsdir, bc);
clear bc;
[nf, ef] = histcounts(y, fixed_edges);
chk('hist_3M_fixed', mat2str(nf), 'exact');
yi = [y(1:100) Inf -Inf NaN y(101:200)];
[ni, ei] = histcounts(yi, 8);
chk('hist_auto_excludes_inf', mat2str([ni, ei]), 'exact');
chk('hist_explicit_counts_inf', mat2str(histcounts(yi, [-2 0 2 Inf])), 'exact');
yj = y;
yj(1:1000:end) = Inf;
yj(2:1000:end) = -Inf;
yj(3:1000:end) = NaN;
[ni, ei] = histcounts(yj, 64);
chk('hist_3M_specials_counts', mat2str(ni), 'exact');
chk('hist_3M_specials_edges', sprintf('%.17g,', ei), 'exact');
chk('hist_3M_specials_explicit', mat2str(histcounts(yj, [-Inf -1 0 1 Inf])), 'exact');
clear yj;
chk('hist_3M_prob', sprintf('%.17g,', histcounts(y, 16, 'Normalization', 'probability')), 'exact');
chk('hist_3M_cumcount', mat2str(histcounts(y, 16, 'Normalization', 'cumcount')), 'exact');
many = linspace(min(y), max(y), 65537);
bits('hist_3M_65536', bitsdir, histcounts(y, many));
bits('hist_3M_70000', bitsdir, histcounts(y, linspace(min(y), max(y), 70001)));
bits('hist_3M_col', bitsdir, histcounts(y.', many));
[~, ~, bm] = histcounts(y, many);
bits('hist_3M_65536_bin', bitsdir, bm);
clear bm many;

% --- 11e: sortrows ---
A = [3 1; 1 2; 3 0; NaN 1; 1 NaN; -0 5; 0 4; 0 3; -0 2; 1 2; NaN NaN; 3 1];
[B, i] = sortrows(A);
chk('sr_default', hexrow(B), 'exact');
chk('sr_default_idx', mat2str(i), 'exact');
chk('sr_default_perm', double(isequaln(B, A(i, :))), 'exact');
[B, i] = sortrows(A, -1);
chk('sr_desc1', hexrow(B), 'exact');
chk('sr_desc1_idx', mat2str(i), 'exact');
[B, i] = sortrows(A, [2 -1]);
chk('sr_mixed', hexrow(B), 'exact');
chk('sr_mixed_idx', mat2str(i), 'exact');
[B, i] = sortrows(A, 'descend');
chk('sr_descend', hexrow(B), 'exact');
chk('sr_descend_idx', mat2str(i), 'exact');
[B, i] = sortrows(A, [1 2], {'ascend', 'descend'});
chk('sr_words', hexrow(B), 'exact');
chk('sr_words_idx', mat2str(i), 'exact');
[B, i] = sortrows(A, 2);
chk('sr_col2', hexrow(B), 'exact');
chk('sr_col2_idx', mat2str(i), 'exact');
[B, i] = sortrows([0; -0; 0; -0]);
chk('sr_zeros_pm', hexrow(B), 'exact');
chk('sr_zeros_pm_idx', mat2str(i), 'exact');
[B, i] = sortrows([-0; 0; -0; 0]);
chk('sr_zeros_mp', hexrow(B), 'exact');
chk('sr_zeros_mp_idx', mat2str(i), 'exact');
[B, i] = sortrows([-0; 0; -0; 0], -1);
chk('sr_zeros_mp_desc', hexrow(B), 'exact');
chk('sr_zeros_mp_desc_idx', mat2str(i), 'exact');
[B, i] = sortrows([-0 1; 0 2; -0 3; 0 4], [1 -2]);
chk('sr_zeros_two_keys', hexrow(B), 'exact');
chk('sr_zeros_two_keys_idx', mat2str(i), 'exact');
[B, i] = sortrows([NaN; 1; NaN; -Inf; Inf; NaN], -1);
chk('sr_nan_desc', hexrow(B), 'exact');
chk('sr_nan_desc_idx', mat2str(i), 'exact');
[B, i] = sortrows(zeros(0, 3));
chk('sr_empty', mat2str([size(B), size(i)]), 'exact');
[B, i] = sortrows([4 2 9]);
chk('sr_one_row', mat2str([B, i]), 'exact');
R = 6e5;
T = [mod((1:R) * phi, 1)' mod((1:R) * 0.381966011250105, 1)' (1:R)'];
[TS, it] = sortrows(T, 2);
chk('sr_600k_head', TS(1, 3) + TS(end, 3) + sum(TS(1:100, 1)), 'exact');
chk('sr_600k_perm', double(isequal(TS, T(it, :))), 'exact');
bits('sr_600k', bitsdir, TS);
bits('sr_600k_idx', bitsdir, it);
[TS, it] = sortrows(T, -2);
bits('sr_600k_desc', bitsdir, TS);
bits('sr_600k_desc_idx', bitsdir, it);
Q = [round(T(:, 1) * 50) round(T(:, 2) * 20) T(:, 3)];
Q(1:7:end, 1) = -Q(1:7:end, 1);
Q(3:11:end, 2) = NaN;
Q(5:13:end, 1) = -0;
Q(9:13:end, 1) = 0;
[QS, iq] = sortrows(Q, [1 -2]);
bits('sr_600k_ties', bitsdir, QS);
bits('sr_600k_ties_idx', bitsdir, iq);
[QS, iq] = sortrows(Q, [-2 1]);
bits('sr_600k_ties_2', bitsdir, QS);
bits('sr_600k_ties_2_idx', bitsdir, iq);
[QS, iq] = sortrows(Q(:, 1));
bits('sr_600k_zeros', bitsdir, QS);
bits('sr_600k_zeros_idx', bitsdir, iq);
[QS, iq] = sortrows(Q, 'descend');
bits('sr_600k_all_desc', bitsdir, QS);
bits('sr_600k_all_desc_idx', bitsdir, iq);
clear T TS Q QS it iq;
R = 11e5;
T = [mod((1:R) * phi, 1)' mod((1:R) * 0.381966011250105, 1)'];
[TS, it] = sortrows(T, 2);
bits('sr_1100k', bitsdir, TS);
bits('sr_1100k_idx', bitsdir, it);
S = sortrows(T, [-1 2]);
bits('sr_1100k_desc_first', bitsdir, S);
clear T TS S it;

function pts(name, v, rule)
for k = 1:numel(v)
    chk(sprintf('%s_%02d', name, k), v(k), rule);
end
end

function t = hexrow(x)
t = reshape(num2hex(x(:)).', 1, []);
end

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
