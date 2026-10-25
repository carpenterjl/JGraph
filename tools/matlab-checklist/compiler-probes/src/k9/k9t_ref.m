function [x, y] = k9t_ref(x, n)
%K9T_REF An output named as an input: passed by reference.
arguments
    x (1,:) double {mustBeReal}
    n (1,1) double {mustBeReal}
end
x = x * n;
y = numel(x);
end
