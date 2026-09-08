% m136_spectral.m -- M136's spectral estimates and measurements: the periodogram and Welch's
% average, the parametric and subspace estimates, the multitaper and Lomb-Scargle ones, the
% measurements taken off a spectrum, findpeaks and the level measurements, the bilevel waveform
% family, the alignment and distance names, and the two change detectors.
%
% Three rules run through the file.
%
% A spectrum is pinned RELATIVE TO THE VECTOR IT LIVES IN. A power spectral density falls twenty
% decades between its peak and its floor, and a bin at 1e-20 of the peak has no relative accuracy in
% any engine: it is the difference of two nearly equal transforms. So every spectrum is pinned by
% its own peak, as M134's coefficients are.
%
% A signal is built from sines and from mod, never from rand, so the file says the same thing on
% every run and on every machine.
%
% Anything whose value is a count, an index or a sample position is pinned exactly. Those are the
% answers a script branches on, and an estimate that is out by one bin is wrong rather than
% imprecise.

% ---------------------------------------------------------------------------------------------
% The signals every section draws on.
% ---------------------------------------------------------------------------------------------
n = 512;
t = (0:n-1)';
x = cos(2*pi*0.1*t) + 0.5*sin(2*pi*0.27*t + 0.3) + 0.05*mod(t, 7)/7;
y = 0.7*cos(2*pi*0.1*t + 0.4) + 0.02*mod(t, 5)/5;
xc = x + 1i*y;
X = [x y];

fs = 1000;
tt = (0:1023)'/fs;
tone = 2*cos(2*pi*100*tt) + 0.05*cos(2*pi*200*tt) + 0.02*cos(2*pi*300*tt) ...
     + 0.001*mod((0:1023)', 13)/13;

% ---------------------------------------------------------------------------------------------
% periodogram.
% ---------------------------------------------------------------------------------------------
chkspec('per_default', periodogram(x), 1e-10);
[~, f] = periodogram(x);
chkvec('per_default_f', f, 1e-12);
chkspec('per_hamming', periodogram(x, hamming(n)), 1e-10);
chkspec('per_nfft', periodogram(x, [], 1024), 1e-10);
chkspec('per_fs', periodogram(x, [], 1024, 1000), 1e-10);
[~, f2] = periodogram(x, [], 1024, 1000);
chkvec('per_fs_f', f2, 1e-12);
chkspec('per_two', periodogram(x, [], 512, 'twosided'), 1e-10);
chkspec('per_center', periodogram(x, [], 512, 'centered'), 1e-10);
[~, fc] = periodogram(x, [], 512, 1000, 'centered');
chkvec('per_center_f', fc, 1e-12);
chkspec('per_power', periodogram(x, hann(n), 1024, 'power'), 1e-10);
chkspec('per_odd', periodogram(x, [], 511), 1e-10);
chkspec('per_odd_center', periodogram(x, [], 511, 'centered'), 1e-10);
chkspec('per_cplx', periodogram(xc, [], 256), 1e-10);
chkspec('per_cplx_center', periodogram(xc, [], 256, 'centered'), 1e-10);
chkspec('per_short', periodogram(x, [], 128), 1e-10);
[~, ~, pc3] = periodogram(x, [], 256, 100);
chkspec('per_conf', pc3, 1e-9);
chkspec('per_freqvec', periodogram(x, [], [10 20 30 40], 1000), 1e-10);
chkspec('per_matrix', periodogram(X, [], 256), 1e-10);

% ---------------------------------------------------------------------------------------------
% pwelch and the two-signal estimates.
% ---------------------------------------------------------------------------------------------
chkspec('pw_default', pwelch(x), 1e-10);
[~, fw] = pwelch(x);
chkvec('pw_default_f', fw, 1e-12);
chkspec('pw_seg', pwelch(x, 128), 1e-10);
chkspec('pw_seg_ov', pwelch(x, 128, 64), 1e-10);
chkspec('pw_full', pwelch(x, hann(128), 100, 512, 1000), 1e-10);
chkspec('pw_power', pwelch(x, hann(128), 100, 512, 1000, 'power'), 1e-10);
chkspec('pw_two', pwelch(x, 128, 64, 256, 'twosided'), 1e-10);
chkspec('pw_center', pwelch(x, 128, 64, 256, 'centered'), 1e-10);
chkspec('pw_maxhold', pwelch(x, 128, 64, 256, 'maxhold'), 1e-10);
chkspec('pw_minhold', pwelch(x, 128, 64, 256, 'minhold'), 1e-10);
[~, ~, pwc] = pwelch(x, 128, 64, 256, 1000);
chkspec('pw_conf', pwc, 1e-9);
chkspec('pw_conf90', pwelch(x, 128, 64, 256, 1000, 'ConfidenceLevel', 0.9), 1e-10);
chkspec('pw_matrix', pwelch(X, 128, 64, 256), 1e-10);
chkspec('pw_cplx', pwelch(xc, 128, 64, 256), 1e-10);
chkspec('pw_freqvec', pwelch(x, 128, 64, [10 20 30], 1000), 1e-10);

chkspec('cp_default', cpsd(x, y), 1e-9);
chkspec('cp_full', cpsd(x, y, hann(128), 100, 512, 1000), 1e-9);
chkspec('cp_two', cpsd(x, y, 128, 64, 256, 'twosided'), 1e-9);
chkspec('ms_default', mscohere(x, y), 1e-8);
chkspec('ms_full', mscohere(x, y, hann(128), 100, 512, 1000), 1e-8);
chkspec('ms_center', mscohere(x, y, 128, 64, 256, 'centered'), 1e-8);
chkspec('tf_default', tfestimate(x, y), 1e-8);
chkspec('tf_full', tfestimate(x, y, hann(128), 100, 512, 1000), 1e-8);

% ---------------------------------------------------------------------------------------------
% The names of 1993: csd, cohere, tfe and specgram, which are their own code and not the modern
% ones with the arguments in another order.
% ---------------------------------------------------------------------------------------------
chkspec('csd_default', csd(x, y), 1e-8);
chkspec('csd_full', csd(x, y, 256, 1000, hamming(128), 32), 1e-8);
[~, csc, csf] = csd(x, y, 256, 1000, hamming(128), 32);
chkspec('csd_conf', csc, 1e-8);
chkvec('csd_f', csf, 1e-12);
chkspec('coh_default', cohere(x, y), 1e-8);
chkspec('coh_full', cohere(x, y, 256, 1000, hamming(128), 32), 1e-8);
chkspec('tfe_default', tfe(x, y), 1e-8);
chkspec('tfe_full', tfe(x, y, 256, 1000, hamming(128), 32), 1e-8);
chkspec('tfe_detrend', tfe(x, y, 256, 1000, hamming(128), 32, 'linear'), 1e-8);
chkspec('sg_default', specgram(x), 1e-9);
[sb, sf, st] = specgram(x, 128, 1000, hamming(64), 32);
chkspec('sg_b', sb, 1e-9);
chkvec('sg_f', sf, 1e-12);
chkvec('sg_t', st, 1e-12);

% ---------------------------------------------------------------------------------------------
% The parametric estimates.
% ---------------------------------------------------------------------------------------------
m = 256;
tm = (0:m-1)';
xm = cos(2*pi*0.12*tm) + 0.6*sin(2*pi*0.3*tm + 0.2) + 0.08*mod(tm, 11)/11;
xmc = xm + 1i*(0.5*cos(2*pi*0.2*tm) + 0.03*mod(tm, 5)/5);

chkspec('cm_auto', corrmtx(xm, 4), 1e-11);
chkspec('cm_cov', corrmtx(xm, 4, 'cov'), 1e-11);
chkspec('cm_mod', corrmtx(xm, 4, 'mod'), 1e-11);
chkspec('cm_pre', corrmtx(xm, 4, 'pre'), 1e-11);
chkspec('cm_post', corrmtx(xm, 4, 'post'), 1e-11);
[~, Rm] = corrmtx(xm, 4, 'mod');
chkspec('cm_R', Rm, 1e-11);
chkspec('cm_cplx', corrmtx(xmc, 3, 'cov'), 1e-11);

chkspec('pb_default', pburg(xm, 8), 1e-9);
[~, fb] = pburg(xm, 8);
chkvec('pb_f', fb, 1e-12);
chkspec('pb_fs', pburg(xm, 8, 512, 1000), 1e-9);
chkspec('pb_two', pburg(xm, 8, 256, 'twosided'), 1e-9);
chkspec('pb_center', pburg(xm, 8, 256, 1000, 'centered'), 1e-9);
[~, ~, pbc] = pburg(xm, 8, 256, 1000);
chkspec('pb_conf', pbc, 1e-9);
chkspec('pb_cplx', pburg(xmc, 6, 128), 1e-9);
chkspec('py_default', pyulear(xm, 8), 1e-9);
chkspec('py_fs', pyulear(xm, 8, 512, 1000), 1e-9);
chkspec('pc_default', pcov(xm, 8), 1e-9);
chkspec('pc_fs', pcov(xm, 8, 512, 1000), 1e-9);
chkspec('pm_default', pmcov(xm, 8), 1e-9);
chkspec('pm_fs', pmcov(xm, 8, 512, 1000), 1e-9);

chkspec('pmu_default', pmusic(xm, 4), 1e-8);
[su, fu] = pmusic(xm, 4, 512, 1000);
chkspec('pmu_fs', su, 1e-8);
chkvec('pmu_f', fu, 1e-12);
chkspec('pmu_two', pmusic(xm, 4, 256, 'twosided'), 1e-8);
chkspec('pmu_win', pmusic(xm, 4, 256, 1000, 32, 16), 1e-8);
chkspec('peig_default', peig(xm, 4), 1e-8);
chkspec('peig_fs', peig(xm, 4, 512, 1000), 1e-8);
[rw, rp] = rootmusic(xm, 4);
chkspec('rm_w', sort(rw), 1e-8);
chkspec('rm_p', sort(rp), 1e-7);
[rw2, ~] = rootmusic(xm, 4, 1000);
chkspec('rm_w_fs', sort(rw2), 1e-8);
[ew, ~] = rooteig(xm, 4);
chkspec('re_w', sort(ew), 1e-8);

chkspec('pmem_default', pmem(xm, 8), 1e-9);
[pe, fe, ae] = pmem(xm, 8, 256, 1000);
chkspec('pmem_fs', pe, 1e-9);
chkvec('pmem_f', fe, 1e-12);
chkspec('pmem_a', ae, 1e-9);

% ---------------------------------------------------------------------------------------------
% pmtm and plomb.
% ---------------------------------------------------------------------------------------------
chkspec('mt_default', pmtm(xm), 1e-9);
[~, fmt] = pmtm(xm);
chkvec('mt_f', fmt, 1e-12);
chkspec('mt_nw', pmtm(xm, 3), 1e-9);
chkspec('mt_nfft', pmtm(xm, 4, 512), 1e-9);
chkspec('mt_fs', pmtm(xm, 4, 512, 1000), 1e-9);
chkspec('mt_unity', pmtm(xm, 4, 512, 1000, 'unity'), 1e-9);
chkspec('mt_eigen', pmtm(xm, 4, 512, 1000, 'eigen'), 1e-9);
chkspec('mt_two', pmtm(xm, 4, 256, 'twosided'), 1e-9);
chkspec('mt_center', pmtm(xm, 4, 256, 1000, 'centered'), 1e-9);
chkspec('mt_drop', pmtm(xm, 4, 256, 1000, 'DropLastTaper', false), 1e-9);
chkspec('mt_sine', pmtm(xm, 'Tapers', 'sine'), 1e-9);
chkspec('mt_sine5', pmtm(xm, 5, 256, 1000, 'Tapers', 'sine'), 1e-9);
chkspec('mt_conf90', pmtm(xm, 4, 256, 1000, 'ConfidenceLevel', 0.9), 1e-9);

np = 200;
tp = (0:np-1)';
xp = cos(2*pi*0.1*tp) + 0.4*sin(2*pi*0.23*tp + 0.5) + 0.05*mod(tp, 7)/7;
tu = tp + 0.3*(mod(tp, 13)/13 - 0.5);
tu = sort(tu - tu(1));
xg = xp; xg(mod(tp, 9) == 0) = NaN;

chkspec('pl_default', plomb(xp), 1e-9);
[~, fpl] = plomb(xp);
chkvec('pl_f', fpl, 1e-12);
chkspec('pl_fs', plomb(xp, 100), 1e-9);
chkspec('pl_t', plomb(xp, tu), 1e-9);
chkspec('pl_fmax', plomb(xp, 100, 20), 1e-9);
chkspec('pl_fvec', plomb(xp, 100, [1 2 5 10 20 30]), 1e-9);
chkspec('pl_ofac', plomb(xp, 100, [], 8), 1e-9);
chkspec('pl_power', plomb(xp, 100, 'power'), 1e-9);
chkspec('pl_norm', plomb(xp, 100, 'normalized'), 1e-9);
chkspec('pl_gap', plomb(xg, tu), 1e-9);
chkspec('pl_matrix', plomb([xp xp*0.5], 100), 1e-9);
[~, ~, pth] = plomb(xp, 100, 'Pd', [0.9 0.95]);
chkspec('pl_pth', pth, 1e-9);

% ---------------------------------------------------------------------------------------------
% The measurements taken off a spectrum.
% ---------------------------------------------------------------------------------------------
[pxx, fx] = periodogram(tone, kaiser(1024, 38), 1024, fs, 'psd');
chknum('enbw1', enbw(hamming(64)), 1e-12);
chknum('enbw2', enbw(hann(128), 1000), 1e-12);
chknum('enbw3', enbw(kaiser(256, 38), 44100), 1e-12);
chknum('bp1', bandpower(tone), 1e-12);
chknum('bp2', bandpower(tone, fs, [50 150]), 1e-10);
chknum('bp3', bandpower(pxx, fx, 'psd'), 1e-10);
chknum('bp4', bandpower(pxx, fx, [50 150], 'psd'), 1e-10);
chkvec('bp5', bandpower([tone tone*0.5], fs, [90 110]), 1e-10);

[mf, mp] = meanfreq(tone, fs);
chknum('mf1', mf, 1e-10); chknum('mf1p', mp, 1e-10);
chknum('mf2', meanfreq(tone, fs, [50 400]), 1e-10);
chknum('mf3', meanfreq(pxx, fx), 1e-10);
[df, dp] = medfreq(tone, fs);
chknum('df1', df, 1e-10); chknum('df1p', dp, 1e-10);
chknum('df2', medfreq(tone, fs, [50 400]), 1e-10);
chknum('df3', medfreq(pxx, fx), 1e-10);

[bw, flo, fhi, pw] = obw(tone, fs);
chknum('obw1', bw, 1e-10); chknum('obw1l', flo, 1e-10);
chknum('obw1h', fhi, 1e-10); chknum('obw1p', pw, 1e-10);
[bw2, flo2, fhi2, pw2] = obw(tone, fs, [], 95);
chknum('obw2', bw2, 1e-10); chknum('obw2l', flo2, 1e-10);
chknum('obw2h', fhi2, 1e-10); chknum('obw2p', pw2, 1e-10);
chknum('obw3', obw(pxx, fx), 1e-10);
[b3, l3, h3, p3] = powerbw(tone, fs);
chknum('pbw1', b3, 1e-10); chknum('pbw1l', l3, 1e-10);
chknum('pbw1h', h3, 1e-10); chknum('pbw1p', p3, 1e-10);
[b4, l4, h4, p4] = powerbw(tone, fs, [], 6);
chknum('pbw2', b4, 1e-10); chknum('pbw2l', l4, 1e-10);
chknum('pbw2h', h4, 1e-10); chknum('pbw2p', p4, 1e-10);
chknum('pbw3', powerbw(pxx, fx), 1e-10);

[r1, hp1, hf1] = thd(tone, fs);
chknum('thd1', r1, 1e-9);
% Only the harmonics that are there are pinned: the fourth and beyond are ten log ten of an
% absent tone, which is the noise floor and has no accuracy in either engine.
chkexact('thd1n', numel(hp1));
for hk = 1:3
    chknum(sprintf('thd1p%d', hk), hp1(hk), 1e-9);
    chknum(sprintf('thd1f%d', hk), hf1(hk), 1e-9);
end
chknum('thd2', thd(tone, fs, 4), 1e-9);
chknum('thd3', thd(pxx, fx, 'psd'), 1e-9);
[s1, np1] = snr(tone, fs);
chknum('snr1', s1, 1e-9); chknum('snr1n', np1, 1e-9);
chknum('snr2', snr(tone, fs, 4), 1e-9);
chknum('snr3', snr(pxx, fx, 'psd'), 1e-9);
chknum('snr4', snr(tone, 0.01*cos(2*pi*77*tt)), 1e-10);
[sd1, dn1] = sinad(tone, fs);
chknum('sinad1', sd1, 1e-9); chknum('sinad1n', dn1, 1e-9);
chknum('sinad2', sinad(pxx, fx, 'psd'), 1e-9);
[sf1, sp1, sfq1] = sfdr(tone, fs);
chknum('sfdr1', sf1, 1e-9); chknum('sfdr1p', sp1, 1e-9); chknum('sfdr1f', sfq1, 1e-9);
chknum('sfdr2', sfdr(tone, fs, 20), 1e-9);
two = 0.4*cos(2*pi*140*tt) + 0.4*cos(2*pi*150*tt) + 0.001*cos(2*pi*160*tt);
[o1, fp1, ff1, ip1, if1] = toi(two, fs);
chknum('toi1', o1, 1e-9);
chkspec('toi1fp', fp1, 1e-9); chkspec('toi1ff', ff1, 1e-9);
chkspec('toi1ip', ip1, 1e-9); chkspec('toi1if', if1, 1e-9);

% ---------------------------------------------------------------------------------------------
% findpeaks and the level measurements.
% ---------------------------------------------------------------------------------------------
nk = 200;
tk = (0:nk-1)';
yk = sin(2*pi*tk/23) + 0.5*sin(2*pi*tk/7) + 0.25*sin(2*pi*tk/3.1) + 0.05*mod(tk, 11)/11;
xk = tk/50;

[p1, l1, w1, pr1] = findpeaks(yk);
chkspec('fp_pks', p1, 1e-12);
chkexact('fp_locs', l1);
chkspec('fp_w', w1, 1e-11);
chkspec('fp_prom', pr1, 1e-11);
[p2, l2] = findpeaks(yk, xk);
chkspec('fp_x_pks', p2, 1e-12);
chkspec('fp_x_locs', l2, 1e-12);
chkspec('fp_fs', findpeaks(yk, 50), 1e-12);
[p3, l3, w3, pr3] = findpeaks(yk, 'MinPeakHeight', 0.5);
chkspec('fp_h', p3, 1e-12); chkexact('fp_h_l', l3);
chkspec('fp_h_w', w3, 1e-11); chkspec('fp_h_p', pr3, 1e-11);
[p4, l4] = findpeaks(yk, 'MinPeakProminence', 0.5);
chkspec('fp_prm', p4, 1e-12); chkexact('fp_prm_l', l4);
[p5, l5] = findpeaks(yk, 'MinPeakDistance', 10);
chkspec('fp_dist', p5, 1e-12); chkexact('fp_dist_l', l5);
[p6, l6] = findpeaks(yk, 'NPeaks', 3);
chkspec('fp_np', p6, 1e-12); chkexact('fp_np_l', l6);
[p7, l7] = findpeaks(yk, 'SortStr', 'descend');
chkspec('fp_sd', p7, 1e-12); chkexact('fp_sd_l', l7);
[p8, l8] = findpeaks(yk, 'SortStr', 'ascend');
chkspec('fp_sa', p8, 1e-12); chkexact('fp_sa_l', l8);
[p9, l9, w9] = findpeaks(yk, 'WidthReference', 'halfheight');
chkspec('fp_hh', p9, 1e-12); chkexact('fp_hh_l', l9); chkspec('fp_hh_w', w9, 1e-11);
[pa, la, wa] = findpeaks(yk, 'MinPeakWidth', 2, 'MaxPeakWidth', 6);
chkspec('fp_ww', pa, 1e-12); chkexact('fp_ww_l', la); chkspec('fp_ww_w', wa, 1e-11);
[pb, lb] = findpeaks(yk, 'Threshold', 0.05);
chkspec('fp_th', pb, 1e-12); chkexact('fp_th_l', lb);

chknum('p2p1', peak2peak(yk), 1e-12);
chkvec('p2p2', peak2peak([yk 2*yk]), 1e-12);
chkvec('p2p3', peak2peak([yk 2*yk], 2), 1e-12);
chknum('p2r1', peak2rms(yk), 1e-12);
chkvec('p2r2', peak2rms([yk 2*yk]), 1e-12);
chknum('rssq1', rssq(yk), 1e-12);
chkvec('rssq2', rssq([yk 2*yk]), 1e-12);
chkvec('rssq3', rssq([yk 2*yk], 2), 1e-12);

[zr, zc] = zerocrossrate(yk);
chknum('zc1', zr, 1e-12); chknum('zc1c', zc, 1e-12);
chknum('zc2', zerocrossrate(yk, 'Level', 0.2), 1e-12);
chkvec('zc3', zerocrossrate(yk, 'WindowLength', 50), 1e-12);
[zr4, zc4] = zerocrossrate(yk, 'WindowLength', 50, 'OverlapLength', 25);
chkvec('zc4', zr4, 1e-12); chkvec('zc4c', zc4, 1e-12);
chknum('zc5', zerocrossrate(yk, 'TransitionEdge', 'rising'), 1e-12);
chknum('zc6', zerocrossrate(yk, 'Threshold', 0.3), 1e-12);
chkspec('zc7', zerocrossrate([yk 2*yk], 'WindowLength', 40), 1e-12);
chknum('zc8', zerocrossrate(yk, 'Method', 'comparison'), 1e-12);

% ---------------------------------------------------------------------------------------------
% The bilevel waveform family.
% ---------------------------------------------------------------------------------------------
sq = zeros(500, 1);
for k = 0:4
    sq(k*100+1 : k*100+45) = 1;
end
hh = exp(-(0:29)'/6); hh = hh/sum(hh);
bl = filter(hh, 1, sq);
bl = bl + 0.02*sin(2*pi*80*(0:499)'/1000) + 0.15*exp(-(0:499)'/40).*sin(2*pi*40*(0:499)'/1000);
tb = (0:499)'/1000;

chkvec('sl1', statelevels(bl), 1e-11);
[lv, hs, bn] = statelevels(bl, 20);
chkvec('sl2', lv, 1e-11); chkexact('sl2h', hs); chkvec('sl2b', bn, 1e-12);
chkvec('sl3', statelevels(bl, 50, 'mean'), 1e-11);
chkvec('sl4', statelevels(bl, 30, 'mode', [-0.2 1.3]), 1e-11);

[cc, mr] = midcross(bl, 1000);
chkvec('mc1', cc, 1e-10); chknum('mc1r', mr, 1e-11);
chkvec('mc2', midcross(bl, 1000, 'MidPercentReferenceLevel', 40), 1e-10);
chkvec('mc3', midcross(bl, tb), 1e-10);

[rr, lc, uc, lr, ur] = risetime(bl, 1000);
chkvec('rt1', rr, 1e-10); chkvec('rt1l', lc, 1e-10); chkvec('rt1u', uc, 1e-10);
chknum('rt1lr', lr, 1e-11); chknum('rt1ur', ur, 1e-11);
chkvec('rt2', risetime(bl, 1000, 'PercentReferenceLevels', [20 80]), 1e-10);
[f2b, lc2, uc2] = falltime(bl, 1000);
chkvec('ft1', f2b, 1e-10); chkvec('ft1l', lc2, 1e-10); chkvec('ft1u', uc2, 1e-10);
chkvec('sr1', slewrate(bl, 1000), 1e-10);
chkvec('sr2', slewrate(bl, 1000, 'PercentReferenceLevels', [20 80]), 1e-10);

[wd, ic, fcr, m2] = pulsewidth(bl, 1000);
chkvec('pw1', wd, 1e-10); chkvec('pw1i', ic, 1e-10); chkvec('pw1f', fcr, 1e-10);
chknum('pw1m', m2, 1e-11);
chkvec('pw2', pulsewidth(bl, 1000, 'Polarity', 'negative'), 1e-10);
[pp2, i3, f3, n3] = pulseperiod(bl, 1000);
chkvec('pp1', pp2, 1e-10); chkvec('pp1i', i3, 1e-10);
chkvec('pp1f', f3, 1e-10); chkvec('pp1n', n3, 1e-10);
chkvec('ps1', pulsesep(bl, 1000), 1e-10);
chkvec('dc1', dutycycle(bl, 1000), 1e-10);
chkvec('dc2', dutycycle(0.001, [100 200 300]), 1e-12);

[os, ol, oi] = overshoot(bl, 1000);
chkvec('os1', os, 1e-10); chkvec('os1l', ol, 1e-10); chkvec('os1i', oi, 1e-10);
chkvec('os2', overshoot(bl, 1000, 'Region', 'preshoot'), 1e-10);
[us, ul, ui] = undershoot(bl, 1000);
chkvec('us1', us, 1e-10); chkvec('us1l', ul, 1e-10); chkvec('us1i', ui, 1e-10);
[st, sl2, si] = settlingtime(bl, 1000, 0.02);
chkvec('st1', st, 1e-10); chkvec('st1l', sl2, 1e-10); chkvec('st1i', si, 1e-10);
chkvec('st2', settlingtime(bl, 1000, 0.03, 'Tolerance', 5), 1e-10);

% ---------------------------------------------------------------------------------------------
% Alignment, distance and the correlation forms.
% ---------------------------------------------------------------------------------------------
na = 60;
ta = (0:na-1)';
aa = sin(2*pi*ta/13) + 0.3*sin(2*pi*ta/5);
bb = [zeros(7,1); aa(1:end-7)];
uu = [1 2 3 4 5 6 7 8];
vv = [4 5 6];

chknum('fd1', finddelay(aa, bb), 1e-12);
chknum('fd2', finddelay(bb, aa), 1e-12);
chknum('fd3', finddelay(aa, bb, 5), 1e-12);
chknum('fd4', finddelay(uu, vv), 1e-12);
[pq, qq, dd] = alignsignals(aa, bb);
chknum('as1', dd, 1e-12);
chkvec('as1p', pq, 1e-12); chkvec('as1q', qq, 1e-12);
[p2b, ~, d2b] = alignsignals(aa, bb, [], 'truncate');
chknum('as2', d2b, 1e-12); chkvec('as2p', p2b, 1e-12);
[p3b, q3b, d3b] = alignsignals(uu, vv);
chkvec('as3p', p3b, 1e-12); chkvec('as3q', q3b, 1e-12); chknum('as3d', d3b, 1e-12);

x1 = [1 2 3 4 5 6 7 8 9];
y1 = [1 3 4 9 8 2 1 5 7 9];
[dw, iw, jw] = dtw(x1, y1);
chknum('dtw1', dw, 1e-12); chkexact('dtw1i', iw); chkexact('dtw1j', jw);
[d4, i4, j4] = dtw(x1, y1, 'absolute');
chknum('dtw2', d4, 1e-12); chkexact('dtw2i', i4); chkexact('dtw2j', j4);
chknum('dtw3', dtw(x1, y1, 'squared'), 1e-12);
chknum('dtw4', dtw(x1, y1, 3), 1e-12);
chknum('dtw5', dtw(aa', bb'), 1e-12);
[de, ex, ey] = edr(x1, y1, 1);
chknum('edr1', de, 1e-12); chkexact('edr1i', ex); chkexact('edr1j', ey);
chknum('edr2', edr(x1, y1, 2, 'absolute'), 1e-12);

datarec = [zeros(1,10) vv zeros(1,5) vv+0.1 zeros(1,8)];
[is, ie, ds] = findsignal(datarec, vv);
chkexact('fs1s', is); chkexact('fs1e', ie); chknum('fs1d', ds, 1e-10);
[is2, ie2, ds2] = findsignal(datarec, vv, 'MaxNumSegments', 2);
chkexact('fs2s', is2); chkexact('fs2e', ie2); chkspec('fs2d', ds2, 1e-9);

A = [1 2 3; 4 5 6; 7 8 9];
B = [1 0; 0 -1];
chkspec('xc2a', xcorr2(A, B), 1e-12);
chkspec('xc2b', xcorr2(A), 1e-12);
chkspec('cc1', cconv([1 2 3 4], [1 1 1], 4), 1e-12);
chkspec('cc2', cconv([1 2 3 4], [1 1 1]), 1e-12);
chkspec('cc3', cconv([1 2 3 4]', [1 1 1]', 6), 1e-12);
chkspec('cm1', convmtx([1 2 3], 4), 1e-12);
chkspec('cm2', convmtx([1 2 3]', 4), 1e-12);
chkexact('cm3', size(convmtx([1 2 3]', 4)));

% ---------------------------------------------------------------------------------------------
% The change detectors.
% ---------------------------------------------------------------------------------------------
nc = 120;
tc = (0:nc-1)';
xcs = 0.4*sin(2*pi*tc/17) + 0.1*mod(tc, 7)/7;
xcs(61:end) = xcs(61:end) + 2;
zcs = 0.3*sin(2*pi*tc/9);
zcs(41:80) = zcs(41:80)*4;
zcs(81:end) = zcs(81:end) + 0.02*(0:39)';

chkexact('cu1', cusum(xcs));
[iu, il, uss, lss] = cusum(xcs);
chkexact('cu1b', [iu il]);
chkspec('cu1u', uss, 1e-10); chkspec('cu1l', lss, 1e-10);
chkexact('cu2', cusum(xcs, 3));
chkexact('cu3', cusum(xcs, 3, 2));
chkexact('cu4', cusum(xcs, 3, 1, 0, 0.5));
[au, al] = cusum(xcs, 5, 1, 0, 0.5, 'all');
chkexact('cu5u', au); chkexact('cu5l', al);

chkexact('cp1', findchangepts(xcs));
[~, rres] = findchangepts(xcs);
chknum('cp1r', rres, 1e-10);
chkexact('cp2', findchangepts(xcs, 'MaxNumChanges', 3));
[~, r3] = findchangepts(xcs, 'MaxNumChanges', 3);
chknum('cp2r', r3, 1e-10);
chkexact('cp3', findchangepts(zcs, 'Statistic', 'rms'));
chkexact('cp4', findchangepts(zcs, 'Statistic', 'std', 'MaxNumChanges', 2));
chkexact('cp5', findchangepts(zcs, 'Statistic', 'linear', 'MaxNumChanges', 2));
chkexact('cp6', findchangepts(xcs, 'MinThreshold', 20));
chkexact('cp7', findchangepts(xcs, 'MinDistance', 30, 'MaxNumChanges', 2));
chkexact('cp8', findchangepts(xcs'));

% ---------------------------------------------------------------------------------------------
% Helpers.
% ---------------------------------------------------------------------------------------------
function chkspec(name, v, tol)
%CHKSPEC A spectrum or a list of coefficients, pinned against the scale of the list it lives in.
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
