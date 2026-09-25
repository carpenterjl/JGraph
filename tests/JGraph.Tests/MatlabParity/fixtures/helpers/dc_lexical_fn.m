function s = dc_lexical_fn(m)
% A MATLAB function whose built-in calls mean what they mean in MATLAB whoever calls it (V11): the
% same four answers dc_find.m prints, joined into one char row for a caller that cannot print them.
[u, ia] = unique([3 1 3]);
s = sprintf('%s;%s;%s;%s;%s;%d', mat2str(find(m)), mat2str(find(m, 1)), mat2str(u), mat2str(ia), ...
    sprintf('%d,', [1 2 3]), double(sprintf('\t')));
end
