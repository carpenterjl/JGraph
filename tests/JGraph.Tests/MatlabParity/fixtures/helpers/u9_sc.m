function s = u9_sc(h)
% A component's StyleConfigurations as one line: its size and variable names, then each row's
% target word and index (U9 fixtures). A node target is named by its text. The Style column is
% left out: an object array in R2025b and a cell of objects here.
T = get(h, 'StyleConfigurations');
s = sprintf('%dx%d vars=%s', size(T, 1), size(T, 2), strjoin(T.Properties.VariableNames, ','));
for k = 1:size(T, 1)
    target = char(string(T.Target(k)));
    index = T.TargetIndex{k};
    if any(strcmp(target, {'node', 'subtree'}))
        shown = u9_nodes(index);
    else
        shown = u9_text(index);
    end
    s = sprintf('%s / %d: %s %s', s, k, target, shown);
end
end
