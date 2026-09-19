function y = vp_clear_in_callback(n)
% A callback clears functions and re-enters the owner (V5, #72).
persistent p
if isempty(p), p = 0; end
p = p + 1;
if n
    y = cellfun(@(k) vp_clear_then_call(k), {0});
else
    y = p;
end
end
