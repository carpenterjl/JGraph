function u9b_chkdiv(name, fn, adr)
% u9b_chk for a line that is a recorded divergence: its rule is div=<adr>, and the recording
% carries what JGraph prints (U9b fixtures).
lastwarn('');
try
    v = fn();
    fprintf('CHK|%s|%s|div=%s\n', name, u9b_text(v), adr);
catch err
    fprintf('CHK|%s|%s|div=%s\n', name, err.identifier, adr);
    fprintf('CHK|%s_msg|%s|div=%s\n', name, strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/'), adr);
end
end
