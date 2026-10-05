function v = u5_setget(h, name, value)
% Sets a property and answers what it then reads as, as text (U5 fixtures).
set(h, name, value);
v = u5_text(get(h, name));
end
