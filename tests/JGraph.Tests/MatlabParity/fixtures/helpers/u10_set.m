function out = u10_set(obj, name, value)
% Writes obj.(name) = value through the dot and answers 'ok', for a u9b_chk line about a write.
obj.(name) = value;
out = 'ok';
end
