function y = vp_clear_other()
% clear functions spares this running function and resets the idle vp_count (V5, #72).
persistent p
if isempty(p), p = 0; end
p = p + 1;
clear functions %#ok<CLFUNC>
p = p + 1;
y = p;
end
