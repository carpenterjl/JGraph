% DS0 (designer plan): App Designer's component code generator is MATLAB code
% (appdesigner.internal.codegeneration.ComponentCodeGenerator over the per-class component
% adapters). This runs it headless over every canvas component of the 44 shipped apps and compares
% what it makes with (a) the component's stored DesignTimeProperties.ComponentCode and (b) the
% component's lines in the file's createComponents, so the generator can serve as the oracle for
% the designer's writer. Only counts and the first few differences are printed.
files = dir(fullfile(matlabroot, 'toolbox', '**', '*.mlapp'));
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
tally = containers.Map();
shown = 0;
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        meta = des.getAppMetadata();
    catch e
        fprintf('%s: load error %s\n', files(k).name, e.identifier); continue
    end
    appType = ''; if isfield(meta, 'AppType'), appType = meta.AppType; end
    rel = ''; if isfield(meta, 'MATLABRelease'), rel = meta.MATLABRelease; end
    bump(tally, sprintf('saved in %s', rel));
    raw = strrep(char(matlab.internal.getCode(fn)), sprintf('\r\n'), newline);
    textLines = splitlines(raw);
    i0 = find(~cellfun(@isempty, regexp(textLines, '^\s*function createComponents\(app\)')), 1);
    if isempty(i0), continue; end
    indent = regexp(textLines{i0}, '^\s*', 'match', 'once');
    i1 = i0 + find(strcmp(textLines(i0+1:end), [indent 'end']), 1);
    textLines = strtrim(textLines(i0+1:i1-1));
    fig = a.components.UIFigure;
    comps = findall(fig, '-property', 'DesignTimeProperties');
    for c = 1:numel(comps)
        obj = comps(c);
        cls = class(obj);
        if ~isKey(adapters, cls), bump(tally, ['no adapter ' cls]); continue; end
        try
            adapter = eval(adapters(cls));
            code = gen.getComponentGenerationCode(obj, adapter, appType);
        catch e
            bump(tally, ['generator error ' e.identifier]); continue
        end
        name = obj.DesignTimeProperties.CodeName;
        parent = '';
        if isprop(obj.Parent, 'DesignTimeProperties'), parent = obj.Parent.DesignTimeProperties.CodeName; end
        code = strrep(strrep(code, 'ad_CODENAME_ad', name), 'ad_PARENTCODENAME_ad', parent);
        stored = obj.DesignTimeProperties.ComponentCode;
        stored = strrep(strrep(stored, 'ad_CODENAME_ad', name), 'ad_PARENTCODENAME_ad', parent);
        sameStored = isequal(strtrim(code(:)), strtrim(stored(:)));
        % the component's lines in the text: every line that starts app.<name> = or app.<name>.
        mine = textLines(startsWith(textLines, ['app.' name ' = ']) | startsWith(textLines, ['app.' name '.']));
        % the generator names a callback in quotes; the client writes it as createCallbackFcn
        genLines = regexprep(strtrim(code(:)), '^(app\.\w+\.\w+Fcn) = ''(\w+)'';$', '$1 = createCallbackFcn(app, @$2, true);');
        sameText = isequal(sort(genLines), sort(mine(:))) ;
        sameTextOrder = isequal(genLines, mine(:));
        key = sprintf('%s: stored=%d text=%d inOrder=%d', rel, sameStored, sameText, sameTextOrder);
        bump(tally, key);
        bump(tally, sprintf('all: stored=%d text=%d inOrder=%d', sameStored, sameText, sameTextOrder));
        if (~sameStored || ~sameTextOrder) && shown < 14
            shown = shown + 1;
            % only the property each line sets: MathWorks' values are not copied here
            what = @(c) strjoin(regexprep(c(:)', '^app\.\w+\.?([\w.]*) = .*$', '$1'), ',');
            fprintf('--- %s (%s)\n', files(k).name, cls);
            fprintf('  generated: %s\n', what(genLines));
            fprintf('  stored:    %s\n', what(strtrim(stored(:))));
            fprintf('  text:      %s\n', what(mine));
        end
    end
    delete(fig);
end
k = keys(tally); v = cell2mat(values(tally));
[~, o] = sort(k);
for i = o, fprintf('%5d  %s\n', v(i), k{i}); end

function bump(m, key)
    if isKey(m, key), m(key) = m(key) + 1; else, m(key) = 1; end
end
