function s = k0_flat(s)
%K0_FLAT One line of text: newlines become ' / ', and HTML links are dropped.
s = regexprep(char(s), '<a [^>]*>|</a>', '');
s = strrep(s, sprintf('\r'), '');
s = strrep(s, newline, ' / ');
end
