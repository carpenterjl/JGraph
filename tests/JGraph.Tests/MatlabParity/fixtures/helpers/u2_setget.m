function v = u2_setget(h, name, value)
% Sets a property and answers what it reads back as (U2 fixtures).
set(h, name, value);
v = get(h, name);
end
