function audit_nargout(namesFile, outFile)
% audit-nargout.m -- for every builtin name JGraph registers (the catalog's Add("name") entries,
% one per line in namesFile): R2025b's nargout(name) - a count, -1/-2 for a variable count, or
% ERR:<identifier> when MATLAB has no such function - and exist(name): 5 for a built-in, 2 for a
% file on the path, 3 for a MEX file, 6 for a P-file, 0 for neither. The output feeds
% tools/matlab-checklist/gen-builtin-outputs.py (V9, ADR 0170). Run as
%   matlab -batch "audit_nargout('names.txt', 'nargout-r2025b.tsv')"
% after copying this file to a folder named audit_nargout.m (its function name).
names = strtrim(string(splitlines(fileread(namesFile))));
names = names(strlength(names) > 0);
fid = fopen(outFile, 'w');
for k = 1:numel(names)
    name = char(names(k));
    try
        n = nargout(name);
        count = sprintf('%d', n);
    catch e
        count = ['ERR:' e.identifier];
    end
    try
        kind = sprintf('%d', exist(name)); %#ok<EXIST>
    catch
        kind = 'ERR';
    end
    fprintf(fid, '%s\t%s\t%s\n', name, count, kind);
end
fclose(fid);
fprintf('audited %d names\n', numel(names));
end
