function s = k10info(x)
%K10INFO Say what the argument arrived as: its class, size, complexity, sparsity and values, for
%cells and structs element by element.
s = describe(x);
end

function s = describe(x)
s = sprintf('%s %s', class(x), mat2str(size(x)));
if isnumeric(x) && ~isreal(x)
    s = [s ' complex'];
end
if issparse(x)
    s = [s ' sparse'];
end
if isnumeric(x) || islogical(x)
    v = reshape(full(x), 1, []);
    if isempty(v)
        s = [s ' []'];
    elseif isa(v, 'double') || isa(v, 'single')
        s = [s ' ' mat2str(v, 17)];
    else
        s = [s ' ' mat2str(v)];
    end
elseif ischar(x)
    s = [s ' ' mat2str(double(reshape(x, 1, [])))];
elseif iscell(x)
    parts = cellfun(@describe, reshape(x, 1, []), 'UniformOutput', false);
    s = [s ' {' strjoin(parts, '; ') '}'];
elseif isstruct(x)
    names = fieldnames(x)';
    s = [s ' <' strjoin(names, ',') '>'];
    parts = {};
    for k = 1:numel(x)
        for n = 1:numel(names)
            parts{end + 1} = [names{n} '=' describe(x(k).(names{n}))]; %#ok<AGROW>
        end
    end
    s = [s ' {' strjoin(parts, '; ') '}'];
elseif isstring(x)
    s = [s ' "' strjoin(cellstr(reshape(x, 1, [])), '","') '"'];
elseif isa(x, 'function_handle')
    s = [s ' ' func2str(x)];
end
end
