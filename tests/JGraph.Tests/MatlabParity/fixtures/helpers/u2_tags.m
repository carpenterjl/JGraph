function s = u2_tags(k)
% The Tags of a list of handles, in order, on one line (U2 fixtures). Untagged objects are left
% out: R2025b keeps an annotation pane it tags scribeOverlay beside every set of axes, and JGraph's
% findall reaches an axes' rulers and grid — furniture of each engine's own, not what a script made.
parts = {};
for i = 1:numel(k)
    tag = get(k(i), 'Tag');
    if ~isempty(tag) && ~strcmp(tag, 'scribeOverlay')
        parts{end + 1} = tag; %#ok<AGROW>
    end
end
if isempty(parts)
    s = '(none)';
else
    s = strjoin(parts, ' ');
end
end
