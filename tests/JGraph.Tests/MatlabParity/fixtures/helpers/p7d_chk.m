function p7d_chk(name, fn, rule, digits)
% One CHK line for a numeric answer, as p6c_chk prints it (class, size, whether it is real, the
% real and imaginary parts as mat2str texts; a warning's identifier after it; an error as its
% identifier and message), with the rule column given (default 'exact') and the digits mat2str
% writes (default 17), so a row can be a divergence or read past the last bits (open item 341,
% ADR 0377).
if nargin < 3
    rule = 'exact';
end
if nargin < 4
    digits = 17;
end
lastwarn('');
try
    v = fn();
    if ischar(v)
        r = sprintf('char%s ''%s''', mat2str(size(v)), v);
    elseif isstring(v)
        r = sprintf('string%s %s', mat2str(size(v)), strjoin(cellstr(v), ','));
    elseif isstruct(v)
        r = sprintf('struct%s', mat2str(size(v)));
    elseif ~isnumeric(v) && ~islogical(v)
        r = sprintf('%s%s', class(v), mat2str(size(v)));
    elseif isreal(v)
        r = sprintf('%s%s real %s', class(v), mat2str(size(v)), mat2str(double(v), digits));
    else
        r = sprintf('%s%s complex re %s im %s', class(v), mat2str(size(v)), mat2str(real(double(v)), digits), mat2str(imag(double(v)), digits));
    end
    [~, wid] = lastwarn;
    if ~isempty(wid)
        r = [r ' WARN ' wid];
    end
    fprintf('CHK|%s|%s|%s\n', name, strrep(r, '|', '/'), rule);
catch err
    fprintf('CHK|%s|%s|%s\n', name, err.identifier, rule);
    fprintf('CHK|%s_msg|%s|%s\n', name, strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/'), rule);
end
end
