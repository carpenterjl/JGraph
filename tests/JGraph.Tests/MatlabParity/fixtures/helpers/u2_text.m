function s = u2_text(v)
% A value as one line for the U2 fixtures: numbers to ten digits, text on one line, no bars.
if isa(v, 'matlab.lang.OnOffSwitchState')
    s = char(v);
elseif ischar(v) && size(v, 1) <= 1
    s = regexprep(v, '\s+', ' ');
elseif isnumeric(v) || islogical(v)
    s = strtrim([class(v) ' ' mat2str(size(v)) ' ' sprintf('%.10g ', v)]);
else
    s = u1_show(v);
end
s = strrep(s, '|', '/');
end
