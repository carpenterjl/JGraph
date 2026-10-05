function s = u5_group(bg)
% A uifigure button group on one line: each radio or toggle button's Text and Value, oldest first,
% and the selected one (U5 fixtures).
k = get(bg, 'Children');
k = k(end:-1:1);
parts = {};
for i = 1:numel(k)
    if any(strcmp(get(k(i), 'Type'), {'uiradiobutton', 'uitogglebutton'}))
        parts{end + 1} = [get(k(i), 'Text') '=' mat2str(get(k(i), 'Value'))]; %#ok<AGROW>
    end
end
sel = get(bg, 'SelectedObject');
if isempty(sel)
    w = '(none)';
else
    w = get(sel, 'Text');
end
s = [strjoin(parts, ' ') ' sel=' w];
end
