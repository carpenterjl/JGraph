function s = u3_state(group, buttons)
% The Value of each button, by Tag, and the Tag of the group's SelectedObject (U3 fixtures).
parts = cell(1, numel(buttons));
for k = 1:numel(buttons)
    parts{k} = sprintf('%s:%s', get(buttons(k), 'Tag'), mat2str(get(buttons(k), 'Value')));
end
s = sprintf('%s selected=%s', strjoin(parts, ' '), u3_tag(get(group, 'SelectedObject')));
end
