function s = u9_setget(h, name, value, varargin)
% Writes one property and reads it, or the others named, back as text (U9 fixtures).
set(h, name, value);
if isempty(varargin)
    varargin = {name};
end
parts = cell(1, numel(varargin));
for k = 1:numel(varargin)
    parts{k} = [varargin{k} '=' u9_text(get(h, varargin{k}))];
end
s = strjoin(parts, ' ');
end
