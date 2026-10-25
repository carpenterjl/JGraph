function r = k9t_ints(a, b, c, d, e, f, g, h, i)
%K9T_INTS Integer classes, real and not.
arguments
    a (1,1) int8 {mustBeReal}
    b (1,:) uint8 {mustBeReal}
    c (1,1) int16 {mustBeReal}
    d (1,1) uint16 {mustBeReal}
    e (1,1) int32 {mustBeReal}
    f (:,:) uint32 {mustBeReal}
    g (1,1) int64 {mustBeReal}
    h (1,1) uint64 {mustBeReal}
    i (1,1) int32
end
r = double(a) + double(sum(b)) + double(c) + double(d) + double(e) + double(sum(f, 'all')) + double(g) + double(h) + double(i);
end
