function probe_k2_names(csvPath)
%PROBE_K2_NAMES Which products own each name JGraph answers, and which names mcc warns are
%non-deployable (compiler and SDK plan, stage K2). csvPath is the dispatch table
%(tools/matlab-checklist/matlab-r2025b-dispatch.csv), whose first column is JGraph's names.
%Prints
%   name<TAB>products<TAB>its file under matlabroot (built-in: no extension), '<elsewhere>' or ''
% where products is matlab.codetools.requiredFilesAndProducts' ProductNumbers joined by commas,
% 1 without asking for a file under toolbox/matlab, or ERR identifier; then
%   product<TAB>number<TAB>name<TAB>version
% for every product met; then
%   excluded<TAB>name<TAB>file
% for every public file el.win64.txt or mcr.rdl leaves out of the runtime that which() resolves
% to (so not a built-in whose .m is only its help), the files mcc warns about when code names them.
beep('off');
rows = splitlines(string(fileread(csvPath)));
rows = rows(2:end);
names = cellstr(unique(extractBefore(rows(contains(rows, ',')), ',')));
root = strrep(matlabroot, '\', '/');
products = containers.Map('KeyType', 'double', 'ValueType', 'any');
for k = 1:numel(names)
    n = names{k};
    if ~isvarname(strrep(n, '.', '_')), continue; end
    file = relfile(which(n), root);
    if isempty(file)
        numbers = '';
        if ~isempty(which(n)), file = '<elsewhere>'; end
    elseif startsWith(file, 'toolbox/matlab/')
        numbers = '1';
    else
        try
            [~, p] = matlab.codetools.requiredFilesAndProducts(n);
            numbers = strjoin(arrayfun(@(e) sprintf('%d', e.ProductNumber), p, 'UniformOutput', false), ',');
            for j = 1:numel(p), products(p(j).ProductNumber) = p(j); end
        catch ME
            numbers = ['ERR ' ME.identifier];
        end
    end
    fprintf('%s\t%s\t%s\n', n, numbers, file);
end
ks = cell2mat(keys(products));
for k = ks
    p = products(k);
    fprintf('product\t%d\t%s\t%s\n', p.ProductNumber, p.Name, p.Version);
end

% The non-deployable files.
el = splitlines(string(fileread(fullfile(matlabroot, 'toolbox', 'matlab', 'depfun', '+matlab', ...
    '+depfun', '+internal', 'private', 'el.win64.txt'))));
rdl = string(regexp(fileread(fullfile(matlabroot, 'toolbox', 'matlab', 'depfun', '+matlab', ...
    '+depfun', '+internal', 'mcr.rdl')), '\$MATLABROOT/(toolbox/\S+\.m)', 'tokens'));
el = [el(2:end); rdl(:)];
el = el(~cellfun(@isempty, regexp(el, '^toolbox/[^+@]+/[a-zA-Z]\w*\.(m|p|mexw64)$', 'once')) ...
    & ~contains(el, ["/private/", "/ja/", "/zh_CN/", "/ko_KR/", "/demos/", "/examples/"]));
el = unique(el);
for k = 1:numel(el)
    [~, n] = fileparts(el(k));
    if strcmp(n, 'Contents'), continue; end
    file = relfile(which(char(n)), root);
    if strcmp(file, el(k))
        fprintf('excluded\t%s\t%s\n', n, file);
    end
end
end

function file = relfile(w, root)
tok = regexp(w, '^built-in \((.*)\)$', 'tokens', 'once');
if ~isempty(tok), w = tok{1}; end
w = strrep(w, '\', '/');
file = '';
if startsWith(w, [root '/']), file = w(numel(root) + 2:end); end
end
