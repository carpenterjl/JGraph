% m160_loops.m -- the loops item 12 stage 2 (ADR 0160) compiles, pinned to R2025b. The compiled
% road and the walk must print these same bytes with the compiler forced on and off, so every
% number is an exact line (seventeen digits) or a bits line over a vector the loop built; the
% loops lean on what the compiler changed -- fused comparisons and back edges (12b), the unchecked
% register file (12c), the vector class and the walked statement (12d) -- and on the refusals and
% bails around them: growth, a logical stored into a double vector, a row plus a column, a
% product of two vectors, a guarded kernel that leaves the reals, positions the walk refuses.
bitsdir = tempname;
mkdir(bitsdir);

phi = 0.618033988749895;
n = 2000;
x = mod((1:n) * phi, 1) - 0.5;
x(7) = -0;
x(11) = NaN;
xc = x';
w = mod((1:n) * 0.381966011250105, 1);

%% 12b -- scalar loops: the benchmark loop, break/continue, nested ranges, a while, NaN compares.
acc = 0;
v = 1.0;
for k = 1:200000
    v = mod(v * 1.0000001 + 0.001, 2);
    if v > 1
        acc = acc + v;
    end
end
chk('bench_acc', acc);
chk('bench_v', v);
chk('bench_k', k);

a = 0; b = 0; c = 0;
for k = 1:30
    a = a + 1;
    if a > 3
        a = a - 1;
        continue;
    end
    b = b + 2;
    if b > 5
        break;
    end
    c = c + 3;
    c = c * 1.001;
end
chk('flow', [a b c k]);

s = 0; t = 0;
for i = 1:40
    s = s + i;
    for j = 1:i
        s = s + j * 0.5;
        t = t + 1;
    end
    t = t * 1.0001;
end
m = 0;
while m < 50
    m = m + 1;
    s = s - m / 7;
    t = t + m;
end
chk('nested', [s t i j m]);

s1 = 0; s2 = 0;
for i = 1:7
    for j = 1:7
        if i == 1; p = 0; elseif i == 2; p = -0; elseif i == 3; p = 1; elseif i == 4; p = -1;
        elseif i == 5; p = 0/0; elseif i == 6; p = 1/0; else; p = -1/0; end
        if j == 1; q = 0; elseif j == 2; q = -0; elseif j == 3; q = 1; elseif j == 4; q = -1;
        elseif j == 5; q = 0/0; elseif j == 6; q = 1/0; else; q = -1/0; end
        r = 0;
        if p < q; r = r + 1; end
        if p <= q; r = r + 2; end
        if p > q; r = r + 4; end
        if p >= q; r = r + 8; end
        if p == q; r = r + 16; end
        if p ~= q; r = r + 32; end
        s1 = s1 + r * (i * 7 + j);
        s2 = s2 + r * r * (i * 7 + j);
    end
end
chk('compares', [s1 s2]);

total = 0;
for k = 0:0.1:10
    total = total + sqrt(k) - log(k + 1) + atan2(k, 3) + mod(k, 0.7) + rem(k, 0.3) + min(k, 4) + max(k, 6) + abs(k - 5) + floor(k) + ceil(k) + round(k);
end
chk('kernels', total);

%% 12d -- vector element loops: a read and a write are the walk's bytes.
y = zeros(1, n);
z = zeros(n, 1);
s = 0;
for k = 1:n
    y(k) = x(k) * 2 + x(n + 1 - k);
    z(k) = xc(k) - w(k);
    s = s + y(k) * z(k);
end
bits('elements_row', bitsdir, y);
bits('elements_col', bitsdir, z);
chk('elements_sum', s);

%% 12d -- elementwise combinations: the walk's own operators and builtins on the whole vector.
v = x(1:40);
u = w(1:40);
s = 0;
for k = 1:300
    v = v .* 0.5 + u;
    u = -u + v ./ 3 - 1;
    t = 2 * v;
    t = t / 7;
    q = v .^ 2 + abs(u);
    r = sin(v) - cos(u) + exp(q .* 0.01);
    s = s + t(k - 40 * floor((k - 1) / 40)) + q(3) - r(5) + floor(v(3)) + w(k);
end
bits('elementwise_v', bitsdir, v);
bits('elementwise_u', bitsdir, u);
bits('elementwise_r', bitsdir, r);
chk('elementwise_sum', s);

c2 = xc(1:5);
r2 = x(1:5);
for k = 1:5
    c2 = c2 * 1.5;
    r2 = r2 .* 2;
    c2(k) = r2(k);
end
bits('orientation_col', bitsdir, c2);
bits('orientation_row', bitsdir, r2);

%% 12d -- what bails: a kernel that leaves the reals, a row plus a column, two vectors under *.
v = x(1:6) + 0.3;
acc = 0;
for k = 1:6
    v = v - 0.2;
    q = sqrt(v);
    p = v .^ 0.5;
    acc = acc + abs(q(1)) + abs(p(2));
end
chk('leaves_reals', acc, 'rel=1e-14');
chk('leaves_reals_class', [isreal(q) isreal(p)]);

r3 = x(1:3);
c3 = xc(1:3);
for k = 1:3
    m3 = r3 + c3;
    r3 = r3 * 2;
end
chk('expanded', [size(m3) sum(m3(:))]);

d = 0;
for k = 1:3
    d = d + r3 * c3 + r3(k);
end
chk('inner_product', d, 'rel=1e-14'); % a dot product: OpenBLAS and the managed kernels sum in different orders

%% 12d -- growth, a logical into a double vector, positions the walk refuses.
g = zeros(1, 3);
for k = 1:9
    g(k) = k * 0.5;
end
bits('grown', bitsdir, g);

f = zeros(1, 10);
h = zeros(1, 10);
for k = 1:10
    f(k) = x(k) > 0;
    tf = x(k) < 0;
    h(k) = tf;
end
chk('logical_store', [sum(f) sum(h)]);
fprintf('CHK|logical_store_class|%s %s|div=ADR0160\n', class(f), class(h));

refused('refused_zero_position', @() vzero(x));
refused('refused_fraction_position', @() vfraction(x));
refused('refused_past_end_read', @() vpast(x));
refused('refused_nested_zero_step', @() vzerostep(), 'div=ADR0160');

%% 12d -- walked statements inside a compiled loop, and a return from one.
cs = cell(1, 5);
st = struct('n', 0);
acc = 0;
for k = 1:5
    acc = acc + k * 0.5;
    fprintf('CHK|walked_%d|%.17g|exact\n', k, acc);
    cs{k} = sprintf('%d', k * k);
    st.n = st.n + acc;
    [q1, r1] = deal(k, k + 1);
    acc = acc + r1 - q1;
end
fprintf('CHK|walked_cell|%s|exact\n', strjoin(cs, ','));
chk('walked_struct', [st.n acc]);
chk('early_return', [partial(3) partial(10)]);

%% 12d -- the char-matrix loop of the d12 row: the cell store walks, the rest compiles.
rowsc = cell(200, 1);
for r = 1:200
    rowsc{r} = char(65 + mod((r - 1) + (0:39), 26));
end
cm = char(rowsc);
chk('charmatrix', [size(cm) sum(double(cm(:))) sum(double(cm(7, :)))]);

%% A loop variable after an empty range, and after a break.
for k = 5:1
    acc = 0;
end
fprintf('CHK|empty_range_var|%d|exact\n', exist('k', 'var'));
for qq = 5:1
    acc = 0;
end
fprintf('CHK|empty_range_new_var|%d|div=ADR0160\n', exist('qq', 'var'));
for k = 1:10
    if k == 4
        break;
    end
end
chk('break_var', k);


function chk(name, v, rule)
if nargin < 3
    rule = 'exact';
end
fprintf('CHK|%s|%s|%s\n', name, sprintf('%.17g ', v), rule);
end

function refused(name, f, rule)
if nargin < 3
    rule = 'exact';
end
ok = 0;
try
    f();
catch
    ok = 1;
end
fprintf('CHK|%s|%d|%s\n', name, ok, rule);
end

function r = partial(n)
r = 0;
for k = 1:n
    r = r + k;
    if r > 6
        return;
    end
end
r = -1;
end

function vzero(x)
for k = 0:2
    t = x(k);
end
end

function vfraction(x)
for k = 1:3
    x(k + 0.5) = 1;
end
end

function vpast(x)
for k = 2000:2002
    t = x(k);
end
end

function vzerostep()
w = 0;
for k = 1:3
    w = w + 1;
    for j = 1:(k - 3):5
        w = w + 100;
    end
end
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
