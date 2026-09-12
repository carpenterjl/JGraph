% p0_bits_rule.m -- the building blocks of the `bits` rule, proven before anything rests on them.
%
% A bits line pins a whole array to the bit: the fixture writes the array through writebits (a
% header `class rows cols ...`, then one num2hex row per element in column-major order, a real
% plane then an imaginary one for complex, logical and char written as the doubles of their codes),
% closes the file, and prints CHK|name|file:<absolute path>|bits. Whoever captures the output
% replaces the path with the file's SHA-256 and deletes the file. This fixture pins num2hex's
% spellings of the awkward doubles as exact lines first, then writes one file of each kind, so a
% recording of it proves MATLAB and JGraph produce byte-identical files for the same array.

chk('neg_zero', num2hex(-0));
chk('nan', num2hex(NaN));
chk('neg_nan', num2hex(-NaN));
chk('zero_over_zero', num2hex(0/0));
chk('inf', num2hex(Inf));
chk('neg_inf', num2hex(-Inf));
chk('subnormal', num2hex(realmin/2));
chk('smallest_subnormal', num2hex(realmin*eps));
chk('eps', num2hex(eps));
chk('one', num2hex(1));
chk('third', num2hex(1/3));
chk('single_pi', num2hex(single(pi)));
chk('single_neg_zero', num2hex(single(-0)));
chk('single_nan', num2hex(single(NaN)));
chk('single_inf', num2hex(single(-Inf)));
chk('single_subnormal', num2hex(single(realmin('single'))/2));
chk('eps_single_class', class(eps(single(1))));
chk('eps_single', num2hex(eps(single(1))));
chk('eps_single_256', num2hex(eps(single(256))));
chk('single_plus_eps', num2hex(single(1) + eps(single(1))));
chk('column_shape', mat2str(size(num2hex([1; 2; 3]))));
chk('row_shape', mat2str(size(num2hex([1 2 3]))));
chk('single_column_shape', mat2str(size(num2hex(single([1; 2; 3])))));
chk('empty_shape', mat2str(size(num2hex(zeros(0, 1)))));
h = num2hex([1 2 2 1]');
chk('rows_in_order', [h(1, :) ' ' h(2, :) ' ' h(3, :) ' ' h(4, :)]);

x = mod((1:1e5) * 0.618033988749895, 1);
bitsdir = tempname;
mkdir(bitsdir);
bits('vector_1e5', bitsdir, x);
bits('matrix_3x4', bitsdir, reshape(x(1:12), 3, 4));
bits('column', bitsdir, x(1:7)');
bits('single_1e4', bitsdir, single(x(1:1e4)));
bits('complex', bitsdir, x(1:100) + 1i * x(101:200));
bits('complex_single', bitsdir, single(x(1:50)) + 1i * single(x(51:100)));
bits('logical', bitsdir, x(1:1000) > 0.5);
bits('char', bitsdir, 'hello, world');
bits('with_specials', bitsdir, [0 -0 Inf -Inf NaN -NaN realmin/2 eps 1/3]);
bits('empty_0x3', bitsdir, zeros(0, 3));
bits('scalar', bitsdir, pi);
bits('nd_2x3x4', bitsdir, reshape(x(1:24), 2, 3, 4));
bits('int32', bitsdir, int32([-5 0 5 2147483647]));
bits('chunk_boundary', bitsdir, x(1:65537));
bits('two_of_the_same', bitsdir, x(1:12));

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, v);
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
