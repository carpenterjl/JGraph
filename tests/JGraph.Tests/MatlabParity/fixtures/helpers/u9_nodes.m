function s = u9_nodes(nodes)
% Some tree nodes by their texts, in order, with the count (U9 fixtures). How the list is shaped
% is left out: a column in R2025b and a row here.
if isempty(nodes)
    s = 'none';
    return;
end
parts = cell(1, numel(nodes));
for k = 1:numel(nodes)
    parts{k} = get(nodes(k), 'Text');
end
s = sprintf('%d [%s]', numel(nodes), strjoin(parts, ','));
end
