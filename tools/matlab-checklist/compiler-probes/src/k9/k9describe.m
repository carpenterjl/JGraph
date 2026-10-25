function d = k9describe(x)
%K9DESCRIBE Say what arrived from the client: class, size, and the value.
d = sprintf('%s %s', class(x), mat2str(size(x)));
if isnumeric(x) && ~isreal(x), d = [d ' complex']; end
if issparse(x), d = [d ' sparse']; end
d = [d ' ' k9value(x)];
end

function s = k9value(x)
if isenum(x)
    s = ['{' strjoin(arrayfun(@char, x(:)', 'UniformOutput', false), ' ') '}'];
elseif isa(x, 'dictionary')
    if ~isConfigured(x)
        s = 'unconfigured';
    else
        k = keys(x);
        v = values(x);
        s = sprintf('%s->%s [%s] -> [%s]', class(k), class(v), k9value(k), k9value(v));
    end
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
    s = ['''' reshape(x, 1, []) ''''];
elseif isstring(x)
    parts = cell(1, numel(x));
    for j = 1:numel(x)
        if ismissing(x(j)), parts{j} = '<missing>'; else, parts{j} = ['"' char(x(j)) '"']; end
    end
    s = ['[' strjoin(parts, ' ') ']'];
elseif iscell(x)
    s = ['{' strjoin(cellfun(@k9describe, x(:)', 'UniformOutput', false), '; ') '}'];
elseif isstruct(x)
    names = fieldnames(x)';
    parts = cell(1, numel(x));
    for j = 1:numel(x)
        fields = cellfun(@(n) [n '=' k9describe(x(j).(n))], names, 'UniformOutput', false);
        parts{j} = ['(' strjoin(fields, ', ') ')'];
    end
    s = ['fields [' strjoin(names, ',') '] ' strjoin(parts, ' ')];
elseif isa(x, 'function_handle')
    s = func2str(x);
elseif isobject(x)
    names = properties(x)';
    if isscalar(x)
        fields = cellfun(@(n) [n '=' k9describe(x.(n))], names, 'UniformOutput', false);
        s = ['(' strjoin(fields, ', ') ')'];
    else
        s = sprintf('array of %d', numel(x));
    end
else
    s = '?';
end
end
