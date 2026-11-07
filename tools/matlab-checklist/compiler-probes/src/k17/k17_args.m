function y = k17_args(x, k)
%K17_ARGS Scales a positive row by k, both checked by an arguments block (stage K17).
arguments
    x (1, :) double {mustBePositive}
    k (1, 1) double = 2
end
y = x * k;
end
