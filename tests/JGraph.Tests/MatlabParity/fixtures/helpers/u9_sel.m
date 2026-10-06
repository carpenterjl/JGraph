function nodes = u9_sel(tr, value)
% Writes a tree's SelectedNodes and reads them back (U9 fixtures).
set(tr, 'SelectedNodes', value);
nodes = get(tr, 'SelectedNodes');
end
