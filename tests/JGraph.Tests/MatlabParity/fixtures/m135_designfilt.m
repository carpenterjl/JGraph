% m135_designfilt.m -- M135's six names: designfilt, the digitalFilter value it returns, and the
% four one-line filters that design one and use it in the same breath.
%
% The file pins three things.
%
% Every combination of response, parameter set and design method that designfilt admits, by the
% coefficients it produces. An IIR design's coefficients are a second-order section matrix, which is
% not a factorisation of a transfer function but a closed form in its own right: the exact 2 in a
% lowpass numerator and the gain each section carries come out of the formula rather than out of a
% root finder. The one thing that does come out of a root finder is which of a fourth-order block's
% two sections is listed first, and which zero pair each is given -- so the sections are sorted into
% a canonical order, and their numerators and denominators sorted separately, before being pinned.
% Everything else about them is pinned elementwise.
%
% The properties a digitalFilter answers to, and the methods that take one -- filtord, isstable,
% firtype and the rest -- since those are the whole of what a filter value is for.
%
% The four verbs on a fixed three-tone signal, with the filter each one designed. The signal is
% built from sines rather than from rand, so the file says the same thing on every run.
%
% The coefficient rule is M134's: a design's coefficients are pinned RELATIVE TO THE VECTOR THEY
% LIVE IN rather than to themselves, because a stopband tap of 1e-18 in a filter whose passband is
% one has no relative accuracy and never did.

% ---------------------------------------------------------------------------------------------
% Every FIR parameter set, in every band, by every method it admits.
% ---------------------------------------------------------------------------------------------
d = designfilt('lowpassfir', 'FilterOrder', 40, 'CutoffFrequency', 0.4);
chkfilt('lp_win', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 41, 'CutoffFrequency', 0.4);
chkfilt('lp_win_odd', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 40, 'CutoffFrequency', 0.4, 'ScalePassband', false);
chkfilt('lp_win_noscale', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 30, 'HalfPowerFrequency', 0.4);
chkfilt('lp_maxflat', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 40, 'PassbandFrequency', 0.3, 'StopbandFrequency', 0.4);
chkfilt('lp_eqrip', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 40, 'PassbandFrequency', 0.3, ...
    'StopbandFrequency', 0.4, 'DesignMethod', 'ls');
chkfilt('lp_ls', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 40, 'PassbandFrequency', 0.3, ...
    'StopbandFrequency', 0.4, 'PassbandWeight', 1, 'StopbandWeight', 5);
chkfilt('lp_eqrip_w', d, 1e-10);
d = designfilt('lowpassfir', 'FilterOrder', 50, 'CutoffFrequency', 0.4, ...
    'PassbandRipple', 1, 'StopbandAttenuation', 40);
chkfilt('lp_cls', d, 1e-9);
d = designfilt('lowpassfir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'kaiserwin');
chkfilt('lp_kaiser', d, 1e-10);
d = designfilt('lowpassfir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'kaiserwin', ...
    'MinOrder', 'even');
chkfilt('lp_kaiser_even', d, 1e-10);
d = designfilt('lowpassfir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60);
chkfilt('lp_eqrip_min', d, 1e-10);

d = designfilt('highpassfir', 'FilterOrder', 40, 'CutoffFrequency', 0.4);
chkfilt('hp_win', d, 1e-10);
d = designfilt('highpassfir', 'FilterOrder', 40, 'StopbandFrequency', 0.3, 'PassbandFrequency', 0.4);
chkfilt('hp_eqrip', d, 1e-10);
d = designfilt('highpassfir', 'FilterOrder', 41, 'StopbandFrequency', 0.3, 'PassbandFrequency', 0.4);
chkfilt('hp_eqrip_odd', d, 1e-10);
d = designfilt('highpassfir', 'FilterOrder', 40, 'StopbandFrequency', 0.3, ...
    'PassbandFrequency', 0.4, 'DesignMethod', 'ls');
chkfilt('hp_ls', d, 1e-10);
d = designfilt('highpassfir', 'FilterOrder', 50, 'CutoffFrequency', 0.4, ...
    'StopbandAttenuation', 40, 'PassbandRipple', 1);
chkfilt('hp_cls', d, 1e-9);
d = designfilt('highpassfir', 'StopbandFrequency', 0.25, 'PassbandFrequency', 0.35, ...
    'StopbandAttenuation', 60, 'PassbandRipple', 0.5, 'DesignMethod', 'kaiserwin');
chkfilt('hp_kaiser', d, 1e-10);
d = designfilt('highpassfir', 'StopbandFrequency', 0.25, 'PassbandFrequency', 0.35, ...
    'StopbandAttenuation', 60, 'PassbandRipple', 0.5);
chkfilt('hp_eqrip_min', d, 1e-10);

d = designfilt('bandpassfir', 'FilterOrder', 40, 'CutoffFrequency1', 0.3, 'CutoffFrequency2', 0.6);
chkfilt('bp_win', d, 1e-10);
d = designfilt('bandpassfir', 'FilterOrder', 44, 'StopbandFrequency1', 0.25, ...
    'PassbandFrequency1', 0.3, 'PassbandFrequency2', 0.6, 'StopbandFrequency2', 0.65);
chkfilt('bp_eqrip', d, 1e-10);
d = designfilt('bandpassfir', 'FilterOrder', 44, 'StopbandFrequency1', 0.25, ...
    'PassbandFrequency1', 0.3, 'PassbandFrequency2', 0.6, 'StopbandFrequency2', 0.65, ...
    'DesignMethod', 'ls');
chkfilt('bp_ls', d, 1e-10);
d = designfilt('bandpassfir', 'FilterOrder', 50, 'CutoffFrequency1', 0.3, ...
    'CutoffFrequency2', 0.6, 'StopbandAttenuation1', 40, 'PassbandRipple', 1, ...
    'StopbandAttenuation2', 40);
chkfilt('bp_cls', d, 1e-9);
d = designfilt('bandpassfir', 'StopbandFrequency1', 0.25, 'PassbandFrequency1', 0.35, ...
    'PassbandFrequency2', 0.55, 'StopbandFrequency2', 0.65, 'StopbandAttenuation1', 60, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation2', 60, 'DesignMethod', 'kaiserwin');
chkfilt('bp_kaiser', d, 1e-10);
d = designfilt('bandpassfir', 'StopbandFrequency1', 0.25, 'PassbandFrequency1', 0.35, ...
    'PassbandFrequency2', 0.55, 'StopbandFrequency2', 0.65, 'StopbandAttenuation1', 60, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation2', 60);
chkfilt('bp_eqrip_min', d, 1e-10);

d = designfilt('bandstopfir', 'FilterOrder', 40, 'CutoffFrequency1', 0.3, 'CutoffFrequency2', 0.6);
chkfilt('bs_win', d, 1e-10);
d = designfilt('bandstopfir', 'FilterOrder', 44, 'PassbandFrequency1', 0.25, ...
    'StopbandFrequency1', 0.3, 'StopbandFrequency2', 0.6, 'PassbandFrequency2', 0.65);
chkfilt('bs_eqrip', d, 1e-10);
d = designfilt('bandstopfir', 'FilterOrder', 44, 'PassbandFrequency1', 0.25, ...
    'StopbandFrequency1', 0.3, 'StopbandFrequency2', 0.6, 'PassbandFrequency2', 0.65, ...
    'DesignMethod', 'ls');
chkfilt('bs_ls', d, 1e-10);
d = designfilt('bandstopfir', 'FilterOrder', 50, 'CutoffFrequency1', 0.3, ...
    'CutoffFrequency2', 0.6, 'PassbandRipple1', 1, 'StopbandAttenuation', 40, ...
    'PassbandRipple2', 1);
chkfilt('bs_cls', d, 1e-9);
d = designfilt('bandstopfir', 'PassbandFrequency1', 0.25, 'StopbandFrequency1', 0.35, ...
    'StopbandFrequency2', 0.55, 'PassbandFrequency2', 0.65, 'PassbandRipple1', 0.5, ...
    'StopbandAttenuation', 60, 'PassbandRipple2', 0.5, 'DesignMethod', 'kaiserwin');
chkfilt('bs_kaiser', d, 1e-10);
d = designfilt('bandstopfir', 'PassbandFrequency1', 0.25, 'StopbandFrequency1', 0.35, ...
    'StopbandFrequency2', 0.55, 'PassbandFrequency2', 0.65, 'PassbandRipple1', 0.5, ...
    'StopbandAttenuation', 60, 'PassbandRipple2', 0.5);
chkfilt('bs_eqrip_min', d, 1e-10);

d = designfilt('differentiatorfir', 'FilterOrder', 21);
chkfilt('diff_eqrip', d, 1e-10);
d = designfilt('differentiatorfir', 'FilterOrder', 21, 'DesignMethod', 'ls');
chkfilt('diff_ls', d, 1e-10);
d = designfilt('differentiatorfir', 'FilterOrder', 20, 'PassbandFrequency', 0.5, ...
    'StopbandFrequency', 0.6);
chkfilt('diff_band', d, 1e-10);
d = designfilt('differentiatorfir', 'FilterOrder', 20, 'PassbandFrequency', 0.5, ...
    'StopbandFrequency', 0.6, 'DesignMethod', 'ls');
chkfilt('diff_band_ls', d, 1e-10);

d = designfilt('hilbertfir', 'FilterOrder', 30, 'TransitionWidth', 0.1);
chkfilt('hilb_eqrip', d, 1e-10);
d = designfilt('hilbertfir', 'FilterOrder', 30, 'TransitionWidth', 0.1, 'DesignMethod', 'ls');
chkfilt('hilb_ls', d, 1e-10);

% ---------------------------------------------------------------------------------------------
% Every IIR parameter set, in every band, by every method it admits.
% ---------------------------------------------------------------------------------------------
d = designfilt('lowpassiir', 'FilterOrder', 7, 'HalfPowerFrequency', 0.3);
chkfilt('ilp_butter', d, 1e-10);
d = designfilt('lowpassiir', 'FilterOrder', 8, 'HalfPowerFrequency', 0.35);
chkfilt('ilp_butter8', d, 1e-10);
d = designfilt('lowpassiir', 'FilterOrder', 8, 'PassbandFrequency', 0.3, 'PassbandRipple', 0.5);
chkfilt('ilp_cheby1', d, 1e-10);
d = designfilt('lowpassiir', 'FilterOrder', 5, 'PassbandFrequency', 0.4, 'PassbandRipple', 1);
chkfilt('ilp_cheby1_odd', d, 1e-10);
d = designfilt('lowpassiir', 'FilterOrder', 7, 'StopbandFrequency', 0.4, 'StopbandAttenuation', 40);
chkfilt('ilp_cheby2', d, 1e-10);
d = designfilt('lowpassiir', 'FilterOrder', 6, 'PassbandFrequency', 0.3, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 40);
chkfilt('ilp_ellip', d, 1e-10);
d = designfilt('lowpassiir', 'FilterOrder', 5, 'PassbandFrequency', 0.35, ...
    'PassbandRipple', 0.2, 'StopbandAttenuation', 60);
chkfilt('ilp_ellip_odd', d, 1e-10);

d = designfilt('highpassiir', 'FilterOrder', 7, 'HalfPowerFrequency', 0.3);
chkfilt('ihp_butter', d, 1e-10);
d = designfilt('highpassiir', 'FilterOrder', 6, 'PassbandFrequency', 0.4, 'PassbandRipple', 0.5);
chkfilt('ihp_cheby1', d, 1e-10);
d = designfilt('highpassiir', 'FilterOrder', 5, 'StopbandFrequency', 0.3, 'StopbandAttenuation', 40);
chkfilt('ihp_cheby2', d, 1e-10);
d = designfilt('highpassiir', 'FilterOrder', 6, 'PassbandFrequency', 0.4, ...
    'StopbandAttenuation', 40, 'PassbandRipple', 0.5);
chkfilt('ihp_ellip', d, 1e-10);

d = designfilt('bandpassiir', 'FilterOrder', 8, 'HalfPowerFrequency1', 0.3, ...
    'HalfPowerFrequency2', 0.6);
chkfilt('ibp_butter', d, 1e-10);
d = designfilt('bandpassiir', 'FilterOrder', 6, 'HalfPowerFrequency1', 0.25, ...
    'HalfPowerFrequency2', 0.7);
chkfilt('ibp_butter6', d, 1e-10);
d = designfilt('bandpassiir', 'FilterOrder', 8, 'PassbandFrequency1', 0.3, ...
    'PassbandFrequency2', 0.6, 'PassbandRipple', 0.5);
chkfilt('ibp_cheby1', d, 1e-10);
d = designfilt('bandpassiir', 'FilterOrder', 8, 'StopbandFrequency1', 0.3, ...
    'StopbandFrequency2', 0.6, 'StopbandAttenuation', 40);
chkfilt('ibp_cheby2', d, 1e-10);
d = designfilt('bandpassiir', 'FilterOrder', 8, 'PassbandFrequency1', 0.3, ...
    'PassbandFrequency2', 0.6, 'StopbandAttenuation1', 40, 'PassbandRipple', 0.5, ...
    'StopbandAttenuation2', 40);
chkfilt('ibp_ellip', d, 1e-10);

d = designfilt('bandstopiir', 'FilterOrder', 8, 'HalfPowerFrequency1', 0.3, ...
    'HalfPowerFrequency2', 0.6);
chkfilt('ibs_butter', d, 1e-10);
d = designfilt('bandstopiir', 'FilterOrder', 6, 'HalfPowerFrequency1', 0.25, ...
    'HalfPowerFrequency2', 0.7);
chkfilt('ibs_butter6', d, 1e-10);
d = designfilt('bandstopiir', 'FilterOrder', 8, 'PassbandFrequency1', 0.3, ...
    'PassbandFrequency2', 0.6, 'PassbandRipple', 0.5);
chkfilt('ibs_cheby1', d, 1e-10);
d = designfilt('bandstopiir', 'FilterOrder', 8, 'StopbandFrequency1', 0.3, ...
    'StopbandFrequency2', 0.6, 'StopbandAttenuation', 40);
chkfilt('ibs_cheby2', d, 1e-10);
d = designfilt('bandstopiir', 'FilterOrder', 8, 'PassbandFrequency1', 0.3, ...
    'PassbandFrequency2', 0.6, 'PassbandRipple', 0.5, 'StopbandAttenuation', 40);
chkfilt('ibs_ellip', d, 1e-10);

% ---------------------------------------------------------------------------------------------
% The minimum-order IIR designs, and every band a MatchExactly can be asked to meet.
% ---------------------------------------------------------------------------------------------
methods = {'butter', 'cheby1', 'cheby2', 'ellip'};
matches = {'stopband', 'passband', 'stopband', 'both'};
for m = 1:4
    d = designfilt('lowpassiir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
        'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', methods{m}, ...
        'MatchExactly', matches{m});
    chkfilt(sprintf('mlp_%s', methods{m}), d, 1e-10);

    d = designfilt('highpassiir', 'StopbandFrequency', 0.3, 'PassbandFrequency', 0.4, ...
        'StopbandAttenuation', 60, 'PassbandRipple', 0.5, 'DesignMethod', methods{m}, ...
        'MatchExactly', matches{m});
    chkfilt(sprintf('mhp_%s', methods{m}), d, 1e-10);

    d = designfilt('bandpassiir', 'StopbandFrequency1', 0.25, 'PassbandFrequency1', 0.35, ...
        'PassbandFrequency2', 0.55, 'StopbandFrequency2', 0.65, 'StopbandAttenuation1', 60, ...
        'PassbandRipple', 0.5, 'StopbandAttenuation2', 60, 'DesignMethod', methods{m}, ...
        'MatchExactly', matches{m});
    chkfilt(sprintf('mbp_%s', methods{m}), d, 1e-10);

    d = designfilt('bandstopiir', 'PassbandFrequency1', 0.25, 'StopbandFrequency1', 0.35, ...
        'StopbandFrequency2', 0.55, 'PassbandFrequency2', 0.65, 'PassbandRipple1', 0.5, ...
        'StopbandAttenuation', 60, 'PassbandRipple2', 0.5, 'DesignMethod', methods{m}, ...
        'MatchExactly', matches{m});
    chkfilt(sprintf('mbs_%s', methods{m}), d, 1e-10);
end

d = designfilt('lowpassiir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'butter', ...
    'MatchExactly', 'passband');
chkfilt('mlp_butter_pass', d, 1e-10);
d = designfilt('lowpassiir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'cheby1', ...
    'MatchExactly', 'stopband');
chkfilt('mlp_cheby1_stop', d, 1e-10);
d = designfilt('lowpassiir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'cheby2', ...
    'MatchExactly', 'passband');
chkfilt('mlp_cheby2_pass', d, 1e-10);
d = designfilt('lowpassiir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'ellip', ...
    'MatchExactly', 'passband');
chkfilt('mlp_ellip_pass', d, 1e-10);
d = designfilt('lowpassiir', 'PassbandFrequency', 0.25, 'StopbandFrequency', 0.35, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 60, 'DesignMethod', 'ellip', ...
    'MatchExactly', 'stopband');
chkfilt('mlp_ellip_stop', d, 1e-10);

% ---------------------------------------------------------------------------------------------
% A sample rate, which normalises the frequencies and is remembered as given.
% ---------------------------------------------------------------------------------------------
d = designfilt('lowpassfir', 'FilterOrder', 30, 'CutoffFrequency', 300, 'SampleRate', 1000);
chkfilt('fs_lp_win', d, 1e-10);
fprintf('CHK|fs_lp_rate|%.17g|exact\n', d.SampleRate);
fprintf('CHK|fs_lp_cut|%.17g|exact\n', d.CutoffFrequency);
d = designfilt('lowpassiir', 'FilterOrder', 6, 'PassbandFrequency', 300, ...
    'PassbandRipple', 0.5, 'StopbandAttenuation', 50, 'SampleRate', 1000);
chkfilt('fs_lp_ellip', d, 1e-10);

% ---------------------------------------------------------------------------------------------
% What a digitalFilter is: its class, its properties, and the analysis names that take one.
% ---------------------------------------------------------------------------------------------
d = designfilt('lowpassfir', 'FilterOrder', 20, 'CutoffFrequency', 0.4);
fprintf('CHK|df_class|%s|exact\n', class(d));
fprintf('CHK|df_resp|%s|exact\n', d.FrequencyResponse);
fprintf('CHK|df_imp|%s|exact\n', d.ImpulseResponse);
fprintf('CHK|df_method|%s|exact\n', d.DesignMethod);
fprintf('CHK|df_isfir|%d|exact\n', isfir(d));
fprintf('CHK|df_isdouble|%d|exact\n', isdouble(d));
fprintf('CHK|df_issingle|%d|exact\n', issingle(d));
fprintf('CHK|df_ord|%d|exact\n', filtord(d));
fprintf('CHK|df_impzlen|%d|exact\n', impzlength(d));
fprintf('CHK|df_type|%d|exact\n', firtype(d));
fprintf('CHK|df_stable|%d|exact\n', isstable(d));
fprintf('CHK|df_lin|%d|exact\n', islinphase(d));
fprintf('CHK|df_min|%d|exact\n', isminphase(d));
fprintf('CHK|df_max|%d|exact\n', ismaxphase(d));
fprintf('CHK|df_all|%d|exact\n', isallpass(d));
chkcplx('df_freqz', freqz(d, 12), 1e-10);
chkcoef('df_impz', impz(d, 12), 1e-10);
chkcoef('df_stepz', stepz(d, 12), 1e-10);
chkcoef('df_grpdelay', grpdelay(d, 12), 1e-9);
chkcoef('df_phasez', phasez(d, 12), 1e-9);
chkcoef('df_zerophase', zerophase(d, 12), 1e-9);
[bb, aa] = tf(d);
chkcoef('df_tf_b', bb, 1e-10);
chkcoef('df_tf_a', aa, 1e-10);
chkcoef('df_filter', filter(d, (1:24)'), 1e-10);
chkcoef('df_fftfilt', fftfilt(d, (1:24)'), 1e-10);

di = designfilt('lowpassiir', 'FilterOrder', 8, 'PassbandFrequency', 0.3, 'PassbandRipple', 0.5);
fprintf('CHK|dfi_imp|%s|exact\n', di.ImpulseResponse);
fprintf('CHK|dfi_isfir|%d|exact\n', isfir(di));
fprintf('CHK|dfi_ord|%d|exact\n', filtord(di));
fprintf('CHK|dfi_stable|%d|exact\n', isstable(di));
fprintf('CHK|dfi_lin|%d|exact\n', islinphase(di));
fprintf('CHK|dfi_min|%d|exact\n', isminphase(di));
fprintf('CHK|dfi_rows|%d|exact\n', size(di.Coefficients, 1));
fprintf('CHK|dfi_cols|%d|exact\n', size(di.Coefficients, 2));
chkcoef('dfi_num', di.Numerator(:), 1e-10);
chkcoef('dfi_den', di.Denominator(:), 1e-10);
[bb, aa] = tf(di);
chkcoef('dfi_tf_b', bb, 1e-9);
chkcoef('dfi_tf_a', aa, 1e-9);
[zz, pp, kk] = zpk(di);
% Only the count for the zeros. All eight sit on top of one another at -1, and a root of
% multiplicity eight is known to the eighth root of eps -- MATLAB's own come back a hundredth of
% the way off the unit circle, in both directions. The poles are simple and are pinned.
fprintf('CHK|dfi_zpk_nz|%d|exact\n', numel(zz));
chkroots('dfi_zpk_p', pp, 1e-8);
chknum('dfi_zpk_k', kk, 1e-8);
chkcplx('dfi_freqz', freqz(di, 12), 1e-9);
chkcoef('dfi_filter', filter(di, (1:24)'), 1e-10);

% ---------------------------------------------------------------------------------------------
% The four one-line filters, on a three-tone signal with no randomness in it.
% ---------------------------------------------------------------------------------------------
n = (0:599)';
x = sin(2*pi*0.03*n) + 0.7*sin(2*pi*0.17*n) + 0.4*sin(2*pi*0.41*n);

[y, dv] = lowpass(x, 0.2);
chkverb('v_lowpass', y, dv, 1e-9);
[y, dv] = lowpass(x, 0.2, 'Steepness', 0.95);
chkverb('v_lowpass_steep', y, dv, 1e-9);
[y, dv] = lowpass(x, 0.2, 'StopbandAttenuation', 80);
chkverb('v_lowpass_att', y, dv, 1e-9);
[y, dv] = lowpass(x, 200, 1000);
chkverb('v_lowpass_fs', y, dv, 1e-9);
[y, dv] = highpass(x, 0.3);
chkverb('v_highpass', y, dv, 1e-9);
[y, dv] = bandpass(x, [0.1 0.3]);
chkverb('v_bandpass', y, dv, 1e-9);
[y, dv] = bandstop(x, [0.1 0.3]);
chkverb('v_bandstop', y, dv, 1e-9);

% A short signal is filtered by an IIR filter instead, and by one whose order the signal can carry.
short = x(1:60);
[y, dv] = lowpass(short, 0.2);
chkverb('v_lowpass_short', y, dv, 1e-9);
[y, dv] = bandpass(short, [0.1 0.3]);
chkverb('v_bandpass_short', y, dv, 1e-9);

% A matrix is filtered column by column.
[y, dv] = lowpass([x, x(end:-1:1)], 0.2);
fprintf('CHK|v_matrix_rows|%d|exact\n', size(y, 1));
fprintf('CHK|v_matrix_cols|%d|exact\n', size(y, 2));
chkcoef('v_matrix_col2', y(1:20, 2), 1e-9);
fprintf('CHK|v_matrix_imp|%s|exact\n', dv.ImpulseResponse);

% An explicitly requested impulse response overrides the length rule.
[y, dv] = lowpass(x, 0.2, 'ImpulseResponse', 'iir');
chkverb('v_lowpass_iir', y, dv, 1e-9);
[y, dv] = lowpass(x, 0.2, 'ImpulseResponse', 'fir');
chkverb('v_lowpass_fir', y, dv, 1e-9);

% ---------------------------------------------------------------------------------------------
% The specifications designfilt refuses, which are as much of its behaviour as the ones it takes.
% ---------------------------------------------------------------------------------------------
chkerr('e_response', @() designfilt('nosuchresponse', 'FilterOrder', 20));
chkerr('e_toofew', @() designfilt('lowpassfir', 'FilterOrder', 20));
chkerr('e_method', @() designfilt('lowpassiir', 'FilterOrder', 8, ...
    'PassbandFrequency', 0.3, 'PassbandRipple', 0.5, 'DesignMethod', 'butter'));
chkerr('e_repeat', @() designfilt('lowpassfir', 'FilterOrder', 20, ...
    'CutoffFrequency', 0.3, 'CutoffFrequency', 0.4));
chkerr('e_odd', @() designfilt('differentiatorfir', 'FilterOrder', 20));
chkerr('e_even', @() designfilt('differentiatorfir', 'FilterOrder', 21, ...
    'PassbandFrequency', 0.5, 'StopbandFrequency', 0.6));
chkerr('e_pairs', @() designfilt('lowpassfir', 'FilterOrder', 20, 'CutoffFrequency'));

% ---------------------------------------------------------------------------------------------
% Helpers.
% ---------------------------------------------------------------------------------------------
function chkfilt(name, d, tol)
%CHKFILT Pins a designed filter: what it is, and every coefficient it carries.
%
%   An IIR filter's sections are sorted into a canonical order first. A bandpass or bandstop
%   cascade's two sections per fourth-order block come out in whichever order a root finder listed
%   the block's roots in, and that is a property of the eigensolver rather than of the filter --
%   MATLAB's own answer changes with the LAPACK build underneath it, and this repository has two
%   backends that disagree about it. Sorting removes the one thing that is not a property of the
%   design and leaves every coefficient pinned elementwise.
    fprintf('CHK|%s_imp|%s|exact\n', name, d.ImpulseResponse);
    fprintf('CHK|%s_method|%s|exact\n', name, d.DesignMethod);
    fprintf('CHK|%s_ord|%d|exact\n', name, filtord(d));
    fprintf('CHK|%s_stable|%d|exact\n', name, isstable(d));
    c = d.Coefficients;
    fprintf('CHK|%s_rows|%d|exact\n', name, size(c, 1));
    fprintf('CHK|%s_cols|%d|exact\n', name, size(c, 2));
    if size(c, 2) == 6
        c = [sortrows(c(:, 1:3), [3 2 1]), sortrows(c(:, 4:6), [3 2 1])];
    end
    chkcoef(name, c(:), tol);
end

function chkverb(name, y, d, tol)
%CHKVERB Pins one of the four verbs: what it designed, and what it did to the signal.
%
%   An FIR design is pinned end to end. An IIR one is pinned across the middle only, and at a looser
%   tolerance. Zero-phase filtering runs the cascade section by section and reflects the signal at
%   each end for each section, so what the first and last samples come out as depends on the order
%   the sections are in -- and that order, for a bandpass or bandstop design, comes out of a root
%   finder rather than out of the filter. The middle does not depend on it, and neither does anyone.
    fprintf('CHK|%s_imp|%s|exact\n', name, d.ImpulseResponse);
    fprintf('CHK|%s_method|%s|exact\n', name, d.DesignMethod);
    fprintf('CHK|%s_ord|%d|exact\n', name, filtord(d));
    fprintf('CHK|%s_len|%d|exact\n', name, numel(y));
    if numel(y) < 200
        % Too short to have a middle: the transient is the whole of it.
        return;
    end
    if strcmp(d.ImpulseResponse, 'fir')
        chkcoef(sprintf('%s_head', name), y(1:16), tol);
        chkcoef(sprintf('%s_tail', name), y(end-15:end), tol);
        chknum(sprintf('%s_energy', name), sum(y.^2), tol);
    else
        m = round(numel(y)/2);
        chkcoef(sprintf('%s_mid', name), y(m:m+15), 1e-7);
        % The energy is taken over the middle third and pinned a decade looser than the samples
        % are: it integrates whatever is left of the transient across two hundred samples.
        chknum(sprintf('%s_midenergy', name), sum(y(201:end-200).^2), 1e-6);
    end
end

function chkerr(name, f)
%CHKERR Pins that a call is refused, without pinning the words it is refused in.
    ok = 0;
    try
        f();
    catch
        ok = 1;
    end
    fprintf('CHK|%s|%d|exact\n', name, ok);
end

function chkcoef(name, v, tol)
%CHKCOEF Pins a coefficient vector elementwise, RELATIVE TO THE VECTOR rather than to each entry.
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
%CHKROOTS Pins a list of roots by its size and by order-free digests, since two eigensolvers that
%   agree on a multiset need not agree on the order it comes out in (ADR 0137).
    u = v(:);
    fprintf('CHK|r_%s_n|%d|exact\n', name, numel(u));
    if isempty(u)
        return;
    end
    chknum(sprintf('r_%s_sumr', name), sum(real(u)), tol);
    chknum(sprintf('r_%s_sumi', name), sum(abs(imag(u))), tol);
    chknum(sprintf('r_%s_summ', name), sum(abs(u)), tol);
    chknum(sprintf('r_%s_maxm', name), max(abs(u)), tol);
    chknum(sprintf('r_%s_minm', name), min(abs(u)), tol);
end
