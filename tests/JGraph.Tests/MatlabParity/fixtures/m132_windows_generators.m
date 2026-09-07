% m132_windows_generators.m -- the Signal Processing Toolbox's windows, waveform generators,
% transforms and conversions (M132).
%
% Three kinds of line are pinned here.
%
% A *window* is pinned by two digests at five lengths -- the sum of its coefficients and the sum
% weighted by position -- and by three individual samples at length 64. The digests are the useful
% test: a coefficient computed by a formula that is right in algebra and evaluated in a different
% order is wrong in its last two figures, and summing sixty-four of them makes that visible where
% one of them would not. The weighted sum is there because a window reversed, or shifted by one
% sample, has the same plain sum. Both are pinned at rel=1e-13, which is tight enough to catch an
% expression evaluated in the wrong order and loose enough not to argue about the last bit of a
% cosine.
%
% A *generator* is pinned on the examples its own help page uses, at rel=1e-12, plus the edges that
% the formula does not decide: what a rectangular pulse does at its two edges, what a sawtooth does
% at exactly zero, what a Dirichlet kernel does where its denominator vanishes.
%
% A *transform* is pinned at rel=1e-10 on a chirp, because it is a Fourier transform and back and
% the two engines' transforms are the same butterflies in the same order but not the same code.
% Counts and lengths -- buffer's frame count, cceps's delay, seqperiod's period, digitrevorder's
% permutation -- are pinned exact, because each is a statement about the method and nothing hides a
% difference of one.

% --- the twenty windows ---------------------------------------------------------------------
plainNames = {'barthannwin', 'bartlett', 'bohmanwin', 'boxcar', 'parzenwin', 'rectwin', 'triang'};
flagNames = {'blackman', 'blackmanharris', 'flattopwin', 'hamming', 'hann', 'hanning', 'nuttallwin'};
lengths = [1 2 7 8 64];

for k = 1:numel(plainNames)
    nm = plainNames{k};
    for j = 1:numel(lengths)
        n = lengths(j);
        w = window(nm, n);
        chkwin(sprintf('%s_%d', nm, n), w);
    end
    chksamples(nm, window(nm, 64));
end

for k = 1:numel(flagNames)
    nm = flagNames{k};
    for j = 1:numel(lengths)
        n = lengths(j);
        chkwin(sprintf('%s_%d', nm, n), window(nm, n));
        chkwin(sprintf('%s_per_%d', nm, n), window(nm, n, 'periodic'));
    end
    chksamples(nm, window(nm, 64));
    chkwin(sprintf('%s_sym_16', nm), window(nm, 16, 'symmetric'));
end

% The five that take a shape parameter of their own. Each is asked at a length that makes its
% parameter bite and at the two lengths where no window has a shape.
for j = 1:numel(lengths)
    n = lengths(j);
    chkwin(sprintf('kaiser_%d', n), kaiser(n));
    chkwin(sprintf('gausswin_%d', n), gausswin(n));
    chkwin(sprintf('tukeywin_%d', n), tukeywin(n));
    chkwin(sprintf('chebwin_%d', n), chebwin(n));
    chkwin(sprintf('taylorwin_%d', n), taylorwin(n));
end

betas = [0 0.5 2.5 8 38];
for j = 1:numel(betas)
    chkwin(sprintf('kaiser_b%d', j), kaiser(64, betas(j)));
end

alphas = [0.5 2.5 5 10];
for j = 1:numel(alphas)
    chkwin(sprintf('gausswin_a%d', j), gausswin(64, alphas(j)));
end

ratios = [0 0.1 0.5 0.9 1];
for j = 1:numel(ratios)
    chkwin(sprintf('tukeywin_r%d', j), tukeywin(64, ratios(j)));
    chkwin(sprintf('tukeywin_odd_r%d', j), tukeywin(31, ratios(j)));
end

atten = [40 60 100 120];
for j = 1:numel(atten)
    chkwin(sprintf('chebwin_r%d', j), chebwin(64, atten(j)));
    chkwin(sprintf('chebwin_odd_r%d', j), chebwin(31, atten(j)));
end

chkwin('taylorwin_5_35', taylorwin(64, 5, -35));
chkwin('taylorwin_3_20', taylorwin(50, 3, -20));
chkwin('taylorwin_8_60', taylorwin(33, 8, -60));
chksamples('kaiser_b', kaiser(200, 2.5));
% chebwin's own arithmetic is a MEX file that cannot be read, so the classical algorithm is written
% out instead; the two agree to sixteen figures in the body of the window and to ten at its edges,
% where a hundred decibels of dynamic range passes through one transform. The digests above are
% pinned at rel=1e-13 and see the body; these three see the edges.
chksamples('chebwin_100', chebwin(64, 100), 1e-9);
chksamples('taylorwin_5', taylorwin(64, 5, -35));

% window called both ways round, which is the point of the name.
chkwin('window_handle', window(@blackmanharris, 32));
chkwin('window_gauss', window(@gausswin, 32, 2.5));
chkwin('window_taylor', window(@taylorwin, 32, 5, -35));

% A length that is not whole is rounded, and a length of zero is an empty column.
chkwin('hamming_round', hamming(8.4));
fprintf('CHK|rectwin_empty_size|%s|exact\n', mat2str(size(rectwin(0))));

% --- the colon operator, which every generator below reads its time axis from ------------------
% MATLAB does not compute a range as start plus index times step: it runs the first half forwards
% and the second half backwards from the last element, so the drift of a step that is not a binary
% fraction lands in the middle rather than at the end. Nothing sees the difference until something
% compares a range value against a boundary, which is what a pulse train does at every edge, so
% these are pinned exact.
tcolon = 0:1/1e3:0.1;
fprintf('CHK|colon_51|%.17g|exact\n', tcolon(51));
fprintf('CHK|colon_91|%.17g|exact\n', tcolon(91));
fprintf('CHK|colon_101|%.17g|exact\n', tcolon(101));
dcolon = 0:1/50:0.1;
fprintf('CHK|colon_d4|%.17g|exact\n', dcolon(4));
tenth = 0:0.1:1;
for k = 1:11
    fprintf('CHK|colon_tenth_%d|%.17g|exact\n', k, tenth(k));
end
third = 0:0.3:1;
for k = 1:4
    fprintf('CHK|colon_third_%d|%.17g|exact\n', k, third(k));
end
fprintf('CHK|colon_whole|%.17g|exact\n', sum(1:1000));

% A loop over a range written in its own head is a different thing again: MATLAB steps there rather
% than reading the array, so the two spellings part company from the middle of the range onwards.
stepped = zeros(1, 11);
k = 1;
for xr = 0:0.1:1
    stepped(k) = xr;
    k = k + 1;
end
for k = 1:11
    fprintf('CHK|loop_tenth_%d|%.17g|exact\n', k, stepped(k));
end
fprintf('CHK|loop_count|%.17g|exact\n', k);

% --- dpss ------------------------------------------------------------------------------------
[e, v] = dpss(64, 4);
fprintf('CHK|dpss_size|%s|exact\n', mat2str(size(e)));
for k = 1:size(e, 2)
    fprintf('CHK|dpss_v%d|%.17g|rel=1e-9\n', k, v(k));
    fprintf('CHK|dpss_e%d_mid|%.17g|rel=1e-8\n', k, e(32, k));
    fprintf('CHK|dpss_e%d_energy|%.17g|rel=1e-12\n', k, sum(e(:, k) .^ 2));
    fprintf('CHK|dpss_e%d_weighted|%.17g|rel=1e-8\n', k, sum(e(:, k) .* (1:64)'));
end

[e2, v2] = dpss(32, 2.5, 3);
fprintf('CHK|dpss3_size|%s|exact\n', mat2str(size(e2)));
fprintf('CHK|dpss3_v|%.17g|rel=1e-9\n', sum(v2));
fprintf('CHK|dpss3_first|%.17g|rel=1e-8\n', e2(1, 1));

[e3, v3] = dpss(24, 3, [2 4]);
fprintf('CHK|dpss_pair_size|%s|exact\n', mat2str(size(e3)));
fprintf('CHK|dpss_pair_v|%.17g|rel=1e-9\n', sum(v3));

% --- generators ------------------------------------------------------------------------------
t = 0:0.001:0.1;
chkvec('chirp_linear', chirp(t, 0, 1, 250), 1e-12);
chkvec('chirp_quad', chirp(t, 100, 1, 200, 'quadratic'), 1e-12);
chkvec('chirp_quad_convex', chirp(t, 100, 1, 200, 'quadratic', 0, 'convex'), 1e-12);
chkvec('chirp_quad_concave', chirp(t, 100, 1, 200, 'quadratic', 0, 'concave'), 1e-12);
chkvec('chirp_log', chirp(t, 10, 1, 400, 'logarithmic'), 1e-12);
chkvec('chirp_phase', chirp(t, 0, 1, 250, 'linear', 90), 1e-12);
chkvec('chirp_default', chirp(t), 1e-12);
chkvec('chirp_col', chirp(t(:), 0, 1, 250), 1e-12);
cx = chirp(t, 0, 1, 250, 'linear', 0, 'complex');
chkvec('chirp_complex_re', real(cx), 1e-12);
chkvec('chirp_complex_im', imag(cx), 1e-12);

x = -3:0.1:3;
chkvec('sinc', sinc(x), 1e-13);
fprintf('CHK|sinc_zero|%.17g|exact\n', sinc(0));
chkvec('diric7', diric(x * 2, 7), 1e-13);
chkvec('diric8', diric(x * 2, 8), 1e-13);
fprintf('CHK|diric_pole7|%.17g|exact\n', diric(0, 7));
fprintf('CHK|diric_pole8|%.17g|exact\n', diric(2 * pi, 8));

tt = 0:0.01:1;
chkvec('sawtooth', sawtooth(2 * pi * 3 * tt), 1e-13);
chkvec('sawtooth_75', sawtooth(2 * pi * 3 * tt, 0.75), 1e-13);
chkvec('sawtooth_0', sawtooth(2 * pi * 3 * tt, 0), 1e-13);
chkvec('sawtooth_1', sawtooth(2 * pi * 3 * tt, 1), 1e-13);
chkvec('sawtooth_neg', sawtooth(-2 * pi * 3 * tt, 0.4), 1e-13);
fprintf('CHK|sawtooth_at_zero|%.17g|exact\n', sawtooth(0, 0.5));
chkvec('square', square(2 * pi * 3 * tt), 1e-13);
chkvec('square_duty', square(2 * pi * 3 * tt, 25), 1e-13);

tp = -1:0.01:1;
chkvec('rectpuls', rectpuls(tp), 1e-13);
chkvec('rectpuls_w', rectpuls(tp, 0.5), 1e-13);
fprintf('CHK|rectpuls_left|%.17g|exact\n', rectpuls(-0.5));
fprintf('CHK|rectpuls_right|%.17g|exact\n', rectpuls(0.5));
chkvec('tripuls', tripuls(tp), 1e-13);
chkvec('tripuls_skew', tripuls(tp, 0.8, -0.5), 1e-13);
fprintf('CHK|tripuls_apex|%.17g|exact\n', tripuls(0));

tg = -5e-4:1e-6:5e-4;
[yi, yq, ye] = gauspuls(tg, 50e3, 0.6);
chkvec('gauspuls_i', yi, 1e-12);
chkvec('gauspuls_q', yq, 1e-12);
chkvec('gauspuls_e', ye, 1e-12);
fprintf('CHK|gauspuls_cutoff|%.17g|rel=1e-12\n', gauspuls('cutoff', 50e3, 0.6, [], -40));
chkvec('gmonopuls', gmonopuls(tg, 2e3), 1e-12);
fprintf('CHK|gmonopuls_cutoff|%.17g|rel=1e-13\n', gmonopuls('cutoff', 2e3));

td = 0:1/1e3:0.1;
d = (0:1/50:0.1)';
chkvec('pulstran_rect', pulstran(td, d, 'rectpuls', 0.01), 1e-12);
chkvec('pulstran_tri', pulstran(td, d, 'tripuls', 0.02, -1), 1e-12);
chkvec('pulstran_gaus', pulstran(td, d, @gauspuls, 10e3, 0.5), 1e-12);
chkvec('pulstran_amp', pulstran(td, [d, (1:numel(d))' / 10], 'rectpuls', 0.01), 1e-12);

% A pulse train sample by sample, because a train that is right everywhere but at one edge has the
% same mass as one that is right: the edges are where a strict comparison and a loose one differ.
ptri = pulstran(td, d, 'tripuls', 0.02, -1);
for k = 1:numel(ptri)
    fprintf('CHK|pulstran_tri_%d|%.17g|rel=1e-12\n', k, ptri(k));
end
proto = tripuls(-0.01:1e-3:0.01, 0.02);
chkvec('pulstran_proto', pulstran(td, d, proto, 1e3), 1e-10);

fs = 1000;
tv = 0:1/fs:0.2;
chkvec('vco_scalar', vco(sawtooth(2 * pi * 10 * tv, 0.75), 100, fs), 1e-11);
chkvec('vco_range', vco(sin(2 * pi * 5 * tv), [50 200], fs), 1e-11);

% --- transforms ------------------------------------------------------------------------------
s = chirp(0:1/256:1 - 1/256, 20, 1, 120);
h = hilbert(s);
chkvec('hilbert_re', real(h), 1e-11);
chkvec('hilbert_im', imag(h), 1e-11);
h2 = hilbert(s(:), 128);
chkvec('hilbert_n_re', real(h2), 1e-11);
chkvec('hilbert_n_im', imag(h2), 1e-11);
fprintf('CHK|hilbert_shape|%s|exact\n', mat2str(size(hilbert(s))));
fprintf('CHK|hilbert_col_shape|%s|exact\n', mat2str(size(hilbert(s(:)))));

g = czt(s);
chkvec('czt_default_re', real(g), 1e-10);
chkvec('czt_default_im', imag(g), 1e-10);
m = 32;
w0 = exp(-1j * 2 * pi * 0.1 / m);
a0 = exp(1j * 2 * pi * 0.05);
g2 = czt(s, m, w0, a0);
chkvec('czt_spiral_re', real(g2), 1e-10);
chkvec('czt_spiral_im', imag(g2), 1e-10);

gz = goertzel(s, [5 12 40]);
chkvec('goertzel_re', real(gz), 1e-10);
chkvec('goertzel_im', imag(gz), 1e-10);
gzm = goertzel([s(:), circshift(s(:), 7)], [3 9]);
chkvec('goertzel_mat_re', real(gzm), 1e-10);
fprintf('CHK|goertzel_mat_shape|%s|exact\n', mat2str(size(gzm)));

fw = fwht(s);
chkvec('fwht', fw, 1e-11);
chkvec('fwht_had', fwht(s, [], 'hadamard'), 1e-11);
chkvec('fwht_dya', fwht(s, [], 'dyadic'), 1e-11);
chkvec('fwht_pad', fwht(s(1:100), 128), 1e-11);
chkvec('ifwht', ifwht(fw), 1e-11);
chkvec('ifwht_had', ifwht(fwht(s, [], 'hadamard'), [], 'hadamard'), 1e-11);

sig = [1 2 3 4 3 2 1 0.5 0.25 0.125];
[xh, nd] = cceps(sig);
chkvec('cceps', xh, 1e-11);
fprintf('CHK|cceps_nd|%.17g|exact\n', nd);
chkvec('icceps', icceps(xh, nd), 1e-10);
[xh2, nd2] = cceps(s);
chkvec('cceps_chirp', xh2, 1e-10);
fprintf('CHK|cceps_chirp_nd|%.17g|exact\n', nd2);
[rh, yh] = rceps(sig);
chkvec('rceps', rh, 1e-11);
chkvec('rceps_min', yh, 1e-11);

d4 = dftmtx(4);
chkvec('dftmtx4_re', real(d4), 1e-13);
chkvec('dftmtx4_im', imag(d4), 1e-13);
chkvec('dftmtx8_re', real(dftmtx(8)), 1e-13);

xr = (1:8)';
[yb, ib] = bitrevorder(xr);
chkvec('bitrevorder', yb, 1e-13);
chkvec('bitrevorder_idx', ib, 1e-13);
[y4, i4] = digitrevorder((1:16)', 4);
chkvec('digitrevorder4', y4, 1e-13);
chkvec('digitrevorder4_idx', i4, 1e-13);
chkvec('digitrevorder_row', digitrevorder(1:8, 2), 1e-13);

% --- conversions and framing -----------------------------------------------------------------
db = [-40 -6 0 3 12 40];
chkvec('db2mag', db2mag(db), 1e-13);
chkvec('db2pow', db2pow(db), 1e-13);
chkvec('mag2db', mag2db([0.01 0.5 1 2 100]), 1e-13);
chkvec('pow2db', pow2db([0.01 0.5 1 2 1000]), 1e-13);
fprintf('CHK|pow2db_one|%.17g|exact\n', pow2db(1));
fprintf('CHK|db2mag_zero|%.17g|exact\n', db2mag(0));

bx = 1:10;
chkvec('buffer_4', buffer(bx, 4), 1e-13);
fprintf('CHK|buffer_4_size|%s|exact\n', mat2str(size(buffer(bx, 4))));
chkvec('buffer_over', buffer(bx, 4, 1), 1e-13);
chkvec('buffer_nodelay', buffer(bx, 4, 1, 'nodelay'), 1e-13);
chkvec('buffer_under', buffer(bx, 4, -1), 1e-13);
chkvec('buffer_ic', buffer(bx, 4, 2, [-1 -2]), 1e-13);
[by, bz] = buffer(bx, 4);
chkvec('buffer_2out_y', by, 1e-13);
chkvec('buffer_2out_z', bz, 1e-13);
[by2, bz2, bo2] = buffer(bx, 4, 2);
chkvec('buffer_3out_y', by2, 1e-13);
fprintf('CHK|buffer_3out_z_size|%s|exact\n', mat2str(size(bz2)));
chkvec('buffer_3out_opt', bo2, 1e-13);
[by3, bz3, bo3] = buffer(bx, 4, -1);
chkvec('buffer_u_y', by3, 1e-13);
fprintf('CHK|buffer_u_z_size|%s|exact\n', mat2str(size(bz3)));
chkvec('buffer_u_opt', bo3, 1e-13);
[by4, bz4, bo4] = buffer(bx, 3, 1, 'nodelay');
chkvec('buffer_nd_y', by4, 1e-13);
chkvec('buffer_nd_z', bz4, 1e-13);
chkvec('buffer_nd_opt', bo4, 1e-13);
chkvec('buffer_col', buffer((1:10)', 4), 1e-13);

chkvec('datawrap_row', datawrap(1:10, 4), 1e-13);
chkvec('datawrap_col', datawrap((1:10)', 4), 1e-13);
fprintf('CHK|datawrap_col_size|%s|exact\n', mat2str(size(datawrap((1:10)', 4))));

fprintf('CHK|seqperiod|%.17g|exact\n', seqperiod([1 2 3 1 2 3 1 2]));
[sp, sn] = seqperiod([1 2 3 1 2 3 1 2]);
fprintf('CHK|seqperiod_n|%.17g|rel=1e-13\n', sn);
fprintf('CHK|seqperiod_flat|%.17g|exact\n', seqperiod([4 4 4 4 4]));
fprintf('CHK|seqperiod_none|%.17g|exact\n', seqperiod([1 2 3 4 5]));
chkvec('seqperiod_matrix', seqperiod([1 2 1 2 1 2; 3 3 3 3 3 3]'), 1e-13);
fprintf('CHK|seqperiod_tol|%.17g|exact\n', seqperiod([1 2 3 1 2 3.0000001], 1e-5));

sd = reshape(1:24, 2, 3, 4);
[sx, sperm, snshift] = shiftdata(sd, 2);
fprintf('CHK|shiftdata_size|%s|exact\n', mat2str(size(sx)));
chkvec('shiftdata_perm', sperm, 1e-13);
chkvec('shiftdata_values', sx(:), 1e-13);
su = unshiftdata(sx, sperm, snshift);
chkvec('unshiftdata', su(:), 1e-13);
[sx2, sperm2, snshift2] = shiftdata(reshape(1:6, 1, 1, 6), []);
fprintf('CHK|shiftdata_empty_size|%s|exact\n', mat2str(size(sx2)));
fprintf('CHK|shiftdata_nshifts|%.17g|exact\n', snshift2);
chkvec('unshiftdata_empty', reshape(unshiftdata(sx2, sperm2, snshift2), 1, []), 1e-13);

uq = -1:0.1:1;
chkvec('uencode3', double(uencode(uq, 3)), 1e-13);
chkvec('uencode8', double(uencode(uq, 8)), 1e-13);
chkvec('uencode_v', double(uencode(uq, 4, 2)), 1e-13);
chkvec('uencode_signed', double(uencode(uq, 5, 1, 'signed')), 1e-13);
chkvec('udecode3', udecode(uencode(uq, 3), 3), 1e-13);
chkvec('udecode_v', udecode(uencode(uq, 4, 2), 4, 2), 1e-13);
chkvec('udecode_signed', udecode(uencode(uq, 5, 1, 'signed'), 5), 1e-13);
chkvec('udecode_wrap', udecode(int8([-100 -3 0 3 100]), 3, 1, 'wrap'), 1e-13);
chkvec('udecode_sat', udecode(int8([-100 -3 0 3 100]), 3, 1, 'saturate'), 1e-13);

fprintf('CHK|marcumq_10_20|%.17g|rel=1e-12\n', marcumq(10, 20));
chkvec('marcumq_sweep', marcumq(1, 0:0.5:6), 1e-11);
chkvec('marcumq_m3', marcumq(2, 0:0.5:6, 3), 1e-11);
chkvec('marcumq_a', marcumq(0:0.5:5, 3), 1e-11);
fprintf('CHK|marcumq_zero_b|%.17g|exact\n', marcumq(3, 0));
% The far tail, where the series is a sum of terms that each dwarf the answer: the order sweeps so
% that a difference which is really about one branch of the series shows as one row and not five.
for k = 1:5
    fprintf('CHK|marcumq_tail_%d|%.17g|div=ADR0136\n', k, marcumq(30, 40, k));
end
for k = 1:5
    fprintf('CHK|marcumq_head_%d|%.17g|rel=1e-10\n', k, marcumq(40, 30, k));
end

fm = 100;
fsm = 4000;
tm = (0:1/fsm:0.05)';
msg = sin(2 * pi * 20 * tm) * 0.8;
[ym, tym] = modulate(msg, fm, fsm, 'am');
chkvec('modulate_am', ym, 1e-12);
chkvec('modulate_t', tym, 1e-13);
chkvec('modulate_dsbtc', modulate(msg, fm, fsm, 'amdsb-tc'), 1e-12);
chkvec('modulate_dsbtc_opt', modulate(msg, fm, fsm, 'amdsb-tc', -1), 1e-12);
chkvec('modulate_ssb', modulate(msg, fm, fsm, 'amssb'), 1e-11);
chkvec('modulate_fm', modulate(msg, fm, fsm, 'fm'), 1e-11);
chkvec('modulate_fm_opt', modulate(msg, fm, fsm, 'fm', 0.1), 1e-11);
chkvec('modulate_pm', modulate(msg, fm, fsm, 'pm'), 1e-11);
chkvec('modulate_qam', modulate(msg, fm, fsm, 'qam', msg / 2), 1e-12);
chkvec('demod_am', demod(modulate(msg, fm, fsm, 'am'), fm, fsm, 'am'), 1e-9);
chkvec('demod_pm', demod(modulate(msg, fm, fsm, 'pm', 1), fm, fsm, 'pm', 1), 1e-9);
[q1, q2] = demod(modulate(msg, fm, fsm, 'qam', msg / 2), fm, fsm, 'qam');
chkvec('demod_qam1', q1, 1e-9);
chkvec('demod_qam2', q2, 1e-9);

fx = (1:20)';
chkvec('framesig_plain', framesig(fx, 5), 1e-13);
fprintf('CHK|framesig_plain_size|%s|exact\n', mat2str(size(framesig(fx, 5))));
chkvec('framesig_overlap', framesig(fx, 5, OverlapLength = 2), 1e-13);
chkvec('framesig_underlap', framesig(fx, 5, UnderlapLength = 2), 1e-13);
chkvec('framesig_window', framesig(fx, 5, Window = hamming(5)), 1e-13);
[fw2, fc2, fi2] = framesig(fx, 5, OverlapLength = 3);
chkvec('framesig_ol_y', fw2, 1e-13);
chkvec('framesig_ol_cond', fc2, 1e-13);
fprintf('CHK|framesig_ol_idx|%.17g|exact\n', fi2);
[fw3, fc3, fi3] = framesig(fx, 6, IncompleteFrameRule = 'zeropad');
chkvec('framesig_pad_y', fw3, 1e-13);
fprintf('CHK|framesig_pad_cond_size|%s|exact\n', mat2str(size(fc3)));
fprintf('CHK|framesig_pad_idx|%.17g|exact\n', fi3);
chkvec('framesig_initidx', framesig(fx, 5, InitialIndex = 3), 1e-13);
chkvec('framesig_initcond', framesig(fx, 5, InitialCondition = [-1; -2]), 1e-13);

% --- the helpers ------------------------------------------------------------------------------

function chkwin(name, w)
%CHKWIN Pins a window by two digests: its sum, and its sum weighted by position.
    n = numel(w);
    fprintf('CHK|w_%s_sum|%.17g|rel=1e-13\n', name, sum(w));
    if n > 0
        fprintf('CHK|w_%s_wsum|%.17g|rel=1e-13\n', name, sum(w(:) .* (1:n)'));
    else
        fprintf('CHK|w_%s_wsum|%.17g|exact\n', name, 0);
    end
end

function chksamples(name, w, tol)
%CHKSAMPLES Pins three individual coefficients of a window.
    if nargin < 3
        tol = 1e-13;
    end
    n = numel(w);
    fprintf('CHK|s_%s_first|%.17g|rel=%g\n', name, w(1), tol);
    fprintf('CHK|s_%s_mid|%.17g|rel=%g\n', name, w(ceil(n / 2)), tol);
    fprintf('CHK|s_%s_last|%.17g|rel=%g\n', name, w(n), tol);
end

function chkvec(name, v, tol)
%CHKVEC Pins a vector by four digests, which between them see a change anywhere in it.
%
%   None of the four is a plain sum, and that is deliberate. Most of the signals here oscillate
%   about zero, so their sum is a cancellation of a hundred numbers of order one down to a number of
%   order 1e-16 -- a quantity whose relative error is total and which says nothing about whether the
%   signal is right. The mass and the total variation are cancellation-free and see any change of
%   magnitude or shape; the two ratios carry the sign and the position information that a magnitude
%   alone would lose, and are bounded by one, so an absolute tolerance on them means something.
    u = v(:);
    n = numel(u);
    mass = sum(abs(u));
    fprintf('CHK|v_%s_mass|%.17g|rel=%g\n', name, mass, tol);
    fprintf('CHK|v_%s_var|%.17g|rel=%g\n', name, sum(abs(diff(u))), tol);
    if mass > 0
        fprintf('CHK|v_%s_sign|%.17g|abs=%g\n', name, sum(u) / mass, tol);
        fprintf('CHK|v_%s_place|%.17g|abs=%g\n', name, ...
            sum(u .* (1:n)') / sum(abs(u) .* (1:n)'), tol);
    else
        fprintf('CHK|v_%s_sign|%.17g|exact\n', name, 0);
        fprintf('CHK|v_%s_place|%.17g|exact\n', name, 0);
    end
    fprintf('CHK|v_%s_last|%.17g|rel=%g\n', name, u(n), tol);
end
