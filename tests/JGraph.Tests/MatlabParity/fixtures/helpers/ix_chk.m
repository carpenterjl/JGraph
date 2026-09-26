function ix_chk(name, v, rule)
% IX_CHK  Print an interop fixture line: CHK|name|ix_show(v)|rule (exact unless a rule is given).
%   A char value that is already text (an identifier, a display) prints as it is.
if nargin < 3
    rule = 'exact';
end
if ischar(v) && (isrow(v) || isempty(v))
    s = strrep(strrep(v, '|', '/'), newline, ' / ');
else
    s = ix_show(v);
end
fprintf('CHK|%s|%s|%s\n', name, s, rule);
end
