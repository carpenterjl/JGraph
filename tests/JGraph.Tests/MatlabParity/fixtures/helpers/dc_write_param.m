function y = dc_write_param(x)
% A MATLAB function that writes its parameter (V11): the caller's array must not change, whoever
% called - a JGS caller's reference included.
x(1) = 7;
y = x;
end
