function s = k17_show(v)
%K17_SHOW One line for a value: its class, size and contents, nested for cells and structs (stage K17).
sz = mat2str(size(v));
if ischar(v)
    if size(v, 1) <= 1
        s = sprintf('char %s ''%s''', sz, escapeText(v));
    else
        rows = cellstr(v);
        s = sprintf('char %s [%s]', sz, strjoin(cellfun(@(r) ['''' escapeText(r) ''''], rows', 'UniformOutput', false), '; '));
    end
elseif isstring(v)
    parts = arrayfun(@(e) showString(e), v(:)', 'UniformOutput', false);
    s = sprintf('string %s [%s]', sz, strjoin(parts, ', '));
elseif (islogical(v) || isnumeric(v)) && issparse(v)
    [i, j, x] = find(v);
    s = sprintf('sparse %s %s nnz=%d [%s]', class(v), sz, nnz(v), strjoin(arrayfun(@(a, b, c) sprintf('(%d,%d)=%s', a, b, num(c)), i(:).', j(:).', x(:).', 'UniformOutput', false), ' '));
elseif islogical(v)
    s = sprintf('logical %s %s', sz, mat2str(v));
elseif isnumeric(v)
    if numel(v) <= 16
        s = sprintf('%s %s %s', class(v), sz, nums(v));
    else
        s = sprintf('%s %s first=%s last=%s sum=%s', class(v), sz, num(v(1)), num(v(end)), num(sum(double(v(:)))));
    end
elseif iscell(v)
    parts = cellfun(@k17_show, v(:)', 'UniformOutput', false);
    if numel(v) > 8, parts = [parts(1:8), {'...'}]; end
    s = sprintf('cell %s {%s}', sz, strjoin(parts, '; '));
elseif isstruct(v)
    names = fieldnames(v)';
    if numel(v) == 1
        parts = cellfun(@(n) sprintf('%s=%s', n, k17_show(v.(n))), names, 'UniformOutput', false);
        s = sprintf('struct %s {%s}', sz, strjoin(parts, '; '));
    else
        s = sprintf('struct %s fields [%s]', sz, strjoin(names, ', '));
        if numel(v) <= 4
            elems = arrayfun(@(e) k17_show(e), v(:)', 'UniformOutput', false);
            s = sprintf('%s {%s}', s, strjoin(elems, ' | '));
        end
    end
elseif isdatetime(v)
    s = sprintf('datetime %s [%s]', sz, strjoin(cellstr(string(v(:)', 'yyyy-MM-dd HH:mm:ss.SSS')), ', '));
elseif isa(v, 'function_handle')
    s = sprintf('function_handle %s', func2str(v));
elseif isobject(v)
    s = sprintf('%s %s', class(v), sz);
else
    s = sprintf('%s %s', class(v), sz);
end
end

function t = nums(v)
if ~isreal(v)
    % v(:).' and not v(:)': a quote would conjugate the values it lists.
    t = ['[' strjoin(arrayfun(@(e) sprintf('%s%s%si', num(real(e)), signOf(imag(e)), num(abs(imag(e)))), v(:).', 'UniformOutput', false), ' ') ']'];
elseif isempty(v)
    t = '[]';
else
    t = ['[' strjoin(arrayfun(@num, v(:).', 'UniformOutput', false), ' ') ']'];
end
end

function t = signOf(e)
if e < 0, t = '-'; else, t = '+'; end
end

function t = num(e)
if isinteger(e)
    t = sprintf('%d', e);
elseif isnan(e)
    t = 'NaN';
elseif isinf(e)
    if e > 0, t = 'Inf'; else, t = '-Inf'; end
elseif e == round(e) && abs(e) < 1e15
    t = sprintf('%d', e);
else
    t = sprintf('%.10g', e);
end
end

function t = showString(e)
if ismissing(e)
    t = '<missing>';
else
    t = ['"' escapeText(char(e)) '"'];
end
end

function t = escapeText(c)
t = strrep(strrep(c, char(13), '\r'), char(10), '\n');
t = strrep(t, char(9), '\t');
over = t > 127;
if any(over)
    codes = arrayfun(@(ch) sprintf('\\u%04x', double(ch)), t(over), 'UniformOutput', false);
    out = cell(1, numel(t));
    out(~over) = num2cell(t(~over));
    out(over) = codes;
    t = [out{:}];
end
end
