% oi_filter_classes.m -- filter's classes and refusals as R2025b answers them (open item 305, ADR 0350).
%
% A complex b, a, x or zi is filtered in complex arithmetic, and each output comes back real when
% every imaginary part is zero. A single anywhere among b, a, x and zi (never the dimension) makes
% the call single and works it in single arithmetic, so its values are pinned to the bit (%.9g is
% unique for a float). Integers and logicals are read as their values and answer double. The
% refusals come in R2025b's order: every argument's kind (inputType, sparseInput) in turn, then b
% and a as vectors, then the dimension, then a(1), then zi. An empty b or a answers zeros.

bi = [0.2 0.3];  ai = [1 -0.5 0.25];
bo = [0.1 0.2 0.3 0.4];  ao = [2 -0.4 0.3];
xr = [1 4 2 8 5 7 3 6];
xcx = xr + 1i * xr(end:-1:1);
X45 = reshape(mod((1:20)*0.618, 1), 4, 5);

% --- complex -------------------------------------------------------------------------------------
fk_cx('cx_x', @() filter(bi, ai, xcx));
fk_cx('cx_b', @() filter(bi + 0.1i, ai, xr));
fk_cx('cx_a', @() filter(bi, ai + [0 0.1i 0], xr));
fk_cx('cx_a1', @() filter(bi, [1i -0.5 0.25], xr));
fk_cx('cx_zi', @() filter(bi, ai, xr, [0.5 0.25i]));
fk_cx('cx_all', @() filter(bo + 0.2i, ao - 0.1i, xcx, [1i 2 3]));
fk_cx('cx_fir', @() filter([1 2i 3], 1, xcx));
fk_cx('cx_fir2', @() filter([1 2i 3], 2, xr));
fk_cx('cx_m45', @() filter(bi, ai, X45 + 1i * fliplr(X45), [], 2));
fk_cx('cx_m45_zi', @() filter(bo, ao, X45 + 1i * X45, reshape(1:15, 3, 5) * 1i, 1));
fk_cx('cx_real_parts', @() filter(bi, ai, complex(xr)));
fk_cx('cx_real_m45', @() filter(bi, ai, complex(X45), [], 2));
fk_cx('cx_zf_real', @() filter([1 0], 1, [1 1i]));
fk_cx('cx_zi_real_zf', @() filter([1 2], 1, [1 2], 1i));
fk_cx('cx_scalar', @() filter(1, 1, 1i));

% --- single, to the bit --------------------------------------------------------------------------
fk_bits('s_x', @() filter(bi, ai, single(xr)));
fk_bits('s_b', @() filter(single(bi), ai, xr));
fk_bits('s_a', @() filter(bi, single(ai), xr));
fk_bits('s_zi', @() filter(bi, ai, xr, single([0.5 0.25])));
fk_bits('s_iir3', @() filter(bo, ao, single(xr)));
fk_bits('s_fir', @() filter(single(bo), 1, xr));
fk_bits('s_long', @() filter(single([0.3 -0.7 0.11 0.05]), single([1.25 -0.4 0.2 -0.05 0.01]), single(sin(1:40) * 3), single([0.1 -0.2 0.3 -0.4])));
fk_bits('s_m45', @() filter(single(bo), ao, X45, [], 2));
fk_bits('s_int', @() filter(single(bi), ai, int8(xr)));
fk_bits('s_cx', @() filter(single(bi), ai, xcx));
fk_bits('s_zi_int_x', @() filter([1 2], 1, int8([1 2]), single(3)));
fk_bits('s_mixed_int', @() filter(single([1 2]), int8(1), int16([1 2])));
fk_bits('s_dim_only', @() filter([1 2], 1, [1 2], [], single(2)));

% --- integers and logicals ------------------------------------------------------------------------
fk_bits('int_x', @() filter(bi, ai, int8(xr)));
fk_bits('int_b', @() filter(int8([1 2]), 1, xr));
fk_bits('int_all', @() filter(int8([1 2]), int8(1), int8(xr)));
fk_bits('int_mixed', @() filter(int8([1 2]), 1, int16([1 2])));
fk_bits('int_zi', @() filter([1 2], 1, [1 2], int8(3)));
fk_bits('uint_x', @() filter(bi, ai, uint16(xr)));
fk_bits('log_x', @() filter(bi, ai, xr > 3));
fk_bits('log_b', @() filter([true true], 1, xr));
fk_bits('log_all', @() filter(true, true, [true false]));
fk_bits('log_dim', @() filter([1 2], 1, [1 4 2 8], [], true));
fk_bits('col_b', @() filter([1; 2], [1; 0.5], xr));

% --- empty coefficients ---------------------------------------------------------------------------
fk_bits('empty_b10', @() filter(zeros(1, 0), ai, xr));
fk_bits('empty_b01', @() filter(zeros(0, 1), 1, xr));
fk_bits('empty_b_zi', @() filter(zeros(1, 0), [1 0.5], [1 4 2 8], 3));
fk_bits('empty_a10', @() filter([1 2 3], zeros(1, 0), [1 4 2 8]));
fk_bits('empty_a01', @() filter([1 2], zeros(0, 1), xr));
fk_bits('empty_a_col', @() filter([1 2 3], zeros(1, 0), [1 4 2 8]'));
fk_bits('empty_a_single', @() filter([0.5 1], zeros(1, 0), single([1 4 2 8])));
fk_bits('empty_both', @() filter(zeros(1, 0), zeros(1, 0), [1 4 2 8]));
fk_cx('empty_a_cx', @() filter([1 2], zeros(1, 0), [1 4 2 8] + 1i));

% --- refusals -------------------------------------------------------------------------------------
fk_bits('mat_b', @() filter([1 2; 3 4], 1, xr));
fk_bits('mat_a', @() filter(1, [1 0.5; 0.2 0.1], xr));
fk_bits('nd_b', @() filter(reshape(1:2, 1, 1, 2), 1, xr));
fk_bits('empty00_b', @() filter([], ai, xr));
fk_bits('empty00_a', @() filter(bi, [], xr));
fk_bits('sp_b', @() filter(sparse(bi), ai, xr));
fk_bits('sp_a', @() filter(bi, sparse(ai), xr));
fk_bits('sp_x', @() filter(bi, ai, sparse(xr)));
fk_bits('sp_zi', @() filter(bi, ai, xr, sparse([0.5 0.25])));
fk_bits('sp_dim', @() filter([1 2], 1, xr, [], sparse(1)));
fk_bits('char_x', @() filter(bi, ai, 'abc'));
fk_bits('char_b', @() filter('ab', ai, xr));
fk_bits('char_a', @() filter(bi, 'ab', xr));
fk_bits('str_x', @() filter(bi, ai, "abc"));
fk_bits('cell_x', @() filter(bi, ai, {1}));
fk_bits('struct_x', @() filter(bi, ai, struct('a', 1)));
fk_bits('str_dim', @() filter([1 2], 1, xr, [], "a"));
fk_bits('a1_zero', @() filter(bi, [0 1], xr));
fk_bits('a1_zero_fir', @() filter(bi, 0, xr));
fk_bits('a1_zero_cx', @() filter(bi, [0 1i], xr));
fk_bits('a1_nan', @() filter(bi, [NaN 1], xr));
fk_bits('a1_inf', @() filter(bi, [Inf 1], xr));
fk_bits('cx_dim', @() filter([1 2], 1, [1 2], [], 1i));
fk_bits('vec_dim', @() filter([1 2], 1, xr, [], [1 2]));

% --- the order of the refusals --------------------------------------------------------------------
fk_bits('ord_sp_mat', @() filter(sparse([1 2; 3 4]), 1, xr));
fk_bits('ord_char_mat', @() filter([1 2; 3 4], 'ab', xr));
fk_bits('ord_charb_spx', @() filter('ab', 1, sparse(xr)));
fk_bits('ord_spb_chara', @() filter(sparse([1 2]), 'ab', xr));
fk_bits('ord_spzi_charx', @() filter([1 2], 1, 'ab', sparse(1)));
fk_bits('ord_mat_spdim', @() filter([1 2; 3 4], 1, xr, [], sparse(1)));
fk_bits('ord_mat_a0', @() filter([1 2; 3 4], [0 1], xr));
fk_bits('ord_dim_a0', @() filter([1 2], [0 1], xr, [], 0));
fk_bits('ord_dim_mat', @() filter([1 2; 3 4], 1, xr, [], 0));
fk_bits('ord_zi_dim', @() filter([1 2], 1, xr, [1 2 3], 0));
fk_bits('ord_a0_zi', @() filter([1 2], [0 1], xr, [1 2 3]));
fk_bits('ord_inf_zi', @() filter([1 2], [Inf 1], xr, [1 2 3]));
fk_bits('ord_a0_charx', @() filter([1 2], [0 1], 'abc'));
fk_bits('ord_charzi_a0', @() filter([1 2], [0 1], xr, 'a'));

function fk_cx(key, f)
% Class, size and realness of both outputs, and an order-weighted digest of each plane.
try
    [y, zf] = f();
catch e
    fprintf('CHK|%s_err|%s|exact\n', key, e.identifier);
    return;
end
fprintf('CHK|%s|%s %s %d / %s %s %d|exact\n', key, class(y), mat2str(size(y)), isreal(y), ...
    class(zf), mat2str(size(zf)), isreal(zf));
fk_digest([key '_y'], y);
fk_digest([key '_zf'], zf);
end

function fk_digest(key, v)
if isempty(v)
    return;
end
w = (1:numel(v))';
d = sum(double(v(:)) .* w);
fprintf('CHK|%s_re|%.17g|rel=1e-13\n', key, real(d));
if ~isreal(v)
    fprintf('CHK|%s_im|%.17g|rel=1e-13\n', key, imag(d));
end
end

function fk_bits(key, f)
% Class, size and every value to the last bit of its class (17 digits a double, 9 a single).
try
    [y, zf] = f();
catch e
    fprintf('CHK|%s_err|%s|exact\n', key, e.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', key, e.message);
    return;
end
fprintf('CHK|%s|%s %s / %s %s|exact\n', key, class(y), mat2str(size(y)), class(zf), mat2str(size(zf)));
fprintf('CHK|%s_y|%s|exact\n', key, fk_values(y));
if isa(zf, 'single') && isreal(zf) && size(zf, 1) > 0
    % R2025b's first final delay of a single filter is not always its own running delay: it can
    % differ from it in the last bit (ADR 0350), so that row is held to two ulps of a single and
    % the other delays to the bit.
    for s = 1:numel(zf) / size(zf, 1)
        fprintf('CHK|%s_zf1_%d|%.9g|rel=2.5e-7\n', key, s, double(zf(1, s)));
    end
    zf = zf(2:end, :);
end
fprintf('CHK|%s_zf|%s|exact\n', key, fk_values(zf));
end

function s = fk_values(v)
if isa(v, 'single')
    digits = '%.9g';
else
    digits = '%.17g';
end
if isreal(v)
    s = strtrim(sprintf([digits ' '], double(v(:))));
else
    s = strtrim(sprintf([digits '%+' digits(2:end) 'i '], [real(double(v(:))) imag(double(v(:)))].'));
end
end
