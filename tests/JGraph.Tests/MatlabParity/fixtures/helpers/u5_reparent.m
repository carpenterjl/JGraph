function s = u5_reparent(h, p)
% Moves a component to another parent and answers where it then is (U5 fixtures).
set(h, 'Parent', p);
s = u5_kind(h);
end
