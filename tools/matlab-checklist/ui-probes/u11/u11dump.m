function u11dump(file)
% U11DUMP Prints the hgS struct tree a MATLAB .fig holds: per node its type, the stored
% properties (short values), what 'special' holds, and the children, depth first.
w = whos('-file', file);
fprintf('vars:'); fprintf(' %s', w.name); fprintf('\n');
names = {w.name};
k = find(startsWith(names, 'hgS_'), 1);
s = load(file, '-mat', names{k});
r = s.(names{k});
fprintf('root: class %s size %s fields {%s}\n', class(r), mat2str(size(r)), strjoin(fieldnames(r)', ','));
for i = 1:numel(r)
    node(r(i), '');
end
end

function node(n, ind)
if ~isfield(n, 'type')
    fprintf('%s<node without type: %s>\n', ind, short(n));
    return
end
fprintf('%s[%s] handle=%.10g fields={%s}\n', ind, n.type, n.handle, strjoin(fieldnames(n)', ','));
p = n.properties;
fn = fieldnames(p);
for i = 1:numel(fn)
    fprintf('%s  .%s = %s\n', ind, fn{i}, short(p.(fn{i})));
end
if ~isempty(n.special)
    fprintf('%s  special: %s\n', ind, short(n.special));
end
for i = 1:numel(n.children)
    node(n.children(i), [ind '    ']);
end
end

function t = short(v)
if ischar(v)
    if size(v, 1) > 1
        t = sprintf('char %s', mat2str(size(v)));
    else
        t = ['''' v(1:min(end, 70)) ''''];
    end
elseif isa(v, 'function_handle')
    t = ['@fh ' func2str(v)];
elseif isnumeric(v) || islogical(v)
    if numel(v) <= 8
        t = sprintf('%s %s', class(v), mat2str(v, 6));
    else
        t = sprintf('%s %s', class(v), mat2str(size(v)));
    end
elseif iscell(v)
    if iscellstr(v) && numel(v) <= 6
        t = ['{' strjoin(cellfun(@(x) ['''' x ''''], v(:)', 'UniformOutput', false), ',') '}'];
    else
        t = sprintf('cell %s', mat2str(size(v)));
    end
elseif isstruct(v)
    f = fieldnames(v);
    t = sprintf('struct %s {%s}', mat2str(size(v)), strjoin(f', ','));
    if isscalar(v)
        for i = 1:numel(f)
            if isstruct(v.(f{i})) || ~isempty(v.(f{i}))
                t = [t sprintf(' %s=%s', f{i}, short(v.(f{i})))]; %#ok<AGROW>
            end
        end
    end
else
    t = sprintf('<%s %s>', class(v), mat2str(size(v)));
end
end
