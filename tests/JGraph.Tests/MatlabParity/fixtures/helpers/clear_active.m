function y = clear_active(n)
% clear functions while this function is executing must not reset its own persistent (appendix A #72).
persistent p
if isempty(p), p = 0; end
p = p + 1;
if n
    clear functions %#ok<CLFUNC>
    y = clear_active(0);
else
    y = p;
end
end
