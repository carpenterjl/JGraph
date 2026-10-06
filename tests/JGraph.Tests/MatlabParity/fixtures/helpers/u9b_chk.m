function u9b_chk(name, fn)
% One CHK line for what fn answers, as U9b's text, or two for the error it raises; a warning is
% noted after the answer (U9b fixtures).
lastwarn('');
try
    v = fn();
    r = u9b_text(v);
    [~, wid] = lastwarn;
    if ~isempty(wid)
        r = [r ' WARN ' wid];
    end
    fprintf('CHK|%s|%s|exact\n', name, r);
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', name, strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/'));
end
end
