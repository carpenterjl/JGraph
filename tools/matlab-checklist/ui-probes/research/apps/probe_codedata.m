% What App Designer's design-time copy of the code holds (headless; no windows).
src = fullfile(matlabroot,'toolbox','comm','comm','+comm','+internal','+bertool','DataExport.mlapp');
r = appdesigner.internal.serialization.FileReader(src);
[c, v] = r.readAppCodeData();
fprintf('version: %s\n', string(v));
disp(fieldnames(c));
f = fieldnames(c);
for k = 1:numel(f)
    x = c.(f{k});
    fprintf('== %s : %s %s\n', f{k}, class(x), mat2str(size(x)));
end
if isfield(c,'Callbacks') && ~isempty(c.Callbacks)
    disp(fieldnames(c.Callbacks));
    cb = c.Callbacks(1);
    g = fieldnames(cb);
    for k = 1:numel(g)
        y = cb.(g{k});
        fprintf('  -- %s : %s %s\n', g{k}, class(y), mat2str(size(y)));
        if ischar(y) || isstring(y) || iscell(y), disp(y); end
    end
end
f2 = setdiff(f, {'Callbacks'});
for k = 1:numel(f2)
    x = c.(f2{k});
    if isstruct(x) || isobject(x)
        try, disp(x); catch e, disp(e.message); end
    else
        disp(x);
    end
end
