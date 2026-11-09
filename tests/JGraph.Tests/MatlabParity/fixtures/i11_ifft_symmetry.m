% i11_ifft_symmetry.m -- ifft's own conjugate-symmetry test (the every-row plan's I11, ADR 0323):
% R2025b answers a real array when every slice along the transform dimension, padded or cut to the
% transform length, is exactly conjugate-symmetric, with no 'symmetric' word given. These lines pin
% which inputs answer real, that the real answer is the 'symmetric' one, and the forms around it
% (a length, a dimension, ifft2 and ifftn, the words, a single, a NaN, a scalar, an empty).
phi = 0.618033988749895;
x8 = mod((1:8)' * phi, 1) - 0.5;
x9 = mod((1:9)' * phi, 1) - 0.5;
F8 = fft(x8);
F9 = fft(x9);
chk('even_isreal', isreal(ifft(F8)), 'exact');
chk('odd_isreal', isreal(ifft(F9)), 'exact');
chk('even_same_as_symmetric', isequal(ifft(F8), ifft(F8, 'symmetric')), 'exact');
chk('odd_same_as_symmetric', isequal(ifft(F9), ifft(F9, 'symmetric')), 'exact');
chk('even_maxerr', max(abs(ifft(F8) - x8)), 'abs=1e-15');
chk('row_isreal', isreal(ifft(F8.')), 'exact');
chk('row_shape', mat2str(size(ifft(F8.'))), 'exact');

% One slice that is not symmetric makes the whole answer complex.
G = [F8, F8];
chk('matrix_both_isreal', isreal(ifft(G)), 'exact');
H = G;
H(2, 2) = H(2, 2) + 1i;
chk('matrix_one_off_isreal', isreal(ifft(H)), 'exact');
chk('matrix_one_off_col1_imag', max(abs(imag(subsref(ifft(H), struct('type', '()', 'subs', {{':', 1}}))))), 'abs=1e-15');
chk('matrix_along_rows_isreal', isreal(ifft(G.', [], 2)), 'exact');
chk('matrix_along_dim1_of_rows_isreal', isreal(ifft(G.')), 'exact');

% A real input is conjugate-symmetric when it reads the same backwards past its first element.
chk('real_symmetric_isreal', isreal(ifft([1 2 3 2])), 'exact');
chk('real_symmetric_value', mat2str(ifft([1 2 3 2])), 'exact');
chk('real_plain_isreal', isreal(ifft([1 2 3 4])), 'exact');
chk('real_odd_symmetric_isreal', isreal(ifft([5 2 2])), 'exact');

% The test is made on the slice the transform reads: padded or cut.
chk('pad_breaks_isreal', isreal(ifft([1 2 2], 4)), 'exact');
chk('pad_keeps_isreal', isreal(ifft([1 2 0 2], 6)), 'exact');
chk('cut_makes_isreal', isreal(ifft([1 2 2 5], 3)), 'exact');
chk('cut_breaks_isreal', isreal(ifft([1 2 3 2], 3)), 'exact');

% A complex array whose imaginary parts are all zero, a complex DC term, a Nyquist term.
chk('complex_zero_imag_isreal', isreal(ifft(complex([1 2 2]))), 'exact');
chk('dc_imag_isreal', isreal(ifft([1+1i 2 2])), 'exact');
F8n = F8;
F8n(5) = F8n(5) + 0.5i;
chk('nyquist_imag_isreal', isreal(ifft(F8n)), 'exact');
chk('nyquist_imag_symmetric_maxerr', max(abs(ifft(F8n, 'symmetric') - ifft(F8, 'symmetric'))), 'abs=1e-15');

% The words, a class, a NaN, a scalar and an empty.
chk('nonsymmetric_isreal', isreal(ifft(F8, 'nonsymmetric')), 'exact');
chk('nonsymmetric_imag_max', max(abs(imag(ifft(F8, 'nonsymmetric')))), 'abs=1e-15');
chk('single_isreal', isreal(ifft(single(F8))), 'exact');
chk('single_class', class(ifft(single(F8))), 'exact');
chk('nan_isreal', isreal(ifft([1 NaN NaN])), 'exact');
chk('scalar_isreal', isreal(ifft(5)), 'exact');
chk('scalar_complex_isreal', isreal(ifft(2 + 3i)), 'exact');
chk('empty_isreal', isreal(ifft(zeros(0, 1))), 'exact');

% Two and N dimensions: the whole array's symmetry, not each pass's.
A = mod(reshape(1:24, 4, 6) * phi, 1);
chk('ifft2_isreal', isreal(ifft2(fft2(A))), 'exact');
chk('ifft2_maxerr', max(max(abs(ifft2(fft2(A)) - A))), 'abs=1e-14');
chk('ifft2_same_as_symmetric', isequal(ifft2(fft2(A)), ifft2(fft2(A), 'symmetric')), 'exact');
B = fft2(A);
B(2, 3) = B(2, 3) + 1i;
chk('ifft2_off_isreal', isreal(ifft2(B)), 'exact');
chk('ifft2_cols_symmetric_only_isreal', isreal(ifft2(fft(A))), 'exact');
C = mod(reshape(1:60, 3, 4, 5) * phi, 1);
chk('ifftn_isreal', isreal(ifftn(fftn(C))), 'exact');
chk('ifftn_maxerr', max(abs(reshape(ifftn(fftn(C)) - C, [], 1))), 'abs=1e-14');
chk('ifft2_pad_isreal', isreal(ifft2(fft2(A), 4, 7)), 'exact');

function chk(name, v, rule)
if ischar(v)
    fprintf('CHK|%s|%s|%s\n', name, v, rule);
else
    fprintf('CHK|%s|%.17g|%s\n', name, double(v), rule);
end
end
