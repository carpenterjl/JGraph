function p7c_chk(name, fn, full)
% One CHK line for an answer: its class, size and values (a char, string or cellstr as its text);
% an error is its identifier, and with FULL (the default) a second line with its message
% (open items 340, 341; ADR 0379).
if nargin < 3
    full = true;
end
try
    v = fn();
    if ischar(v)
        r = sprintf('char%s ''%s''', mat2str(size(v)), v);
    elseif isstring(v)
        r = sprintf('string%s %s', mat2str(size(v)), strjoin(cellstr(v(:)'), ','));
    elseif iscell(v)
        r = sprintf('cell%s %s', mat2str(size(v)), strjoin(v(:)', ','));
    elseif isreal(v)
        r = sprintf('%s%s real %s', class(v), mat2str(size(v)), mat2str(double(v), 17));
    else
        r = sprintf('%s%s complex re %s im %s', class(v), mat2str(size(v)), mat2str(real(double(v)), 17), mat2str(imag(double(v)), 17));
    end
    fprintf('CHK|%s|%s|exact\n', name, strrep(r, '|', '/'));
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    if full
        fprintf('CHK|%s_msg|%s|exact\n', name, strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/'));
    end
end
end
