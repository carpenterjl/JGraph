function y = vp_clear_sibling(n)
% Another function clears while this one is suspended lower on the stack (V5, #72).
persistent p
if isempty(p), p = 0; end
p = p + 1;
if n
    vp_do_clear();
end
y = p;
end
