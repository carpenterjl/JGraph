function y = vp_count()
% A counter in a function file, for the clear cases of value_isolation_persist (V5).
persistent n
if isempty(n), n = 0; end
n = n + 1;
y = n;
end
