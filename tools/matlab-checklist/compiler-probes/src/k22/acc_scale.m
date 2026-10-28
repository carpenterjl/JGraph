function y = acc_scale(x, k)
%ACC_SCALE The acceptance set's matrix answer: x scaled by k (stage K22).
arguments
    x double
    k (1,1) double
end
y = x * k;
end
