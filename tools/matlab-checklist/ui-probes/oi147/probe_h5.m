function probe_h5(f)
% Dumps the structure of a v7.3 MAT file's subsystem and refs, one line per object.
fid = H5F.open(f);
walk(fid, '/', 0);
H5F.close(fid);
end

function walk(fid, path, depth)
info = h5info(fid_name(fid), path);
fprintf('%sG %s attrs=%s\n', repmat(' ', 1, depth), path, attrs(info.Attributes));
for d = 1:numel(info.Datasets)
    ds = info.Datasets(d);
    full = [path, iff(path(end) == '/', '', '/'), ds.Name];
    fprintf('%sD %s type=%s size=%s attrs=%s\n', repmat(' ', 1, depth + 1), full, typ(ds.Datatype), mat2str(ds.Dataspace.Size), attrs(ds.Attributes));
end
for g = 1:numel(info.Groups)
    if depth < 3
        walk(fid, info.Groups(g).Name, depth + 1);
    end
end
end

function n = fid_name(fid)
n = H5F.get_name(fid);
end

function s = typ(t)
if isfield(t, 'Class'), s = t.Class; else, s = '?'; end
if isfield(t, 'Type') && ischar(t.Type), s = [s ':' t.Type]; end
end

function s = attrs(a)
s = '';
for i = 1:numel(a)
    v = a(i).Value;
    if ischar(v), vs = v; elseif iscell(v), vs = strjoin(cellfun(@char, v, 'UniformOutput', false), ','); else, vs = mat2str(double(v)); end
    if numel(vs) > 60, vs = [vs(1:60) '...']; end
    s = [s a(i).Name '=' vs ';']; %#ok<AGROW>
end
end

function r = iff(c, a, b)
if c, r = a; else, r = b; end
end
