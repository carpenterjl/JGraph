function v = u5_setgetraw(h, name, value)
% Sets a property and answers what it then reads as (U5 fixtures).
set(h, name, value);
v = get(h, name);
end
