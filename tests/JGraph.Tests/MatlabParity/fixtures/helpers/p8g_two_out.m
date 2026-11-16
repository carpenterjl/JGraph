function r = p8g_two_out(fn)
% Both answers of a two-output call as one char row: the value's class, size and parts, then the
% index (open item 351; ADR 0383).
[v, k] = fn();
if isreal(v)
    r = sprintf('%s%s real %s', class(v), mat2str(size(v)), mat2str(double(v), 17));
else
    r = sprintf('%s%s complex re %s im %s', class(v), mat2str(size(v)), mat2str(real(double(v)), 17), mat2str(imag(double(v)), 17));
end
r = sprintf('%s idx %s', r, mat2str(k));
end
