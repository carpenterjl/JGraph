function t = ix_flat(t)
% IX_FLAT  Displayed text as one line: hyperlinks reduced to their text, each line trimmed, blank
%   lines dropped, the rest joined with " / ".
t = regexprep(t, '<a [^>]*>([^<]*)</a>', '$1');
t = regexprep(t, '</?strong>', '');
lines = strtrim(splitlines(string(t)));
lines = lines(strlength(lines) > 0);
t = char(strjoin(lines, ' / '));
t = strrep(t, '|', '/');
end
