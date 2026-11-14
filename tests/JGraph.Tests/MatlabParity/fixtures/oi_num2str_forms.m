% Open item 316 (ADR 0359): num2str's precision and format forms as R2025b's num2str.m lays them
% out. A complex array prints its real parts right-justified and its imaginary parts, signed and
% followed by i, after them, each column padded to num2str.m's forced width: num2str(z, 17) is 17
% significant digits of each part. An integer class ignores a digit count. A format with one
% conversion is printed an element at a time, each column right-justified (left when the
% conversion is left-aligned in a width) and the result trimmed: num2str([1; 10], '%d') is
% [' 1'; '10']. Every row prints the answer's size and its rows joined with ' / ', a blank
% written as '_'.

cases = {
 'c_row17', [0.7+0.1i 0.3-0.2i], 17
 'c_row10', [0.7+0.1i 0.3-0.2i], 10
 'c_row4', [0.7+0.1i 0.3-0.2i], 4
 'c_row1', [0.7+0.1i 0.3-0.2i], 1
 'c_scalar17', 0.7+0.1i, 17
 'c_scalar5', pi+exp(1)*1i, 5
 'c_scalar20', pi+exp(1)*1i, 20
 'c_int17', 3+4i, 17
 'c_int5', [3+4i 10-200i], 5
 'c_neg17', -0.7-0.1i, 17
 'c_mat17', [0.7+0.1i 0.3; 1/3i 2], 17
 'c_mat6', [0.7+0.1i 0.3; 1/3i 2], 6
 'c_big17', 1e10+1e-10i, 17
 'c_nan17', complex(NaN, Inf), 17
 'c_nan5', [complex(NaN, 1) complex(2, -Inf)], 5
 'c_fmt', [0.7+0.1i 0.3-0.2i], '%10.5f'
 'c_fmt2', 0.7+0.1i, '%g'
 'c_fmt3', [1+2i 3+4i], '%d'
 'c_fmt_col', [1+2i; 30-4i], '%d'
 'c_fmt_lit', [1+2i 3-4i], 'x%dy'
 'c_fmt_left', [1.5+2i; 30-4i], '%-8.2f'
 'c_fmt_zero', 0.5-0.25i, '%08.3f'
 'c_fmt_e', 0.5-0.25i, '%e'
 'c_single17', single(0.7+0.1i), 17
 'c_single', single(0.7+0.1i), []
 'c_default', [0.7+0.1i 0.3-0.2i], []
 'c_default_mat', [0.7+0.1i 0.3; 1/3i 2], []
 'c_default_int', [1+2i 30-400i; 5 6i], []
 'c_default_big', [1e6+1i 2], []
 'c_default_nan', [complex(NaN, 1) 2+3i], []
 'c_default_col', [0.5+1i; 100.25-2i], []
 'c_zero17', complex(0.1, 0), 17
 'r_row17', [0.7 0.3 1/3], 17
 'r_row10', [0.7 0.3 1/3], 10
 'r_row2', [0.7 0.3 1/3], 2
 'r_scalar17', 0.7, 17
 'r_scalar20', 0.1, 20
 'r_mat17', [0.7 0.3; 1/3 2], 17
 'r_mat5', [pi 1e5; -1e-5 2], 5
 'r_int17', [1 2 300], 17
 'r_int3', [1 2 300000], 3
 'r_nan17', [NaN Inf -Inf 1], 17
 'r_fmt', [0.7 0.3], '%10.5f'
 'r_fmt_mat', [1 2; 3 4], '%d,'
 'r_fmt_col', [1; 10], '%d'
 'r_fmt_widths', [1 100; 1000 2], '%g'
 'r_fmt_leftbar', [1; 10], '%-d|'
 'r_fmt_left3', [1; 1000], '%-3d'
 'r_fmt_col82', [1.5; 10], '%8.2f'
 'r_fmt_scalar', 5, '%10.2f'
 'r_fmt_pad', 3.14159, '%08.3f'
 'r_fmt_two', [1 2; 3 4], '%d,%d;'
 'r_fmt_trail', [1 2], '%d  '
 'i_int8', int8([1 -2 3]), 17
 'i_int32_3', int32([123456 7]), 3
 'i_uint8_fmt', uint8(200), '%x'
 'i_int16_def', int16([100 -2]), []
 'i_int16_col', int16([100; -2]), 2
 'l_true17', [true false], 17
 's_single17', single(0.1), 17
 's_single_row', single([0.1 0.7]), 10
 'e_fmt_none_c', 1+2i, 'abc'
 'e_fmt_none_r', 1, 'abc'
 'e_cell_c', 1+2i, {3}
};
for k = 1:size(cases, 1)
    [key, x, p] = cases{k, :};
    u9b_chk(key, @() n2s(x, p));
end

function s = n2s(x, p)
% num2str(x) or num2str(x, p), as its size and its rows with blanks written '_'.
if isempty(p) && isnumeric(p)
    t = num2str(x);
else
    t = num2str(x, p);
end
rows = cell(1, size(t, 1));
for r = 1:size(t, 1)
    rows{r} = strrep(t(r, :), ' ', '_');
end
s = [class(t) ' ' mat2str(size(t)) ' ' strjoin(rows, ' / ')];
end
