function y = dc_write_field(s)
% A MATLAB function writing into a field of a struct it was handed (V11): its parameter is a value,
% the nested write detaches the field's array, and the caller's struct - and every alias of it -
% keeps [1 2 3].
s.f(1) = 9;
y = s;
end
