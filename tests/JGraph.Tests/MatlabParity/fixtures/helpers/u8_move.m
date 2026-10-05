function s = u8_move(h, p)
% Moves an object to another parent and answers where it then is (U8 fixtures).
set(h, 'Parent', p);
s = u8_chain(h);
end
