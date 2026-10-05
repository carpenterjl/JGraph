function s = u8_chain(h)
% An object's Type and that of each thing it sits in, up to its figure (U8 fixtures).
s = get(h, 'Type');
p = get(h, 'Parent');
while ~isempty(p) && ~strcmp(get(p, 'Type'), 'root')
    s = [s ' < ' get(p, 'Type')]; %#ok<AGROW>
    p = get(p, 'Parent');
end
end
