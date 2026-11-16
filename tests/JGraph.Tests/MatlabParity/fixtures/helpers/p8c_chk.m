function p8c_chk(name, fn, rule, places)
% One CHK line as p7d_chk prints it, but a char answer as its codes (a code past 127 or a NUL
% prints safely), an integer answer through mat2str of its own class, and an N-D answer's
% elements as one row after its size (open items 346 and 349, ADR 0386). The rule column is
% given (default 'exact'), and so are the places mat2str writes (default 17).
if nargin < 3
    rule = 'exact';
end
if nargin < 4
    places = 17;
end
lastwarn('');
try
    v = fn();
    w = v;
    if (isnumeric(v) || islogical(v) || ischar(v)) && ndims(v) > 2
        w = reshape(v, 1, []);
    end
    if ischar(v)
        r = sprintf('char%s %s', mat2str(size(v)), mat2str(double(w)));
    elseif isstring(v)
        r = sprintf('string%s %s', mat2str(size(v)), strjoin(cellstr(v), ','));
    elseif isstruct(v)
        r = sprintf('struct%s', mat2str(size(v)));
    elseif ~isnumeric(v) && ~islogical(v)
        r = sprintf('%s%s', class(v), mat2str(size(v)));
    elseif isreal(v) && isinteger(v)
        r = sprintf('%s%s real %s', class(v), mat2str(size(v)), mat2str(w));
    elseif isreal(v)
        r = sprintf('%s%s real %s', class(v), mat2str(size(v)), mat2str(double(w), places));
    else
        r = sprintf('%s%s complex re %s im %s', class(v), mat2str(size(v)), mat2str(real(double(w)), places), mat2str(imag(double(w)), places));
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
