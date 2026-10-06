function s = u9_cmtype(h, value)
% Writes an object's ContextMenu and answers the Type of what it then holds, or 'none' (U9 fixtures).
set(h, 'ContextMenu', value);
menu = get(h, 'ContextMenu');
if isempty(menu)
    s = 'none';
else
    s = get(menu, 'Type');
end
end
