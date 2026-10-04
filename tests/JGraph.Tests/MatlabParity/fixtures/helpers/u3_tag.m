function s = u3_tag(h)
% The Tag of a handle, or a word for none or several (U3 fixtures).
if isempty(h)
    s = '(none)';
elseif numel(h) > 1
    s = sprintf('(%d handles)', numel(h));
else
    s = get(h, 'Tag');
end
end
