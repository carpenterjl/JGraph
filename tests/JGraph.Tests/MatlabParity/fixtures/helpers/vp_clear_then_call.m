function y = vp_clear_then_call(k)
% The callback of vp_clear_in_callback (V5, #72).
clear functions %#ok<CLFUNC>
y = vp_clear_in_callback(k);
end
