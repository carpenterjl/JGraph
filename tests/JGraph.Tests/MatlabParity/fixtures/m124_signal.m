% m124_signal.m -- the six Signal names that existed at M124 (butter db dct firpm freqz idct) and
% the base filter. Three of these forms diverged when the fixture was written and were recorded
% under ADR 0126; M134 closed all three, so the lines below are ordinary pinned values now.

% butter's two-output form, and its single output, which is b alone.
[b, a] = butter(4, 0.3);
fprintf('CHK|butter_two_outputs|%d|exact\n', 1);
fprintf('CHK|butter_single_numel|%d|exact\n', numel(butter(4, 0.3)));
fprintf('CHK|butter_b1|%.17g|rel=1e-12\n', b(1));
fprintf('CHK|butter_b3|%.17g|rel=1e-12\n', b(3));
fprintf('CHK|butter_a2|%.17g|rel=1e-12\n', a(2));
fprintf('CHK|butter_a5|%.17g|rel=1e-12\n', a(5));
fprintf('CHK|butter_bsum|%.17g|rel=1e-12\n', sum(b));
fprintf('CHK|butter_asum|%.17g|rel=1e-12\n', sum(a));

% filter with those coefficients, on a ramp and on a step.
y = filter(b, a, 1:20);
fprintf('CHK|filter_shape|%s|shape\n', mat2str(size(y)));
fprintf('CHK|filter_y10|%.17g|rel=1e-12\n', y(10));
fprintf('CHK|filter_y20|%.17g|rel=1e-12\n', y(20));
s = filter(b, a, ones(1, 200));
fprintf('CHK|filter_step_settles|%.17g|rel=1e-9\n', s(200));
fprintf('CHK|filter_fir|%.17g|rel=1e-14\n', sum(filter([1 2 1] / 4, 1, [1 0 0 0 1 0 0 0])));

% freqz's single output is h alone, eight complex values.
h = freqz(b, a, 8);
fprintf('CHK|freqz_single_numel|%d|exact\n', numel(h));
fprintf('CHK|freqz_h2_real|%.17g|rel=1e-12\n', real(h(2)));
fprintf('CHK|freqz_h2_imag|%.17g|rel=1e-12\n', imag(h(2)));

% dct and idct on a ramp: a round trip and two coefficients.
d = dct(1:8);
fprintf('CHK|dct_shape|%s|shape\n', mat2str(size(d)));
fprintf('CHK|dct_1|%.17g|rel=1e-12\n', d(1));
fprintf('CHK|dct_2|%.17g|rel=1e-12\n', d(2));
fprintf('CHK|dct_8|%.17g|abs=1e-12\n', d(8));
r = idct(d);
fprintf('CHK|idct_roundtrip|%.17g|abs=1e-12\n', max(abs(r - (1:8))));
fprintf('CHK|idct_last|%.17g|rel=1e-12\n', r(8));

% firpm: a lowpass of order 20. The exchange is a transcription of MATLAB's since M134, so the
% coefficients agree to the last figures rather than to five.
f = firpm(20, [0 0.3 0.5 1], [1 1 0 0]);
fprintf('CHK|firpm_shape|%s|shape\n', mat2str(size(f)));
fprintf('CHK|firpm_centre|%.17g|rel=1e-12\n', f(11));
fprintf('CHK|firpm_symmetric|%.17g|abs=1e-12\n', max(abs(f - fliplr(f))));
fprintf('CHK|firpm_dc|%.17g|rel=1e-12\n', sum(f));

% db: the decibel conversion in its voltage and power readings.
fprintf('CHK|db_2|%.17g|rel=1e-12\n', db(2));
fprintf('CHK|db_half|%.17g|rel=1e-12\n', db(0.5));
fprintf('CHK|db_10|%.17g|rel=1e-12\n', db(10));
