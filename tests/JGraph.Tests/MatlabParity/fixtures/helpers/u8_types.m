function s = u8_types(h)
% The Types of some objects, in order (U8 fixtures). How the list is shaped is left out: Children is
% a column in R2025b and a row here.
parts = cell(1, numel(h));
for k = 1:numel(h)
    parts{k} = get(h(k), 'Type');
end
s = strjoin(parts, ',');
end
