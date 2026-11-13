% filter_conditions.m -- filter's initial and final conditions in R2025b's layout (open item 279).
%
% zf is the delays first and the signal's other dimensions after them, the filtered one taken out:
% [L 1] for any vector, [L 4] for a 4-by-5 filtered along its rows, [L 5] down its columns. zi is
% read the same way, or as one vector of length L that every slice starts from. Each case pins zf's
% size and class exactly, and its values by an order-weighted digest, so a transposed layout with
% the same numbers in it still fails. Both parities of L are swept (2 and 3), FIR and IIR, and a(1)
% other than one. A complex filter is refused here and a single one is worked in double; both are
% recorded as divergences (div=ADR0345).

bi = [0.2 0.3];  ai = [1 -0.5 0.25];          % L = 2, feedback
b3 = [1 -2 3 0.5] / 3;                          % L = 3, no feedback
bo = [0.1 0.2 0.3 0.4];  ao = [2 -0.4 0.3];     % L = 3, feedback, a(1) = 2
xr = [1 4 2 8 5 7 3 6];
xc = xr.';
X45 = reshape(mod((1:20)*0.618, 1), 4, 5);
X234 = reshape(mod((1:24)*0.414, 1), 2, 3, 4);

% --- zf's shape without zi ---------------------------------------------------------------------
fc_zf('row_fir', b3, 1, xr, [], []);
fc_zf('row_iir', bi, ai, xr, [], []);
fc_zf('row_iir3', bo, ao, xr, [], []);
fc_zf('col_fir', [1 2 3] / 6, 1, xc, [], []);
fc_zf('col_iir3', bo, ao, xc, [], []);
fc_zf('scalar', bi, ai, 5, [], []);
fc_zf('empty00', bi, ai, [], [], []);
fc_zf('empty10', bi, ai, zeros(1, 0), [], []);
fc_zf('empty03', bi, ai, zeros(0, 3), [], []);
fc_zf('empty30', bi, ai, zeros(3, 0), [], []);
fc_zf('m45_d1', bi, ai, X45, [], 1);
fc_zf('m45_d2', bi, ai, X45, [], 2);
fc_zf('m45_d2_L3', bo, ao, X45, [], 2);
fc_zf('m45_d1_L3', b3, 1, X45, [], 1);
fc_zf('m45_d3', bi, ai, X45, [], 3);
fc_zf('nd_d1', b3, 1, X234, [], 1);
fc_zf('nd_d2', bi, ai, X234, [], 2);
fc_zf('nd_d3', bi, ai, X234, [], 3);
fc_zf('row_d1', bi, ai, xr, [], 1);
fc_zf('col_d2', bi, ai, xc, [], 2);
fc_zf('row_d7', bi, ai, [1 2 3], [], 7);
fc_zf('L0_row', 2, 4, xr, [], []);
fc_zf('L0_m45', 2, 4, X45, [], 2);
fc_zf('L1_m45', [1 2], 1, X45, [], 2);

% --- zi's accepted shapes ----------------------------------------------------------------------
fc_zf('zi_row_row', bi, ai, xr, [0.5 0.25], []);
fc_zf('zi_col_row', bi, ai, xr, [0.5; 0.25], []);
fc_zf('zi_row_col', bi, ai, xc, [0.5 0.25], []);
fc_zf('zi_vec_m45_d2', bi, ai, X45, [0.5 0.25], 2);
fc_zf('zi_vec_nd_d3', bi, ai, X234, [0.5 0.25], 3);
fc_zf('zi_25_m45_d1', bi, ai, X45, reshape(1:10, 2, 5) / 10, 1);
fc_zf('zi_24_m45_d2', bi, ai, X45, reshape(1:8, 2, 4) / 10, 2);
fc_zf('zi_35_m45_d1', bo, ao, X45, reshape(1:15, 3, 5) / 10, 1);
fc_zf('zi_34_m45_d2', b3, 1, X45, reshape(1:12, 3, 4) / 10, 2);
fc_zf('zi_223_nd_d3', bi, ai, X234, reshape(1:12, 2, 2, 3) / 10, 3);
fc_zf('zi_26_nd_d3', bi, ai, X234, reshape(1:12, 2, 6) / 10, 3);
fc_zf('zi_224_nd_d2', bi, ai, X234, reshape(1:16, 2, 2, 4) / 10, 2);
fc_zf('zi_312_nd_d1', b3, 1, X234, reshape(1:36, 3, 12) / 10, 1);
fc_zf('zi_220_m45_d3', bi, ai, X45, reshape(1:40, 2, 20) / 10, 3);
fc_zf('zi_scalar_L1', [1 2], 1, X45, 0.5, 2);
fc_zf('zi_row4_L1', [1 2], 1, X45, [0.1 0.2 0.3 0.4], 2);
fc_zf('zi_scalar_x', bi, ai, 5, [0.5 0.25], []);
fc_zf('zi_empty_x', bi, ai, [], [0.5 0.25], []);
fc_zf('zi_empty03_x', bi, ai, zeros(0, 3), ones(2, 3), []);
fc_zf('zi_empty03_xv', bi, ai, zeros(0, 3), [1 2], []);
fc_zf('zi_L0_10', 3, 1, xc, zeros(1, 0), []);
fc_zf('zi_L0_05', 3, 1, X45, zeros(0, 5), []);
fc_zf('zi_logical', bi, ai, xr, [true false], []);
fc_zf('zi_int8', bi, ai, xr, int8([1 2]), []);

% --- zi refused --------------------------------------------------------------------------------
fc_zf('bad_len', bi, ai, xr, [1 2 3], []);
fc_zf('bad_scalar_L2', bi, ai, xr, 0.5, []);
fc_zf('bad_02', bi, ai, xr, zeros(0, 2), []);
fc_zf('bad_52', bi, ai, X45, reshape(1:10, 5, 2), 1);
fc_zf('bad_25_d2', bi, ai, X45, reshape(1:10, 2, 5), 2);
fc_zf('bad_1x2x5', bi, ai, X45, reshape(1:10, 1, 2, 5), 1);
fc_zf('bad_2x1x4', bi, ai, X45, reshape(1:8, 2, 1, 4), 2);
fc_zf('bad_232', bi, ai, X234, reshape(1:12, 2, 3, 2), 3);
fc_zf('bad_col4_L1', [1 2], 1, X45, [0.1; 0.2; 0.3; 0.4], 2);
fc_zf('bad_112', bi, ai, xr, reshape([0.5 0.25], 1, 1, 2), []);
fc_zf('bad_L0_scalar', 3, 1, xc, 0, []);
fc_zf('bad_L0_03', 3, 1, xc, zeros(0, 3), []);
fc_zf('bad_char', bi, ai, xr, 'ab', []);
fc_zf('bad_char_empty', bi, ai, xr, '', []);
fc_zf('bad_dim0', bi, ai, xr, [], 0);
fc_zf('bad_dim_frac', bi, ai, xr, [], 1.5);
try
    filter(bi, ai, xr, [1 2 3]);
    fprintf('CHK|y_only_bad|no error|exact\n');
catch e
    fprintf('CHK|y_only_bad|%s|exact\n', e.identifier);
    fprintf('CHK|y_only_bad_msg|%s|exact\n', e.message);
end

% --- zf resumes a filter cut in two along any dimension -----------------------------------------
fc_resume('resume_m45_d2', bi, ai, X45, 2);
fc_resume('resume_m45_d1', bo, ao, X45, 1);
fc_resume('resume_nd_d3', bi, ai, X234, 3);
fc_resume('resume_nd_d2', b3, 1, X234, 2);
fc_resume('resume_row', b3, 1, xr, 2);

% --- the classes ------------------------------------------------------------------------------
[~, zs] = filter(bi, ai, single(xr));
fprintf('CHK|single_zf_class|%s|exact\n', class(zs));
fprintf('CHK|single_zf_size|%s|exact\n', mat2str(size(zs)));
[~, zs2] = filter(single(bi), ai, X45, [], 2);
fprintf('CHK|single_b_zf_class|%s|exact\n', class(zs2));
fprintf('CHK|single_b_zf_size|%s|exact\n', mat2str(size(zs2)));
% R2025b works a single filter in single; JGraph works it in double and rounds once.
fprintf('CHK|single_zf_last|%.9g|div=ADR0345\n', double(zs(2)));
try
    [~, zx] = filter(bi, ai, xr + 1i * xr(end:-1:1));
    cx = sprintf('%s %s', class(zx), mat2str(size(zx)));
catch
    cx = 'refused';
end
fprintf('CHK|complex_zf|%s|div=ADR0345\n', cx);

function fc_zf(key, b, a, x, zi, dim)
try
    if isempty(dim)
        if isempty(zi) && isnumeric(zi) && isequal(size(zi), [0 0])
            [y, zf] = filter(b, a, x);
        else
            [y, zf] = filter(b, a, x, zi);
        end
    else
        [y, zf] = filter(b, a, x, zi, dim);
    end
catch e
    fprintf('CHK|%s_err|%s|exact\n', key, e.identifier);
    return;
end
fprintf('CHK|%s_y|%s %s|exact\n', key, class(y), mat2str(size(y)));
fprintf('CHK|%s_zf|%s %s|exact\n', key, class(zf), mat2str(size(zf)));
if ~isempty(zf)
    w = (1:numel(zf))';
    fprintf('CHK|%s_zfd|%.17g|rel=1e-13\n', key, sum(double(zf(:)) .* w));
end
if ~isempty(y)
    w = (1:numel(y))';
    fprintf('CHK|%s_yd|%.17g|rel=1e-13\n', key, sum(double(y(:)) .* w));
end
end

function fc_resume(key, b, a, x, dim)
n = size(x, dim);
k = floor(n / 2);
first = repmat({':'}, 1, ndims(x));
first{dim} = 1:k;
second = first;
second{dim} = k+1:n;
[~, z1] = filter(b, a, x(first{:}), [], dim);
[y2, z2] = filter(b, a, x(second{:}), z1, dim);
[yw, zw] = filter(b, a, x, [], dim);
fprintf('CHK|%s_y|%.3g|abs=1e-15\n', key, max(abs(y2(:) - reshape(yw(second{:}), [], 1))));
fprintf('CHK|%s_zf|%.3g|abs=1e-15\n', key, max(abs(z2(:) - zw(:))));
end
