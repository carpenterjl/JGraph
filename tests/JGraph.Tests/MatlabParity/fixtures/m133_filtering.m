% m133_filtering.m -- the Signal Processing Toolbox's filtering, coefficient conversions and
% multirate names (M133).
%
% Four kinds of line are pinned here.
%
% A *conversion* is pinned elementwise. These are small answers -- a six-column section matrix, a
% state-space quadruple, a list of roots -- and every element of them is a decision as well as a
% number: which pole went in which section, which zero was paired with it, where the gain landed.
% A digest over such a matrix would pass while the sections were in the wrong order, which is the
% one mistake worth catching, so the sections are pinned row by row and the row count exact.
%
% A *filter pass* is pinned by digests over its output. The output is as long as the signal and
% almost entirely uninteresting; what matters is that it is the same everywhere, which is what the
% mass and total variation of the answer and of its difference from the input say.
%
% A *rate change* is pinned by digests and by its length exactly. The length is the statement about
% the method -- how the filter's delay was trimmed, whether the last partial frame was kept -- and
% the digests say the samples inside it agree.
%
% Anything that counts -- a section count, a lattice's order, a frame's length, the number of
% outliers a Hampel pass found -- is pinned exact, because nothing hides a difference of one.

% --- the signals every section shares --------------------------------------------------------
n = (0:199)';
x = sin(2*pi*n/25) + 0.3*cos(2*pi*n/7) + 0.05*sin(2*pi*n/3.5);
xrow = x.';
xwide = [x, circshift(x, 40), 0.5*x];

b3 = [1 -0.5 0.2];
a3 = [1 0.3 -0.1 0.05];

% --- transfer function to roots and back ------------------------------------------------------
[z, p, k] = tf2zp(b3, a3);
chkroots('tf2zp_z', z, 1e-12);
chkroots('tf2zp_p', p, 1e-12);
fprintf('CHK|tf2zp_k|%.17g|rel=1e-12\n', k);
fprintf('CHK|tf2zp_zn|%d|exact\n', numel(z));

[z2, p2, k2] = tf2zpk([1 2 3], [1 0.4 -0.2 0.03]);
chkroots('tf2zpk_z', z2, 1e-12);
chkroots('tf2zpk_p', p2, 1e-12);
fprintf('CHK|tf2zpk_k|%.17g|rel=1e-12\n', k2);

[nn, dd] = zp2tf(z, p, k);
chkrow('zp2tf_n', nn, 1e-12);
chkrow('zp2tf_d', dd, 1e-12);

% A numerator of several rows: one system, several outputs, one column of zeros each.
[zm, pm, km] = tf2zp([1 2 3; 0 1 -1], [1 0.5 0.1]);
fprintf('CHK|tf2zp_mrows|%d|exact\n', size(zm, 1));
fprintf('CHK|tf2zp_mcols|%d|exact\n', size(zm, 2));
chkroots('tf2zp_mz', zm(:), 1e-12);
chkroots('tf2zp_mk', km, 1e-12);

% --- state space --------------------------------------------------------------------------------
[A, B, C, D] = tf2ss(b3, a3);
chkmat('tf2ss_A', A, 1e-12);
chkmat('tf2ss_B', B, 1e-12);
chkmat('tf2ss_C', C, 1e-12);
chkmat('tf2ss_D', D, 1e-12);

[n2, d2] = ss2tf(A, B, C, D);
chkrow('ss2tf_n', n2, 1e-10);
chkrow('ss2tf_d', d2, 1e-10);

[z3, p3, k3] = ss2zp(A, B, C, D);
chkroots('ss2zp_p', p3, 1e-10);
fprintf('CHK|ss2zp_k|%.17g|rel=1e-10\n', k3);
fprintf('CHK|ss2zp_zn|%d|exact\n', numel(z3));

zz = [0.5; -0.5; 0.3+0.4i; 0.3-0.4i];
pz = [0.9*exp(1i*0.3); 0.9*exp(-1i*0.3); 0.6; -0.7; 0.2];
[A2, B2, C2, D2] = zp2ss(zz, pz, 2);
chkmat('zp2ss_A', A2, 1e-12);
chkmat('zp2ss_B', B2, 1e-12);
chkmat('zp2ss_C', C2, 1e-12);
chkmat('zp2ss_D', D2, 1e-12);

% --- padding, stabilising, scaling, partial fractions --------------------------------------------
[eb, ea, en, em] = eqtflength([1 2 0 0], [1 0.5]);
chkrow('eqtf_b', eb, 1e-15);
chkrow('eqtf_a', ea, 1e-15);
fprintf('CHK|eqtf_n|%d|exact\n', en);
fprintf('CHK|eqtf_m|%d|exact\n', em);
[eb2, ea2] = eqtflength([1 2], [1 0.5 0.2 0.1]);
chkrow('eqtf2_b', eb2, 1e-15);

chkrow('polystab1', polystab([1 2 3 4]), 1e-12);
chkrow('polystab2', polystab([1 -3 2]), 1e-12);
chkrow('polystab3', polystab([2 0 0 -8]), 1e-12);
chkrow('polyscale1', polyscale([1 2 3 4], 0.85), 1e-14);
chkrow('polyscale2', polyscale([1 -0.9 0.81], 1.1), 1e-14);

[r1, q1, s1] = residuez([1 2], [1 -0.5 0.06]);
chkroots('residuez_r', r1, 1e-11);
chkroots('residuez_p', q1, 1e-11);
fprintf('CHK|residuez_kn|%d|exact\n', numel(s1));
[rb, ra] = residuez(r1, q1, s1);
chkroots('residuez_b', rb, 1e-11);
chkroots('residuez_a', ra, 1e-11);

[r2, q2, s2] = residuez([1 0 -1], [1 -0.6 0.09]);
chkroots('residuez2_r', r2, 1e-9);
chkroots('residuez2_p', q2, 1e-9);
chkroots('residuez2_k', s2, 1e-11);

% --- second-order sections over twelve root sets ---------------------------------------------
sets = cell(1, 12);
sets{1}  = {[], [0.5; -0.5], 1};
sets{2}  = {[], [0.9*exp(1i*0.4); 0.9*exp(-1i*0.4)], 2};
sets{3}  = {[-1; -1], [0.8*exp(1i*0.2); 0.8*exp(-1i*0.2)], 0.25};
sets{4}  = {[0.5], [0.6; 0.7; -0.8], 1.5};
sets{5}  = {[0.3+0.4i; 0.3-0.4i], [0.2; 0.4; 0.6; 0.8], 1};
sets{6}  = {zz, pz, 2};
sets{7}  = {[1; -1], [0.95*exp(1i*0.1); 0.95*exp(-1i*0.1); 0.5], -3};
sets{8}  = {[], [0.1; 0.2; 0.3; 0.4; 0.5; 0.6], 1};
sets{9}  = {[0.9; -0.9; 0.5+0.5i; 0.5-0.5i], [0.99*exp(1i*0.05); 0.99*exp(-1i*0.05); 0.3+0.3i; 0.3-0.3i], 1};
sets{10} = {[0; 0], [0.7*exp(1i*1.2); 0.7*exp(-1i*1.2); 0.7*exp(1i*2.4); 0.7*exp(-1i*2.4)], 4};
sets{11} = {[2; 0.5], [0.85; -0.85; 0.4], 1};
sets{12} = {[-1; -1; -1; -1], [0.6*exp(1i*0.3); 0.6*exp(-1i*0.3); 0.8*exp(1i*0.9); 0.8*exp(-1i*0.9); 0.2], 0.5};

for i = 1:numel(sets)
    zi = sets{i}{1};
    pi_ = sets{i}{2};
    ki = sets{i}{3};
    [sos, g] = zp2sos(zi, pi_, ki);
    chksos(sprintf('zp2sos%d', i), sos, g);
    [sd, gd] = zp2sos(zi, pi_, ki, 'down');
    chksos(sprintf('zp2sosdown%d', i), sd, gd);
    one = zp2sos(zi, pi_, ki);
    chkmat(sprintf('zp2sos1out%d', i), one, 1e-12);
    [zb, pb, kb] = sos2zp(sos, g);
    fprintf('CHK|sos2zp%d_zn|%d|exact\n', i, numel(zb));
    fprintf('CHK|sos2zp%d_pn|%d|exact\n', i, numel(pb));
    fprintf('CHK|sos2zp%d_k|%.17g|rel=1e-10\n', i, kb);
    [bb, aa] = sos2tf(sos, g);
    chkrow(sprintf('sos2tf%d_b', i), bb, 1e-10);
    chkrow(sprintf('sos2tf%d_a', i), aa, 1e-10);
end

% Scaling changes where the gain sits and nothing else about the filter.
[si, gi] = zp2sos(zz, pz, 2, 'up', 'inf');
chksos('zp2sos_inf', si, gi);
[st, gt] = zp2sos(zz, pz, 2, 'down', 2);
chksos('zp2sos_two', st, gt);
[sk, gk] = zp2sos([0.5; -0.5; 0.25; -0.25], [0.6; 0.7; 0.8; 0.9], 1, 'up', 'none', true);
chksos('zp2sos_krz', sk, gk);

[ts, tg] = tf2sos(b3, a3);
chksos('tf2sos', ts, tg);
[ts2, tg2] = tf2sos([1 0 0 0 0 -1], [1 0.2 0.04 0.008], 'down', 'inf');
chksos('tf2sos_down', ts2, tg2);

[ss1, sg1] = ss2sos(A, B, C, D);
chksos('ss2sos', ss1, sg1);

[sa, sb, sc, sd2] = sos2ss(ts, tg);
chkmat('sos2ss_A', sa, 1e-10);
chkmat('sos2ss_C', sc, 1e-10);
chkmat('sos2ss_D', sd2, 1e-10);

[cn, cd] = sos2ctf(ts);
chkmat('sos2ctf_n', cn, 1e-12);
chkmat('sos2ctf_d', cd, 1e-12);
[cn2, cd2] = sos2ctf(ts, [tg 1 1]);
chkmat('sos2ctf2_n', cn2, 1e-12);

[qn, qd, qg] = zp2ctf(zz, pz, 2);
chkmat('zp2ctf_n', qn, 1e-12);
chkmat('zp2ctf_d', qd, 1e-12);
fprintf('CHK|zp2ctf_g|%.17g|rel=1e-12\n', qg);
[q4n, q4d, q4g] = zp2ctf(zz, pz, 2, SectionOrder=4);
chkmat('zp2ctf4_n', q4n, 1e-12);
chkmat('zp2ctf4_d', q4d, 1e-12);
fprintf('CHK|zp2ctf4_g|%.17g|rel=1e-12\n', q4g);
[q5n, q5d] = zp2ctf(zz, pz, 2, Direction="down", Scale="inf");
chkmat('zp2ctf_dinf_n', q5n, 1e-11);
chkmat('zp2ctf_dinf_d', q5d, 1e-11);

chkmat('scaleFS1', scaleFilterSections(ts(:, 1:3), 0.5), 1e-12);
chkmat('scaleFS2', scaleFilterSections(ts(:, 1:3), [1.5; 0.5; -2]), 1e-12);

cc = sos2cell(ts, tg);
fprintf('CHK|sos2cell_n|%d|exact\n', numel(cc));
fprintf('CHK|sos2cell_11n|%d|exact\n', numel(cc{1}{1}));
[cs, cg] = cell2sos(cc);
chkmat('cell2sos', cs, 1e-12);
fprintf('CHK|cell2sos_g|%.17g|rel=1e-12\n', cg);

% --- lattices -----------------------------------------------------------------------------------
kfir = tf2latc([1 0.5 0.25 0.125]);
chkcol('tf2latc_fir', kfir, 1e-12);
kmin = tf2latc([1 0.5 0.25 0.125], 'min');
chkcol('tf2latc_min', kmin, 1e-12);
kmax = tf2latc([1 0.5 0.25 0.125], 'max');
chkcol('tf2latc_max', kmax, 1e-12);
kall = tf2latc(1, a3);
chkcol('tf2latc_allpole', kall, 1e-12);
[kiir, viir] = tf2latc(b3, a3);
chkcol('tf2latc_k', kiir, 1e-11);
chkcol('tf2latc_v', viir, 1e-11);

chkrow('latc2tf_fir', latc2tf(kfir), 1e-12);
chkrow('latc2tf_max', latc2tf(kfir, 'max'), 1e-12);
[lap_n, lap_d] = latc2tf(kfir, 'allpass');
chkrow('latc2tf_apn', lap_n, 1e-12);
chkrow('latc2tf_apd', lap_d, 1e-12);
[lpn, lpd] = latc2tf(kall, 'allpole');
chkrow('latc2tf_poln', lpn, 1e-12);
chkrow('latc2tf_pold', lpd, 1e-12);
[lin, lid] = latc2tf(kiir, viir);
chkrow('latc2tf_iirn', lin, 1e-10);
chkrow('latc2tf_iird', lid, 1e-10);

% --- filtering ------------------------------------------------------------------------------------
% An eighth-order lowpass written out by its roots rather than designed, because butter's two-output
% form is M134's (see the div= lines in m124_signal.m) and this milestone should not wait for it.
p8 = 0.7 * exp(1i * [0.2; -0.2; 0.5; -0.5; 0.9; -0.9; 1.3; -1.3]);
z8 = -ones(8, 1);
[bb8, ab8] = zp2tf(z8, p8, 0.01);
chkvec('filtfilt_b8', filtfilt(bb8, ab8, x), 1e-10);
chkvec('filtfilt_b8row', filtfilt(bb8, ab8, xrow).', 1e-10);
chkvec('filtfilt_short', filtfilt(b3, a3, x(1:60)), 1e-10);
chkvec('filtfilt_wide', reshape(filtfilt(b3, a3, xwide), [], 1), 1e-10);
chkvec('filtfilt_long', filtfilt(b3, a3, repmat(x, 60, 1)), 1e-10);

[sosb, gb] = zp2sos(z8, p8, 0.01);
chkvec('filtfilt_sos', filtfilt(sosb, gb, x), 1e-10);
chkvec('sosfilt', sosfilt(sosb, x), 1e-11);
chkvec('sosfilt_wide', reshape(sosfilt(sosb, xwide), [], 1), 1e-11);

chkvec('fftfilt1', fftfilt([1 2 3 2 1] / 9, x), 1e-11);
chkvec('fftfilt2', fftfilt(ones(64, 1) / 64, x), 1e-11);
chkvec('fftfilt3', fftfilt([1 -1], x, 32), 1e-11);
chkvec('fftfilt_row', fftfilt([1 2 1] / 4, xrow).', 1e-11);

chkrow('filtic1', filtic(b3, a3, [1 2 3], [4 5]), 1e-12);
chkrow('filtic2', filtic(b3, a3, [1 2 3]), 1e-12);
chkrow('filtic3', filtic([1 2], [1], [], [7 8]), 1e-12);

[lf, lg, lzf] = latcfilt([0.5 1], x);
chkvec('latcfilt_f', lf, 1e-12);
chkvec('latcfilt_g', lg, 1e-12);
chkcol('latcfilt_zf', lzf, 1e-12);
[lf2, lg2] = latcfilt(kall, 1, x);
chkvec('latcfilt_pole_f', lf2, 1e-10);
chkvec('latcfilt_pole_g', lg2, 1e-10);
[lf3, lg3] = latcfilt(kiir, viir, x);
chkvec('latcfilt_ladder_f', lf3, 1e-10);
chkvec('latcfilt_ladder_g', lg3, 1e-10);
[lf4, lg4, lz4] = latcfilt([0.5 1], x, 'ic', [0.25 -0.25]);
chkvec('latcfilt_ic_f', lf4, 1e-12);
chkcol('latcfilt_ic_zf', lz4, 1e-12);

chkvec('medfilt1_3', medfilt1(x), 1e-14);
chkvec('medfilt1_7', medfilt1(x, 7), 1e-14);
chkvec('medfilt1_8', medfilt1(x, 8), 1e-14);
chkvec('medfilt1_tr', medfilt1(x, 7, [], 1, 'truncate'), 1e-14);
xn = x; xn(30) = NaN; xn(120) = NaN;
chkvec('medfilt1_omit', medfilt1(xn, 5, [], 1, 'omitnan'), 1e-14);
chkvec('medfilt1_wide', reshape(medfilt1(xwide, 5), [], 1), 1e-14);

[hy, hi, hm, hs] = hampel(x, 5, 2);
chkvec('hampel_y', hy, 1e-12);
fprintf('CHK|hampel_count|%d|exact\n', sum(hi));
chkvec('hampel_m', hm, 1e-12);
chkvec('hampel_s', hs, 1e-12);
xo = x; xo(40) = 8; xo(41) = -6;
[hy2, hi2] = hampel(xo, 3);
fprintf('CHK|hampel2_count|%d|exact\n', sum(hi2));
chkvec('hampel2_y', hy2, 1e-12);

[sgb, sgg] = sgolay(3, 11);
chkmat('sgolay_b', sgb, 1e-11);
chkmat('sgolay_g', sgg, 1e-11);
[sgb2, sgg2] = sgolay(5, 25);
chkmat('sgolay2_b', sgb2, 1e-10);
chkmat('sgolay2_g', sgg2, 1e-10);
sgw = sgolay(2, 9, (1:9).' / 9);
chkmat('sgolay_w', sgw, 1e-11);

chkvec('sgolayfilt_3_11', sgolayfilt(x, 3, 11), 1e-11);
chkvec('sgolayfilt_5_25', sgolayfilt(x, 5, 25), 1e-10);
chkvec('sgolayfilt_wide', reshape(sgolayfilt(xwide, 3, 11), [], 1), 1e-11);
chkvec('sgolayfilt_w', sgolayfilt(x, 2, 9, (1:9).' / 9), 1e-11);

% --- multirate --------------------------------------------------------------------------------
u3 = upsample(x, 3);
fprintf('CHK|upsample_n|%d|exact\n', numel(u3));
chkvec('upsample3', u3, 1e-13);
chkvec('upsample3p1', upsample(x, 3, 1), 1e-13);
chkvec('upsample_row', upsample(xrow, 2).', 1e-13);
chkvec('upsample_wide', reshape(upsample(xwide, 2), [], 1), 1e-13);

d3 = downsample(x, 3);
fprintf('CHK|downsample_n|%d|exact\n', numel(d3));
chkvec('downsample3', d3, 1e-13);
chkvec('downsample3p2', downsample(x, 3, 2), 1e-13);
chkvec('downsample_wide', reshape(downsample(xwide, 4), [], 1), 1e-13);

h5 = [1 2 3 2 1] / 9;
uf = upfirdn(x, h5, 3, 2);
fprintf('CHK|upfirdn_n|%d|exact\n', numel(uf));
chkvec('upfirdn32', uf, 1e-12);
chkvec('upfirdn11', upfirdn(x, h5), 1e-12);
chkvec('upfirdn13', upfirdn(x, h5, 1, 3), 1e-12);
chkvec('upfirdn_row', upfirdn(xrow, h5, 2, 3).', 1e-12);

[iy, ib] = interp(x, 3);
fprintf('CHK|interp3_n|%d|exact\n', numel(iy));
chkvec('interp3', iy, 1e-9);
chkcol('interp3_b', ib, 1e-10);
chkvec('interp4', interp(x, 4), 1e-9);
chkvec('interp2n2', interp(x, 2, 2, 0.4), 1e-9);
chkvec('interp_row', interp(xrow, 3).', 1e-9);

dy = decimate(x, 4);
fprintf('CHK|decimate4_n|%d|exact\n', numel(dy));
chkvec('decimate4', dy, 1e-9);
chkvec('decimate3', decimate(x, 3), 1e-9);
chkvec('decimate_n5', decimate(x, 4, 5), 1e-9);
df = decimate(x, 4, 'fir');
fprintf('CHK|decimate_fir_n|%d|exact\n', numel(df));
chkvec('decimate_fir', df, 1e-9);
chkvec('decimate_fir20', decimate(x, 5, 20, 'fir'), 1e-9);
chkvec('decimate_row', decimate(xrow, 4).', 1e-9);

[ry, rb] = resample(x, 3, 2);
fprintf('CHK|resample32_n|%d|exact\n', numel(ry));
chkvec('resample32', ry, 1e-9);
chkrow('resample32_b', rb, 1e-10);
r57 = resample(x, 5, 7);
fprintf('CHK|resample57_n|%d|exact\n', numel(r57));
chkvec('resample57', r57, 1e-9);
chkvec('resample_n4', resample(x, 3, 2, 4), 1e-9);
chkvec('resample_beta', resample(x, 3, 2, 10, 8), 1e-9);
chkvec('resample_given', resample(x, 2, 3, h5), 1e-9);
chkvec('resample_row', resample(xrow, 2, 3).', 1e-9);
chkvec('resample_wide', reshape(resample(xwide, 3, 2), [], 1), 1e-9);
chkvec('resample_same', resample(x, 2, 2), 1e-12);

% --- repair -----------------------------------------------------------------------------------
xg = x;
xg(50:60) = NaN;
xg(150) = NaN;
chkvec('fillgaps_8', fillgaps(xg, 40, 8), 1e-9);
% The order-choosing form is pinned looser than the rest, and it earns it. Akaike's criterion picks
% an autoregressive model of order forty-seven for this segment, and that model has a pole just
% outside the unit circle -- 1.0009 -- so running it eleven samples into the gap grows whatever
% difference its coefficients started with rather than damping it. The two engines fit the same
% model to the last few bits and their predictions from it agree to seven figures. The fixed-order
% forms above and below are pinned at 1e-9 and are the better test of the fit itself.
chkvec('fillgaps_aic', fillgaps(xg), 1e-6);
xg2 = x;
xg2(1:5) = NaN;
xg2(196:200) = NaN;
chkvec('fillgaps_edges', fillgaps(xg2, 60, 6), 1e-9);

[eu, el] = envelope(x);
chkvec('env_u', eu, 1e-10);
chkvec('env_l', el, 1e-10);
[eu2, el2] = envelope(x, 30, 'rms');
chkvec('envrms_u', eu2, 1e-11);
chkvec('envrms_l', el2, 1e-11);
[eu3, el3] = envelope(x, 31);
chkvec('envfir_u', eu3, 1e-10);
[eu4, el4] = envelope(x, 20, 'peaks');
chkvec('envpk_u', eu4, 1e-9);
chkvec('envpk_l', el4, 1e-9);
[ewu, ewl] = envelope(xwide, 40, 'rms');
chkvec('envwide_u', reshape(ewu, [], 1), 1e-11);

% --- helpers ----------------------------------------------------------------------------------

function chkrow(name, v, tol)
%CHKROW Pins a short row of coefficients elementwise.
    u = v(:);
    fprintf('CHK|r_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        chknum(sprintf('r_%s_%d', name, i), u(i), tol);
    end
end

function chkcol(name, v, tol)
%CHKCOL The same for a column, whose orientation is also pinned.
    fprintf('CHK|c_%s_rows|%d|exact\n', name, size(v, 1));
    fprintf('CHK|c_%s_cols|%d|exact\n', name, size(v, 2));
    u = v(:);
    for i = 1:numel(u)
        chknum(sprintf('c_%s_%d', name, i), u(i), tol);
    end
end

function chkmat(name, m, tol)
%CHKMAT Pins a small matrix elementwise, shape first.
    fprintf('CHK|m_%s_rows|%d|exact\n', name, size(m, 1));
    fprintf('CHK|m_%s_cols|%d|exact\n', name, size(m, 2));
    u = m(:);
    for i = 1:numel(u)
        chknum(sprintf('m_%s_%d', name, i), u(i), tol);
    end
end

function chksos(name, sos, g)
%CHKSOS Pins a cascade row by row, with its section count exact.
%
%   Elementwise rather than by digest, because the interesting mistakes here are about arrangement
%   -- which pole went in which section and which section came first -- and every one of them leaves
%   the multiset of coefficients alone.
    fprintf('CHK|sos_%s_n|%d|exact\n', name, size(sos, 1));
    for r = 1:size(sos, 1)
        for c = 1:6
            chknum(sprintf('sos_%s_%d_%d', name, r, c), sos(r, c), 1e-12);
        end
    end
    chknum(sprintf('sos_%s_g', name), g, 1e-12);
end

function chknum(name, value, tol)
%CHKNUM Pins one number, relatively unless it is a coefficient that algebra says is zero.
%
%   A section's unused numerator slot, a projection matrix's off-diagonal, a stabilised
%   polynomial's cancelled term: each of these is exactly zero in the formula and a few units of
%   the last place in arithmetic, and the two engines reach that dust by different roads. A
%   relative rule on 1e-16 is a rule about which road, which is not what any of this is testing.
    if abs(value) < 1e-9
        fprintf('CHK|%s|%.17g|abs=1e-9\n', name, value);
    else
        fprintf('CHK|%s|%.17g|rel=%g\n', name, value, tol);
    end
end

function chkroots(name, v, tol)
%CHKROOTS Pins a list of roots by its size and by two order-free digests.
%
%   Order-free on purpose. Two eigensolvers that agree on a polynomial's roots to the last bit can
%   still list a pair of real roots the other way round, and that says nothing about either of them.
%   The sum and the sum of squares between them see any change to the multiset.
    u = v(:);
    fprintf('CHK|k_%s_n|%d|exact\n', name, numel(u));
    fprintf('CHK|k_%s_sr|%.17g|rel=%g\n', name, sum(real(u)), tol);
    fprintf('CHK|k_%s_si|%.17g|abs=%g\n', name, sum(imag(u)), tol);
    fprintf('CHK|k_%s_qr|%.17g|rel=%g\n', name, sum(real(u).^2 - imag(u).^2), tol);
    fprintf('CHK|k_%s_m|%.17g|rel=%g\n', name, sum(abs(u)), tol);
end

function chkvec(name, v, tol)
%CHKVEC Pins a signal by four digests, which between them see a change anywhere in it.
%
%   None of the four is a plain sum. Most of these signals oscillate about zero, so their sum is a
%   cancellation of two hundred numbers of order one down to a number of order 1e-16 -- a quantity
%   whose relative error is total and which says nothing. The mass and the total variation are
%   cancellation-free and see any change of magnitude or shape; the two ratios carry the sign and
%   position information a magnitude alone would lose, and are bounded by one.
    u = v(:);
    n = numel(u);
    mass = sum(abs(u));
    fprintf('CHK|v_%s_n|%d|exact\n', name, n);
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
