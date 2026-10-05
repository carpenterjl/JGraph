function s = u5_kind(h)
% A component on one line: its Type, its parent's Type, and its Position (U5 fixtures). A grid, and
% anything in one, is placed by the grid some time after it is made in R2025b and at once here, so
% its Position is left out; u5_grid waits for those.
kind = get(h, 'Type');
owner = get(get(h, 'Parent'), 'Type');
if strcmp(kind, 'uigridlayout') || strcmp(owner, 'uigridlayout')
    s = [kind ' in ' owner];
else
    s = [kind ' in ' owner ' at ' mat2str(get(h, 'Position'))];
end
end
