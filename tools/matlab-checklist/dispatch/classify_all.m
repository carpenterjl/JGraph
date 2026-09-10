names = strtrim(string(splitlines(fileread('names.txt'))));
names = names(names ~= "");
fid = fopen('classify-all.csv', 'w');
fprintf(fid, 'name,kind,method_classes\n');
for k = 1:numel(names)
    n = char(names(k));
    try
        w = which(n, '-all');
    catch
        w = {};
    end
    if isempty(w)
        fprintf(fid, '%s,none,\n', n); continue;
    end
    first = w{1};
    if startsWith(first, 'built-in')
        kind = 'builtin';
    elseif endsWith(first, '.m') || endsWith(first, '.p') || endsWith(first, '.mlx')
        kind = 'mfile';
    else
        kind = 'other';
    end
    cls = {};
    for j = 1:numel(w)
        t = regexp(w{j}, '@([A-Za-z_][A-Za-z0-9_.]*)', 'tokens', 'once');
        if ~isempty(t)
            cls{end+1} = t{1}; %#ok<AGROW>
        end
    end
    cls = unique(cls);
    fprintf(fid, '%s,%s,%s\n', n, kind, strjoin(cls, ';'));
end
fclose(fid);
disp('classify-all done');
