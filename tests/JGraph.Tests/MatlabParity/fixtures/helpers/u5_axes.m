function s = u5_axes(ax)
% A uiaxes on one line: its Type, its parent's Type, its Units and its Position (U5 fixtures).
s = [get(ax, 'Type') ' in ' get(get(ax, 'Parent'), 'Type') ' ' get(ax, 'Units') ' ' mat2str(get(ax, 'Position'), 6)];
end
