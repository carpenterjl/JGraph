% m153_transforms.m -- dct, idct, fft and ifft over the length classes ADR 0153 touches: a power of
% two below and above the six-step threshold, an odd length, a 3-smooth length, a 5-smooth length,
% a prime, and 100000 (2^5 * 5^5, the shape of the benchmark's 4M row). Every value is compared to
% R2025b within the transform's own precision: the two engines take different roads to the same
% sum, so the last bits are not pinned here; the bits are pinned build to build by the
% head2head_v3 bits scripts instead.
phi = 0.618033988749895;
lengths = [8 33 100 1000 4097 32768 65536 65537 98304 100000];
for n = lengths
    x = mod((1:n)' * phi, 1) - 0.5 + 0.25 * sin(2 * pi * 0.01 * (0:n-1)');
    d = dct(x);
    r = idct(d);
    chk(sprintf('dct_%d_sumabs', n), sum(abs(d)) / n, 'rel=1e-12');
    chk(sprintf('dct_%d_c1', n), d(1), 'rel=1e-11');
    chk(sprintf('dct_%d_c2', n), d(2), 'rel=1e-11');
    chk(sprintf('dct_%d_c3', n), d(3), 'rel=1e-11');
    chk(sprintf('dct_%d_mid', n), d(floor(n / 2) + 1), 'abs=1e-10');
    chk(sprintf('dct_%d_end', n), d(end), 'abs=1e-10');
    chk(sprintf('dct_%d_energy', n), sum(d .^ 2) / sum(x .^ 2), 'rel=1e-12');
    chk(sprintf('idct_%d_maxerr', n), max(abs(r - x)), 'abs=1e-11');
    ri = idct(x);
    chk(sprintf('idct_%d_c1', n), ri(1), 'rel=1e-11');
    F = fft(x);
    chk(sprintf('fft_%d_dc', n), real(F(1)), 'rel=1e-11');
    chk(sprintf('fft_%d_b2_re', n), real(F(2)), 'abs=1e-9');
    chk(sprintf('fft_%d_b2_im', n), imag(F(2)), 'abs=1e-9');
    chk(sprintf('fft_%d_peak', n), max(abs(F)), 'rel=1e-12');
    chk(sprintf('fft_%d_energy', n), sum(abs(F) .^ 2) / n, 'rel=1e-12');
    chk(sprintf('ifft_%d_maxerr', n), max(abs(ifft(F) - x)), 'abs=1e-11');
    chk(sprintf('fft_%d_shape', n), mat2str(size(F)), 'exact');
end

% The argument grammar around the packed road: padding, cropping, a dimension, a row, a class.
x = mod((1:100)' * phi, 1);
chk('dct_pad_shape', mat2str(size(dct(x, 128))), 'exact');
dp = dct(x, 128);
chk('dct_pad_c1', dp(1), 'rel=1e-11');
chk('dct_crop_shape', mat2str(size(dct(x, 64))), 'exact');
dc = dct(x, 64);
chk('dct_crop_c2', dc(2), 'rel=1e-11');
chk('dct_row_shape', mat2str(size(dct(x'))), 'exact');
chk('dct_row_equals_column', max(abs(dct(x') - dct(x)')), 'abs=1e-13');
chk('dct_dim2_of_column', max(abs(dct(x, [], 2) - x)), 'abs=1e-13');
chk('dct_matrix_shape', mat2str(size(dct([x, 2*x]))), 'exact');
dm = dct([x, 2*x]);
chk('dct_matrix_col2', max(abs(dm(:, 2) - 2 * dct(x))), 'abs=1e-12');
chk('dct_single_class', class(dct(single(x))), 'exact');
ds = dct(single(x));
chk('dct_single_c2', double(ds(2)), 'rel=1e-5');
chk('dct_type1_roundtrip', max(abs(dct(dct(x, 'Type', 1), 'Type', 1) - x)), 'abs=1e-11');
chk('dct_type4_roundtrip', max(abs(dct(dct(x, 'Type', 4), 'Type', 4) - x)), 'abs=1e-11');
chk('dct_type3_is_idct', max(abs(dct(dct(x), 'Type', 3) - x)), 'abs=1e-11');
chk('dct_scalar', dct(7), 'div=ADR0153');   % MATLAB takes 7 through its FFT and answers 7.0000000000000009; the scalar is its own transform here
chk('dct_empty_shape', mat2str(size(dct(zeros(0, 1)))), 'div=ADR0153');   % MATLAB answers 0-by-0 for a 0-by-1; the empty column stays a column here

function chk(name, v, rule)
if ischar(v)
    fprintf('CHK|%s|%s|%s\n', name, v, rule);
else
    fprintf('CHK|%s|%.17g|%s\n', name, double(v), rule);
end
end
