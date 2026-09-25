% A MATLAB script run from JGS that leaves anonymous functions written in MATLAB in the caller's
% workspace (V11): called later from JGS, x(1) is still the first element and [a, a] still joins.
first = @(x) x(1);
twice = @(a) [a, a];
