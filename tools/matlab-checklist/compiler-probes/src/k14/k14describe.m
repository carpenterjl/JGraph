function d = k14describe(x)
%K14DESCRIBE Say what arrived from the client: class, size, and the value.
d = sprintf('%s %s', class(x), mat2str(size(x)));
if isnumeric(x) && ~isreal(x), d = [d ' complex']; end
if issparse(x), d = [d ' sparse']; end
d = [d ' ' k14value(x)];
end

function s = k14value(x)
if isenum(x)
    s = ['{' strjoin(arrayfun(@char, x(:)', 'UniformOutput', false), ' ') '}'];
elseif isdatetime(x)
    x.Format = 'uuuu-MM-dd HH:mm:ss.SSS';
    s = ['[' strjoin(cellstr(x(:)'), ' ') '] zone=''' x.TimeZone ''''];
elseif isnumeric(x) || islogical(x)
    x = full(x);
    parts = cell(1, numel(x));
    for j = 1:numel(x)
        v = x(j);
        if isinteger(v) || islogical(v)
            parts{j} = sprintf('%d', v);
        elseif isreal(v)
            parts{j} = sprintf('%.17g', v);
        else
            parts{j} = sprintf('%.17g%+.17gi', real(v), imag(v));
        end
    end
    s = ['[' strjoin(parts, ' ') ']'];
elseif ischar(x)
    s = ['''' k14ascii(reshape(x, 1, [])) ''''];
elseif isstring(x)
    parts = cell(1, numel(x));
    for j = 1:numel(x)
        if ismissing(x(j)), parts{j} = '<missing>'; else, parts{j} = ['"' k14ascii(char(x(j))) '"']; end
    end
    s = ['[' strjoin(parts, ' ') ']'];
elseif iscell(x)
    s = ['{' strjoin(cellfun(@k14describe, x(:)', 'UniformOutput', false), '; ') '}'];
elseif isstruct(x)
    names = fieldnames(x)';
    parts = cell(1, numel(x));
    for j = 1:numel(x)
        fields = cellfun(@(n) [n '=' k14describe(x(j).(n))], names, 'UniformOutput', false);
        parts{j} = ['(' strjoin(fields, ', ') ')'];
    end
    s = ['fields [' strjoin(names, ',') '] ' strjoin(parts, ' ')];
else
    s = ['<' class(x) '>'];
end
end

function s = k14ascii(s)
% Characters past ASCII as \uXXXX, so the recording is one encoding everywhere.
out = '';
for c = s
    if c > 127 || c < 32
        out = [out sprintf('\\u%04x', double(c))]; %#ok<AGROW>
    else
        out = [out c]; %#ok<AGROW>
    end
end
s = out;
end
