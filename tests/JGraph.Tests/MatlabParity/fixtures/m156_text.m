% m156_text.m -- the text conversions item 09 (ADR 0156) reroutes, pinned to R2025b before any of
% them moves: string(x) of numbers, which 09a writes through one scalar formatter; a chain of
% string + over every kind of operand in every position, which 09b builds once; and char of a
% numeric array, which 09c writes straight from its buffer. Text is compared as text: each value is
% wrapped in <> so the comparator cannot read "1e+05" and "100000" as the same number, and the
% large sweeps are bits lines over the files both engines write.

% --- 09a: string of one number ---
one = [0, -0, 1, -1, 7, 9, 10, 11, 99, 100, 101, 999, 1000, 99999, 100000, 100001, 999999, ...
    1000000, 123456789, 2^31 - 1, 2^31, 2^32, 1e15 - 1, 1e15, 1e15 + 1, 2^52, 2^53 - 1, 2^53, ...
    2^53 + 2, 1e16, 1e17, 1e21, 1e22, 1e100, realmax, -realmax, 0.1, 0.2, 0.3, 0.1 + 0.2, 0.5, ...
    1.5, 2.5, -2.5, 0.25, 1/3, 2/3, pi, -pi, exp(1), 12345.678, 123456.78, 1234567.8, 99999.5, ...
    99999.4, 100000.5, 1e4 + 0.1, 100.5, 1e-5, 1.5e-5, 1e-4, 0.0001234, 0.00001234, ...
    1.23456789e-7, 1e-10, 5e-324, realmin, Inf, -Inf, 9.99995, 9.999949, 0.000099999, ...
    123.456789012345678, 0.000123456789];
for k = 1:numel(one)
    chk(sprintf('one_%02d', k), wrap(string(one(k))), 'exact');
end

t = '';
for k = 0:17
    t = [t ' ' char(string(10^k - 1)) ' ' char(string(10^k)) ' ' char(string(10^k + 1))];
end
chk('decades', ['<' strtrim(t) '>'], 'exact');

t = '';
for k = 0:64
    t = [t ' ' char(string(2^k - 1)) ' ' char(string(2^k)) ' ' char(string(-(2^k + 1)))];
end
chk('powers_of_two', ['<' strtrim(t) '>'], 'exact');

t = '';
d = (-40:40) * 0.1;
for k = 1:numel(d)
    t = [t ' ' char(string(d(k)))];
end
chk('tenths', ['<' strtrim(t) '>'], 'exact');

chk('arr_row', wrap(join(string([1 2.5 -0 1e5 1e-5]), ' ')), 'exact');
s = string([1 NaN; -0 2.5]);
chk('arr_mat_size', mat2str(size(s)), 'exact');
chk('arr_mat_missing', sprintf('%d', ismissing(s)), 'exact');
chk('arr_mat_11', wrap(s(1, 1)), 'exact');
chk('arr_mat_21', wrap(s(2, 1)), 'exact');
chk('arr_mat_22', wrap(s(2, 2)), 'exact');
chk('arr_col_size', mat2str(size(string((1:3)'))), 'exact');
chk('arr_empty_size', mat2str(size(string(zeros(0, 3)))), 'exact');

chk('nan_row_missing', sprintf('%d', ismissing(string([1 NaN]))), 'exact');
chk('nan_plus_missing', double(ismissing("a" + string(NaN))), 'exact');
chk('word_nan_missing', double(ismissing(string("NaN"))), 'exact');
chk('word_nan_text', wrap(string("NaN")), 'exact');

chk('cls_int8', wrap(join(string(int8([-128 -1 0 127])), ' ')), 'exact');
chk('cls_uint8', wrap(join(string(uint8([0 255])), ' ')), 'exact');
chk('cls_int16', wrap(join(string(int16([-32768 32767])), ' ')), 'exact');
chk('cls_uint16', wrap(join(string(uint16([0 65535])), ' ')), 'exact');
chk('cls_int32', wrap(join(string(int32([-2147483648 2147483647])), ' ')), 'exact');
chk('cls_uint32', wrap(join(string(uint32([0 4294967295])), ' ')), 'exact');
chk('cls_int64_small', wrap(join(string(int64([-9007199254740992 123456789012 9007199254740992])), ' ')), 'exact');
% An int64 beyond 2^53 is held as a double, and a single is written as num2str writes a double
% where R2025b writes the single's double value to fourteen digits: both older than item 09.
chk('cls_int64_max', wrap(string(intmax('int64'))), 'div=ADR0156');
chk('cls_uint64_max', wrap(string(intmax('uint64'))), 'div=ADR0156');
chk('cls_single', wrap(join(string(single([pi 0.1 1e10 16777217 -2.5 1e-5])), ' ')), 'div=ADR0156');
chk('cls_logical', wrap(join(string([true false true]), ' ')), 'exact');

bitsdir = tempname;
mkdir(bitsdir);
r = mod((1:20000) * 0.6180339887498949, 1);
textbits('sweep_fractions', bitsdir, string(r));
textbits('sweep_small', bitsdir, string(r * 1e-5));
textbits('sweep_billions', bitsdir, string(round(r * 1e9)));
textbits('sweep_hundred_trillions', bitsdir, string(floor(r * 1e14)));
% R2025b takes one precision for a whole array from its largest magnitude, as num2str does for a
% matrix; JGraph takes each element's own, so a fraction beside a larger one loses digits here.
chk('array_precision', wrap(join(string([9016.9943749474514 12345.5 -4.2520534963913263]), ' ')), 'div=ADR0156');
chk('array_precision_small', wrap(join(string([-29.144008230214052 -123.25]), ' ')), 'div=ADR0156');
textbits('sweep_tenths', bitsdir, string((-20000:20000) * 0.1));

% --- 09b: a chain of string + over every kind of operand in every position ---
ops = {"ab", ["x" "y"], ["p"; "q"], 'cd', string(NaN), 7, NaN, true, ['ef'; 'gh'], {'u'; 'v'}, ["x" "y" "z"]};
names = {'str', 'row', 'col', 'chr', 'mis', 'num', 'nan', 'lgc', 'cmx', 'cel', 'row3'};
textish = [true true true false true false false false false false true];
for i = 1:numel(ops)
    for j = 1:numel(ops)
        for k = 1:numel(ops)
            if ~(textish(i) || textish(j) || textish(k))
                continue;
            end
            try
                t = show(ops{i} + ops{j} + ops{k});
            catch
                t = 'ERR';
            end
            % A char row meeting a number, a logical, a char matrix, a cell or another char row under +
            % joins text in JGraph where R2025b adds code points: older than item 09.
            rule = 'exact';
            if (strcmp(names{i}, 'chr') && ~textish(j)) || (strcmp(names{j}, 'chr') && ~textish(i))
                rule = 'div=ADR0156';
            end
            chk(sprintf('chain_%s_%s_%s', names{i}, names{j}, names{k}), ['<' t '>'], rule);
        end
    end
end

ids = [1 22 NaN 333];
sv = ["a"; "b"; string(NaN); "d"];
chk('chain4_keys', ['<' show("R" + string(ids') + "-" + sv) '>'], 'exact');
chk('chain4_numeric_head', ['<' show(1 + 2 + "a" + 3) '>'], 'exact');
chk('chain4_char_head', ['<' show('x' + 1 + "y" + 'z') '>'], 'div=ADR0156');
chk('chain5_cells', ['<' show({'a'; 'b'} + "-" + ["1" "2"] + "." + 7) '>'], 'exact');
x = "q";
chk('chain_parens', ['<' show(x + ("a" + "b") + x) '>'], 'exact');
chk('chain_nested_right', ['<' show("a" + ("b" + ("c" + "d")) + "e") '>'], 'exact');
% The missing string is a sentinel text in JGraph, so a chain that spells it is missing: older
% than item 09, and the fused chain keeps it.
chk('chain_sentinel_text', ['<' show("<miss" + "ing>" + "x" + "y") '>'], 'div=ADR0156');

global touched
touched = 0;
try
    r = ["a" "b"] + ["c" "d" "e"] + touch();
    chk('err_order_threw', 0, 'exact');
catch err
    chk('err_order_threw', 1, 'exact');
end
chk('err_order_touched', touched, 'exact');
try
    r = ["a" "b"] + ["c" "d" "e"] + no_such_function_m156();
    chk('err_throwing_leaf', 0, 'exact');
catch err
    % The size error comes first, so the message never names the function the chain did not reach.
    chk('err_throwing_leaf', double(~contains(err.message, 'no_such_function_m156')), 'exact');
end

% --- 09c: char of a numeric array ---
codes = {[65 66 67], [65 65.4 65.5 65.9 66.5], [955 8364 20320 65 0], [65535 65536 65537 70000], ...
    -1, [65 NaN], [65 Inf], 1e10, [72 73 74; 75 76 77], (65:67)', zeros(0, 3), zeros(3, 0), [], ...
    int8([72 -1]), uint16([955 8364]), single([72.7 73.2]), [true false], 65 + mod(7 + (0:39), 26)};
cnames = {'abc', 'frac', 'unicode', 'wide', 'negative', 'nan', 'inf', 'huge', 'matrix', 'column', ...
    'empty_0x3', 'empty_3x0', 'empty', 'int8', 'uint16', 'single', 'logical', 'charmatrix_row'};
% R2025b saturates a code into [0, 65535] and refuses a logical; JGraph casts to int and keeps the
% low sixteen bits, answers a logical's 0 and 1, and makes char of a 0-by-3 array 0-by-0. All older
% than item 09, and 09c keeps the cast exactly (no range check).
cdiv = {'wide', 'negative', 'inf', 'huge', 'empty_0x3', 'int8', 'logical'};
for k = 1:numel(codes)
    try
        c = char(codes{k});
        t = mat2str(size(c));
        if ~isempty(c)
            t = [t ':' sprintf('%d,', double(c))];
        end
    catch
        t = 'ERR';
    end
    rule = 'exact';
    if any(strcmp(cnames{k}, cdiv))
        rule = 'div=ADR0156';
    end
    chk(['char_' cnames{k}], ['<' t '>'], rule);
end

function t = wrap(s)
t = ['<' char(s) '>'];
end

function t = show(v)
if isstring(v)
    parts = cell(1, numel(v));
    for i = 1:numel(v)
        if ismissing(v(i))
            parts{i} = '<M>';
        else
            parts{i} = char(v(i));
        end
    end
    t = sprintf('string%s:%s', mat2str(size(v)), strjoin(parts, ','));
elseif ischar(v)
    t = sprintf('char%s:%s', mat2str(size(v)), reshape(v, 1, []));
else
    t = sprintf('%s%s:%s', class(v), mat2str(size(v)), sprintf('%g,', double(v)));
end
end

function s = touch()
global touched
touched = 1;
s = "t";
end

function chk(name, v, rule)
if ischar(v)
    fprintf('CHK|%s|%s|%s\n', name, v, rule);
else
    fprintf('CHK|%s|%.17g|%s\n', name, double(v), rule);
end
end

function textbits(name, folder, s)
p = fullfile(folder, [name '.txt']);
fid = fopen(p, 'w');
fprintf(fid, '%s\n', s(:));
fclose(fid);
fprintf('CHK|%s|file:%s|bits\n', name, p);
end
