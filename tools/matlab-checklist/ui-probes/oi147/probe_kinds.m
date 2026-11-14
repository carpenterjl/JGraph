function probe_kinds(f)
% Lists each distinct (kind, MATLAB_class, datatype, attribute names) combination in a v7.3 file.
info = h5info(f);
m = containers.Map();
m = walk(info, m);
k = sort(keys(m));
for i = 1:numel(k)
    fprintf('%6d  %s\n', m(k{i}), k{i});
end
end

function m = walk(g, m)
m = add(m, ['G ' cls(g.Attributes) ' ' names(g.Attributes)]);
for d = 1:numel(g.Datasets)
    ds = g.Datasets(d);
    t = ds.Datatype;
    ty = t.Class;
    if ischar(t.Type), ty = [ty ':' t.Type]; end
    m = add(m, ['D ' cls(ds.Attributes) ' ' ty ' ' names(ds.Attributes)]);
end
for c = 1:numel(g.Groups)
    m = walk(g.Groups(c), m);
end
end

function m = add(m, key)
key = char(key);
if isKey(m, key), m(key) = m(key) + 1; else, m(key) = 1; end
end

function s = cls(a)
s = '-';
if isempty(a), return; end
for i = 1:numel(a)
    if strcmp(a(i).Name, 'MATLAB_class'), s = char(a(i).Value); end
end
if contains(s, '.'), s = '<object>'; end
end

function s = names(a)
s = '';
if isempty(a), return; end
n = sort({a.Name});
n = n(~strcmp(n, 'H5PATH') & ~strcmp(n, 'MATLAB_class'));
s = strjoin(n, ',');
end
