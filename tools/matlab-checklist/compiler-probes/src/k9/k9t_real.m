function r = k9t_real(a, b, c, d)
%K9T_REAL Real doubles: a scalar, a row, a matrix and a fixed 2x3x4 array.
arguments
    a (1,1) double {mustBeReal}
    b (1,:) double {mustBeReal}
    c (:,:) double {mustBeReal}
    d (2,3,4) double {mustBeReal}
end
r = a + sum(b) + sum(c, 'all') + sum(d, 'all');
end
