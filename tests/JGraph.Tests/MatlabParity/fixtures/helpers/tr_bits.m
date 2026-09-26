function s = tr_bits(v)
% TR_BITS  A digest of an array's bit patterns (temporary_reuse.m): two position-weighted sums
% over the hexadecimal spelling of every element's own bits (num2hex), exact in doubles, so an
% element that differs by one bit changes the digest. Shape is not part of it; -0 and 0, a NaN's
% payload and a subnormal are.
h = num2hex(double(v(:)));
d = double(h(:));
p = (1:numel(d))';
s = sprintf('%.0f_%.0f_%d', sum(d .* (mod(p, 251) + 1)), sum(d .* (mod(p, 1009) + 1)), numel(v));
end
