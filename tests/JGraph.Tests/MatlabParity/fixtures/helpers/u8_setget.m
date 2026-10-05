function v = u8_setget(h, name, value, other)
% Writes one property and reads it, or another, back (U8 fixtures).
set(h, name, value);
if nargin < 4
    other = name;
end
v = get(h, other);
end
