% m134_design.m -- M134's fifty-five names: the five analogue prototypes, the five classical IIR
% designs and their four minimum-order rules, the four band transforms and the two maps to the unit
% circle, twenty FIR designs, and the seventeen analysis names.
%
% Two rules run through the file.
%
% A design's coefficients are pinned RELATIVE TO THE VECTOR THEY LIVE IN rather than to themselves.
% A bandpass design's numerator alternates between coefficients of order 1e11 and coefficients that
% algebra says are zero; the second kind arrive as a few units in the last place of the first kind,
% which is dust, and a relative rule on dust is a rule about which order two engines summed a
% cancelling sum in.
%
% A root list is pinned by its count and by order-free digests. Two eigensolvers that agree on a
% polynomial's roots to the last bit can still list a conjugate-free pair the other way round --
% ADR 0137 recorded that for `roots`, and the transmission-zero route MATLAB's own designs take for
% `cheby2`, `ellip` and `besself` has the same freedom.

% ---------------------------------------------------------------------------------------------
% The five analogue prototypes.
% ---------------------------------------------------------------------------------------------
for n = [1 2 3 4 5 8]
    [z, p, k] = buttap(n);
    chkcplx(sprintf('buttap%d_p', n), p, 1e-12);
    chknum(sprintf('buttap%d_k', n), k, 1e-12);
    fprintf('CHK|buttap%d_nz|%d|exact\n', n, numel(z));

    [z, p, k] = cheb1ap(n, 1.5);
    chkcplx(sprintf('cheb1ap%d_p', n), p, 1e-12);
    chknum(sprintf('cheb1ap%d_k', n), k, 1e-12);
    fprintf('CHK|cheb1ap%d_nz|%d|exact\n', n, numel(z));

    [z, p, k] = cheb2ap(n, 40);
    chkcplx(sprintf('cheb2ap%d_z', n), z, 1e-12);
    chkcplx(sprintf('cheb2ap%d_p', n), p, 1e-12);
    chknum(sprintf('cheb2ap%d_k', n), k, 1e-12);

    [z, p, k] = ellipap(n, 1.5, 40);
    chkcplx(sprintf('ellipap%d_z', n), z, 1e-11);
    chkcplx(sprintf('ellipap%d_p', n), p, 1e-11);
    chknum(sprintf('ellipap%d_k', n), k, 1e-11);

    [z, p, k] = besselap(n);
    chkcplx(sprintf('besselap%d_p', n), p, 1e-13);
    chknum(sprintf('besselap%d_k', n), k, 1e-13);
    fprintf('CHK|besselap%d_nz|%d|exact\n', n, numel(z));
end

[~, p25, ~] = besselap(25);
chkroots('besselap25', p25, 1e-12);
[z6, p6, k6] = ellipap(6, 0.5, 60);
chkcplx('ellipap6b_z', z6, 1e-9);
chkcplx('ellipap6b_p', p6, 1e-9);
chknum('ellipap6b_k', k6, 1e-9);

% ---------------------------------------------------------------------------------------------
% The five classical designs, in every band and both domains.
% ---------------------------------------------------------------------------------------------
bands = {'low', 'high', 'bandpass', 'stop'};
digital = {0.3, 0.4, [0.25 0.6], [0.25 0.6]};
analogue = {40, 40, [30 90], [30 90]};

for t = 1:4
    bt = bands{t};
    wn = digital{t};
    aw = analogue{t};
    for n = [3 6]
        [b, a] = butter(n, wn, bt);
        chkcoef(sprintf('butter_%s_%d_b', bt, n), b, 1e-10);
        chkcoef(sprintf('butter_%s_%d_a', bt, n), a, 1e-10);

        [b, a] = cheby1(n, 1.5, wn, bt);
        chkcoef(sprintf('cheby1_%s_%d_b', bt, n), b, 1e-10);
        chkcoef(sprintf('cheby1_%s_%d_a', bt, n), a, 1e-10);

        [b, a] = cheby2(n, 40, wn, bt);
        chkcoef(sprintf('cheby2_%s_%d_b', bt, n), b, 1e-10);
        chkcoef(sprintf('cheby2_%s_%d_a', bt, n), a, 1e-10);

        [b, a] = ellip(n, 1.5, 40, wn, bt);
        chkcoef(sprintf('ellip_%s_%d_b', bt, n), b, 1e-10);
        chkcoef(sprintf('ellip_%s_%d_a', bt, n), a, 1e-10);
    end

    [b, a] = butter(4, aw, bt, 's');
    chkcoef(sprintf('buttera_%s_b', bt), b, 1e-10);
    chkcoef(sprintf('buttera_%s_a', bt), a, 1e-10);
    [b, a] = cheby1(4, 1.5, aw, bt, 's');
    chkcoef(sprintf('cheby1a_%s_b', bt), b, 1e-10);
    chkcoef(sprintf('cheby1a_%s_a', bt), a, 1e-10);
    [b, a] = cheby2(4, 40, aw, bt, 's');
    chkcoef(sprintf('cheby2a_%s_b', bt), b, 1e-9);
    chkcoef(sprintf('cheby2a_%s_a', bt), a, 1e-9);
    [b, a] = ellip(4, 1.5, 40, aw, bt, 's');
    chkcoef(sprintf('ellipa_%s_b', bt), b, 1e-9);
    chkcoef(sprintf('ellipa_%s_a', bt), a, 1e-9);
    [b, a] = besself(4, aw, bt);
    % The Bessel highpass and bandstop have four zeros stacked on one point, so every coefficient
    % of their numerators past the leading one is noise in both engines; the digests see the shape
    % without pretending the dust means anything.
    chkvec(sprintf('bessela_%s_b', bt), b, 1e-8);
    chkcoef(sprintf('bessela_%s_a', bt), a, 1e-9);
end

% The single output is the numerator alone, and the three-output form is the roots.
bs = butter(4, 0.3);
fprintf('CHK|butter_single_n|%d|exact\n', numel(bs));
chkcoef('butter_single', bs, 1e-12);
[z, p, k] = butter(6, [0.25 0.6]);
chkroots('butter_zpk_z', z, 1e-11);
chkroots('butter_zpk_p', p, 1e-11);
chknum('butter_zpk_k', k, 1e-11);
[z, p, k] = ellip(5, 1, 40, 0.35, 'high');
chkroots('ellip_zpk_z', z, 1e-10);
chkroots('ellip_zpk_p', p, 1e-10);
chknum('ellip_zpk_k', k, 1e-10);
[A, B, C, D] = butter(4, 0.3);
chkcoef('butter_ss_A', A(:), 1e-10);
chkcoef('butter_ss_B', B(:), 1e-10);
chkcoef('butter_ss_C', C(:), 1e-10);
chkcoef('butter_ss_D', D(:), 1e-10);

% ---------------------------------------------------------------------------------------------
% Minimum order.
% ---------------------------------------------------------------------------------------------
ordcases = { ...
    {0.2, 0.35}, {0.5, 0.3}, {[0.3 0.6], [0.2 0.7]}, {[0.2 0.7], [0.3 0.6]}};
for c = 1:4
    wp = ordcases{c}{1};
    ws = ordcases{c}{2};
    [n, wn] = buttord(wp, ws, 1, 40);
    fprintf('CHK|buttord%d_n|%d|exact\n', c, n);
    chkcoef(sprintf('buttord%d_wn', c), wn, 1e-11);
    [n, wn] = cheb1ord(wp, ws, 1, 40);
    fprintf('CHK|cheb1ord%d_n|%d|exact\n', c, n);
    chkcoef(sprintf('cheb1ord%d_wn', c), wn, 1e-11);
    [n, wn] = cheb2ord(wp, ws, 1, 40);
    fprintf('CHK|cheb2ord%d_n|%d|exact\n', c, n);
    chkcoef(sprintf('cheb2ord%d_wn', c), wn, 1e-11);
    [n, wn] = ellipord(wp, ws, 1, 40);
    fprintf('CHK|ellipord%d_n|%d|exact\n', c, n);
    chkcoef(sprintf('ellipord%d_wn', c), wn, 1e-11);
end

[n, wn] = buttord(30, 50, 1, 40, 's');
fprintf('CHK|buttord_s_n|%d|exact\n', n);
chkcoef('buttord_s_wn', wn, 1e-11);
[n, wn] = cheb1ord(30, 50, 1, 40, 's');
fprintf('CHK|cheb1ord_s_n|%d|exact\n', n);
chkcoef('cheb1ord_s_wn', wn, 1e-11);
[n, wn] = cheb2ord([30 60], [20 70], 1, 40, 's');
fprintf('CHK|cheb2ord_s_n|%d|exact\n', n);
chkcoef('cheb2ord_s_wn', wn, 1e-11);
[n, wn] = ellipord(30, 50, 1, 40, 's');
fprintf('CHK|ellipord_s_n|%d|exact\n', n);
chkcoef('ellipord_s_wn', wn, 1e-11);

% ---------------------------------------------------------------------------------------------
% The band transforms and the two maps to the unit circle.
% ---------------------------------------------------------------------------------------------
pb = 1;
pa = [1 sqrt(2) 1];
[nn, dd] = lp2lp(pb, pa, 24);
chkcoef('lp2lp_b', nn, 1e-11);
chkcoef('lp2lp_a', dd, 1e-11);
[nn, dd] = lp2hp(pb, pa, 24);
chkcoef('lp2hp_b', nn, 1e-11);
chkcoef('lp2hp_a', dd, 1e-11);
[nn, dd] = lp2bp(pb, pa, 24, 10);
chkcoef('lp2bp_b', nn, 1e-10);
chkcoef('lp2bp_a', dd, 1e-10);
[nn, dd] = lp2bs(pb, pa, 24, 10);
chkcoef('lp2bs_b', nn, 1e-10);
chkcoef('lp2bs_a', dd, 1e-10);

[zd, pd, kd] = bilinear([], [-1+1i; -1-1i], 2, 10);
chkroots('bilinear_zpk_z', zd, 1e-12);
chkroots('bilinear_zpk_p', pd, 1e-12);
chknum('bilinear_zpk_k', kd, 1e-12);
[nn, dd] = bilinear(1, [1 2 2], 10);
chkcoef('bilinear_tf_b', nn, 1e-12);
chkcoef('bilinear_tf_a', dd, 1e-12);
[nn, dd] = bilinear(1, [1 2 2], 10, 1.5);
chkcoef('bilinear_warp_b', nn, 1e-12);
chkcoef('bilinear_warp_a', dd, 1e-12);

[nn, dd] = impinvar(1, [1 2 2], 10);
chkcoef('impinvar_b', nn, 1e-11);
chkcoef('impinvar_a', dd, 1e-11);
[nn, dd] = impinvar([1 1], [1 3 3 1], 5);
chkcoef('impinvar2_b', nn, 1e-10);
chkcoef('impinvar2_a', dd, 1e-10);

[hh, ww] = freqs(1, [1 0.4 1], 20);
chkcoef('freqs_w', ww, 1e-11);
chkcplx('freqs_h', hh, 1e-11);
hf = freqs([1 0], [1 0.4 1], [0.5 1 2 4]);
chkcplx('freqs_at', hf, 1e-12);

% ---------------------------------------------------------------------------------------------
% FIR design.
% ---------------------------------------------------------------------------------------------
chkcoef('fir1_low', fir1(20, 0.4), 1e-12);
chkcoef('fir1_high', fir1(20, 0.4, 'high'), 1e-12);
chkcoef('fir1_pass', fir1(24, [0.3 0.6]), 1e-12);
chkcoef('fir1_stop', fir1(24, [0.3 0.6], 'stop'), 1e-12);
chkcoef('fir1_dc1', fir1(30, [0.2 0.4 0.6 0.8], 'DC-1'), 1e-12);
chkcoef('fir1_noscale', fir1(20, 0.4, 'noscale'), 1e-12);
chkcoef('fir1_kaiser', fir1(20, 0.4, kaiser(21, 4.5)), 1e-12);

chkcoef('fir2_a', fir2(30, [0 0.6 0.6 1], [1 1 0 0]), 1e-11);
chkcoef('fir2_b', fir2(41, [0 0.3 0.5 0.7 1], [0 1 0.5 0.2 0]), 1e-11);

chkcoef('firls_a', firls(20, [0 0.3 0.5 1], [1 1 0 0]), 1e-12);
chkcoef('firls_b', firls(21, [0 0.3 0.5 1], [1 1 0 0]), 1e-12);
chkcoef('firls_w', firls(24, [0 0.3 0.4 0.6 0.7 1], [0 0 1 1 0 0], [1 2 3]), 1e-11);
chkcoef('firls_h', firls(30, [0.1 0.9], [1 1], 'h'), 1e-11);
chkcoef('firls_h2', firls(31, [0.1 0.9], [1 1], 'h'), 1e-11);
chkcoef('firls_d', firls(20, [0 0.9], [0 0.9*pi], 'd'), 1e-10);
chkcoef('firls_d2', firls(21, [0 0.9], [0 0.9*pi], 'd'), 1e-10);

[h, err] = firpm(20, [0 0.3 0.5 1], [1 1 0 0]);
chkcoef('firpm_a', h, 1e-11);
chknum('firpm_a_err', err, 1e-11);
[h, err] = firpm(21, [0 0.3 0.5 1], [1 1 0 0]);
chkcoef('firpm_b', h, 1e-11);
chknum('firpm_b_err', err, 1e-11);
chkcoef('firpm_w', firpm(30, [0 0.2 0.3 0.6 0.7 1], [0 0 1 1 0 0], [1 5 1]), 1e-11);
chkcoef('firpm_h', firpm(30, [0.1 0.9], [1 1], 'h'), 1e-11);
chkcoef('firpm_h2', firpm(31, [0.1 0.9], [1 1], 'h'), 1e-11);
chkcoef('firpm_d', firpm(20, [0 0.9], [0 0.9], 'd'), 1e-11);
chkcoef('firpm_g', firpm(50, [0 0.4 0.5 1], [1 1 0 0], {32}), 1e-11);
chkcoef('firpm_big', firpm(400, [0 0.3 0.32 1], [1 1 0 0]), 1e-9);
[~, ~, res] = firpm(20, [0 0.3 0.5 1], [1 1 0 0]);
fprintf('CHK|firpm_res_ngrid|%d|exact\n', numel(res.fgrid));
fprintf('CHK|firpm_res_nextr|%d|exact\n', numel(res.fextr));
chkvec('firpm_res_error', res.error, 1e-10);

[n, f, a, w] = firpmord([1000 1200], [1 0], [0.05 0.01], 8000);
fprintf('CHK|firpmord_n|%d|exact\n', n);
chkcoef('firpmord_f', f, 1e-12);
chkcoef('firpmord_a', a, 1e-12);
chkcoef('firpmord_w', w, 1e-12);
[n, f, a, w] = remezord([800 1000 2000 2400], [0 1 0], [0.01 0.05 0.01], 8000);
fprintf('CHK|remezord_n|%d|exact\n', n);
chkcoef('remezord_f', f, 1e-12);
chkcoef('remezord_a', a, 1e-12);
chkcoef('remezord_w', w, 1e-12);
chkcoef('remez_h', remez(20, [0 0.3 0.5 1], [1 1 0 0]), 1e-11);

[n, wn, beta, ftype] = kaiserord([1000 1200], [1 0], [0.05 0.01], 8000);
fprintf('CHK|kaiserord_n|%d|exact\n', n);
chkcoef('kaiserord_wn', wn, 1e-12);
chknum('kaiserord_beta', beta, 1e-12);
fprintf('CHK|kaiserord_type|%s|exact\n', ftype);
[n, wn, beta, ftype] = kaiserord([800 1000 2000 2400], [0 1 0], [0.01 0.05 0.01], 8000);
fprintf('CHK|kaiserord2_n|%d|exact\n', n);
chkcoef('kaiserord2_wn', wn, 1e-12);
chknum('kaiserord2_beta', beta, 1e-12);
fprintf('CHK|kaiserord2_type|%s|exact\n', ftype);

chkcoef('fircls_a', fircls(50, [0 0.4 0.8 1], [0 1 0], [0.02 1.02 0.01], [-0.02 0.98 -0.01]), 1e-9);
chkcoef('fircls_b', fircls(20, [0 0.5 1], [1 0], [1.02 0.01], [0.98 -0.01]), 1e-9);
chkcoef('fircls1_a', fircls1(55, 0.3, 0.02, 0.008), 1e-9);
chkcoef('fircls1_h', fircls1(54, 0.3, 0.02, 0.008, 'high'), 1e-9);
chkcoef('fircls1_wt', fircls1(55, 0.3, 0.02, 0.008, 0.25), 1e-9);

[b, a, b1, b2] = maxflat(10, 2, 0.2);
chkcoef('maxflat_b', b, 1e-10);
chkcoef('maxflat_a', a, 1e-10);
chkcoef('maxflat_b1', b1, 1e-10);
chkcoef('maxflat_b2', b2, 1e-10);
[b, a] = maxflat(6, 0, 0.4);
chkcoef('maxflat_fir_b', b, 1e-10);
chkcoef('maxflat_fir_a', a, 1e-10);
[b, a] = maxflat(8, 'sym', 0.3);
chkcoef('maxflat_sym_b', b, 1e-10);
chkcoef('maxflat_sym_a', a, 1e-10);

chkcoef('rcos_n', rcosdesign(0.25, 6, 4, 'normal'), 1e-12);
chkcoef('rcos_s', rcosdesign(0.25, 6, 4, 'sqrt'), 1e-12);
chkcoef('gaussdesign', gaussdesign(0.3, 4, 3), 1e-12);
chkcoef('gaussfir', gaussfir(0.3, 3, 2), 1e-12);
chkcoef('firgauss', firgauss(4, 5), 1e-12);
chkcoef('firrcos_n', firrcos(20, 0.25, 0.25, 2, 'rolloff', 'normal'), 1e-12);
chkcoef('firrcos_s', firrcos(20, 0.25, 0.25, 2, 'rolloff', 'sqrt'), 1e-12);
chkcoef('intfilt_b', intfilt(4, 2, 1), 1e-11);
chkcoef('intfilt_b2', intfilt(3, 3, 0.8), 1e-11);
chkcoef('intfilt_l', intfilt(4, 3, 'l'), 1e-11);

[yb, ya] = yulewalk(8, [0 0.4 0.4 0.6 0.6 1], [1 1 0 0 1 1]);
chkcoef('yulewalk_b', yb, 1e-9);
chkcoef('yulewalk_a', ya, 1e-9);

% ---------------------------------------------------------------------------------------------
% Analysis.
% ---------------------------------------------------------------------------------------------
fir = fir1(20, 0.4);
[bb, ba] = butter(6, 0.3);
[eb, ea] = ellip(5, 1, 40, [0.25 0.6]);
names = {'fir', 'butter', 'ellip', 'tiny', 'delay'};
bs = {fir, bb, eb, [1 0.5], [0 0 1]};
as = {1, ba, ea, [1 -0.6], 1};

for c = 1:numel(names)
    nm = names{c};
    b = bs{c};
    a = as{c};

    [h, w] = freqz(b, a, 64);
    chkcplx(sprintf('freqz_%s', nm), h, 1e-11);
    chkcoef(sprintf('freqzw_%s', nm), w, 1e-12);
    chkcplx(sprintf('freqzwhole_%s', nm), freqz(b, a, 32, 'whole'), 1e-11);
    chkcplx(sprintf('freqzat_%s', nm), freqz(b, a, [0.1 0.5 1.0 2.0 3.0]), 1e-11);
    chkcplx(sprintf('freqzfs_%s', nm), freqz(b, a, 16, 1000), 1e-11);

    n = impzlength(b, a);
    fprintf('CHK|impzlength_%s|%d|exact\n', nm, n);
    chkvec(sprintf('impz_%s', nm), impz(b, a, n), 1e-10);
    chkvec(sprintf('stepz_%s', nm), stepz(b, a, n), 1e-10);

    % Both grids land a point exactly where a design's zeros sit on the circle -- DC for a
    % bandpass, Nyquist for a lowpass -- and there the delay is nought over nought. One engine
    % reaches an infinity there and another a large finite number, depending on whether its
    % linear algebra put the zero exactly on the circle or a bit off it; neither is more right.
    % Those two points are skipped and the rest of both grids is pinned.
    [gd, gw] = grpdelay(b, a, 32);
    chkcoef(sprintf('grpdelay_%s', nm), gd(2:end), 1e-8);
    chkcoef(sprintf('grpdelayw_%s', nm), gw, 1e-12);
    gdw = grpdelay(b, a, 16, 'whole');
    chkcoef(sprintf('grpdelaywhole_%s', nm), gdw([2:8 10:16]), 1e-8);

    chkcoef(sprintf('phasez_%s', nm), phasez(b, a, 32), 1e-8);
    pdv = phasedelay(b, a, 32);
    chkcoef(sprintf('phasedelay_%s', nm), pdv(2:end), 1e-8);
    [hz, zw, zphi] = zerophase(b, a, 32);
    chkcoef(sprintf('zerophase_%s', nm), hz, 1e-9);
    chkcoef(sprintf('zerophasew_%s', nm), zw, 1e-12);
    chkcoef(sprintf('zerophasep_%s', nm), zphi, 1e-8);

    fprintf('CHK|filtord_%s|%d|exact\n', nm, filtord(b, a));
    chknum(sprintf('filternorm2_%s', nm), filternorm(b, a, 2), 1e-9);
    chknum(sprintf('filterorminf_%s', nm), filternorm(b, a, inf), 1e-10);
    fprintf('CHK|isstable_%s|%d|exact\n', nm, isstable(b, a));
    fprintf('CHK|isminphase_%s|%d|exact\n', nm, isminphase(b, a));
    fprintf('CHK|ismaxphase_%s|%d|exact\n', nm, ismaxphase(b, a));
    fprintf('CHK|isallpass_%s|%d|exact\n', nm, isallpass(b, a));
    fprintf('CHK|islinphase_%s|%d|exact\n', nm, islinphase(b, a));
end

fprintf('CHK|firtype_1|%d|exact\n', firtype(fir1(20, 0.4)));
fprintf('CHK|firtype_2|%d|exact\n', firtype(firls(21, [0 0.3 0.5 1], [1 1 0 0])));
fprintf('CHK|firtype_3|%d|exact\n', firtype(firls(30, [0.1 0.9], [1 1], 'h')));
fprintf('CHK|firtype_4|%d|exact\n', firtype(firls(31, [0.1 0.9], [1 1], 'h')));

fprintf('CHK|isallpass_true|%d|exact\n', isallpass([0.5 1], [1 0.5]));
fprintf('CHK|isstable_false|%d|exact\n', isstable(1, [1 -2]));
fprintf('CHK|isminphase_false|%d|exact\n', isminphase([1 2], 1));
fprintf('CHK|ismaxphase_true|%d|exact\n', ismaxphase([1 2], 1));
fprintf('CHK|impzlength_tol|%d|exact\n', impzlength([1 0.5], [1 -0.9], 1e-8));
chkvec('impz_range', impz([1 0.5], [1 -0.6], 0:5), 1e-11);
chkvec('stepz_range', stepz([1 0.5], [1 -0.6], 0:5), 1e-11);

% ---------------------------------------------------------------------------------------------
% Helpers.
% ---------------------------------------------------------------------------------------------

function chkcoef(name, v, tol)
%CHKCOEF Pins a coefficient vector elementwise, RELATIVE TO THE VECTOR rather than to each entry.
%
%   A bandpass design's numerator alternates between numbers of order 1e11 and numbers algebra says
%   are zero. The second kind arrive as a few units in the last place of the first, which is dust,
%   and dust has no relative accuracy at all. So the threshold below is a fraction of the largest
%   coefficient present, and anything under it is pinned absolutely against that same scale.
    u = v(:);
    finite = u(isfinite(u));
    if isempty(finite)
        scale = 1;
    else
        scale = max(abs(finite));
    end
    if scale == 0
        scale = 1;
    end
    fprintf('CHK|f_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        % An infinity is a statement rather than a number -- a group delay where a zero sits on the
        % unit circle, say -- so it is pinned as itself and takes no tolerance. Everything else is
        % pinned to a fraction OF THE VECTOR, not of itself: a stopband response of 1e-9 in a
        % response whose passband is 1 has no relative accuracy and never did.
        if ~isfinite(u(i))
            fprintf('CHK|f_%s_%d|%.17g|exact\n', name, i, u(i));
        else
            fprintf('CHK|f_%s_%d|%.17g|abs=%g\n', name, i, u(i), scale * tol);
        end
    end
end

function chkcplx(name, v, tol)
%CHKCPLX The same for a complex list, real and imaginary parts pinned apart.
    u = v(:);
    finite = u(isfinite(u));
    scale = max([abs(real(finite)); abs(imag(finite)); 0]);
    if scale == 0
        scale = 1;
    end
    fprintf('CHK|x_%s_n|%d|exact\n', name, numel(u));
    for i = 1:numel(u)
        pinone(sprintf('x_%s_%dr', name, i), real(u(i)), scale, tol);
        pinone(sprintf('x_%s_%di', name, i), imag(u(i)), scale, tol);
    end
end

function pinone(name, value, scale, tol)
%PINONE One part of one number, against the scale of the list it came from.
    if ~isfinite(value)
        fprintf('CHK|%s|%.17g|exact\n', name, value);
    else
        fprintf('CHK|%s|%.17g|abs=%g\n', name, value, scale * tol);
    end
end

function chknum(name, value, tol)
%CHKNUM One number on its own, relatively unless it is algebraically zero.
    if abs(value) < 1e-9
        fprintf('CHK|%s|%.17g|abs=1e-9\n', name, value);
    else
        fprintf('CHK|%s|%.17g|rel=%g\n', name, value, tol);
    end
end

function chkroots(name, v, tol)
%CHKROOTS Pins a list of roots by its size and by order-free digests.
%
%   Order-free on purpose. The transmission-zero route MATLAB's own cheby2, ellip and besself take
%   answers the same multiset in a different order from the pencil this build reads it off, and
%   that says nothing about either of them (ADR 0138).
    u = v(:);
    fprintf('CHK|k_%s_n|%d|exact\n', name, numel(u));
    fprintf('CHK|k_%s_sr|%.17g|rel=%g\n', name, sum(real(u)), tol);
    fprintf('CHK|k_%s_si|%.17g|abs=%g\n', name, sum(imag(u)), tol);
    fprintf('CHK|k_%s_qr|%.17g|rel=%g\n', name, sum(real(u).^2 - imag(u).^2), tol);
    fprintf('CHK|k_%s_m|%.17g|rel=%g\n', name, sum(abs(u)), tol);
end

function chkvec(name, v, tol)
%CHKVEC Pins a response by four digests, which between them see a change anywhere in it.
    u = v(:);
    n = numel(u);
    mass = sum(abs(u));
    fprintf('CHK|v_%s_n|%d|exact\n', name, n);
    fprintf('CHK|v_%s_mass|%.17g|rel=%g\n', name, mass, tol);
    fprintf('CHK|v_%s_var|%.17g|rel=%g\n', name, sum(abs(diff(u))), tol);
    if mass > 0
        fprintf('CHK|v_%s_sign|%.17g|abs=%g\n', name, sum(u) / mass, tol);
        fprintf('CHK|v_%s_place|%.17g|abs=%g\n', name, ...
            sum(u .* (1:n)') / sum(abs(u) .* (1:n)'), max(tol, 1e-7));
    else
        fprintf('CHK|v_%s_sign|%.17g|exact\n', name, 0);
        fprintf('CHK|v_%s_place|%.17g|exact\n', name, 0);
    end
    if mass > 0
        fprintf('CHK|v_%s_last|%.17g|abs=%g\n', name, u(n), mass * tol);
    else
        fprintf('CHK|v_%s_last|%.17g|exact\n', name, u(n));
    end
end
