function p6c_chk(name, fn)
% One CHK line for a numeric answer: class, size, whether it is real, and its real and imaginary
% parts as two mat2str texts, so a zero-imaginary complex value shows as complex and the line does
% not lean on how mat2str spells a complex infinity; a warning's identifier is noted after the
% answer; an error is two lines, its identifier and its message (open items 330, 333; ADR 0370).
lastwarn('');
try
    v = fn();
    if ischar(v)
        r = sprintf('char%s ''%s''', mat2str(size(v)), v);
    elseif isreal(v)
        r = sprintf('%s%s real %s', class(v), mat2str(size(v)), mat2str(double(v), 17));
    else
        r = sprintf('%s%s complex re %s im %s', class(v), mat2str(size(v)), mat2str(real(double(v)), 17), mat2str(imag(double(v)), 17));
    end
    [~, wid] = lastwarn;
    if ~isempty(wid)
        r = [r ' WARN ' wid];
    end
    fprintf('CHK|%s|%s|exact\n', name, strrep(r, '|', '/'));
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', name, strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/'));
end
end
