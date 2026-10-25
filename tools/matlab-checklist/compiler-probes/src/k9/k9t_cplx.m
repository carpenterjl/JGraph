function r = k9t_cplx(a, b, c)
%K9T_CPLX Doubles that may be complex, and singles.
arguments
    a (1,1) double
    b (:,1) double
    c (1,1) single
end
r = a + sum(b) + double(c);
end
