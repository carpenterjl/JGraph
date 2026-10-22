function s = k0_show(v)
%K0_SHOW A one-line description of a value: class, size and contents where short.
sz = mat2str(size(v));
if ischar(v)
    s = sprintf('char %s ''%s''', sz, k0_flat(v));
elseif isstring(v)
    s = sprintf('string %s [%s]', sz, strjoin(cellstr(v(:)'), ', '));
elseif islogical(v) || isnumeric(v)
    if numel(v) <= 12
        s = sprintf('%s %s %s', class(v), sz, mat2str(v));
    else
        s = sprintf('%s %s', class(v), sz);
    end
elseif iscell(v)
    parts = cellfun(@(e) k0_show(e), v(:)', 'UniformOutput', false);
    if numel(v) > 6, parts = [parts(1:6), {'...'}]; end
    s = sprintf('cell %s {%s}', sz, strjoin(parts, '; '));
elseif isstruct(v)
    s = sprintf('struct %s fields [%s]', sz, strjoin(fieldnames(v)', ', '));
else
    s = sprintf('%s %s', class(v), sz);
end
end
