function s = u9_texts(holder)
% The nodes of a tree or a node, each with how many it holds, in the order they stand, those
% under a node after it with a dot per level (U9 fixtures).
s = strtrim(walk(holder, 0));
end

function s = walk(h, depth)
s = '';
kids = get(h, 'Children');
for k = 1:numel(kids)
    s = [s sprintf('%s%s(%d) ', repmat('.', 1, depth), get(kids(k), 'Text'), numel(get(kids(k), 'Children')))]; %#ok<AGROW>
    s = [s walk(kids(k), depth + 1)]; %#ok<AGROW>
end
end
