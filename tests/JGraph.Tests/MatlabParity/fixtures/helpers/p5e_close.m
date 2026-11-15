function p5e_close(name, v, tol)
% One CHK line per element of a numeric answer, its real and its imaginary part, each held to a
% relative tolerance: for answers two engines reach by different rounding (open item 321).
v = double(v);
fprintf('CHK|%s_shape|%s %d|exact\n', name, mat2str(size(v)), isreal(v));
for k = 1:numel(v)
    fprintf('CHK|%s_%d_re|%.17g|rel=%g\n', name, k, real(v(k)), tol);
    fprintf('CHK|%s_%d_im|%.17g|rel=%g\n', name, k, imag(v(k)), tol);
end
end
