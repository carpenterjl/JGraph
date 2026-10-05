function o = u6_setidx(o, name, k, v)
% Writes one element of a property from outside every class (U6 fixtures).
o.(name)(k) = v;
end
