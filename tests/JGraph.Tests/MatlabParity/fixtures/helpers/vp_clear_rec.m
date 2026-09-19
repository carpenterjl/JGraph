function y = vp_clear_rec(depth)
% clear functions at every level of a recursion: no level's persistent is reset (V5, #72).
persistent p
if isempty(p), p = zeros(1, 2); end
p(1) = p(1) + 1;
if depth > 0
    clear functions %#ok<CLFUNC>
    vp_clear_rec(depth - 1);
end
p(2) = p(2) + 10;
y = p;
end
