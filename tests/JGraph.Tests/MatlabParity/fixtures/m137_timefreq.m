% m137_timefreq.m -- M137's time-frequency transforms, signal modelling and vibration analysis:
% the short-time transform and everything built on it, the synchrosqueezed transform and its
% ridges, the six spectral descriptors and the kurtogram, the linear-prediction conversions and the
% four autoregressive fits, the two fits that start from a response, the modal trio, and the
% rainflow count, envelope spectrum, synchronous average and order-tracking family.
%
% Three rules run through the file, the same three as M136's.
%
% A spectrum, a map or a list of coefficients is pinned RELATIVE TO THE VECTOR IT LIVES IN. A
% spectrogram falls twenty decades between its peak and its floor, and a bin at 1e-20 of the peak
% is the difference of two nearly equal transforms and has no relative accuracy in any engine.
%
% A signal is built from sines and from mod, never from rand, so the file says the same thing on
% every run and on every machine.
%
% Anything whose value is a count, an index or a sample position is pinned exactly. Those are the
% answers a script branches on, and an estimate out by one bin is wrong rather than imprecise.

% ---------------------------------------------------------------------------------------------
% The signals every section draws on.
% ---------------------------------------------------------------------------------------------
n = 1024;
fs = 1000;
t = (0:n-1)'/fs;
x = cos(2*pi*80*t) + 0.5*sin(2*pi*230*t + 0.3) + 0.05*mod((0:n-1)', 7)/7;
y = 0.7*cos(2*pi*80*t + 0.4) + 0.02*mod((0:n-1)', 5)/5;
xc = x + 1i*y;
w128 = hamming(128);

% ---------------------------------------------------------------------------------------------
% spectrogram.
% ---------------------------------------------------------------------------------------------
[s1, f1, t1] = spectrogram(x);
chkexact('sg_default_size', size(s1));
chkspec('sg_default', s1(:, 1), 1e-10);
chkvec('sg_default_f', f1(1:8), 1e-12);
chkvec('sg_default_t', t1, 1e-12);

[s2, f2, t2, p2] = spectrogram(x, w128, 64, 256, fs);
chkexact('sg_size', size(s2));
chkspec('sg_s1', s2(:, 1), 1e-10);
chkspec('sg_s5', s2(:, 5), 1e-10);
chkvec('sg_f', f2(1:6), 1e-12);
chkvec('sg_t', t2(1:6), 1e-12);
chkspec('sg_p1', p2(:, 1), 1e-10);
chknum('sg_pmax', max(p2(:)), 1e-10);

[~, f3, ~, p3] = spectrogram(x, w128, 64, 256, fs, 'power');
chkspec('sg_power', p3(:, 2), 1e-10);
chkvec('sg_power_f', f3(1:4), 1e-12);
[s4, f4, ~, p4] = spectrogram(x, w128, 64, 256, fs, 'twosided');
chkspec('sg_two_s', s4(:, 2), 1e-10);
chkvec('sg_two_f', f4([1 2 127 128 129 256]), 1e-12);
chkspec('sg_two_p', p4(:, 2), 1e-10);
[s5, f5, ~, p5] = spectrogram(x, w128, 64, 256, fs, 'centered');
chkspec('sg_ctr_s', s5(:, 2), 1e-10);
chkvec('sg_ctr_f', f5([1 2 128 129 256]), 1e-12);
chkspec('sg_ctr_p', p5(:, 2), 1e-10);

[s6, f6, ~, p6] = spectrogram(x, w128, 100, 127, fs);
chkexact('sg_odd_size', size(s6));
chkvec('sg_odd_f', f6([1 2 63 64]), 1e-12);
chkspec('sg_odd_p', p6(:, 3), 1e-10);

[s7, f7] = spectrogram(xc, w128, 64, 256, fs);
chkexact('sg_complex_size', size(s7));
chkspec('sg_complex', s7(:, 3), 1e-10);
chkvec('sg_complex_f', f7([1 2 255 256]), 1e-12);

fv = [50 80 120 230]';
[s8, f8, t8] = spectrogram(x, w128, 64, fv, fs);
chkexact('sg_fvec_size', size(s8));
chkspec('sg_fvec', s8(:, 2), 1e-9);
chkvec('sg_fvec_f', f8, 1e-12);
chkvec('sg_fvec_t', t8(1:4), 1e-12);

[~, ~, ~, p9] = spectrogram(x, w128, 64, 256, fs, 'reassigned');
chkspec('sg_reassigned', p9(:, 4), 1e-9);
chknum('sg_reassigned_sum', sum(p9(:)), 1e-10);
[~, ~, ~, pa, fc, tc] = spectrogram(x, w128, 64, 256, fs, 'MinThreshold', -30);
chkspec('sg_threshold', pa(:, 3), 1e-10);
chkexact('sg_threshold_nz', nnz(pa));
chkspec('sg_fcorr', fc(:, 4), 1e-10);
chkspec('sg_tcorr', tc(:, 4), 1e-10);

[sb, ~, tb] = spectrogram(x, w128, 64, 256, fs, 'OutputTimeDimension', 'downrows');
chkexact('sg_downrows_size', size(sb));
chkexact('sg_downrows_t_size', size(tb));
chkspec('sg_downrows', sb(2, :), 1e-10);

[~, fd, td] = spectrogram(x, w128, 64, 256);
chkvec('sg_normalized_f', fd([1 2 128 129]), 1e-12);
chkvec('sg_normalized_t', td(1:4), 1e-12);

% ---------------------------------------------------------------------------------------------
% xspectrogram.
% ---------------------------------------------------------------------------------------------
[xs, xf, xt, xc2] = xspectrogram(x, y, w128, 64, 256, fs);
chkexact('xs_size', size(xs));
chkspec('xs_s', xs(:, 2), 1e-10);
chkvec('xs_f', xf(1:4), 1e-12);
chkvec('xs_t', xt(1:4), 1e-12);
chkspec('xs_c', xc2(:, 2), 1e-10);
chkspec('xs_power', xspectrogram(x, y, w128, 64, 256, fs, 'power'), 1e-10);

% ---------------------------------------------------------------------------------------------
% stft, istft and iscola.
% ---------------------------------------------------------------------------------------------
[st1, sf1, sT1] = stft(x, fs);
chkexact('stft_size', size(st1));
chkspec('stft_s', st1(:, 2), 1e-10);
chkvec('stft_f', sf1([1 2 64 65 128]), 1e-12);
chkvec('stft_t', sT1(1:4), 1e-12);

wh = hann(64, 'periodic');
[st2, sf2, sT2] = stft(x, fs, 'Window', wh, 'OverlapLength', 32, 'FFTLength', 128);
chkexact('stft2_size', size(st2));
chkspec('stft2_s', st2(:, 3), 1e-10);
chkvec('stft2_f', sf2([1 2 64 65 128]), 1e-12);
chkvec('stft2_t', sT2(1:4), 1e-12);

[st3, sf3] = stft(x, fs, 'Window', wh, 'OverlapLength', 32, 'FrequencyRange', 'onesided');
chkexact('stft_onesided_size', size(st3));
chkspec('stft_onesided', st3(:, 3), 1e-10);
chkvec('stft_onesided_f', sf3(1:4), 1e-12);
[st4, sf4] = stft(x, fs, 'Window', wh, 'OverlapLength', 32, 'FrequencyRange', 'twosided');
chkspec('stft_twosided', st4(:, 3), 1e-10);
chkvec('stft_twosided_f', sf4(1:4), 1e-12);
[~, sf5, sT5] = stft(x);
chkvec('stft_normalized_f', sf5([1 2 64 65]), 1e-12);
chkvec('stft_normalized_t', sT5(1:4), 1e-12);

win = hann(128, 'periodic');
S = stft(x, fs, 'Window', win, 'OverlapLength', 96);
[xr, tr] = istft(S, fs, 'Window', win, 'OverlapLength', 96);
chkexact('istft_len', numel(xr));
chkspec('istft', xr(129:640), 1e-10);
chkvec('istft_t', tr(1:4), 1e-12);
chkexact('istft_isreal', double(isreal(xr)));
chkexact('istft_roundtrip', double(max(abs(xr(129:end-128) - x(129:numel(xr)-128))) < 1e-10));
chkspec('istft_ola', istft(S, fs, 'Window', win, 'OverlapLength', 96, 'Method', 'ola'), 1e-10);
So = stft(x, fs, 'Window', win, 'OverlapLength', 96, 'FrequencyRange', 'onesided');
chkspec('istft_onesided', ...
    istft(So, fs, 'Window', win, 'OverlapLength', 96, 'FrequencyRange', 'onesided'), 1e-10);

chkexact('cola_hann96', double(iscola(hann(128, 'periodic'), 96)));
chkexact('cola_hamming64', double(iscola(hamming(128, 'periodic'), 64)));
chkexact('cola_hamming127', double(iscola(hamming(127), 60)));
[cola1, colam, colad] = iscola(hann(128, 'periodic'), 64);
chkexact('cola_flag', double(cola1));
chknum('cola_median', colam, 1e-12);
chknum('cola_deviation', colad, 1e-12);
[cola2, colam2] = iscola(rectwin(100), 50, 'ola');
chkexact('cola_ola_flag', double(cola2));
chknum('cola_ola_median', colam2, 1e-12);

% ---------------------------------------------------------------------------------------------
% stftmag2sig. The iteration is stopped by a residual tolerance, so the answer is only defined to
% that tolerance; a bounded iteration count is what can be pinned tightly.
% ---------------------------------------------------------------------------------------------
mfs = 1000;
mt = (0:999)'/mfs;
mx = cos(2*pi*7*mt) + 2;
mwin = hann(50, 'periodic');
MS = stft(mx, 'Window', mwin, 'OverlapLength', 25);
chkspec('mag2sig_five', ...
    stftmag2sig(abs(MS), 50, 'Window', mwin, 'OverlapLength', 25, 'MaxIterations', 5), 1e-10);
chkspec('mag2sig_fgla', ...
    stftmag2sig(abs(MS), 50, 'Window', mwin, 'OverlapLength', 25, 'Method', 'fgla', ...
    'MaxIterations', 5), 1e-10);
[mrec, mtimes] = stftmag2sig(abs(MS), 50, mfs, 'Window', mwin, 'OverlapLength', 25);
chkexact('mag2sig_len', numel(mrec));
chkvec('mag2sig_t', mtimes(1:4), 1e-12);

% Run to its own stopping rule the reconstruction is a fixed point of a hundred alternations
% between the two domains, and two engines that start a rounding apart end a few parts in a
% thousand apart. What is worth pinning there is the property the name promises -- that the
% reconstruction's short-time magnitudes are the ones it was given -- not the samples.
mback = abs(stft(mrec, 'Window', mwin, 'OverlapLength', 25));
chkexact('mag2sig_consistent', double(max(max(abs(mback - abs(MS)))) < 0.02));
chknum('mag2sig_mean', mean(mrec), 1e-2);

% ---------------------------------------------------------------------------------------------
% fsst, ifsst and tfridge.
% ---------------------------------------------------------------------------------------------
qfs = 400;
qn = 400;
qt = (0:qn-1)'/qfs;
qx = cos(2*pi*(50*qt + 30*qt.^2)) + 0.6*cos(2*pi*120*qt) + 0.02*mod((0:qn-1)', 11)/11;
qz = qx + 1i*0.4*sin(2*pi*70*qt);
qw = kaiser(64, 10);

[sst, qf, qtt] = fsst(qx, qfs, qw);
chkexact('fsst_size', size(sst));
chkvec('fsst_f', qf([1 2 16 33]), 1e-12);
chkvec('fsst_t', qtt(1:4), 1e-12);
chkspec('fsst_col', sst(:, 100), 1e-9);
chknum('fsst_max', max(abs(sst(:))), 1e-10);
chknum('fsst_sum', sum(abs(sst(:))), 1e-10);

[sst2, qf2] = fsst(qx);
chkexact('fsst_normalized_size', size(sst2));
chkvec('fsst_normalized_f', qf2([1 2 100 129]), 1e-12);
chkspec('fsst_normalized_col', sst2(:, 200), 1e-9);

[sst3, qf3] = fsst(qz, qfs, qw);
chkexact('fsst_complex_size', size(sst3));
chkvec('fsst_complex_f', qf3([1 2 32 33 64]), 1e-12);
chkspec('fsst_complex_col', sst3(:, 100), 1e-9);

[sst4, qf4] = fsst(qx, qfs, kaiser(63, 10));
chkexact('fsst_odd_size', size(sst4));
chkvec('fsst_odd_f', qf4([1 2 32]), 1e-12);
chkspec('fsst_odd_col', sst4(:, 100), 1e-9);

chkspec('ifsst', ifsst(sst, qw), 1e-9);
chkexact('ifsst_len', numel(ifsst(sst, qw)));
chkexact('ifsst_roundtrip', double(max(abs(ifsst(sst, qw) - qx)) < 1e-8));
chkspec('ifsst_scalar_window', ifsst(sst, 64), 1e-9);
chkspec('ifsst_complex', ifsst(sst3, qw), 1e-9);

[fr, ir, lr] = tfridge(sst, qf);
chkexact('ridge_size', size(fr));
chkvec('ridge_f', fr(1:8), 1e-12);
chkexact('ridge_i', ir(1:8));
chkexact('ridge_l', lr(1:8));
[fr2, ir2] = tfridge(sst, qf, 1);
chkvec('ridge_penalty_f', fr2(1:8), 1e-12);
chkexact('ridge_penalty_i', ir2(1:8));
[fr3, ir3] = tfridge(sst, qf, 0.5, 'NumRidges', 2);
chkexact('ridge_two_size', size(fr3));
chkvec('ridge_two_f', reshape(fr3(1:6, :), [], 1), 1e-12);
chkexact('ridge_two_i', reshape(ir3(1:6, :), [], 1));
[fr4, ir4] = tfridge(sst, qf, 0.5, 'NumRidges', 2, 'NumFrequencyBins', 2);
chkvec('ridge_bins_f', reshape(fr4(1:6, :), [], 1), 1e-12);
chkexact('ridge_bins_i', reshape(ir4(1:6, :), [], 1));
chkspec('ifsst_ridge', ifsst(sst, qw, ir), 1e-9);
chkexact('ifsst_ridge_size', size(ifsst(sst, qw, ir3, 'NumFrequencyBins', 3)));
chkspec('ifsst_ridge_two', ifsst(sst, qw, ir3, 'NumFrequencyBins', 3), 1e-9);
chkspec('ifsst_band', ifsst(sst, qw, qf, [40 90]), 1e-9);

% ---------------------------------------------------------------------------------------------
% The spectral descriptors and the kurtogram.
% ---------------------------------------------------------------------------------------------
dfs = 1000;
dn = 2000;
dt = (0:dn-1)'/dfs;
dx = cos(2*pi*120*dt) + 0.4*sin(2*pi*310*dt) + 0.05*mod((0:dn-1)', 13)/13;
dx2 = [dx, 0.5*cos(2*pi*200*dt) + 0.03*mod((0:dn-1)', 7)/7];
dw = hamming(200);

[dk, dks, dkc] = spectralKurtosis(dx, dfs);
chkexact('skurt_size', size(dk));
chkspec('skurt', dk, 1e-10);
chkspec('skurt_spread', dks, 1e-10);
chkspec('skurt_centroid', dkc, 1e-10);
[ds, dss, dsc] = spectralSkewness(dx, dfs);
chkspec('sskew', ds, 1e-10);
chkspec('sskew_spread', dss, 1e-10);
chkspec('sskew_centroid', dsc, 1e-10);
[df, dfa, dfg] = spectralFlatness(dx, dfs);
chkspec('sflat', df, 1e-10);
chkspec('sflat_arithmetic', dfa, 1e-10);
chkspec('sflat_geometric', dfg, 1e-10);
[dc2, dcp, dcm] = spectralCrest(dx, dfs);
chkspec('screst', dc2, 1e-10);
chkspec('screst_peak', dcp, 1e-10);
chkspec('screst_mean', dcm, 1e-10);
chkspec('sentropy', spectralEntropy(dx, dfs), 1e-10);
chkspec('sentropy_unscaled', spectralEntropy(dx, dfs, 'Scaled', false), 1e-10);
chknum('sentropy_whole', spectralEntropy(dx, dfs, 'Instantaneous', false), 1e-10);

[dk2, dks2] = spectralKurtosis(dx, dfs, 'Window', dw, 'OverlapLength', 100, 'FFTLength', 512);
chkexact('skurt_named_size', size(dk2));
chkspec('skurt_named', dk2, 1e-10);
chkspec('skurt_named_spread', dks2, 1e-10);
chkspec('screst_magnitude', ...
    spectralCrest(dx, dfs, 'Window', dw, 'OverlapLength', 100, 'SpectrumType', 'magnitude'), 1e-10);
chkspec('sflat_range', ...
    spectralFlatness(dx, dfs, 'Window', dw, 'OverlapLength', 100, 'Range', [50 400]), 1e-10);
dk3 = spectralKurtosis(dx2, dfs, 'Window', dw, 'OverlapLength', 100);
chkexact('skurt_channels_size', size(dk3));
chkspec('skurt_channels', reshape(dk3(1:6, :), [], 1), 1e-10);

[dk4, dks4, dkc4, dth, dfo] = spectralKurtosis( ...
    dx, dfs, 'Window', dw, 'OverlapLength', 100, 'Scaled', false);
chkexact('skurt_unscaled_size', size(dk4));
chkspec('skurt_unscaled', dk4, 1e-10);
chkspec('skurt_unscaled_spread', dks4, 1e-10);
chkspec('skurt_unscaled_centroid', dkc4, 1e-10);
chknum('skurt_threshold', dth, 1e-12);
chkvec('skurt_frequencies', dfo(1:6), 1e-12);
[~, ~, ~, dth2] = spectralKurtosis( ...
    dx, dfs, 'Window', dw, 'OverlapLength', 100, 'Scaled', false, 'ConfidenceLevel', 0.8);
chknum('skurt_threshold_80', dth2, 1e-12);

[dS, dF] = spectrogram(dx, dw, 100, 512, dfs);
dP = abs(dS).^2;
chkspec('skurt_from_map', spectralKurtosis(dP, dF), 1e-10);
chkspec('sskew_from_map', spectralSkewness(dP, dF), 1e-10);
chkspec('sflat_from_map', spectralFlatness(dP, dF), 1e-10);
chkspec('screst_from_map', spectralCrest(dP, dF), 1e-10);
chkspec('sentropy_from_map', spectralEntropy(dP, dF), 1e-10);

kfs = 1000;
kn = 4096;
kt = (0:kn-1)'/kfs;
kimp = zeros(kn, 1);
kimp(200:400:kn) = 1;
kh = exp(-(0:99)'/12).*cos(2*pi*310*(0:99)'/kfs);
ky = 0.4*cos(2*pi*120*kt) + 0.05*mod((0:kn-1)', 13)/13 + filter(kh, 1, kimp);
[kg, kf, kw2, kfc, kwc, kbw] = kurtogram(ky, kfs);
chkexact('kurtogram_size', size(kg));
chkspec('kurtogram_row1', kg(1, :), 1e-10);
chkspec('kurtogram_row2', kg(2, :), 1e-10);
chkspec('kurtogram_row3', kg(3, :), 1e-10);
chkspec('kurtogram_col', kg(:, 10), 1e-10);
chkvec('kurtogram_f', kf(1:6), 1e-12);
chkvec('kurtogram_w', kw2, 1e-12);
chkvec('kurtogram_pick', [kfc kwc kbw], 1e-10);
[kg2, ~, kw3, kfc2] = kurtogram(ky, kfs, 3);
chkexact('kurtogram_level_size', size(kg2));
chkspec('kurtogram_level_row', kg2(4, :), 1e-10);
chkvec('kurtogram_level_w', kw3, 1e-12);
chknum('kurtogram_level_fc', kfc2, 1e-10);
[kg3, kf3, kw4, kfc3, kwc3, kbw3] = kurtogram(ky, kfs, 0);
chkspec('kurtogram_zero', kg3, 1e-10);
chkvec('kurtogram_zero_f', kf3, 1e-12);
chkvec('kurtogram_zero_w', kw4, 1e-12);
chkvec('kurtogram_zero_pick', [kfc3 kwc3 kbw3], 1e-10);

% ---------------------------------------------------------------------------------------------
% The linear-prediction recursions and conversions.
% ---------------------------------------------------------------------------------------------
mn = 400;
mtv = (0:mn-1)';
mx2 = filter(1, [1 -1.3 0.85 -0.2], ...
    cos(2*pi*0.07*mtv) + 0.3*sin(2*pi*0.23*mtv) + 0.02*mod(mtv, 17)/17);
mx3 = [mx2, filter(1, [1 -0.5 0.4], cos(2*pi*0.11*mtv) + 0.02*mod(mtv, 5)/5)];
Rfull = xcorr(mx2, 8, 'biased');
r8 = Rfull(9:end);

[la, le, lk] = levinson(r8);
chkexact('levinson_size', size(la));
chkspec('levinson_a', la, 1e-11);
chknum('levinson_e', le, 1e-11);
chkspec('levinson_k', lk, 1e-11);
[la2, le2, lk2] = levinson(r8, 4);
chkspec('levinson4_a', la2, 1e-11);
chknum('levinson4_e', le2, 1e-11);
chkspec('levinson4_k', lk2, 1e-11);

[rR, rU, rk, re] = rlevinson(la, le);
chkspec('rlevinson_r', rR, 1e-10);
chkspec('rlevinson_u', rU(:, 3), 1e-10);
chkspec('rlevinson_k', rk, 1e-10);
chkspec('rlevinson_e', re, 1e-10);

[pa, pe] = lpc(mx2, 6);
chkexact('lpc_size', size(pa));
chkspec('lpc_a', pa, 1e-11);
chknum('lpc_e', pe, 1e-11);
[pa2, pe2] = lpc(mx3, 4);
chkspec('lpc_channels_a', reshape(pa2', [], 1), 1e-11);
chkspec('lpc_channels_e', pe2, 1e-11);

[ba, be, bk] = arburg(mx2, 6);
chkspec('arburg_a', ba, 1e-11);
chknum('arburg_e', be, 1e-11);
chkspec('arburg_k', bk, 1e-11);
[ya, ye, yk] = aryule(mx2, 6);
chkspec('aryule_a', ya, 1e-11);
chknum('aryule_e', ye, 1e-11);
chkspec('aryule_k', yk, 1e-11);
[ca, ce] = arcov(mx2, 6);
chkspec('arcov_a', ca, 1e-11);
chknum('arcov_e', ce, 1e-8);
[ma, me] = armcov(mx2, 6);
chkspec('armcov_a', ma, 1e-11);
chknum('armcov_e', me, 1e-8);
[ba2, be2] = arburg(mx3, 4);
chkspec('arburg_channels_a', reshape(ba2', [], 1), 1e-11);
chkspec('arburg_channels_e', be2, 1e-11);

[ca2, ce2] = ac2poly(r8);
chkspec('ac2poly_a', ca2, 1e-11);
chknum('ac2poly_e', ce2, 1e-11);
[ck, cr0] = ac2rc(r8);
chkspec('ac2rc_k', ck, 1e-11);
chknum('ac2rc_r0', cr0, 1e-12);
chkspec('poly2ac', poly2ac(ca2, ce2), 1e-10);
[pk2, pr0] = poly2rc(ca2, ce2);
chkspec('poly2rc_k', pk2, 1e-11);
chknum('poly2rc_r0', pr0, 1e-10);
chkspec('rc2ac', rc2ac(ck, cr0), 1e-10);
[rp, rpe] = rc2poly(ck, cr0);
chkspec('rc2poly_a', rp, 1e-11);
chknum('rc2poly_e', rpe, 1e-11);
chkspec('rc2poly_alone', rc2poly(ck), 1e-11);
[sk2, se2] = schurrc(r8);
chkspec('schurrc_k', sk2, 1e-11);
chknum('schurrc_e', se2, 1e-11);
chkspec('rc2is', rc2is(ck), 1e-12);
chkspec('is2rc', is2rc(rc2is(ck)), 1e-12);
chkspec('rc2lar', rc2lar(ck), 1e-12);
chkspec('lar2rc', lar2rc(rc2lar(ck)), 1e-12);

lsf = poly2lsf(ca2);
chkspec('poly2lsf', lsf, 1e-11);
chkspec('lsf2poly', lsf2poly(lsf), 1e-11);
lsf2 = poly2lsf(ba);
chkspec('poly2lsf6', lsf2, 1e-11);
chkspec('lsf2poly6', lsf2poly(lsf2), 1e-11);

% ---------------------------------------------------------------------------------------------
% prony, stmcb, invfreqz and invfreqs.
% ---------------------------------------------------------------------------------------------
hh = filter([1 0.4 -0.2], [1 -0.9 0.3], [1 zeros(1, 63)]);
[pb, ppa] = prony(hh, 2, 2);
chkspec('prony_b', pb, 1e-10);
chkspec('prony_a', ppa, 1e-10);
[pb2, ppa2] = prony(hh, 4, 3);
chkspec('prony_over_b', pb2, 1e-10);
chkspec('prony_over_a', ppa2, 1e-10);
[sb2, sa2] = stmcb(hh, 2, 2);
chkspec('stmcb_b', sb2, 1e-10);
chkspec('stmcb_a', sa2, 1e-10);
[sb3, sa3] = stmcb(hh, 3, 2, 8);
chkspec('stmcb_iter_b', sb3, 1e-10);
chkspec('stmcb_iter_a', sa3, 1e-10);
[sb4, sa4] = stmcb(hh, [1 zeros(1, 63)], 2, 2, 6);
chkspec('stmcb_two_b', sb4, 1e-10);
chkspec('stmcb_two_a', sa4, 1e-10);

[bz, az] = butter(4, 0.3);
[hz, wz] = freqz(bz, az, 64);
[ib, ia] = invfreqz(hz, wz, 4, 4);
chkspec('invfreqz_b', ib, 1e-9);
chkspec('invfreqz_a', ia, 1e-9);
[ib2, ia2] = invfreqz(hz, wz, 4, 4, ones(64, 1), 30, 0.01);
chkspec('invfreqz_gauss_b', ib2, 1e-8);
chkspec('invfreqz_gauss_a', ia2, 1e-8);
[bs, as1] = besself(3, 1);
[hs, ws] = freqs(bs, as1, logspace(-1, 1, 64));
[jb, ja] = invfreqs(hs(:), ws(:), 2, 3);
chkspec('invfreqs_b', jb, 1e-9);
chkspec('invfreqs_a', ja, 1e-9);
[jb2, ja2] = invfreqs(hs(:), ws(:), 2, 3, ones(64, 1), 20, 0.01);
chkspec('invfreqs_gauss_b', jb2, 1e-8);
chkspec('invfreqs_gauss_a', ja2, 1e-8);

% ---------------------------------------------------------------------------------------------
% The modal trio.
% ---------------------------------------------------------------------------------------------
vfs = 4000;
vn = 8192;
vt = (0:vn-1)'/vfs;
vu = cos(2*pi*37*vt) + 0.7*sin(2*pi*211*vt) + 0.5*cos(2*pi*533*vt) + 0.05*mod((0:vn-1)', 23)/23;
vmode = @(f0, z, g) g*filter(1, ...
    [1 -2*exp(-2*pi*z*f0/vfs)*cos(2*pi*f0/vfs*sqrt(1-z^2)) exp(-4*pi*z*f0/vfs)], vu);
vy = vmode(120, 0.02, 1) + vmode(430, 0.01, 0.6) + vmode(880, 0.03, 0.3);
vw = hann(1024);

[frf, vf] = modalfrf(vu, vy, vfs, vw, 512, 'Sensor', 'dis');
chkexact('modalfrf_size', size(frf));
chkspec('modalfrf', frf, 1e-10);
chkvec('modalfrf_f', vf(1:4), 1e-12);
[frf2, ~, coh] = modalfrf(vu, vy, vfs, vw, 512, 'Sensor', 'acc');
chkspec('modalfrf_acceleration', frf2, 1e-10);
chkspec('modalfrf_coherence', coh, 1e-8);
chkspec('modalfrf_h2', ...
    modalfrf(vu, vy, vfs, vw, 512, 'Sensor', 'vel', 'Estimator', 'H2'), 1e-10);
chkspec('modalfrf_hv', ...
    modalfrf(vu, vy, vfs, vw, 512, 'Sensor', 'dis', 'Estimator', 'Hv'), 1e-10);

[vfn, vdr] = modalfit(frf, vf, vfs, 3, 'FitMethod', 'pp');
chkspec('modalfit_pp_fn', vfn, 1e-9);
chkspec('modalfit_pp_dr', vdr, 1e-9);
[vfn2, vdr2, vms, vofrf] = modalfit(frf, vf, vfs, 3, 'FitMethod', 'lsce');
chkspec('modalfit_lsce_fn', vfn2, 1e-8);
chkspec('modalfit_lsce_dr', vdr2, 1e-8);
chkexact('modalfit_ms_size', size(vms));
chkscaled('modalfit_ms', vms, 20, 1e-7);
chkexact('modalfit_ofrf_size', size(vofrf));
chkspec('modalfit_ofrf', vofrf(200:400), 1e-7);
[vfn3, vdr3] = modalfit(frf, vf, vfs, 3, 'FitMethod', 'lsce', 'FreqRange', [50 1000]);
chkspec('modalfit_range_fn', vfn3, 1e-8);
chkspec('modalfit_range_dr', vdr3, 1e-8);
chkspec('modalfit_phys', ...
    modalfit(frf, vf, vfs, 4, 'FitMethod', 'lsce', 'PhysFreq', [120 430]), 1e-8);
vsd = modalsd(frf, vf, vfs, 'MaxModes', 6);
chkexact('modalsd_size', size(vsd));
chkspec('modalsd', reshape(vsd(1:4, 1:4), [], 1), 1e-8);

% ---------------------------------------------------------------------------------------------
% rainflow, envspectrum and tsa.
% ---------------------------------------------------------------------------------------------
rfs = 500;
rn = 3000;
rt = (0:rn-1)'/rfs;
rx = 3*sin(2*pi*1.3*rt) + 1.2*sin(2*pi*5.7*rt + 0.4) + 0.4*sin(2*pi*11.1*rt) ...
   + 0.05*mod((0:rn-1)', 19)/19;
[rc, rm, rmr, rmm, ridx] = rainflow(rx, rfs);
chkexact('rainflow_size', size(rc));
chkexact('rainflow_counts', rc(:, 1));
chkspec('rainflow_ranges', rc(:, 2), 1e-11);
chkspec('rainflow_means', rc(:, 3), 1e-11);
chkvec('rainflow_starts', rc(1:8, 4), 1e-12);
chkvec('rainflow_ends', rc(1:8, 5), 1e-12);
chkexact('rainflow_matrix_size', size(rm));
chkexact('rainflow_matrix', reshape(rm(:, 1:3), [], 1));
chkvec('rainflow_range_edges', rmr, 1e-12);
chkvec('rainflow_mean_edges', rmm, 1e-12);
chkexact('rainflow_index', ridx(1:8));
rc2 = rainflow(rx);
chkvec('rainflow_samples', reshape(rc2(1:4, :), [], 1), 1e-12);
chkspec('rainflow_ext', reshape(rainflow(rx(ridx), 'ext'), [], 1), 1e-11);

efs = 4000;
en = 4096;
eimp = zeros(en, 1);
eimp(1:250:en) = 1;
eh = exp(-(0:199)'/25).*sin(2*pi*900*(0:199)'/efs);
ey = filter(eh, 1, eimp) + 0.05*mod((0:en-1)', 7)/7;
[esp, ef, eenv, et] = envspectrum(ey, efs);
chkexact('envspectrum_size', size(esp));
chkspec('envspectrum', esp, 1e-9);
chkvec('envspectrum_f', ef(1:4), 1e-12);
chkspec('envspectrum_envelope', eenv, 1e-10);
chkvec('envspectrum_t', et(1:4), 1e-12);
chkspec('envspectrum_hilbert', ...
    envspectrum(ey, efs, 'Method', 'hilbert', 'Band', [700 1100]), 1e-9);
chkspec('envspectrum_band', ...
    envspectrum(ey, efs, 'Band', [700 1100], 'FilterOrder', 80), 1e-9);

tfs = 1000;
tn = 4000;
ttv = (0:tn-1)'/tfs;
tf0 = 5;
tx = sin(2*pi*tf0*ttv) + 0.5*sin(2*pi*3*tf0*ttv) + 0.05*mod((0:tn-1)', 11)/11;
tp = (0:1/tf0:ttv(end))';
[ta, tta, tpp, trr] = tsa(tx, tfs, tp);
chkexact('tsa_len', numel(ta));
chkspec('tsa', ta, 1e-10);
chkvec('tsa_t', tta(1:4), 1e-12);
chkvec('tsa_phase', tpp(1:4), 1e-12);
chknum('tsa_rpm', trr, 1e-10);
chkspec('tsa_spline', tsa(tx, tfs, tp, 'Method', 'spline'), 1e-10);
chkspec('tsa_pchip', tsa(tx, tfs, tp, 'Method', 'pchip'), 1e-10);
[ta2, ~, ~, trr2] = tsa(tx, tfs, tp, 'Method', 'fft');
chkexact('tsa_fft_len', numel(ta2));
chkspec('tsa_fft', ta2, 1e-10);
chknum('tsa_fft_rpm', trr2, 1e-10);
ta3 = tsa(tx, tfs, tp, 'NumRotations', 2);
chkexact('tsa_rotations_len', numel(ta3));
chkspec('tsa_rotations', ta3, 1e-10);

% ---------------------------------------------------------------------------------------------
% The order-tracking family.
% ---------------------------------------------------------------------------------------------
ofs = 2000;
on = 6000;
ot = (0:on-1)'/ofs;
orpm = 1200 + 900*ot;
ophase = 2*pi*cumsum(orpm/60)/ofs;
ox = sin(ophase) + 0.5*sin(2*ophase) + 0.3*sin(3.5*ophase) + 0.02*mod((0:on-1)', 17)/17;

otach = double(mod(ophase, 2*pi) < 0.6);
[trpm, ttt, ttp] = tachorpm(otach, ofs);
chkexact('tachorpm_len', numel(trpm));
chkspec('tachorpm', trpm, 1e-9);
chkvec('tachorpm_t', ttt(1:4), 1e-12);
chkvec('tachorpm_pulses', ttp(1:5), 1e-11);
chkspec('tachorpm_linear', tachorpm(otach, ofs, 'FitType', 'linear'), 1e-10);
trpm2 = tachorpm(otach, ofs, 'FitType', 'linear', 'OutputFs', 200);
chkexact('tachorpm_outputfs_len', numel(trpm2));
chkspec('tachorpm_outputfs', trpm2, 1e-10);
chkspec('tachorpm_fitpoints', tachorpm(otach, ofs, 'FitPoints', 5), 1e-9);

[mf, ff2, rf, tf2, resf] = rpmfreqmap(ox, ofs, orpm);
chkexact('rpmfreqmap_size', size(mf));
chkvec('rpmfreqmap_f', ff2(1:4), 1e-12);
chkvec('rpmfreqmap_rpm', rf(1:4), 1e-11);
chkvec('rpmfreqmap_t', tf2(1:4), 1e-12);
chknum('rpmfreqmap_res', resf, 1e-11);
chkspec('rpmfreqmap_col', mf(:, 3), 1e-10);
chknum('rpmfreqmap_max', max(mf(:)), 1e-10);
mf2 = rpmfreqmap(ox, ofs, orpm, 8);
chkexact('rpmfreqmap_res8_size', size(mf2));
chkspec('rpmfreqmap_res8', mf2(:, 2), 1e-10);
chkspec('rpmfreqmap_peak', ...
    rpmfreqmap(ox, ofs, orpm, 'Amplitude', 'peak', 'Window', 'hamming'), 1e-10);
chkspec('rpmfreqmap_db', ...
    reshape(rpmfreqmap(ox, ofs, orpm, 'Amplitude', 'power', 'Scale', 'dB', ...
    'OverlapPercent', 25), [], 1), 1e-10);

[mo, oo, ro, to, reso] = rpmordermap(ox, ofs, orpm);
chkexact('rpmordermap_size', size(mo));
chkvec('rpmordermap_o', oo(1:4), 1e-12);
chkvec('rpmordermap_rpm', ro(1:4), 1e-11);
chkvec('rpmordermap_t', to(1:4), 1e-11);
chknum('rpmordermap_res', reso, 1e-11);
chkspec('rpmordermap_col', mo(:, 3), 1e-10);
chknum('rpmordermap_max', max(mo(:)), 1e-10);

[osp, oop] = orderspectrum(mo, oo);
chkexact('orderspectrum_size', size(osp));
chkspec('orderspectrum', osp, 1e-10);
chkvec('orderspectrum_o', oop(1:4), 1e-12);
chkspec('orderspectrum_signal', orderspectrum(ox, ofs, orpm), 1e-10);
chkspec('orderspectrum_power', orderspectrum(mo, oo, 'Amplitude', 'power'), 1e-10);

[omg, orr, ott] = ordertrack(mo, oo, ro, to, [1 2 3.5]);
chkexact('ordertrack_size', size(omg));
chkspec('ordertrack', reshape(omg(:, 1:6), [], 1), 1e-10);
chkvec('ordertrack_rpm', orr(1:4), 1e-11);
chkvec('ordertrack_t', ott(1:4), 1e-11);
chkspec('ordertrack_signal', ...
    reshape(ordertrack(ox, ofs, orpm, [1 2 3.5]), [], 1), 1e-10);

% orderwaveform's answer is the fixed point of a conjugate-gradient iteration stopped at a
% relative residual of 1e-3, so it is only defined to that tolerance: two engines that take
% different step sequences land a few parts in 1e5 apart, which is inside what the algorithm
% promises and outside what a tight fixture could ask for.
wfs = 1000;
wn = 1500;
wt = (0:wn-1)'/wfs;
wrpm = 600 + 400*wt;
wphase = 2*pi*cumsum(wrpm/60)/wfs;
wx = sin(wphase) + 0.6*sin(2*wphase) + 0.02*mod((0:wn-1)', 13)/13;
ow1 = orderwaveform(wx, wfs, wrpm, 1);
chkexact('orderwaveform_size', size(ow1));
chkscaled('orderwaveform', ow1, 1.7, 1e-3);
chknum('orderwaveform_rms', sqrt(mean(ow1.^2)), 1e-4);
chkscaled('orderwaveform_two', ...
    reshape(orderwaveform(wx, wfs, wrpm, [1 2]), [], 1), 1.7, 1e-3);
chkscaled('orderwaveform_decoupled', ...
    reshape(orderwaveform(wx, wfs, wrpm, [1 2], 'Decouple', true), [], 1), 1.7, 1e-3);
chkscaled('orderwaveform_bandwidth', orderwaveform(wx, wfs, wrpm, 1, 'Bandwidth', 30), 2.5, 1e-3);
chkscaled('orderwaveform_second', orderwaveform(wx, wfs, wrpm, 1, 'FilterOrder', 2), 1.7, 1e-3);

% ---------------------------------------------------------------------------------------------
% The two-signal estimates that M136 left without their MIMO and estimator forms.
% ---------------------------------------------------------------------------------------------
gn = 2048;
gt = (0:gn-1)';
gu1 = cos(2*pi*0.07*gt) + 0.4*sin(2*pi*0.19*gt) + 0.05*mod(gt, 13)/13;
gu2 = sin(2*pi*0.11*gt) + 0.3*cos(2*pi*0.29*gt) + 0.05*mod(gt, 7)/7;
gy1 = filter([1 0.5], [1 -0.6 0.1], gu1) + 0.4*filter(0.3, [1 -0.2], gu2);
gy2 = filter([0.2 0.1], [1 -0.8 0.2], gu1) + filter([1 -0.4], [1 -0.5], gu2);
gw = hamming(256);
chkspec('tfe_h1', tfestimate(gu1, gy1, gw, 128, 256, 'Estimator', 'h1'), 1e-10);
chkspec('tfe_h2', tfestimate(gu1, gy1, gw, 128, 256, 'Estimator', 'h2'), 1e-10);
[gm, gf] = tfestimate([gu1 gu2], [gy1 gy2], gw, 128, 256, 'mimo');
chkexact('tfe_mimo_size', size(gm));
chkvec('tfe_mimo_f', gf(1:4), 1e-12);
chkspec('tfe_mimo_11', gm(:, 1, 1), 1e-10);
chkscaled('tfe_mimo_21', gm(:, 2, 1), 1, 1e-9);
chkspec('tfe_mimo_12', gm(:, 1, 2), 1e-10);
chkspec('tfe_mimo_22', gm(:, 2, 2), 1e-10);
gc = mscohere([gu1 gu2], [gy1 gy2], gw, 128, 256, 'mimo');
chkexact('coherence_mimo_size', size(gc));
chkspec('coherence_mimo_1', gc(:, 1), 1e-10);
chkspec('coherence_mimo_2', gc(:, 2), 1e-10);

% ---------------------------------------------------------------------------------------------
% Helpers.
% ---------------------------------------------------------------------------------------------
function chkspec(name, v, tol)
%CHKSPEC A spectrum, a map or a list of coefficients, pinned against the scale of the list it
%lives in.
    u = v(:);
    fprintf('CHK|s_%s_n|%d|exact\n', name, numel(u));
    if isempty(u)
        return;
    end
    scale = max(abs(u(isfinite(u))));
    if isempty(scale) || scale == 0
        scale = 1;
    end
    for i = 1:numel(u)
        if isreal(u)
            pinone(sprintf('s_%s_%d', name, i), u(i), scale, tol);
        else
            pinone(sprintf('s_%s_%dr', name, i), real(u(i)), scale, tol);
            pinone(sprintf('s_%s_%di', name, i), imag(u(i)), scale, tol);
        end
    end
end

function chkscaled(name, v, scale, tol)
%CHKSCALED The same, against a scale the caller states rather than one read off the values. A
%vector whose own peak moves between engines would otherwise change the RULE as well as the value,
%and a fixture that disagrees about its own tolerance says nothing useful about the numbers.
    u = v(:);
    fprintf('CHK|s_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        if isreal(u)
            pinone(sprintf('s_%s_%d', name, i), u(i), scale, tol);
        else
            pinone(sprintf('s_%s_%dr', name, i), real(u(i)), scale, tol);
            pinone(sprintf('s_%s_%di', name, i), imag(u(i)), scale, tol);
        end
    end
end

function chkdiv(name, v, adr)
%CHKDIV A quantity this build knowingly disagrees with MATLAB about; the ADR says why.
    u = v(:);
    fprintf('CHK|d_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        fprintf('CHK|d_%s_%d|%.17g|div=%s\n', name, i, abs(u(i)), adr);
    end
end

function chkvec(name, v, tol)
%CHKVEC A short list whose entries all matter on their own terms.
    u = v(:);
    fprintf('CHK|v_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        chknum(sprintf('v_%s_%d', name, i), u(i), tol);
    end
end

function chkexact(name, v)
%CHKEXACT A count, an index or a sample position, which is right or wrong and never close.
    u = v(:);
    fprintf('CHK|e_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        fprintf('CHK|e_%s_%d|%.17g|exact\n', name, i, u(i));
    end
end

function pinone(name, value, scale, tol)
%PINONE One number, against the scale of the list it came from.
    if ~isfinite(value)
        fprintf('CHK|%s|%.17g|exact\n', name, value);
    else
        fprintf('CHK|%s|%.17g|abs=%g\n', name, value, scale * tol);
    end
end

function chknum(name, value, tol)
%CHKNUM One number on its own, relatively unless it is algebraically zero.
    if ~isfinite(value)
        fprintf('CHK|%s|%.17g|exact\n', name, value);
    elseif abs(value) < 1e-9
        fprintf('CHK|%s|%.17g|abs=1e-9\n', name, value);
    else
        fprintf('CHK|%s|%.17g|rel=%g\n', name, value, tol);
    end
end
