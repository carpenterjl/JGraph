function u9b_num
% U9b probe: how jsonencode writes a double, a single and an integer, against sprintf's forms.
% Headless.
v = [0 1 9 10 99 100 999 1000 99999 100000 999999 1e6 1234567 9999999 1e7 10000001 12345678 99999999 1e8 123456789 1e9 ...
    1e10 1e11 1e12 1e13 1e14 999999999999999 1e15 1e16 2^53 2^60 1e20 1e21 1e22 1e100 1e300 realmax ...
    0.5 0.25 0.1 0.2 0.3 0.01 0.001 0.0012 0.0001 0.00012345 1e-5 1.5e-5 1e-6 1e-7 1.234e-8 1e-10 1e-100 1e-300 realmin 5e-324 1e-320 ...
    1.5 2.5 123.456 1234567.5 12345678.5 123456789.25 1e6 + 0.1 1e7 + 0.5 1e14 + 0.5 1e15 + 0.5 4503599627370495.5 ...
    1/3 2/3 pi exp(1) sqrt(2) 0.1 + 0.2 1 - eps 1 + eps 100.25 1e5 + 1/3 1e7 / 3 1e8 / 3 1e15 / 3 1e16 / 3 ...
    -1 -1e7 -123456789 -0.5 -1e-5 -1/3 -1e15];
for k = 1:numel(v)
    x = v(k);
    fprintf('%-26s %-26s %-26s => %s | %s\n', sprintf('%.17g', x), sprintf('%.15g', x), num2str(x, 17), jsonencode(x), jsonencode(-x));
end
s = single([0.1 1/3 pi 1e7 12345678 1e10 1.5e-5 16777217 3e38 1e-38 2.5 100.5]);
for k = 1:numel(s)
    fprintf('single %-14s %-14s => %s\n', sprintf('%.9g', s(k)), sprintf('%.7g', s(k)), jsonencode(s(k)));
end
ints = {int8(127), int32(123456789), int32(-2147483648), uint32(4294967295), int64(1e15), int64(123456789012), uint16(65535)};
for k = 1:numel(ints)
    fprintf('int %s %s => %s\n', class(ints{k}), num2str(ints{k}), jsonencode(ints{k}));
end
fprintf('array %s\n', jsonencode([1e15 0.5 123456789 1/3 1e-5]));
fprintf('logical-int mix %s\n', jsonencode({int8(1), true, 1e7}));
end
