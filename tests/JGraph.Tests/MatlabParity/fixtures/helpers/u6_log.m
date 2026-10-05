function s = u6_log()
% The value-isolation call log so far, emptied as it is read (U6 fixtures).
global vlog_text
s = vlog_text;
vlog_text = '';
if isempty(s)
    s = '(none)';
end
end
