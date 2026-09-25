function y = dc_local_alias(x)
% An alias written inside a MATLAB function (V11): the parameter keeps its value.
w = x;
w(2) = 9;
y = x;
end
