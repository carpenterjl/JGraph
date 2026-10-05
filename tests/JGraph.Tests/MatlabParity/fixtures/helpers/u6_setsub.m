function o = u6_setsub(o, name, field, v)
% Writes one field of a struct held by a property, from outside every class (U6 fixtures).
o.(name).(field) = v;
end
