% m159_slices.m -- the reads item 12a (ADR 0159) serves as a copy, pinned to R2025b: a read is a
% copy, so every selection is a bits line over data built from products and mod (the same bytes on
% both engines), with the shapes MATLAB gives them as exact lines; the empties, the fractional and
% classed bounds, and the positions outside the extent are pinned as the shapes and refusals MATLAB
% answers. The explicit solvers item 12e.1 touches are pinned by their step counts on short runs
% where the two engines still take the same steps, and by the energy of a smooth orbit.
bitsdir = tempname;
mkdir(bitsdir);

phi = 0.618033988749895;
x = mod((1:300000) * phi, 1) - 0.5;
x(7) = -0;
x(11) = NaN;
M = reshape(mod((1:400000) * phi, 1), 400, 1000);

bits('full', bitsdir, x(1:300000));
bits('prefix', bitsdir, x(1:100000));
bits('inner', bitsdir, x(1000:200000));
bits('step2', bitsdir, x(1:2:end));
bits('step7', bitsdir, x(3:7:end));
bits('desc', bitsdir, x(end:-1:1));
bits('desc3', bitsdir, x(end-5:-3:2));
bits('one', bitsdir, x(5:5));
bits('signed_zero', bitsdir, x(6:8));
bits('nan_payload', bitsdir, x(10:12));
xc = x';
bits('col_read', bitsdir, xc(2:2:end));
bits('matrix_linear', bitsdir, M(3:3:30000));
bits('row', bitsdir, M(234, :));
bits('col', bitsdir, M(:, 567));
bits('colblock', bitsdir, M(:, 201:400));
bits('rowblock', bitsdir, M(101:300, :));
bits('subblock', bitsdir, M(101:300, 201:400));
bits('stepped_block', bitsdir, M(1:3:end, end:-2:1));
bits('scalar_row_range', bitsdir, M(77, 10:990));
bits('range_scalar_col', bitsdir, M(10:390, 77));
bits('one_by_range', bitsdir, M(7, 10:10));
bits('all_all', bitsdir, M(:, :));
mask = x > 0;
bits('logical_slice', bitsdir, mask(1:2:end));
Mk = M > 0.5;
bits('logical_block', bitsdir, Mk(1:50, 1:50));
u = uint8(mod((1:100000) * 7, 251));
bits('uint8_slice', bitsdir, u(3:3:end));
c = char(65 + mod(reshape((1:6000) * 3, 60, 100), 26));
bits('char_rows', bitsdir, double(c(10:20, :)));
bits('char_slice', bitsdir, double(c(2:2:end, 5:50)));

shape('shape_full', x(1:300000));
shape('shape_step', x(1:2:end));
shape('shape_desc', x(end:-1:1));
shape('shape_one', x(5:5));
shape('shape_col_read', xc(2:2:end));
shape('shape_matrix_linear', M(3:3:30000));
shape('shape_row', M(234, :));
shape('shape_col', M(:, 567));
shape('shape_subblock', M(101:300, 201:400));
shape('shape_stepped', M(1:3:end, end:-2:1));
shape('shape_one_by_range', M(7, 10:10));
shape('shape_element', M(7, 9));
shape('shape_empty_desc', x(5:1));
shape('shape_empty_step', x(1:-1:5));
shape('shape_empty_col_range', M(:, 5:1));
shape('shape_empty_row_range', M(5:1, :));
shape('shape_empty_both', M(5:1, 5:1));
shape('shape_frac_stop', x(1:2.5));
shape('shape_frac_step', x(1:0.5:1));
shape('shape_uint8_bounds', x(uint8(2):5));
shape('shape_int32_step', x(1:int32(3):20));
shape('shape_logical_slice', mask(1:2:end));
shape('shape_char_rows', c(10:20, :));

chk('frac_stop_sum', sum(x(1:2.5)), 'exact');
chk('uint8_bounds_sum', sum(x(uint8(2):5)), 'exact');
chk('int32_step_sum', sum(x(1:int32(3):20)), 'exact');
chk('single_bound_sum', sum(x(single(3):9)), 'exact');
chk('char_class', class(c(3:7, :)), 'exact');
chk('char_row_text', c(4, 1:10), 'exact');
chk('logical_class', class(mask(1:2:end)), 'exact');
chk('uint8_class', class(u(3:3:end)), 'exact');
chk('uint8_sum', sum(double(u(3:3:end))), 'exact');

refused('refused_zero', @() x(0:5));
refused('refused_high', @() x(299990:300010));
refused('refused_negative', @() x(-3:-1:-10));
refused('refused_past_end', @() x(300001:300001));
refused('refused_block_zero', @() M(0:2, 1));
refused('refused_block_high', @() M(1:3, 999:1001));
refused('refused_row_high', @() M(401, :));
refused('refused_fractional_row', @() M(1.5, :));
% R2025b rounds a colon with fractional operands when it is used directly as an index, with a
% warning (elements 2, 3, 3 here); JGraph refuses the fractional position (ADR 0159, older than the item).
refused('refused_fractional_step', @() x(2:0.5:3), 'div=ADR0159');
% R2025b refuses a classed range whose stop is outside the class (MATLAB:colon:outOfRange);
% JGraph saturates it to [254 255 255 255 255] and reads five elements (ADR 0159, older than the item).
refused('refused_uint8_saturating', @() x(uint8(254):258), 'div=ADR0159');

% --- 12e.1: the explicit solvers whose interpolant lost its per-component weights ---
lorenz = @(t, y) [10*(y(2) - y(1)); y(1)*(28 - y(3)) - y(2); y(1)*y(2) - (8/3)*y(3)];
orbit = @(t, y) [y(3); y(4); -y(1)/(y(1)^2 + y(2)^2)^1.5; -y(2)/(y(1)^2 + y(2)^2)^1.5];
[tl, yl] = ode23(lorenz, [0 20], [1; 1; 1]);
chk('ode23_lorenz20_rows', numel(tl), 'exact');
[tl, yl] = ode45(lorenz, [0 20], [1; 1; 1]);
chk('ode45_lorenz20_rows', numel(tl), 'exact');
opts = odeset('RelTol', 1e-9, 'AbsTol', 1e-11);
[to, yo] = ode45(orbit, [0 10], [1; 0; 0; 1], opts);
chk('ode45_orbit10_rows', numel(to), 'exact');
chk('ode45_orbit10_energy', 0.5*(yo(end,3)^2 + yo(end,4)^2) - 1/hypot(yo(end,1), yo(end,2)), 'rel=1e-6');
[tn, yn] = ode45(orbit, 0:0.5:10, [1; 0; 0; 1], opts);
chk('ode45_orbit_named_rows', numel(tn), 'exact');
chk('ode45_orbit_named_x_end', yn(end, 1), 'rel=1e-6');
[tr, yr] = ode23(orbit, [0 10], [1; 0; 0; 1], odeset('Refine', 8));
chk('ode23_orbit_refine8_rows', numel(tr), 'exact');
chk('ode23_orbit_refine8_x_end', yr(end, 1), 'rel=1e-3');

function shape(name, v)
fprintf('CHK|%s|%d %d|exact\n', name, size(v, 1), size(v, 2));
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

function chk(name, v, rule)
if ischar(v)
    fprintf('CHK|%s|%s|%s\n', name, v, rule);
else
    fprintf('CHK|%s|%.17g|%s\n', name, double(v), rule);
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
