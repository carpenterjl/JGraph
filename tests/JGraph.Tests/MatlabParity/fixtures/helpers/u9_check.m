function nodes = u9_check(ct, value)
% Writes a check box tree's CheckedNodes and reads them back (U9 fixtures).
set(ct, 'CheckedNodes', value);
nodes = get(ct, 'CheckedNodes');
end
