function p5e_chk(name, fn)
% u9b_chk for a numeric answer whose parts may be Inf or NaN: class, size, and the real and
% imaginary parts as two mat2str texts, so the line does not lean on how mat2str spells a complex
% infinity; a warning is noted after the answer (open item 321).
lastwarn('');
try
    v = fn();
    if isreal(v)
        r = sprintf('%s%s re %s', class(v), mat2str(size(v)), mat2str(double(v), 17));
    else
        r = sprintf('%s%s re %s im %s', class(v), mat2str(size(v)), mat2str(real(double(v)), 17), mat2str(imag(double(v)), 17));
    end
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
