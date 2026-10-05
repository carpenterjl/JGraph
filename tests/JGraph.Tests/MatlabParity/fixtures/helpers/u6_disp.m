function s = u6_disp(text)
% What disp printed for an object, without the help link R2025b wraps the class name in (U6 fixtures).
s = strtrim(regexprep(text, '<[^>]*>', ''));
end
