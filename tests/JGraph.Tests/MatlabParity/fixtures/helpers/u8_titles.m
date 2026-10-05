function s = u8_titles(h)
% The titles, texts or tooltips of an object's children, in the order Children lists them (U8 fixtures).
c = get(h, 'Children');
parts = cell(1, numel(c));
for k = 1:numel(c)
    if isprop(c(k), 'Title')
        parts{k} = get(c(k), 'Title');
    elseif isprop(c(k), 'Text')
        parts{k} = get(c(k), 'Text');
    elseif isprop(c(k), 'Tooltip')
        parts{k} = get(c(k), 'Tooltip');
    else
        parts{k} = ['<' get(c(k), 'Type') '>'];
    end
end
s = strjoin(parts, ',');
end
