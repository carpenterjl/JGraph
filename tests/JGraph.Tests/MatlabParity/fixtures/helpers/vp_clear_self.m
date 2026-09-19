function y = vp_clear_self(n)
% clear <own name> while executing (V5, #72).
persistent p
if isempty(p), p = 0; end
p = p + 1;
if n
    clear vp_clear_self
end
y = p;
end
